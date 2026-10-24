using System;
using System.Collections.Generic;

namespace Cartograph.EditorUI
{
    // UI undo state only; this is not the durable provenance/history log.
    public sealed class TextUndoHistory
    {
        public sealed class Snapshot
        {
            public readonly string Text;
            public readonly int Cursor;
            public readonly int Selection;
            public Snapshot(string text, int cursor, int selection)
            {
                Text = text ?? "";
                Cursor = Math.Max(0, Math.Min(cursor, Text.Length));
                Selection = Math.Max(0, Math.Min(selection, Text.Length));
            }
        }

        private readonly List<Snapshot> states = new List<Snapshot>();
        private readonly int maxStates;
        private readonly long maxCharacters;
        private int index;
        private long characters;
        public bool CanUndo => index > 0;
        public bool CanRedo => index + 1 < states.Count;
        public Snapshot Current => states[index];

        public TextUndoHistory(string text, int maxStates = 100, long maxCharacters = 2000000)
        {
            if (maxStates < 2 || maxCharacters < 1) throw new ArgumentOutOfRangeException();
            this.maxStates = maxStates; this.maxCharacters = maxCharacters;
            var initial = new Snapshot(text, 0, 0);
            states.Add(initial); characters = initial.Text.Length;
        }

        public void UpdateSelection(int cursor, int selection)
        {
            states[index] = new Snapshot(Current.Text, cursor, selection);
        }

        public void Record(string text, int cursor, int selection)
        {
            if (text == Current.Text) { UpdateSelection(cursor, selection); return; }
            while (states.Count > index + 1)
            {
                characters -= states[states.Count - 1].Text.Length;
                states.RemoveAt(states.Count - 1);
            }
            var next = new Snapshot(text, cursor, selection);
            states.Add(next); characters += next.Text.Length; index++;
            // Keep the present state even when a single document exceeds the UI budget.
            while (states.Count > 1 && (states.Count > maxStates || characters > maxCharacters))
            {
                characters -= states[0].Text.Length; states.RemoveAt(0); index--;
            }
        }

        public Snapshot Undo() { if (CanUndo) index--; return Current; }
        public Snapshot Redo() { if (CanRedo) index++; return Current; }
    }
}
