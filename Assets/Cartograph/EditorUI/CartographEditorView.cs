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
        private SecondaryNotePane secondaryPane;
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
        private FontAsset ownedFontAsset;
        private ReviewedTextBinding reviewed;
        private TextField projectPath;
        private Button newNoteButton;
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
                ownedPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                ownedPanel.scale = 1;
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
            projectPath = new TextField { label = "プロジェクト", tooltip = "notes と .cartograph を保存するフォルダの絶対パス" };
            projectPath.style.width = 360; projectPath.style.maxWidth = Length.Percent(55); projectBar.Add(projectPath);
            projectPath.textEdition.placeholder = "ファイルが選択されていません";
            Button(projectBar, "ファイルを選択", SelectProjectFolder);
            Button(projectBar, "閉じる", () => Run(async () =>
            {
                if (!(workspace is ProjectEditorWorkspace target)) throw new InvalidOperationException("通常モードで開いてください。");
                await target.CloseProject();
                projectPath.SetValueWithoutNotify("");
                secondaryPane.Hide(); reviewed.Bind(null, null); activeId = null; undoHistories.Clear();
                ResetMapContext();
            }));

            var main = new TwoPaneSplitView(0, 520, TwoPaneSplitViewOrientation.Horizontal);
            main.style.flexGrow = 1; main.style.minHeight = 0; root.Add(main);
            mapPanel = new MapPanel(); main.Add(mapPanel);
            mapPanel.OpenNoteRequested += OpenNoteBeside;
            var rightWorkspace = new VisualElement(); rightWorkspace.AddToClassList("right-workspace");
            rightWorkspace.style.minWidth = 380; rightWorkspace.style.flexGrow = 1; main.Add(rightWorkspace);
            var noteHeader = new VisualElement(); noteHeader.AddToClassList("note-header"); rightWorkspace.Add(noteHeader);
            var workbench = Row(rightWorkspace); workbench.AddToClassList("workbench");
            workbench.style.flexGrow = 1; workbench.style.minHeight = 0;
            var sidebar = new VisualElement();
            sidebar.AddToClassList("sidebar");
            sidebar.style.width = 145; sidebar.style.minWidth = 120; sidebar.style.flexShrink = 0;
            sidebar.style.paddingLeft = 12; sidebar.style.paddingRight = 12;
            workbench.Add(sidebar);
            projectLabel = new Label(); projectLabel.style.whiteSpace = WhiteSpace.Normal;
            sidebar.Add(projectLabel);
            newNoteButton = Button(sidebar, "＋ 新しいノート", () => Run(async () =>
            {
                await CreateEditableNote();
            })); newNoteButton.AddToClassList("primary-action");
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; sidebar.Add(scroll);
            noteTree = new NoteTreeView(); scroll.Add(noteTree);
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
            noteTree.RenameRequested += id =>
            {
                var record = workspace.Documents.FirstOrDefault(d => d.id == id); if (record == null) return;
                var target = workspace as ProjectEditorWorkspace; if (target == null) return;
                var tree = target.Tree;
                noteDialog.ShowRename(record.title, name => Execute(async () =>
                {
                    if (!ReferenceEquals(tree, target.Tree)) throw new InvalidOperationException("ノートを選び直してください。");
                    await target.RenameDocument(id, name);
                    if (secondaryPane.DocumentId == id) secondaryPane.Hide();
                    undoHistories.Remove(id); ResetMapContext();
                }));
            };
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
            noteHeader.Add(breadcrumb);
            title = new TextField { name = "document-title" };
            title.AddToClassList("note-title");
            title.style.fontSize = 21; title.style.marginBottom = 2;
            title.style.flexShrink = 0;
            noteHeader.Add(title);
            var toolbar = Row(paper); toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.AddToClassList("format-toolbar");
            toolbar.style.flexShrink = 0;
            undoButton = Button(toolbar, "元に戻す", () => ReplayUndo(false));
            redoButton = Button(toolbar, "やり直し", () => ReplayUndo(true));
            Button(toolbar, "大見出し", () => PrefixLine("# "));
            Button(toolbar, "見出し", () => PrefixLine("## "));
            Button(toolbar, "小見出し", () => PrefixLine("### "));
            Button(toolbar, "本文", () => PrefixLine(""));
            Button(toolbar, "太字", () => WrapSelection("**"));
            Button(toolbar, "斜体", () => WrapSelection("*"));
            Button(toolbar, "箇条書き", () => PrefixLine("- "));
            Button(toolbar, "番号付き", () => PrefixLine("1. "));
            Button(toolbar, "保存を再試行", () => Run(() => workspace.FlushSave()));
            Button(toolbar, "分割して開く", () => OpenNoteBeside(activeId, 1));
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
            if (root == null || workspace == null || busy || Composing) return;
            // The frame after IME confirmation also submits the final value.
            if (rich.Enabled) rich.Tick();
            else if (reviewed.Enabled) reviewed.Tick();
            else { SubmitTitle(); SubmitBody(ChangeOrigin.Typing); }
            secondaryPane.Tick();
            UpdateSaveStatus(); UpdateUndoButtons();
            if (activeId != null && undoHistories.TryGetValue(activeId, out var history))
                history.UpdateSelection(body.cursorIndex, body.selectIndex);
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
            bool projectOpen = !(workspace is IReviewingEditorWorkspace project) || project.Tree != null;
            var actualRoot = (workspace as ProjectEditorWorkspace)?.ProjectRoot;
            var pathFocus = projectPath.panel?.focusController.focusedElement as VisualElement;
            bool editingPath = pathFocus != null && (ReferenceEquals(pathFocus, projectPath) || projectPath.Contains(pathFocus));
            if (actualRoot != null && !editingPath) projectPath.SetValueWithoutNotify(actualRoot);
            mapPanel.SetProjectOpen(projectOpen);
            emptyNoteMessage.text = projectOpen ? "まだノートが開かれていません。\n「新しいノート」で書き始められます。" :
                "まだノートが開かれていません。\n上のプロジェクト欄から保存先フォルダを開いてください。";
            newNoteButton.SetEnabled(true);
            newNoteButton.text = workspace is IReviewingEditorWorkspace target && target.Tree == null ? "保存先を選んで書く" : "＋ 新しいノート";
            body.tooltip = current == null ? "保存先を選び、ノートを作成すると入力できます。" : "本文を入力";
            title.isReadOnly = body.isReadOnly = current == null;
            if (current == null)
            {
                activeId = null; submittedBody = submittedTitle = "";
                title.SetValueWithoutNotify(""); body.SetValueWithoutNotify("");
            }
            // A save-state notification must never overwrite focused text/IME or reset selection.
            if (current != null && activeId != current.id)
            {
                activeId = current.id;
                submittedTitle = current.title ?? ""; submittedBody = current.markdown ?? "";
                title.SetValueWithoutNotify(submittedTitle); body.SetValueWithoutNotify(submittedBody);
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
            var documents = workspace.Documents;
            relatedControl.SetNotes(current?.id, documents);
            relatedControl.SetConnections(MapAnalysisAdapter.ConnectedNotes(displayedMap, current?.id));
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
            var selectedEntry = entries.FirstOrDefault(e => e.DocumentId == activeId);
            breadcrumb.text = selectedEntry == null ? "notes" : "notes / " + selectedEntry.RelativePath.Replace("/", " / ");
            var signature = new StringBuilder(activeId).Append(projectOpen ? "|open" : "|closed");
            var folderPaths = workspace is IReviewingEditorWorkspace reviewing ? FolderPaths(reviewing.Tree).ToArray() : Array.Empty<string>();
            foreach (var folder in folderPaths) signature.Append("|folder:").Append(folder.Length).Append(':').Append(folder);
            foreach (var item in entries) signature.Append('|').Append(item.DocumentId.Length).Append(':').Append(item.DocumentId)
                .Append(':').Append(item.RelativePath.Length).Append(':').Append(item.RelativePath)
                .Append(':').Append(item.Title.Length).Append(':').Append(item.Title);
            // Body edits/save notifications must not reconstruct the entire file list.
            if (listSignature != signature.ToString())
            {
                noteTree.Render(NoteTree.Build(entries, folderPaths), activeId,
                    projectOpen ? "ノートはまだありません" : "まだノートが開かれていません");
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
                if (workspace is IReviewedNoteAccess access && activeId != null && title.value != submittedTitle)
                {
                    var oldId = activeId;
                    await access.RenameDocument(oldId, title.value);
                    submittedTitle = workspace.Current.title;
                    if (secondaryPane.DocumentId == oldId)
                        secondaryPane.Open(workspace.Current, 1, true);
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
            else { await workspace.FlushSave(); await workspace.CreateDocument(); }
            body.schedule.Execute(() => { if (rich.Enabled) rich.FocusRaw(0, 0); else { body.Focus(); body.SelectRange(0, 0); } });
        }

        private async Task OpenProjectFolder()
        {
            await OpenProjectAt(projectPath.value);
        }

        private void SelectProjectFolder()
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

        private void ResetMapContext()
        {
            mapRequestJob?.Pause(); mapProjectKey = Guid.NewGuid().ToString("N");
            mapGeneration = 0; mapVersionSignature = null; mapGate.Reset(mapProjectKey);
            displayedMap = null; relatedControl?.SetConnections(Array.Empty<string>());
            mapPanel?.Present(null);
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
            relatedControl.SetConnections(MapAnalysisAdapter.ConnectedNotes(snapshot, workspace.Current?.id));
        }
        private async void AnalyzeMap(MapRequest request)
        {
            if (!(workspace is ProjectEditorWorkspace source) || source.Tree == null) return;
            bool Current() => this != null && ReferenceEquals(workspace, source) &&
                mapProjectKey == request.ProjectKey && mapGeneration == request.Generation;
            try
            {
                var snapshot = await MapAnalysisAdapter.Analyze(source, request, Current);
                if (Current()) PresentMap(snapshot);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Current()) mapPanel.ShowError("マップを更新できません：" + ex.Message); }
        }
        public void SetFlowerColor(Emotion emotion, Color32 color) => mapPanel?.SetFlowerColor(emotion, color);

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
            var replacement = marker + text.Substring(start, end - start) + marker;
            SetBody(text.Substring(0, start) + replacement + text.Substring(end), start + marker.Length,
                end + marker.Length);
        }

        private void PrefixLine(string prefix)
        {
            if (Composing) return;
            if (rich.Enabled && !rich.Submit(ChangeOrigin.Typing)) return;
            var text = body.value ?? "";
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

        private void UpdateSaveStatus()
        {
            bool pending = reviewed.HasPending || secondaryPane.HasPending ||
                (activeId != null && title.value != submittedTitle);
            status.text = workspace.Current == null ? "プロジェクト・ノート未選択" :
                !workspace.IsPersistent ? "デモ：保存されません" :
                workspace.State == SaveState.Error ? "保存失敗・未保存" :
                pending ? "未保存の入力あり" :
                workspace.State == SaveState.Saving ? "保存中…" :
                workspace.State == SaveState.Saved ? "保存済み" : "未保存";
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
            mapRequestJob?.Pause();
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
