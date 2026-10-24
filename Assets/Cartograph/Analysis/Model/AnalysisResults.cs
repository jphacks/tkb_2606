using System;
using System.Collections.Generic;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>
    /// ノートの本文に書かれたリンクを、まだ解決していない形で持つ。
    /// 宛先の解決(同名ノート、フォルダ付き、複数候補)は地図全体を見て行う。
    /// </summary>
    public sealed class NoteLink
    {
        /// <summary>リンクが書かれた行の所属拠点の Node.Id。</summary>
        public string FromNode { get; }
        /// <summary>行番号(1 始まり)。</summary>
        public int Line { get; }
        /// <summary>[[ ]] の中の書かれ方そのまま(例: "波動#基礎")。</summary>
        public string Target { get; }
        /// <summary>'#' より前のノート名(フォルダ付きもある)。"[[#見出し]]" では空(同じファイル)。</summary>
        public string TargetNote { get; }
        /// <summary>'#' より後の見出し名。ファイルへのリンクでは null。</summary>
        public string TargetHeading { get; }
        public EdgeKind Kind { get; }
        /// <summary>理由。種別のある行に「: 理由」がなければ null。</summary>
        public string Reason { get; }

        public NoteLink(string fromNode, int line, string target, string targetNote, string targetHeading, EdgeKind kind, string reason)
        {
            if (string.IsNullOrEmpty(fromNode)) throw new ArgumentException("拠点のIDが空です。", nameof(fromNode));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            FromNode = fromNode;
            Line = line;
            Target = target ?? "";
            TargetNote = targetNote ?? "";
            TargetHeading = string.IsNullOrEmpty(targetHeading) ? null : targetHeading;
            Kind = kind;
            Reason = string.IsNullOrEmpty(reason) ? null : reason;
        }
    }

    /// <summary>1 つのノートだけを見て決まる解析結果。変更のあったファイルだけを再解析してキャッシュする単位。</summary>
    public sealed class NoteAnalysis
    {
        public NotePath NoteId { get; }
        /// <summary>解析した本文の版(AnalysisInput.Revision)。</summary>
        public long Revision { get; }
        /// <summary>出現順。Isolated はまだ付かない(地図全体で決まる)。</summary>
        public IReadOnlyList<Node> Nodes { get; }
        /// <summary>見出しの親子から作った auto の道。</summary>
        public IReadOnlyList<Edge> AutoEdges { get; }
        public IReadOnlyList<NoteLink> Links { get; }
        public IReadOnlyList<Fog> Fogs { get; }
        public IReadOnlyList<Flower> Flowers { get; }
        /// <summary>ノート単独で決まる目印(理由なし)。</summary>
        public IReadOnlyList<Mark> Marks { get; }

        public NoteAnalysis(NotePath noteId, long revision, IEnumerable<Node> nodes, IEnumerable<Edge> autoEdges,
            IEnumerable<NoteLink> links, IEnumerable<Fog> fogs, IEnumerable<Flower> flowers, IEnumerable<Mark> marks)
        {
            if (!noteId.IsValid) throw new ArgumentException("ノートIDが空です。", nameof(noteId));
            NoteId = noteId;
            Revision = revision;
            Nodes = Copy(nodes);
            AutoEdges = Copy(autoEdges);
            Links = Copy(links);
            Fogs = Copy(fogs);
            Flowers = Copy(flowers);
            Marks = Copy(marks);
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) =>
            source == null ? Array.Empty<T>() : new List<T>(source).AsReadOnly();
    }

    /// <summary>地図全体の解析結果。マップ描画が読む。</summary>
    public sealed class AnalysisResult
    {
        public IReadOnlyList<Node> Nodes { get; }
        public IReadOnlyList<Edge> Edges { get; }
        public IReadOnlyList<Fog> Fogs { get; }
        public IReadOnlyList<Mark> Marks { get; }
        public IReadOnlyList<Flower> Flowers { get; }
        /// <summary>ノートごとの、解析した本文の版。UI が手元の版と比べて古い結果を捨てるために使う。</summary>
        public IReadOnlyDictionary<NotePath, long> Revisions { get; }

        public AnalysisResult(IEnumerable<Node> nodes, IEnumerable<Edge> edges, IEnumerable<Fog> fogs,
            IEnumerable<Mark> marks, IEnumerable<Flower> flowers, IReadOnlyDictionary<NotePath, long> revisions)
        {
            Nodes = Copy(nodes);
            Edges = Copy(edges);
            Fogs = Copy(fogs);
            Marks = Copy(marks);
            Flowers = Copy(flowers);
            var copy = new Dictionary<NotePath, long>();
            if (revisions != null)
                foreach (var pair in revisions) copy[pair.Key] = pair.Value;
            Revisions = copy;
        }

        public static readonly AnalysisResult Empty =
            new AnalysisResult(null, null, null, null, null, null);

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) =>
            source == null ? Array.Empty<T>() : new List<T>(source).AsReadOnly();
    }
}
