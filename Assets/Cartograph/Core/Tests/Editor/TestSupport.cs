using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cartograph.Core.Tests
{
    /// <summary>メモリ上のファイルシステム。書き込み失敗や、途中終了の再現にも使う。</summary>
    internal sealed class MemoryFileSystem : IFileSystem
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        private readonly HashSet<string> dirs = new HashSet<string>();
        public bool FailWrites;
        public int WriteCount;

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

        private void CheckFail()
        {
            if (FailWrites) throw new IOException("disk full (test)");
        }

        public bool FileExists(string path) => Files.ContainsKey(N(path));
        public bool DirectoryExists(string path) => dirs.Contains(N(path));
        public string ReadAllText(string path) => Files.TryGetValue(N(path), out var s) ? s : null;

        public void WriteAllTextAtomic(string path, string text)
        {
            CheckFail();
            WriteCount++;
            path = N(path);
            EnsureDirs(Parent(path));
            Files[path] = text;
        }

        public void AppendText(string path, string text)
        {
            CheckFail();
            WriteCount++;
            path = N(path);
            EnsureDirs(Parent(path));
            Files.TryGetValue(path, out var cur);
            cur = cur ?? "";
            if (cur.Length > 0 && cur[cur.Length - 1] != '\n') cur += "\n";
            Files[path] = cur + text;
        }

        public void CreateDirectory(string path) => EnsureDirs(N(path));

        public IEnumerable<string> GetDirectories(string dir)
        {
            dir = N(dir);
            return dirs.Where(d => Parent(d) == dir).ToList();
        }

        public IEnumerable<string> GetFiles(string dir)
        {
            dir = N(dir);
            return Files.Keys.Where(f => Parent(f) == dir).ToList();
        }

        public void MoveFile(string from, string to)
        {
            CheckFail();
            from = N(from);
            to = N(to);
            EnsureDirs(Parent(to));
            Files[to] = Files[from];
            Files.Remove(from);
        }

        public void DeleteFile(string path) => Files.Remove(N(path));
    }

    internal sealed class Env
    {
        public readonly MemoryFileSystem Fs = new MemoryFileSystem();
        public readonly ProjectPaths Paths = new ProjectPaths(Path.Combine(Path.GetTempPath(), "cartograph-test-project"));
        public readonly InternalClipboard Clip = new InternalClipboard();
        public readonly NoteSessionOptions Options;
        private long clock = 1_000_000;

        public Env(int snapshotInterval = 1000)
        {
            Options = new NoteSessionOptions
            {
                SaveDebounceMs = -1,
                SnapshotInterval = snapshotInterval,
                Clock = () => ++clock,
            };
        }

        public static NotePath P(string s) => NotePath.Parse(s);
        public Task<NoteSession> Open(string note) => NoteSession.OpenAsync(P(note), Paths, Fs, Clip, Options);
        public void PutNote(string note, string body) => Fs.WriteAllTextAtomic(Paths.NoteFile(P(note)), body);
        public string Body(string note) => Fs.ReadAllText(Paths.NoteFile(P(note)));
        public string History(string note) => Fs.ReadAllText(Paths.HistoryFile(P(note)));
        public string SnapshotJson(string note) => Fs.ReadAllText(Paths.SnapshotFile(P(note)));
        public void SetHistory(string note, string text) => Fs.Files[Paths.HistoryFile(P(note)).Replace('\\', '/')] = text;
        public void SetBody(string note, string text) => Fs.Files[Paths.NoteFile(P(note)).Replace('\\', '/')] = text;
        public List<string> HistoryLines(string note)
            => (History(note) ?? "").Split('\n').Where(l => l.Length > 0).ToList();
    }

    internal static class Typing
    {
        public static ChangeCandidate Key(string newText, int caret, string ch) => new ChangeCandidate
        {
            NewText = newText,
            SelStartBefore = caret,
            SelEndBefore = caret,
            Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = ch },
        };

        public static ReviewResult TypeAt(NoteSession s, int pos, string ch)
            => s.Apply(Key(s.Text.Insert(pos, ch), pos, ch));

        /// <summary>code point ごとにキー入力として打つ。</summary>
        public static void TypeString(NoteSession s, int pos, string str)
        {
            for (int i = 0; i < str.Length;)
            {
                int n = char.IsHighSurrogate(str[i]) && i + 1 < str.Length ? 2 : 1;
                string ch = str.Substring(i, n);
                var r = TypeAt(s, pos, ch);
                if (!r.Approved) throw new InvalidOperationException("入力が遮断されました: " + ch);
                pos += n;
                i += n;
            }
        }

        public static ReviewResult Ime(NoteSession s, int pos, string text)
            => s.Apply(new ChangeCandidate
            {
                NewText = s.Text.Insert(pos, text),
                SelStartBefore = pos,
                SelEndBefore = pos,
                Evidence = new InputEvidence { Kind = EvidenceKind.ImeCommit, Text = text },
            });

        public static ReviewResult Paste(NoteSession s, int pos, string text)
            => s.Apply(new ChangeCandidate
            {
                NewText = s.Text.Insert(pos, text),
                SelStartBefore = pos,
                SelEndBefore = pos,
                Evidence = new InputEvidence { Kind = EvidenceKind.Paste },
            });

        public static ReviewResult Delete(NoteSession s, int pos, int len)
            => s.Apply(new ChangeCandidate
            {
                NewText = s.Text.Remove(pos, len),
                SelStartBefore = pos,
                SelEndBefore = pos + len,
                Evidence = new InputEvidence { Kind = EvidenceKind.Key },
            });
    }
}
