using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>
    /// 分割のつまみの上と、つまみをドラッグしている間だけ、マウスカーソルを「↔」にする。
    /// USS の cursor は実行時の画面では効かないため、Cursor.SetCursor で絵を差し替える。
    /// 入った/出たのイベントには頼らず、画面全体のポインタの動きのたびに「いまつまみの上か」を求め直す
    /// (UI が一時的に無効になる、画面の外へ出る、などでイベントを取りこぼしても、↔ が残らない)。
    /// </summary>
    public sealed class SplitResizeCursor
    {
        private const int Size = 32;
        private const float GrabWidth = 10f;
        private static Texture2D texture;
        private readonly VisualElement root;
        private readonly List<VisualElement> handles = new List<VisualElement>();
        private bool dragging, shown;

        /// <summary>この分割自身のつまみ。入れ子の分割のつまみを拾わないよう、直下の子だけを見る。</summary>
        public static VisualElement HandleOf(TwoPaneSplitView split)
        {
            foreach (var child in split.hierarchy.Children())
                if (child.ClassListContains("unity-two-pane-split-view__dragline-anchor")) return child;
            return null;
        }

        public SplitResizeCursor(VisualElement root)
        {
            this.root = root;
            root.RegisterCallback<PointerMoveEvent>(evt =>
            {
                // ボタンが離されているのにドラッグ中のままなら、取りこぼしなので終わらせる。
                if (dragging && evt.pressedButtons == 0) dragging = false;
                Update(evt.position);
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(evt => { dragging = false; Update(evt.position); }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => { dragging = false; Show(false); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => { dragging = false; Show(false); });
        }

        public void Add(TwoPaneSplitView split)
        {
            var handle = HandleOf(split);
            if (handle == null) return;
            // 既定のつまみは 1px ほどで、つかみにくい。線の位置は変えずに、見えない当たり判定だけ広げる。
            handle.style.width = GrabWidth;
            handle.style.translate = new Translate(-GrabWidth / 2f, 0);
            handle.style.backgroundColor = Color.clear;
            handle.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) { dragging = true; Show(true); } }, TrickleDown.TrickleDown);
            handles.Add(handle);
        }

        private void Update(Vector2 position)
        {
            bool over = dragging;
            for (int i = 0; i < handles.Count && !over; i++)
                over = handles[i].panel != null && handles[i].worldBound.Contains(position);
            Show(over);
        }

        private void Show(bool on)
        {
            if (on == shown) return;
            shown = on;
            if (on) UnityEngine.Cursor.SetCursor(Texture(), new Vector2(Size / 2f, Size / 2f), CursorMode.Auto);
            else UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        // 白い「↔」に黒い縁取り。32x32 の絵を、実行時に作る(素材ファイルを増やさない)。
        private static Texture2D Texture()
        {
            if (texture != null) return texture;
            texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "SplitResizeCursor" };
            var clear = new Color32(0, 0, 0, 0);
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            bool Shape(int x, int y)
            {
                int cy = Size / 2, dy = Mathf.Abs(y - cy);
                if (dy <= 1 && x >= 5 && x <= Size - 6) return true;                       // 軸
                if (x >= 3 && x <= 11) return dy <= x - 3;                                  // 左の矢じり
                if (x >= Size - 12 && x <= Size - 4) return dy <= (Size - 4) - x;           // 右の矢じり
                return false;
            }
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (Shape(x, y)) { pixels[y * Size + x] = new Color32(255, 255, 255, 255); continue; }
                for (int ox = -1; ox <= 1 && pixels[y * Size + x].a == 0; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if ((ox == 0 && oy == 0) || !Shape(x + ox, y + oy)) continue;
                    pixels[y * Size + x] = new Color32(0, 0, 0, 255); break;
                }
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }
    }
}
