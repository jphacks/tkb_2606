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
        static readonly string[] Folders = { "Buildings/", "Nature/", "Flowers/", "Flags/", "Tiles/" };
        const int PixelsPerUnit = 32;

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
            importer.spritePivot = new Vector2(0.5f, 0.5f);
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
            importer.spritesheet = Slice(System.IO.Path.GetFileNameWithoutExtension(assetPath), width, height, cell.Value.x, cell.Value.y);
#pragma warning restore CS0618
        }

        static bool IsTarget(string path)
        {
            if (!path.StartsWith(Root) || !path.EndsWith(".png")) return false;
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
            bool tileSheet = name == "tile_border" || name == "tile_border_inner" || name == "tile_ground" || name == "tile_wall";
            if (tileSheet && (width > 32 || height > 32)) return new Vector2Int(32, 32);
            return null;
        }

        static SpriteMetaData[] Slice(string name, int width, int height, int cw, int ch)
        {
            var list = new List<SpriteMetaData>();
            int cols = width / cw, rows = height / ch, index = 0;
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
                list.Add(new SpriteMetaData
                {
                    name = name + "_" + index++,
                    rect = new Rect(col * cw, height - (row + 1) * ch, cw, ch),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                });
            return list.ToArray();
        }
    }
}
