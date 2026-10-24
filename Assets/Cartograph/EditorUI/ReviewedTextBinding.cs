using System;
using System.Text;
using Cartograph.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>Collects input evidence; no unreviewed production write path.</summary>
    public sealed class ReviewedTextBinding
    {
        private readonly TextField field;
        private readonly Func<bool> composing;
        private readonly Action<string> notice;
        private readonly StringBuilder textInput = new StringBuilder();
        private IReviewedNoteAccess access;
        private IReviewingEditorWorkspace workspace;
        private string id, accepted = "", keyText;
        private long revision;
        private int beforeStart, beforeEnd;
        private bool imeEvidence;
        private TextUndoHistory history;
        public bool Enabled => access != null && id != null;
        public bool CanUndo => Enabled && history.CanUndo;
        public bool CanRedo => Enabled && history.CanRedo;
        public bool HasPending => Enabled && field.value != accepted;

        public ReviewedTextBinding(TextField field, Func<bool> composing, Action<string> notice)
        {
            this.field = field; this.composing = composing; this.notice = notice;
            field.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            field.RegisterCallback<ValidateCommandEvent>(evt =>
            {
                if (Enabled && IsClipboard(evt.commandName)) evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<ExecuteCommandEvent>(evt =>
            {
                if (!Enabled || !IsClipboard(evt.commandName)) return;
                evt.StopImmediatePropagation(); ClipboardCommand(evt.commandName);
            }, TrickleDown.TrickleDown);
        }
        private static bool IsClipboard(string command) => command == "Copy" || command == "Cut" || command == "Paste";
        private bool Focused
        {
            get
            {
                var focus = field.panel?.focusController.focusedElement as VisualElement;
                return focus != null && (ReferenceEquals(focus, field) || field.Contains(focus));
            }
        }

        public void Bind(IReviewingEditorWorkspace workspace, DocumentRecord document)
        {
            this.workspace = workspace; access = workspace as IReviewedNoteAccess;
            if (access == null || document == null) { id = null; return; }
            if (id != document.id)
            {
                id = document.id; accepted = document.markdown ?? ""; revision = document.revision;
                history = NewHistory(); field.SetValueWithoutNotify(accepted); ClearEvidence();
                beforeStart = beforeEnd = 0;
            }
            else if (!HasPending && !composing())
            {
                revision = document.revision;
                if (accepted != (document.markdown ?? ""))
                {
                    int cursor = field.cursorIndex, selection = field.selectIndex;
                    accepted = document.markdown ?? ""; field.SetValueWithoutNotify(accepted);
                    field.SelectRange(Math.Min(cursor, accepted.Length), Math.Min(selection, accepted.Length));
                    history = NewHistory();
                }
            }
        }
        private TextUndoHistory NewHistory() => new TextUndoHistory(accepted, metadata: access.GetLabeledDocument(id));
        public void CompositionChanged(bool active)
        { if (Enabled && Focused && active) imeEvidence = true; }
        public void TextInput(char character)
        { if (Enabled && Focused && (!char.IsControl(character) || character == '\r' || character == '\n' || character == '\t')) textInput.Append(character); }
        private void CaptureSelection()
        {
            beforeStart = field.cursorIndex; beforeEnd = field.selectIndex;
            history.UpdateSelection(beforeStart, beforeEnd);
        }
        private void OnKey(KeyDownEvent evt)
        {
            if (!Enabled) return;
            CaptureSelection();
            if (composing()) return;
            // Some input backends send Shift+Enter as a newline character with no
            // Return key code. Handle both forms, after the IME composition guard.
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter ||
                evt.character == '\r' || evt.character == '\n')
            {
                // Stopping propagation alone does not cancel TextField's default
                // action (Shift+Enter can commit/blur a field). Insert exactly one
                // reviewed newline and retain the editing focus and selection.
                field.panel?.focusController.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
                if (!Submit(ChangeOrigin.Typing)) return;
                CaptureSelection();
                int start = Math.Min(beforeStart, beforeEnd), end = Math.Max(beforeStart, beforeEnd);
                if (Apply(accepted.Remove(start, end - start).Insert(start, "\n"),
                    new InputEvidence { Kind = EvidenceKind.Key, Text = "\n" }, true))
                    field.SelectRange(start + 1, start + 1);
                return;
            }
            bool modifier = evt.ctrlKey || evt.commandKey;
            if ((modifier && evt.keyCode == KeyCode.V) || (evt.shiftKey && evt.keyCode == KeyCode.Insert))
            { evt.StopImmediatePropagation(); ClipboardCommand("Paste"); return; }
            if (modifier && (evt.keyCode == KeyCode.C || evt.keyCode == KeyCode.X))
            { evt.StopImmediatePropagation(); ClipboardCommand(evt.keyCode == KeyCode.C ? "Copy" : "Cut"); return; }
            if ((modifier && evt.keyCode == KeyCode.Z) || (modifier && evt.keyCode == KeyCode.Y))
            { evt.StopImmediatePropagation(); Replay(evt.keyCode == KeyCode.Y || evt.shiftKey); return; }
            keyText = evt.character == '\0' ? null : evt.character.ToString();
        }
        public void Tick()
        {
            if (!Enabled || composing()) return;
            Submit(ChangeOrigin.Typing);
            if (Focused) CaptureSelection();
            ClearEvidence();
        }
        public bool Submit(ChangeOrigin origin)
        {
            if (!Enabled || field.value == accepted) return true;
            if (composing()) return false;
            var kind = origin == ChangeOrigin.Format ? EvidenceKind.Format :
                imeEvidence ? EvidenceKind.ImeCommit : EvidenceKind.Key;
            string evidenceText = textInput.Length > 0 ? textInput.ToString() : keyText;
            evidenceText = evidenceText?.Replace("\r\n", "\n").Replace('\r', '\n');
            // No evidence: reject unexplained insertion (e.g. an external drop).
            if (kind == EvidenceKind.Key && evidenceText == null) kind = EvidenceKind.None;
            if (kind == EvidenceKind.Key && evidenceText != null)
            {
                var diff = TextDiff.Compute(accepted, field.value);
                if (diff.Inserted == evidenceText && evidenceText.Length > 1 &&
                    !(evidenceText.Length == 2 && char.IsSurrogatePair(evidenceText, 0)))
                {
                    int cursor = field.cursorIndex, selection = field.selectIndex;
                    string working = accepted.Remove(diff.Pos, diff.Deleted.Length);
                    int at = diff.Pos;
                    // Multiple keyboard events can arrive in one frame. Preserve key provenance
                    // by reviewing each captured code point rather than pretending it was IME.
                    for (int i = 0; i < evidenceText.Length;)
                    {
                        int length = char.IsHighSurrogate(evidenceText[i]) && i + 1 < evidenceText.Length ? 2 : 1;
                        string piece = evidenceText.Substring(i, length);
                        working = working.Insert(at, piece);
                        if (!Apply(working, new InputEvidence { Kind = EvidenceKind.Key, Text = piece }, true)) return false;
                        at += length; i += length;
                    }
                    field.SelectRange(cursor, selection); return true;
                }
            }
            return Apply(field.value, new InputEvidence { Kind = kind, Text = evidenceText }, true);
        }
        private bool Apply(string candidate, InputEvidence evidence, bool record)
        {
            try
            {
                var result = access.ReviewDocument(id, revision, new ChangeCandidate
                { NewText = candidate, SelStartBefore = beforeStart, SelEndBefore = beforeEnd, Evidence = evidence });
                if (!result.Approved)
                {
                    field.SetValueWithoutNotify(result.RevertText);
                    field.SelectRange(result.RevertSelStart, result.RevertSelEnd);
                    notice(result.Reason); ClearEvidence(); return false;
                }
                accepted = candidate; revision = workspace.Documents.FindRecord(id).revision;
                field.SetValueWithoutNotify(accepted);
                if (record) history.Record(accepted, field.cursorIndex, field.selectIndex, access.GetLabeledDocument(id));
                ClearEvidence(); return true;
            }
            catch (Exception ex) { notice("未保存です：" + ex.Message); return false; }
        }
        public void ClipboardCommand(string command)
        {
            if (!Enabled || composing() || !Submit(ChangeOrigin.Typing)) return;
            CaptureSelection();
            int start = Math.Min(beforeStart, beforeEnd), end = Math.Max(beforeStart, beforeEnd);
            try
            {
                if (command == "Copy" || command == "Cut")
                {
                    if (end == start) return;
                    GUIUtility.systemCopyBuffer = accepted.Substring(start, end - start);
                    workspace.NotifyCopied(id, start, end - start);
                    if (command == "Copy") return;
                    if (Apply(accepted.Remove(start, end - start), new InputEvidence { Kind = EvidenceKind.Key }, true))
                        field.SelectRange(start, start);
                }
                else
                {
                    string pasted = GUIUtility.systemCopyBuffer ?? "";
                    if (pasted.Length == 0) return;
                    string candidate = accepted.Remove(start, end - start).Insert(start, pasted);
                    if (Apply(candidate, new InputEvidence { Kind = EvidenceKind.Paste }, true))
                    {
                        int cursor = Math.Min(start + pasted.Length, accepted.Length);
                        field.SelectRange(cursor, cursor);
                    }
                }
            }
            catch (Exception ex) { notice(ex.Message); }
        }
        public void Replay(bool redo)
        {
            if (!Enabled || composing() || !Submit(ChangeOrigin.Typing)) return;
            if (redo ? !history.CanRedo : !history.CanUndo) return;
            var previous = history.Current;
            var next = redo ? history.Redo() : history.Undo();
            var labels = (LabeledText)next.Metadata;
            var diff = TextDiff.Compute(accepted, next.Text);
            var evidence = new InputEvidence { Kind = redo ? EvidenceKind.Redo : EvidenceKind.Undo,
                Text = diff.Inserted, UndoLabels = labels.Slice(diff.Pos, diff.Inserted.Length) };
            if (!Apply(next.Text, evidence, false))
            { if (redo) history.Undo(); else history.Redo(); next = previous; }
            field.SelectRange(next.Cursor, next.Selection);
        }
        private void ClearEvidence() { textInput.Clear(); keyText = null; imeEvidence = false; }
    }

    internal static class DocumentLookup
    {
        internal static DocumentRecord FindRecord(this System.Collections.Generic.IReadOnlyList<DocumentRecord> list, string id)
        {
            foreach (var document in list) if (document.id == id) return document;
            throw new InvalidOperationException("ノートが見つかりません。");
        }
    }
}
