using System;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>拠点(見出し)。見出しのないファイルは、ファイル名を Title とする 1 拠点(Level = 0, Line = 1)。</summary>
    public sealed class Node
    {
        /// <summary>"ノートID#見出しパス"(例: "大学用/授業/ノート#フーリエ変換/複素数")。同じ階層に同名の見出しがあれば、後ろに "~2"、"~3" を付ける。見出しのないファイルは "ノートID#"。</summary>
        public string Id { get; }
        public NotePath File { get; }
        public string Title { get; }
        /// <summary>見出しの深さ(1〜3)。見出しのないファイルの拠点は 0。</summary>
        public int Level { get; }
        /// <summary>見出し行の行番号(1 始まり)。</summary>
        public int Line { get; }
        /// <summary>見出し行の先頭の文字位置(UTF-16)。分割エディタでその位置を開くために使う。</summary>
        public int Position { get; }
        /// <summary>見出しの直下から次の見出しの手前までの、typed の文字数(空白・改行・コードブロックの中は数えない)。</summary>
        public int TypedChars { get; }
        /// <summary>ファイルの最初の見出しにだけ付く。ほかの島と道で 1 本もつながっていない島を表す。</summary>
        public bool Isolated { get; }

        public Node(string id, NotePath file, string title, int level, int line, int position, int typedChars, bool isolated)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("拠点のIDが空です。", nameof(id));
            if (!file.IsValid) throw new ArgumentException("ノートIDが空です。", nameof(file));
            if (level < 0 || level > 3) throw new ArgumentOutOfRangeException(nameof(level));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
            if (typedChars < 0) throw new ArgumentOutOfRangeException(nameof(typedChars));
            Id = id;
            File = file;
            Title = title ?? "";
            Level = level;
            Line = line;
            Position = position;
            TypedChars = typedChars;
            Isolated = isolated;
        }

        public Node WithIsolated(bool isolated) =>
            new Node(Id, File, Title, Level, Line, Position, TypedChars, isolated);
    }

    public enum EdgeKind { Unspecified, Premise, Contrast, Example }

    /// <summary>道。From と To は Node.Id。</summary>
    public sealed class Edge
    {
        public string From { get; }
        public string To { get; }
        /// <summary>未指定 = 種別のないふつうのリンク(と見出しの親子)。前提 = ==&gt;、対比 = &lt;-&gt;、具体例 = ..&gt;。</summary>
        public EdgeKind Kind { get; }
        /// <summary>理由。書かれていなければ null。</summary>
        public string Reason { get; }
        /// <summary>From と To のファイルのフォルダが異なる。</summary>
        public bool CrossFolder { get; }
        /// <summary>見出しの親子から自動で作った道。</summary>
        public bool Auto { get; }

        public Edge(string from, string to, EdgeKind kind, string reason, bool crossFolder, bool auto)
        {
            if (string.IsNullOrEmpty(from)) throw new ArgumentException("道の始点が空です。", nameof(from));
            if (string.IsNullOrEmpty(to)) throw new ArgumentException("道の終点が空です。", nameof(to));
            From = from;
            To = to;
            Kind = kind;
            Reason = string.IsNullOrEmpty(reason) ? null : reason;
            CrossFolder = crossFolder;
            Auto = auto;
        }
    }

    /// <summary>霧。?・TODO: を含む行。</summary>
    public sealed class Fog
    {
        /// <summary>所属する拠点の Node.Id。</summary>
        public string Node { get; }
        /// <summary>行番号(1 始まり)。</summary>
        public int Line { get; }
        /// <summary>その行の文字列(改行を除く)。</summary>
        public string Text { get; }

        public Fog(string node, int line, string text)
        {
            if (string.IsNullOrEmpty(node)) throw new ArgumentException("拠点のIDが空です。", nameof(node));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            Node = node;
            Line = line;
            Text = text ?? "";
        }
    }

    public enum MarkType { NoReason, BrokenLink, Isolated }

    /// <summary>BrokenLink の副区分。それ以外の種類では None。</summary>
    public enum MarkDetail { None, Missing, Ambiguous }

    /// <summary>道や拠点に付く目印。Edge を作れないリンク(リンク切れ)も表せるよう、リンクの出どころを自分で持つ。</summary>
    public sealed class Mark
    {
        public MarkType Type { get; }
        public MarkDetail Detail { get; }
        /// <summary>目印が付く拠点(リンクの所属拠点、または孤島の拠点)の Node.Id。</summary>
        public string Node { get; }
        /// <summary>リンクが書かれた行(1 始まり)。孤島は拠点の見出し行。</summary>
        public int Line { get; }
        /// <summary>リンク先の書かれ方(例: "波動#基礎")。孤島では空。</summary>
        public string Target { get; }

        public Mark(MarkType type, MarkDetail detail, string node, int line, string target)
        {
            if (string.IsNullOrEmpty(node)) throw new ArgumentException("拠点のIDが空です。", nameof(node));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            if (type != MarkType.BrokenLink && detail != MarkDetail.None)
                throw new ArgumentException("副区分を持てるのはリンク切れだけです。", nameof(detail));
            if (type == MarkType.BrokenLink && detail == MarkDetail.None)
                throw new ArgumentException("リンク切れには副区分が必要です。", nameof(detail));
            Type = type;
            Detail = detail;
            Node = node;
            Line = line;
            Target = target ?? "";
        }

        /// <summary>クリック時に出す 1 文(仕様書の案)。</summary>
        public string Message
        {
            get
            {
                switch (Type)
                {
                    case MarkType.NoReason: return "この道の理由は、まだ書かれていません";
                    case MarkType.BrokenLink:
                        return Detail == MarkDetail.Ambiguous
                            ? "この道の先が、複数あります"
                            : "この道の先は、まだ地図にありません";
                    default: return "ここは、まだどこともつながっていません";
                }
            }
        }
    }

    /// <summary>感情の分類。喜・怒・哀・楽。</summary>
    public enum Emotion { Joy, Anger, Sorrow, Pleasure }

    /// <summary>花。感情語 1 つにつき 1 輪。</summary>
    public sealed class Flower
    {
        public string Node { get; }
        /// <summary>感情語が書かれた行(1 始まり)。</summary>
        public int Line { get; }
        /// <summary>辞書の語(本文に書かれた形)。</summary>
        public string Word { get; }
        public Emotion Emotion { get; }
        /// <summary>否定が続いて、色を失った枯れた花。</summary>
        public bool Withered { get; }

        public Flower(string node, int line, string word, Emotion emotion, bool withered)
        {
            if (string.IsNullOrEmpty(node)) throw new ArgumentException("拠点のIDが空です。", nameof(node));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            if (string.IsNullOrEmpty(word)) throw new ArgumentException("語が空です。", nameof(word));
            Node = node;
            Line = line;
            Word = word;
            Emotion = emotion;
            Withered = withered;
        }
    }
}
