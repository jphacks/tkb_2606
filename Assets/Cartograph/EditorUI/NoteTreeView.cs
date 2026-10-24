using System;
using System.Collections.Generic;
using Cartograph.EditorUI.Navigation;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    public sealed class NoteTreeView : VisualElement
    {
        private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();
        public event Action<string> NoteSelected;
        public event Action<string> DeleteRequested;

        public void Render(NoteTreeNode root, string activeDocumentId)
        {
            Clear();
            if (root.Children.Count == 0) Add(new Label("ノートはまだありません"));
            foreach (var node in root.Children) AddNode(this, node, activeDocumentId);
        }

        private void AddNode(VisualElement parent, NoteTreeNode node, string activeId)
        {
            if (node.IsFolder)
            {
                var foldout = new Foldout { text = node.Name, value = !expanded.TryGetValue(node.Path, out var open) || open };
                foldout.AddToClassList("town-folder");
                foldout.RegisterValueChangedCallback(evt => { expanded[node.Path] = evt.newValue; evt.StopPropagation(); });
                parent.Add(foldout);
                foreach (var child in node.Children) AddNode(foldout, child, activeId);
            }
            else
            {
                var note = node.Note;
                var selected = note.DocumentId == activeId;
                var label = string.IsNullOrWhiteSpace(note.Title) ? node.Name : note.Title;
                var button = new Button(() => NoteSelected?.Invoke(note.DocumentId))
                {
                    text = (selected ? "● " : "") + label, tooltip = note.RelativePath, focusable = false
                };
                button.style.whiteSpace = WhiteSpace.Normal;
                button.AddToClassList("note-row");
                if (selected) button.AddToClassList("note-row--selected");
                button.style.minHeight = 30;
                button.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                button.style.flexGrow = 1; button.style.flexShrink = 1; button.style.minWidth = 0;
                var menu = new VisualElement(); menu.style.display = DisplayStyle.None;
                menu.AddToClassList("note-mini-menu");
                menu.Add(new Button(() => { menu.style.display = DisplayStyle.None; NoteSelected?.Invoke(note.DocumentId); }) { text = "開く" });
                menu.Add(new Button(() => { menu.style.display = DisplayStyle.None; DeleteRequested?.Invoke(note.DocumentId); }) { text = "削除…" });
                menu.Add(new Button(() => menu.style.display = DisplayStyle.None) { text = "閉じる" });
                var more = new Button(() => menu.style.display = menu.style.display.value == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None)
                    { text = "⋯", tooltip = "ノートのメニュー" };
                more.style.width = 24; more.style.paddingLeft = more.style.paddingRight = 2;
                button.RegisterCallback<PointerUpEvent>(evt =>
                { if (evt.button == 1) { menu.style.display = DisplayStyle.Flex; evt.StopPropagation(); } });
                row.Add(button); row.Add(more); parent.Add(row); parent.Add(menu);
            }
        }
    }
}
