# Cartograph Editor UI

Unity 6000.3.24f1 / UI Toolkit。現在のUnityプロジェクトはリポジトリ直下。

## 起動
Cartograph > Create Editor Demo Scene でデモシーンを作成し、Assets/Cartograph/EditorUI/CartographEditorDemo.unityを単独で開いてPlay。
画面は横幅1000px以上を推奨。デモの本文はメモリのみで、Play停止で消える。

## 現在の機能
Markdown本文・タイトル編集、書式記号の挿入、文字数、保存状態、ノート別Undo/Redo、階層フォルダ表示、現在位置、独立したM0検証欄。
内部ペースト・差分判定・本番保存・履歴は未接続。Wordのように本文を見たまま装飾するエディターではなく、仕様書の素のMarkdownを扱う試作。

## 保存基盤との接続
IEditorWorkspaceをCartographEditorView.Bindに渡す。
階層表示の追加口はIProjectNavigationSource。詳細はDocs/Development/ui-integration.md。
新しい保存基盤はユーザーのプロジェクト内notes/と.cartograph/を扱う仕様。過去のpersistentDataPath固定という案は更新済み。

## 検証
EditMode用テストはTests/Editor。Undoモデルと階層モデルはUnity非依存の別アセンブリ。
最新の作業状況・実機確認手順はDocs/Development/kanami-progress.md。
