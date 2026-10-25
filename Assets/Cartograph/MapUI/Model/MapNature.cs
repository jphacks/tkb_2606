using System;
using System.Collections.Generic;
using System.Linq;

namespace Cartowords.MapPresentation
{
    public enum NatureKind
    {
        Grass, Sprout, Bush, BushBerry, Rock, MushroomBrown, MushroomRed, Stump,
        PineSmall, PineLarge, RoundSmall, RoundLarge
    }

    public readonly struct NatureItem
    {
        public readonly NatureKind Kind;
        // X,Y は基準のマス。OffsetX/OffsetY はマス内のずれ(単位はマス、おおむね ±0.5)。描く位置は (X+OffsetX, Y+OffsetY)。
        public readonly int X, Y;
        public readonly float OffsetX, OffsetY;
        public NatureItem(NatureKind kind, int x, int y, float offsetX = 0f, float offsetY = 0f)
        { Kind = kind; X = x; Y = y; OffsetX = offsetX; OffsetY = offsetY; }
        public float WorldX => X + OffsetX;
        public float WorldY => Y + OffsetY;
        public bool IsLargeTree => Kind == NatureKind.PineLarge || Kind == NatureKind.RoundLarge;
    }

    // 自然物の密度と段階の設定(仮の値)。Resources/MapConfig/nature.json で上書きできる。
    [Serializable]
    public sealed class NatureSettings
    {
        // 本文のこの文字数ごとに 1 個。1 つの拠点の上限と、地図全体の上限。
        public int CharsPerItem = 50, MaxPerNode = 40, TotalCap = 4000;
        // 土地の空きマスのうち、自然物で埋めてよい割合の上限(0〜1)。1 に近いと、空きマスが木で埋まり、規則正しく並んで見える。
        public float MaxFillRatio = 0.5f;
        // 本文量(文字数)がこの値以上なら、その段階へ。段階が上がるほど、低木・木・森になる。
        public int BushFrom = 200, TreeFrom = 400, ForestFrom = 900;
        // 大きい木は、拠点の中心からこのマス数以上(縦横のどちらか)離れた所だけに置く。
        public int LargeTreeDistance = 2;
        // 拡大率がこの値より小さい時は、自然物を描かない(描く数を抑える)。
        public float HideBelowScale = 0.3f;

        public static NatureSettings Default => new NatureSettings();

        public NatureSettings Normalized()
        {
            var s = (NatureSettings)MemberwiseClone();
            s.CharsPerItem = Math.Max(1, s.CharsPerItem); s.MaxPerNode = Math.Max(0, s.MaxPerNode); s.TotalCap = Math.Max(0, s.TotalCap);
            s.BushFrom = Math.Max(0, s.BushFrom); s.TreeFrom = Math.Max(s.BushFrom, s.TreeFrom); s.ForestFrom = Math.Max(s.TreeFrom, s.ForestFrom);
            s.LargeTreeDistance = Math.Max(1, s.LargeTreeDistance);
            if (float.IsNaN(s.HideBelowScale) || s.HideBelowScale < 0) s.HideBelowScale = 0;
            s.MaxFillRatio = float.IsNaN(s.MaxFillRatio) ? 0.5f : Math.Max(0f, Math.Min(1f, s.MaxFillRatio));
            return s;
        }
    }

    public readonly struct NatureNode
    {
        public readonly string Id, Town;
        public readonly long Cell;
        public readonly int TypedChars, Level;
        public NatureNode(string id, long cell, int typedChars, int level, string town)
        { Id = id; Cell = cell; TypedChars = typedChars; Level = level; Town = town ?? ""; }
    }

    // 拠点の土地に自然物を置く。乱数は使わず、拠点IDとマスから決まる値だけで選ぶので、同じ入力なら必ず同じ結果になる。
    // 本文が増えると、すでにある自然物は動かず、追加分だけ増える(同じ段階の間)。
    public static class NaturePlacer
    {
        public static List<NatureItem> Place(IEnumerable<NatureNode> nodes, IEnumerable<MapRoad> roads, MapOccupancy occupancy, NatureSettings settings)
        {
            var s = (settings ?? NatureSettings.Default).Normalized();
            var result = new List<NatureItem>();
            var targets = nodes.Where(n => n.TypedChars > 0 && MapLayout.LandRadius(n.TypedChars) >= 2)
                .OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
            if (targets.Length == 0 || s.MaxPerNode == 0 || s.TotalCap == 0) return result;

            var incident = IncidentKinds(roads);
            // 全体の上限を超える時は、各拠点の数を同じ割合で減らす。
            var budgets = targets.Select(n => Math.Min(s.MaxPerNode, Math.Max(1, n.TypedChars / s.CharsPerItem))).ToArray();
            long total = budgets.Sum(b => (long)b);
            if (total > s.TotalCap) for (int i = 0; i < budgets.Length; i++) budgets[i] = (int)(budgets[i] * (long)s.TotalCap / total);

            var natureCells = new HashSet<long>();
            for (int i = 0; i < targets.Length; i++)
            {
                if (budgets[i] > 0) PlaceNode(targets[i], budgets[i], incident, occupancy, s, result, natureCells);
            }
            return result;
        }

        // 拠点ごとの、道の種別(自動の道は除く)。前提の道は、先の拠点だけに付ける。
        static Dictionary<string, List<RoadKind>> IncidentKinds(IEnumerable<MapRoad> roads)
        {
            var result = new Dictionary<string, List<RoadKind>>(StringComparer.Ordinal);
            void Add(string id, RoadKind kind)
            {
                if (!result.TryGetValue(id, out var list)) result[id] = list = new List<RoadKind>();
                if (!list.Contains(kind)) list.Add(kind);
            }
            if (roads == null) return result;
            foreach (var road in roads.Where(r => !r.Auto))
            {
                if (road.Kind != RoadKind.Premise) Add(road.From, road.Kind);
                Add(road.To, road.Kind);
            }
            foreach (var list in result.Values) list.Sort();
            return result;
        }

        static void PlaceNode(NatureNode node, int budget, Dictionary<string, List<RoadKind>> incident, MapOccupancy occupancy,
            NatureSettings s, List<NatureItem> result, HashSet<long> natureCells)
        {
            int cx = MapCell.X(node.Cell), cy = MapCell.Y(node.Cell), r = MapLayout.LandRadius(node.TypedChars);
            var candidates = new List<(int X, int Y, int Hash, bool Side)>();
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > r * r + r) continue;      // 土地の範囲(MapCanvas の土地と同じ式)
                int x = cx + dx, y = cy + dy;
                if (!occupancy.IsFree(x, y)) continue;
                bool side = occupancy.Kind(x + 1, y) == OccupyKind.Path || occupancy.Kind(x - 1, y) == OccupyKind.Path ||
                    occupancy.Kind(x, y + 1) == OccupyKind.Path || occupancy.Kind(x, y - 1) == OccupyKind.Path;
                candidates.Add((x, y, MapLayout.StableHash(node.Id + ":" + x + "," + y), side));
            }
            candidates.Sort((a, b) => a.Hash != b.Hash ? a.Hash.CompareTo(b.Hash) : a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            // 空きマスの一定の割合までしか置かない(全部埋めると、木が規則正しく並んで見える)。
            budget = Math.Min(budget, (int)Math.Ceiling(candidates.Count * s.MaxFillRatio));
            incident.TryGetValue(node.Id, out var kinds);
            int town = MapLayout.StableHash(node.Town);
            int tier = node.TypedChars >= s.ForestFrom ? 3 : node.TypedChars >= s.TreeFrom ? 2 : node.TypedChars >= s.BushFrom ? 1 : 0;
            int placed = 0, sideBudget = budget / 3;
            // 隣り合う拠点の土地が重なる所で、同じマスに 2 回置かないための記録(自然物は占有表に入れない)。
            var used = natureCells;
            foreach (var c in candidates)
            {
                if (placed >= budget) break;
                if (!c.Side || sideBudget <= 0) continue;
                if (!used.Add(MapCell.Key(c.X, c.Y))) continue;
                result.Add(Make(SideKind(kinds, c.Hash), node, c.X, c.Y, occupancy)); placed++; sideBudget--;
            }
            foreach (var c in candidates)
            {
                if (placed >= budget) break;
                if (!used.Add(MapCell.Key(c.X, c.Y))) continue;
                int distance = Math.Max(Math.Abs(c.X - cx), Math.Abs(c.Y - cy));
                result.Add(Make(Choose(c.Hash, tier, node.Level, distance, town, s), node, c.X, c.Y, occupancy)); placed++;
            }
        }

        // マス内のずれ。拠点ID とマスだけから決まる(乱数なし)。隣のマスが道・建物・旗などで塞がっていたら、そちらへは寄せない。
        // 自然物同士の重なりは許す(占有表には記録しない)。
        public static (float X, float Y) OffsetFor(string nodeId, int x, int y)
        {
            int h = MapLayout.StableHash(nodeId + ":" + x + "," + y + ":offset") & 0x7fffffff;
            return ((h % 1024) / 1023f - 0.5f, (h / 1024 % 1024) / 1023f - 0.5f);
        }

        static NatureItem Make(NatureKind kind, NatureNode node, int x, int y, MapOccupancy occupancy)
        {
            var (ox, oy) = OffsetFor(node.Id, x, y);
            const float Near = 0.1f;
            if (!occupancy.IsFree(x + 1, y)) ox = Math.Min(ox, Near);
            if (!occupancy.IsFree(x - 1, y)) ox = Math.Max(ox, -Near);
            if (!occupancy.IsFree(x, y + 1)) oy = Math.Min(oy, Near);
            if (!occupancy.IsFree(x, y - 1)) oy = Math.Max(oy, -Near);
            return new NatureItem(kind, x, y, ox, oy);
        }

        // 道の脇: 前提の先は松、対比は岩、具体例はキノコか芽、ふつうの道は低木。複数あれば、マスごとに選ぶ。
        static NatureKind SideKind(List<RoadKind> kinds, int hash)
        {
            var kind = kinds == null || kinds.Count == 0 ? RoadKind.Unspecified : kinds[hash % kinds.Count];
            switch (kind)
            {
                case RoadKind.Premise: return NatureKind.PineSmall;
                case RoadKind.Contrast: return NatureKind.Rock;
                case RoadKind.Example: return hash / 7 % 2 == 0 ? NatureKind.MushroomRed : NatureKind.Sprout;
                default: return NatureKind.Bush;
            }
        }

        // 本文量の段階(0..3)・見出しの階層・町から、マスの自然物を選ぶ。町が同じなら、木の系統(丸い木か松)とキノコの色が同じ。
        static NatureKind Choose(int hash, int tier, int level, int distance, int town, NatureSettings s)
        {
            int r = hash / 7 % 100;
            bool round = town % 2 == 0;
            var mushroom = town / 2 % 2 == 0 ? NatureKind.MushroomBrown : NatureKind.MushroomRed;
            var small = round ? NatureKind.RoundSmall : NatureKind.PineSmall;
            var large = round ? NatureKind.RoundLarge : NatureKind.PineLarge;
            bool canLarge = distance >= s.LargeTreeDistance;
            switch (tier)
            {
                case 0: return r < 50 ? NatureKind.Grass : NatureKind.Sprout;
                case 1:
                    return r < 30 ? NatureKind.Grass : r < 50 ? NatureKind.Sprout : r < 75 ? NatureKind.Bush
                        : r < 85 ? NatureKind.BushBerry : r < 95 ? mushroom : NatureKind.Stump;
                default:
                {
                    // 見出しレベル 3 は木にしない(低木)。1 は大きい木、2 とそれ以外は小さい木。
                    NatureKind tree = level == 3 ? (r % 2 == 0 ? NatureKind.Bush : NatureKind.BushBerry)
                        : (level == 1 || (level == 2 && tier == 3)) && canLarge && r >= (tier == 3 ? 70 : 85) ? large : small;
                    if (tier == 2)
                        return r < 15 ? NatureKind.Grass : r < 25 ? NatureKind.Bush : r < 35 ? NatureKind.BushBerry
                            : r < 42 ? mushroom : r < 47 ? NatureKind.Stump : r < 52 ? NatureKind.Rock : tree;
                    return r < 8 ? NatureKind.Grass : r < 14 ? NatureKind.Bush : r < 18 ? mushroom
                        : r < 22 ? NatureKind.Stump : r < 25 ? NatureKind.Rock : tree;
                }
            }
        }
    }
}
