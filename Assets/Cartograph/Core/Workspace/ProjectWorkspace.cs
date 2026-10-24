using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Cartograph.Core
{
    public sealed class NoteEntry
    {
        public NotePath Path { get; }
        public string Name => Path.Name;
        public NoteEntry(NotePath path) { Path = path; }
    }

    /// <summary>フォルダ(町)。入れ子の深さは自由。Path は notes/ からの相対位置("" がルート)。</summary>
    public sealed class FolderNode
    {
        public string Name { get; }
        public string Path { get; }
        public List<FolderNode> Folders { get; } = new List<FolderNode>();
        public List<NoteEntry> Notes { get; } = new List<NoteEntry>();

        public FolderNode(string name, string path)
        {
            Name = name;
            Path = path;
        }
    }

    /// <summary>
    /// プロジェクトフォルダを開いて、notes/ 以下のフォルダ階層とノートを扱う窓口。
    /// UI やフォルダ選択ダイアログには依存せず、絶対パスだけを受け取る。
    /// </summary>
    public sealed class ProjectWorkspace
    {
        private readonly IFileSystem fs;
        private readonly ProjectPaths paths;
        private readonly NoteSessionOptions options;
        private readonly Dictionary<string, Task<NoteSession>> sessions = new Dictionary<string, Task<NoteSession>>();

        public InternalClipboard Clipboard { get; } = new InternalClipboard();
        public FolderNode Tree { get; private set; }
        public string Root => paths.Root;
        public event Action TreeChanged;

        private ProjectWorkspace(ProjectPaths paths, IFileSystem fs, NoteSessionOptions options)
        {
            this.paths = paths;
            this.fs = fs;
            this.options = options;
        }

        public static ProjectWorkspace Open(string absoluteRoot, IFileSystem fs = null, NoteSessionOptions options = null)
        {
            fs = fs ?? new DiskFileSystem();
            var ws = new ProjectWorkspace(new ProjectPaths(absoluteRoot), fs, options ?? new NoteSessionOptions());
            fs.CreateDirectory(ws.paths.NotesDir);
            ws.RefreshTree();
            return ws;
        }

        public void RefreshTree()
        {
            Tree = Build(paths.NotesDir, "", "");
            TreeChanged?.Invoke();
        }

        public bool NoteExists(NotePath note) => note.IsValid && fs.FileExists(paths.NoteFile(note));

        /// <summary>全ノートを階層をたどって一覧にする(階層を持たない UI 向けの平らな一覧)。</summary>
        public List<NotePath> AllNotes()
        {
            var list = new List<NotePath>();
            Collect(Tree, list);
            return list;
        }

        public Task<NoteSession> OpenNote(NotePath note)
        {
            if (!note.IsValid) throw new ArgumentException("ノートの場所が空です。", nameof(note));
            if (sessions.TryGetValue(note.Value, out var existing)
                && !existing.IsFaulted && !existing.IsCanceled && !(existing.IsCompleted && existing.Result.IsClosed))
                return existing;
            if (!NoteExists(note)) throw new FileNotFoundException("ノートが見つかりません: " + note);
            var task = NoteSession.OpenAsync(note, paths, fs, Clipboard, options);
            sessions[note.Value] = task;
            return task;
        }

        public async Task<NoteSession> CreateNote(NotePath note)
        {
            if (!note.IsValid) throw new ArgumentException("ノートの場所が空です。", nameof(note));
            string file = paths.NoteFile(note);
            if (fs.FileExists(file)) throw new IOException("同じ名前のノートがあります: " + note);
            fs.WriteAllTextAtomic(file, "");
            RefreshTree();
            return await OpenNote(note);
        }

        public async Task CloseNote(NotePath note)
        {
            if (!sessions.TryGetValue(note.Value, out var task)) return;
            var session = await task;
            await session.CloseAsync();
            sessions.Remove(note.Value);
        }

        /// <summary>ノート名を変える。本文と、履歴・スナップショットも一緒に移す。</summary>
        public async Task<NotePath> RenameNote(NotePath note, string newName)
        {
            var target = note.WithName(newName);
            if (target == note) return note;
            if (fs.FileExists(paths.NoteFile(target))) throw new IOException("同じ名前のノートがあります: " + target);

            await CloseNote(note);
            Move(paths.NoteFile(note), paths.NoteFile(target));
            Move(paths.HistoryFile(note), paths.HistoryFile(target));
            Move(paths.SnapshotFile(note), paths.SnapshotFile(target));
            Clipboard.RenameSource(note.Value, target.Value);
            RefreshTree();
            return target;
        }

        public async Task TrashNote(NotePath note)
        {
            if (!NoteExists(note)) throw new FileNotFoundException("ノートが見つかりません: " + note);
            await FlushAll();
            await CloseNote(note);
            string trash = System.IO.Path.Combine(Root, ".cartograph", "trash", Guid.NewGuid().ToString("N"));
            var moved = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var source in new[] { paths.NoteFile(note), paths.HistoryFile(note), paths.SnapshotFile(note) })
                {
                    if (!fs.FileExists(source)) continue;
                    string target = System.IO.Path.Combine(trash, moved.Count + "-" + System.IO.Path.GetFileName(source));
                    fs.MoveFile(source, target); moved.Add(new KeyValuePair<string, string>(source, target));
                }
            }
            catch
            {
                for (int i = moved.Count - 1; i >= 0; i--) fs.MoveFile(moved[i].Value, moved[i].Key);
                throw;
            }
            Clipboard.Clear();
            RefreshTree();
        }

        public async Task FlushAll()
        {
            var open = new List<Task<NoteSession>>(sessions.Values);
            foreach (var t in open)
            {
                var s = await t;
                if (!s.IsClosed) await s.Flush();
                if (s.State == NoteSaveState.Error) throw new IOException(s.Error);
            }
        }

        private void Move(string from, string to)
        {
            if (!fs.FileExists(from)) { fs.DeleteFile(to); return; }
            fs.DeleteFile(to);   // 以前の同名ノートの履歴が残っていても、新しい名前には引き継がない
            fs.MoveFile(from, to);
        }

        private FolderNode Build(string dir, string name, string rel)
        {
            var node = new FolderNode(name, rel);

            var folders = new List<string>(fs.GetDirectories(dir));
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var d in folders)
            {
                string n = System.IO.Path.GetFileName(d.TrimEnd('/', '\\'));
                if (n.StartsWith(".", StringComparison.Ordinal) || !NotePath.IsValidName(n)) continue;
                node.Folders.Add(Build(d, n, rel.Length == 0 ? n : rel + "/" + n));
            }

            var files = new List<string>(fs.GetFiles(dir));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                if (!string.Equals(System.IO.Path.GetExtension(f), ".md", StringComparison.OrdinalIgnoreCase)) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(f);
                if (NotePath.TryParse(rel.Length == 0 ? n : rel + "/" + n, out var p)) node.Notes.Add(new NoteEntry(p));
            }
            return node;
        }

        private static void Collect(FolderNode node, List<NotePath> into)
        {
            foreach (var f in node.Folders) Collect(f, into);
            foreach (var n in node.Notes) into.Add(n.Path);
        }
    }
}
