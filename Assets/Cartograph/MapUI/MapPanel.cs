using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using UnityEngine;
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
            // 関連ノートの候補(点線)を選んだ時の「つなぐ / 却下」。
            candidateBar.AddToClassList("map-candidate-bar"); candidateBar.style.display = DisplayStyle.None;
            candidateBar.Add(new Button(() => ResolveCandidate(true)) { text = "つなぐ", tooltip = "このノートどうしを関連づけます(リンクを書き足します)" });
            candidateBar.Add(new Button(() => ResolveCandidate(false)) { text = "却下", tooltip = "この候補はもう提案しません" });
            Add(candidateBar);
            canvas.CandidateSelected += (candidate, at) =>
            {
                shownCandidate = candidate; candidateBar.style.display = isPreview ? DisplayStyle.None : DisplayStyle.Flex;
                PlaceCandidateBar(at);
                info.text = "関連の候補: " + candidate.FromNote + " と " + candidate.ToNote + " / " + candidate.Reason;
            };
            canvas.NodeSelected += node => { HideCandidate(); info.text = node.Title + " / " + node.TypedChars + " 文字 / ダブルクリックで開く"; };
            canvas.LayoutChanged += layout => { if (!isPreview) LayoutChanged?.Invoke(layout); };
            canvas.MarkSelected += message => { HideCandidate(); info.text = message; };
            canvas.BuildingSelected += (building, node) =>
            {
                HideCandidate();
                info.text = building.Name + " / 「" + node.Title + "」に " + building.Count + " 回 / ダブルクリックで開く";
            };
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
        private readonly VisualElement candidateBar = new VisualElement();
        private MapCandidate shownCandidate;
        public event Action<MapCandidate> CandidateAccepted;
        public event Action<MapCandidate> CandidateRejected;
        // クリックした位置の右上に出す。パネルからはみ出す時は内側に寄せる。大きさは、出した直後の配置が済むまで分からないので、見積もりも使う。
        private const float BarGap = 10f, EstimatedBarWidth = 150f, EstimatedBarHeight = 40f;
        private void PlaceCandidateBar(Vector2 at)
        {
            candidateBar.style.bottom = StyleKeyword.Auto;
            float width = float.IsNaN(candidateBar.resolvedStyle.width) || candidateBar.resolvedStyle.width < 1 ? EstimatedBarWidth : candidateBar.resolvedStyle.width;
            float height = float.IsNaN(candidateBar.resolvedStyle.height) || candidateBar.resolvedStyle.height < 1 ? EstimatedBarHeight : candidateBar.resolvedStyle.height;
            float x = at.x + BarGap, y = at.y - height - BarGap;
            if (layout.width > 1) x = Mathf.Clamp(x, 4, Mathf.Max(4, layout.width - width - 4));
            if (layout.height > 1) y = Mathf.Clamp(y, 4, Mathf.Max(4, layout.height - height - 4));
            candidateBar.style.left = x; candidateBar.style.top = y;
        }
        private void HideCandidate()
        { shownCandidate = null; candidateBar.style.display = DisplayStyle.None; canvas.ClearCandidateSelection(); }
        private void ResolveCandidate(bool accept)
        {
            var candidate = shownCandidate; HideCandidate();
            if (candidate == null) return;
            if (accept) CandidateAccepted?.Invoke(candidate); else CandidateRejected?.Invoke(candidate);
        }
        public void Present(MapSnapshot snapshot)
        {
            // 新しい解析結果に、選んでいた候補がなければ(承認・却下の直後など)、操作欄を閉じる。
            if (shownCandidate != null && (snapshot == null || !snapshot.Candidates.Any(c => c.Key == shownCandidate.Key))) HideCandidate();
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
        private static MapSnapshot Sample() => MapSample.Build();
    }
}
