using System;
using System.Collections.Generic;

namespace Cartowords.MapPresentation
{
    // 道の経路。道は 16px の細かいマス(サブマス)の上にあり、土地のマス(32px)とは決して重ならない。
    // サブマス (sx, sy) は、土地のマス (sx >> 1, sy >> 1) の中の 1/4。向きは 0上 1右 2下 3左、マスクは 上=1 右=2 下=4 左=8。
    public sealed class RoadSpec
    {
        public string From, To;
        /// <summary>両端の拠点の土地(領域)のマス(32px)。道はこの領域の縁から出る。</summary>
        public ICollection<long> FromRegion, ToRegion;
    }

    public sealed class RoadRoute
    {
        /// <summary>道の番号(RoadSpec の並びの添字)。小さいほうが交差で上を通る。</summary>
        public int Index;
        public string From, To;
        /// <summary>始点側(領域の縁)から終点側へ、サブマスを順に。</summary>
        public List<long> Cells = new List<long>();
        /// <summary>各サブマスの、この道だけのマスク(前後の向き + 両端では領域へ向かう口)。</summary>
        public List<int> Masks = new List<int>();
    }

    public sealed class RoadCrossing
    {
        public long Cell;             // サブマス
        public int Over, Under;       // 道の番号
        public bool OverVertical;     // 上を通る道が縦か
    }

    public sealed class RoadLayout
    {
        public readonly List<RoadRoute> Routes = new List<RoadRoute>();
        /// <summary>描くマスク。合流・分岐は全員のマスクの和、橋の交差は上の道のマスクだけ。</summary>
        public readonly Dictionary<long, int> Mask = new Dictionary<long, int>();
        /// <summary>そのサブマスを描く道の番号(合流は最小、橋は上の道)。</summary>
        public readonly Dictionary<long, int> Owner = new Dictionary<long, int>();
        public readonly List<RoadCrossing> Crossings = new List<RoadCrossing>();

        /// <summary>道が触れる 32px マス(建物・自然物の占有に使う)。</summary>
        public HashSet<long> TouchedCells()
        {
            var result = new HashSet<long>();
            foreach (long sub in Mask.Keys) result.Add(MapRoads.CellOf(sub));
            return result;
        }
    }

    public static class MapRoads
    {
        public const int MaskUp = 1, MaskRight = 2, MaskDown = 4, MaskLeft = 8;
        static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { -1, 0, 1, 0 };
        const int StepCost = 4, TurnCost = 3, NoDir = 4, MaxExpansions = 400000;

        public static long Sub(int x, int y) => MapCell.Key(x, y);
        /// <summary>サブマスを含む 32px マス。</summary>
        public static long CellOf(long sub) => MapCell.Key(MapCell.X(sub) >> 1, MapCell.Y(sub) >> 1);

        // 交差(橋)の規則: 1 つのサブマスを通るのがちょうど 2 本の別の道で、
        //   ・一方のマスクがまっすぐ縦(5)、もう一方がまっすぐ横(10)で、
        //   ・どちらもそのマスが道の端(領域の縁のマス)ではなく、
        //   ・2 本が拠点を共有していない
        // ときだけ「交差」。それ以外(同じ拠点につながる道、T 字、平行な合流、端のマス)は合流で、マスクを足して普通に描く。
        public static bool IsCrossing(int maskA, bool endA, int maskB, bool endB, bool shareNode)
        {
            if (endA || endB || shareNode) return false;
            return (maskA == 5 && maskB == 10) || (maskA == 10 && maskB == 5);
        }

        public static RoadLayout Build(ISet<long> land, IList<RoadSpec> specs)
        {
            var layout = new RoadLayout();
            for (int i = 0; i < specs.Count; i++)
            {
                var route = FindRoute(land, specs[i]);
                if (route == null) continue;
                route.Index = i; route.From = specs[i].From; route.To = specs[i].To;
                layout.Routes.Add(route);
            }
            // サブマスごとに、通る道を集める。
            var users = new Dictionary<long, List<int>>();   // サブマス → Routes の添字
            var order = new List<long>();
            for (int r = 0; r < layout.Routes.Count; r++)
                foreach (long cell in layout.Routes[r].Cells)
                {
                    if (!users.TryGetValue(cell, out var list)) { users[cell] = list = new List<int>(); order.Add(cell); }
                    if (!list.Contains(r)) list.Add(r);
                }
            order.Sort();
            foreach (long cell in order)
            {
                var list = users[cell];
                int union = 0, owner = int.MaxValue;
                foreach (int r in list)
                {
                    var route = layout.Routes[r]; int at = route.Cells.IndexOf(cell);
                    union |= route.Masks[at]; if (route.Index < owner) owner = route.Index;
                }
                if (list.Count == 2)
                {
                    var a = layout.Routes[list[0]]; var b = layout.Routes[list[1]];
                    int ia = a.Cells.IndexOf(cell), ib = b.Cells.IndexOf(cell);
                    bool share = a.From == b.From || a.From == b.To || a.To == b.From || a.To == b.To;
                    if (IsCrossing(a.Masks[ia], ia == 0 || ia == a.Cells.Count - 1, b.Masks[ib], ib == 0 || ib == b.Cells.Count - 1, share))
                    {
                        var over = a.Index < b.Index ? a : b; var under = over == a ? b : a;
                        int overMask = over == a ? a.Masks[ia] : b.Masks[ib];
                        layout.Crossings.Add(new RoadCrossing { Cell = cell, Over = over.Index, Under = under.Index, OverVertical = overMask == 5 });
                        union = overMask; owner = over.Index;
                    }
                }
                layout.Mask[cell] = union; layout.Owner[cell] = owner;
            }
            return layout;
        }

        // 1 本の道を探す。領域が隣り合っている・道が通れない時は null。
        public static RoadRoute FindRoute(ISet<long> land, RoadSpec spec)
        {
            if (spec.FromRegion == null || spec.ToRegion == null || spec.FromRegion.Count == 0 || spec.ToRegion.Count == 0) return null;
            var fromCells = new List<long>(spec.FromRegion); fromCells.Sort();
            var toCells = new List<long>(spec.ToRegion); toCells.Sort();
            var toSet = new HashSet<long>(toCells);
            // 土地が隣り合っている(壁の隙間がない)なら道はいらない。
            foreach (long c in fromCells)
            {
                if (toSet.Contains(c)) return null;
                int cx = MapCell.X(c), cy = MapCell.Y(c);
                for (int d = 0; d < 4; d++) if (toSet.Contains(MapCell.Key(cx + Dx[d], cy + Dy[d]))) return null;
            }
            var sources = Frontier(land, fromCells);
            var targets = Frontier(land, toCells);
            if (sources.Count == 0 || targets.Count == 0) return null;
            var sourceList = new List<long>(sources.Keys); sourceList.Sort();

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (long c in fromCells) Extend(c, ref minX, ref minY, ref maxX, ref maxY);
            foreach (long c in toCells) Extend(c, ref minX, ref minY, ref maxX, ref maxY);
            // 目的地の領域の範囲(サブマス)。最短の見積もりに使う。
            int tx0 = int.MaxValue, ty0 = int.MaxValue, tx1 = int.MinValue, ty1 = int.MinValue;
            foreach (long c in toCells) Extend(c, ref tx0, ref ty0, ref tx1, ref ty1);

            foreach (int margin in new[] { 12, 60 })
            {
                var cells = Search(land, sourceList, targets, minX - margin, minY - margin, maxX + margin, maxY + margin, tx0, ty0, tx1, ty1);
                if (cells == null) continue;
                var route = new RoadRoute();
                route.Cells = cells;
                for (int i = 0; i < cells.Count; i++)
                {
                    int mask = 0;
                    if (i > 0) mask |= 1 << DirTo(cells[i], cells[i - 1]);
                    if (i + 1 < cells.Count) mask |= 1 << DirTo(cells[i], cells[i + 1]);
                    if (i == 0) mask |= 1 << sources[cells[0]];
                    if (i == cells.Count - 1) mask |= 1 << targets[cells[i]];
                    route.Masks.Add(mask);
                }
                return route;
            }
            return null;
        }

        // サブマス単位の範囲(両端を含む)に広げる。
        static void Extend(long cell, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            int x = MapCell.X(cell) * 2, y = MapCell.Y(cell) * 2;
            if (x < minX) minX = x; if (x + 1 > maxX) maxX = x + 1;
            if (y < minY) minY = y; if (y + 1 > maxY) maxY = y + 1;
        }

        static int DirTo(long from, long to)
        {
            int dx = MapCell.X(to) - MapCell.X(from), dy = MapCell.Y(to) - MapCell.Y(from);
            return dy < 0 ? 0 : dx > 0 ? 1 : dy > 0 ? 2 : 3;
        }

        // 領域の縁に接する、土地ではないサブマス → そこから領域へ向かう向き(複数あれば小さい向き)。
        static Dictionary<long, int> Frontier(ISet<long> land, List<long> region)
        {
            var result = new Dictionary<long, int>();
            foreach (long c in region)
            {
                int cx = MapCell.X(c), cy = MapCell.Y(c);
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    int sx = cx * 2 + i, sy = cy * 2 + j;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = sx + Dx[d], ny = sy + Dy[d];
                        if (land.Contains(MapCell.Key(nx >> 1, ny >> 1))) continue;
                        long n = Sub(nx, ny); int toward = (d + 2) % 4;
                        if (!result.TryGetValue(n, out int old) || toward < old) result[n] = toward;
                    }
                }
            }
            return result;
        }

        struct Entry { public int F, G; public long State; }
        static bool Less(Entry a, Entry b)
        {
            if (a.F != b.F) return a.F < b.F;
            if (a.G != b.G) return a.G > b.G;
            return a.State < b.State;
        }

        // 向き付きの A*(曲がるたびに少し高くして、階段でなく折れ線にする)。同じ費用なら、サブマスの鍵が小さいほうを先に。
        static List<long> Search(ISet<long> land, List<long> sources, Dictionary<long, int> targets,
            int minX, int minY, int maxX, int maxY, int tx0, int ty0, int tx1, int ty1)
        {
            int Heuristic(int x, int y)
            {
                int dx = x < tx0 ? tx0 - x : x > tx1 ? x - tx1 : 0, dy = y < ty0 ? ty0 - y : y > ty1 ? y - ty1 : 0;
                return (dx + dy) * StepCost;
            }
            var best = new Dictionary<long, int>();
            var parent = new Dictionary<long, long>();
            var heap = new List<Entry>();
            void Push(Entry e)
            {
                heap.Add(e); int i = heap.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (!Less(heap[i], heap[p])) break; var t = heap[i]; heap[i] = heap[p]; heap[p] = t; i = p; }
            }
            Entry Pop()
            {
                var top = heap[0]; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                if (heap.Count > 0)
                {
                    heap[0] = last; int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, m = i;
                        if (l < heap.Count && Less(heap[l], heap[m])) m = l;
                        if (r < heap.Count && Less(heap[r], heap[m])) m = r;
                        if (m == i) break;
                        var t = heap[i]; heap[i] = heap[m]; heap[m] = t; i = m;
                    }
                }
                return top;
            }
            foreach (long s in sources)
            {
                int x = MapCell.X(s), y = MapCell.Y(s);
                if (x < minX || x > maxX || y < minY || y > maxY) continue;
                long state = (s << 3) | NoDir;
                best[state] = 0; Push(new Entry { F = Heuristic(x, y), G = 0, State = state });
            }
            for (int expanded = 0; heap.Count > 0 && expanded < MaxExpansions; expanded++)
            {
                var e = Pop();
                if (best[e.State] < e.G) continue;
                long cell = e.State >> 3; int dir = (int)(e.State & 7);
                if (targets.ContainsKey(cell))
                {
                    var path = new List<long>();
                    for (long s = e.State; ; s = parent[s])
                    {
                        path.Add(s >> 3);
                        if (!parent.ContainsKey(s)) break;
                    }
                    path.Reverse(); return path;
                }
                int cx = MapCell.X(cell), cy = MapCell.Y(cell);
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                    if (land.Contains(MapCell.Key(nx >> 1, ny >> 1))) continue;
                    int g = e.G + StepCost + (dir != NoDir && dir != d ? TurnCost : 0);
                    long state = (Sub(nx, ny) << 3) | (uint)d;
                    if (best.TryGetValue(state, out int old) && old <= g) continue;
                    best[state] = g; parent[state] = e.State;
                    Push(new Entry { F = g + Heuristic(nx, ny), G = g, State = state });
                }
            }
            return null;
        }
    }
}
