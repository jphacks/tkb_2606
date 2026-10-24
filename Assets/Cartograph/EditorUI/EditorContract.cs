using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cartograph.EditorUI
{
    public enum SaveState { Saved, Dirty, Saving, Error }
    public enum ChangeOrigin { Typing, Format, Undo, Redo, InternalPaste, Title }

    [Serializable]
    public sealed class DocumentRecord
    {
        public int schemaVersion = 1;
        public string id;
        public string title;
        public string markdown;
        public long revision;
        public string createdAt;
        public string updatedAt;
        public DocumentRecord Copy() => (DocumentRecord)MemberwiseClone();
    }

    // All calls and Changed notifications run on Unity's main thread.
    // Implementations return copies; the view must never mutate stored records.
    public interface IEditorWorkspace
    {
        event Action Changed;
        IReadOnlyList<DocumentRecord> Documents { get; }
        DocumentRecord Current { get; }
        SaveState State { get; }
        string Error { get; }
        bool IsPersistent { get; }
        Task CreateDocument();
        Task OpenDocument(string id);
        void UpdateTitle(string title);
        void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin);
        Task FlushSave();
        string ExportMarkdown();
    }
}
