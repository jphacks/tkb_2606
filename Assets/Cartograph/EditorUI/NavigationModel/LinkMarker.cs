using System;
using System.Text.RegularExpressions;

namespace Cartograph.EditorUI.Navigation
{
    /// <summary>道の種類(行頭の記号)。ファイルには記号のまま保存され、表示だけ色とアイコンに置き換える。</summary>
    public enum LinkMarkerKind { None, Premise, Contrast, Example }

    /// <summary>Pure helpers for the "==&gt; / &lt;-&gt; / ..&gt; [[note]]: reason" line notation.</summary>
    public static class LinkMarker
    {
        // Same shape as NoteParser.KindRegex, but only when a [[link]] follows (so ordinary text is never hidden).
        private static readonly Regex Line = new Regex(@"^[ \t]*(==>|<->|\.\.>)[ \t]*(?=\[\[)", RegexOptions.Compiled);

        public static string Symbol(LinkMarkerKind kind)
        {
            switch (kind)
            {
                case LinkMarkerKind.Premise: return "==>";
                case LinkMarkerKind.Contrast: return "<->";
                case LinkMarkerKind.Example: return "..>";
                default: return "";
            }
        }

        public static string Label(LinkMarkerKind kind)
        {
            switch (kind)
            {
                case LinkMarkerKind.Premise: return "前提";
                case LinkMarkerKind.Contrast: return "対比";
                case LinkMarkerKind.Example: return "具体例";
                default: return "";
            }
        }

        /// <summary>Badge text shown instead of the raw marker characters.</summary>
        public static string Icon(LinkMarkerKind kind)
        {
            switch (kind)
            {
                case LinkMarkerKind.Premise: return "→ 前提";
                case LinkMarkerKind.Contrast: return "⇄ 対比";
                case LinkMarkerKind.Example: return "・ 具体例";
                default: return "";
            }
        }

        /// <summary>Colour as RGB bytes: blue / red / green.</summary>
        public static void Color(LinkMarkerKind kind, out byte r, out byte g, out byte b)
        {
            switch (kind)
            {
                case LinkMarkerKind.Premise: r = 90; g = 160; b = 255; break;
                case LinkMarkerKind.Contrast: r = 240; g = 100; b = 100; break;
                case LinkMarkerKind.Example: r = 90; g = 200; b = 120; break;
                default: r = g = b = 200; break;
            }
        }

        /// <summary>Length of the leading marker (with surrounding blanks) when the line is a marker line; otherwise 0.</summary>
        public static int PrefixLength(string line, out LinkMarkerKind kind)
        {
            kind = LinkMarkerKind.None;
            if (line == null) return 0;
            var m = Line.Match(line);
            if (!m.Success) return 0;
            kind = FromSymbol(m.Groups[1].Value);
            return m.Length;
        }

        public static LinkMarkerKind Parse(string line)
        {
            PrefixLength(line, out var kind);
            return kind;
        }

        public static LinkMarkerKind FromSymbol(string symbol)
        {
            switch (symbol)
            {
                case "==>": return LinkMarkerKind.Premise;
                case "<->": return LinkMarkerKind.Contrast;
                case "..>": return LinkMarkerKind.Example;
                default: return LinkMarkerKind.None;
            }
        }

        /// <summary>
        /// The link line without trailing newline. The reason is only meaningful with a kind
        /// (the parser ignores it otherwise), so it is dropped for <see cref="LinkMarkerKind.None"/>.
        /// </summary>
        public static string BuildLine(LinkMarkerKind kind, string target, string reason)
        {
            string link = "[[" + target + "]]";
            if (kind == LinkMarkerKind.None) return link;
            string clean = (reason ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return Symbol(kind) + " " + link + (clean.Length == 0 ? "" : ": " + clean);
        }
    }
}
