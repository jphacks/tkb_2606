using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cartograph.Art.Editor
{
    // Art/ 以下のドット絵を、ぼかさない・圧縮しない・1マス32px の設定にそろえる。
    // シートは名前から1コマの大きさを決めて自動で切り分ける(コマ名は <ファイル名>_<番号>、左上から右へ)。
    public class PixelArtImporter : AssetPostprocessor
    {
        const string Root = "Assets/Cartograph/Art/";
        // 地図の描画が実行時に読む素材の複製(Resources.Load で読むため)。
        const string MapArtRoot = "Assets/Cartograph/MapUI/Resources/MapArt/";
        static readonly string[] Folders = { "Buildings/", "Nature/", "Flowers/", "Flags/", "Tiles/", "Fog/", "Marks/", "Collapse/" };
        const int PixelsPerUnit = 32;

        // 設定を変えたときは番号を上げる。上げると、既に取り込み済みの素材も取り込み直される。
        public override uint GetVersion() => 5;

        void OnPreprocessTexture()
        {
            if (!IsTarget(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            // 人物は足元(下辺中央)が基準。それ以外は中央。
            importer.spritePivot = assetPath.Contains("/Characters/") ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
            importer.maxTextureSize = 2048;

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            var cell = CellSize(assetPath, width, height);
            if (cell == null)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                return;
            }
            importer.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable CS0618
            importer.spritesheet = Slice(System.IO.Path.GetFileNameWithoutExtension(assetPath), width, height, cell.Value.x, cell.Value.y, assetPath.Contains("/Characters/"));
#pragma warning restore CS0618
        }

        static bool IsTarget(string path)
        {
            if (!path.EndsWith(".png")) return false;
            if (path.StartsWith(MapArtRoot)) return true;
            if (!path.StartsWith(Root)) return false;
            var rest = path.Substring(Root.Length);
            foreach (var folder in Folders) if (rest.StartsWith(folder)) return true;
            return false;
        }

        // 1コマの大きさ(幅, 高さ)。切り分けない一枚絵は null。
        static Vector2Int? CellSize(string path, int width, int height)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (path.Contains("/Flags/") && name.EndsWith("_anim")) return new Vector2Int(64, 64);
            if (path.Contains("/Flowers/Anim/")) return new Vector2Int(32, 64);
            if (name == "flower_all_32x64") return new Vector2Int(32, 64);
            if (name == "flower_all_withered_32x32") return new Vector2Int(32, 32);
            if (name.StartsWith("npc_")) return new Vector2Int(32, 48);
            if (name == "anim_fog" || name == "anim_wall_collapse" || name == "path_plain" || name.StartsWith("overlay_"))
                return new Vector2Int(32, 32);
            bool tileSheet = name == "tile_border" || name == "tile_border_inner" || name == "tile_ground" || name == "tile_wall";
            if (tileSheet && (width > 32 || height > 32)) return new Vector2Int(32, 32);
            return null;
        }

        static SpriteMetaData[] Slice(string name, int width, int height, int cw, int ch, bool bottom)
        {
            var list = new List<SpriteMetaData>();
            int cols = width / cw, rows = height / ch, index = 0;
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
                list.Add(new SpriteMetaData
                {
                    name = name + "_" + index++,
                    rect = new Rect(col * cw, height - (row + 1) * ch, cw, ch),
                    alignment = (int)(bottom ? SpriteAlignment.BottomCenter : SpriteAlignment.Center),
                    pivot = bottom ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f),
                });
            return list.ToArray();
        }
    }
}
