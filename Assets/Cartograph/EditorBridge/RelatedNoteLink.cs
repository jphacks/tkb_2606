using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    public sealed class RelatedLinkEdit
    {
        public string Text { get; }
        public int Cursor { get; }
        public RelatedLinkEdit(string text, int cursor) { Text = text; Cursor = cursor; }
    }
    public static class RelatedNoteLink
    {
        public static RelatedLinkEdit Insert(string text, int cursor, string current, string target, IEnumerable<string> notes)
        {
            var ids = new HashSet<string>(notes ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (current == null || !ids.Contains(current) || !ids.Contains(target ?? "") || target == current)
                throw new InvalidOperationException("関連づけるノートを選び直してください。");
            var path = NotePath.Parse(target);
            if (target.IndexOfAny(new[] { '#', '[', ']' }) >= 0)
                throw new InvalidOperationException("リンク先の名前に # や角括弧が含まれています。ノート名を変更してください。");
            // The shared parser treats a root-level bare name as a name lookup.
            if (path.FolderPath.Length == 0 && ids.Count(id => NotePath.Parse(id).Name == path.Name) > 1)
                throw new InvalidOperationException("同名のノートが複数あります。ルートのノートを一意の名前に変更してください。");
            text = text ?? ""; cursor = Math.Max(0, Math.Min(cursor, text.Length));
            var parsed = Cartograph.Analysis.NoteParser.Parse(new Cartograph.Analysis.AnalysisInput(
                NotePath.Parse(current), new LabeledText(text, Origin.Unknown), 0));
            if (parsed.Links.Any(link => link.TargetNote == target && link.TargetHeading == null))
                return new RelatedLinkEdit(text, cursor);
            int end = text.IndexOf('\n', cursor); if (end < 0) end = text.Length;
            string link = (end > 0 && text[end - 1] != '\n' ? "\n" : "") + "[[" + target + "]]\n";
            return new RelatedLinkEdit(text.Insert(end, link), end + link.Length);
        }
    }
}
