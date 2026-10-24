using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    // Scratch field: never bound to the workspace or its durable edit history.
    public sealed class M0ProbePanel : VisualElement
    {
        private readonly TextField scratch;
        private readonly Label metrics;
        private int changes;

        public M0ProbePanel()
        {
            style.paddingTop = 8; style.paddingBottom = 8;
            Add(new Label("M0検証欄（ノート・履歴には保存しません）"));
            Add(new Label("ここで日本語変換と1万字での入力を確認。貼り付けは本エディターで確認してください。"));
            scratch = new TextField { multiline = true, name = "m0-scratch" };
            scratch.style.height = 140;
            scratch.verticalScrollerVisibility = ScrollerVisibility.Auto;
            Add(new Button(LoadTenThousand) { text = "検証欄に1万字をセット" });
            Add(scratch);
            metrics = new Label("入力遅延とFPSは未計測。文字数と変更回数だけを表示します。");
            metrics.style.whiteSpace = WhiteSpace.Normal; Add(metrics);
            scratch.RegisterValueChangedCallback(evt =>
            {
                int length = evt.newValue.Length;
                changes++;
                metrics.text = $"{length:N0} UTF-16単位 / 変更{changes}回。描画・IME遅延は要実機確認。";
            });
        }

        private void LoadTenThousand()
        {
            scratch.SetValueWithoutNotify(new string('あ', 10000));
            changes = 0;
            metrics.text = "1万字セット済み。追加入力・範囲選択・スクロール・IME変換を確認してください。";
            scratch.Focus();
        }
    }
}
