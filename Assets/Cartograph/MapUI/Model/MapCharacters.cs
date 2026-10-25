using System;
using System.Collections.Generic;

namespace Cartowords.MapPresentation
{
    // 向きは MapArt.Facing と同じ並び(下・上・左・右)。
    public enum NpcFacing { Down, Up, Left, Right }

    // ある時刻の人物の姿。X,Y はマス単位の位置(描く位置は NatureItem と同じく (X*Tile, Y*Tile + Tile/2) が足元)。
    public readonly struct NpcPose
    {
        public readonly float X, Y;
        public readonly NpcFacing Facing;
        public readonly int Frame;   // 0..3。止まっている間は 0。
        public readonly bool Moving;
        public NpcPose(float x, float y, NpcFacing facing, int frame, bool moving)
        { X = x; Y = y; Facing = facing; Frame = frame; Moving = moving; }
    }

    // ノート(ファイル)ごとに 1 人。そのファイルの土地にある空きマスの経由点を、決まった順に巡る。
    // 姿は (時刻, ファイルのハッシュ, 経由点) だけで決まる純粋な関数で、乱数も毎フレームの確保も使わない。
    public sealed class MapNpc
    {
        public const int KindCount = 8;
        public const float TilesPerSecond = 0.8f, FrameSeconds = 0.15f, MinPause = 1f, MaxPause = 3f;
        public const int WalkFrames = 4, MaxWaypoints = 6;

        private struct Leg { public int AX, AY, CX, CY, BX, BY; public float Pause, Seg1, Seg2, Start; }

        public string File { get; }
        public int Kind { get; }
        private readonly int hash;
        private readonly Leg[] legs;
        private readonly float cycle, offset;
        private readonly int startX, startY;
        public int WaypointCount { get; }
        public bool Wanders => legs.Length > 0;
        // 経由点(巡る順)。テストと確認用。
        public IReadOnlyList<long> Waypoints { get; }

        public static int KindOf(string file) => MapLayout.StableHash(file) % KindCount;

        private MapNpc(string file, List<long> waypoints, List<Leg> legList)
        {
            File = file; hash = MapLayout.StableHash(file); Kind = hash % KindCount;
            Waypoints = waypoints; WaypointCount = waypoints.Count;
            startX = MapCell.X(waypoints[0]); startY = MapCell.Y(waypoints[0]);
            legs = legList.ToArray();
            float total = 0;
            for (int i = 0; i < legs.Length; i++)
            {
                legs[i].Start = total;
                total += legs[i].Pause + (legs[i].Seg1 + legs[i].Seg2) / TilesPerSecond;
            }
            cycle = total;
            // ファイルごとに巡回の途中から始め、みんなが同じ動きにならないようにする。
            offset = cycle > 0 ? (hash % 997) / 997f * cycle : 0;
        }

        public NpcPose At(double seconds)
        {
            if (legs.Length == 0) return new NpcPose(startX, startY, NpcFacing.Down, 0, false);
            double wrapped = (seconds + offset) % cycle; if (wrapped < 0) wrapped += cycle;
            float t = (float)wrapped;
            int i = 0;
            while (i + 1 < legs.Length && t >= legs[i + 1].Start) i++;
            var leg = legs[i];
            float local = t - leg.Start;
            if (local < leg.Pause) return new NpcPose(leg.AX, leg.AY, NpcFacing.Down, 0, false);
            float d = (local - leg.Pause) * TilesPerSecond;
            int frame = (int)(local / FrameSeconds) % WalkFrames;
            if (d < leg.Seg1 || leg.Seg2 <= 0)
            {
                if (d > leg.Seg1) d = leg.Seg1;
                int sx = Math.Sign(leg.CX - leg.AX), sy = Math.Sign(leg.CY - leg.AY);
                return new NpcPose(leg.AX + sx * d, leg.AY + sy * d, FacingOf(sx, sy), frame, true);
            }
            d -= leg.Seg1; if (d > leg.Seg2) d = leg.Seg2;
            int tx = Math.Sign(leg.BX - leg.CX), ty = Math.Sign(leg.BY - leg.CY);
            return new NpcPose(leg.CX + tx * d, leg.CY + ty * d, FacingOf(tx, ty), frame, true);
        }

        private static NpcFacing FacingOf(int dx, int dy)
            => dx > 0 ? NpcFacing.Right : dx < 0 ? NpcFacing.Left : dy < 0 ? NpcFacing.Up : NpcFacing.Down;

        // 歩ける所 = そのファイルの土地で、占有(旗・道・建物)も追加の障害物(自然物)もないマス。
        // 空きマスがなければ、そのファイルの土地(旗のマスを含む。建物・自然物は除く)と、そこから RoadReach マス以内に
        // 4 近傍でつながる道のマスだけを歩く。壁・他のファイルの土地・建物・自然物は決して歩かない。
        public const int RoadReach = 2;

        public static List<MapNpc> Build(IReadOnlyDictionary<string, HashSet<long>> landByFile, MapOccupancy occupancy, ICollection<long> blocked = null,
            IReadOnlyCollection<long> roadCells = null)
        {
            var result = new List<MapNpc>();
            var files = new List<string>(landByFile.Keys); files.Sort(StringComparer.Ordinal);
            foreach (string file in files)
            {
                HashSet<long> roads = null;
                if (roadCells != null)
                {
                    // 他のファイルの土地は避ける。
                    roads = new HashSet<long>();
                    foreach (long key in roadCells)
                    {
                        bool others = false;
                        foreach (string other in files) if (other != file && landByFile[other].Contains(key)) { others = true; break; }
                        if (!others) roads.Add(key);
                    }
                }
                var npc = BuildOne(file, landByFile[file], occupancy, blocked, roads);
                if (npc != null) result.Add(npc);
            }
            return result;
        }

        public static MapNpc BuildOne(string file, HashSet<long> land, MapOccupancy occupancy, ICollection<long> blocked = null, ICollection<long> roadCells = null)
        {
            HashSet<long> area = land;
            bool free = true;   // true の間は占有のない土地のマスだけ。
            bool Walk(int x, int y)
            {
                long key = MapCell.Key(x, y);
                return area.Contains(key) && (!free || occupancy == null || occupancy.IsFree(x, y)) && (blocked == null || !blocked.Contains(key));
            }
            var cells = new List<long>();
            foreach (long key in area) if (Walk(MapCell.X(key), MapCell.Y(key))) cells.Add(key);
            if (cells.Count == 0)
            {
                // 旗だけの土地: 土地(旗のマス含む)と、そこからつながる近くの道のマス。
                free = false;
                area = new HashSet<long>();
                var frontier = new List<long>();
                foreach (long key in land)
                {
                    var kind = occupancy == null ? OccupyKind.None : occupancy.Kind(MapCell.X(key), MapCell.Y(key));
                    if (kind == OccupyKind.Building || kind == OccupyKind.Nature) continue;
                    if (blocked != null && blocked.Contains(key)) continue;
                    area.Add(key); frontier.Add(key);
                }
                if (area.Count == 0) return null;
                if (roadCells != null)
                    for (int step = 0; step < RoadReach; step++)
                    {
                        var next = new List<long>();
                        foreach (long key in frontier)
                        {
                            int fx = MapCell.X(key), fy = MapCell.Y(key);
                            for (int k = 0; k < 4; k++)
                            {
                                long nk = MapCell.Key(fx + (k == 0 ? 1 : k == 1 ? -1 : 0), fy + (k == 2 ? 1 : k == 3 ? -1 : 0));
                                if (land.Contains(nk) || area.Contains(nk) || !roadCells.Contains(nk) || (blocked != null && blocked.Contains(nk))) continue;
                                area.Add(nk); next.Add(nk);
                            }
                        }
                        frontier = next;
                    }
                foreach (long key in area) cells.Add(key);
            }
            if (cells.Count == 0) return null;
            cells.Sort();
            int hash = MapLayout.StableHash(file);
            long first = cells[hash % cells.Count];

            // 最初の経由点とつながっている空きマスだけを使う。
            var component = new List<long> { first };
            var seen = new HashSet<long> { first };
            for (int head = 0; head < component.Count; head++)
            {
                int cx = MapCell.X(component[head]), cy = MapCell.Y(component[head]);
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    long nk = MapCell.Key(nx, ny);
                    if (Walk(nx, ny) && seen.Add(nk)) component.Add(nk);
                }
            }
            component.Sort();

            var waypoints = new List<long> { first };
            var used = new HashSet<long> { first };
            for (int i = 1; i < MaxWaypoints && waypoints.Count < component.Count; i++)
            {
                int start = MapLayout.StableHash(file + "#" + i) % component.Count;
                for (int probe = 0; probe < component.Count; probe++)
                {
                    long candidate = component[(start + probe) % component.Count];
                    if (used.Contains(candidate) || !TryLeg(waypoints[waypoints.Count - 1], candidate, Walk, out _)) continue;
                    waypoints.Add(candidate); used.Add(candidate); break;
                }
            }
            // 最後の経由点から最初へ戻れるようにする。戻れなければ最後の経由点を外す。
            while (waypoints.Count > 1 && !TryLeg(waypoints[waypoints.Count - 1], waypoints[0], Walk, out _)) waypoints.RemoveAt(waypoints.Count - 1);

            var legs = new List<Leg>();
            if (waypoints.Count > 1)
                for (int i = 0; i < waypoints.Count; i++)
                {
                    TryLeg(waypoints[i], waypoints[(i + 1) % waypoints.Count], Walk, out var leg);
                    leg.Pause = MinPause + (MapLayout.StableHash(file + "@" + i) % 2001) / 1000f * ((MaxPause - MinPause) / 2f);
                    legs.Add(leg);
                }
            return new MapNpc(file, waypoints, legs);
        }

        // a から b へ、横→縦(だめなら縦→横)の道筋が、歩ける所だけを通るか。
        private static bool TryLeg(long a, long b, Func<int, int, bool> walk, out Leg leg)
        {
            int ax = MapCell.X(a), ay = MapCell.Y(a), bx = MapCell.X(b), by = MapCell.Y(b);
            leg = default;
            if (ax == bx && ay == by) return false;
            if (Clear(ax, ay, bx, ay, walk) && Clear(bx, ay, bx, by, walk)) { Fill(ref leg, ax, ay, bx, ay, bx, by); return true; }
            if (Clear(ax, ay, ax, by, walk) && Clear(ax, by, bx, by, walk)) { Fill(ref leg, ax, ay, ax, by, bx, by); return true; }
            return false;
        }
        private static void Fill(ref Leg leg, int ax, int ay, int cx, int cy, int bx, int by)
        {
            leg.AX = ax; leg.AY = ay; leg.CX = cx; leg.CY = cy; leg.BX = bx; leg.BY = by;
            leg.Seg1 = Math.Abs(cx - ax) + Math.Abs(cy - ay); leg.Seg2 = Math.Abs(bx - cx) + Math.Abs(by - cy);
        }
        private static bool Clear(int x0, int y0, int x1, int y1, Func<int, int, bool> walk)
        {
            int sx = Math.Sign(x1 - x0), sy = Math.Sign(y1 - y0), x = x0, y = y0;
            while (true)
            {
                if (!walk(x, y)) return false;
                if (x == x1 && y == y1) return true;
                x += sx; y += sy;
            }
        }
    }
}
