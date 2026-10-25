using System;
using System.Collections.Generic;
using System.IO;

namespace Cartograph.Core
{
    /// <summary>
    /// notes/ からの相対位置で表すノートの識別子。例: "大学用/授業/科目A/ノート"(区切りは常に '/'、拡張子なし)。
    /// ".." や絶対パス、ファイル名に使えない文字を含むものは作れない。
    /// </summary>
    public readonly struct NotePath : IEquatable<NotePath>
    {
        private static readonly char[] InvalidChars = "<>:\"\\|?*".ToCharArray();
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public string Value { get; }
        public bool IsValid => Value != null;

        private NotePath(string value) { Value = value; }

        /// <summary>1 つの名前(フォルダ名・ノート名)として使えるか。</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name == "." || name == "..") return false;
            if (name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal)) return false;
            if (name.IndexOf('/') >= 0) return false;
            foreach (char c in name)
            {
                if (c < 32 || Array.IndexOf(InvalidChars, c) >= 0) return false;
            }
            int dot = name.IndexOf('.');
            string stem = dot >= 0 ? name.Substring(0, dot) : name;
            if (ReservedNames.Contains(stem)) return false;
            return true;
        }

        public static bool TryParse(string s, out NotePath path)
        {
            path = default;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var seg in s.Split('/'))
                if (!IsValidName(seg)) return false;
            path = new NotePath(s);
            return true;
        }

        public static NotePath Parse(string s)
        {
            if (!TryParse(s, out var p)) throw new ArgumentException("ノートの場所として使えない名前です: " + s, nameof(s));
            return p;
        }

        public IReadOnlyList<string> Segments => Value == null ? Array.Empty<string>() : Value.Split('/');

        public string Name
        {
            get
            {
                if (Value == null) return "";
                int i = Value.LastIndexOf('/');
                return i < 0 ? Value : Value.Substring(i + 1);
            }
        }

        /// <summary>所属フォルダ("" ならルート)。</summary>
        public string FolderPath
        {
            get
            {
                if (Value == null) return "";
                int i = Value.LastIndexOf('/');
                return i < 0 ? "" : Value.Substring(0, i);
            }
        }

        /// <summary>同じフォルダの別名のパスを返す(名前変更用)。</summary>
        public NotePath WithName(string newName)
        {
            if (!IsValidName(newName)) throw new ArgumentException("ノート名として使えません: " + newName, nameof(newName));
            string folder = FolderPath;
            return new NotePath(folder.Length == 0 ? newName : folder + "/" + newName);
        }

        /// <summary>同じ名前のまま別フォルダに置いたパスを返す(移動用)。folder が空ならルート。</summary>
        public NotePath WithFolder(string folder)
        {
            folder = folder ?? "";
            if (folder.Length > 0) folder = Parse(folder + "/placeholder").FolderPath;
            return new NotePath(folder.Length == 0 ? Name : folder + "/" + Name);
        }

        public bool Equals(NotePath other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NotePath p && Equals(p);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? "";
        public static bool operator ==(NotePath a, NotePath b) => a.Equals(b);
        public static bool operator !=(NotePath a, NotePath b) => !a.Equals(b);
    }

    /// <summary>プロジェクトフォルダ内の保存先の対応表。</summary>
    public sealed class ProjectPaths
    {
        public string Root { get; }
        public string NotesDir { get; }
        public string HistoryDir { get; }

        public ProjectPaths(string absoluteRoot)
        {
            if (string.IsNullOrEmpty(absoluteRoot) || !Path.IsPathRooted(absoluteRoot))
                throw new ArgumentException("プロジェクトフォルダは絶対パスで指定してください。", nameof(absoluteRoot));
            Root = absoluteRoot.TrimEnd('/', '\\');
            NotesDir = Path.Combine(Root, "notes");
            HistoryDir = Path.Combine(Root, ".cartograph", "history");
        }

        public string NoteFile(NotePath note) => Combine(NotesDir, note, ".md");
        public string HistoryFile(NotePath note) => Combine(HistoryDir, note, ".jsonl");
        public string SnapshotFile(NotePath note) => Combine(HistoryDir, note, ".snapshot.json");

        private static string Combine(string baseDir, NotePath note, string extension)
        {
            if (!note.IsValid) throw new ArgumentException("ノートの場所が空です。", nameof(note));
            string rel = note.Value.Replace('/', Path.DirectorySeparatorChar) + extension;
            return Path.Combine(baseDir, rel);
        }
    }
}
