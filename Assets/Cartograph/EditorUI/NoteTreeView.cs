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
                parent.Add(button);
            }
        }
    }
}
