using System.Collections.Generic;
using System.Linq;

namespace Cartograph.Core
{
    /// <summary>
    /// メモリの上だけにあるファイルシステム。ディスクには何も書かない。
    /// デモプロジェクトで使う(閉じれば内容は消える)。パスの区切りは '\' も '/' も同じものとして扱う。
    /// 保存用のスレッドと UI スレッドから同時に触られるので、すべての操作を 1 つのロックで守る。
    /// </summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, string> files = new Dictionary<string, string>();
        private readonly HashSet<string> dirs = new HashSet<string>();

        private static string N(string p) => p.Replace('\\', '/').TrimEnd('/');

        private static string Parent(string p)
        {
            int i = p.LastIndexOf('/');
            return i <= 0 ? "" : p.Substring(0, i);
        }

        private void EnsureDirs(string dir)
        {
            while (!string.IsNullOrEmpty(dir)) { dirs.Add(dir); dir = Parent(dir); }
        }

        /// <summary>いま持っているファイルの数(テストと確認用)。</summary>
        public int FileCount { get { lock (gate) return files.Count; } }

        public bool FileExists(string path) { lock (gate) return files.ContainsKey(N(path)); }
        public bool DirectoryExists(string path) { lock (gate) return dirs.Contains(N(path)); }

        public string ReadAllText(string path)
        {
            lock (gate) return files.TryGetValue(N(path), out var s) ? s : null;
        }

        public void WriteAllTextAtomic(string path, string text)
        {
            lock (gate)
            {
                path = N(path);
                EnsureDirs(Parent(path));
                files[path] = text;
            }
        }

        public void AppendText(string path, string text)
        {
            lock (gate)
            {
                path = N(path);
                EnsureDirs(Parent(path));
                files.TryGetValue(path, out var cur);
                cur = cur ?? "";
                if (cur.Length > 0 && cur[cur.Length - 1] != '\n') cur += "\n";
                files[path] = cur + text;
            }
        }

        public void CreateDirectory(string path) { lock (gate) EnsureDirs(N(path)); }

        public IEnumerable<string> GetDirectories(string dir)
        {
            lock (gate)
            {
                dir = N(dir);
                return dirs.Where(d => Parent(d) == dir).ToList();
            }
        }

        public IEnumerable<string> GetFiles(string dir)
        {
            lock (gate)
            {
                dir = N(dir);
                return files.Keys.Where(f => Parent(f) == dir).ToList();
            }
        }

        public void MoveFile(string from, string to)
        {
            lock (gate)
            {
                from = N(from);
                to = N(to);
                EnsureDirs(Parent(to));
                files[to] = files[from];
                files.Remove(from);
            }
        }

        public void DeleteFile(string path) { lock (gate) files.Remove(N(path)); }
    }
}
