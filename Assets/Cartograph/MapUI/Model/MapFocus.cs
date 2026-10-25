using System;
using System.Collections.Generic;
using System.Linq;

namespace Cartowords.MapPresentation
{
    // エディタのカーソル行から、マップ上の拠点を求める。
    public static class MapFocus
    {
        // 同じファイルの拠点のうち、見出し行がカーソル行以前で最後のもの。最初の見出しより前の行は最初の拠点。
        public static string NodeAt(IEnumerable<MapNode> nodes, string file, int line)
        {
            if (nodes == null || file == null) return null;
            MapNode best = null, first = null;
            foreach (var node in nodes)
            {
                if (node == null || node.File != file) continue;
                if (first == null || node.Line < first.Line) first = node;
                if (node.Line <= line && (best == null || node.Line > best.Line)) best = node;
            }
            return (best ?? first)?.Id;
        }
    }

    // 画面の内側の余白より外にある点を、中央へ寄せるパン量を求める。
    public static class MapAutoScroll
    {
        public const float MarginRatio = 0.2f;
        public static bool TryDelta(MapPoint screen, float width, float height, out float dx, out float dy)
        {
            dx = dy = 0;
            if (width <= 1 || height <= 1 || float.IsNaN(screen.X) || float.IsNaN(screen.Y)) return false;
            float left = width * MarginRatio, right = width * (1 - MarginRatio), top = height * MarginRatio, bottom = height * (1 - MarginRatio);
            if (screen.X >= left && screen.X <= right && screen.Y >= top && screen.Y <= bottom) return false;
            dx = width / 2 - screen.X; dy = height / 2 - screen.Y;
            return true;
        }
    }

    // パン量を、ゆっくり始まりゆっくり終わる動きで少しずつ渡す。Advance の戻り値は「終わった」。
    public sealed class MapPanTween
    {
        private readonly float totalX, totalY, duration;
        private float applied;
        public MapPanTween(float dx, float dy, float duration)
        { totalX = dx; totalY = dy; this.duration = Math.Max(0.0001f, duration); }
        public bool Advance(float elapsed, out float dx, out float dy)
        {
            float t = Math.Max(0, Math.Min(1, elapsed / duration));
            float eased = t * t * (3 - 2 * t), step = eased - applied;
            applied = eased; dx = totalX * step; dy = totalY * step;
            return t >= 1;
        }
    }

    // マップを操作している間と、最後の操作から一定時間は、自動スクロールを止める。
    public sealed class MapInputGate
    {
        private readonly float quiet;
        private float last = float.NegativeInfinity;
        public MapInputGate(float quietSeconds = 3f) { quiet = quietSeconds; }
        public void Touch(float now) { last = now; }
        public bool IsQuiet(float now, bool active) => !active && now - last >= quiet;
    }

    // 見出しの書き換えで拠点の ID が変わったものを、前の拠点と対応づける(位置の引き継ぎと、追加との見分けに使う)。
    // 戻り値は 新しいID → 古いID。
    public static class MapRename
    {
        public static Dictionary<string, string> Match(IEnumerable<MapNode> before, IEnumerable<MapNode> after)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (before == null || after == null) return result;
            var oldNodes = before.Where(n => n != null).ToList(); var newNodes = after.Where(n => n != null).ToList();
            var oldIds = new HashSet<string>(oldNodes.Select(n => n.Id), StringComparer.Ordinal);
            var newIds = new HashSet<string>(newNodes.Select(n => n.Id), StringComparer.Ordinal);
            foreach (var file in newNodes.Select(n => n.File).Distinct(StringComparer.Ordinal))
            {
                var removed = oldNodes.Where(n => n.File == file && !newIds.Contains(n.Id)).OrderBy(n => n.Line).ToList();
                var added = newNodes.Where(n => n.File == file && !oldIds.Contains(n.Id)).OrderBy(n => n.Line).ToList();
                if (removed.Count == 0 || added.Count == 0) continue;
                if (removed.Count == added.Count && removed.Zip(added, (a, b) => a.Level == b.Level).All(same => same))
                {
                    for (int i = 0; i < added.Count; i++) result[added[i].Id] = removed[i].Id;
                    continue;
                }
                // 見出しのないノート(拠点 1 つ)に最初の見出しが付いた時と、最後の見出しが消えた時は、ノートの拠点を引き継ぐ。
                bool wasPlain = oldNodes.Count(n => n.File == file) == 1, isPlain = newNodes.Count(n => n.File == file) == 1;
                if (wasPlain || isPlain) result[added[0].Id] = removed[0].Id;
            }
            return result;
        }
    }
}
