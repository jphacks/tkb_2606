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
        // 並べ替え専用。不明なら DateTime.MinValue。
        public DateTime Created { get; }
        public DateTime Updated { get; }
        public NoteTreeEntry(string documentId, string relativePath, string title,
            DateTime created = default, DateTime updated = default)
        {
            if (string.IsNullOrEmpty(documentId)) throw new ArgumentException("Document ID is required.");
            DocumentId = documentId;
            RelativePath = Normalize(relativePath);
            Title = title ?? "";
            Created = created; Updated = updated;
        }

        // Display model only; never used for filesystem access or authorization.
        internal static string Normalize(string path)
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
        internal void Sort() => Sort(NoteTreeSort.Default);

        /// <summary>Re-sorts this subtree. Folders take the newest/oldest time of what they contain.</summary>
        public void Sort(NoteTreeSort sort)
        {
            sort = sort ?? NoteTreeSort.Default;
            children.Sort((a, b) =>
            {
                if (sort.FoldersFirst && a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                int order = 0;
                if (sort.Key == NoteSortKey.Updated) order = a.LatestUpdated().CompareTo(b.LatestUpdated());
                else if (sort.Key == NoteSortKey.Created) order = a.EarliestCreated().CompareTo(b.EarliestCreated());
                if (order == 0)
                    order = a.IsFolder && b.IsFolder ? StringComparer.Ordinal.Compare(a.Name, b.Name)
                        : CompareNatural(NoteStem(a.Name), NoteStem(b.Name));
                if (sort.Descending) order = -order;
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.Path, b.Path);
            });
            foreach (var child in children) child.Sort(sort);
        }

        private DateTime LatestUpdated()
        {
            if (!IsFolder) return Note.Updated;
            var latest = DateTime.MinValue;
            foreach (var child in children) { var t = child.LatestUpdated(); if (t > latest) latest = t; }
            return latest;
        }
        // 作成日時が不明(MinValue)のものは「最も新しい」側に寄せ、先頭に紛れ込ませない。
        private DateTime EarliestCreated()
        {
            if (!IsFolder) return Note.Created == DateTime.MinValue ? DateTime.MaxValue : Note.Created;
            var earliest = DateTime.MaxValue;
            foreach (var child in children) { var t = child.EarliestCreated(); if (t < earliest) earliest = t; }
            return earliest;
        }

        private static string NoteStem(string name) => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - 3) : name;

        // Compare digit runs without parsing integers: arbitrarily long suffixes remain sortable.
        private static int CompareNatural(string left, string right)
        {
            int a = 0, b = 0;
            while (a < left.Length && b < right.Length)
            {
                if (IsDigit(left[a]) && IsDigit(right[b]))
                {
                    int aEnd = a, bEnd = b;
                    while (aEnd < left.Length && IsDigit(left[aEnd])) aEnd++;
                    while (bEnd < right.Length && IsDigit(right[bEnd])) bEnd++;
                    while (a < aEnd && left[a] == '0') a++;
                    while (b < bEnd && right[b] == '0') b++;
                    var order = (aEnd - a).CompareTo(bEnd - b);
                    if (order != 0) return order;
                    while (a < aEnd)
                    {
                        order = left[a++].CompareTo(right[b++]);
                        if (order != 0) return order;
                    }
                    a = aEnd; b = bEnd;
                }
                else
                {
                    var order = left[a++].CompareTo(right[b++]);
                    if (order != 0) return order;
                }
            }
            return (left.Length - a).CompareTo(right.Length - b);
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }

    public static class NoteTree
    {
        public static NoteTreeNode Build(IEnumerable<NoteTreeEntry> notes, IEnumerable<string> folderPaths = null)
        {
            if (notes == null) throw new ArgumentNullException(nameof(notes));
            var root = new NoteTreeNode("notes", "");
            var folders = new Dictionary<string, NoteTreeNode>(StringComparer.Ordinal) { { "", root } };
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in folderPaths ?? Array.Empty<string>())
            {
                var parent = root; string path = "";
                foreach (var part in NoteTreeEntry.Normalize(raw).Split('/'))
                {
                    path = path.Length == 0 ? part : path + "/" + part;
                    if (!folders.TryGetValue(path, out var folder))
                    { folder = new NoteTreeNode(part, path); folders.Add(path, folder); parent.Add(folder); }
                    parent = folder;
                }
            }
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
