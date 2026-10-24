using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cartograph.Core
{
    /// <summary>ファイル操作の窓口。テストでは差し替える。文字コードは常に UTF-8(BOM なし)。</summary>
    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        /// <summary>ファイルがなければ null。</summary>
        string ReadAllText(string path);
        /// <summary>一時ファイルに書いてから置き換える。途中で落ちても元のファイルは壊れない。親フォルダは作る。</summary>
        void WriteAllTextAtomic(string path, string text);
        /// <summary>末尾に追記する。ファイルの最後が改行で終わっていなければ、先に改行を入れる。親フォルダは作る。</summary>
        void AppendText(string path, string text);
        void CreateDirectory(string path);
        /// <summary>直下のフォルダのフルパス。フォルダがなければ空。</summary>
        IEnumerable<string> GetDirectories(string dir);
        /// <summary>直下のファイルのフルパス。フォルダがなければ空。</summary>
        IEnumerable<string> GetFiles(string dir);
        void MoveFile(string from, string to);
        void DeleteFile(string path);
    }

    public sealed class DiskFileSystem : IFileSystem
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public string ReadAllText(string path)
        {
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Utf8);
        }

        public void WriteAllTextAtomic(string path, string text)
        {
            EnsureParent(path);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8.GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public void AppendText(string path, string text)
        {
            EnsureParent(path);
            using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                bool needNewline = false;
                if (fs.Length > 0)
                {
                    fs.Seek(-1, SeekOrigin.End);
                    needNewline = fs.ReadByte() != '\n';
                }
                fs.Seek(0, SeekOrigin.End);
                var bytes = Utf8.GetBytes((needNewline ? "\n" : "") + text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public IEnumerable<string> GetDirectories(string dir)
            => Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0];

        public IEnumerable<string> GetFiles(string dir)
            => Directory.Exists(dir) ? Directory.GetFiles(dir) : new string[0];

        public void MoveFile(string from, string to)
        {
            EnsureParent(to);
            File.Move(from, to);
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static void EnsureParent(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}
