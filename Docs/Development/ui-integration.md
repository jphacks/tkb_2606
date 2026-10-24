# UI階層表示の接続方法

なべかなのdev/kanami側に、IEditorWorkspaceを変えずに利用できる任意のUIアダプターを追加した。

```csharp
public interface IProjectNavigationSource {
    string ProjectName { get; }
    IReadOnlyList<NoteTreeEntry> Notes { get; }
}
```

namespaceはCartograph.EditorUI。NoteTreeEntryはCartograph.EditorUI.Navigation。
NoteTreeEntry(documentId, relativePath, title)を作り、relativePathにはnotes/からの相対パスを渡す。
例：new NoteTreeEntry("stable-id", "大学用/授業/科目A/ノート.md", "ノート")。
Windowsの区切り文字は/へ正規化する。表示用モデルであり、実際のファイルアクセスのパス検証を代替しない。
各文書IDと相対パスは一意。空階層、親参照、絶対パス、ファイルとフォルダの衝突は拒否する。
空フォルダはこのAPIでは表現しない。実フォルダ一覧のAPIが確定したら追加対応する。

IEditorWorkspaceを実装するUIアダプターが、同時にこのインターフェースも実装すれば、view.Bind(adapter)で階層を表示できる。
保存基盤自体にUI名前空間への依存を追加する必要はない。保存基盤のAPIを受ける薄いアダプターをUI側に置ける。

- インターフェースがない場合は既存のフラット文書一覧を表示する。
- 一覧やタイトルやパスが変わったらIEditorWorkspace.Changedをメインスレッドから通知する。
- ノートを選ぶとUIはFlushSaveを待ってOpenDocument(documentId)を呼ぶ。
- 現在文書のIDに一致する項目を選択表示する。
- ノートのタイトル変更とファイル名変更は別の操作。デモではタイトル変更でパスを変更しない。
- プロジェクト切り替えAPI、フォルダ選択ダイアログ、読み書き、空フォルダの扱いは今回追加していない。
- 共通EditorContract.csは今回変更していない。りょうすけの作業中の契約を勝手に書き換えないための追加口であり、本番APIを決めるものではない。

新仕様に必要な、変更差分・src・typed/unknownラベルの接続は次の統合で行う。今回の変更で入力制限や来歴追跡が完成したとは扱わない。
