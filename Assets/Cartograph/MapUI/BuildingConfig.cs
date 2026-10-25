using Cartowords.MapPresentation;
using UnityEngine;

namespace Cartograph.MapUI
{
    // 建物の点数と段階の設定。Resources/MapConfig/buildings.json を読み、読めなければ既定値を使う。
    public static class BuildingConfig
    {
        static BuildingSettings loaded;
        public static BuildingSettings Settings => loaded ?? (loaded = Load());

        static BuildingSettings Load()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("MapConfig/buildings");
                if (asset != null) return JsonUtility.FromJson<BuildingSettings>(asset.text).Normalized();
            }
            catch (System.Exception) { }
            return BuildingSettings.Default;
        }
    }
}
