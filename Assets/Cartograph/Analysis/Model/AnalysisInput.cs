using System;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 解析へ渡す入力。本文と来歴ラベル、revision をひと組で扱う。
    /// Document は呼び出し側が NoteSession.CloneDocument() などで切り出したコピーを渡す(解析側は変更しない)。
    /// 履歴のないノートは、本文全体を Origin.Unknown にしたものを渡す。
    /// </summary>
    public sealed class AnalysisInput
    {
        /// <summary>notes/ からの相対パス(拡張子なし、区切りは '/')。</summary>
        public Cartograph.Core.NotePath NoteId { get; }

        /// <summary>本文とラベル列。位置はすべて UTF-16 単位。</summary>
        public Cartograph.Core.LabeledText Document { get; }

        /// <summary>この本文の版。結果に引き継がれ、古い結果かどうかの判別に使う。</summary>
        public long Revision { get; }

        public string Text => Document.Text;

        public AnalysisInput(Cartograph.Core.NotePath noteId, Cartograph.Core.LabeledText document, long revision)
        {
            if (!noteId.IsValid) throw new ArgumentException("ノートIDが空です。", nameof(noteId));
            NoteId = noteId;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Revision = revision;
        }
    }
}
