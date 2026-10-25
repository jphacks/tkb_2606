using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>VSCode風のノート索引。展開、並べ替え、絞り込み、キーボード操作、ドラッグ移動を持つ。</summary>
    public sealed class NoteTreeView : VisualElement
    {
        private const string SortPrefKey = "CARTOWORDS.IndexSort";
        private const float DragThreshold = 6f;

        private sealed class Row { public NoteTreeNode Node; public VisualElement Element; }

        private NoteTreeNode root;
        private string activeId, emptyText, revealedId, cursorPath = "", filter = "";
        private readonly HashSet<string> expanded = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Row> rows = new List<Row>();
        private readonly ScrollView list = new ScrollView();
        private readonly TextField search = new TextField();
        private readonly Button sortKeyButton, directionButton, foldersButton;
        private readonly Label dragHint = new Label();
        private NoteTreeSort sort = LoadSort();
        private string dropFolder;
        private VisualElement dropElement;

        public string CurrentFolder { get; private set; } = "";
        public new NoteTreeSort Sort => sort;

        public event Action<string> NoteSelected;
        public event Action<string> DeleteRequested;
        public event Action<string> RenameRequested;
        public event Action<string> CreateFolderRequested;
        public event Action<string> CreateNoteRequested;
        /// <summary>ノート(ID)を別のフォルダ(相対パス。ルートは空)へ移す要求。</summary>
        public event Action<string, string> MoveRequested;

        public NoteTreeView()
        {
            focusable = true; tabIndex = 0;
            style.flexGrow = 1; style.minHeight = 0;
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            search.AddToClassList("tree-search");
            search.textEdition.placeholder = "ノートを検索";
            search.style.marginBottom = 3;
            search.RegisterValueChangedCallback(evt => { filter = (evt.newValue ?? "").Trim(); Rebuild(); });
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape && search.value.Length > 0) { search.value = ""; evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.DownArrow) { Focus(); evt.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            Add(search);

            var sortRow = new VisualElement(); sortRow.style.flexDirection = FlexDirection.Row; sortRow.style.flexShrink = 0;
            sortRow.style.flexWrap = Wrap.Wrap; // 索引が狭いときは折り返す
            sortKeyButton = new Button(OpenSortMenu) { tooltip = "並べ替えの基準" };
            directionButton = new Button(() => SetSort(new NoteTreeSort(sort.Key, !sort.Descending, sort.FoldersFirst))) { };
            foldersButton = new Button(() => SetSort(new NoteTreeSort(sort.Key, sort.Descending, !sort.FoldersFirst))) { };
            foreach (var button in new[] { sortKeyButton, directionButton, foldersButton })
            { button.style.marginRight = 3; button.style.marginBottom = 3; sortRow.Add(button); }
            sortKeyButton.style.flexShrink = 0;
            Add(sortRow);

            list.style.flexGrow = 1; list.style.minHeight = 0;
            Add(list);
            dragHint.style.display = DisplayStyle.None; dragHint.AddToClassList("muted");
            dragHint.style.whiteSpace = WhiteSpace.NoWrap; dragHint.style.flexShrink = 0;
            Add(dragHint);
            RefreshSortButtons();
        }

        public void ResetFolder()
        {
            CurrentFolder = ""; cursorPath = ""; revealedId = null; expanded.Clear();
        }

        /// <summary>ノートを含むフォルダを展開し、選択位置として覚える。</summary>
        public void RevealNote(string id)
        {
            int slash = (id ?? "").LastIndexOf('/');
            CurrentFolder = slash < 0 ? "" : id.Substring(0, slash);
            for (var folder = CurrentFolder; folder.Length > 0; folder = ParentOf(folder)) expanded.Add(folder);
            var node = root == null ? null : FindNote(root, id);
            if (node != null) cursorPath = node.Path;
        }

        public void Render(NoteTreeNode root, string activeDocumentId, string emptyMessage = "ノートはまだありません")
        {
            this.root = root; emptyText = emptyMessage;
            activeId = activeDocumentId;
            // 開いているノートが変わったときだけ展開する。手で畳んだフォルダを再描画で開き直さない。
            if (activeId != revealedId) { RevealNote(activeId); revealedId = activeId; }
            if (FindFolder(root, CurrentFolder) == null) CurrentFolder = "";
            Rebuild();
        }

        private static string ParentOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }
        private static NoteTreeNode FindFolder(NoteTreeNode node, string path)
        {
            if (node.Path == path) return node;
            foreach (var child in node.Children.Where(n => n.IsFolder))
            { var found = FindFolder(child, path); if (found != null) return found; }
            return null;
        }
        private static NoteTreeNode FindNote(NoteTreeNode node, string id)
        {
            foreach (var child in node.Children)
            {
                var found = child.IsFolder ? FindNote(child, id) : child.Note.DocumentId == id ? child : null;
                if (found != null) return found;
            }
            return null;
        }

        // ---- 並べ替え -------------------------------------------------------------------

        private static NoteTreeSort LoadSort()
        {
            try
            {
                var parts = PlayerPrefs.GetString(SortPrefKey, "").Split('|');
                if (parts.Length == 3 && Enum.TryParse(parts[0], out NoteSortKey key))
                    return new NoteTreeSort(key, parts[1] == "1", parts[2] != "0");
            }
            catch (Exception) { }
            return NoteTreeSort.Default;
        }
        private void SetSort(NoteTreeSort next)
        {
            sort = next;
            PlayerPrefs.SetString(SortPrefKey, next.Key + "|" + (next.Descending ? "1" : "0") + "|" + (next.FoldersFirst ? "1" : "0"));
            RefreshSortButtons(); Rebuild();
        }
        private static string KeyLabel(NoteSortKey key) =>
            key == NoteSortKey.Updated ? "更新日時" : key == NoteSortKey.Created ? "作成日時" : "名前";
        private void RefreshSortButtons()
        {
            sortKeyButton.text = KeyLabel(sort.Key) + " ▾";
            directionButton.text = sort.Descending ? "降順 ↓" : "昇順 ↑";
            directionButton.tooltip = sort.Key == NoteSortKey.Name
                ? "名前の昇順(あ→ん)/降順を切り替える"
                : "昇順は古い順、降順は新しい順。クリックで切り替える";
            foldersButton.text = sort.FoldersFirst ? "フォルダ優先" : "混在";
            foldersButton.tooltip = "フォルダを先頭にまとめるか、ノートと混ぜて並べるかを切り替える";
        }
        private void OpenSortMenu()
        {
            var menu = new GenericDropdownMenu();
            foreach (var key in new[] { NoteSortKey.Name, NoteSortKey.Updated, NoteSortKey.Created })
            {
                var chosen = key;
                menu.AddItem(KeyLabel(key), sort.Key == key, () => SetSort(new NoteTreeSort(chosen, sort.Descending, sort.FoldersFirst)));
            }
            menu.DropDown(sortKeyButton.worldBound, sortKeyButton, default(DropdownMenuSizeMode));
        }

        // ---- 描画 -----------------------------------------------------------------------

        private void Rebuild()
        {
            list.Clear(); rows.Clear();
            if (root == null) return;
            root.Sort(sort);
            bool filtering = filter.Length > 0;
            int shown = AddChildren(root, 0, filtering);
            if (shown == 0)
            {
                var text = filtering ? "一致するノートがありません" : emptyText;
                if (!string.IsNullOrEmpty(text)) list.Add(new Label(text));
            }
        }

        private int AddChildren(NoteTreeNode node, int depth, bool filtering)
        {
            int count = 0;
            foreach (var child in node.Children)
            {
                if (child.IsFolder)
                {
                    if (filtering && !HasMatch(child)) continue;
                    AddRow(child, depth); count++;
                    if (filtering || expanded.Contains(child.Path)) count += AddChildren(child, depth + 1, filtering);
                }
                else if (!filtering || Matches(child)) { AddRow(child, depth); count++; }
            }
            return count;
        }
        private bool Matches(NoteTreeNode note)
        {
            var title = string.IsNullOrWhiteSpace(note.Note.Title) ? note.Name : note.Note.Title;
            return title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || note.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private bool HasMatch(NoteTreeNode folder) =>
            folder.Children.Any(c => c.IsFolder ? HasMatch(c) : Matches(c));

        private void AddRow(NoteTreeNode node, int depth)
        {
            var row = new VisualElement();
            row.AddToClassList("tree-row");
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
            row.style.height = 24; row.style.flexShrink = 0;
            row.style.paddingLeft = 6 + depth * 14;
            string text;
            if (node.IsFolder)
            {
                text = (expanded.Contains(node.Path) || filter.Length > 0 ? "▾ " : "▸ ") + node.Name;
                row.tooltip = node.Path + "（右クリックでメニュー）";
                if (node.Path == CurrentFolder) row.AddToClassList("tree-row--folder-current");
            }
            else
            {
                text = string.IsNullOrWhiteSpace(node.Note.Title) ? node.Name : node.Note.Title;
                row.tooltip = node.Note.RelativePath + "（右クリックで名前変更・削除／ドラッグで移動）";
                if (node.Note.DocumentId == activeId) row.AddToClassList("tree-row--selected");
            }
            if (node.Path == cursorPath) row.AddToClassList("tree-row--cursor");
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.flexGrow = 1; label.style.flexShrink = 1; label.style.minWidth = 0;
            label.style.whiteSpace = WhiteSpace.NoWrap; label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis; label.style.fontSize = 13;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(label);
            WireRow(row, node);
            list.Add(row); rows.Add(new Row { Node = node, Element = row });
        }

        // ---- 操作 -----------------------------------------------------------------------

        private void Activate(NoteTreeNode node)
        {
            cursorPath = node.Path;
            if (node.IsFolder)
            {
                CurrentFolder = node.Path;
                if (!expanded.Remove(node.Path)) expanded.Add(node.Path);
                Rebuild();
            }
            else
            {
                CurrentFolder = ParentOf(node.Path);
                NoteSelected?.Invoke(node.Note.DocumentId);
            }
        }

        private void WireRow(VisualElement row, NoteTreeNode node)
        {
            Vector2 start = default; bool pressed = false, dragging = false;
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = true; dragging = false; start = evt.position;
                row.CapturePointer(evt.pointerId); Focus();
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed || node.IsFolder) return;
                if (!dragging && (evt.position - (Vector3)start).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;
                if (dragging) UpdateDrag(node, evt.position);
            });
            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == 1) { ShowMenu(node, row, evt.position); evt.StopImmediatePropagation(); return; }
                if (evt.button != 0 || !pressed) return;
                pressed = false;
                if (row.HasPointerCapture(evt.pointerId)) row.ReleasePointer(evt.pointerId);
                if (dragging) { dragging = false; EndDrag(node); }
                else Activate(node);
                evt.StopPropagation();
            });
            row.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (dragging) ClearDrag();
                pressed = false; dragging = false;
            });
        }

        private void ShowMenu(NoteTreeNode node, VisualElement row, Vector2 position)
        {
            var menu = new GenericDropdownMenu();
            if (node.IsFolder)
            {
                menu.AddItem("この中にノートを作成", false, () => CreateNoteRequested?.Invoke(node.Path));
                menu.AddItem("この中にフォルダを作成", false, () => CreateFolderRequested?.Invoke(node.Path));
            }
            else
            {
                var id = node.Note.DocumentId;
                menu.AddItem("開く", false, () => NoteSelected?.Invoke(id));
                menu.AddItem("名前を変更…", false, () => RenameRequested?.Invoke(id));
                menu.AddItem("削除…", false, () => DeleteRequested?.Invoke(id));
            }
            menu.DropDown(new Rect(position.x, position.y, 1, 1), row, default(DropdownMenuSizeMode));
        }

        // ---- ドラッグ移動 ---------------------------------------------------------------

        private void UpdateDrag(NoteTreeNode dragged, Vector2 position)
        {
            string target = null; VisualElement element = null;
            foreach (var row in rows)
            {
                if (!row.Element.worldBound.Contains(position)) continue;
                target = row.Node.IsFolder ? row.Node.Path : ParentOf(row.Node.Path);
                element = row.Node.IsFolder ? row.Element : null;
                break;
            }
            // 行の外でも索引の中なら、ルートへ移す指定になる。
            if (target == null && list.worldBound.Contains(position)) target = "";
            if (target == ParentOf(dragged.Path)) target = null; // 今いる場所へは移さない
            if (!ReferenceEquals(element, dropElement))
            {
                dropElement?.RemoveFromClassList("tree-row--drop");
                dropElement = element; dropElement?.AddToClassList("tree-row--drop");
            }
            dropFolder = target;
            dragHint.text = target == null ? "ここには移せません" : "移動先: " + (target.Length == 0 ? "（一番上）" : target);
            dragHint.style.display = DisplayStyle.Flex;
        }
        private void EndDrag(NoteTreeNode dragged)
        {
            var target = dropFolder; ClearDrag();
            if (target != null) MoveRequested?.Invoke(dragged.Note.DocumentId, target);
        }
        private void ClearDrag()
        {
            dropElement?.RemoveFromClassList("tree-row--drop"); dropElement = null;
            dropFolder = null; dragHint.style.display = DisplayStyle.None;
        }

        // ---- キーボード -----------------------------------------------------------------

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (rows.Count == 0) return;
            int index = rows.FindIndex(r => r.Node.Path == cursorPath);
            var current = index >= 0 ? rows[index].Node : null;
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow: MoveCursor(Mathf.Min(rows.Count - 1, index + 1)); break;
                case KeyCode.UpArrow: MoveCursor(Mathf.Max(0, index < 0 ? 0 : index - 1)); break;
                case KeyCode.Home: MoveCursor(0); break;
                case KeyCode.End: MoveCursor(rows.Count - 1); break;
                case KeyCode.RightArrow:
                    if (current != null && current.IsFolder && filter.Length == 0 && !expanded.Contains(current.Path))
                    { expanded.Add(current.Path); Rebuild(); ScrollToCursor(); }
                    else MoveCursor(Mathf.Min(rows.Count - 1, index + 1));
                    break;
                case KeyCode.LeftArrow:
                    if (current != null && current.IsFolder && filter.Length == 0 && expanded.Contains(current.Path))
                    { expanded.Remove(current.Path); Rebuild(); ScrollToCursor(); }
                    else if (current != null)
                    {
                        var parent = rows.FindIndex(r => r.Node.IsFolder && r.Node.Path == ParentOf(current.Path));
                        if (parent >= 0) MoveCursor(parent);
                    }
                    break;
                case KeyCode.Return: case KeyCode.KeypadEnter: if (current != null) Activate(current); break;
                case KeyCode.F2: if (current != null && !current.IsFolder) RenameRequested?.Invoke(current.Note.DocumentId); break;
                case KeyCode.Delete: if (current != null && !current.IsFolder) DeleteRequested?.Invoke(current.Note.DocumentId); break;
                default: return;
            }
            evt.StopPropagation();
        }

        private void MoveCursor(int index)
        {
            if (index < 0 || index >= rows.Count) return;
            var node = rows[index].Node;
            cursorPath = node.Path;
            CurrentFolder = node.IsFolder ? node.Path : ParentOf(node.Path);
            foreach (var row in rows) row.Element.EnableInClassList("tree-row--cursor", row.Node.Path == cursorPath);
            ScrollToCursor();
        }
        private void ScrollToCursor()
        {
            var row = rows.FirstOrDefault(r => r.Node.Path == cursorPath);
            if (row != null) list.ScrollTo(row.Element);
        }
    }
}
