using Cartowords.MapPresentation;
using UnityEngine;

namespace Cartograph.MapUI
{
    // 自然物の密度と段階の設定。Resources/MapConfig/nature.json を読み、読めなければ既定値を使う。
    public static class NatureConfig
    {
        static NatureSettings loaded;
        public static NatureSettings Settings => loaded ?? (loaded = Load());

        static NatureSettings Load()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("MapConfig/nature");
                if (asset != null) return JsonUtility.FromJson<NatureSettings>(asset.text).Normalized();
            }
            catch (System.Exception) { }
            return NatureSettings.Default;
        }
    }
}
