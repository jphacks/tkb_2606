using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Cartograph.EditorUI
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class CartographEditorView : MonoBehaviour
    {
        private IEditorWorkspace workspace;
        private VisualElement root;
        private NoteTreeView noteTree;
        private Label projectLabel, breadcrumb;
        private TextField title, body;
        private Label status, count, message, pasteNotice;
        private Button undoButton, redoButton;
        private readonly Dictionary<string, TextUndoHistory> undoHistories = new Dictionary<string, TextUndoHistory>();
        private bool replayingUndo;
        private string listSignature;
        private string activeId, submittedBody, submittedTitle;
        private bool busy;
        private PanelSettings ownedPanel;
        private Font ownedFont;
#if ENABLE_INPUT_SYSTEM
        private Keyboard imeKeyboard;
        private bool composing;
#endif

        public void Bind(IEditorWorkspace value)
        {
            if (workspace != null) workspace.Changed -= Refresh;
            workspace = value ?? throw new ArgumentNullException(nameof(value));
            workspace.Changed += Refresh;
            if (root != null) Refresh();
        }

        private void Start()
        {
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                ownedPanel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ownedPanel.referenceResolution = new Vector2Int(1440, 900);
                document.panelSettings = ownedPanel;
            }
            root = document.rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.style.backgroundColor = (Color)new Color32(244, 243, 239, 255);
            root.style.color = (Color)new Color32(35, 48, 45, 255);
            // Use an installed Japanese-capable font, never a remote font/CDN.
            var fonts = Font.GetOSInstalledFontNames();
            foreach (var candidate in new[] { "Yu Gothic", "Meiryo", "Noto Sans CJK JP", "Hiragino Sans" })
            {
                if (Array.IndexOf(fonts, candidate) < 0) continue;
                ownedFont = Font.CreateDynamicFontFromOSFont(candidate, 18);
                root.style.unityFont = ownedFont;
                break;
            }
            var header = Row(root);
            header.style.paddingLeft = 24;
            header.style.paddingRight = 24;
            header.style.height = 64;
            header.style.alignItems = Align.Center;
            var brand = new Label("CARTOGRAPH  /  自分の言葉で、地図をつくる");
            brand.style.flexGrow = 1;
            header.Add(brand);
            status = new Label(); header.Add(status);

            var main = Row(root); main.style.flexGrow = 1; main.style.minHeight = 0;
            var sidebar = new VisualElement();
            sidebar.style.width = 280; sidebar.style.paddingLeft = 20; sidebar.style.paddingRight = 20;
            main.Add(sidebar);
            projectLabel = new Label(); projectLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(projectLabel);
            sidebar.Add(new Label("notes / フォルダとノート"));
            Button(sidebar, "＋ 新しいノート", () => Run(async () =>
            {
                await workspace.FlushSave();
                await workspace.CreateDocument();
            }));
            var scroll = new ScrollView(); scroll.style.flexGrow = 1;
            sidebar.Add(scroll);
            noteTree = new NoteTreeView(); scroll.Add(noteTree);
            noteTree.NoteSelected += id => Run(async () =>
            { await workspace.FlushSave(); await workspace.OpenDocument(id); });

            var paper = new VisualElement();
            paper.style.flexGrow = 1; paper.style.minWidth = 0;
            paper.style.marginRight = 24; paper.style.marginBottom = 24;
            paper.style.paddingLeft = 28; paper.style.paddingRight = 28;
            paper.style.paddingTop = 20; paper.style.paddingBottom = 16;
            paper.style.backgroundColor = Color.white;
            main.Add(paper);
            breadcrumb = new Label(); breadcrumb.style.fontSize = 12;
            breadcrumb.style.whiteSpace = WhiteSpace.Normal; breadcrumb.style.marginBottom = 12;
            paper.Add(breadcrumb);
            title = new TextField { name = "document-title" };
            title.style.fontSize = 28; title.style.marginBottom = 16;
            paper.Add(title);
            var toolbar = Row(paper); toolbar.style.flexWrap = Wrap.Wrap;
            undoButton = Button(toolbar, "元に戻す", () => ReplayUndo(false));
            redoButton = Button(toolbar, "やり直し", () => ReplayUndo(true));
            Button(toolbar, "見出し1", () => PrefixLine("# "));
            Button(toolbar, "見出し2", () => PrefixLine("## "));
            Button(toolbar, "見出し3", () => PrefixLine("### "));
            Button(toolbar, "太字", () => WrapSelection("**"));
            Button(toolbar, "斜体", () => WrapSelection("*"));
            Button(toolbar, "箇条書き", () => PrefixLine("- "));
            Button(toolbar, "番号付き", () => PrefixLine("1. "));
            Button(toolbar, "保存を再試行", () => Run(() => workspace.FlushSave()));
            var hint = new Label("初期試作：Markdown編集。書式ボタンで記号を挿入します。");
            hint.style.fontSize = 12; hint.style.marginTop = 8; hint.style.marginBottom = 10;
            paper.Add(hint);
            pasteNotice = new Label(); pasteNotice.style.display = DisplayStyle.None;
            pasteNotice.style.color = (Color)new Color32(151, 70, 40, 255);
            paper.Add(pasteNotice);
            body = new TextField { name = "document-body", multiline = true };
            body.style.flexGrow = 1; body.style.minHeight = 150; body.style.fontSize = 18;
            body.verticalScrollerVisibility = ScrollerVisibility.Auto;
            paper.Add(body);
            count = new Label(); count.style.marginTop = 10; paper.Add(count);
            message = new Label(); message.style.whiteSpace = WhiteSpace.Normal;
            message.style.color = (Color)new Color32(151, 70, 40, 255); paper.Add(message);
            var probe = new M0ProbePanel(); probe.style.display = DisplayStyle.None;
            Button(paper, "M0検証欄の表示／非表示", () =>
                probe.style.display = probe.style.display.value == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None);
            paper.Add(probe);

            title.RegisterValueChangedCallback(_ => { if (!Composing) SubmitTitle(); });
            body.RegisterValueChangedCallback(_ => { UpdateCount(); if (!Composing) SubmitBody(ChangeOrigin.Typing); });
            // Fail closed until the core clipboard policy is connected. This includes title input.
            GuardClipboard(title); GuardClipboard(body);
            body.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!(evt.ctrlKey || evt.commandKey) || Composing) return;
                bool undo = evt.keyCode == KeyCode.Z && !evt.shiftKey;
                bool redo = evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey);
                if (!undo && !redo) return;
                evt.StopImmediatePropagation(); ReplayUndo(redo);
            }, TrickleDown.TrickleDown);
            if (workspace == null) Bind(new DemoWorkspace());
            else Refresh();
        }

        private bool Composing
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return composing;
#else
                return !string.IsNullOrEmpty(Input.compositionString);
#endif
            }
        }
#if ENABLE_INPUT_SYSTEM
        private void OnComposition(IMECompositionString value) { composing = value.Count > 0; }
        private void TrackKeyboard()
        {
            if (imeKeyboard == Keyboard.current) return;
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange -= OnComposition;
            imeKeyboard = Keyboard.current;
            composing = false;
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange += OnComposition;
        }
#endif
        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            TrackKeyboard();
#endif
            if (root == null || workspace == null || busy || Composing) return;
            // The frame after IME confirmation also submits the final value.
            SubmitTitle(); SubmitBody(ChangeOrigin.Typing);
            if (activeId != null && undoHistories.TryGetValue(activeId, out var history))
                history.UpdateSelection(body.cursorIndex, body.selectIndex);
        }

        private bool SubmitTitle()
        {
            if (activeId == null || title.value == submittedTitle) return true;
            var previous = submittedTitle; submittedTitle = title.value;
            try { workspace.UpdateTitle(submittedTitle); }
            catch (Exception ex) { submittedTitle = previous; message.text = ex.Message; return false; }
            return true;
        }

        private bool SubmitBody(ChangeOrigin origin)
        {
            if (activeId == null || body.value == submittedBody) return true;
            var previous = submittedBody; submittedBody = body.value;
            try { workspace.ApplyEditorChange(activeId, submittedBody, origin); }
            catch (Exception ex) { submittedBody = previous; message.text = ex.Message; return false; }
            if (!replayingUndo && undoHistories.TryGetValue(activeId, out var history))
                history.Record(submittedBody, body.cursorIndex, body.selectIndex);
            UpdateUndoButtons();
            return true;
        }

        private void Refresh()
        {
            var current = workspace.Current;
            // A save-state notification must never overwrite focused text/IME or reset selection.
            if (current != null && activeId != current.id)
            {
                activeId = current.id;
                submittedTitle = current.title ?? ""; submittedBody = current.markdown ?? "";
                title.SetValueWithoutNotify(submittedTitle); body.SetValueWithoutNotify(submittedBody);
                if (!undoHistories.ContainsKey(activeId)) undoHistories.Add(activeId, new TextUndoHistory(submittedBody));
            }
            var documents = workspace.Documents;
            var navigation = workspace as IProjectNavigationSource;
            var entries = navigation?.Notes ?? documents.Select(d => new NoteTreeEntry(d.id, d.id + ".md", d.title)).ToArray();
            projectLabel.text = navigation?.ProjectName ?? "ノート";
            var selectedEntry = entries.FirstOrDefault(e => e.DocumentId == activeId);
            breadcrumb.text = selectedEntry == null ? "notes" : "notes / " + selectedEntry.RelativePath.Replace("/", " / ");
            var signature = new StringBuilder(activeId);
            foreach (var item in entries) signature.Append('|').Append(item.DocumentId.Length).Append(':').Append(item.DocumentId)
                .Append(':').Append(item.RelativePath.Length).Append(':').Append(item.RelativePath)
                .Append(':').Append(item.Title.Length).Append(':').Append(item.Title);
            // Body edits/save notifications must not reconstruct the entire file list.
            if (listSignature != signature.ToString())
            {
                noteTree.Render(NoteTree.Build(entries), activeId);
                listSignature = signature.ToString();
            }
            status.text = !workspace.IsPersistent ? "デモ：保存されません" :
                workspace.State == SaveState.Saved ? "保存済み" :
                workspace.State == SaveState.Saving ? "保存中…" :
                workspace.State == SaveState.Error ? "保存失敗・未保存" : "未保存";
            message.text = workspace.Error ?? "";
            UpdateCount(); UpdateUndoButtons();
        }

        private async void Run(Func<Task> action)
        {
            if (busy || Composing) return;
            if (!SubmitTitle() || !SubmitBody(ChangeOrigin.Typing)) return;
            busy = true; root.SetEnabled(false);
            try { await action(); Refresh(); }
            catch (Exception ex) { if (this != null) message.text = "操作に失敗しました：" + ex.Message; }
            finally { busy = false; if (this != null) root.SetEnabled(true); }
        }

        private void WrapSelection(string marker)
        {
            if (Composing) return;
            var text = body.value ?? "";
            var start = Mathf.Clamp(Math.Min(body.cursorIndex, body.selectIndex), 0, text.Length);
            var end = Mathf.Clamp(Math.Max(body.cursorIndex, body.selectIndex), start, text.Length);
            var replacement = marker + text.Substring(start, end - start) + marker;
            SetBody(text.Substring(0, start) + replacement + text.Substring(end), start + marker.Length,
                end + marker.Length);
        }

        private void PrefixLine(string prefix)
        {
            if (Composing) return;
            var text = body.value ?? "";
            var cursor = Mathf.Clamp(body.cursorIndex, 0, text.Length);
            var start = cursor == 0 ? 0 : text.LastIndexOf('\n', cursor - 1) + 1;
            SetBody(text.Insert(start, prefix), cursor + prefix.Length, cursor + prefix.Length);
        }

        private void SetBody(string value, int start, int end)
        {
            body.SetValueWithoutNotify(value);
            SubmitBody(ChangeOrigin.Format); UpdateCount();
            body.Focus(); body.SelectRange(start, end);
        }

        private void GuardClipboard(TextField field)
        {
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                bool paste = ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.V) ||
                    (evt.shiftKey && evt.keyCode == KeyCode.Insert);
                if (!paste) return;
                evt.StopImmediatePropagation();
                ShowPasteNotice();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<ValidateCommandEvent>(evt =>
            {
                if (evt.commandName != "Paste") return;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<ExecuteCommandEvent>(evt =>
            {
                if (evt.commandName != "Paste") return;
                evt.StopImmediatePropagation();
                ShowPasteNotice();
            }, TrickleDown.TrickleDown);
        }

        private IVisualElementScheduledItem pasteNoticeTimer;
        private void ShowPasteNotice()
        {
            pasteNoticeTimer?.Pause();
            pasteNotice.text = "外部からの貼り付けはできません";
            pasteNotice.style.display = DisplayStyle.Flex;
            pasteNoticeTimer = pasteNotice.schedule.Execute(() => pasteNotice.style.display = DisplayStyle.None)
                .StartingIn(2000);
        }

        private void UpdateUndoButtons()
        {
            bool has = activeId != null && undoHistories.ContainsKey(activeId);
            undoButton.SetEnabled(has && undoHistories[activeId].CanUndo);
            redoButton.SetEnabled(has && undoHistories[activeId].CanRedo);
        }

        private void ReplayUndo(bool redo)
        {
            if (busy || Composing || activeId == null || !undoHistories.TryGetValue(activeId, out var history)) return;
            if (!SubmitBody(ChangeOrigin.Typing)) return;
            if (redo ? !history.CanRedo : !history.CanUndo) return;
            var previous = history.Current;
            var next = redo ? history.Redo() : history.Undo();
            replayingUndo = true;
            body.SetValueWithoutNotify(next.Text);
            bool accepted = SubmitBody(redo ? ChangeOrigin.Redo : ChangeOrigin.Undo);
            replayingUndo = false;
            if (!accepted)
            {
                if (redo) history.Undo(); else history.Redo();
                body.SetValueWithoutNotify(previous.Text);
                next = previous;
            }
            body.Focus(); body.SelectRange(next.Cursor, next.Selection);
            UpdateCount(); UpdateUndoButtons();
        }

        private void UpdateCount()
        {
            count.text = $"{new StringInfo(body.value ?? "").LengthInTextElements:N0} 文字（Markdown記号を含む）";
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            parent.Add(row); return row;
        }

        private static Button Button(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text, focusable = false };
            button.style.minHeight = 30; button.style.marginBottom = 6;
            parent.Add(button); return button;
        }

        private void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            if (imeKeyboard != null) imeKeyboard.onIMECompositionChange -= OnComposition;
#endif
            if (workspace != null) workspace.Changed -= Refresh;
            if (ownedPanel != null) Destroy(ownedPanel);
            if (ownedFont != null) Destroy(ownedFont);
        }
    }
}
