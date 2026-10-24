using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>A target picker: a deliberate selection requests a link without a second button.</summary>
    public sealed class RelatedNotesControl : VisualElement
    {
        private readonly Button picker;
        private List<KeyValuePair<string, string>> targets = new List<KeyValuePair<string, string>>();
        private readonly HashSet<string> connections = new HashSet<string>(StringComparer.Ordinal);
        private string currentDocumentId;

        public event Action<string> Requested;
        public event Action<string, bool> ConnectionRequested;

        public RelatedNotesControl()
        {
            AddToClassList("related-notes");
            style.flexDirection = FlexDirection.Row;
            Add(new Label("関連ノート"));
            picker = new Button(OpenMenu) { name = "related-note-picker", text = "関連ノートを選ぶ ▾" };
            picker.style.flexGrow = 1;
            picker.style.minWidth = 0;
            picker.tooltip = "ノートを選ぶと、現在の段落から相手のノートへ地図の線をつなぎます。";
            Add(picker);
            SetNotes(null, Array.Empty<DocumentRecord>());
        }

        public void SetNotes(string currentId, IEnumerable<DocumentRecord> documents)
        {
            var nextTargets = new List<KeyValuePair<string, string>>();
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in (documents ?? Enumerable.Empty<DocumentRecord>())
                .Where(d => d != null && !string.IsNullOrEmpty(d.id) && d.id != currentId)
                .GroupBy(d => d.id, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(d => d.id, StringComparer.Ordinal))
            {
                // Paths distinguish equal titles and make the destination unambiguous.
                var label = string.IsNullOrWhiteSpace(note.title) || note.title == note.id
                    ? note.id : note.title + "（" + note.id + "）";
                // A title can contain any punctuation, so never rely on display text being unique.
                var unique = label;
                for (var suffix = 2; labels.Contains(unique); suffix++)
                    unique = label + " [" + suffix + "]";
                labels.Add(unique);
                nextTargets.Add(new KeyValuePair<string, string>(unique, note.id));
            }
            if (currentDocumentId != currentId)
            {
                currentDocumentId = currentId;
                connections.Clear();
            }
            if (!targets.SequenceEqual(nextTargets)) targets = nextTargets;
            var enabled = !string.IsNullOrEmpty(currentId) && targets.Count > 0;
            if (picker.enabledSelf != enabled) picker.SetEnabled(enabled);
        }

        /// <summary>Only accepted map edges supplied by the parent determine checked items.</summary>
        public void SetConnections(IEnumerable<string> connectedIds)
        {
            var next = new HashSet<string>((connectedIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            if (connections.SetEquals(next)) return;
            connections.Clear();
            connections.UnionWith(next);
        }

        private void OpenMenu()
        {
            if (targets.Count == 0 || string.IsNullOrEmpty(currentDocumentId)) return;
            var ownerId = currentDocumentId;
            var menu = new GenericDropdownMenu();
            foreach (var target in targets)
            {
                var targetId = target.Value;
                menu.AddItem(target.Key, connections.Contains(targetId), () =>
                {
                    // Menus can outlive a project refresh: never apply an old target to a new note.
                    if (ownerId != currentDocumentId ||
                        !targets.Any(item => item.Value == targetId)) return;
                    var connect = !connections.Contains(targetId);
                    ConnectionRequested?.Invoke(targetId, connect);
                    // Legacy consumers only supported adding a link.
                    if (ConnectionRequested == null && connect) Requested?.Invoke(targetId);
                });
            }
            menu.DropDown(picker.worldBound, picker, default(DropdownMenuSizeMode));
        }

        // Kept for callers that reset after a failed insertion; a menu has no selected placeholder.
        public void ResetSelection() { }
    }
}
