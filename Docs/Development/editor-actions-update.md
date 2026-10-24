# 入力・ノート削除・プロジェクト終了の追加

- 主編集欄・分割編集欄のReviewedTextBindingでEnter/テンキーEnterを捕捉し、キャレットまたは選択範囲へLF改行を入力証拠付きで挿入。IME変換中のEnterは捕捉しません。
- ノート削除は画面内で確認してから実行。本文・履歴・スナップショットを.cartograph/trash/<一意ID>/へ移動します。復元ボタンは未実装です。途中の移動失敗時は移動済みファイルを戻す処理があります。
- ProjectWorkspace.TrashNote(NotePath)を追加。既存APIの引数・戻り値は変更していません。削除成功時に内部コピー情報をクリアします。
- ProjectEditorWorkspace.DeleteDocument(string) / CloseProject()を追加。保存失敗時は処理を止め、未保存内容を保持します。削除後は残るノートを開き、最後の1件なら未選択になります。
- マップの「選択」ボタンを削除。ノード選択・ダブルクリック操作は継続。
- プロジェクト入力欄は固定の短い幅へ変更。「閉じる」ボタンを追加。
- 書式は大見出し・見出し・小見出し・太字・斜体へ改名。書式は従来通りMarkdown記号を挿入します。

しょうたろうへ：今回の保存関連変更はCore/Workspace/ProjectWorkspace.csとEditorBridge/ProjectEditorWorkspace.csに上記メソッドを追加しています。解析の型・Revision仕様には変更ありません。ノート削除・プロジェクト終了でUIのMapContextをリセットします。別ブランチで同じファイルを変更している場合は統合時に確認してください。

確認：Unity6000.3.24f1参照DLLによるEditor・Playerコンパイル成功。Mono簡易ランナーでテスト122件成功（追加4件）。Unity Test Runner・画面上のIMEとポインター操作は未確認。