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

        MapArt()
        {
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
    }
}
