using System;
using System.Collections.Generic;

namespace Cartograph.EditorUI
{
    /// <summary>Session-only histories, reused only while the authoritative text still matches.</summary>
    public sealed class NoteUndoHistoryCache
    {
        private readonly Dictionary<string, TextUndoHistory> histories = new Dictionary<string, TextUndoHistory>(StringComparer.Ordinal);
        public TextUndoHistory GetOrCreate(string noteId, string text, object metadata)
        {
            if (noteId == null) throw new ArgumentNullException(nameof(noteId));
            text = text ?? "";
            if (!histories.TryGetValue(noteId, out var history) || history.Current.Text != text)
                histories[noteId] = history = new TextUndoHistory(text, metadata: metadata);
            return history;
        }
        public void Clear() => histories.Clear();
    }
}
