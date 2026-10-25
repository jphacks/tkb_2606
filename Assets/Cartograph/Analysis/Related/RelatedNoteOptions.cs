using System;

namespace Cartograph.Analysis
{
    /// <summary>
    /// 関連ノート検出の重みとしきい値。外から調整できるよう値だけを持つ。
    /// 合計点が <see cref="Threshold"/> 以上の組だけを候補にする。
    /// 既定値では、感情の類似だけ・キーワード 1 つだけでは候補にならない。
    /// </summary>
    public sealed class RelatedNoteOptions
    {
        /// <summary>候補にする合計点の下限。</summary>
        public double Threshold { get; set; } = 2.0;

        /// <summary>共通するキーワード 1 語あたりの点。</summary>
        public double KeywordWeight { get; set; } = 1.0;

        /// <summary>キーワードの点の合計の上限(語数が多すぎても他の根拠を飲み込まない)。</summary>
        public double MaxKeywordScore { get; set; } = 3.0;

        /// <summary>両方のノートが同じ固有名詞(別名つきノート)に触れているときの点。</summary>
        public double ProperNounWeight { get; set; } = 1.5;

        /// <summary>一方のノートの本文に、もう一方のノート名(題名・別名・ファイル名)が出てくるときの点。</summary>
        public double MentionWeight { get; set; } = 3.0;

        /// <summary>感情の傾向が近いときの最大点(類似度を掛ける)。低めにして補助にとどめる。</summary>
        public double EmotionWeight { get; set; } = 0.5;

        /// <summary>感情の類似を見るのに、両方のノートに必要な感情語の数。</summary>
        public int MinEmotionWords { get; set; } = 3;

        /// <summary>感情の類似とみなすコサイン類似度の下限(0〜1)。</summary>
        public double MinEmotionSimilarity { get; set; } = 0.8;

        /// <summary>ノート名の言及と数えるノート名の最短文字数(1 文字の名前を拾わない)。</summary>
        public int MinMentionLength { get; set; } = 2;

        /// <summary>このノート数以上のとき、多くのノートに出る語をキーワードから外す。</summary>
        public int CommonKeywordMinNotes { get; set; } = 4;

        /// <summary>この割合(0〜1)を超えるノートに出る語は、ありふれた語としてキーワードから外す。</summary>
        public double MaxKeywordDocumentFraction { get; set; } = 0.5;

        /// <summary>理由ラベルに並べる語の最大数。</summary>
        public int MaxReasonTerms { get; set; } = 3;
    }
}
