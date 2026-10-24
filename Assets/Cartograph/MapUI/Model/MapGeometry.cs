using System;
using System.Collections.Generic;
using System.Linq;

namespace Cartowords.MapPresentation
{
    public readonly struct MapPoint
    {
        public readonly float X, Y;
        public MapPoint(float x, float y) { X = x; Y = y; }
    }
    public sealed class MapViewport
    {
        public float Scale { get; private set; } = 1;
        public float OffsetX { get; private set; }
        public float OffsetY { get; private set; }
        public MapPoint Screen(MapPoint p) => new MapPoint(p.X * Scale + OffsetX, p.Y * Scale + OffsetY);
        public MapPoint World(MapPoint p) => new MapPoint((p.X - OffsetX) / Scale, (p.Y - OffsetY) / Scale);
        public void Pan(float x, float y) { OffsetX += x; OffsetY += y; }
        public void Zoom(float factor, MapPoint anchor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0) return;
            var world = World(anchor); Scale = Math.Max(0.12f, Math.Min(3f, Scale * factor));
            OffsetX = anchor.X - world.X * Scale; OffsetY = anchor.Y - world.Y * Scale;
        }
        public void Pinch(MapPoint oldCenter, MapPoint newCenter, float oldDistance, float newDistance)
        {
            if (oldDistance > 1 && newDistance > 1) Zoom(newDistance / oldDistance, oldCenter);
            Pan(newCenter.X - oldCenter.X, newCenter.Y - oldCenter.Y);
        }
        public void Fit(IEnumerable<MapPoint> points, float width, float height)
        {
            var list = points.ToArray();
            if (width <= 0 || height <= 0) return;
            if (list.Length == 0) { Scale = 1; OffsetX = width / 2; OffsetY = height / 2; return; }
            float left = list.Min(p => p.X), right = list.Max(p => p.X);
            float top = list.Min(p => p.Y), bottom = list.Max(p => p.Y);
            Scale = Math.Max(0.12f, Math.Min(1.4f, Math.Min(width / (right - left + 200), height / (bottom - top + 200))));
            OffsetX = width / 2 - (left + right) / 2 * Scale;
            OffsetY = height / 2 - (top + bottom) / 2 * Scale;
        }
    }
    public sealed class MapTouchGesture
    {
        private sealed class Touch { public MapPoint Start, Point; public bool Moved; }
        private readonly Dictionary<int, Touch> touches = new Dictionary<int, Touch>();
        public int Count => touches.Count;
        public bool Contains(int id) => touches.ContainsKey(id);
        public void Down(int id, MapPoint point)
        {
            touches[id] = new Touch { Start = point, Point = point };
            if (touches.Count > 1) foreach (var touch in touches.Values) touch.Moved = true;
        }
        public bool Move(int id, MapPoint point, MapViewport viewport)
        {
            if (!touches.TryGetValue(id, out var touch)) return false;
            var pair = touches.OrderBy(p => p.Key).Take(2).Select(p => p.Value).ToArray();
            var previous = touch.Point;
            if (pair.Length == 2)
            {
                var oldCenter = Center(pair[0].Point, pair[1].Point); float oldDistance = Distance(pair[0].Point, pair[1].Point);
                touch.Point = point;
                viewport.Pinch(oldCenter, Center(pair[0].Point, pair[1].Point), oldDistance, Distance(pair[0].Point, pair[1].Point));
            }
            else
            {
                touch.Point = point;
                if (Distance(touch.Start, point) > 5) touch.Moved = true;
                if (touch.Moved) viewport.Pan(point.X - previous.X, point.Y - previous.Y);
            }
            return true;
        }
        public bool Up(int id, bool cancel = false)
        {
            if (!touches.TryGetValue(id, out var touch)) return false;
            touches.Remove(id);
            foreach (var other in touches.Values) other.Moved = true;
            return !cancel && !touch.Moved;
        }
        public void Clear() => touches.Clear();
        private static MapPoint Center(MapPoint a, MapPoint b) => new MapPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        private static float Distance(MapPoint a, MapPoint b) => (float)Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    }
    public static class MapLayout
    {
        public const float Tile = 32;
        const float Spread = Tile / 20; // 1マスが大きくなった分、拠点の間隔も広げる
        public static int LandRadius(int typedChars) => typedChars <= 0 ? 0 : Math.Min(4, 1 + (int)Math.Sqrt(typedChars / 100.0));
        public static int StableHash(string text)
        {
            unchecked { uint hash = 2166136261; foreach (char c in text) hash = (hash ^ c) * 16777619; return (int)(hash & 0x7fffffff); }
        }
        public static Dictionary<string, MapPoint> Solve(MapSnapshot snapshot, IReadOnlyDictionary<string, MapPoint> previous = null)
        {
            var nodes = snapshot.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
            int count = nodes.Length;
            var towns = nodes.Select(n => n.Town).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var x = new float[count]; var y = new float[count]; var ax = new float[count]; var ay = new float[count];
            var pinned = new bool[count];
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                index[nodes[i].Id] = i;
                int town = Array.IndexOf(towns, nodes[i].Town);
                double angle = town * 2 * Math.PI / Math.Max(1, towns.Length);
                float distance = (nodes[i].Isolated ? 620 : towns.Length > 1 ? 280 : 0) * Spread;
                ax[i] = (float)Math.Cos(angle) * distance; ay[i] = (float)Math.Sin(angle) * distance;
                double seed = StableHash(nodes[i].Id) * 0.001;
                x[i] = ax[i] + (float)Math.Cos(seed) * (80 + i % 7 * 24) * Spread;
                y[i] = ay[i] + (float)Math.Sin(seed) * (80 + i % 11 * 18) * Spread;
                if (previous != null && previous.TryGetValue(nodes[i].Id, out var point))
                { x[i] = point.X; y[i] = point.Y; pinned[i] = true; }
            }
            // Bounded layout work: never runs each frame. Rendering uses its cached result.
            for (int step = 0; step < 28; step++)
            {
                var dx = new float[count]; var dy = new float[count];
                for (int i = 0; i < count; i++)
                {
                    dx[i] += (ax[i] - x[i]) * 0.018f; dy[i] += (ay[i] - y[i]) * 0.018f;
                    for (int j = i + 1; j < count; j++)
                    {
                        float vx = x[i] - x[j], vy = y[i] - y[j];
                        float squared = vx * vx + vy * vy + 64;
                        if (squared > 300000) continue;
                        float force = 1400 / squared;
                        dx[i] += vx * force; dy[i] += vy * force; dx[j] -= vx * force; dy[j] -= vy * force;
                    }
                }
                foreach (var edge in snapshot.Roads)
                {
                    int a = index[edge.From], b = index[edge.To];
                    float vx = x[b] - x[a], vy = y[b] - y[a];
                    float distance = (float)Math.Sqrt(vx * vx + vy * vy + 1);
                    float pull = (distance - 155 * Spread) * 0.025f / distance;
                    dx[a] += vx * pull; dy[a] += vy * pull; dx[b] -= vx * pull; dy[b] -= vy * pull;
                }
                for (int i = 0; i < count; i++)
                { if (pinned[i]) continue; x[i] += Math.Max(-18, Math.Min(18, dx[i])); y[i] += Math.Max(-18, Math.Min(18, dy[i])); }
            }
            var result = new Dictionary<string, MapPoint>(StringComparer.Ordinal);
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            if (previous != null)
                foreach (var node in nodes)
                    if (previous.TryGetValue(node.Id, out var point))
                    { result[node.Id] = point; occupied.Add((int)Math.Round(point.X / Tile) + ":" + (int)Math.Round(point.Y / Tile)); }
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(nodes[i].Id)) continue;
                int tx = (int)Math.Round(x[i] / Tile), ty = (int)Math.Round(y[i] / Tile);
                // Stable, bounded collision resolution at tile resolution.
                while (!occupied.Add(tx + ":" + ty)) tx++;
                result[nodes[i].Id] = new MapPoint(tx * Tile, ty * Tile);
            }
            return result;
        }
    }
}
