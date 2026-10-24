using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    /// <summary>A single editable line. Offsets are UTF-16, relative to Raw.</summary>
    public sealed class MarkdownBlock
    {
        private readonly int[] visibleOffsets;
        private readonly int prefixLength;
        public int SourceStart { get; }
        public int SourceLength => Raw.Length;
        public string Raw { get; }
        public string DisplayText { get; }
        public int HeadingLevel { get; }
        internal MarkdownBlock(int start, string raw, int prefix, int heading, string display, int[] offsets)
        { SourceStart = start; Raw = raw; prefixLength = prefix; HeadingLevel = heading; DisplayText = display; visibleOffsets = offsets; }
        public int RawOffset(int displayOffset)
        {
            if (displayOffset < 0 || displayOffset > DisplayText.Length) throw new ArgumentOutOfRangeException(nameof(displayOffset));
            return displayOffset < visibleOffsets.Length ? visibleOffsets[displayOffset] :
                visibleOffsets.Length == 0 ? prefixLength : visibleOffsets[visibleOffsets.Length - 1] + 1;
        }
        public string ReplaceVisible(string newDisplay)
        {
            newDisplay = newDisplay ?? "";
            var diff = TextDiff.Compute(DisplayText, newDisplay);
            if (diff.IsEmpty) return Raw;
            // Removing all visible content must not leave empty delimiter pairs
            // (**** would become a visible rule/marker on the next projection).
            if (newDisplay.Length == 0) return Raw.Substring(0, prefixLength);
            int start = RawOffset(diff.Pos);
            int end = diff.Deleted.Length == 0 ? start : visibleOffsets[diff.Pos + diff.Deleted.Length - 1] + 1;
            string replacement = Raw.Remove(start, end - start).Insert(start, diff.Inserted);
            // A selection may span several independently formatted runs. Drop
            // their wrappers instead of retaining unmatched closing delimiters.
            if (MarkdownBlockProjection.Parse(replacement)[0].DisplayText != newDisplay &&
                newDisplay.IndexOf('\n') < 0 && newDisplay.IndexOf('\r') < 0)
                return Raw.Substring(0, prefixLength) + newDisplay;
            return replacement;
        }
    }

    public static class MarkdownBlockProjection
    {
        public static int MapSelectionOffset(int offset, DiffResult diff)
        {
            if (offset <= diff.Pos) return offset;
            if (offset >= diff.Pos + diff.Deleted.Length) return offset + diff.Inserted.Length - diff.Deleted.Length;
            return diff.Pos + diff.Inserted.Length;
        }
        private static readonly Regex Prefix = new Regex(@"^( {0,3})(?:(#{1,6})(?:[ \t]+|$)|(?:[-+*]|\d+[.)])[ \t]+|>[ \t]?)");
        public static IReadOnlyList<MarkdownBlock> Parse(string raw)
        {
            raw = raw ?? "";
            var blocks = new List<MarkdownBlock>();
            int start = 0;
            while (true)
            {
                int end = raw.IndexOf('\n', start);
                if (end < 0) end = raw.Length;
                int length = end - start;
                if (length > 0 && raw[end - 1] == '\r') length--;
                string line = raw.Substring(start, length);
                var prefix = Prefix.Match(line);
                int skip = prefix.Success ? prefix.Length : 0;
                int heading = prefix.Success ? prefix.Groups[2].Length : 0;
                var display = new StringBuilder(); var offsets = new List<int>();
                ProjectInline(line, skip, line.Length, display, offsets);
                blocks.Add(new MarkdownBlock(start, line, skip, heading, display.ToString(), offsets.ToArray()));
                if (end == raw.Length) break;
                start = end + 1;
            }
            return blocks.AsReadOnly();
        }
        private static void ProjectInline(string raw, int start, int end, StringBuilder display, List<int> offsets)
        {
            for (int i = start; i < end;)
            {
                if (raw[i] == '\\' && i + 1 < end)
                { display.Append(raw[i + 1]); offsets.Add(i + 1); i += 2; continue; }
                if (i + 1 < end && raw[i] == '[' && raw[i + 1] == '[')
                {
                    int close = raw.IndexOf("]]", i + 2, StringComparison.Ordinal);
                    if (close >= 0 && close < end)
                    {
                        int pipe = raw.IndexOf('|', i + 2, close - i - 2);
                        ProjectInline(raw, pipe >= 0 ? pipe + 1 : i + 2, close, display, offsets);
                        i = close + 2; continue;
                    }
                }
                if (raw[i] == '[')
                {
                    int labelEnd = raw.IndexOf("](", i + 1, StringComparison.Ordinal);
                    int close = labelEnd < 0 ? -1 : raw.IndexOf(')', labelEnd + 2);
                    if (close >= 0 && close < end)
                    { ProjectInline(raw, i + 1, labelEnd, display, offsets); i = close + 1; continue; }
                }
                if (raw[i] == '*' || raw[i] == '_' || raw[i] == '`' || raw[i] == '~')
                {
                    int count = 1;
                    while (i + count < end && raw[i + count] == raw[i] && count < 3) count++;
                    string marker = raw.Substring(i, count);
                    int close = raw.IndexOf(marker, i + count, StringComparison.Ordinal);
                    if (close > i + count && close < end && (raw[i] != '~' || count == 2))
                    {
                        if (raw[i] == '`')
                            for (int n = i + count; n < close; n++) { display.Append(raw[n]); offsets.Add(n); }
                        else ProjectInline(raw, i + count, close, display, offsets);
                        i = close + count; continue;
                    }
                }
                display.Append(raw[i]); offsets.Add(i); i++;
            }
        }
    }
}
