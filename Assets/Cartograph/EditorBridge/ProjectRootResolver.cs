using System;
using System.IO;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    /// <summary>
    /// 選ばれたフォルダを、プロジェクトのルートに直す。プロジェクトは「ルート/notes/…」で、ルートに .cartograph がある。
    /// ルートの中の notes フォルダを選んでしまうと「notes/notes」を探して空に見えるので、その場合は 1 つ上をルートにする。
    /// </summary>
    public static class ProjectRootResolver
    {
        public static string Resolve(string path, IFileSystem fs)
        {
            if (string.IsNullOrEmpty(path) || fs == null) return path;
            string trimmed = path.TrimEnd('/', '\\');
            if (!string.Equals(Path.GetFileName(trimmed), "notes", StringComparison.OrdinalIgnoreCase)) return path;
            // その中にも notes や .cartograph があるなら、そのフォルダ自身がルート。
            if (fs.DirectoryExists(Path.Combine(trimmed, "notes")) || fs.DirectoryExists(Path.Combine(trimmed, ".cartograph"))) return path;
            string parent = Path.GetDirectoryName(trimmed);
            if (string.IsNullOrEmpty(parent) || !fs.DirectoryExists(Path.Combine(parent, ".cartograph"))) return path;
            return parent;
        }
    }
}
