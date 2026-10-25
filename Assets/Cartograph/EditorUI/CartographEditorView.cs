using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using Cartograph.MapUI;
using Cartowords.MapPresentation;
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
        private NotebookLibraryDialog notebookLibrary;
        private SecondaryNotePane secondaryPane;
        private Label projectLabel, breadcrumb;
        private TextField title, body;
        private Label status, count, message, pasteNotice;
        private Button undoButton, redoButton;
        private VisualElement formatToolbar;
        private readonly Dictionary<string, TextUndoHistory> undoHistories = new Dictionary<string, TextUndoHistory>();
        private bool replayingUndo;
        private string listSignature;
        private string activeId, submittedBody, submittedTitle;
        private bool busy;
        private PanelSettings ownedPanel;
        private Font ownedFont;
        private FontAsset ownedFontAsset;
        private ReviewedTextBinding reviewed;
        private TextField projectPath;
        private Button newNoteButton;
        private Button newFolderButton;
        private Button notebookPicker, notePicker, closeNotebookButton;
        private MapPanel mapPanel;
        private readonly MapSnapshotGate mapGate = new MapSnapshotGate();
        private string mapProjectKey = Guid.NewGuid().ToString("N"), mapVersionSignature;
        private long mapGeneration;
        private IVisualElementScheduledItem mapRequestJob;
        private System.Threading.SynchronizationContext uiContext;
        private int uiThread;
        public event Action<MapRequest> MapRefreshRequested;
        private VisualElement writingArea;
        private Label emptyNoteMessage;
        private RelatedNotesControl relatedControl;
        private NoteActionDialog noteDialog;
        private RichNoteEditor rich;
        private MapSnapshot displayedMap;
        private VisualElement projectTransition;
        private IVisualElementScheduledItem transitionHideJob;
#if UNITY_EDITOR
        private bool choosingProject;
#endif
        private int richCursor, richSelection;
        private int EditorCursor => rich != null && rich.Enabled ? richCursor : body.cursorIndex;
        private int EditorSelection => rich != null && rich.Enabled ? richSelection : body.selectIndex;
#if ENABLE_INPUT_SYSTEM
        private Keyboard imeKeyboard;
        private bool composing;
#endif

        public void Bind(IEditorWorkspace value)
        {
            if (workspace != null) workspace.Changed -= Refresh;
            workspace = value ?? throw new ArgumentNullException(nameof(value));
            activeId = null; listSignature = null; undoHistories.Clear();
            reviewed?.Bind(null, null);
            ResetMapContext();
            workspace.Changed += Refresh;
            if (root != null) Refresh();
        }

        private void Start()
        {
            uiContext = System.Threading.SynchronizationContext.Current;
            uiThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var document = GetComponent<UIDocument>();
            if (document.panelSettings == null)
            {
                ownedPanel = ScriptableObject.CreateInstance<PanelSettings>();
                // 拡大率は ApplyUiScale が画面の大きさから決める(ScaleWithScreenSize だと上限・下限を置けない)。
                ownedPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                ownedPanel.scale = UiScaleFor(Screen.width, Screen.height);
                ownedPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("CartographTheme");
                document.panelSettings = ownedPanel;
            }
            if (document.panelSettings.themeStyleSheet == null)
                document.panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("CartographTheme");
            root = document.rootVisualElement;
            root.Clear();
            root.AddToClassList("cartowords");
            root.style.flexGrow = 1;
            root.style.backgroundColor = (Color)new Color32(31, 36, 50, 255);
            // Disabled controls may fade, but the app surface must stay opaque
            // while a project loads; otherwise the Unity camera shows through.
            root.style.opacity = 1;
            root.style.color = (Color)new Color32(229, 233, 242, 255);
            root.style.fontSize = 16;
            // Use an installed Japanese-capable font, never a remote font/CDN.
            var fonts = Font.GetOSInstalledFontNames();
            foreach (var candidate in new[] { "Meiryo", "Noto Sans CJK JP", "Hiragino Sans", "Yu Gothic" })
            {
                if (Array.IndexOf(fonts, candidate) < 0) continue;
                ownedFont = Font.CreateDynamicFontFromOSFont(candidate, 18);
                ownedFontAsset = FontAsset.CreateFontAsset(ownedFont);
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(ownedFontAsset);
                break;
            }
            var header = Row(root);
            header.style.paddingLeft = 24;
            header.style.paddingRight = 24;
            header.style.height = 30; header.style.flexShrink = 0;
            header.style.alignItems = Align.Center;
            var brand = new Label("CARTOWORDS"); brand.AddToClassList("brand");
            header.Add(brand);
            var tagline = new Label(" /  自分の言葉で、地図をつくる");
            tagline.style.flexGrow = 1; tagline.AddToClassList("muted"); header.Add(tagline);
            status = new Label(); status.AddToClassList("save-status"); header.Add(status);
            var projectBar = Row(root); projectBar.style.paddingLeft = 24; projectBar.style.paddingRight = 24;
            projectBar.AddToClassList("project-bar");
            projectBar.style.flexShrink = 0;
            projectBar.style.display = DisplayStyle.None;
            projectPath = new TextField { label = "フォルダ名", tooltip = "notes と .cartograph を保存するフォルダの絶対パス" };
            projectPath.style.width = 360; projectPath.style.maxWidth = Length.Percent(55); projectBar.Add(projectPath);
            projectPath.textEdition.placeholder = "ファイルが選択されていません";
            Button(projectBar, "フォルダを選択", SelectProjectFolder);
            Button(projectBar, "閉じる", RequestCloseProject);

            // 左から 索引 | マップ | エディタ。境界はドラッグで動かせ、幅は次回起動時に復元する。
            ApplyUiScale();
            var main = new TwoPaneSplitView(0, PaneWidth(IndexRatioKey, 0.14f, 200, 320), TwoPaneSplitViewOrientation.Horizontal);
            main.style.flexGrow = 1; main.style.minHeight = 0; root.Add(main);
            var sidebar = new VisualElement();
            sidebar.AddToClassList("sidebar");
            sidebar.style.minWidth = 170;
            sidebar.style.paddingLeft = 12; sidebar.style.paddingRight = 12;
            main.Add(sidebar);
            var contentSplit = new TwoPaneSplitView(0, PaneWidth(MapRatioKey, 0.36f, 280, 1100), TwoPaneSplitViewOrientation.Horizontal);
            contentSplit.style.flexGrow = 1; contentSplit.style.minWidth = 0; main.Add(contentSplit);
            mapPanel = new MapPanel(); mapPanel.style.minWidth = 240; contentSplit.Add(mapPanel);
            mapPanel.OpenNoteRequested += OpenNoteBeside;
            mapPanel.CandidateAccepted += AcceptCandidate;
            mapPanel.CandidateRejected += RejectCandidate;
            mapPanel.LayoutChanged += QueueLayoutSave;
            var rightWorkspace = new VisualElement(); rightWorkspace.AddToClassList("right-workspace");
            rightWorkspace.style.minWidth = 420; rightWorkspace.style.flexGrow = 1; contentSplit.Add(rightWorkspace);
            // つまみは、2 つのペインが入ってから作られる。
            RememberRatio(contentSplit, mapPanel, MapRatioKey);
            var resizeCursor = new SplitResizeCursor(root);
            resizeCursor.Add(contentSplit);
            RememberRatio(main, sidebar, IndexRatioKey);
            resizeCursor.Add(main);
            var noteHeader = new VisualElement(); noteHeader.AddToClassList("note-header"); rightWorkspace.Add(noteHeader);
            var workbench = Row(rightWorkspace); workbench.AddToClassList("workbench");
            workbench.style.flexGrow = 1; workbench.style.minHeight = 0;
            projectLabel = new Label(); projectLabel.style.whiteSpace = WhiteSpace.Normal;
            notebookPicker = Button(sidebar, "ノート用フォルダを選ぶ ▾", OpenNotebookMenu);
            notebookPicker.style.whiteSpace = WhiteSpace.NoWrap; notebookPicker.style.overflow = Overflow.Hidden;
            notebookPicker.style.textOverflow = TextOverflow.Ellipsis;
            notePicker = Button(sidebar, "ノートを選ぶ ▾", OpenNoteMenu);
            closeNotebookButton = Button(sidebar, "閉じる", RequestCloseProject);
            newNoteButton = Button(sidebar, "＋ 新しいノート", () => Run(async () =>
            {
                await CreateEditableNote();
            })); newNoteButton.AddToClassList("primary-action");
            newFolderButton = Button(sidebar, "＋ フォルダ", () => RequestCreateFolder(noteTree.CurrentFolder));
            // 索引(検索・並べ替え・ツリー)は、サイドバーの残りの高さを使う。
            notePicker.style.display = DisplayStyle.None; // ツリーと検索があるので、ノート選択メニューは使わない
            noteTree = new NoteTreeView(); sidebar.Add(noteTree);
            noteTree.CreateFolderRequested += RequestCreateFolder;
            noteTree.MoveRequested += (id, folder) =>
            {
                var target = workspace as ProjectEditorWorkspace; if (target == null) return;
                var tree = target.Tree;
                Run(async () =>
                {
                    if (!ReferenceEquals(tree, target.Tree)) throw new InvalidOperationException("ノートを選び直してください。");
                    await target.MoveDocument(id, folder);
                    if (secondaryPane.DocumentId == id) secondaryPane.Hide();
                    undoHistories.Remove(id); ResetMapContext();
                });
            };
            noteTree.CreateNoteRequested += folder => Run(async () =>
            {
                if (workspace is ProjectEditorWorkspace target) await target.CreateDocumentInFolder(folder);
            });
            noteTree.DeleteRequested += id =>
            {
                var record = workspace.Documents.FirstOrDefault(d => d.id == id); if (record == null) return;
                var target = workspace as ProjectEditorWorkspace; if (target == null) return;
                var tree = target.Tree;
                noteDialog.ShowDelete(record.title, () => Execute(async () =>
                {
                    if (!ReferenceEquals(tree, target.Tree)) throw new InvalidOperationException("ノートを選び直してください。");
                    await target.DeleteDocument(id);
                    if (secondaryPane.DocumentId == id) secondaryPane.Hide();
                    undoHistories.Remove(id); ResetMapContext();
                }));
            };
            noteTree.RenameRequested += RequestRename;
            noteTree.NoteSelected += id => Run(async () =>
            { await workspace.FlushSave(); await workspace.OpenDocument(id); });

            var editors = Row(workbench); editors.style.flexGrow = 1; editors.style.minWidth = 0;
            editors.style.flexDirection = FlexDirection.Column;
            var paper = new VisualElement();
            writingArea = paper;
            emptyNoteMessage = new Label("まだノートが開かれていません。\n上のプロジェクト欄から保存先フォルダを開いてください。");
            emptyNoteMessage.style.whiteSpace = WhiteSpace.Normal; emptyNoteMessage.style.marginTop = 24;
            emptyNoteMessage.style.marginLeft = 16; editors.Add(emptyNoteMessage);
            paper.AddToClassList("editor-sheet");
            paper.style.flexGrow = 1; paper.style.flexBasis = 0; paper.style.minWidth = 200;
            paper.style.paddingLeft = 8; paper.style.paddingRight = 8;
            paper.style.paddingTop = 6; paper.style.paddingBottom = 6;
            editors.Add(paper);
            breadcrumb = new Label(); breadcrumb.style.fontSize = 12;
            breadcrumb.AddToClassList("muted");
            breadcrumb.style.whiteSpace = WhiteSpace.Normal; breadcrumb.style.marginBottom = 2;
            breadcrumb.style.display = DisplayStyle.None;
            var titleHint = new Label("ノート名（クリックして変更・Enterで確定）");
            titleHint.AddToClassList("muted"); titleHint.style.fontSize = 11;
            noteHeader.Add(titleHint);
            title = new TextField { name = "document-title" };
            title.tooltip = "本全体の題名です。左の章名とは別に保存します。Enter、または欄の外をクリックして確定します。";
            title.AddToClassList("note-title");
            title.style.fontSize = 21; title.style.marginBottom = 2;
            title.style.flexShrink = 0;
            noteHeader.Add(title);
            title.isReadOnly = false;
            var toolbar = Row(editors); editors.Insert(0, toolbar);
            formatToolbar = toolbar; toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.AddToClassList("format-toolbar");
            toolbar.style.flexShrink = 0;
            undoButton = Button(toolbar, "↶ 戻る", () => ReplayUndo(false));
            undoButton.tooltip = "直前の文字入力・書式変更を取り消す（Ctrl+Z）";
            redoButton = Button(toolbar, "やり直し", () => ReplayUndo(true));
            Button(toolbar, "大見出し", () => PrefixLine("# "));
            Button(toolbar, "見出し", () => PrefixLine("## "));
            Button(toolbar, "小見出し", () => PrefixLine("### "));
            Button(toolbar, "本文", () => PrefixLine(""));
            Button(toolbar, "太字", () => WrapSelection("**"));
            Button(toolbar, "斜体", () => WrapSelection("*"));
            Button(toolbar, "箇条書き", () => PrefixLine("- "));
            Button(toolbar, "番号付き", () => PrefixLine("1. "));
            relatedControl = new RelatedNotesControl(); paper.Add(relatedControl);
            relatedControl.ConnectionRequested += (targetId, connect) => Run(async () =>
            {
                if (connect)
                {
                    var edit = RelatedNoteLink.Insert(body.value, EditorCursor, activeId, targetId, workspace.Documents.Select(d => d.id));
                    SetBody(edit.Text, edit.Cursor, edit.Cursor);
                }
                else await DisconnectRelated(targetId);
            });
            pasteNotice = new Label(); pasteNotice.style.display = DisplayStyle.None;
            pasteNotice.style.color = (Color)new Color32(255, 170, 133, 255);
            paper.Add(pasteNotice);
            body = new TextField { name = "document-body", multiline = true };
            body.AddToClassList("note-body");
            body.style.flexGrow = 1; body.style.minHeight = 100; body.style.fontSize = 18;
            body.verticalScrollerVisibility = ScrollerVisibility.Auto;
            paper.Add(body);
            reviewed = new ReviewedTextBinding(body, () => Composing, ShowReviewNotice);
            rich = new RichNoteEditor(() => Composing, ShowReviewNotice); paper.Add(rich);
            rich.Changed += (raw, cursor, selection) =>
            {
                if (body.value != raw) body.SetValueWithoutNotify(raw);
                richCursor = cursor; richSelection = selection;
                submittedBody = raw; UpdateCount();
            };
            count = new Label(); count.AddToClassList("muted"); count.style.fontSize = 11; count.style.marginTop = 3; paper.Add(count);
            message = new Label(); message.style.whiteSpace = WhiteSpace.Normal;
            message.style.color = (Color)new Color32(255, 170, 133, 255); noteHeader.Add(message);
            secondaryPane = new SecondaryNotePane(() => Composing,
                (id, revision, text, origin) => ((ISecondaryNoteEditor)workspace).ApplySecondaryEdit(id, revision, text, origin),
                () => secondaryPane.Hide());
            editors.Add(secondaryPane);

            title.RegisterValueChangedCallback(_ => { if (!(workspace is IReviewedNoteAccess) && !Composing) SubmitTitle(); });
            title.RegisterCallback<FocusOutEvent>(_ => { if (workspace is IReviewedNoteAccess && title.value != submittedTitle && !busy && !Composing) Run(() => Task.CompletedTask); });
            title.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return || Composing || !(workspace is IReviewedNoteAccess)) return;
                evt.StopImmediatePropagation(); Run(() => Task.CompletedTask);
            });
            body.RegisterValueChangedCallback(_ => { UpdateCount(); if (!reviewed.Enabled && !Composing) SubmitBody(ChangeOrigin.Typing); });
            // Fail closed until the core clipboard policy is connected. This includes title input.
            GuardClipboard(body);
            body.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!(evt.ctrlKey || evt.commandKey) || Composing || reviewed.Enabled) return;
                bool undo = evt.keyCode == KeyCode.Z && !evt.shiftKey;
                bool redo = evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey);
                if (!undo && !redo) return;
                evt.StopImmediatePropagation(); ReplayUndo(redo);
            }, TrickleDown.TrickleDown);
            noteDialog = new NoteActionDialog(); root.Add(noteDialog);
            projectTransition = new VisualElement { focusable = true };
            projectTransition.style.position = Position.Absolute;
            projectTransition.style.left = projectTransition.style.right = projectTransition.style.top = projectTransition.style.bottom = 0;
            projectTransition.style.backgroundColor = (Color)new Color32(31, 36, 50, 255);
            projectTransition.style.alignItems = Align.Center; projectTransition.style.justifyContent = Justify.Center;
            projectTransition.Add(new Label("保存先を読み込んでいます…"));
            projectTransition.style.display = DisplayStyle.None; root.Add(projectTransition);
            if (workspace == null) Bind(new ProjectEditorWorkspace());
            else Refresh();
#if ENABLE_INPUT_SYSTEM
            TrackKeyboard();
#endif
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
        private void OnComposition(IMECompositionString value)
        {
            composing = value.Count > 0;
            reviewed?.CompositionChanged(composing); rich?.CompositionChanged(composing); secondaryPane?.CompositionChanged(composing);
        }
        private void OnTextInput(char value) { reviewed?.TextInput(value); rich?.TextInput(value); secondaryPane?.TextInput(value); }
        private void TrackKeyboard()
        {
            if (imeKeyboard == Keyboard.current) return;
            if (imeKeyboard != null) { imeKeyboard.onIMECompositionChange -= OnComposition; imeKeyboard.onTextInput -= OnTextInput; }
            imeKeyboard = Keyboard.current;
            composing = false;
            if (imeKeyboard != null) { imeKeyboard.onIMECompositionChange += OnComposition; imeKeyboard.onTextInput += OnTextInput; }
        }
#endif
        private void LateUpdate()
        {
#if ENABLE_INPUT_SYSTEM
            TrackKeyboard();
#endif
            ApplyUiScale(); // ウィンドウの大きさが変わったら、拡大率を合わせ直す
            if (root == null || workspace == null) return;
            if (busy || Composing) { UpdateSaveStatus(); return; }
            // The frame after IME confirmation also submits the final value.
            if (rich.Enabled) rich.Tick();
            else if (reviewed.Enabled) reviewed.Tick();
            else { SubmitTitle(); SubmitBody(ChangeOrigin.Typing); }
            secondaryPane.Tick();
            UpdateMapFocus(false);
            UpdateSaveStatus(); UpdateUndoButtons();
            if (activeId != null && undoHistories.TryGetValue(activeId, out var history))
                history.UpdateSelection(body.cursorIndex, body.selectIndex);
        }

        // カーソルのある拠点をマップに伝える。ノート・カーソル・本文のどれかが変わった時だけ求め直す(force は解析結果が届いた時)。
        private string focusNote, focusText;
        private int focusCursor = -1;
        private void UpdateMapFocus(bool force)
        {
            if (mapPanel == null) return;
            string text = body.value ?? ""; int cursor = EditorCursor;
            if (!force && focusCursor == cursor && focusNote == activeId && ReferenceEquals(focusText, text)) return;
            focusCursor = cursor; focusNote = activeId; focusText = text;
            string node = activeId == null || displayedMap == null ? null :
                MapFocus.NodeAt(displayedMap.Nodes, activeId, TextLinePosition.LineAt(text, cursor));
            mapPanel.SetEditorFocus(node);
        }

        private bool SubmitTitle()
        {
            if (activeId == null || title.value == submittedTitle) return true;
            if (workspace is IReviewedNoteAccess) return true;
            var previous = submittedTitle; submittedTitle = title.value;
            try { workspace.UpdateTitle(submittedTitle); }
            catch (Exception ex) { submittedTitle = previous; message.text = ex.Message; return false; }
            return true;
        }

        private bool SubmitBody(ChangeOrigin origin)
        {
            if (origin == ChangeOrigin.Typing && rich != null && rich.Enabled) return rich.Submit(origin);
            if (reviewed.Enabled)
            {
                bool result = reviewed.Submit(origin);
                submittedBody = body.value; UpdateCount(); UpdateUndoButtons(); return result;
            }
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
            title.style.display = current == null ? DisplayStyle.None : DisplayStyle.Flex;
            writingArea.style.display = current == null ? DisplayStyle.None : DisplayStyle.Flex;
            emptyNoteMessage.style.display = current == null ? DisplayStyle.Flex : DisplayStyle.None;
            formatToolbar.SetEnabled(current != null);
            bool projectOpen = !(workspace is IReviewingEditorWorkspace project) || project.Tree != null;
            var actualRoot = (workspace as ProjectEditorWorkspace)?.ProjectRoot;
            var pathFocus = projectPath.panel?.focusController.focusedElement as VisualElement;
            bool editingPath = pathFocus != null && (ReferenceEquals(pathFocus, projectPath) || projectPath.Contains(pathFocus));
            if (actualRoot != null && !editingPath) projectPath.SetValueWithoutNotify(actualRoot);
            mapPanel.SetProjectOpen(projectOpen);
            notebookPicker.text = projectOpen ? ((workspace as ProjectEditorWorkspace)?.ProjectName ?? "ノート用フォルダ") + " ▾" : "ノート用フォルダを選ぶ ▾";
            notePicker.SetEnabled(projectOpen); closeNotebookButton.SetEnabled(projectOpen);
            emptyNoteMessage.text = projectOpen ? "まだノートが開かれていません。\n「新しいノート」で書き始められます。" :
                "まだノートが開かれていません。\n左の「ノート用フォルダを選ぶ」から選択してください。";
            newNoteButton.SetEnabled(true);
            newNoteButton.style.display = projectOpen ? DisplayStyle.Flex : DisplayStyle.None;
            newFolderButton.style.display = projectOpen ? DisplayStyle.Flex : DisplayStyle.None;
            newNoteButton.text = "＋ 新しいノート";
            body.tooltip = current == null ? "保存先を選び、ノートを作成すると入力できます。" : "本文を入力";
            title.isReadOnly = !projectOpen; body.isReadOnly = current == null;
            if (current == null)
            {
                activeId = null; submittedBody = submittedTitle = "";
                body.SetValueWithoutNotify("");
            }
            // A save-state notification must never overwrite focused text/IME or reset selection.
            if (current != null && activeId != current.id)
            {
                activeId = current.id;
                submittedTitle = current.title ?? ""; submittedBody = current.markdown ?? "";
                body.SetValueWithoutNotify(submittedBody);
                if (!undoHistories.ContainsKey(activeId)) undoHistories.Add(activeId, new TextUndoHistory(submittedBody));
            }
            else if (current != null && !Composing && body.value == submittedBody && submittedBody != (current.markdown ?? ""))
            {
                // Same note edited in the other pane: synchronize its authoritative value.
                int cursor = body.cursorIndex, selection = body.selectIndex;
                submittedBody = current.markdown ?? "";
                body.SetValueWithoutNotify(submittedBody);
                body.SelectRange(Math.Min(cursor, submittedBody.Length), Math.Min(selection, submittedBody.Length));
                undoHistories[activeId] = new TextUndoHistory(submittedBody);
            }
            if (workspace is ProjectEditorWorkspace book)
            {
                var focus = title.panel?.focusController.focusedElement as VisualElement;
                if (focus == null || !(ReferenceEquals(focus, title) || title.Contains(focus)))
                { submittedTitle = book.BookTitle; title.SetValueWithoutNotify(submittedTitle); }
            }
            var documents = workspace.Documents;
            relatedControl.SetNotes(current?.id, documents);
            relatedControl.SetConnections(MapAnalysisAdapter.ConnectedNotes(displayedMap, current?.id));
            relatedControl.SetSuggestions(MapAnalysisAdapter.SuggestedNotes(displayedMap, current?.id));
            QueueMapRequest(documents);
            reviewed.Bind(workspace as IReviewingEditorWorkspace, current);
            rich.Bind(workspace as IReviewingEditorWorkspace, current);
            rich.style.display = rich.Enabled ? DisplayStyle.Flex : DisplayStyle.None;
            body.style.display = rich.Enabled ? DisplayStyle.None : DisplayStyle.Flex;
            secondaryPane.SetReviewWorkspace(workspace as IReviewingEditorWorkspace);
            if (secondaryPane.DocumentId != null)
                secondaryPane.Refresh(documents.FirstOrDefault(d => d.id == secondaryPane.DocumentId));
            var navigation = workspace as IProjectNavigationSource;
            var entries = navigation?.Notes ?? documents.Select(d => new NoteTreeEntry(d.id, d.id + ".md", d.title)).ToArray();
            projectLabel.text = navigation?.ProjectName ?? "ノート";
            projectLabel.style.display = projectOpen ? DisplayStyle.Flex : DisplayStyle.None;
            var selectedEntry = entries.FirstOrDefault(e => e.DocumentId == activeId);
            breadcrumb.text = "notes";
            var signature = new StringBuilder(activeId).Append(projectOpen ? "|open" : "|closed");
            var folderPaths = workspace is IReviewingEditorWorkspace reviewing ? FolderPaths(reviewing.Tree).ToArray() : Array.Empty<string>();
            foreach (var folder in folderPaths) signature.Append("|folder:").Append(folder.Length).Append(':').Append(folder);
            foreach (var item in entries) signature.Append('|').Append(item.DocumentId.Length).Append(':').Append(item.DocumentId)
                .Append(':').Append(item.RelativePath.Length).Append(':').Append(item.RelativePath)
                .Append(':').Append(item.Title.Length).Append(':').Append(item.Title);
            // 日時順のときは、保存のたびに並びが変わりうる。
            if (noteTree.Sort.Key == NoteSortKey.Updated) foreach (var item in entries) signature.Append('|').Append(item.Updated.Ticks);
            else if (noteTree.Sort.Key == NoteSortKey.Created) foreach (var item in entries) signature.Append('|').Append(item.Created.Ticks);
            signature.Append("|sort:").Append(noteTree.Sort.Key).Append(noteTree.Sort.Descending).Append(noteTree.Sort.FoldersFirst);
            // Body edits/save notifications must not reconstruct the entire file list.
            if (listSignature != signature.ToString())
            {
                noteTree.Render(NoteTree.Build(entries, folderPaths), activeId,
                    "");
                listSignature = signature.ToString();
            }
            UpdateSaveStatus();
            message.text = workspace.Error ?? "";
            UpdateCount(); UpdateUndoButtons();
        }

        private async void Run(Func<Task> action)
        {
            if (busy) return;
            try { await Execute(action); }
            catch (Exception ex) { if (this != null) { Refresh(); message.text = "操作に失敗しました：" + ex.Message; } }
        }
        private async Task Execute(Func<Task> action)
        {
            if (busy) throw new InvalidOperationException("処理が終わるまでお待ちください。");
            if (Composing) throw new InvalidOperationException("日本語変換を確定してから操作してください。");
            busy = true; root.SetEnabled(false);
            try
            {
                if (!SubmitBody(ChangeOrigin.Typing) || !secondaryPane.Submit(ChangeOrigin.Typing))
                    throw new InvalidOperationException("入力内容の案内を確認して、もう一度操作してください。");
                if (workspace is ProjectEditorWorkspace book && title.value != submittedTitle)
                {
                    book.UpdateBookTitle(title.value);
                    submittedTitle = book.BookTitle;
                }
                else if (!SubmitTitle()) throw new InvalidOperationException("ノート名を確認してください。");
                await action(); Refresh();
            }
            finally { busy = false; if (this != null) root.SetEnabled(true); }
        }

        // Map integration entry point: one-based heading line, not a UTF-16 offset.
        // Call on double-click only. Single-click selection stays in the map.
        public void OpenNoteBeside(string documentId, int headingLine)
        {
            if (root == null || workspace == null) return;
            Run(async () =>
            {
                if (headingLine < 1) throw new ArgumentOutOfRangeException(nameof(headingLine));
                await workspace.FlushSave();
                var document = workspace is IReviewedNoteAccess access ? await access.LoadDocument(documentId) :
                    workspace.Documents.FirstOrDefault(d => d.id == documentId);
                if (document == null) throw new InvalidOperationException("開くノートが見つかりません。");
                secondaryPane.Open(document, headingLine, workspace is ISecondaryNoteEditor);
            });
        }

        private async Task CreateEditableNote()
        {
            if (workspace is IReviewingEditorWorkspace target && target.Tree == null)
            {
#if UNITY_EDITOR
                var selected = UnityEditor.EditorUtility.OpenFolderPanel("ノートの保存先を選択", projectPath.value, "");
                if (string.IsNullOrEmpty(selected)) return;
                projectPath.SetValueWithoutNotify(selected);
#endif
                if (string.IsNullOrWhiteSpace(projectPath.value))
                    throw new InvalidOperationException("上のプロジェクト欄に保存先フォルダを入力してください。");
                await OpenProjectFolder();
                if (workspace.Current == null) await workspace.CreateDocument();
            }
            else
            {
                await workspace.FlushSave();
                if (workspace is ProjectEditorWorkspace project) await project.CreateDocumentInFolder(noteTree.CurrentFolder);
                else await workspace.CreateDocument();
            }
            body.schedule.Execute(() => { if (rich.Enabled) rich.FocusRaw(0, 0); else { body.Focus(); body.SelectRange(0, 0); } });
        }

        private async Task OpenProjectFolder()
        {
            await OpenProjectAt(projectPath.value);
        }

        private void OpenNotebookMenu()
        {
            if (busy) return;
            var menu = new GenericDropdownMenu();
            var active = (workspace as ProjectEditorWorkspace)?.ProjectRoot;
            foreach (var folder in NotebookFolders())
            {
                string path = folder.Key;
                menu.AddItem(folder.Value, string.Equals(path, active, StringComparison.OrdinalIgnoreCase), () => Run(() => OpenProjectAt(path)));
            }
            menu.AddSeparator("");
            menu.AddItem("＋ 新規作成・保存先を追加…", false, SelectProjectFolder);
            menu.DropDown(notebookPicker.worldBound, notebookPicker, default(DropdownMenuSizeMode));
        }
        private void OpenNoteMenu()
        {
            if (busy) return;
            var menu = new GenericDropdownMenu();
            foreach (var record in workspace.Documents.OrderBy(d => d.id, StringComparer.OrdinalIgnoreCase))
            {
                string id = record.id;
                menu.AddItem(id.Replace("/", " › "), id == activeId, () => Run(async () =>
                {
                    await workspace.FlushSave(); await workspace.OpenDocument(id);
                    noteTree.RevealNote(id); listSignature = null;
                }));
            }
            if (workspace.Documents.Count == 0) menu.AddDisabledItem("ノートはまだありません", false);
            menu.DropDown(notePicker.worldBound, notePicker, default(DropdownMenuSizeMode));
        }
        private KeyValuePair<string, string>[] NotebookFolders()
        {
            string library = System.IO.Path.Combine(Application.persistentDataPath, "Notebooks");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (System.IO.Directory.Exists(library))
                foreach (string path in System.IO.Directory.GetDirectories(library)) paths.Add(path);
            foreach (string path in PlayerPrefs.GetString("CARTOWORDS.NotebookFolders", "").Split('\n'))
                if (!string.IsNullOrWhiteSpace(path) && System.IO.Directory.Exists(path)) paths.Add(path);
            string last = PlayerPrefs.GetString("CARTOWORDS.LastProject", "");
            if (!string.IsNullOrWhiteSpace(last) && System.IO.Directory.Exists(last)) paths.Add(last);
            string active = (workspace as ProjectEditorWorkspace)?.ProjectRoot;
            if (!string.IsNullOrWhiteSpace(active) && System.IO.Directory.Exists(active)) paths.Add(active);
            return paths.OrderBy(p => System.IO.Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .Select(p => new KeyValuePair<string, string>(p, paths.Count(other => string.Equals(System.IO.Path.GetFileName(other), System.IO.Path.GetFileName(p), StringComparison.OrdinalIgnoreCase)) > 1
                    ? System.IO.Path.GetFileName(p) + "（" + p + "）" : System.IO.Path.GetFileName(p))).ToArray();
        }
        private void SelectProjectFolder()
        {
            if (busy) return;
            if (notebookLibrary == null) { notebookLibrary = new NotebookLibraryDialog(BrowseProjectFolder); root.Add(notebookLibrary); }
            string library = System.IO.Path.Combine(Application.persistentDataPath, "Notebooks");
            var folders = NotebookFolders();
            notebookLibrary.Show(folders, path => Execute(() => OpenProjectAt(path)), name => Execute(async () =>
            {
                name = (name ?? "").Trim();
                if (!Cartograph.Core.NotePath.IsValidName(name) || name.StartsWith(".", StringComparison.Ordinal))
                    throw new ArgumentException("フォルダ名に使えない文字が含まれています。");
                string path = System.IO.Path.Combine(library, name);
                if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path)) throw new System.IO.IOException("同じ名前のフォルダがあります。");
                System.IO.Directory.CreateDirectory(path); await OpenProjectAt(path);
            }));
        }
        private void BrowseProjectFolder()
        {
#if UNITY_EDITOR
            if (choosingProject || busy) return;
            choosingProject = true;
            ShowProjectTransition();
            UnityEditor.EditorApplication.delayCall += async () =>
            {
                if (this == null) return;
                try
                {
                    var selected = UnityEditor.EditorUtility.OpenFolderPanel("CARTOWORDS 保存先フォルダを選択", PlayerPrefs.GetString("CARTOWORDS.LastProject", ""), "");
                    if (!string.IsNullOrEmpty(selected)) await Execute(() => OpenProjectAt(selected));
                }
                catch (Exception ex) { if (this != null) { Refresh(); message.text = "操作に失敗しました：" + ex.Message; } }
                finally { choosingProject = false; QueueRepaint(); HideProjectTransition(); }
            };
#else
            // A native folder picker is not bundled in the player yet.
            // Preserve the existing path entry fallback instead of a nonfunctional button.
            if (string.IsNullOrWhiteSpace(projectPath.value))
            { message.text = "保存先フォルダのパスをプロジェクト欄に入力して「ファイルを選択」を押してください。"; projectPath.Focus(); return; }
            Run(OpenProjectFolder);
#endif
        }

        private async Task OpenProjectAt(string selected)
        {
            ShowProjectTransition();
            await Task.Yield();
            var target = workspace as IReviewingEditorWorkspace;
            if (target == null) throw new InvalidOperationException("デモを終了して通常モードで起動してください。");
            FlushMapLayout();
            try { await target.OpenProject(selected); }
            catch
            {
                HideProjectTransition();
                projectPath.SetValueWithoutNotify((workspace as ProjectEditorWorkspace)?.ProjectRoot ?? "");
                throw;
            }
            projectPath.SetValueWithoutNotify((workspace as ProjectEditorWorkspace)?.ProjectRoot ?? selected);
            reviewed.Bind(null, null);
            secondaryPane.Hide(); activeId = null; undoHistories.Clear(); listSignature = null;
            ResetMapContext();
            noteTree.ResetFolder(); listSignature = null;
            var remembered = new HashSet<string>(PlayerPrefs.GetString("CARTOWORDS.NotebookFolders", "").Split('\n')
                .Where(p => !string.IsNullOrWhiteSpace(p)), StringComparer.OrdinalIgnoreCase);
            remembered.Add(projectPath.value);
            PlayerPrefs.SetString("CARTOWORDS.NotebookFolders", string.Join("\n", remembered));
            PlayerPrefs.SetString("CARTOWORDS.LastProject", projectPath.value); PlayerPrefs.Save();
            QueueRepaint();
            HideProjectTransition();
        }
        private void ShowProjectTransition()
        {
            if (projectTransition == null) return;
            projectTransition.style.display = DisplayStyle.Flex; projectTransition.BringToFront(); projectTransition.Focus();
            projectTransition.MarkDirtyRepaint();
        }
        private void HideProjectTransition()
        {
            transitionHideJob?.Pause();
            transitionHideJob = projectTransition?.schedule.Execute(() =>
            {
                if (this == null) return;
                if (choosingProjectForTransition || busy) { HideProjectTransition(); return; }
                QueueRepaint(); projectTransition.style.display = DisplayStyle.None;
            }).StartingIn(100);
        }
        // 地図の点線(自動検出の候補)を承認する: どちらかのノートの末尾にリンクを書き足す。
        // いま開いているノートが片方なら、そのノートに足す。
        private void AcceptCandidate(MapCandidate candidate) => Run(async () =>
        {
            var target = workspace as ProjectEditorWorkspace;
            if (target == null) return;
            string owner = candidate.ToNote == activeId ? candidate.ToNote : candidate.FromNote;
            string other = owner == candidate.FromNote ? candidate.ToNote : candidate.FromNote;
            await target.AppendRelatedLink(owner, other);
            if (owner == activeId) reviewed.Bind(null, null);
        });
        private void RejectCandidate(MapCandidate candidate)
        {
            if (!(workspace is ProjectEditorWorkspace target)) return;
            if (!target.RejectRelated(candidate.FromNote, candidate.ToNote))
                message.text = "却下を保存できませんでした。この起動の間だけ、提案しません。";
            mapVersionSignature = null; QueueMapRequest(workspace.Documents);
        }
        private async Task DisconnectRelated(string targetId)
        {
            if (!(workspace is IReviewingEditorWorkspace source) || activeId == null)
                throw new InvalidOperationException("ノートを開いてください。");
            await RelatedLinkRemoval.Disconnect(source, activeId, targetId);
            reviewed.Bind(null, null);
        }
        private bool choosingProjectForTransition
        {
            get
            {
#if UNITY_EDITOR
                return choosingProject;
#else
                return false;
#endif
            }
        }
        private void QueueRepaint()
        {
            root?.schedule.Execute(() =>
            {
                if (this == null || root?.panel == null) return;
                root.Query<VisualElement>().ForEach(element => element.MarkDirtyRepaint());
                root.MarkDirtyRepaint();
            });
        }
        private void OnApplicationFocus(bool focused) { if (focused) QueueRepaint(); }

        // 拠点の位置は、変わってから少し待って保存する。待っている間にプロジェクトが替わったら、別のプロジェクトへは書かない。
        private IReadOnlyDictionary<string, MapPoint> pendingLayout;
        private string pendingLayoutRoot;
        private IVisualElementScheduledItem layoutSaveJob;
        private void QueueLayoutSave(IReadOnlyDictionary<string, MapPoint> layout)
        {
            if (!(workspace is ProjectEditorWorkspace project) || project.ProjectRoot == null) return;
            pendingLayout = layout; pendingLayoutRoot = project.ProjectRoot;
            layoutSaveJob?.Pause();
            layoutSaveJob = root.schedule.Execute(FlushMapLayout); layoutSaveJob.StartingIn(1500);
        }
        private void FlushMapLayout()
        {
            layoutSaveJob?.Pause();
            var layout = pendingLayout; pendingLayout = null;
            if (layout != null && workspace is ProjectEditorWorkspace project && project.ProjectRoot == pendingLayoutRoot)
                project.SaveMapLayout(layout);
        }
        private void ResetMapContext()
        {
            FlushMapLayout();
            rich?.ResetHistory(); secondaryPane?.ResetHistory();
            mapRequestJob?.Pause(); mapProjectKey = Guid.NewGuid().ToString("N");
            mapGeneration = 0; mapVersionSignature = null; mapGate.Reset(mapProjectKey);
            displayedMap = null; relatedControl?.SetConnections(Array.Empty<string>()); relatedControl?.SetSuggestions(null);
            mapPanel?.Present(null); focusCursor = -1; focusNote = null; focusText = null;
            // 開いたプロジェクトに保存してあった拠点の位置を、最初の解析結果が来る前に渡す。
            if (workspace is ProjectEditorWorkspace project && project.ProjectRoot != null) mapPanel?.SetSavedLayout(project.LoadMapLayout());
        }
        private static IEnumerable<string> FolderPaths(Cartograph.Core.FolderNode rootFolder)
        {
            if (rootFolder == null) yield break;
            foreach (var folder in rootFolder.Folders)
            {
                yield return folder.Path;
                foreach (var nested in FolderPaths(folder)) yield return nested;
            }
        }
        private Dictionary<string, long> MapVersions() => workspace.Documents.ToDictionary(d => d.id, d => d.revision, StringComparer.Ordinal);
        private void QueueMapRequest(IReadOnlyList<DocumentRecord> documents)
        {
            var signature = string.Join("|", documents.Select(d => d.id.Length + ":" + d.id + ":" + d.revision));
            if (signature == mapVersionSignature) return;
            mapVersionSignature = signature;
            mapRequestJob?.Pause(); mapPanel.Waiting();
            mapRequestJob = root.schedule.Execute(() =>
            {
                if (this == null || workspace == null) return;
                if (busy || Composing) { mapVersionSignature = null; QueueMapRequest(workspace.Documents); return; }
                var request = new MapRequest(mapProjectKey, ++mapGeneration, MapVersions());
                MapRefreshRequested?.Invoke(request);
                AnalyzeMap(request);
            }).StartingIn(500);
        }
        // Analyzer adapter publishes a COMPLETE snapshot on the UI thread, or this
        // method marshals it there. Analysis owns parsing; MapUI owns only drawing.
        public void PresentMap(MapSnapshot snapshot)
        {
            if (uiContext != null && System.Threading.Thread.CurrentThread.ManagedThreadId != uiThread)
            { uiContext.Post(_ => PresentMap(snapshot), null); return; }
            if (this == null || workspace == null || mapPanel == null || snapshot == null ||
                snapshot.Request.Generation != mapGeneration || !mapGate.TryAccept(snapshot, MapVersions())) return;
            mapPanel.Present(snapshot);
            displayedMap = snapshot;
            UpdateMapFocus(true);
            relatedControl.SetConnections(MapAnalysisAdapter.ConnectedNotes(snapshot, workspace.Current?.id));
            relatedControl.SetSuggestions(MapAnalysisAdapter.SuggestedNotes(snapshot, workspace.Current?.id));
        }
        private async void AnalyzeMap(MapRequest request)
        {
            if (!(workspace is ProjectEditorWorkspace source) || source.Tree == null) return;
            bool Current() => this != null && ReferenceEquals(workspace, source) &&
                mapProjectKey == request.ProjectKey && mapGeneration == request.Generation;
            try
            {
                var snapshot = await MapAnalysisAdapter.Analyze(source, request, Current, source.RejectedRelatedPairs().ToArray());
                if (Current()) PresentMap(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Current()) mapPanel.ShowError("マップを更新できません：" + ex.Message); }
        }

        private void ShowReviewNotice(string text)
        {
            ShowPasteNotice(); pasteNotice.text = text;
        }

        private void WrapSelection(string marker)
        {
            if (Composing) return;
            if (rich.Enabled && !rich.Submit(ChangeOrigin.Typing)) return;
            var text = body.value ?? "";
            var start = Mathf.Clamp(Math.Min(EditorCursor, EditorSelection), 0, text.Length);
            var end = Mathf.Clamp(Math.Max(EditorCursor, EditorSelection), start, text.Length);
            var selected = text.Substring(start, end - start);
            var replacement = selected.Contains("\n") ? string.Join("\n", selected.Split('\n').Select(line =>
                line.Length == 0 ? line : marker + line + marker)) : marker + selected + marker;
            SetBody(text.Substring(0, start) + replacement + text.Substring(end), start + marker.Length,
                start + replacement.Length - marker.Length);
        }

        private void PrefixLine(string prefix)
        {
            if (Composing) return;
            if (rich.Enabled && !rich.Submit(ChangeOrigin.Typing)) return;
            var text = body.value ?? "";
            int selectionStart = Mathf.Clamp(Math.Min(EditorCursor, EditorSelection), 0, text.Length);
            int selectionEnd = Mathf.Clamp(Math.Max(EditorCursor, EditorSelection), 0, text.Length);
            if (selectionStart != selectionEnd && text.Substring(selectionStart, selectionEnd - selectionStart).Contains("\n"))
            {
                int first = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
                int last = text.IndexOf('\n', selectionEnd > 0 && text[selectionEnd - 1] == '\n' ? selectionEnd - 1 : selectionEnd);
                if (last < 0) last = text.Length;
                string changed = string.Join("\n", text.Substring(first, last - first).Split('\n').Select(line =>
                {
                    if (prefix.Length == 0 || prefix.StartsWith("#", StringComparison.Ordinal))
                        line = System.Text.RegularExpressions.Regex.Replace(line, @"^#{1,6}[ \t]+", "");
                    return prefix + line;
                }));
                SetBody(text.Substring(0, first) + changed + text.Substring(last), first, first + changed.Length);
                return;
            }
            var cursor = Mathf.Clamp(EditorCursor, 0, text.Length);
            var start = cursor == 0 ? 0 : text.LastIndexOf('\n', cursor - 1) + 1;
            int remove = 0;
            if (prefix.Length == 0 || prefix.StartsWith("#", StringComparison.Ordinal))
                remove = System.Text.RegularExpressions.Regex.Match(text.Substring(start), @"^#{1,6}[ \t]+").Length;
            var updated = text.Remove(start, remove).Insert(start, prefix);
            int position = Math.Max(start + prefix.Length, cursor + prefix.Length - remove);
            SetBody(updated, position, position);
        }

        private void SetBody(string value, int start, int end)
        {
            if (rich.Enabled)
            { reviewed.Bind(null, null); reviewed.Bind(workspace as IReviewingEditorWorkspace, workspace.Current); }
            body.SetValueWithoutNotify(value);
            SubmitBody(ChangeOrigin.Format); UpdateCount();
            body.SelectRange(start, end);
            richCursor = start; richSelection = end;
            if (rich.Enabled) { rich.Bind(workspace as IReviewingEditorWorkspace, workspace.Current); rich.FocusRaw(start, end); }
            else body.Focus();
        }

        private void GuardClipboard(TextField field)
        {
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (reviewed.Enabled) return;
                bool paste = ((evt.ctrlKey || evt.commandKey) && evt.keyCode == KeyCode.V) ||
                    (evt.shiftKey && evt.keyCode == KeyCode.Insert);
                if (!paste) return;
                evt.StopImmediatePropagation();
                ShowPasteNotice();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<ValidateCommandEvent>(evt =>
            {
                if (reviewed.Enabled) return;
                if (evt.commandName != "Paste") return;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<ExecuteCommandEvent>(evt =>
            {
                if (reviewed.Enabled) return;
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
            if (rich != null && rich.Enabled) { undoButton.SetEnabled(rich.CanUndo); redoButton.SetEnabled(rich.CanRedo); return; }
            if (reviewed.Enabled) { undoButton.SetEnabled(reviewed.CanUndo); redoButton.SetEnabled(reviewed.CanRedo); return; }
            bool has = activeId != null && undoHistories.ContainsKey(activeId);
            undoButton.SetEnabled(has && undoHistories[activeId].CanUndo);
            redoButton.SetEnabled(has && undoHistories[activeId].CanRedo);
        }

        private void ReplayUndo(bool redo)
        {
            if (rich.Enabled) { rich.Replay(redo); UpdateUndoButtons(); return; }
            if (reviewed.Enabled) { reviewed.Replay(redo); UpdateUndoButtons(); return; }
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
            count.text = $"{MarkdownTextCount.Count(body.value):N0} 文字（記号・改行を除く）";
        }

        private void RequestCreateFolder(string parent)
        {
            if (!(workspace is ProjectEditorWorkspace target) || target.Tree == null) return;
            var tree = target.Tree;
            noteDialog.ShowCreateFolder(FolderPaths(tree), parent, (folder, name) => Execute(() =>
            {
                if (!ReferenceEquals(tree, target.Tree)) throw new InvalidOperationException("保存先を選び直してください。");
                target.CreateFolder(folder, name); listSignature = null;
                return Task.CompletedTask;
            }));
        }
        private void RequestRename(string id)
        {
            var target = workspace as ProjectEditorWorkspace;
            var record = workspace.Documents.FirstOrDefault(d => d.id == id);
            if (target == null || record == null) return;
            var tree = target.Tree;
            noteDialog.ShowRename(record.title, name => Execute(async () =>
            {
                if (!ReferenceEquals(tree, target.Tree)) throw new InvalidOperationException("ノートを選び直してください。");
                await target.RenameDocument(id, name);
                if (secondaryPane.DocumentId == id) secondaryPane.Hide();
                undoHistories.Remove(id); ResetMapContext();
            }));
        }

        private void RequestCloseProject()
        {
            if (busy) return;
            Func<Task> close = () => Execute(async () =>
            {
                if (!(workspace is ProjectEditorWorkspace target)) throw new InvalidOperationException("通常モードで開いてください。");
                FlushMapLayout();
                await target.CloseProject();
                projectPath.SetValueWithoutNotify("");
                secondaryPane.Hide(); reviewed.Bind(null, null); activeId = null; undoHistories.Clear();
                ResetMapContext();
            });
            bool pending = Composing || (rich.Enabled ? rich.HasPending : reviewed.HasPending) ||
                secondaryPane.HasPending || workspace.State != SaveState.Saved ||
                (workspace is ProjectEditorWorkspace book && book.ProjectRoot != null && title.value != submittedTitle);
            if (pending) noteDialog.ShowCloseConfirmation(close);
            else RunClose(close);
        }
        private async void RunClose(Func<Task> close)
        {
            try { await close(); }
            catch (Exception ex) { Refresh(); message.text = "閉じられませんでした：" + ex.Message; }
        }

        private void UpdateSaveStatus()
        {
            bool pending = Composing || (rich.Enabled ? rich.HasPending : reviewed.HasPending) || secondaryPane.HasPending ||
                (activeId != null && title.value != submittedTitle);
            status.text = workspace.Current == null ? "プロジェクト・ノート未選択" :
                !workspace.IsPersistent ? "デモ：保存されません" :
                workspace.State == SaveState.Error ? "保存失敗・未保存" :
                pending || workspace.State == SaveState.Dirty || workspace.State == SaveState.Saving ? "保存中…" :
                workspace.State == SaveState.Saved ? "保存済み" : "未保存";
        }

        // ---- 画面サイズへの追従 -----------------------------------------------------------
        // UI の拡大率は、画面(ゲームビュー)の大きさに合わせて決める。基準は 1600x900 で 1.0。
        // 小さい画面で大きすぎず、大きい画面で小さすぎないよう、幅・高さの小さいほうの比で、範囲を区切る。
        private const float ReferenceWidth = 1600f, ReferenceHeight = 900f, MinUiScale = 0.75f, MaxUiScale = 2f;
        internal static float UiScaleFor(float width, float height)
        {
            if (width <= 0 || height <= 0) return 1f;
            return Mathf.Clamp(Mathf.Min(width / ReferenceWidth, height / ReferenceHeight), MinUiScale, MaxUiScale);
        }
        private int appliedScreenWidth, appliedScreenHeight;
        private void ApplyUiScale()
        {
            if (ownedPanel == null || (Screen.width == appliedScreenWidth && Screen.height == appliedScreenHeight)) return;
            appliedScreenWidth = Screen.width; appliedScreenHeight = Screen.height;
            ownedPanel.scale = UiScaleFor(Screen.width, Screen.height);
        }
        // 論理ピクセル(拡大率を引いた後)での画面幅。
        private float LogicalWidth() => Screen.width / (ownedPanel != null ? Mathf.Max(0.1f, ownedPanel.scale) : 1f);

        // 索引とマップの幅は、画面幅に対する割合で決める。ユーザーがドラッグで動かした時だけ、その割合を保存する。
        private const string IndexRatioKey = "CARTOWORDS.IndexRatio";
        private const string MapRatioKey = "CARTOWORDS.MapRatio";
        private float PaneWidth(string key, float defaultRatio, float min, float max)
        {
            float width = LogicalWidth();
            float ratio = PlayerPrefs.GetFloat(key, defaultRatio);
            if (ratio < 0.05f || ratio > 0.8f) ratio = defaultRatio;
            return Mathf.Clamp(width * ratio, min, max);
        }
        // 分割のつまみを離した時に、固定側ペインの幅の割合を保存する。
        private void RememberRatio(TwoPaneSplitView split, VisualElement pane, string key)
        {
            var handle = SplitResizeCursor.HandleOf(split);
            if (handle == null) return;
            handle.RegisterCallback<PointerUpEvent>(_ =>
            {
                float width = LogicalWidth();
                if (width > 1 && pane.resolvedStyle.width >= 100) PlayerPrefs.SetFloat(key, pane.resolvedStyle.width / width);
            }, TrickleDown.TrickleDown);
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
            mapRequestJob?.Pause(); FlushMapLayout();
#if ENABLE_INPUT_SYSTEM
            if (imeKeyboard != null) { imeKeyboard.onIMECompositionChange -= OnComposition; imeKeyboard.onTextInput -= OnTextInput; }
#endif
            if (workspace != null) workspace.Changed -= Refresh;
            if (ownedPanel != null) Destroy(ownedPanel);
            if (ownedFontAsset != null) Destroy(ownedFontAsset);
            if (ownedFont != null) Destroy(ownedFont);
        }
    }
}
