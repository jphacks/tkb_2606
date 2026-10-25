# Cartograph.Analysis

Unity 非依存の Markdown 解析(`noEngineReferences`)。`Cartograph.Core` だけを参照する。
`Model/` に型、`Parsing/` に解析本体。いまあるのは 1 ノートの解析 `NoteParser.Parse`(拠点・親子の道・本文量・リンク・霧・理由なしの目印)。
地図全体は `ProjectAnalyzer.Analyze(AnalysisInput の一覧)`(リンクの解決、孤島、リンク切れ)。毎回すべてのノートを解析し、キャッシュは持たない(revision はプロジェクトを開き直す・名前変更で 0 に戻るため、再利用の鍵にしない)。

## 感情の花

`EmotionLexicon`(`Emotion/`)を `NoteParser.Parse(input, lexicon)` / `ProjectAnalyzer.Analyze(inputs, lexicon)` に渡すと、花が作られる。**lexicon が null だと花は咲かない**ので、呼び出し側は `EmotionLexicon.Default`(初版・下書き)などを明示して渡す。

- 対象: 本文の typed の文字だけ(語のすべての文字が typed のとき)。見出し行とコードブロックの中は対象外
- 最長一致、語は 2 文字以上。い形容詞は語幹で登録(例: `嬉し`)。CSV は 1 行に `語,分類`(喜 / 怒 / 哀 / 楽)。`#` 始まりと空行は無視
- 否定: 語の直後が「ない」「ません」「くない」などで始まるときは枯れた花(`Flower.Withered`)
- 1 語につき 1 輪。同じ文(。．！？!? で区切る。行が変われば別の文)の中の同じ語は 1 輪にまとめる
- 位置は行番号(`Flower.Line`)。見出しより前の語は最初の見出しの所属

## 共通の約束

| 項目 | 約束 |
| --- | --- |
| ノートID | `Cartograph.Core.NotePath`(`notes/` からの相対パス、拡張子なし、区切りは `/`) |
| 文字位置 | UTF-16 単位(C# の string と同じ) |
| 行 | 1 始まり |
| 入力 | `AnalysisInput` = ノートID + `LabeledText`(本文と来歴ラベル)+ `Revision`。履歴のないノートは本文全体を Unknown にして渡す |
| Node.Id | `ノートID#見出しパス`。同じ階層の同名見出しは `~2`、`~3`。見出しのないファイルは `ノートID#` |

## 型

- `AnalysisInput` … 解析への入力
- `Node` / `Edge` / `Fog` / `Mark` / `Flower` … 仕様書「データ構造」どおり。`Mark` は Edge を作れないリンク切れも表せるよう、拠点・行・リンク先の書かれ方を自分で持つ
- `NoteLink` … 宛先を解決する前のリンク
- `NoteAnalysis` … 1 ノートだけで決まる結果(再解析とキャッシュの単位)
- `AnalysisResult` … 地図全体の結果。マップ描画が読む。`Revisions` で古い結果を判別する

## 関連ノートの自動検出

`RelatedNoteDetector`(`Related/`)は、ノート同士の関連候補を返す(リンクは作らない)。入力は `RelatedNoteSource`(ID・題名・本文)の一覧。
`Detect` で全件、`DetectFor(noteId, ...)` で 1 ノートに関わる組だけを計算し、`Replace` で既存の候補へ反映する。
根拠は言及・固有名詞(別名つきノートへの共通の言及)・共通キーワード・感情の類似(補助)。重みとしきい値は `RelatedNoteOptions`。
確定済み・却下済みの組は `RelatedPair` で `excluded` に渡すと候補から外れる。別名の編集・名前変更のときは `Detect` で全件を計算し直す。
