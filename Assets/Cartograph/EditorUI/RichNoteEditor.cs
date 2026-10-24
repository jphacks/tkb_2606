using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>Editable Markdown blocks with canonical raw text retained by the workspace.</summary>
    public sealed class RichNoteEditor : VisualElement
    {
        private sealed class Row
        {
            public TextField Field;
            public ReviewedTextBinding Binding;
            public BlockEditorWorkspace Workspace;
        }
        private readonly Func<bool> composing;
        private readonly Action<string> notice;
        private readonly ScrollView scroll;
        private readonly List<Row> rows = new List<Row>();
        private IReadOnlyList<MarkdownBlock> blocks = MarkdownBlockProjection.Parse("");
        private IReviewingEditorWorkspace source;
        private IReviewedNoteAccess access;
        private string id, raw = "";
        private TextUndoHistory history;
        private bool processing, replaying;
        private int cursor, selection;
        private int pointerGeneration;
        private bool pointerSelecting;
        public event Action<string, int, int> Changed;
        public bool Enabled => access != null && id != null;
        public bool CanUndo => Enabled && history != null && history.CanUndo;
        public bool CanRedo => Enabled && history != null && history.CanRedo;
        public bool HasPending => rows.Any(row => row.Binding.HasPending);

        public RichNoteEditor(Func<bool> composing, Action<string> notice)
        {
            this.composing = composing; this.notice = notice;
            AddToClassList("rich-note-editor");
            style.flexGrow = 1; style.minHeight = 100;
            scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 10; scroll.style.paddingRight = 10; scroll.style.paddingTop = 8;
            Add(scroll);
            RegisterCallback<PointerDownEvent>(_ => { pointerGeneration++; pointerSelecting = true; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => { pointerGeneration++; pointerSelecting = false; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => { pointerGeneration++; pointerSelecting = false; });
            RegisterCallback<PointerCaptureOutEvent>(_ => pointerSelecting = false);
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!Enabled || composing()) return;
                if ((evt.ctrlKey || evt.commandKey) && !evt.shiftKey && (evt.keyCode == KeyCode.Home || evt.keyCode == KeyCode.End))
                {
                    panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                    if (Submit(ChangeOrigin.Typing)) { int position = evt.keyCode == KeyCode.Home ? 0 : raw.Length; FocusRaw(position, position); }
                    return;
                }
                if ((evt.ctrlKey || evt.commandKey) && (evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y))
                {
                    panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                    Replay(evt.keyCode == KeyCode.Y || evt.shiftKey);
                }
            }, TrickleDown.TrickleDown);
        }
        public void Bind(IReviewingEditorWorkspace source, DocumentRecord document)
        {
            if (processing || composing()) return;
            if (source == null || document == null || !(source is IReviewedNoteAccess))
            {
                this.source = source; access = null; id = null; raw = "";
                rows.Clear(); scroll.Clear(); SetEnabled(false); return;
            }
            bool switched = id != document.id || !ReferenceEquals(this.source, source);
            this.source = source; access = (IReviewedNoteAccess)source; id = document.id;
            SetEnabled(true);
            if (!switched && HasPending) return;
            if (switched)
            {
                raw = document.markdown ?? "";
                history = new TextUndoHistory(raw, metadata: access.GetLabeledDocument(id)); cursor = selection = 0;
            }
            else if (raw != (document.markdown ?? "") && HasFocus())
            {
                // Workspace notifications can arrive inside a child binding's
                // ReviewDocument call. Reconcile only after it has returned.
                schedule.Execute(Reconcile);
                return;
            }
            else if (raw != (document.markdown ?? "") && !replaying)
                history.Record(document.markdown ?? "", cursor, selection, access.GetLabeledDocument(id));
            Render(document, switched);
        }
        private void Render(DocumentRecord document, bool force = false)
        {
            var next = MarkdownBlockProjection.Parse(document.markdown ?? "");
            bool rebuild = force || rows.Count != next.Count;
            if (!rebuild)
                for (int i = 0; i < next.Count; i++)
                    if (blocks[i].HeadingLevel != next[i].HeadingLevel) { rebuild = true; break; }
            bool focused = HasFocus();
            raw = document.markdown ?? ""; blocks = next;
            if (rebuild)
            {
                rows.Clear(); scroll.Clear();
                for (int i = 0; i < blocks.Count; i++)
                {
                    var field = new TextField { multiline = true, name = "rich-block-" + i };
                    field.textSelection.selectAllOnFocus = false;
                    field.textSelection.selectAllOnMouseUp = false;
                    field.AddToClassList("rich-note-block");
                    field.style.whiteSpace = WhiteSpace.Normal;
                    field.style.fontSize = blocks[i].HeadingLevel == 1 ? 32 : blocks[i].HeadingLevel == 2 ? 26 : blocks[i].HeadingLevel > 0 ? 22 : 18;
                    field.style.unityFontStyleAndWeight = blocks[i].HeadingLevel > 0 ? FontStyle.Bold : FontStyle.Normal;
                    field.style.minHeight = blocks[i].HeadingLevel > 0 ? 42 : 29;
                    field.style.marginBottom = blocks[i].HeadingLevel > 0 ? 8 : 2;
                    var input = field.Q<VisualElement>(className: "unity-text-field__input");
                    if (input != null)
                    {
                        input.style.backgroundColor = Color.clear;
                        input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 0;
                        input.style.whiteSpace = WhiteSpace.Normal;
                    }
                    var facade = new BlockEditorWorkspace(source, id, i);
                    int rowIndex = i;
                    field.RegisterCallback<KeyDownEvent>(evt => JoinBoundary(rowIndex, field, evt), TrickleDown.TrickleDown);
                    field.RegisterCallback<KeyDownEvent>(evt =>
                    {
                        if (!Enabled || composing() || evt.shiftKey || evt.ctrlKey || evt.commandKey) return;
                        int target = -1;
                        if (evt.keyCode == KeyCode.UpArrow && rowIndex > 0 && field.cursorPosition.y <= field.resolvedStyle.fontSize * 1.7f) target = rowIndex - 1;
                        if (evt.keyCode == KeyCode.DownArrow && rowIndex + 1 < rows.Count &&
                            field.cursorPosition.y >= field.resolvedStyle.height - field.resolvedStyle.fontSize * 2) target = rowIndex + 1;
                        int direction = evt.keyCode == KeyCode.UpArrow ? -1 : 1;
                        while (target >= 0 && target < blocks.Count && blocks[target].IsRelatedLink) target += direction;
                        if (target >= blocks.Count) return;
                        if (target < 0) return;
                        panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                        int column = field.cursorIndex;
                        if (!Submit(ChangeOrigin.Typing) || target >= blocks.Count) return;
                        int at = blocks[target].SourceStart + blocks[target].RawOffset(Math.Min(column, blocks[target].DisplayText.Length));
                        FocusRaw(at, at);
                    }, TrickleDown.TrickleDown);
                    var binding = new ReviewedTextBinding(field, composing, notice);
                    rows.Add(new Row { Field = field, Binding = binding, Workspace = facade });
                    scroll.Add(field);
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Binding.Bind(rows[i].Workspace, rows[i].Workspace.Current);
                rows[i].Field.style.display = blocks[i].IsRelatedLink ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (rebuild && focused) QueueFocus(cursor, selection);
        }
        private void QueueFocus(int start, int end)
        {
            int token = pointerGeneration; string document = id, content = raw;
            schedule.Execute(() =>
            {
                var focused = panel?.focusController.focusedElement as VisualElement;
                if (pointerSelecting || token != pointerGeneration || id != document || raw != content ||
                    (focused != null && !Contains(focused))) return;
                FocusRaw(start, end);
            });
        }
        private bool HasFocus()
        {
            var focused = panel?.focusController.focusedElement as VisualElement;
            return focused != null && Contains(focused);
        }
        private void CaptureSelection()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var focused = panel?.focusController.focusedElement as VisualElement;
                if (focused == null || !(ReferenceEquals(focused, rows[i].Field) || rows[i].Field.Contains(focused))) continue;
                cursor = blocks[i].SourceStart + blocks[i].RawOffset(Math.Min(rows[i].Field.cursorIndex, blocks[i].DisplayText.Length));
                selection = blocks[i].SourceStart + blocks[i].RawOffset(Math.Min(rows[i].Field.selectIndex, blocks[i].DisplayText.Length));
                history?.UpdateSelection(cursor, selection); break;
            }
        }
        public void Tick()
        {
            if (!Enabled || composing()) return;
            Reconcile();
            CaptureSelection();
            processing = true;
            try
            {
                foreach (var row in rows)
                {
                    row.Binding.Tick();
                    if ((Find().markdown ?? "") != raw) break;
                }
            }
            finally { processing = false; }
            Reconcile();
            Changed?.Invoke(raw, cursor, selection);
        }
        private void Reconcile()
        {
            if (!Enabled || composing() || processing) return;
            var current = Find();
            string updated = current.markdown ?? "";
            if (updated == raw) return;
            bool ownEdit = rows.Count == blocks.Count && rows.Where((row, index) => row.Field.value != blocks[index].DisplayText).Any();
            if (!ownEdit) CaptureSelection();
            var diff = TextDiff.Compute(raw, updated);
            if (ownEdit) cursor = selection = diff.Pos + diff.Inserted.Length;
            else
            {
                cursor = MarkdownBlockProjection.MapSelectionOffset(cursor, diff);
                selection = MarkdownBlockProjection.MapSelectionOffset(selection, diff);
            }
            if (!replaying) history.Record(updated, cursor, selection, access.GetLabeledDocument(id));
            Render(current);
            if (!ownEdit && HasFocus()) FocusRaw(cursor, selection);
            Changed?.Invoke(raw, cursor, selection);
        }
        public bool Submit(ChangeOrigin origin)
        {
            if (!Enabled) return true;
            if (composing()) return false;
            processing = true;
            try
            {
                foreach (var row in rows)
                {
                    string before = Find().markdown ?? "";
                    if (!row.Binding.Submit(origin)) return false;
                    string after = Find().markdown ?? "";
                    if (after != before)
                    {
                        var diff = TextDiff.Compute(before, after);
                        cursor = selection = diff.Pos + diff.Inserted.Length;
                        if (!replaying) history.Record(after, cursor, selection, access.GetLabeledDocument(id));
                        Render(Find());
                        Changed?.Invoke(raw, cursor, selection);
                        return true;
                    }
                }
                CaptureSelection(); return true;
            }
            catch (Exception ex) { notice(ex.Message); return false; }
            finally { processing = false; }
        }
        private DocumentRecord Find() => source.Documents.First(record => record.id == id);
        private void JoinBoundary(int index, TextField field, KeyDownEvent evt)
        {
            if (!Enabled || composing() || field.cursorIndex != field.selectIndex || evt.ctrlKey || evt.commandKey) return;
            int from, length;
            if (evt.keyCode == KeyCode.Backspace && field.cursorIndex == 0 && index > 0)
            {
                if (blocks[index - 1].IsRelatedLink) return;
                from = blocks[index - 1].SourceStart + blocks[index - 1].SourceLength;
                length = blocks[index].SourceStart + blocks[index].RawOffset(0) - from;
            }
            else if (evt.keyCode == KeyCode.Delete && field.cursorIndex == field.value.Length && index + 1 < blocks.Count)
            {
                if (blocks[index + 1].IsRelatedLink) return;
                from = blocks[index].SourceStart + blocks[index].SourceLength;
                length = blocks[index + 1].SourceStart + blocks[index + 1].RawOffset(0) - from;
            }
            else return;
            panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
            if (!Submit(ChangeOrigin.Typing)) return;
            var current = Find(); string text = current.markdown ?? "";
            if (from < 0 || length < 1 || from + length > text.Length) return;
            processing = true;
            try
            {
                var result = access.ReviewDocument(id, current.revision, new ChangeCandidate
                { NewText = text.Remove(from, length), SelStartBefore = from, SelEndBefore = from + length,
                    Evidence = new InputEvidence { Kind = EvidenceKind.Key } });
                if (!result.Approved) { notice(result.Reason); return; }
                cursor = selection = from;
                history.Record(Find().markdown, cursor, selection, access.GetLabeledDocument(id));
                Render(Find(), true); QueueFocus(from, from);
                Changed?.Invoke(raw, cursor, selection);
            }
            finally { processing = false; }
        }
        public void CompositionChanged(bool active) { foreach (var row in rows) row.Binding.CompositionChanged(active); }
        public void TextInput(char character) { foreach (var row in rows) row.Binding.TextInput(character); }
        public void FocusRaw(int start, int end)
        {
            if (!Enabled || rows.Count == 0) return;
            start = Math.Max(0, Math.Min(start, raw.Length)); end = Math.Max(0, Math.Min(end, raw.Length));
            int index = blocks.Count - 1;
            for (int i = 0; i < blocks.Count; i++)
                if (start <= blocks[i].SourceStart + blocks[i].SourceLength) { index = i; break; }
            var block = blocks[index];
            if (block.IsRelatedLink)
            {
                int next = index;
                while (next < blocks.Count && blocks[next].IsRelatedLink) next++;
                if (next >= blocks.Count)
                { next = index; while (next >= 0 && blocks[next].IsRelatedLink) next--; }
                if (next < 0) return;
                index = next; block = blocks[index]; start = end = block.SourceStart + block.RawOffset(0);
            }
            int a = DisplayOffset(block, start - block.SourceStart), b = DisplayOffset(block, end - block.SourceStart);
            rows[index].Field.Focus(); rows[index].Field.SelectRange(a, b);
            cursor = start; selection = end;
            scroll.ScrollTo(rows[index].Field);
        }
        private static int DisplayOffset(MarkdownBlock block, int offset)
        {
            for (int i = 0; i < block.DisplayText.Length; i++) if (block.RawOffset(i) >= offset) return i;
            return block.DisplayText.Length;
        }
        public void Replay(bool redo)
        {
            if (!Enabled || composing() || !Submit(ChangeOrigin.Typing) || (redo ? !CanRedo : !CanUndo)) return;
            var next = redo ? history.Redo() : history.Undo();
            var labels = (LabeledText)next.Metadata;
            var diff = TextDiff.Compute(raw, next.Text);
            replaying = processing = true;
            try
            {
                var result = access.ReviewDocument(id, Find().revision, new ChangeCandidate
                {
                    NewText = next.Text, SelStartBefore = cursor, SelEndBefore = selection,
                    Evidence = new InputEvidence { Kind = redo ? EvidenceKind.Redo : EvidenceKind.Undo, Text = diff.Inserted,
                        UndoLabels = labels.Slice(diff.Pos, diff.Inserted.Length) }
                });
                if (!result.Approved)
                { if (redo) history.Undo(); else history.Redo(); notice(result.Reason); return; }
                cursor = next.Cursor; selection = next.Selection; Render(Find(), true); FocusRaw(cursor, selection); Changed?.Invoke(raw, cursor, selection);
            }
            catch (Exception ex) { if (redo) history.Undo(); else history.Redo(); notice(ex.Message); }
            finally { replaying = processing = false; }
        }
    }
}
