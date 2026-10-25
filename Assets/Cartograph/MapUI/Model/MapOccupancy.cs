using System.Collections.Generic;

namespace Cartowords.MapPresentation
{
    // マスを何が占めているか。旗・花・目印(拠点の中心 3x3)、道、建物、自然物の順に予約し、先に予約したものが優先される。
    public enum OccupyKind { None, Flag, Path, Building, Nature }

    public sealed class MapOccupancy
    {
        private readonly Dictionary<long, OccupyKind> cells = new Dictionary<long, OccupyKind>();
        public int Count => cells.Count;

        public OccupyKind Kind(int x, int y) => cells.TryGetValue(MapCell.Key(x, y), out var kind) ? kind : OccupyKind.None;
        public bool IsFree(int x, int y) => !cells.ContainsKey(MapCell.Key(x, y));

        // 空いていれば予約して true。すでに何かが占めていれば何も変えずに false。
        public bool Reserve(int x, int y, OccupyKind kind)
        {
            if (kind == OccupyKind.None || cells.ContainsKey(MapCell.Key(x, y))) return false;
            cells[MapCell.Key(x, y)] = kind; return true;
        }

        // 中心のまわり radius マス(正方形)を予約する。旗と花と目印の置き場に使う。
        public void ReserveAround(long center, int radius, OccupyKind kind)
        {
            int cx = MapCell.X(center), cy = MapCell.Y(center);
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
                Reserve(cx + dx, cy + dy, kind);
        }

        public void ReserveAll(IEnumerable<long> keys, OccupyKind kind)
        {
            foreach (long key in keys) Reserve(MapCell.X(key), MapCell.Y(key), kind);
        }

        // 全拠点の中心 3x3 と道のマスを予約した占有マップ。
        public static MapOccupancy ForNodes(IEnumerable<long> nodeCells, IEnumerable<long> pathCells)
        {
            var occupancy = new MapOccupancy();
            foreach (long center in nodeCells) occupancy.ReserveAround(center, 1, OccupyKind.Flag);
            if (pathCells != null) occupancy.ReserveAll(pathCells, OccupyKind.Path);
            return occupancy;
        }
    }
}
