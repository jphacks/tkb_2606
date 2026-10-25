using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Cartowords.MapPresentation
{
    // 拠点の位置の保存形式(.cartograph/map-layout.txt)。
    // 1 行目は版、2 行目からは「x(マス)<TAB>y(マス)<TAB>拠点ID」。拠点IDは行の残りすべて(タブを含んでよい)。
    // 壊れた行は読み飛ばし、版が違えば全体を無視する。保存は拠点ID順で、同じ位置なら同じ文字列になる(git の差分が安定する)。
    public static class MapLayoutFile
    {
        public const string Header = "cartowords-map-layout 1";

        public static string Serialize(IReadOnlyDictionary<string, MapPoint> positions)
        {
            var text = new StringBuilder(Header).Append('\n');
            if (positions == null) return text.ToString();
            foreach (var pair in positions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Key.IndexOf('\n') >= 0 || pair.Key.IndexOf('\r') >= 0) continue;
                if (float.IsNaN(pair.Value.X) || float.IsNaN(pair.Value.Y) || float.IsInfinity(pair.Value.X) || float.IsInfinity(pair.Value.Y)) continue;
                text.Append(Cell(pair.Value.X)).Append('\t').Append(Cell(pair.Value.Y)).Append('\t').Append(pair.Key).Append('\n');
            }
            return text.ToString();
        }

        public static Dictionary<string, MapPoint> Parse(string text)
        {
            var result = new Dictionary<string, MapPoint>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return result;
            var lines = text.Split('\n');
            if (lines[0].TrimEnd('\r') != Header) return result;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                int first = line.IndexOf('\t'); if (first <= 0) continue;
                int second = line.IndexOf('\t', first + 1); if (second <= first + 1) continue;
                string id = line.Substring(second + 1);
                if (id.Length == 0 ||
                    !int.TryParse(line.Substring(0, first), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(line.Substring(first + 1, second - first - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int y)) continue;
                result[id] = new MapPoint(x * MapLayout.Tile, y * MapLayout.Tile);
            }
            return result;
        }

        private static string Cell(float world) => ((int)Math.Round(world / MapLayout.Tile)).ToString(CultureInfo.InvariantCulture);

        // 位置が同じか(保存すべき変化があるか)の比較。
        public static bool Same(IReadOnlyDictionary<string, MapPoint> a, IReadOnlyDictionary<string, MapPoint> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var pair in a)
                if (!b.TryGetValue(pair.Key, out var other) || other.X != pair.Value.X || other.Y != pair.Value.Y) return false;
            return true;
        }
    }
}
