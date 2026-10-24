using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Cartograph.Analysis;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    public sealed class RelatedLinkRange
    {
        public int Start { get; }
        public int Length { get; }
        public RelatedLinkRange(int start, int length) { Start = start; Length = length; }
    }

    /// <summary>Locates only parser-recognized links; never removes their surrounding prose.</summary>
    public static class RelatedLinkRemoval
    {
        public static async System.Threading.Tasks.Task Disconnect(IReviewingEditorWorkspace workspace, string currentId, string targetId)
        {
            if (!(workspace is IReviewedNoteAccess access)) throw new InvalidOperationException("ノートを開いてください。");
            var tree = workspace.Tree;
            var ids = workspace.Documents.Select(d => d.id).ToArray();
            if (currentId == targetId || !ids.Contains(currentId) || !ids.Contains(targetId))
                throw new InvalidOperationException("関連ノートを選び直してください。");
            await access.LoadDocument(currentId); await access.LoadDocument(targetId);
            if (!ReferenceEquals(tree, workspace.Tree)) throw new OperationCanceledException("保存先が変わりました。");
            foreach (var pair in new[] { new[] { currentId, targetId }, new[] { targetId, currentId } })
            {
                var record = workspace.Documents.First(d => d.id == pair[0]);
                foreach (var range in GetRanges(record.markdown, pair[0], pair[1], ids))
                {
                    record = workspace.Documents.First(d => d.id == pair[0]);
                    var result = access.ReviewDocument(record.id, record.revision, new ChangeCandidate
                    {
                        NewText = record.markdown.Remove(range.Start, range.Length),
                        SelStartBefore = range.Start, SelEndBefore = range.Start + range.Length,
                        Evidence = new InputEvidence { Kind = EvidenceKind.Format }
                    });
                    if (!result.Approved) throw new InvalidOperationException(result.Reason);
                }
            }
        }
        private static readonly Regex Link = new Regex(@"\[\[([^\[\]\r\n]+)\]\]");
        public static IReadOnlyList<RelatedLinkRange> GetRanges(string text, string currentId, string targetId, IEnumerable<string> allNoteIds)
        {
            text = text ?? "";
            var current = NotePath.Parse(currentId);
            var target = NotePath.Parse(targetId);
            var ids = new HashSet<string>(allNoteIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            ids.Add(current.Value);
            if (!ids.Contains(target.Value)) return Array.Empty<RelatedLinkRange>();
            var parsed = NoteParser.Parse(new AnalysisInput(current, new LabeledText(text, Origin.Unknown), 0));
            var accepted = new Dictionary<int, HashSet<string>>();
            foreach (var link in parsed.Links)
            {
                bool matches;
                if (link.TargetNote.Length == 0) matches = current == target;
                else if (link.TargetNote.IndexOf('/') >= 0)
                    matches = NotePath.TryParse(link.TargetNote, out var path) && path == target;
                else
                    matches = link.TargetNote == target.Name && ids.Count(id => NotePath.Parse(id).Name == link.TargetNote) == 1;
                if (!matches) continue;
                if (!accepted.TryGetValue(link.Line, out var targets)) accepted[link.Line] = targets = new HashSet<string>(StringComparer.Ordinal);
                targets.Add(link.Target);
            }
            var ranges = new List<RelatedLinkRange>();
            int start = 0, lineNumber = 1;
            while (start <= text.Length)
            {
                int end = start;
                while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
                if (accepted.TryGetValue(lineNumber, out var targets))
                    foreach (Match match in Link.Matches(text.Substring(start, end - start)))
                        if (targets.Contains(match.Groups[1].Value.Trim())) ranges.Add(new RelatedLinkRange(start + match.Index, match.Length));
                if (end == text.Length) break;
                start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
                lineNumber++;
            }
            ranges.Sort((a, b) => b.Start.CompareTo(a.Start));
            return ranges.AsReadOnly();
        }
    }
}
