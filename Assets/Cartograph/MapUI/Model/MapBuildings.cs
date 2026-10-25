using System;
using System.Collections.Generic;
using System.Linq;

namespace Cartowords.MapPresentation
{
    // 建物の描画用データ。固有名詞ノートの別名が、ある拠点に出現したことを表す。
    public sealed class MapBuilding
    {
        public string Node { get; }
        /// <summary>固有名詞ノートのID(ダブルクリックで開くファイル)。</summary>
        public string File { get; }
        /// <summary>表示名(ノート名)。</summary>
        public string Name { get; }
        public int Strong { get; }
        public int Medium { get; }
        public int Weak { get; }
        public int Count => Strong + Medium + Weak;
        public MapBuilding(string node, string file, string name, int strong, int medium, int weak)
        {
            if (string.IsNullOrEmpty(node) || string.IsNullOrEmpty(file) || strong < 0 || medium < 0 || weak < 0 || strong + medium + weak == 0)
                throw new ArgumentException("Invalid rendering building.");
            Node = node; File = file; Name = name ?? ""; Strong = strong; Medium = medium; Weak = weak;
            int slash = file.LastIndexOf('/'); if (string.IsNullOrEmpty(Name)) Name = slash < 0 ? file : file.Substring(slash + 1);
        }
    }

    // 点数と段階の設定(仮の値)。Resources/MapConfig/buildings.json で上書きできる(JsonUtility で読むため、公開フィールドにしてある)。
    [Serializable]
    public sealed class BuildingSettings
    {
        public int StrongWeight = 3, MediumWeight = 2, WeakWeight = 1;
        // 点数がこの値以上なら、その段階以上の建物になる。
        public int HouseFrom = 3, LodgeFrom = 8, LodgeBigFrom = 15, TowerFrom = 25;
        // ビルは TowerFrom から、この点数ごとに中階が1つ増える。中階は最大 MaxFloors。
        public int FloorStep = 10, MaxFloors = 6;
        // 1つの拠点に建てる建物の最大数(点数の高い順)。
        public int MaxPerNode = 6;

        public static BuildingSettings Default => new BuildingSettings();

        // 不正な値(負、段階の逆転)を直して返す。設定ファイルが壊れていても描画が止まらないように。
        public BuildingSettings Normalized()
        {
            var s = (BuildingSettings)MemberwiseClone();
            s.StrongWeight = Math.Max(0, s.StrongWeight); s.MediumWeight = Math.Max(0, s.MediumWeight); s.WeakWeight = Math.Max(1, s.WeakWeight);
            s.HouseFrom = Math.Max(1, s.HouseFrom); s.LodgeFrom = Math.Max(s.HouseFrom, s.LodgeFrom);
            s.LodgeBigFrom = Math.Max(s.LodgeFrom, s.LodgeBigFrom); s.TowerFrom = Math.Max(s.LodgeBigFrom, s.TowerFrom);
            s.FloorStep = Math.Max(1, s.FloorStep); s.MaxFloors = Math.Max(1, s.MaxFloors); s.MaxPerNode = Math.Max(1, s.MaxPerNode);
            return s;
        }
    }

    // 建物が建つ演出。前の解析になかった建物だけを「新しい」とし、最初の地図と、前の地図と無関係な地図には演出を付けない。
    public static class BuildingAppearance
    {
        public const int Frames = 4;
        public static List<string> NewKeys(ICollection<string> previous, IEnumerable<string> current, bool incremental)
        {
            var result = new List<string>();
            if (!incremental || previous == null) return result;
            foreach (var key in current) if (!previous.Contains(key)) result.Add(key);
            return result;
        }
        // 建ち始めてからの経過コマ数 → 大きさの割合(0 は、まだ見えない)。始まる前(負)は見えず、最後は 1。
        public static float Grow(int age) => age < 0 ? 0 : age >= Frames ? 1 : (age + 1) / (float)(Frames + 1);
    }

    public enum BuildingShape { Hut, House, Lodge3x2, Lodge3x3, Tower }

    // 建物の大きさ。Width と Depth は地面で占めるマス数。ビルの Depth は 1 で、Floors(中階の数)だけ上へ伸びる。
    public readonly struct BuildingSize
    {
        public readonly BuildingShape Shape;
        public readonly int Score, Width, Depth, Floors;
        public BuildingSize(BuildingShape shape, int score, int width, int depth, int floors)
        { Shape = shape; Score = score; Width = width; Depth = depth; Floors = floors; }
    }

    public static class BuildingSizing
    {
        public static int Score(MapBuilding building, BuildingSettings settings)
            => building.Strong * settings.StrongWeight + building.Medium * settings.MediumWeight + building.Weak * settings.WeakWeight;

        // ビルの幅(1〜3マス)は、固有名詞ごとの安定した値で決める。
        public static BuildingSize Measure(MapBuilding building, BuildingSettings settings)
        {
            var s = (settings ?? BuildingSettings.Default).Normalized();
            int score = Score(building, s);
            if (score >= s.TowerFrom)
            {
                int floors = Math.Min(s.MaxFloors, 1 + (score - s.TowerFrom) / s.FloorStep);
                return new BuildingSize(BuildingShape.Tower, score, MapLayout.StableHash(building.File) % 3 + 1, 1, floors);
            }
            if (score >= s.LodgeBigFrom) return new BuildingSize(BuildingShape.Lodge3x3, score, 3, 3, 0);
            if (score >= s.LodgeFrom) return new BuildingSize(BuildingShape.Lodge3x2, score, 3, 2, 0);
            if (score >= s.HouseFrom) return new BuildingSize(BuildingShape.House, score, 2, 2, 0);
            return new BuildingSize(BuildingShape.Hut, score, 1, 1, 0);
        }
    }

    public static class MapCell
    {
        public static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        public static int X(long key) => (int)(key >> 32);
        public static int Y(long key) => (int)(uint)key;
    }

    public readonly struct BuildingPlacement
    {
        public readonly MapBuilding Building;
        public readonly BuildingSize Size;
        /// <summary>地面の占有範囲の左上のマス。</summary>
        public readonly int X, Y;
        public BuildingPlacement(MapBuilding building, BuildingSize size, int x, int y)
        { Building = building; Size = size; X = x; Y = y; }
    }

    // 建物を置くマスを決める。乱数は使わず、同じ入力なら必ず同じ結果になる。
    public static class BuildingPlacer
    {
        const int SearchRadius = 12;

        /// <summary>
        /// nodeCells: 拠点ID → 拠点のマス。各拠点の中心 3x3(旗と花の置き場)と pathCells(道のマス)は避ける。
        /// 拠点ごとに点数の高い順に、拠点に近いマスから探す。置けなかった建物は省く。
        /// 戻り値の並びは、拠点ID順、点数の高い順。
        /// </summary>
        public static List<BuildingPlacement> Place(IEnumerable<MapBuilding> buildings, IReadOnlyDictionary<string, long> nodeCells,
            ISet<long> pathCells, BuildingSettings settings)
            => PlaceOn(buildings, nodeCells, MapOccupancy.ForNodes(nodeCells.Values, pathCells), settings);

        /// <summary>占有マップを渡す版。置いた建物のマスは、占有マップに予約される。</summary>
        public static List<BuildingPlacement> PlaceOn(IEnumerable<MapBuilding> buildings, IReadOnlyDictionary<string, long> nodeCells,
            MapOccupancy occupied, BuildingSettings settings)
        {
            var s = (settings ?? BuildingSettings.Default).Normalized();
            var result = new List<BuildingPlacement>();
            var byNode = buildings.Where(b => nodeCells.ContainsKey(b.Node)).GroupBy(b => b.Node).OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var group in byNode)
            {
                long center = nodeCells[group.Key];
                var ordered = group.Select(b => (Building: b, Size: BuildingSizing.Measure(b, s)))
                    .OrderByDescending(p => p.Size.Score).ThenBy(p => p.Building.File, StringComparer.Ordinal).Take(s.MaxPerNode);
                foreach (var (building, size) in ordered)
                {
                    if (!TryFind(center, size, occupied, out int x, out int y)) continue;
                    for (int dx = 0; dx < size.Width; dx++)
                    for (int dy = 0; dy < size.Depth; dy++)
                        occupied.Reserve(x + dx, y + dy, OccupyKind.Building);
                    result.Add(new BuildingPlacement(building, size, x, y));
                }
            }
            return result;
        }

        // 拠点に近い順(建物の中心までの距離、次に上、次に左)に、空いている左上のマスを探す。
        static bool TryFind(long center, BuildingSize size, MapOccupancy occupied, out int x, out int y)
        {
            int cx = MapCell.X(center), cy = MapCell.Y(center);
            bool found = false; x = y = 0;
            long best = long.MaxValue;
            for (int top = cy - SearchRadius; top <= cy + SearchRadius; top++)
            for (int left = cx - SearchRadius; left <= cx + SearchRadius; left++)
            {
                // 距離は 2 倍した座標で比べる(中心が半マスにあたる場合があるため)。
                long dx = 2L * left + size.Width - 2L * cx - 1, dy = 2L * top + size.Depth - 2L * cy - 1;
                long distance = dx * dx + dy * dy;
                // 走査は上から下、左から右の順なので、同じ距離なら先に見つけた(より上・より左の)ほうを残す。
                if (distance >= best) continue;
                if (!IsFree(left, top, size, occupied)) continue;
                best = distance; x = left; y = top; found = true;
            }
            return found;
        }

        static bool IsFree(int left, int top, BuildingSize size, MapOccupancy occupied)
        {
            for (int dx = 0; dx < size.Width; dx++)
            for (int dy = 0; dy < size.Depth; dy++)
                if (!occupied.IsFree(left + dx, top + dy)) return false;
            return true;
        }
    }
}
