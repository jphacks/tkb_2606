# なべかな作業状況（2026-10-24）

## 現在の場所とブランチ
Unityプロジェクトのルートは C:\jphacks\tkb_2606。以前のCartowordsサブフォルダからリポジトリ直下へ移動済み。
今回の開始コミットは36d6148（Move Unity project to repository root）。開始時のworking treeはclean。
ユーザー指定でdev/kanamiを作成し、切り替えてから今回の変更を行った。

## これまでに作成済み
- UI Toolkitの文書一覧・タイトル・本文編集・Markdown書式ボタン・文字数・保存状態表示。
- IEditorWorkspaceによる保存基盤の差し替え口と、メモリのみのDemoWorkspace。
- 文書ごとの本文Undo/Redo、ショートカット、選択範囲復元。
- 1万字とIME入力を試せる独立したM0検証欄。
- ペースト拒否の2秒通知。現在は内部ペーストも拒否する暫定状態。
- Unity非依存のUndoモデルと6件の自動テスト。

これらは今回のブランチ作成前に既存mainへ登録されていた。今回のブランチで新しく作ったものとは区別する。

## 今回dev/kanamiで行った作業
- ファイル階層表示用のUnity非依存モデルを作成。相対パスからフォルダツリーを組み立てる。
- フォルダを開閉できるNoteTreeViewを追加。ノートをクリックすると既存のOpenDocumentへ接続する。
- 選択中ノートの印と、本文上部のパス表示を追加。
- デモに大学用/授業/科目A、大学用/授業/科目B、個人用の構造を追加。
- 異なるフォルダの同名ノートを文書IDで区別する。
- 追加の階層表示モデル用テスト6件とUnity metaファイルを追加。
- 保存基盤との接続方法を別資料に記録。

IEditorWorkspace/EditorContract.cs、本番保存、入力履歴、Packages、ProjectSettings、既存シーンは変更していない。
実際のプロジェクトフォルダを開く処理・ファイルの読み込みは未接続。デモは永続化されない。

## 今回の主な変更ファイル
- Assets/Cartograph/EditorUI/CartographEditorView.cs
- Assets/Cartograph/EditorUI/DemoWorkspace.cs
- Assets/Cartograph/EditorUI/NoteTreeView.cs
- Assets/Cartograph/EditorUI/ProjectNavigation.cs
- Assets/Cartograph/EditorUI/NavigationModel/NoteTree.cs
- Assets/Cartograph/EditorUI/NavigationModel/Cartograph.NavigationModel.asmdef
- Assets/Cartograph/EditorUI/Tests/Editor/NoteTreeTests.cs
- Assets/Cartograph/EditorUI/Tests/Editor/Cartograph.EditorUndo.Tests.asmdef
- 新規Unityアセットに対応する.meta
- Docs/Development/kanami-progress.md と ui-integration.md
- Assets/Cartograph/EditorUI/README.md（現在の場所と手順に更新）

## 検証結果
- Unity 6000.3.24f1の参照DLLと付属Roslynを使ったC#コンパイル：成功、警告なし。
- NUnitテストをUnity付属Monoの簡易ランナーで実行：12成功、0失敗。
- 追加の6件：3階層、別フォルダ同名ノート、フォルダ優先とWindows区切りの正規化、空一覧、不正な表示パスの拒否、ID/パスの重複とファイル・フォルダ衝突。
- Unity画面でのフォルダ開閉・クリック・見た目は未確認。Unity Test RunnerでのEditMode実行も今回未実施。前回は同じプロジェクトが開いているためCLI起動できなかった。
- データモデルのテスト成功だけでM1完了とはしない。M0のIME・入力遅延・入力制限の実機確認と、本番保存の接続が残る。

## あなたが今できる確認
1. Unity Hubから C:\jphacks\tkb_2606 を開く。使用バージョンは6000.3.24f1。
2. コンパイルが終わったら CARTOWORDS > Create Editor Demo Scene。
3. Assets/Cartograph/EditorUI/CartographEditorDemo.unityを単独で開いてPlay。
4. 左の大学用 → 授業 → 科目A/科目Bを開閉し、ノートを切り替える。
5. 選択印、本文上部のパス、タイトルと本文が一致することを確認。
6. ノートを編集してから別ノートへ切り替え、戻ってUndo/Redoを試す。
7. M0検証欄に1万字をセットし、入力・IME変換・スクロールを試す。
8. 期待と違う挙動があれば、操作順と実際の結果を共有する。
現状デモなので、Play停止で本文は消える。検証欄はノートへ保存されない。

## これからの担当作業
なべかな：M0の実機確認結果への修正、りょうすけのAPIに合わせたプロジェクト選択・階層データ接続、差分入力審査・内部コピー・IME由来の接続。M0の結果を確かめてからM1完了に進む。
りょうすけ：プロジェクト内notes/.mdと.cartograph/history/.jsonlの保存、差分履歴、来歴ラベル、保存失敗時の保持・復元。入力判定のAPI案を返す。
デザイナー：編集画面の余白・文字サイズ・フォルダの表示・エラーの表示を確認できる。

今回の作業にネットワーク接続・push・マージは含めない。
