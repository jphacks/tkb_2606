using System;
using System.Collections.Generic;

namespace Cartograph.Analysis
{
    /// <summary>検出への入力。ノートID は <c>NotePath.Value</c> と同じ(拡張子なし、区切りは '/')。</summary>
    public sealed class RelatedNoteSource
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }

        public RelatedNoteSource(string id, string title, string body)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("ノートIDが空です。", nameof(id));
            Id = id;
            Title = title ?? "";
            Body = body ?? "";
        }
    }

    /// <summary>向きのないノートの組。除外(確定済み・却下済み)の指定と候補の識別に使う。</summary>
    public readonly struct RelatedPair : IEquatable<RelatedPair>
    {
        /// <summary>ID の序数順で小さいほう。</summary>
        public string A { get; }
        public string B { get; }

        public RelatedPair(string first, string second)
        {
            if (string.CompareOrdinal(first, second) <= 0) { A = first; B = second; }
            else { A = second; B = first; }
        }

        public bool Involves(string id) => string.Equals(A, id, StringComparison.Ordinal) || string.Equals(B, id, StringComparison.Ordinal);
        public bool Equals(RelatedPair other) => string.Equals(A, other.A, StringComparison.Ordinal) && string.Equals(B, other.B, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RelatedPair p && Equals(p);
        public override int GetHashCode() => unchecked((A?.GetHashCode() ?? 0) * 397 ^ (B?.GetHashCode() ?? 0));
        public override string ToString() => A + " <-> " + B;
    }

    public enum RelatedReasonKind { Mention, ProperNoun, Keyword, Emotion }

    /// <summary>候補になった根拠 1 つ。Label はそのまま画面に出せる日本語。</summary>
    public sealed class RelatedReason
    {
        public RelatedReasonKind Kind { get; }
        public double Score { get; }
        public string Label { get; }

        public RelatedReason(RelatedReasonKind kind, double score, string label)
        {
            Kind = kind; Score = score; Label = label ?? "";
        }
    }

    /// <summary>関連ノートの候補。From と To は <see cref="RelatedPair"/> の順(向きの意味はない)。</summary>
    public sealed class RelatedNoteCandidate
    {
        public string From { get; }
        public string To { get; }
        public double Score { get; }
        /// <summary>点の高い順。</summary>
        public IReadOnlyList<RelatedReason> Reasons { get; }
        public RelatedPair Pair => new RelatedPair(From, To);

        public RelatedNoteCandidate(string from, string to, double score, IReadOnlyList<RelatedReason> reasons)
        {
            From = from; To = to; Score = score; Reasons = reasons;
        }

        public bool Involves(string id) => Pair.Involves(id);
        public string Other(string id) => string.Equals(From, id, StringComparison.Ordinal) ? To : From;
    }
}
