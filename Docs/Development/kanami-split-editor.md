# なべかな作業記録：分割エディターUIの準備

2026-10-24 / dev/kanami / 作業開始時のコミットa0fe05e、working treeはclean。
最新版Ver.3.1のF10に備え、マップや保存基盤に依存しない右ペインを実装した。

## 今回行った作業
- 「このノートを右に開く」ボタンと、閉じられる右の本文ペインを追加。
- 左の現在ノートを切り替えず、右ペインにノートを表示できる。
- マップから呼ぶOpenNoteBeside(documentId, headingLine)を追加。行番号は1始まり、内部のカーソル位置はUTF-16。
- 改行LF/CRLF/CR、日本語・絵文字を考慮した行番号→文字位置変換を追加。
- 右ペインの本文編集とUndo/Redoをデモに接続。右のタイトルは表示のみ。
- デモに文書IDと期待revisionを指定する更新口を追加。右の古い編集で新しい内容を上書きしない。
- 同じノートを左右で開いて編集した場合、正本の本文を反映。片方からの外部更新を受けた際は、受け手のUndo履歴をリセットし、古い本文へ巻き戻さない。
- 本番側が右ペインの更新口を持たなければ閲覧専用にする。
- 右ペインにも暫定のペースト拒否と、保存エラー表示から独立した2秒通知を追加。

## ファイル
更新：Assets/Cartograph/EditorUI/CartographEditorView.cs
更新：Assets/Cartograph/EditorUI/DemoWorkspace.cs
追加：Assets/Cartograph/EditorUI/SecondaryNotePane.cs
追加：Assets/Cartograph/EditorUI/SecondaryNoteContract.cs
追加：Assets/Cartograph/EditorUI/NavigationModel/TextLinePosition.cs
追加：Assets/Cartograph/EditorUI/Tests/Editor/TextLinePositionTests.cs
追加：対応.metaと、本資料。
EditorContract.cs、本番保存・履歴・解析、Packages、ProjectSettings、シーンは変更していない。

## 検証結果
- Unity 6000.3.24f1付属RoslynとUnity参照DLLでコンパイル成功、警告なし。
- NUnit形式のモデルテストをUnity付属Monoの簡易ランナーで実行：16成功、0失敗（既存12＋行位置4）。
- デモの実クラスを使う追加のスタンドアロンチェック5件：成功。
  1. 右の別ノートを編集しても左のノートID・本文を保持。
  2. 右の対象ノートへ日本語・絵文字を保存。
  3. 古いrevisionによる更新を拒否し、最新本文を保持。
  4. 同じノートを更新するとCurrentにも正本の本文が反映。
  5. 返却したコピーを書き換えても保存内部を変更しない。
- Unity画面での左右の操作・実際のスクロール・IME・入力制限の確認は未実施。
- Unity Test RunnerでのEditMode実行は今回未実施。モデルテストとスタンドアロン確認を、実機確認済みとは扱わない。

## 今試せること
1. UnityでC:\jphacks\tkb_2606を開き、コンパイル完了を待つ。
2. CARTOWORDS > Create Editor Demo Scene で作ったシーンを単独で開いてPlay。
3. 画面幅は1400px程度を推奨。「このノートを右に開く」を押す。
4. 左のツリーで別ノートを選ぶ。右は前のノートを残し、左だけ切り替わることを確認。
5. 右の本文を編集し、左でそのノートを開き直して変更が見えることを確認。
6. 同じノートを左右で開き、文字入力が相手に反映されることを確認。相手からの更新後は受け手のUndoがリセットされる暫定仕様。
7. 右のUndo/Redo、閉じる、再度開く、日本語変換を確認。
8. マップ実装時はダブルクリックからOpenNoteBeside(id, line)を呼ぶ。現在はマップもダブルクリック接続も未実装。

デモはメモリのみ。Play停止で消える。分割を閉じて開き直すと右の一時Undo履歴もリセットする。

## しょうたろーへの接続メモ
既存IEditorWorkspaceは変更していない。追加したISecondaryNoteEditorはUIアダプター用の任意接口。
```csharp
DocumentRecord ApplySecondaryEdit(string documentId, long expectedRevision,
    string markdown, ChangeOrigin origin);
```
同じUIアダプターがIEditorWorkspaceとISecondaryNoteEditorを実装すると右ペインを編集可能にする。実装がなければ右は閲覧専用。
本番保存基盤がこの型に直接依存する必要はなく、文書IDとrevisionを受ける本番APIをUI側で変換すればよい。
更新は同期的に承認・メモリ反映し、更新後レコードのコピーを返す。永続化は既存の自動保存・Flushに統合する。
現状Documentsが本文を含む前提で右を開いている。本番APIが一覧をメタデータだけにする場合は、OpenNoteBeside内に文書IDによる非同期ロードを接続する。
新しい差分入力判定・来歴のAPIはまだ未接続。右ペインでもそれを迂回して本番へ保存しないよう、統合時に承認処理を通す。

## 次の作業
- 本人の画面操作で左右の入力・IME・見出し位置への移動を確認し、不具合を修正。
- しょうたろーから保存・入力審査APIを受け取り、左右両方を本番へ接続。
- 解析データ契約が決まったらマップ描画と単クリック選択/ダブルクリック起動を実装。
本変更だけではF10やM4の受け入れ完了とはしない。commit後もpush・マージは行わない。
