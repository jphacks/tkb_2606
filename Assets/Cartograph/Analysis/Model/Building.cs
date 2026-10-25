using System;
using Cartograph.Core;

namespace Cartograph.Analysis
{
    /// <summary>強調の強さ。Markdown の装飾で測る。見出しの中 = Strong、太字 = Medium、通常本文 = Weak。</summary>
    public enum Emphasis : byte { Weak = 0, Medium = 1, Strong = 2 }

    /// <summary>
    /// 別名の検出に使う、本文 1 行ぶんの走査用テキスト。ノート単位の解析で作り、地図全体の照合で読む。
    /// 数えない文字(インラインコード、リンクの括弧、太字の記号)は '\0' に置き換えてあり、語の途切れとして扱う。
    /// </summary>
    public sealed class ScanLine
    {
        public string Node { get; }
        public int Line { get; }
        public string Text { get; }
        /// <summary>Text の 1 文字ごとの強調の強さ(Emphasis の値)。</summary>
        public byte[] Levels { get; }

        public ScanLine(string node, int line, string text, byte[] levels)
        {
            if (string.IsNullOrEmpty(node)) throw new ArgumentException("拠点のIDが空です。", nameof(node));
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line), "行は 1 始まりです。");
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Levels = levels ?? throw new ArgumentNullException(nameof(levels));
            if (levels.Length != text.Length) throw new ArgumentException("強調の長さが本文と合いません。", nameof(levels));
            Node = node;
            Line = line;
        }
    }

    /// <summary>
    /// 建物。固有名詞ノートの別名が、ある拠点の本文に出現したことを表す。
    /// (固有名詞ノート, 拠点)の組ごとに 1 つ。出現回数は強調の段階ごとに分けて持つ(点数への換算は描画側の設定)。
    /// </summary>
    public sealed class Building
    {
        public NotePath ProperNounNote { get; }
        /// <summary>別名が出現した拠点の Node.Id。</summary>
        public string Node { get; }
        public int Strong { get; }
        public int Medium { get; }
        public int Weak { get; }
        public int Count => Strong + Medium + Weak;

        public Building(NotePath properNounNote, string node, int strong, int medium, int weak)
        {
            if (!properNounNote.IsValid) throw new ArgumentException("ノートIDが空です。", nameof(properNounNote));
            if (string.IsNullOrEmpty(node)) throw new ArgumentException("拠点のIDが空です。", nameof(node));
            if (strong < 0 || medium < 0 || weak < 0) throw new ArgumentOutOfRangeException(nameof(weak));
            if (strong + medium + weak == 0) throw new ArgumentException("出現回数が 0 です。");
            ProperNounNote = properNounNote;
            Node = node;
            Strong = strong;
            Medium = medium;
            Weak = weak;
        }
    }
}
