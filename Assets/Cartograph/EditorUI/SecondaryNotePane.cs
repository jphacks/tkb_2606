using System;
using Cartograph.EditorUI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    public sealed class SecondaryNotePane : VisualElement
    {
        private readonly Label heading, info, pasteNotice;
        private readonly TextField body;
        private readonly Func<bool> composing;
        private readonly Func<string, long, string, ChangeOrigin, DocumentRecord> apply;
        private readonly Action closed;
        private readonly VisualElement tools;
        private string lastSubmitted = "";
        private long revision;
        private bool editable;
        private TextUndoHistory undo;
        private readonly ReviewedTextBinding reviewed;
        private readonly RichNoteEditor rich;
        private IReviewingEditorWorkspace reviewingWorkspace;
        public void SetReviewWorkspace(IReviewingEditorWorkspace workspace) { reviewingWorkspace = workspace; }
        public void ResetHistory() => rich.ResetHistory();
        public void CompositionChanged(bool active) { reviewed.CompositionChanged(active); rich.CompositionChanged(active); }
        public void TextInput(char character) { reviewed.TextInput(character); rich.TextInput(character); }
        public string DocumentId { get; private set; }
        public bool HasPending => rich.Enabled ? rich.HasPending : reviewed.Enabled ? reviewed.HasPending : DocumentId != null && body.value != lastSubmitted;

        public SecondaryNotePane(Func<bool> composing,
            Func<string, long, string, ChangeOrigin, DocumentRecord> apply, Action closed)
        {
            this.composing = composing; this.apply = apply; this.closed = closed;
            style.flexGrow = 1; style.flexBasis = 0; style.minWidth = 200;
            style.paddingLeft = 14; style.paddingRight = 14;
            style.paddingTop = 12; style.paddingBottom = 12;
            style.marginRight = 14; style.marginLeft = 14; style.marginBottom = 12;
            AddToClassList("editor-card");
            heading = new Label(); heading.style.whiteSpace = WhiteSpace.Normal; Add(heading);
            pasteNotice = new Label(); pasteNotice.style.display = DisplayStyle.None; Add(pasteNotice);
            Add(new Button(() => { if (!composing() && Submit(ChangeOrigin.Typing)) this.closed(); }) { text = "分割ノートを閉じる" });
            tools = new VisualElement(); tools.style.flexDirection = FlexDirection.Row; Add(tools);
            tools.Add(new Button(() => Replay(false)) { text = "↶ 戻る", tooltip = "直前の編集を取り消す（Ctrl+Z）" });
            tools.Add(new Button(() => Replay(true)) { text = "やり直し" });
            body = new TextField { multiline = true, name = "secondary-note-body" };
            body.AddToClassList("note-body");
            body.style.flexGrow = 1; body.style.minHeight = 100; body.style.fontSize = 16;
            body.verticalScrollerVisibility = ScrollerVisibility.Auto; Add(body);
            info = new Label(); info.style.whiteSpace = WhiteSpace.Normal; Add(info);
            reviewed = new ReviewedTextBinding(body, composing, text => { info.text = text; });
            rich = new RichNoteEditor(composing, text => { info.text = text; });
            Insert(IndexOf(body), rich);
            rich.Changed += (raw, cursor, selection) => { if (body.value != raw) body.SetValueWithoutNotify(raw); lastSubmitted = raw; };
            body.RegisterValueChangedCallback(_ => { if (!reviewed.Enabled && !composing()) Submit(ChangeOrigin.Typing); });
            body.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (composing() || reviewed.Enabled) return;
                bool modifier = evt.ctrlKey || evt.commandKey;
                if ((modifier && evt.keyCode == KeyCode.V) || (evt.shiftKey && evt.keyCode == KeyCode.Insert))
                { evt.StopImmediatePropagation(); Notice(); }
                else if (modifier && (evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y))
                { evt.StopImmediatePropagation(); Replay(evt.keyCode == KeyCode.Y || evt.shiftKey); }
            }, TrickleDown.TrickleDown);
            body.RegisterCallback<ValidateCommandEvent>(evt =>
            { if (!reviewed.Enabled && evt.commandName == "Paste") evt.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            body.RegisterCallback<ExecuteCommandEvent>(evt =>
            { if (!reviewed.Enabled && evt.commandName == "Paste") { evt.StopImmediatePropagation(); Notice(); } }, TrickleDown.TrickleDown);
            style.display = DisplayStyle.None;
        }

        private IVisualElementScheduledItem noticeTimer;
        private void Notice()
        {
            noticeTimer?.Pause(); pasteNotice.text = "外部からの貼り付けはできません";
            pasteNotice.style.display = DisplayStyle.Flex;
            noticeTimer = schedule.Execute(() => pasteNotice.style.display = DisplayStyle.None).StartingIn(2000);
        }
        private string DefaultInfo() => editable ? "右ペイン：保存は接続先の状態に従います" : "閲覧専用：本番の文書別編集APIは未接続です";

        public bool Open(DocumentRecord document, int line, bool canEdit)
        {
            if (composing() || !Submit(ChangeOrigin.Typing)) return false;
            int offset = TextLinePosition.Offset(document.markdown, line);
            DocumentId = document.id; revision = document.revision;
            editable = canEdit; lastSubmitted = document.markdown ?? "";
            tools.SetEnabled(editable);
            body.SetValueWithoutNotify(lastSubmitted); body.isReadOnly = !editable;
            undo = new TextUndoHistory(lastSubmitted);
            reviewed.Bind(reviewingWorkspace, document);
            rich.Bind(reviewingWorkspace, document);
            rich.SetEnabled(editable);
            rich.style.display = rich.Enabled ? DisplayStyle.Flex : DisplayStyle.None;
            body.style.display = rich.Enabled ? DisplayStyle.None : DisplayStyle.Flex;
            heading.text = document.title; info.text = DefaultInfo();
            style.display = DisplayStyle.Flex;
            body.schedule.Execute(() =>
            {
                if (rich.Enabled) { rich.FocusRaw(offset, offset); return; }
                body.Focus(); body.SelectRange(offset, offset);
                body.schedule.Execute(() =>
                {
                    var scroll = body.Q<ScrollView>();
                    if (scroll != null) scroll.scrollOffset = new Vector2(0, Mathf.Max(0, body.cursorPosition.y - scroll.contentViewport.layout.height * 0.3f));
                });
            });
            return true;
        }

        public void Refresh(DocumentRecord document)
        {
            if (rich.Enabled) { rich.Bind(reviewingWorkspace, document); rich.SetEnabled(editable); if (document != null) heading.text = document.title; return; }
            if (reviewed.Enabled) { reviewed.Bind(reviewingWorkspace, document); return; }
            if (document == null || document.id != DocumentId || composing()) return;
            // Never replace a pending local change with a save-state notification.
            if (body.value != lastSubmitted) return;
            heading.text = document.title; revision = document.revision;
            if (lastSubmitted == (document.markdown ?? "")) return;
            int cursor = body.cursorIndex, selection = body.selectIndex;
            lastSubmitted = document.markdown ?? ""; body.SetValueWithoutNotify(lastSubmitted);
            body.SelectRange(Math.Min(cursor, lastSubmitted.Length), Math.Min(selection, lastSubmitted.Length));
            undo = new TextUndoHistory(lastSubmitted);
        }

        public bool Submit(ChangeOrigin origin)
        {
            if (rich.Enabled) return rich.Submit(origin);
            if (reviewed.Enabled) return reviewed.Submit(origin);
            if (DocumentId == null || body.value == lastSubmitted) return true;
            if (!editable || composing()) return false;
            try
            {
                var updated = apply(DocumentId, revision, body.value, origin);
                revision = updated.revision; lastSubmitted = body.value;
                if (origin != ChangeOrigin.Undo && origin != ChangeOrigin.Redo)
                    undo.Record(lastSubmitted, body.cursorIndex, body.selectIndex);
                info.text = DefaultInfo(); return true;
            }
            catch (Exception ex) { info.text = "右ペインは未保存です：" + ex.Message; return false; }
        }

        public void Tick()
        {
            if (rich.Enabled) { rich.Tick(); return; }
            if (reviewed.Enabled) { reviewed.Tick(); return; }
            if (DocumentId == null || composing()) return;
            Submit(ChangeOrigin.Typing);
            undo.UpdateSelection(body.cursorIndex, body.selectIndex);
        }

        private void Replay(bool redo)
        {
            if (rich.Enabled) { rich.Replay(redo); return; }
            if (reviewed.Enabled) { reviewed.Replay(redo); return; }
            if (!editable || composing() || !Submit(ChangeOrigin.Typing)) return;
            if (redo ? !undo.CanRedo : !undo.CanUndo) return;
            var previous = undo.Current;
            var next = redo ? undo.Redo() : undo.Undo();
            body.SetValueWithoutNotify(next.Text);
            if (!Submit(redo ? ChangeOrigin.Redo : ChangeOrigin.Undo))
            {
                if (redo) undo.Undo(); else undo.Redo();
                next = previous; body.SetValueWithoutNotify(next.Text);
            }
            body.Focus(); body.SelectRange(next.Cursor, next.Selection);
        }

        public void Hide()
        {
            rich.Bind(null, null);
            DocumentId = null; style.display = DisplayStyle.None;
            reviewed.Bind(null, null);
        }
    }
}
