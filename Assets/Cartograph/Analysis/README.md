# Cartograph.Analysis

Unity 非依存の Markdown 解析(`noEngineReferences`)。`Cartograph.Core` だけを参照する。
`Model/` に型、`Parsing/` に解析本体。いまあるのは 1 ノートの解析 `NoteParser.Parse`(拠点・親子の道・本文量・リンク・霧・理由なしの目印)。
リンクの解決、孤島、リンク切れ、感情の花はこれから追加する。

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
