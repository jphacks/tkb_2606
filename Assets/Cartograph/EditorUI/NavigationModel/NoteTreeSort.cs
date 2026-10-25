namespace Cartograph.EditorUI.Navigation
{
    public enum NoteSortKey { Name, Updated, Created }

    /// <summary>How a note tree is ordered; shared by the UI and its tests.</summary>
    public sealed class NoteTreeSort
    {
        public static readonly NoteTreeSort Default = new NoteTreeSort(NoteSortKey.Name, false, true);

        public NoteSortKey Key { get; }
        public bool Descending { get; }
        public bool FoldersFirst { get; }

        public NoteTreeSort(NoteSortKey key, bool descending, bool foldersFirst)
        { Key = key; Descending = descending; FoldersFirst = foldersFirst; }
    }
}
