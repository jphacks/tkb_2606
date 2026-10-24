using System;
using System.Collections.Generic;
using System.Linq;

namespace Cartograph.EditorUI.Navigation
{
    public sealed class NoteTreeEntry
    {
        public string DocumentId { get; }
        public string RelativePath { get; }
        public string Title { get; }
        public NoteTreeEntry(string documentId, string relativePath, string title)
        {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentException("Document ID is required.");
            DocumentId = documentId;
            RelativePath = Normalize(relativePath);
            Title = title ?? "";
        }

        // Display model only; never used for filesystem access or authorization.
        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Relative note path is required.");
            path = path.Replace('\\', '/');
            if (path.StartsWith("/") || path.Contains(":")) throw new ArgumentException("Use a relative note path.");
            var parts = path.Split('/');
            if (parts.Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".."))
                throw new ArgumentException("Invalid path segment.");
            return path;
        }
    }

    public sealed class NoteTreeNode
    {
        private readonly List<NoteTreeNode> children = new List<NoteTreeNode>();
        public string Name { get; }
        public string Path { get; }
        public NoteTreeEntry Note { get; }
        public bool IsFolder => Note == null;
        public IReadOnlyList<NoteTreeNode> Children => children.AsReadOnly();
        internal NoteTreeNode(string name, string path, NoteTreeEntry note = null)
        { Name = name; Path = path; Note = note; }
        internal void Add(NoteTreeNode node) => children.Add(node);
        internal void Sort()
        {
            children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                return StringComparer.Ordinal.Compare(a.Name, b.Name);
            });
            foreach (var child in children) child.Sort();
        }
    }

    public static class NoteTree
    {
        public static NoteTreeNode Build(IEnumerable<NoteTreeEntry> notes)
        {
            if (notes == null) throw new ArgumentNullException(nameof(notes));
            var root = new NoteTreeNode("notes", "");
            var folders = new Dictionary<string, NoteTreeNode>(StringComparer.Ordinal) { { "", root } };
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in notes)
            {
                if (note == null) throw new ArgumentException("Null note.");
                if (!paths.Add(note.RelativePath) || !ids.Add(note.DocumentId))
                    throw new ArgumentException("Duplicate path or document ID.");
                var parts = note.RelativePath.Split('/');
                var parent = root;
                string path = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    path = path.Length == 0 ? parts[i] : path + "/" + parts[i];
                    if (!folders.TryGetValue(path, out var folder))
                    {
                        folder = new NoteTreeNode(parts[i], path); folders.Add(path, folder); parent.Add(folder);
                    }
                    parent = folder;
                }
                parent.Add(new NoteTreeNode(parts[parts.Length - 1], note.RelativePath, note));
            }
            if (paths.Any(p => folders.ContainsKey(p))) throw new ArgumentException("A note cannot also be a folder.");
            root.Sort(); return root;
        }
    }
}
