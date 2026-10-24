using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cartograph.EditorUI.Navigation
{
    // Display count only; does not change the saved Markdown or provenance.
    public static class MarkdownTextCount
    {
        public static int Count(string markdown) => new StringInfo(PlainText(markdown)).LengthInTextElements;
        public static string PlainText(string markdown)
        {
            var output = new StringBuilder(); string fence = null;
            foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw;
                var marker = Regex.Match(line, @"^\s{0,3}(`{3,}|~{3,})");
                if (fence != null)
                {
                    if (marker.Success && marker.Groups[1].Value[0] == fence[0] && marker.Groups[1].Length >= fence.Length) fence = null;
                    else output.Append(line);
                    continue;
                }
                if (marker.Success) { fence = marker.Groups[1].Value; continue; }
                if (Regex.IsMatch(line, @"^\s{0,3}(?:([-*_])\s*){3,}$|^\s{0,3}(?:=+|-+)\s*$|^\s*\[[^\]]+\]:\s*\S+")) continue;
                line = Regex.Replace(line, @"^\s{0,3}(?:>\s*)+", "");
                line = Regex.Replace(line, @"^\s{0,3}#{1,6}\s+", "");
                line = Regex.Replace(line, @"\s+#+\s*$", "");
                line = Regex.Replace(line, @"^\s*(?:[-+*]|\d+[.)])\s+(?:\[[ xX]\]\s+)?", "");
                var code = new System.Collections.Generic.List<string>();
                line = Regex.Replace(line, @"(`+)(.+?)\1", m => { code.Add(m.Groups[2].Value); return "\uE000" + (code.Count - 1) + "\uE001"; });
                line = Regex.Replace(line, @"\\([\\`*_{}\[\]()#+.!>~-])", m => { code.Add(m.Groups[1].Value); return "\uE000" + (code.Count - 1) + "\uE001"; });
                line = Regex.Replace(line, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);
                line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^\s)]*(?:\s+""[^""]*"")?\)", "$1");
                line = Regex.Replace(line, @"\[([^\]]+)\]\[[^\]]*\]", "$1");
                line = Regex.Replace(line, @"(\*\*|__|~~)(.+?)\1", "$2");
                line = Regex.Replace(line, @"\*([^\s*](?:[^*]*?[^\s*])?)\*", "$1");
                line = Regex.Replace(line, @"(?<!\w)_([^\r\n]+?)_(?!\w)", "$1");
                line = Regex.Replace(line, @"<([^ >]+@[^ >]+|https?://[^ >]+)>", "$1");
                line = Regex.Replace(line, @"</?[A-Za-z][^>]*>", "");
                line = Regex.Replace(line, @"\\([\\`*_{}\[\]()#+.!>~-])", "$1");
                line = System.Net.WebUtility.HtmlDecode(line);
                line = Regex.Replace(line, "\uE000(\\d+)\uE001", m => int.TryParse(m.Groups[1].Value, out var index) && index < code.Count ? code[index] : m.Value);
                output.Append(line);
            }
            return output.ToString();
        }
    }
}
