# 保存・入力審査・分割編集の統合（2026-10-24）

## 今回行ったこと
- dev/kanamiに最新main（5ddbb1e、PR #2マージ）を取り込んだ。既存のProjectSettings.assetの変更は保持。
- 通常起動をDemoWorkspaceから本番のProjectEditorWorkspaceへ切り替えた。
- プロジェクトフォルダの絶対パス入力と「開く」を追加。Unity Editorでは「フォルダを選択」も使用可能。Player版ではパス入力で開く。
- 実ノートの階層を既存ツリーへ接続。新規ノートは現在ノートの所属町に作成。
- タイトルによる実ファイル名変更はEnterまたはフォーカス離脱で確定。入力のたびの非同期リネームは行わない。
- 文書ID別の読込/審査/ラベル取得/名前変更を追加し、左右のノートをCoreの独立したセッションへ接続。
- キー/IME/内部ペースト/Undo/Redo/書式をReviewDocumentへ渡す経路を追加。通常本文入力をUnreviewed経路で保存しない。
- OSコピー成功後のNotifyCopied、内部バッファ照合、拒否時の本文と選択範囲の復元を接続。
- Undoスナップショットにラベルを保持し、再挿入時に元のtyped/unknownを復元する。
- 画面に未反映の編集がある時は保存済み表示を出さない。
- 保存失敗時はClose/切替/リネームを中断し、未保存本文と履歴を持つセッションを残す修正を加えた。
- 実行可能なスタンドアロンテストスクリプトと統合テストを追加。

## 変更した主なファイル
Assets/Cartograph/EditorBridge/ProjectEditorWorkspace.cs（新規：本番UIアダプター、複数文書API）
Assets/Cartograph/EditorBridge/CoreEditorWorkspace.cs（旧アダプターで失敗時の購読解除を防ぐ）
Assets/Cartograph/EditorUI/ReviewedTextBinding.cs（新規：入力証拠・審査・内部コピー・ラベル付きUndo）
Assets/Cartograph/EditorUI/CartographEditorView.cs（プロジェクト選択と接続）
Assets/Cartograph/EditorUI/SecondaryNotePane.cs（右ペインも審査へ）
Assets/Cartograph/EditorUI/UndoModel/TextUndoHistory.cs（任意メタデータ保持）
Assets/Cartograph/Core/Workspace/NoteSession.cs、ProjectWorkspace.cs（保存失敗時の保持）
Assets/Cartograph/Core/Tests/Editor/CloseFailureTests.cs
Assets/Cartograph/EditorUI/Tests/Editor/TextUndoHistoryTests.cs
Tests/Standalone/（統合テスト、現在のTest/TestCaseを実行する簡易ランナー、run-tests.ps1）

## 自動検証
- Unity 6000.3.24f1の参照DLLでEditor用C#コンパイル成功。
- UNITY_EDITORを外したPlayer用条件でもC#コンパイル成功。Playerの実ビルドを行った意味ではない。
- スタンドアロンのNUnit形式テスト：108件成功、0件失敗（Core/モデル/追加統合テスト）。
- 書込失敗時のClose/リネーム中止、未保存保持、再試行、typed復元、別ノート編集、内部コピー、blocked記録、古いrevision拒否を確認。
- 既存の実ディスク保存/再読込テストも成功。
- 最初は一時ディレクトリへの書込権限で1件失敗したため、テスト一時領域を許可済みの.results/tempへ移して再実行し、全件成功。
- これはUnity Test Runnerの実行やUI/IME実機確認の代わりではない。Unity本体のコンパイル/アセットインポート、画面の入力とクリップボード、Windows IME、1万字の入力遅延、見出しへの実スクロールは確認が残る。

## 再実行
プロジェクトルートのPowerShellで：
```powershell
./Tests/Standalone/run-tests.ps1
```
Unityの場所が異なる場合は-UnityEditorにEditorフォルダを渡す。UnityでNUnitパッケージを復元済みであること。
結果はTests/Standalone/.results/test-results.log。成果物はGit対象外。

## 手元で確認する手順
1. Play停止→Unityのコード更新完了を待つ→エディターシーンでPlay。
2. 上部の「フォルダを選択」で、次のサンプルのCARTOWORDS-sampleを選ぶ。
   C:\Users\piyoc\Documents\Codex\2026-10-24\u\outputs\CARTOWORDS-sample
   notesフォルダそのものではなく、その親を選ぶ。
3. 左の大学用→授業→科目Aからノートを選ぶ。本文を入力し、約0.5秒後に「保存済み」を確認。
4. 日本語IMEで変換・取消・候補変更・確定。途中の文字が消えたり、二重に入ったりしないか確認。
5. 自分で入力した部分をコピーし、同じノートと別ノートへ貼る。外部アプリの別の文章は拒否されるか確認。Ctrl+Vだけでなく右クリックも確認。
6. Undo/Redo、範囲選択して置換、書式ボタンを確認。書式記号は現Core仕様でunknown。
7. 「このノートを右に開く」後、左で別ノートを選び、左右それぞれを編集・保存。
8. タイトルを変更しEnter。notes配下の実ファイル名と履歴のパスが追随するか確認。
9. Play停止→再度Play→同じプロジェクトを開き、左右で編集した本文が復元されるか確認。
10. notes/*.mdと.cartograph/history/*.jsonlが作られていることを確認。サンプルを既存ファイルとして開いた部分はunknown、新たに入力した部分からtypedを記録する。
Play停止の直前に未保存の入力が残っていれば、先に「保存を再試行」を押して保存済みを確認する。

## 現時点の制限と次の作業
- M0/M1/M2の実機受け入れは未完。実機結果なしに完了扱いにしない。
- 実際のプロジェクト変更による空の町だけの表示は未対応（ノートのある町を表示）。
- プロジェクトを最後に開いた場所の記憶、Player版のネイティブフォルダ選択は未追加。
- 解析/マップ/霧/孤島/土地/目印/感情の花はまだ未実装。マップからのダブルクリックも未接続。
- 履歴書き出しUI、OS終了/強制終了時の保証、ビルド配布と性能確認は今後。
- 同じノートを別ペインから更新された場合、受け手のUndoはリセットする暫定仕様。
- 既存Coreの名前変更で複数ファイルの移動中に障害が起きる場合などの一括復旧は、別途仕上げで検証する。
- 今回はAPIとUI経路の統合までを実施。次は実機入力の確認/修正を優先し、その後Markdown解析→マップの順に進める。

push、PR作成、リモートへのマージは実施していない。
