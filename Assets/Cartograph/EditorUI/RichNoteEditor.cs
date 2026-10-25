using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.Core;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cartograph.EditorUI
{
    /// <summary>Editable Markdown blocks with canonical raw text retained by the workspace.</summary>
    public sealed class RichNoteEditor : VisualElement
    {
        private sealed class Row
        {
            public TextField Field;
            public Label Badge;
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
        private readonly NoteUndoHistoryCache noteHistories = new NoteUndoHistoryCache();
        private IReviewingEditorWorkspace historySource;
        private bool processing, replaying;
        private int cursor, selection;
        private int pointerGeneration;
        private bool pointerSelecting;
        private bool documentSelection, draggingDocument;
        private int dragAnchor;
        private readonly List<VisualElement> selectionPaint = new List<VisualElement>();
        private readonly VisualElement selectionLayer;
        private readonly VisualElement caret;
        private string caretText;
        private int caretIndex = -1;
        private TextField caretField;
        private float caretChangedAt;
        public event Action<string, int, int> Changed;
        public bool Enabled => access != null && id != null;
        public bool CanUndo => Enabled && history != null && history.CanUndo;
        public bool CanRedo => Enabled && history != null && history.CanRedo;
        public bool HasPending => rows.Any(row => row.Binding.HasPending);

        /// <summary>Call on project open/close and identifier-changing operations.</summary>
        public void ResetHistory()
        {
            noteHistories.Clear(); history = null; id = null;
        }

        public RichNoteEditor(Func<bool> composing, Action<string> notice)
        {
            this.composing = composing; this.notice = notice;
            AddToClassList("rich-note-editor");
            style.flexGrow = 1; style.minHeight = 100;
            scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 10; scroll.style.paddingRight = 10; scroll.style.paddingTop = 8;
            Add(scroll);
            selectionLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            selectionLayer.style.position = Position.Absolute;
            selectionLayer.style.left = selectionLayer.style.right = selectionLayer.style.top = selectionLayer.style.bottom = 0;
            selectionLayer.style.overflow = Overflow.Hidden;
            Add(selectionLayer);
            caret = new VisualElement { pickingMode = PickingMode.Ignore };
            caret.style.position = Position.Absolute; caret.style.width = 1.5f;
            caret.style.backgroundColor = new Color(.55f, .91f, .95f, 1);
            caret.style.display = DisplayStyle.None; selectionLayer.Add(caret);
            schedule.Execute(UpdateCaretAndIme).Every(16);
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (!Enabled || composing() || evt.button != 0) return;
                var target = evt.target as VisualElement;
                if (target != null && target.GetFirstAncestorOfType<Scroller>() != null) return;
                if (!Submit(ChangeOrigin.Typing)) return;
                documentSelection = false; ClearSelectionPaint();
                dragAnchor = HitOffset(evt.position);
                int row = BlockIndex(dragAnchor);
                if (row < 0 || row >= rows.Count) return;
                var block = blocks[row];
                int local = DisplayOffset(block, dragAnchor - block.SourceStart);
                rows[row].Field.Focus(); rows[row].Field.SelectRange(local, local);
                cursor = selection = dragAnchor;
                pointerGeneration++; pointerSelecting = draggingDocument = true;
                this.CapturePointer(evt.pointerId);
                // Own the gesture from the first event. Native text input would
                // otherwise capture it and clamp selection to its single row.
                panel?.focusController.IgnoreEvent(evt);
#pragma warning disable CS0618
                evt.PreventDefault(); // Also suppress compatibility mouse events on the row.
#pragma warning restore CS0618
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!Enabled || !pointerSelecting || evt.pressedButtons == 0) return;
                int at = HitOffset(evt.position);
                evt.StopImmediatePropagation();
                SelectDocument(dragAnchor, at);
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!draggingDocument) return;
                SelectDocument(dragAnchor, HitOffset(evt.position));
                draggingDocument = false; this.ReleasePointer(evt.pointerId);
                pointerSelecting = false; pointerGeneration++;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(_ => { pointerGeneration++; pointerSelecting = false; }, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => { pointerGeneration++; pointerSelecting = false; });
            RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                // TextField captures the pointer itself. Its capture transitions
                // bubble through this editor and must not cancel our drag.
                if (ReferenceEquals(evt.target, this))
                { pointerSelecting = false; draggingDocument = false; }
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!Enabled || composing()) return;
                bool mod = evt.ctrlKey || evt.commandKey;
                if (mod && evt.keyCode == KeyCode.S)
                {
                    panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                    SaveFromShortcut(); return;
                }
                if (mod && evt.keyCode == KeyCode.A)
                {
                    panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                    if (Submit(ChangeOrigin.Typing)) SelectDocument(0, raw.Length);
                    return;
                }
                if (mod && evt.shiftKey && (evt.keyCode == KeyCode.Home || evt.keyCode == KeyCode.End))
                {
                    panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
                    CaptureSelection();
                    SelectDocument(documentSelection ? selection : cursor, evt.keyCode == KeyCode.Home ? 0 : raw.Length);
                    return;
                }
                if (documentSelection && HandleDocumentKey(evt)) return;
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
            if (!ReferenceEquals(historySource, source))
            {
                noteHistories.Clear(); history = null; historySource = source;
            }
            bool switched = id != document.id || !ReferenceEquals(this.source, source) || history == null;
            this.source = source; access = (IReviewedNoteAccess)source; id = document.id;
            SetEnabled(true);
            if (!switched && HasPending) return;
            if (switched)
            {
                documentSelection = draggingDocument = false; ClearSelectionPaint();
                raw = document.markdown ?? "";
                history = noteHistories.GetOrCreate(id, raw, access.GetLabeledDocument(id));
                cursor = history.Current.Cursor; selection = history.Current.Selection;
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
                    #pragma warning disable CS0618
                    field.textSelection.cursorColor = Color.clear;
                    #pragma warning restore CS0618
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
                        input.style.fontSize = field.style.fontSize;
                        input.style.paddingTop = input.style.paddingBottom = 0;
                        input.style.minHeight = blocks[i].HeadingLevel == 1 ? 36 : blocks[i].HeadingLevel == 2 ? 30 : blocks[i].HeadingLevel > 0 ? 26 : 22;
                    }
                    // Set the actual text element's metrics before an empty row
                    // receives focus, so its caret never starts at theme-default size.
                    foreach (var textElement in field.Query<TextElement>().ToList())
                        textElement.style.fontSize = field.style.fontSize;
                    var facade = new BlockEditorWorkspace(source, id, i);
                    facade.ReplacementSelection = () => documentSelection ? Tuple.Create(cursor, selection) : null;
                    int rowIndex = i;
                    field.RegisterCallback<KeyDownEvent>(evt => InsertNewline(rowIndex, field, evt), TrickleDown.TrickleDown);
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
                    rows.Add(new Row { Field = field, Binding = binding, Workspace = facade, Badge = MakeBadge(field) });
                    scroll.Add(field);
                }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Binding.Bind(rows[i].Workspace, rows[i].Workspace.Current);
                rows[i].Field.style.display = blocks[i].IsRelatedLink ? DisplayStyle.None : DisplayStyle.Flex;
                rows[i].Field.style.marginTop = 0;
                rows[i].Field.style.marginBottom = blocks[i].HasSoftBreakAfter ? 0 : 13;
                rows[i].Field.style.minHeight = blocks[i].HeadingLevel == 1 ? 36 : blocks[i].HeadingLevel == 2 ? 30 : blocks[i].HeadingLevel > 0 ? 26 : 22;
                rows[i].Field.style.paddingTop = rows[i].Field.style.paddingBottom = 0;
                ApplyMarker(rows[i], blocks[i].Marker);
            }
            if (rebuild && focused) QueueFocus(cursor, selection);
        }
        /// <summary>A badge drawn over the left padding of a row; it replaces the hidden ==&gt; / &lt;-&gt; / ..&gt; characters.</summary>
        private static Label MakeBadge(TextField field)
        {
            var badge = new Label { name = "road-kind-badge", pickingMode = PickingMode.Ignore };
            badge.style.position = Position.Absolute;
            badge.style.left = 0; badge.style.top = 1;
            badge.style.fontSize = 12;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.paddingLeft = badge.style.paddingRight = 4;
            badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius =
                badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
            badge.style.display = DisplayStyle.None;
            field.Add(badge);
            return badge;
        }
        private static void ApplyMarker(Row row, Navigation.LinkMarkerKind kind)
        {
            if (kind == Navigation.LinkMarkerKind.None)
            {
                row.Badge.style.display = DisplayStyle.None;
                row.Field.style.paddingLeft = 0;
                row.Field.style.borderLeftWidth = 0;
                return;
            }
            Navigation.LinkMarker.Color(kind, out byte r, out byte g, out byte b);
            Color color = new Color32(r, g, b, 255);
            row.Badge.text = Navigation.LinkMarker.Icon(kind);
            row.Badge.style.color = color;
            row.Badge.style.backgroundColor = (Color)new Color32(r, g, b, 40);
            row.Badge.style.display = DisplayStyle.Flex;
            row.Field.style.paddingLeft = 76;
            row.Field.style.borderLeftWidth = 3;
            row.Field.style.borderLeftColor = color;
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
        private int BlockIndex(int offset)
        {
            for (int i = 0; i < blocks.Count; i++)
                if (offset <= blocks[i].SourceStart + blocks[i].SourceLength) return i;
            return blocks.Count - 1;
        }
        private int HitOffset(Vector2 world)
        {
            int row = -1; float distance = float.MaxValue;
            for (int i = 0; i < rows.Count; i++)
            {
                if (blocks[i].IsRelatedLink) continue;
                var bounds = rows[i].Field.worldBound;
                float d = world.y < bounds.yMin ? bounds.yMin - world.y : world.y > bounds.yMax ? world.y - bounds.yMax : 0;
                if (d < distance) { row = i; distance = d; }
            }
            if (row < 0) return 0;
            var field = rows[row].Field;
            var input = field.Q<TextElement>() as VisualElement ?? field;
            Vector2 local = input.WorldToLocal(world) - input.contentRect.position;
            float lineHeight = SelectionLineHeight(field);
            int best = 0; float nearest = float.MaxValue;
            for (int i = 0; i <= blocks[row].DisplayText.Length; i++)
            {
                Vector2 pos = field.textSelection.GetCursorPositionFromStringIndex(i);
                float vertical = local.y < pos.y - lineHeight ? pos.y - lineHeight - local.y : local.y > pos.y ? local.y - pos.y : 0;
                float d = vertical * 1000 + Mathf.Abs(pos.x - local.x);
                if (d < nearest) { nearest = d; best = i; }
            }
            return blocks[row].SourceStart + blocks[row].RawOffset(best);
        }
        private void ClearSelectionPaint()
        {
            foreach (var paint in selectionPaint) paint.RemoveFromHierarchy();
            selectionPaint.Clear();
        }
        private static float SelectionLineHeight(TextField field)
        {
            var text = field.Q<TextElement>();
            if (text != null)
            {
                float measured = text.MeasureTextSize("M", 0, VisualElement.MeasureMode.Undefined,
                    0, VisualElement.MeasureMode.Undefined).y;
                if (measured > 0 && !float.IsNaN(measured)) return measured;
            }
            return field.resolvedStyle.fontSize * 1.4f;
        }
        private void UpdateCaretAndIme()
        {
            caret.style.display = DisplayStyle.None;
            if (!Enabled || panel == null) return;
            var focused = panel.focusController.focusedElement as VisualElement;
            var field = rows.Select(row => row.Field).FirstOrDefault(f => focused != null && (focused == f || f.Contains(focused)));
            if (field == null) return;
            var text = field.Q<TextElement>(); if (text == null || text.layout.width <= 0) return;
            int at = Math.Max(0, Math.Min(field.cursorIndex, field.value.Length));
            Vector2 p = field.textSelection.GetCursorPositionFromStringIndex(at);
            float height = field.resolvedStyle.fontSize + 3;
            Vector2 top = selectionLayer.WorldToLocal(text.LocalToWorld(new Vector2(
                p.x + text.contentRect.x, p.y - SelectionLineHeight(field) + text.contentRect.y)));
            if (caretField != field || caretIndex != at || caretText != field.value)
            { caretField = field; caretIndex = at; caretText = field.value; caretChangedAt = Time.realtimeSinceStartup; }
            caret.style.left = top.x; caret.style.top = top.y; caret.style.height = height;
            bool visible = !documentSelection && field.cursorIndex == field.selectIndex &&
                (composing() || (Time.realtimeSinceStartup - caretChangedAt) % 1.06f < .53f);
            caret.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
#if ENABLE_INPUT_SYSTEM
            // Input System expects upper-left pixel coordinates. Position the
            // candidate window below the line, rather than over the typed text.
            var bounds = panel.visualTree.worldBound;
            if (Keyboard.current != null && bounds.width > 0 && bounds.height > 0)
            {
                Vector2 below = selectionLayer.LocalToWorld(new Vector2(top.x, top.y + height + 12));
                Keyboard.current.SetIMECursorPosition(new Vector2(
                    Mathf.Clamp((below.x - bounds.x) * Screen.width / bounds.width + 64, 0, Mathf.Max(0, Screen.width - 360)),
                    Mathf.Clamp((below.y - bounds.y) * Screen.height / bounds.height, 0, Screen.height - 1)));
            }
#endif
        }
        private void SelectDocument(int anchor, int end)
        {
            cursor = end; selection = anchor; documentSelection = anchor != end;
            ClearSelectionPaint();
            if (!documentSelection)
            {
                // A simple click is a caret placement, not a range to paint.
                // Do not clear every row's native selection to (0,0): Tick
                // would read that back and move the caret to the line start.
                FocusRaw(end, end);
                history?.UpdateSelection(cursor, selection);
                Changed?.Invoke(raw, cursor, selection);
                return;
            }
            int start = Math.Min(anchor, end), stop = Math.Max(anchor, end);
            for (int i = 0; i < rows.Count; i++)
            {
                var block = blocks[i]; if (block.IsRelatedLink) continue;
                var field = rows[i].Field;
                field.SelectRange(0, 0);
                if (stop <= block.SourceStart || start > block.SourceStart + block.SourceLength) continue;
                var input = field.Q<TextElement>() as VisualElement ?? field;
                int a = DisplayOffset(block, Math.Max(0, start - block.SourceStart));
                int b = DisplayOffset(block, Math.Min(block.SourceLength, stop - block.SourceStart));
                for (int n = a; n < b;)
                {
                    Vector2 p = field.textSelection.GetCursorPositionFromStringIndex(n);
                    int next = n + 1;
                    Vector2 q = field.textSelection.GetCursorPositionFromStringIndex(next);
                    while (next < b)
                    {
                        Vector2 candidate = field.textSelection.GetCursorPositionFromStringIndex(next + 1);
                        if (candidate.y != p.y) break;
                        q = candidate; next++;
                    }
                    var paint = new VisualElement { pickingMode = PickingMode.Ignore };
                    Vector2 top = selectionLayer.WorldToLocal(input.LocalToWorld(
                        new Vector2(p.x + input.contentRect.x, p.y - SelectionLineHeight(field) + input.contentRect.y)));
                    paint.style.position = Position.Absolute; paint.style.left = top.x;
                    float height = SelectionLineHeight(field);
                    paint.style.top = top.y;
                    paint.style.width = q.y == p.y ? Mathf.Max(1, q.x - p.x) : Mathf.Max(5, height / 2);
                    paint.style.height = height;
                    paint.style.backgroundColor = new Color(0.78f, .94f, .96f, .42f);
                    selectionLayer.Add(paint); selectionPaint.Add(paint);
                    n = next;
                }
            }
            history?.UpdateSelection(cursor, selection);
            Changed?.Invoke(raw, cursor, selection);
        }
        private bool HandleDocumentKey(KeyDownEvent evt)
        {
            bool mod = evt.ctrlKey || evt.commandKey;
            string command = mod && evt.keyCode == KeyCode.C ? "Copy" : mod && evt.keyCode == KeyCode.X ? "Cut" :
                (mod && evt.keyCode == KeyCode.V) || (evt.shiftKey && evt.keyCode == KeyCode.Insert) ? "Paste" : null;
            bool deletion = evt.keyCode == KeyCode.Backspace || evt.keyCode == KeyCode.Delete;
            bool newline = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
            bool printable = !mod && !evt.altKey && !char.IsControl(evt.character) && evt.character != '\0';
            if (command == null && !deletion && !newline && !printable)
            {
                if (evt.keyCode == KeyCode.Escape || evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.RightArrow)
                { documentSelection = false; ClearSelectionPaint(); FocusRaw(cursor, cursor); }
                return false;
            }
            panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
            int start = Math.Min(cursor, selection), end = Math.Max(cursor, selection);
            if (command == "Copy" || command == "Cut")
            {
                var text = new System.Text.StringBuilder(); var offsets = new List<int>();
                foreach (var block in blocks)
                {
                    if (block.IsRelatedLink) continue;
                    for (int n = 0; n < block.DisplayText.Length; n++)
                    {
                        int at = block.SourceStart + block.RawOffset(n);
                        if (at >= start && at < end) { text.Append(block.DisplayText[n]); offsets.Add(at); }
                    }
                    int lf = block.SourceStart + block.SourceLength;
                    if (lf >= start && lf < end && lf < raw.Length)
                    { if (raw[lf] == '\r') lf++; text.Append('\n'); offsets.Add(lf); }
                }
                GUIUtility.systemCopyBuffer = text.ToString();
                if (source is IProjectedCopyAccess projected) projected.NotifyProjectedCopied(id, text.ToString(), offsets);
                else source.NotifyCopied(id, start, end - start);
                if (command == "Copy") return true;
            }
            string inserted = command == "Paste" ? GUIUtility.systemCopyBuffer ?? "" : printable ? evt.character.ToString() : newline ? (evt.shiftKey ? "  \n" : "\n") : "";
            processing = true;
            try
            {
                string working = raw;
                int steps = newline && evt.shiftKey ? inserted.Length : 1;
                for (int step = 0; step < steps; step++)
                {
                    string piece = steps > 1 ? inserted[step].ToString() : inserted;
                    int at = start + step;
                    working = working.Remove(at, step == 0 ? end - start : 0).Insert(at, piece);
                    var result = access.ReviewDocument(id, Find().revision, new ChangeCandidate
                    {
                        NewText = working, SelStartBefore = at, SelEndBefore = step == 0 ? end : at,
                        Evidence = new InputEvidence { Kind = command == "Paste" ? EvidenceKind.Paste : EvidenceKind.Key, Text = piece }
                    });
                    if (!result.Approved) { notice(result.Reason); return true; }
                }
                documentSelection = false; ClearSelectionPaint(); cursor = selection = start + inserted.Length;
                history.Record(Find().markdown, cursor, selection, access.GetLabeledDocument(id));
                Render(Find(), true); QueueFocus(cursor, selection); Changed?.Invoke(raw, cursor, selection);
            }
            catch (Exception ex) { notice(ex.Message); }
            finally { processing = false; }
            return true;
        }
        private async void SaveFromShortcut()
        {
            try { if (Submit(ChangeOrigin.Typing)) await source.FlushSave(); }
            catch (Exception ex) { notice("保存できませんでした：" + ex.Message); }
        }
        private void CaptureSelection()
        {
            if (documentSelection) return;
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
            documentSelection = false; ClearSelectionPaint();
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
        private void InsertNewline(int index, TextField field, KeyDownEvent evt)
        {
            if (!Enabled || composing() || evt.ctrlKey || evt.commandKey || evt.altKey) return;
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter &&
                evt.character != '\r' && evt.character != '\n') return;
            panel?.focusController.IgnoreEvent(evt); evt.StopImmediatePropagation();
            // A row facade projects only one line. A newline must be reviewed on
            // the whole document before rebuilding rows, never fed back through
            // the old one-line binding as an unexplained text change.
            int localCursor = field.cursorIndex, localSelection = field.selectIndex;
            if (!Submit(ChangeOrigin.Typing) || index >= blocks.Count) return;
            var block = blocks[index];
            int a = block.SourceStart + block.RawOffset(Math.Min(localCursor, block.DisplayText.Length));
            int b = block.SourceStart + block.RawOffset(Math.Min(localSelection, block.DisplayText.Length));
            int start = Math.Min(a, b), end = Math.Max(a, b);
            processing = true;
            try
            {
                var current = Find();
                string inserted = evt.shiftKey ? "  \n" : "\n";
                string updated = current.markdown;
                for (int n = 0; n < inserted.Length; n++)
                {
                    int at = start + n;
                    updated = updated.Remove(at, n == 0 ? end - start : 0).Insert(at, inserted[n].ToString());
                    var result = access.ReviewDocument(id, Find().revision, new ChangeCandidate
                    {
                        NewText = updated, SelStartBefore = at, SelEndBefore = n == 0 ? end : at,
                        Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = inserted[n].ToString() }
                    });
                    if (!result.Approved) { notice(result.Reason); return; }
                }
                cursor = selection = start + inserted.Length;
                history.Record(Find().markdown, cursor, selection, access.GetLabeledDocument(id));
                Render(Find(), true); QueueFocus(cursor, selection);
                Changed?.Invoke(raw, cursor, selection);
            }
            catch (Exception ex) { notice("未保存です：" + ex.Message); }
            finally { processing = false; }
        }
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
            if (start != end && BlockIndex(start) != BlockIndex(end))
            {
                rows[BlockIndex(start)].Field.Focus();
                SelectDocument(start, end); return;
            }
            documentSelection = false; ClearSelectionPaint();
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
            if (!Enabled || composing() || !Submit(ChangeOrigin.Typing)) return;
            documentSelection = false; ClearSelectionPaint();
            if (redo ? !CanRedo : !CanUndo) { FocusRaw(cursor, cursor); return; }
            var next = redo ? history.Redo() : history.Undo();
            var labels = (LabeledText)next.Metadata;
            var diff = TextDiff.Compute(raw, next.Text);
            replaying = processing = true;
            try
            {
                var result = access.ReviewDocument(id, Find().revision, new ChangeCandidate
                {
                    NewText = next.Text, SelStartBefore = diff.Pos, SelEndBefore = diff.Pos + diff.Deleted.Length,
                    Evidence = new InputEvidence { Kind = redo ? EvidenceKind.Redo : EvidenceKind.Undo, Text = diff.Inserted,
                        UndoLabels = labels.Slice(diff.Pos, diff.Inserted.Length) }
                });
                if (!result.Approved)
                { if (redo) history.Undo(); else history.Redo(); notice(result.Reason); return; }
                documentSelection = false; ClearSelectionPaint();
                cursor = selection = Math.Max(0, Math.Min(next.Cursor, next.Text.Length));
                Render(Find(), true); QueueFocus(cursor, selection); Changed?.Invoke(raw, cursor, selection);
            }
            catch (Exception ex) { if (redo) history.Undo(); else history.Redo(); notice(ex.Message); }
            finally { replaying = processing = false; }
        }
    }
}
