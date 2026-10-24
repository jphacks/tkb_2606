using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.MapUI
{
    public sealed class MapCanvas : VisualElement
    {
        private readonly struct Cell
        {
            public readonly int X, Y;
            public readonly Color32 Color;
            public Cell(int x, int y, Color32 color) { X = x; Y = y; Color = color; }
        }
        private readonly struct Quad
        {
            public readonly Rect Rect;
            public readonly Color32 Color;
            public Quad(Rect rect, Color32 color) { Rect = rect; Color = color; }
        }
        private readonly MapViewport viewport = new MapViewport();
        private readonly VisualElement labels = new VisualElement { pickingMode = PickingMode.Ignore };
        private readonly List<Label> labelPool = new List<Label>();
        private readonly Dictionary<Emotion, Color32> flowerColors = new Dictionary<Emotion, Color32>
        {
            { Emotion.Joy, new Color32(245, 207, 91, 255) }, { Emotion.Anger, new Color32(237, 109, 119, 255) },
            { Emotion.Sadness, new Color32(111, 168, 233, 255) }, { Emotion.Delight, new Color32(246, 164, 87, 255) }
        };
        private MapSnapshot snapshot;
        private Dictionary<string, MapPoint> positions = new Dictionary<string, MapPoint>();
        private readonly Dictionary<long, Cell> land = new Dictionary<long, Cell>();
        private readonly Dictionary<string, MapNode> nodes = new Dictionary<string, MapNode>();
        private string selected, lastClick;
        private float lastClickAt;
        private Vector2 pointerStart, previousPointer;
        private bool dragging, moved, pendingFit;
        public event Action<MapNode> NodeSelected;
        public event Action<MapNode> NodeDoubleClicked;
        public event Action<string> MarkSelected;

        public MapCanvas()
        {
            name = "knowledge-map"; style.flexGrow = 1; style.minHeight = 200;
            style.overflow = Overflow.Hidden; style.position = Position.Relative;
            labels.style.position = Position.Absolute; labels.style.left = labels.style.top = 0;
            labels.style.right = labels.style.bottom = 0; Add(labels);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ =>
            { if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); } else UpdateLabels(); });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pointerStart = previousPointer = evt.localPosition; dragging = true; moved = false;
                this.CapturePointer(evt.pointerId); evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                Vector2 point = evt.localPosition;
                if ((point - pointerStart).sqrMagnitude > 25) moved = true;
                if (moved) { viewport.Pan(point.x - previousPointer.x, point.y - previousPointer.y); Repaint(); }
                previousPointer = point;
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging || evt.button != 0) return;
                dragging = false; this.ReleasePointer(evt.pointerId);
                if (!moved) SelectAt(evt.localPosition);
                evt.StopPropagation();
            });
            RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
            RegisterCallback<WheelEvent>(evt =>
            {
                viewport.Zoom(Mathf.Exp(-evt.delta.y * 0.08f), new MapPoint(evt.localMousePosition.x, evt.localMousePosition.y));
                Repaint(); evt.StopPropagation();
            });
        }

        public void SetSnapshot(MapSnapshot value)
        {
            bool firstData = snapshot == null || snapshot.Nodes.Count == 0;
            snapshot = value; lastClick = null;
            if (value == null || !value.Nodes.Any(n => n.Id == selected)) selected = null;
            nodes.Clear(); land.Clear();
            positions = value == null ? new Dictionary<string, MapPoint>() : MapLayout.Solve(value);
            if (value != null)
            {
                foreach (var node in value.Nodes)
                {
                    nodes.Add(node.Id, node);
                    var point = positions[node.Id]; int radius = MapLayout.LandRadius(node.TypedChars);
                    int cx = (int)(point.X / MapLayout.Tile), cy = (int)(point.Y / MapLayout.Tile);
                    int tone = MapLayout.StableHash(node.Town) % 22;
                    for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                        if (x * x + y * y <= radius * radius + radius)
                            PutLand(cx + x, cy + y, new Color32((byte)(55 + tone), (byte)(91 + tone), 91, 255));
                }
                foreach (var edge in value.Roads) Carve(positions[edge.From], positions[edge.To]);
            }
            pendingFit = firstData;
            if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); }
            Repaint();
        }
        private void PutLand(int x, int y, Color32 color) => land[((long)x << 32) | (uint)y] = new Cell(x, y, color);
        private void Carve(MapPoint a, MapPoint b)
        {
            int x = (int)(a.X / MapLayout.Tile), y = (int)(a.Y / MapLayout.Tile);
            int ex = (int)(b.X / MapLayout.Tile), ey = (int)(b.Y / MapLayout.Tile);
            int dx = Math.Abs(ex - x), dy = Math.Abs(ey - y), sx = x < ex ? 1 : -1, sy = y < ey ? 1 : -1, error = dx - dy;
            for (int step = 0; step <= dx + dy; step++)
            {
                long key = ((long)x << 32) | (uint)y;
                if (!land.ContainsKey(key)) PutLand(x, y, new Color32(42, 63, 74, 255));
                if (x == ex && y == ey) break;
                int twice = 2 * error;
                if (twice > -dy) { error -= dy; x += sx; }
                if (twice < dx) { error += dx; y += sy; }
            }
        }
        public void Fit() { viewport.Fit(positions.Values, contentRect.width, contentRect.height); Repaint(); }
        public void Zoom(float factor) { viewport.Zoom(factor, new MapPoint(contentRect.width / 2, contentRect.height / 2)); Repaint(); }
        public void FocusSelection()
        {
            if (selected == null || !positions.TryGetValue(selected, out var p)) { Fit(); return; }
            var at = viewport.Screen(p); viewport.Pan(contentRect.width / 2 - at.X, contentRect.height / 2 - at.Y); Repaint();
        }
        public void SetFlowerColor(Emotion emotion, Color32 color) { flowerColors[emotion] = color; Repaint(); }
        private void Repaint() { MarkDirtyRepaint(); UpdateLabels(); }
        private void SelectAt(Vector2 at)
        {
            if (snapshot == null) return;
            foreach (var mark in snapshot.Marks)
            {
                var p = viewport.Screen(positions[mark.Node]);
                if ((new Vector2(p.X + 13, p.Y - 17) - at).sqrMagnitude < 100)
                { selected = mark.Node; lastClick = null; MarkSelected?.Invoke(mark.Message); Repaint(); return; }
            }
            string closest = null; float distance = 24 * 24;
            foreach (var pair in positions)
            {
                var p = viewport.Screen(pair.Value); float squared = (new Vector2(p.X, p.Y) - at).sqrMagnitude;
                if (squared < distance) { distance = squared; closest = pair.Key; }
            }
            if (closest == null) { selected = null; lastClick = null; Repaint(); return; }
            selected = closest; NodeSelected?.Invoke(nodes[closest]);
            if (lastClick == closest && Time.realtimeSinceStartup - lastClickAt < 0.35f)
            { lastClick = null; NodeDoubleClicked?.Invoke(nodes[closest]); }
            else { lastClick = closest; lastClickAt = Time.realtimeSinceStartup; }
            Repaint();
        }
        private void UpdateLabels()
        {
            int count = 0;
            if (snapshot == null)
            { foreach (var label in labelPool) label.style.display = DisplayStyle.None; return; }
            foreach (var town in snapshot.Nodes.GroupBy(n => n.Town).Take(16))
            {
                var points = town.Select(n => positions[n.Id]).ToArray();
                var p = viewport.Screen(new MapPoint(points.Average(point => point.X), points.Min(point => point.Y) - 65));
                if (p.X < -80 || p.Y < -30 || p.X > contentRect.width || p.Y > contentRect.height) continue;
                string name = town.Key.Length == 0 ? "ノート" : town.Key;
                SetLabel(count++, name, p.X, p.Y, true);
            }
            foreach (var node in snapshot.Nodes.OrderByDescending(n => n.Id == selected))
            {
                if (viewport.Scale < 0.3f && node.Id != selected) continue;
                var p = viewport.Screen(positions[node.Id]);
                if (p.X < -40 || p.Y < -30 || p.X > contentRect.width + 40 || p.Y > contentRect.height + 30) continue;
                if (count >= 106) break;
                SetLabel(count++, node.Title.Length > 22 ? node.Title.Substring(0, 21) + "…" : node.Title, p.X + 9, p.Y + 9, false);
            }
            for (int i = count; i < labelPool.Count; i++) labelPool[i].style.display = DisplayStyle.None;
        }
        private void SetLabel(int index, string text, float x, float y, bool town)
        {
            while (labelPool.Count <= index)
            {
                var created = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                created.style.position = Position.Absolute; created.style.fontSize = 11;
                created.style.backgroundColor = (Color)new Color32(12, 22, 36, 210);
                created.style.paddingLeft = created.style.paddingRight = 3;
                labels.Add(created); labelPool.Add(created);
            }
            var label = labelPool[index]; label.text = text; label.style.display = DisplayStyle.Flex;
            label.style.left = x; label.style.top = y;
            label.style.color = (Color)(town ? new Color32(183, 195, 152, 255) : new Color32(223, 231, 232, 255));
        }
        private void AddQuad(List<Quad> quads, float x, float y, float width, float height, Color32 color)
        {
            var rect = new Rect(x, y, width, height);
            if (width > 0.2f && height > 0.2f && rect.Overlaps(contentRect)) quads.Add(new Quad(rect, color));
        }
        private void WorldQuad(List<Quad> quads, float x, float y, float size, Color32 color)
        {
            var p = viewport.Screen(new MapPoint(x, y));
            AddQuad(quads, p.X - size * viewport.Scale / 2, p.Y - size * viewport.Scale / 2,
                size * viewport.Scale, size * viewport.Scale, color);
        }
        private void Draw(MeshGenerationContext context)
        {
            var quads = new List<Quad>();
            // Brick darkness stays behind carved land. No animation/continuous repaint loop.
            for (int y = 0; y < contentRect.height; y += 20)
            for (int x = -10; x < contentRect.width; x += 24)
                AddQuad(quads, x + (y / 20 % 2) * 12, y, 23, 19, new Color32(13, 24, (byte)(36 + (x + y + 5000) % 5), 255));
            foreach (var cell in land.Values) WorldQuad(quads, cell.X * MapLayout.Tile, cell.Y * MapLayout.Tile, MapLayout.Tile - 0.6f, cell.Color);
            Flush(context, quads); quads.Clear();
            if (snapshot == null) return;
            var painter = context.painter2D;
            foreach (var edge in snapshot.Roads)
            {
                var a = viewport.Screen(positions[edge.From]); var b = viewport.Screen(positions[edge.To]);
                Vector2 from = new Vector2(a.X, a.Y), to = new Vector2(b.X, b.Y);
                if (!new Rect(Math.Min(a.X, b.X) - 10, Math.Min(a.Y, b.Y) - 10, Math.Abs(a.X - b.X) + 20,
                    Math.Abs(a.Y - b.Y) + 20).Overlaps(contentRect)) continue;
                var direction = (to - from).normalized; var normal = new Vector2(-direction.y, direction.x);
                painter.lineWidth = edge.Auto ? 1 : edge.Kind == RoadKind.Example ? 1.2f : 2;
                painter.strokeColor = edge.Auto ? new Color32(110, 143, 140, 180) : new Color32(177, 161, 118, 235);
                painter.BeginPath();
                if (edge.Kind == RoadKind.Example)
                    for (int i = 0; i < 8; i++) { painter.MoveTo(Vector2.Lerp(from, to, i / 8f)); painter.LineTo(Vector2.Lerp(from, to, (i + 0.55f) / 8f)); }
                else if (edge.Kind == RoadKind.Contrast)
                    for (int i = -1; i <= 1; i += 2) { painter.MoveTo(from + normal * i * 2); painter.LineTo(to + normal * i * 2); }
                else { painter.MoveTo(from); painter.LineTo(to); }
                if (edge.Kind == RoadKind.Premise)
                {
                    var tip = to - direction * 9;
                    painter.MoveTo(tip - direction * 8 + normal * 4); painter.LineTo(tip); painter.LineTo(tip - direction * 8 - normal * 4);
                }
                painter.Stroke();
            }
            foreach (var fog in snapshot.Fogs)
            {
                var p = positions[fog.Node];
                int radius = MapLayout.LandRadius(nodes[fog.Node].TypedChars);
                for (int x = -Math.Min(1, radius); x <= Math.Min(1, radius); x++)
                    WorldQuad(quads, p.X + x * MapLayout.Tile, p.Y, MapLayout.Tile, new Color32(181, 193, 208, 125));
            }
            foreach (var node in snapshot.Nodes)
            {
                var p = viewport.Screen(positions[node.Id]);
                if (node.Id == selected) AddQuad(quads, p.X - 9, p.Y - 9, 18, 18, new Color32(86, 210, 218, 255));
                AddQuad(quads, p.X - 5, p.Y - 5, 10, 10, new Color32(210, 230, 188, 255));
            }
            foreach (var flower in snapshot.Flowers)
            {
                if (nodes[flower.Node].TypedChars == 0) continue;
                var p = viewport.Screen(positions[flower.Node]);
                int hash = MapLayout.StableHash(flower.Node + ":" + flower.Line);
                float x = p.X + (hash % 5 - 2) * 7, y = p.Y + (hash / 5 % 5 - 2) * 7;
                var color = flower.Withered ? new Color32(131, 129, 126, 255) : flowerColors[flower.Emotion];
                AddQuad(quads, x, y + 2, 2, 7, new Color32(112, 151, 119, 255));
                int shape = (int)flower.Emotion;
                if (shape == 0) { AddQuad(quads, x - 3, y - 1, 8, 3, color); AddQuad(quads, x, y - 4, 2, 9, color); }
                else if (shape == 1) { AddQuad(quads, x - 3, y - 3, 7, 7, color); AddQuad(quads, x - 1, y - 5, 3, 11, color); }
                else if (shape == 2) { AddQuad(quads, x, y - 5, 2, 9, color); AddQuad(quads, x - 2, y - 1, 6, 4, color); }
                else { AddQuad(quads, x - 4, y - 4, 3, 3, color); AddQuad(quads, x + 2, y - 4, 3, 3, color); AddQuad(quads, x - 4, y + 2, 9, 3, color); }
            }
            foreach (var mark in snapshot.Marks)
            {
                var p = viewport.Screen(positions[mark.Node]);
                var gold = new Color32(255, 208, 106, 255);
                AddQuad(quads, p.X + 11, p.Y - 23, 4, 9, gold); AddQuad(quads, p.X + 11, p.Y - 11, 4, 3, gold);
            }
            Flush(context, quads);
        }
        private static void Flush(MeshGenerationContext context, List<Quad> quads)
        {
            // Each mesh stays below the UInt16 vertex-index limit.
            const int batch = 12000;
            for (int offset = 0; offset < quads.Count; offset += batch)
            {
                int count = Math.Min(batch, quads.Count - offset); var mesh = context.Allocate(count * 4, count * 6);
                for (int i = 0; i < count; i++)
                {
                    var q = quads[offset + i];
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMin, q.Rect.yMin, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMax, q.Rect.yMin, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMax, q.Rect.yMax, Vertex.nearZ), tint = q.Color });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.Rect.xMin, q.Rect.yMax, Vertex.nearZ), tint = q.Color });
                    ushort start = (ushort)(i * 4);
                    mesh.SetNextIndex(start); mesh.SetNextIndex((ushort)(start + 1)); mesh.SetNextIndex((ushort)(start + 2));
                    mesh.SetNextIndex((ushort)(start + 2)); mesh.SetNextIndex((ushort)(start + 3)); mesh.SetNextIndex(start);
                }
            }
        }
    }
}
