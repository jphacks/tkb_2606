using System.Threading.Tasks;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    /// <summary>
    /// IEditorWorkspace に、入力の審査・階層一覧・プロジェクトを開く口を足した契約。
    /// 既存の EditorContract.cs は変えず、UI は対応済みの実装なら as で受けて使う。
    ///
    /// 画面側の流れ:
    ///   1. テキスト欄の値が変わったら、変更前の選択範囲と入力の証拠から ChangeCandidate を作る。
    ///   2. ReviewChange を呼ぶ。
    ///   3. result.Approved なら何もしない(Core 側が本文・来歴・履歴を更新済み)。
    ///   4. 拒否なら、本文を result.RevertText に、選択範囲を RevertSelStart/End に戻し、
    ///      result.Reason を 2 秒だけ表示する。
    /// コピー・切り取りでは、OS のクリップボードへ書いたあとに NotifyCopied を呼ぶ。
    /// </summary>
    public interface IReviewingEditorWorkspace : IEditorWorkspace
    {
        /// <summary>notes/ 以下のフォルダ階層。プロジェクトを開く前は null。</summary>
        FolderNode Tree { get; }

        /// <summary>プロジェクトフォルダ(絶対パス)を開く。今のノートは先に保存する。</summary>
        Task OpenProject(string absoluteFolderPath);

        ReviewResult ReviewChange(string documentId, ChangeCandidate candidate);

        void NotifyCopied(string documentId, int start, int length);
    }
}
