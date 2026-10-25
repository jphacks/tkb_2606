using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    public sealed class NoteTreeView : VisualElement
    {
        private NoteTreeNode root;
        private string activeId, emptyText;
        public string CurrentFolder { get; private set; } = "";
        public void ResetFolder() { CurrentFolder = ""; }
        public void RevealNote(string id)
        {
            int slash = (id ?? "").LastIndexOf('/');
            CurrentFolder = slash < 0 ? "" : id.Substring(0, slash);
        }
        public event Action<string> NoteSelected;
        public event Action<string> DeleteRequested;
        public event Action<string> RenameRequested;
        public event Action<string> CreateFolderRequested;
        public event Action<string> CreateNoteRequested;
        public event Action<string> FolderOpened;
        private void NavigateTo(string path) { ShowFolder(path); FolderOpened?.Invoke(CurrentFolder); }

        public void Render(NoteTreeNode root, string activeDocumentId, string emptyMessage = "ノートはまだありません")
        {
            this.root = root; activeId = activeDocumentId; emptyText = emptyMessage;
            ShowFolder(CurrentFolder);
        }

        private NoteTreeNode FindFolder(NoteTreeNode node, string path)
        {
            if (node.Path == path) return node;
            foreach (var child in node.Children.Where(n => n.IsFolder))
            { var found = FindFolder(child, path); if (found != null) return found; }
            return null;
        }
        private void ShowFolder(string path)
        {
            var folder = FindFolder(root, path) ?? root;
            CurrentFolder = folder.Path; Clear();
            if (CurrentFolder.Length > 0)
            {
                Add(new Button(() => NavigateTo(CurrentFolder.Contains("/") ? CurrentFolder.Substring(0, CurrentFolder.LastIndexOf('/')) : "")) { text = "← 戻る" });
                var location = new Label(CurrentFolder) { tooltip = CurrentFolder };
                location.style.whiteSpace = WhiteSpace.NoWrap; location.style.overflow = Overflow.Hidden;
                location.style.textOverflow = TextOverflow.Ellipsis; Add(location);
            }
            if (folder.Children.Count == 0 && !string.IsNullOrEmpty(emptyText)) Add(new Label(emptyText));
            foreach (var note in folder.Children.Where(n => !n.IsFolder)) AddNode(this, note, activeId);
            foreach (var child in folder.Children.Where(n => n.IsFolder)) AddNode(this, child, activeId);
        }
        private void AddNode(VisualElement parent, NoteTreeNode node, string activeId)
        {
            if (node.IsFolder)
            {
                var header = new Button(() => NavigateTo(node.Path)) { text = "▸ " + node.Name, tooltip = node.Path + "（クリックで開く・右クリックでメニュー）" };
                header.AddToClassList("note-row");
                header.style.whiteSpace = WhiteSpace.NoWrap; header.style.textOverflow = TextOverflow.Ellipsis;
                header.style.overflow = Overflow.Hidden; header.style.height = 30;
                header.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                parent.Add(header);
                header.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button != 1) return;
                    evt.StopImmediatePropagation();
                    var menu = new GenericDropdownMenu();
                    menu.AddItem("この中にノートを作成", false, () => CreateNoteRequested?.Invoke(node.Path));
                    menu.AddItem("この中にフォルダを作成", false, () => CreateFolderRequested?.Invoke(node.Path));
                    menu.DropDown(new UnityEngine.Rect(evt.position.x, evt.position.y, 1, 1), header, default(DropdownMenuSizeMode));
                });
            }
            else
            {
                var note = node.Note;
                var selected = note.DocumentId == activeId;
                var label = string.IsNullOrWhiteSpace(note.Title) ? node.Name : note.Title;
                var button = new Button(() => NoteSelected?.Invoke(note.DocumentId))
                {
                    text = label, tooltip = note.RelativePath, focusable = false
                };
                button.style.whiteSpace = WhiteSpace.NoWrap;
                button.style.overflow = Overflow.Hidden; button.style.textOverflow = TextOverflow.Ellipsis;
                button.style.height = 30; button.style.maxHeight = 30;
                button.AddToClassList("note-row");
                if (selected) button.AddToClassList("note-row--selected");
                button.style.minHeight = 30;
                button.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                button.style.flexGrow = 1; button.style.flexShrink = 1; button.style.minWidth = 0;
                button.tooltip = note.RelativePath + "（右クリックで名前変更・削除）";
                button.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button != 1) return;
                    evt.StopImmediatePropagation();
                    var menu = new GenericDropdownMenu();
                    menu.AddItem("開く", false, () => NoteSelected?.Invoke(note.DocumentId));
                    menu.AddItem("名前を変更…", false, () => RenameRequested?.Invoke(note.DocumentId));
                    menu.AddItem("削除…", false, () => DeleteRequested?.Invoke(note.DocumentId));
                    menu.DropDown(new UnityEngine.Rect(evt.position.x, evt.position.y, 1, 1), button, default(DropdownMenuSizeMode));
                });
                row.Add(button); parent.Add(row);
            }
        }
    }
}
