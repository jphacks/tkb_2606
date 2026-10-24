using System;
using System.Collections.Generic;
using Cartowords.MapPresentation;
using UnityEngine.UIElements;

namespace Cartograph.MapUI
{
    public sealed class MapPanel : VisualElement
    {
        private readonly MapCanvas canvas = new MapCanvas();
        private readonly Label caption = new Label("MAP VIEW"), info = new Label("プロジェクトを開き、解析データを接続してください。");
        private readonly Button preview;
        private MapSnapshot latest;
        private bool isPreview;
        public event Action<string, int> OpenNoteRequested;
        public event Action<Emotion, UnityEngine.Color32> FlowerColorChanged;
        public MapPanel()
        {
            AddToClassList("map-panel"); style.flexGrow = 1; style.minWidth = 240;
            Add(canvas);
            var toolbar = new VisualElement(); toolbar.AddToClassList("map-tools");
            toolbar.Add(new Button(() => canvas.Zoom(1.25f)) { text = "＋", tooltip = "拡大" });
            toolbar.Add(new Button(() => canvas.Zoom(0.8f)) { text = "−", tooltip = "縮小" });
            toolbar.Add(new Button(canvas.Fit) { text = "全体", tooltip = "地図全体を表示" });
            toolbar.Add(new Button(canvas.FocusSelection) { text = "選択", tooltip = "選択した拠点を中央へ" });
            var palette = new VisualElement(); palette.AddToClassList("map-palette"); palette.style.display = DisplayStyle.None;
            toolbar.Add(new Button(() => palette.style.display = palette.style.display.value == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None)
                { text = "花", tooltip = "感情ごとの花の色" });
            Add(toolbar); caption.AddToClassList("map-caption"); caption.pickingMode = PickingMode.Ignore; Add(caption);
            info.AddToClassList("map-info"); info.pickingMode = PickingMode.Ignore; Add(info);
            preview = new Button(() =>
            {
                isPreview = !isPreview;
                canvas.SetSnapshot(isPreview ? Sample() : latest);
                preview.text = isPreview ? "サンプルを閉じる" : "表示サンプル";
                caption.text = isPreview ? "MAP VIEW / 表示サンプル（実ノートではありません）" : "MAP VIEW";
                info.text = isPreview ? "ドラッグで移動・ホイールで拡大縮小。サンプルから実ノートは開きません。" : "解析データ待ち";
            });
            preview.AddToClassList("map-preview-button"); Add(preview);
            palette.Add(new Label("花の色"));
            string[] names = { "喜", "怒", "哀", "楽" };
            var choices = new List<string> { "黄", "赤", "青", "橙", "紫", "青緑" };
            var colors = new[] { new UnityEngine.Color32(245,207,91,255), new UnityEngine.Color32(237,109,119,255),
                new UnityEngine.Color32(111,168,233,255), new UnityEngine.Color32(246,164,87,255),
                new UnityEngine.Color32(176,138,227,255), new UnityEngine.Color32(79,193,182,255) };
            for (int i = 0; i < names.Length; i++)
            {
                var emotion = (Emotion)i;
                var choice = new DropdownField(names[i], choices, i);
                choice.RegisterValueChangedCallback(evt =>
                {
                    var color = colors[choices.IndexOf(evt.newValue)]; canvas.SetFlowerColor(emotion, color);
                    FlowerColorChanged?.Invoke(emotion, color);
                }); palette.Add(choice);
            }
            var paletteHint = new Label("形は感情ごとに固定。色の変更はこの起動中に適用します。");
            paletteHint.style.whiteSpace = WhiteSpace.Normal; paletteHint.style.fontSize = 11; palette.Add(paletteHint); Add(palette);
            canvas.NodeSelected += node => info.text = node.Title + " / " + node.TypedChars + " 文字 / ダブルクリックで開く";
            canvas.MarkSelected += message => info.text = message;
            canvas.NodeDoubleClicked += node =>
            {
                if (isPreview) { info.text = "表示サンプルです。実ノートの編集は行いません。"; return; }
                OpenNoteRequested?.Invoke(node.File, node.Line);
            };
        }
        public void Present(MapSnapshot snapshot)
        {
            latest = snapshot; isPreview = false; preview.text = "表示サンプル";
            caption.text = "MAP VIEW"; canvas.SetSnapshot(snapshot);
            info.text = snapshot == null ? "解析データ待ち" : snapshot.Nodes.Count + " 拠点 / ドラッグで移動・ダブルクリックで開く";
        }
        public void Waiting() { if (!isPreview) info.text = "解析データ待ち / 表示サンプルでマップ操作を確認できます"; }
        public void SetFlowerColor(Emotion emotion, UnityEngine.Color32 color) => canvas.SetFlowerColor(emotion, color);
        private static MapSnapshot Sample()
        {
            var nodes = new List<MapNode>(); var roads = new List<MapRoad>(); var flowers = new List<MapFlower>();
            string[] titles = { "今日の学び", "光合成", "光とエネルギー", "観察メモ", "問い", "波の性質", "振動", "音", "気づき", "次の一歩", "日記", "考えごと" };
            for (int i = 0; i < titles.Length; i++)
            {
                nodes.Add(new MapNode("sample-" + i, i < 6 ? "大学/生物" : "個人/日記", titles[i], i + 1, i == 10 ? 0 : 140 + i * 65, i == 11));
                if (i > 0 && i < 11) roads.Add(new MapRoad("sample-" + (i - 1), "sample-" + i, (RoadKind)(i % 4), i % 3 == 0));
                if (i % 2 == 0) flowers.Add(new MapFlower("sample-" + i, i + 1, (Emotion)(i / 2 % 4), i == 8));
            }
            return new MapSnapshot(new MapRequest("preview", 1, new Dictionary<string, long>()), nodes, roads,
                new[] { new MapFog("sample-4", 5) }, new[] {
                    new MapMark("sample-4", MarkKind.BrokenLink, "この道の先は、まだ地図にありません"),
                    new MapMark("sample-11", MarkKind.Isolated, "ここは、まだどこともつながっていません"),
                    new MapMark("sample-6", MarkKind.MissingReason, "この道の理由は、まだ書かれていません") }, flowers);
        }
    }
}
