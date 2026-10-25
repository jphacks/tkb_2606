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
        // 実ノートの拠点の位置が変わった時(表示サンプルでは通知しない)。
        public event Action<IReadOnlyDictionary<string, MapPoint>> LayoutChanged;
        public void SetSavedLayout(IReadOnlyDictionary<string, MapPoint> saved) => canvas.SetSavedLayout(saved);
        // エディタのカーソルがある拠点。表示サンプルの間は覚えておくだけで、実ノートに戻ったときに示す。
        private string editorNode;
        public void SetEditorFocus(string nodeId)
        {
            editorNode = nodeId;
            if (!isPreview) canvas.SetEditorFocus(nodeId);
        }

        public MapPanel()
        {
            AddToClassList("map-panel"); style.flexGrow = 1; style.minWidth = 240;
            Add(canvas);
            var toolbar = new VisualElement(); toolbar.AddToClassList("map-tools");
            toolbar.Add(new Button(() => canvas.Zoom(1.25f)) { text = "＋", tooltip = "拡大" });
            toolbar.Add(new Button(() => canvas.Zoom(0.8f)) { text = "−", tooltip = "縮小" });
            toolbar.Add(new Button(canvas.Fit) { text = "全体", tooltip = "地図全体を表示" });
            Add(toolbar); caption.AddToClassList("map-caption"); caption.pickingMode = PickingMode.Ignore; Add(caption);
            info.AddToClassList("map-info"); info.pickingMode = PickingMode.Ignore; Add(info);
            preview = new Button(() =>
            {
                isPreview = !isPreview;
                canvas.SetSnapshot(isPreview ? Sample() : latest);
                if (!isPreview) canvas.SetEditorFocus(editorNode);
                preview.text = isPreview ? "サンプルを閉じる" : "表示サンプル";
                caption.text = isPreview ? "MAP VIEW / 表示サンプル（実ノートではありません）" : "MAP VIEW";
                info.text = isPreview ? "ドラッグで移動・ホイールで拡大縮小。サンプルから実ノートは開きません。" : "解析データ待ち";
            });
            preview.AddToClassList("map-preview-button"); Add(preview);
            canvas.NodeSelected += node => info.text = node.Title + " / " + node.TypedChars + " 文字 / ダブルクリックで開く";
            canvas.LayoutChanged += layout => { if (!isPreview) LayoutChanged?.Invoke(layout); };
            canvas.MarkSelected += message => info.text = message;
            canvas.BuildingSelected += (building, node) =>
                info.text = building.Name + " / 「" + node.Title + "」に " + building.Count + " 回 / ダブルクリックで開く";
            canvas.BuildingDoubleClicked += (building, node) =>
            {
                if (isPreview) { info.text = "表示サンプルです。実ノートの編集は行いません。"; return; }
                OpenNoteRequested?.Invoke(building.File, 1);
            };
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
            if (snapshot == null) editorNode = null;
            info.text = snapshot == null ? "解析データ待ち" : snapshot.Nodes.Count + " 拠点・" + snapshot.Roads.Count + " 本の線 / " +
                (snapshot.Roads.Count == 0 ? "「関連ノート」から相手を選び「線でつなぐ」で追加できます" : "ドラッグで移動・ダブルクリックで開く");
        }
        public void Waiting() { if (!isPreview) info.text = "解析データ待ち / 表示サンプルでマップ操作を確認できます"; }
        public void ShowError(string message) { info.text = message; }
        public void SetProjectOpen(bool open)
        {
            preview.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
            if (open && isPreview) Present(latest);
            if (!open && !isPreview) info.text = "保存先フォルダを開くと、ノートのマップを表示します。";
        }
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
                    new MapMark("sample-6", MarkKind.MissingReason, "この道の理由は、まだ書かれていません") }, flowers,
                buildings: new[] {
                    new MapBuilding("sample-0", "人物/田中", "田中", 1, 0, 0), new MapBuilding("sample-1", "人物/田中", "田中", 0, 1, 2),
                    new MapBuilding("sample-2", "人物/鈴木", "鈴木", 1, 2, 3), new MapBuilding("sample-3", "人物/鈴木", "鈴木", 2, 3, 5),
                    new MapBuilding("sample-5", "場所/図書館", "図書館", 4, 4, 9), new MapBuilding("sample-7", "場所/図書館", "図書館", 0, 0, 1),
                    new MapBuilding("sample-8", "用語/光合成", "光合成", 5, 5, 12) });
        }
    }
}
