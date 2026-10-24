namespace Cartograph.EditorUI
{
    // Optional UI adapter; the shared IEditorWorkspace contract remains unchanged.
    public interface ISecondaryNoteEditor
    {
        DocumentRecord ApplySecondaryEdit(string documentId, long expectedRevision, string markdown, ChangeOrigin origin);
    }
}
