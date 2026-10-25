using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using UnityEngine;
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
        /// <summary>Connect with a chosen road kind and optional reason (target id, kind, reason).</summary>
        public event Action<string, LinkMarkerKind, string> LinkRequested;
        private readonly VisualElement options;
        private readonly Label optionsTarget;
        private readonly TextField reasonField;
        private string pendingTargetId;

        public RelatedNotesControl()
        {
            AddToClassList("related-notes");
            style.flexDirection = FlexDirection.Row;
            style.flexWrap = Wrap.Wrap;
            Add(new Label("関連ノート"));
            picker = new Button(OpenMenu) { name = "related-note-picker", text = "関連ノートを選ぶ ▾" };
            picker.style.flexGrow = 1;
            picker.style.minWidth = 0;
            picker.tooltip = "ノートを選ぶと、現在の段落から相手のノートへ地図の線をつなぎます。";
            Add(picker);
            options = BuildOptions(out optionsTarget, out reasonField);
            Add(options);
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
                HideOptions();
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

        private List<string> suggested = new List<string>();
        /// <summary>自動検出で関連しそうなノート(点の高い順)。メニューの先頭に「おすすめ」として出す。</summary>
        public void SetSuggestions(IEnumerable<string> ids)
        {
            suggested = (ids ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).Take(5).ToList();
        }

        private void OpenMenu()
        {
            if (targets.Count == 0 || string.IsNullOrEmpty(currentDocumentId)) return;
            var ownerId = currentDocumentId;
            var menu = new GenericDropdownMenu();
            // おすすめ(まだつながっていないもの)を先に並べ、そのあとに全ノートを並べる。
            var ordered = targets.Where(t => suggested.Contains(t.Value) && !connections.Contains(t.Value))
                .OrderBy(t => suggested.IndexOf(t.Value)).Select(t => new KeyValuePair<string, string>("★ " + t.Key, t.Value)).ToList();
            int recommended = ordered.Count;
            ordered.AddRange(targets);
            for (int index = 0; index < ordered.Count; index++)
            {
                var target = ordered[index];
                if (index == recommended && recommended > 0) menu.AddSeparator("");
                var targetId = target.Value;
                menu.AddItem(target.Key, connections.Contains(targetId), () =>
                {
                    // Menus can outlive a project refresh: never apply an old target to a new note.
                    if (ownerId != currentDocumentId ||
                        !targets.Any(item => item.Value == targetId)) return;
                    var connect = !connections.Contains(targetId);
                    if (connect && LinkRequested != null) { ShowOptions(targetId, target.Key); return; }
                    ConnectionRequested?.Invoke(targetId, connect);
                    // Legacy consumers only supported adding a link.
                    if (ConnectionRequested == null && connect) Requested?.Invoke(targetId);
                });
            }
            menu.DropDown(picker.worldBound, picker, default(DropdownMenuSizeMode));
        }

        private VisualElement BuildOptions(out Label targetLabel, out TextField reason)
        {
            var panel = new VisualElement { name = "related-note-options" };
            panel.style.display = DisplayStyle.None;
            panel.style.width = Length.Percent(100);
            panel.style.flexDirection = FlexDirection.Row;
            panel.style.flexWrap = Wrap.Wrap;
            panel.style.alignItems = Align.Center;
            panel.style.marginTop = 4;
            targetLabel = new Label();
            targetLabel.style.marginRight = 6;
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(targetLabel);
            AddKindButton(panel, LinkMarkerKind.Premise, "前提", "このノートは、相手のノートを前提にしている");
            AddKindButton(panel, LinkMarkerKind.Contrast, "対比", "相手のノートと対比している");
            AddKindButton(panel, LinkMarkerKind.Example, "具体例", "相手のノートの具体例");
            AddKindButton(panel, LinkMarkerKind.None, "指定なし", "種類を決めずにつなぐ");
            reason = new TextField { name = "related-note-reason" };
            reason.style.flexGrow = 1; reason.style.minWidth = 120; reason.style.marginLeft = 6;
            reason.tooltip = "つなぐ理由（省略できます）。種類を選ぶ前に入力してください。";
            panel.Add(reason);
            var cancel = new Button(HideOptions) { text = "やめる" };
            panel.Add(cancel);
            return panel;
        }

        private void AddKindButton(VisualElement parent, LinkMarkerKind kind, string text, string tip)
        {
            var button = new Button(() => Confirm(kind)) { text = text, tooltip = tip };
            if (kind != LinkMarkerKind.None)
            {
                LinkMarker.Color(kind, out byte r, out byte g, out byte b);
                button.style.color = (Color)new Color32(r, g, b, 255);
            }
            parent.Add(button);
        }

        private void ShowOptions(string targetId, string label)
        {
            pendingTargetId = targetId;
            optionsTarget.text = (label ?? targetId).Replace("★ ", "") + " へ：";
            reasonField.SetValueWithoutNotify("");
            options.style.display = DisplayStyle.Flex;
        }

        private void HideOptions()
        {
            pendingTargetId = null;
            options.style.display = DisplayStyle.None;
            reasonField.SetValueWithoutNotify("");
        }

        private void Confirm(LinkMarkerKind kind)
        {
            var targetId = pendingTargetId;
            var reason = reasonField.value;
            HideOptions();
            // A refresh may have removed the target while the buttons were open.
            if (targetId == null || string.IsNullOrEmpty(currentDocumentId) ||
                !targets.Any(item => item.Value == targetId)) return;
            LinkRequested?.Invoke(targetId, kind, reason);
        }

        // Kept for callers that reset after a failed insertion; a menu has no selected placeholder.
        public void ResetSelection() { }
    }
}
