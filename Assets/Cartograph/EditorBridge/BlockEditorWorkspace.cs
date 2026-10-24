using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    /// <summary>Optional clipboard path for selections whose hidden syntax makes their raw offsets discontinuous.</summary>
    public interface IProjectedCopyAccess
    {
        void NotifyProjectedCopied(string documentId, string visibleText, IReadOnlyList<int> sourceOffsets);
    }

    /// <summary>Projects one source line into an editor without bypassing Core's input review.</summary>
    public sealed class BlockEditorWorkspace : IReviewingEditorWorkspace, IReviewedNoteAccess
    {
        private readonly IReviewingEditorWorkspace source;
        private readonly IReviewedNoteAccess access;
        private readonly string id;
        private readonly int blockIndex;
        public Func<Tuple<int, int>> ReplacementSelection { get; set; }
        public BlockEditorWorkspace(IReviewingEditorWorkspace source, string id, int blockIndex)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            access = source as IReviewedNoteAccess ?? throw new ArgumentException("本文の審査に対応していません。", nameof(source));
            this.id = id ?? throw new ArgumentNullException(nameof(id));
            if (blockIndex < 0) throw new ArgumentOutOfRangeException(nameof(blockIndex));
            this.blockIndex = blockIndex;
        }
        public event Action Changed { add { source.Changed += value; } remove { source.Changed -= value; } }
        public bool IsPersistent => source.IsPersistent;
        public SaveState State => source.State;
        public string Error => source.Error;
        public FolderNode Tree => source.Tree;
        private DocumentRecord RawRecord => source.Documents.FirstOrDefault(d => d.id == id);
        private MarkdownBlock Block(string raw)
        {
            var blocks = MarkdownBlockProjection.Parse(raw);
            if (blockIndex >= blocks.Count) throw new InvalidOperationException("編集対象の行が変更されました。ノートを開き直してください。");
            return blocks[blockIndex];
        }
        private void RequireId(string requested)
        { if (requested != id) throw new ArgumentException("別のノートは編集できません。", nameof(requested)); }
        public DocumentRecord Current
        {
            get
            {
                var raw = RawRecord;
                if (raw == null) return null;
                var projected = raw.Copy(); projected.markdown = Block(raw.markdown).DisplayText;
                return projected;
            }
        }
        public IReadOnlyList<DocumentRecord> Documents
        { get { var current = Current; return current == null ? Array.Empty<DocumentRecord>() : new[] { current }; } }
        public async Task<DocumentRecord> LoadDocument(string documentId)
        { RequireId(documentId); await access.LoadDocument(id); return Current; }
        public LabeledText GetLabeledDocument(string documentId)
        {
            RequireId(documentId);
            var original = access.GetLabeledDocument(id);
            var block = Block(original.Text);
            var projectedRuns = new List<Run>();
            var rawRuns = original.Labels.Runs;
            int runIndex = 0, runEnd = rawRuns.Count == 0 ? 0 : rawRuns[0].Length;
            for (int i = 0; i < block.DisplayText.Length; i++)
            {
                int offset = block.SourceStart + block.RawOffset(i);
                while (runIndex + 1 < rawRuns.Count && offset >= runEnd)
                { runIndex++; runEnd += rawRuns[runIndex].Length; }
                projectedRuns.Add(new Run(1, rawRuns[runIndex].Origin));
            }
            var projected = new LabeledText();
            projected.Insert(0, block.DisplayText, LabeledSlice.FromRuns(projectedRuns));
            return projected;
        }
        public ReviewResult ReviewDocument(string documentId, long expectedRevision, ChangeCandidate candidate)
        {
            RequireId(documentId);
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            var raw = RawRecord ?? throw new InvalidOperationException("ノートが開かれていません。");
            if (raw.revision != expectedRevision) throw new InvalidOperationException("別のペインで更新されています。最新の本文を確認してください。");
            var block = Block(raw.markdown);
            string replacement = block.ReplaceVisible(candidate.NewText);
            var mapped = new ChangeCandidate
            {
                NewText = raw.markdown.Remove(block.SourceStart, block.SourceLength).Insert(block.SourceStart, replacement),
                SelStartBefore = SourceOffset(block, candidate.SelStartBefore),
                SelEndBefore = SourceOffset(block, candidate.SelEndBefore),
                Evidence = candidate.Evidence
            };
            var range = ReplacementSelection?.Invoke();
            if (range != null)
            {
                var inserted = TextDiff.Compute(block.DisplayText, candidate.NewText).Inserted;
                int start = Math.Min(range.Item1, range.Item2), end = Math.Max(range.Item1, range.Item2);
                mapped.NewText = raw.markdown.Remove(start, end - start).Insert(start, inserted);
                mapped.SelStartBefore = start; mapped.SelEndBefore = end;
            }
            var result = access.ReviewDocument(id, expectedRevision, mapped);
            if (result.Approved) return result;
            return new ReviewResult
            {
                Approved = false, Reason = result.Reason, Ops = result.Ops,
                RevertText = block.DisplayText,
                RevertSelStart = LocalSelection(candidate.SelStartBefore, block.DisplayText.Length),
                RevertSelEnd = LocalSelection(candidate.SelEndBefore, block.DisplayText.Length)
            };
        }
        private static int SourceOffset(MarkdownBlock block, int local)
        { return local < 0 ? -1 : block.SourceStart + block.RawOffset(Math.Min(local, block.DisplayText.Length)); }
        private static int LocalSelection(int value, int length) => Math.Max(0, Math.Min(value, length));
        public ReviewResult ReviewChange(string documentId, ChangeCandidate candidate)
        { return ReviewDocument(documentId, Current.revision, candidate); }
        public void NotifyCopied(string documentId, int start, int length)
        {
            RequireId(documentId);
            var block = Block(RawRecord.markdown);
            if (start < 0 || length < 0 || start + length > block.DisplayText.Length) throw new ArgumentOutOfRangeException(nameof(start));
            if (length == 0) return;
            var offsets = Enumerable.Range(start, length).Select(i => block.SourceStart + block.RawOffset(i)).ToArray();
            if (source is IProjectedCopyAccess projected)
                projected.NotifyProjectedCopied(id, block.DisplayText.Substring(start, length), offsets);
            else
            {
                if (offsets[offsets.Length - 1] - offsets[0] + 1 != length)
                    throw new InvalidOperationException("書式をまたぐコピーにはまだ対応していません。");
                source.NotifyCopied(id, offsets[0], length);
            }
        }
        public Task FlushSave() => source.FlushSave();
        public string ExportMarkdown() => RawRecord?.markdown ?? "";
        public Task RenameDocument(string documentId, string title) => throw new NotSupportedException("ノート一覧から名前を変更してください。");
        public Task CreateDocument() => throw new NotSupportedException();
        public Task OpenDocument(string documentId) => throw new NotSupportedException();
        public Task OpenProject(string path) => throw new NotSupportedException();
        public void UpdateTitle(string title) => throw new NotSupportedException();
        public void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin) => throw new NotSupportedException("入力の証拠をReviewDocumentへ渡してください。");
    }
}
