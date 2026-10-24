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
        private readonly struct Quad
        {
            public readonly Rect Rect;
            public readonly Color32 Color;
            public Quad(Rect rect, Color32 color) { Rect = rect; Color = color; }
        }
        // 目印の中心の、拠点からの世界座標でのずれ(旗の右上)。
        private static readonly Vector2 MarkOffset = new Vector2(14, -18);
        private readonly MapViewport viewport = new MapViewport();
        private readonly MapTouchGesture touchGesture = new MapTouchGesture();
        private readonly VisualElement labels = new VisualElement { pickingMode = PickingMode.Ignore };
        private readonly List<Label> labelPool = new List<Label>();
        private readonly Dictionary<Emotion, Color32> flowerColors = new Dictionary<Emotion, Color32>
        {
            { Emotion.Joy, new Color32(245, 207, 91, 255) }, { Emotion.Anger, new Color32(237, 109, 119, 255) },
            { Emotion.Sadness, new Color32(111, 168, 233, 255) }, { Emotion.Delight, new Color32(246, 164, 87, 255) }
        };
        private MapSnapshot snapshot;
        private Dictionary<string, MapPoint> positions = new Dictionary<string, MapPoint>();
        private readonly HashSet<long> land = new HashSet<long>();
        private int frame;
        // 道のマス: 道が伸びる向きの組み合わせ(上=1 右=2 下=4 左=8)と、種別の重ね絵(向きは 0上 1右 2下 3左)。
        private readonly Dictionary<long, int> pathMask = new Dictionary<long, int>();
        private readonly Dictionary<long, KeyValuePair<RoadKind, int>> pathKind = new Dictionary<long, KeyValuePair<RoadKind, int>>();
        // 壁から土地に変わったマスと、崩れ始めたコマ番号。
        private readonly Dictionary<long, int> collapsing = new Dictionary<long, int>();
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
            // 花と旗のアニメ。拡大率が小さい(描く数が多い)時は止めて、再描画を増やさない。
            schedule.Execute(() =>
            {
                frame++;
                int length = MapArt.Instance.Collapse.Length;
                if (collapsing.Count > 0)
                    foreach (long key in collapsing.Where(p => frame - p.Value >= length).Select(p => p.Key).ToList()) collapsing.Remove(key);
                if ((viewport.Scale >= 0.3f && snapshot != null && snapshot.Nodes.Count > 0) || collapsing.Count > 0) MarkDirtyRepaint();
            }).Every(140);
            RegisterCallback<GeometryChangedEvent>(_ =>
            { if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); } else UpdateLabels(); });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.pointerType == UnityEngine.UIElements.PointerType.touch)
                {
                    dragging = false;
                    if (touchGesture.Count > 0) lastClick = null;
                    touchGesture.Down(evt.pointerId, new MapPoint(evt.localPosition.x, evt.localPosition.y));
                    this.CapturePointer(evt.pointerId); evt.StopPropagation(); return;
                }
                if (evt.button != 0) return;
                pointerStart = previousPointer = evt.localPosition; dragging = true; moved = false;
                this.CapturePointer(evt.pointerId); evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (touchGesture.Move(evt.pointerId, new MapPoint(evt.localPosition.x, evt.localPosition.y), viewport))
                { Repaint(); evt.StopPropagation(); return; }
                if (!dragging) return;
                Vector2 point = evt.localPosition;
                if ((point - pointerStart).sqrMagnitude > 25) moved = true;
                if (moved) { viewport.Pan(point.x - previousPointer.x, point.y - previousPointer.y); Repaint(); }
                previousPointer = point;
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (touchGesture.Contains(evt.pointerId))
                {
                    bool tap = touchGesture.Up(evt.pointerId);
                    this.ReleasePointer(evt.pointerId);
                    if (tap) SelectAt(evt.localPosition);
                    evt.StopPropagation(); return;
                }
                if (!dragging || evt.button != 0) return;
                dragging = false; this.ReleasePointer(evt.pointerId);
                if (!moved) SelectAt(evt.localPosition);
                evt.StopPropagation();
            });
            RegisterCallback<PointerCaptureOutEvent>(evt => { touchGesture.Up(evt.pointerId, true); dragging = false; });
            RegisterCallback<PointerCancelEvent>(evt =>
            { touchGesture.Up(evt.pointerId, true); this.ReleasePointer(evt.pointerId); dragging = false; });
            RegisterCallback<DetachFromPanelEvent>(_ => { touchGesture.Clear(); dragging = false; });
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
            var before = new HashSet<long>(land);
            nodes.Clear(); land.Clear(); pathMask.Clear(); pathKind.Clear();
            positions = value == null ? new Dictionary<string, MapPoint>() : MapLayout.Solve(value, positions);
            if (value != null)
            {
                foreach (var node in value.Nodes)
                {
                    nodes.Add(node.Id, node);
                    var point = positions[node.Id]; int radius = MapLayout.LandRadius(node.TypedChars);
                    int cx = (int)(point.X / MapLayout.Tile), cy = (int)(point.Y / MapLayout.Tile);
                    for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                        if (x * x + y * y <= radius * radius + radius)
                            PutLand(cx + x, cy + y);
                }
                foreach (var edge in value.Roads) Carve(edge, positions[edge.From], positions[edge.To]);
                if (before.Count > 0)
                    foreach (long key in land) if (!before.Contains(key) && !collapsing.ContainsKey(key)) collapsing[key] = frame;
            }
            foreach (long key in collapsing.Keys.Where(k => !land.Contains(k)).ToList()) collapsing.Remove(key);
            pendingFit = firstData;
            if (pendingFit && contentRect.width > 1) { pendingFit = false; Fit(); }
            Repaint();
        }
        private static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        private void PutLand(int x, int y) => land.Add(Key(x, y));
        // 2つの拠点のマスを上下左右だけでたどり、土地を開いて、道のマスの向きを記録する。
        private void Carve(MapRoad edge, MapPoint a, MapPoint b)
        {
            int x = (int)(a.X / MapLayout.Tile), y = (int)(a.Y / MapLayout.Tile);
            int ex = (int)(b.X / MapLayout.Tile), ey = (int)(b.Y / MapLayout.Tile);
            int dx = Math.Abs(ex - x), dy = Math.Abs(ey - y), sx = x < ex ? 1 : -1, sy = y < ey ? 1 : -1, error = dx - dy;
            var cells = new List<Vector2Int> { new Vector2Int(x, y) };
            for (int guard = 0; (x != ex || y != ey) && guard <= dx + dy + 2; guard++)
            {
                int twice = 2 * error;
                if (twice > -dy && twice < dx) { error -= dy; x += sx; cells.Add(new Vector2Int(x, y)); error += dx; y += sy; }
                else if (twice > -dy) { error -= dy; x += sx; }
                else { error += dx; y += sy; }
                cells.Add(new Vector2Int(x, y));
            }
            for (int i = 0; i < cells.Count; i++)
            {
                PutLand(cells[i].x, cells[i].y);
                if (i + 1 >= cells.Count) break;
                var step = cells[i + 1] - cells[i];
                int dir = step.y < 0 ? 0 : step.x > 0 ? 1 : step.y > 0 ? 2 : 3;
                long from = Key(cells[i].x, cells[i].y), to = Key(cells[i + 1].x, cells[i + 1].y);
                pathMask[from] = (pathMask.TryGetValue(from, out int m1) ? m1 : 0) | (1 << dir);
                pathMask[to] = (pathMask.TryGetValue(to, out int m2) ? m2 : 0) | (1 << ((dir + 2) % 4));
                // 重ね絵は道の途中のマスだけ(両端の拠点のマスには載せない)。
                if (i > 0 && !edge.Auto && edge.Kind != RoadKind.Unspecified) pathKind[from] = new KeyValuePair<RoadKind, int>(edge.Kind, dir);
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
                var p = viewport.Screen(positions[mark.Node]); float s = viewport.Scale, reach = Mathf.Max(10, 12 * s);
                if ((new Vector2(p.X + MarkOffset.x * s, p.Y + MarkOffset.y * s) - at).sqrMagnitude < reach * reach)
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
            var art = MapArt.Instance; var sprites = new List<SpriteDraw>();
            var overlays = new List<SpriteDraw>();
            if (art.HasTiles) AddTerrain(sprites, overlays, quads, art);
            else
            {
                // 素材が読めない時の代用(色つきの四角)。
                AddQuad(quads, 0, 0, contentRect.width, contentRect.height, new Color32(13, 24, 38, 255));
                foreach (long key in land)
                    WorldQuad(quads, (int)(key >> 32) * MapLayout.Tile, (int)(uint)key * MapLayout.Tile, MapLayout.Tile - 0.6f, new Color32(60, 96, 91, 255));
            }
            Flush(context, quads); quads.Clear(); FlushSprites(context, sprites); sprites.Clear();
            FlushSprites(context, overlays); overlays.Clear();
            if (snapshot == null) return;
            var painter = context.painter2D;
            // 道の絵が読めない時だけ、線で代用する。
            if (!(art.HasTiles && art.HasPaths))
            foreach (var edge in snapshot.Roads)
            {
                var a = viewport.Screen(positions[edge.From]); var b = viewport.Screen(positions[edge.To]);
                Vector2 from = new Vector2(a.X, a.Y), to = new Vector2(b.X, b.Y);
                if (!new Rect(Math.Min(a.X, b.X) - 10, Math.Min(a.Y, b.Y) - 10, Math.Abs(a.X - b.X) + 20,
                    Math.Abs(a.Y - b.Y) + 20).Overlaps(contentRect)) continue;
                var direction = (to - from).normalized; var normal = new Vector2(-direction.y, direction.x);
                painter.lineWidth = edge.Auto ? 1.5f : edge.Kind == RoadKind.Example ? 2 : 3;
                painter.strokeColor = edge.Auto ? new Color32(131, 172, 166, 220) : new Color32(224, 197, 124, 255);
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
                var fogFrames = art.FogAnim.Length > 0 ? art.FogAnim : art.FogTile;
                for (int x = -Math.Min(1, radius); x <= Math.Min(1, radius); x++)
                {
                    if (fogFrames.Length == 0)
                    { WorldQuad(quads, p.X + x * MapLayout.Tile, p.Y, MapLayout.Tile, new Color32(181, 193, 208, 125)); continue; }
                    int cx = (int)(p.X / MapLayout.Tile) + x, cy = (int)(p.Y / MapLayout.Tile);
                    AddTile(sprites, cx, cy, fogFrames[(frame + MapLayout.StableHash(fog.Node + ":" + fog.Line)) % fogFrames.Length]);
                }
            }
            FlushSprites(context, sprites); sprites.Clear();
            foreach (var node in snapshot.Nodes)
                if (node.Id == selected)
                { var p = viewport.Screen(positions[node.Id]); AddQuad(quads, p.X - 14, p.Y - 14, 28, 28, new Color32(86, 210, 218, 255)); }
            Flush(context, quads); quads.Clear();
            AddFlowers(sprites, art); FlushSprites(context, sprites); sprites.Clear();
            AddFlags(sprites, quads, art); FlushSprites(context, sprites); sprites.Clear();
            foreach (var mark in snapshot.Marks)
            {
                var at = positions[mark.Node]; var markSprite = art.Marks[(int)mark.Kind];
                if (markSprite != null) { AddAnchored(sprites, at.X + MarkOffset.x, at.Y + MarkOffset.y + MapLayout.Tile / 2, markSprite); continue; }
                var p = viewport.Screen(at);
                var gold = new Color32(255, 208, 106, 255);
                AddQuad(quads, p.X + 11, p.Y - 23, 4, 9, gold); AddQuad(quads, p.X + 11, p.Y - 11, 4, 3, gold);
            }
            Flush(context, quads); FlushSprites(context, sprites);
        }
        private readonly struct SpriteDraw
        {
            public readonly Rect Rect;
            public readonly Sprite Sprite;
            public SpriteDraw(Rect rect, Sprite sprite) { Rect = rect; Sprite = sprite; }
        }
        // 1マス(世界座標で Tile)ごとの絵。隣のマスと辺をそろえて、拡大率が中途半端でも隙間を出さない。
        private void AddTile(List<SpriteDraw> sprites, int cx, int cy, Sprite sprite)
        {
            if (sprite == null) return;
            var a = viewport.Screen(new MapPoint(cx * MapLayout.Tile - MapLayout.Tile / 2, cy * MapLayout.Tile - MapLayout.Tile / 2));
            var b = viewport.Screen(new MapPoint(cx * MapLayout.Tile + MapLayout.Tile / 2, cy * MapLayout.Tile + MapLayout.Tile / 2));
            var rect = Rect.MinMaxRect(Mathf.Round(a.X), Mathf.Round(a.Y), Mathf.Round(b.X), Mathf.Round(b.Y));
            if (rect.Overlaps(contentRect)) sprites.Add(new SpriteDraw(rect, sprite));
        }
        // 絵の下の辺の中央を、世界座標の (x, bottom) に合わせて置く。
        private bool AddAnchored(List<SpriteDraw> sprites, float x, float bottom, Sprite sprite)
        {
            if (sprite == null) return false;
            var r = sprite.rect; float s = viewport.Scale;
            var p = viewport.Screen(new MapPoint(x, bottom));
            var rect = Rect.MinMaxRect(Mathf.Round(p.X - r.width * s / 2), Mathf.Round(p.Y - r.height * s),
                Mathf.Round(p.X + r.width * s / 2), Mathf.Round(p.Y));
            if (!rect.Overlaps(contentRect)) return false;
            sprites.Add(new SpriteDraw(rect, sprite)); return true;
        }
        private void AddTerrain(List<SpriteDraw> sprites, List<SpriteDraw> overlays, List<Quad> quads, MapArt art)
        {
            float tile = MapLayout.Tile;
            var topLeft = viewport.World(new MapPoint(0, 0)); var bottomRight = viewport.World(new MapPoint(contentRect.width, contentRect.height));
            int x0 = Mathf.FloorToInt(topLeft.X / tile + 0.5f) - 1, x1 = Mathf.FloorToInt(bottomRight.X / tile + 0.5f) + 1;
            int y0 = Mathf.FloorToInt(topLeft.Y / tile + 0.5f) - 1, y1 = Mathf.FloorToInt(bottomRight.Y / tile + 0.5f) + 1;
            // 引いて見た時は、壁を1枚ずつ描かず暗い色で塗る(描く数を抑える)。
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 20000)
                AddQuad(quads, 0, 0, contentRect.width, contentRect.height, new Color32(34, 30, 34, 255));
            else
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (!land.Contains(Key(x, y))) AddTile(sprites, x, y, art.Wall[MapLayout.StableHash(x + "," + y) % art.Wall.Length]);
            foreach (long key in land)
            {
                int x = (int)(key >> 32), y = (int)(uint)key;
                if (x < x0 || x > x1 || y < y0 || y > y1) continue;
                // 壁に接する辺の組み合わせ: 上=1 右=2 下=4 左=8。縁なしの時だけ、斜めが壁の角を調べる。
                int mask = (land.Contains(Key(x, y - 1)) ? 0 : 1) | (land.Contains(Key(x + 1, y)) ? 0 : 2)
                    | (land.Contains(Key(x, y + 1)) ? 0 : 4) | (land.Contains(Key(x - 1, y)) ? 0 : 8);
                Sprite sprite;
                if (art.HasPaths && pathMask.TryGetValue(key, out int open))
                {
                    AddTile(sprites, x, y, art.PathPlain[open]);
                    AddPathOverlay(overlays, key, x, y, open, art);
                    continue;
                }
                if (collapsing.TryGetValue(key, out int started) && frame - started < art.Collapse.Length)
                { AddTile(sprites, x, y, art.Collapse[frame - started]); continue; }
                if (mask != 0) sprite = art.Border[mask];
                else
                {
                    int inner = !land.Contains(Key(x - 1, y - 1)) ? 0 : !land.Contains(Key(x + 1, y - 1)) ? 1
                        : !land.Contains(Key(x + 1, y + 1)) ? 2 : !land.Contains(Key(x - 1, y + 1)) ? 3 : -1;
                    sprite = inner >= 0 && inner < art.BorderInner.Length ? art.BorderInner[inner]
                        : art.Ground[MapLayout.StableHash(x + ":" + y) % art.Ground.Length];
                }
                AddTile(sprites, x, y, sprite);
            }
        }
        // 種別の重ね絵は、まっすぐ(縦5・横10)のマスだけに載せる。
        private void AddPathOverlay(List<SpriteDraw> overlays, long key, int x, int y, int open, MapArt art)
        {
            if ((open != 5 && open != 10) || !pathKind.TryGetValue(key, out var kind)) return;
            bool vertical = open == 5;
            if (vertical != (kind.Value == 0 || kind.Value == 2)) return;
            Sprite[] frames = kind.Key == RoadKind.Premise ? art.OverlayPremise
                : kind.Key == RoadKind.Contrast ? art.OverlayContrast : art.OverlayExample;
            int index = kind.Key == RoadKind.Premise ? kind.Value : vertical ? 1 : 0;
            if (index < frames.Length) AddTile(overlays, x, y, frames[index]);
        }
        private void AddFlowers(List<SpriteDraw> sprites, MapArt art)
        {
            if (snapshot == null) return;
            var found = new List<KeyValuePair<float, SpriteDraw>>();
            var one = new List<SpriteDraw>(1);
            foreach (var flower in snapshot.Flowers)
            {
                if (nodes[flower.Node].TypedChars == 0) continue;
                int emotion = (int)flower.Emotion, hash = MapLayout.StableHash(flower.Node + ":" + flower.Line);
                var frames = art.Flowers[emotion];
                var sprite = flower.Withered ? art.Withered[emotion] : frames.Length == 0 ? null : frames[(frame + hash) % frames.Length];
                var p = positions[flower.Node];
                // 拠点のマスと、そのまわり1マスのどこかに咲かせる。
                float x = p.X + (hash % 3 - 1) * MapLayout.Tile, bottom = p.Y + (hash / 3 % 3 - 1) * MapLayout.Tile + MapLayout.Tile / 2;
                one.Clear();
                if (AddAnchored(one, x, bottom, sprite)) found.Add(new KeyValuePair<float, SpriteDraw>(bottom, one[0]));
            }
            // 奥(上)から手前(下)の順に重ねる。
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var pair in found) sprites.Add(pair.Value);
        }
        private void AddFlags(List<SpriteDraw> sprites, List<Quad> quads, MapArt art)
        {
            if (snapshot == null) return;
            foreach (var node in snapshot.Nodes.OrderBy(n => positions[n.Id].Y))
            {
                var frames = art.FlagFor(MapLayout.StableHash(node.Town));
                if (frames.Length == 0)
                { var p = viewport.Screen(positions[node.Id]); AddQuad(quads, p.X - 5, p.Y - 5, 10, 10, new Color32(210, 230, 188, 255)); continue; }
                var at = positions[node.Id];
                AddAnchored(sprites, at.X, at.Y + MapLayout.Tile / 2, frames[frame % frames.Length]);
            }
        }
        // 同じ絵(テクスチャ)のものをまとめて1つのメッシュにする。
        private static void FlushSprites(MeshGenerationContext context, List<SpriteDraw> draws)
        {
            const int batch = 12000;
            foreach (var group in draws.GroupBy(d => d.Sprite.texture))
            {
                var list = group.ToList(); var texture = group.Key; float tw = texture.width, th = texture.height;
                for (int offset = 0; offset < list.Count; offset += batch)
                {
                    int count = Math.Min(batch, list.Count - offset); var mesh = context.Allocate(count * 4, count * 6, texture);
                    for (int i = 0; i < count; i++)
                    {
                        var d = list[offset + i]; var t = d.Sprite.textureRect;
                        float u0 = t.xMin / tw, u1 = t.xMax / tw, v0 = t.yMin / th, v1 = t.yMax / th;
                        Color32 white = new Color32(255, 255, 255, 255);
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMin, d.Rect.yMin, Vertex.nearZ), uv = new Vector2(u0, v1), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMax, d.Rect.yMin, Vertex.nearZ), uv = new Vector2(u1, v1), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMax, d.Rect.yMax, Vertex.nearZ), uv = new Vector2(u1, v0), tint = white });
                        mesh.SetNextVertex(new Vertex { position = new Vector3(d.Rect.xMin, d.Rect.yMax, Vertex.nearZ), uv = new Vector2(u0, v0), tint = white });
                        ushort start = (ushort)(i * 4);
                        mesh.SetNextIndex(start); mesh.SetNextIndex((ushort)(start + 1)); mesh.SetNextIndex((ushort)(start + 2));
                        mesh.SetNextIndex((ushort)(start + 2)); mesh.SetNextIndex((ushort)(start + 3)); mesh.SetNextIndex(start);
                    }
                }
            }
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
