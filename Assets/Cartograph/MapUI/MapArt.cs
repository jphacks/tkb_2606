using System;
using UnityEngine;

namespace Cartograph.MapUI
{
    // 地図に使うドット絵。Resources/MapArt から読む(取り込み設定は Art/Editor/PixelArtImporter.cs)。
    // 素材が読めない時は各配列が空になり、呼び出し側は色つきの四角で代用する。
    public sealed class MapArt
    {
        public const int AnimFrames = 8;
        static readonly string[] FlagColors = { "black", "green", "navy", "pink", "purple", "red", "sky", "teal", "white", "yellow" };
        static readonly string[] FlowerNames = { "joy", "anger", "sorrow", "pleasure" };
        static MapArt instance;
        public static MapArt Instance => instance ?? (instance = new MapArt());

        public readonly Sprite[] Wall, Ground, Border, BorderInner;
        public readonly Sprite[][] Flags, Flowers;
        public readonly Sprite[] Withered;
        public readonly Sprite[] FogAnim, FogTile, Marks, Collapse, PathPlain, OverlayPremise, OverlayContrast, OverlayExample;

        // ビルの部品。屋上・中階(a と b を交互に積む)・1階。幅は地面で占めるマス数。
        public sealed class TowerArt
        {
            public readonly Sprite Roof, MidA, MidB, Ground;
            public TowerArt(Sprite roof, Sprite midA, Sprite midB, Sprite ground) { Roof = roof; MidA = midA; MidB = midB; Ground = ground; }
            public bool Complete => Roof != null && MidA != null && MidB != null && Ground != null;
        }
        // 幅 1・2・3 マスごとに、見た目の違うビルを並べる。
        static readonly string[][] TowerNames =
        {
            new[] { "stone_tower", "cottage_tower" },
            new[] { "tile_shop", "timber_inn" },
            new[] { "factory", "modern_office" },
        };
        public readonly Sprite Hut, House, Lodge3x2, Lodge3x3;
        public readonly TowerArt[][] Towers;
        // 自然物の絵。NatureKind の値で引く。読めないものは null。
        static readonly string[] NatureNames =
        {
            "nature_grass_tuft", "nature_sprout", "nature_bush", "nature_bush_berry", "nature_rock",
            "nature_mushroom_brown", "nature_mushroom_red", "nature_stump",
            "nature_tree_pine_s", "nature_tree_pine_l", "nature_tree_round_s", "nature_tree_round_l",
        };
        public readonly Sprite[] Nature;
        // 人物(NPC)。CharacterNames と同じ並び。シートは 32x48 のコマ 4列x4行: 行 = 下・上・左・右の向き、列 = 歩きの 4 コマ。足元が基準。読めなければ空配列。
        public const int CharacterFrames = 4;
        public enum Facing { Down, Up, Left, Right }
        public static readonly string[] CharacterNames = { "girl_twintail", "boy_short", "girl_long_black", "boy_cap", "woman_apron", "man_farmer", "elder_man", "elder_woman" };
        public readonly Sprite[][] Characters;

        MapArt()
        {
            Nature = new Sprite[NatureNames.Length];
            for (int i = 0; i < NatureNames.Length; i++) Nature[i] = Single("Nature/" + NatureNames[i]);
            Characters = new Sprite[CharacterNames.Length][];
            for (int i = 0; i < CharacterNames.Length; i++) Characters[i] = Load("Characters/npc_" + CharacterNames[i]) ?? new Sprite[0];
            Hut = Single("Buildings/building_hut_1x1");
            House = Single("Buildings/building_house_2x2");
            Lodge3x2 = Single("Buildings/building_lodge_3x2");
            Lodge3x3 = Single("Buildings/building_lodge_3x3");
            Towers = new TowerArt[TowerNames.Length][];
            for (int w = 0; w < TowerNames.Length; w++)
            {
                var list = new System.Collections.Generic.List<TowerArt>();
                foreach (string name in TowerNames[w])
                {
                    string p = "Buildings/building_" + name + "_";
                    var tower = new TowerArt(Single(p + "roof"), Single(p + "mid_a"), Single(p + "mid_b"), Single(p + "ground"));
                    if (tower.Complete) list.Add(tower);
                }
                Towers[w] = list.ToArray();
            }
            FogAnim = Load("Fog/anim_fog");
            FogTile = Load("Fog/tile_fog");
            Marks = new[] { Single("Marks/mark_noreason"), Single("Marks/mark_broken"), Single("Marks/mark_isolated") };
            Collapse = Load("Collapse/anim_wall_collapse");
            PathPlain = Load("Tiles/Path/path_plain");
            OverlayPremise = Load("Tiles/Path/overlay_premise");
            OverlayContrast = Load("Tiles/Path/overlay_contrast");
            OverlayExample = Load("Tiles/Path/overlay_example");
            Wall = Load("Tiles/Brick/tile_wall");
            Ground = Load("Tiles/Grass/tile_ground");
            Border = Load("Tiles/Grass/tile_border");
            BorderInner = Load("Tiles/Grass/tile_border_inner");
            Flags = new Sprite[FlagColors.Length][];
            for (int i = 0; i < FlagColors.Length; i++) Flags[i] = Load("Flags/node_flag_" + FlagColors[i] + "_anim");
            Flowers = new Sprite[FlowerNames.Length][];
            Withered = new Sprite[FlowerNames.Length];
            for (int i = 0; i < FlowerNames.Length; i++)
            {
                Flowers[i] = Load("Flowers/Anim/flower_" + FlowerNames[i] + "_anim");
                var single = Resources.Load<Sprite>("MapArt/Flowers/Withered/flower_" + FlowerNames[i] + "_withered");
                Withered[i] = single;
            }
        }

        static Sprite Single(string path) => Resources.Load<Sprite>("MapArt/" + path);

        // 切り分けたコマを、左上から右へ(番号順)に並べて返す。
        static Sprite[] Load(string path)
        {
            var all = Resources.LoadAll<Sprite>("MapArt/" + path);
            Array.Sort(all, (a, b) => Index(a.name).CompareTo(Index(b.name)));
            return all;
        }

        static int Index(string name)
        {
            int underscore = name.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(name.Substring(underscore + 1), out int n) ? n : 0;
        }

        public bool HasTiles => Wall.Length > 0 && Ground.Length > 0 && Border.Length >= 16;
        public bool HasPaths => PathPlain.Length >= 16;
        public Sprite[] FlagFor(int hash) => Flags[hash % Flags.Length];

        // 建物の絵。足りない種類は null / 空を返し、呼び出し側はその建物を描かない。
        public Sprite Fixed(Cartowords.MapPresentation.BuildingShape shape)
        {
            switch (shape)
            {
                case Cartowords.MapPresentation.BuildingShape.Hut: return Hut;
                case Cartowords.MapPresentation.BuildingShape.House: return House;
                case Cartowords.MapPresentation.BuildingShape.Lodge3x2: return Lodge3x2;
                case Cartowords.MapPresentation.BuildingShape.Lodge3x3: return Lodge3x3;
                default: return null;
            }
        }
        // 幅(1〜3)のビルのうち、固有名詞ごとの値で1つ選ぶ。その幅に完成したものがなければ null。
        public TowerArt TowerFor(int width, int hash)
        {
            if (width < 1 || width > Towers.Length || Towers[width - 1].Length == 0) return null;
            var list = Towers[width - 1];
            return list[hash / 3 % list.Length];
        }
    }
}
