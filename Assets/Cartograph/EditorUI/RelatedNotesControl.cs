using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>A target picker: a deliberate selection requests a link without a second button.</summary>
    public sealed class RelatedNotesControl : VisualElement
    {
        private const string Placeholder = "＋ 関連ノートを選んでつなぐ";
        private readonly DropdownField picker;
        private readonly Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.Ordinal);

        public event Action<string> Requested;

        public RelatedNotesControl()
        {
            AddToClassList("related-notes");
            style.flexDirection = FlexDirection.Row;
            Add(new Label("関連ノート"));
            picker = new DropdownField { name = "related-note-picker" };
            picker.style.flexGrow = 1;
            picker.style.minWidth = 0;
            picker.tooltip = "ノートを選ぶと、現在の段落から相手のノートへ地図の線をつなぎます。";
            Add(picker);
            picker.RegisterValueChangedCallback(evt =>
            {
                if (!targets.TryGetValue(evt.newValue ?? string.Empty, out var targetId)) return;
                // Reset before notifying: reentrant refreshes and failed requests remain retryable.
                ResetSelection();
                Requested?.Invoke(targetId);
            });
            SetNotes(null, Array.Empty<DocumentRecord>());
        }

        public void SetNotes(string currentId, IEnumerable<DocumentRecord> documents)
        {
            targets.Clear();
            var choices = new List<string> { Placeholder };
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
                for (var suffix = 2; targets.ContainsKey(unique) || unique == Placeholder; suffix++)
                    unique = label + " [" + suffix + "]";
                targets.Add(unique, note.id);
                choices.Add(unique);
            }
            picker.choices = choices;
            ResetSelection();
            picker.SetEnabled(!string.IsNullOrEmpty(currentId) && targets.Count > 0);
        }

        public void ResetSelection() => picker.SetValueWithoutNotify(Placeholder);
    }
}
