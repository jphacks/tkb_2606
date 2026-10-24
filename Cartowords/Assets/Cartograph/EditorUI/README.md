# Unity版Cartograph：分担確定・りょうすけへの実装依頼

2026-10-24。ユーザーが発案者しょうたろーに確認し、エディターも地図もUnityプロジェクト内で作る方針に確定。
ローカル確認先：C:\jphacks\tkb_2606\Cartowords
Unity：6000.3.24f1。Input Systemが有効。独自のエディター実装は確認時点でなし。

## 今回の分担
- なべかな：UI Toolkitの編集画面、文書一覧、タイトル・本文、ツールバー、文字数、保存状態・エラー、IME・コピー・カット・ペースト・ドロップのUIイベント接続、UIのUndo/Redo。
- りょうすけ：C#の文書管理、永続化、500msデバウンス自動保存、本文と編集履歴の一括保存、ペースト判定サービス、Markdown書き出し、保存エラー・容量上限の処理。
- りょうすけはUIやシーンを重複して作らない。なべかなは本番保存・履歴サービスを重複して作らない。
- 既存のシーン・Packages・ProjectSettingsは今回は変更しない。保存先ディレクトリはApplication.persistentDataPath配下。

## 重要：前の資料からの変更
Web技術・TypeScript・ブラウザ保存を前提にしない。Unity/C#で作る。
最初の統合では本文の正本をUTF-8のMarkdown文字列に固定する。DocumentRecord.contentの構造化JSON案は今回採用しない。markdownフィールドを使う。原文の改行やUnicodeを勝手に正規化しない。
第一段階はUnity標準TextFieldでMarkdownを編集し、書式ボタンが記号を挿入する構成。見たまま太字になるWord互換エディターが完成したという意味ではない。見た目の書式編集は次段階。将来の形式変更に備えてschemaVersion=1を保存する。

## なべかな側で追加したファイル
Assets/Cartograph/EditorUI/EditorContract.cs：両者の接続契約。
Assets/Cartograph/EditorUI/DemoWorkspace.cs：メモリのみのUIデモ。保存済みとは表示しない。
Assets/Cartograph/EditorUI/CartographEditorView.cs：UI Toolkitの画面。Bind(IEditorWorkspace)で本番実装に差し替える。
Assets/Cartograph/EditorUI/Editor/CartographSceneBuilder.cs：Unityメニューからデモシーンを作る補助。
コード一式を添付ZIPでも渡す。現時点でGitHubにはpushしていないため、リモートに存在すると仮定しないこと。

## 接続契約（この名前・型をそのまま使う）
namespace Cartograph.EditorUI

SaveState：Saved / Dirty / Saving / Error
ChangeOrigin：Typing / Format / Undo / Redo / InternalPaste / Title

DocumentRecordはSerializableなclass。フィールド：
- int schemaVersion = 1
- string id
- string title
- string markdown
- long revision
- string createdAt（UTC、ISO 8601）
- string updatedAt（UTC、ISO 8601）
- Copy()で独立したコピーを返す。

IEditorWorkspace：
```csharp
event Action Changed;
IReadOnlyList<DocumentRecord> Documents { get; }
DocumentRecord Current { get; }
SaveState State { get; }
string Error { get; }
bool IsPersistent { get; }
Task CreateDocument();
Task OpenDocument(string id);
void UpdateTitle(string title);
void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin);
Task FlushSave();
string ExportMarkdown();
```

- UIとの呼び出し・Changed通知はUnityメインスレッド。ファイル処理を別スレッドに逃がす場合も通知をメインスレッドに戻す。
- Documents/Currentは保存内部を変更できないコピーを返す。
- 起動処理で保存データをロードした後にBindする。初期文書がない場合は一つ作る。
- UpdateTitle/ApplyEditorChangeは同期的にメモリ内文書を更新してDirtyとし、自動保存を予約する。非同期にあとからCurrentだけを更新する作りにしない。
- documentIdがCurrentと一致しない編集は拒否。revisionは確定編集ごとに単調増加。
- FlushSave成功時点で、その時までの確定編集が本文と履歴の両方に永続化されている。失敗は例外とState=Error/Error文字列で返す。
- OpenDocument/CreateDocument自身も必要な保存を保証する。UI側でも切り替え前にFlushSaveを呼ぶ。
- 保存の順序と世代を管理して、古い保存が新しい編集を上書きしない。保存成功後にさらに編集があればSavedではなくDirtyを維持する。
- Changed通知で本文をエディターに毎回再設定しない。既存UIは文書ID変更時だけ本文を読み直し、カーソルやIMEを守る。
- IsPersistentは本番永続化を使っているか。保存成功状態の代用ではない。
- Errorは通常時空文字。例外にはユーザーに表示可能な説明を用意する。
- ExportMarkdownは現在本文の文字列を返す。ファイルへの書き出しは別サービスとし、失敗も返せる形で提示する。

## りょうすけのAIへの依頼（ここからそのまま渡せます）
上記を確定した初期統合仕様として、Unity/C#でIEditorWorkspaceを実装してください。

最初にリポジトリ・AGENTS.md・Unityバージョン・現在の変更状態を確認し、ユーザーの変更を上書きしないでください。添付のEditorContract.csを利用し、同じ名前の型を別途定義しないでください。契約ファイルがまだ手元になければ、添付ZIPから取得するか、相方から取得してから接続してください。

担当コードの配置先はAssets/Cartograph/DocumentCore/。namespaceはCartograph.DocumentCore。新しいUnityシーンや編集画面を作らず、UI側はIEditorWorkspaceを実装したクラスをBindできる形で提供してください。契約変更が必要なら変更理由と差分を返してください。

実装するもの：
1. IEditorWorkspace実装、起動ロード、文書新規作成・一覧・切り替え・タイトル/本文更新。
2. Application.persistentDataPath配下への永続化。本文・履歴が食い違わないコミット形式。単に二つのJSONを順番に上書きするだけにしない。途中終了からの復元、破損時に原本を上書きしない処理を含める。
3. 500msのデバウンス自動保存とFlushSave。終了時だけの保存に依存しない。保存失敗では未保存本文を保持し、状態と説明をUIへ通知する。
4. 編集イベント：schemaVersion、id、documentId、revision、recordedAt、origin、スナップショット。確定編集を収集し、デバウンス中の履歴を落とさない。文字数や時刻だけで本人性を証明したとは扱わない。明確な容量上限を設け、上限到達時は明示エラーにし、黙って古い履歴を消さない。
5. ClipboardPolicy：同一起動セッションでアプリが発行したトークンと内容を照合する判定ロジック。外部内容は拒否、独自MIMEだけでは許可しない。UI Toolkitへ接続できるC#公開APIと使い方を示す。UI側がOSクリップボード書き込み成功を通知した後にコピー登録し、カットでは書き込み成功後に削除する。内部ペーストは通常編集として履歴保存する。利用環境でトークンを保持できなければ安全に拒否し、制限を報告する。
6. Markdown書き出しサービス。Application.persistentDataPathのexports等の明示的な場所へUTF-8で書き出し、出力パスを返す形を候補にする。勝手な自動送信やクラウド接続は作らない。
7. 起動・Bindの具体例と関連テスト。

確認するケース：保存後再起動の一致、連続編集の順序、文書切り替え、失敗時の本文保持、本文と履歴のrevision一致、破損ファイル復元、容量上限、外部ペースト拒否・内部許可、日本語・改行の往復。保存基盤はUIに依存しないテストを中心にする。実行できない検証は未検証と明記する。

共有してほしいもの：変更ファイル一覧、IEditorWorkspace実装クラス名と生成方法、起動ロードとBindのコード例、ClipboardPolicyの公開API、書き出しAPI、テスト結果、未解決事項。コードを共有できるブランチ/コミットも伝えてください。既存プロジェクト全体やLibrary/Tempを誤ってcommitしないでください。

## 今のUIでできること・未完了
実装済み：文書一覧、新規文書、タイトル・本文入力、見出し1〜3/太字/斜体/リストのMarkdown記号挿入、文字数、保存状態表示、保存先差し替え口。
未完了：本番保存・履歴との統合、Undo/Redoの保証、内部ペースト許可、外部ドロップ制御、書き出しボタン、Wordのような見たまま書式編集。
現在のUIはキーボードとコマンド経由のPasteを拒否する暫定実装。入力経路すべての防御が検証済みという意味ではない。ClipboardPolicy接続後になべかながcopy/cut/paste/dropを仕上げる。
IMEはInput Systemのcomposition通知を使い、変換途中の送信を保留する構成。日本語IMEの実機確認は未実施。

## Unityで画面を開く手順
1. Unity HubでCartowordsを6000.3.24f1で開く。
2. コンパイル完了後、上部メニュー Cartograph > Create Editor Demo Scene。
3. Assets/Cartograph/EditorUI/CartographEditorDemo.unityを単独で開く。
4. Playを押す。Gameビューは横幅1000px以上を推奨。
5. 「デモ：保存されません」と表示されることを確認。停止するとデータは消える。
日本語フォントは端末内のYu Gothic/Meiryo等を探す。配布向けのフォント同梱・ライセンス確認は別途必要。

## 今回の検証とGitの状態
Unity付属Roslynと6000.3.24f1のUnity参照DLL、既存Input System DLLを使い、追加した4つのC#ファイルのコンパイル成功（警告なし）。Unity Player起動や画面・IME・クリップボードの動作確認は未実施。batch起動を試したがログが得られず、Unity上で成功したとは扱わない。
現状Cartowords全体がGit上で未追跡。既存の大量のファイルを勝手に追加・commitしていない。codex/editor-uiブランチ作成は.gitへの書き込みに失敗したため未作成。ファイルは既存mainチェックアウトへ新規追加したのみ。commit/pushは未実施。Library等を除外するGit管理の整備は既存作業と調整して行う。
