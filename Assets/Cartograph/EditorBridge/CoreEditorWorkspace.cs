using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cartograph.Core;

namespace Cartograph.EditorUI
{
    /// <summary>
    /// Cartograph.Core のプロジェクト(notes/ の .md と .cartograph/history/ の履歴)を、
    /// 既存の IEditorWorkspace として見せるアダプター。
    ///
    /// 既存の ApplyEditorChange は入力の証拠を渡せず拒否も返せないので、確認なしで受け入れ、
    /// 来歴は unknown にする(本文量には数えられない)。Undo/Redo/Format は証拠つきで記録する。
    /// 入力の審査と typed の来歴が必要な経路は ReviewChange を使う。
    /// Documents の markdown は、開いているノートだけ中身が入る(一覧のためにすべてを読み込まない)。
    /// </summary>
    public sealed class CoreEditorWorkspace : IReviewingEditorWorkspace
    {
        private const string DefaultTitle = "無題のノート";

        private readonly NoteSessionOptions options;
        private ProjectWorkspace project;
        private NoteSession session;
        private long revision;
        private string createdAt = "";
        private string updatedAt = "";
        private string errorOverride = "";
        private readonly Dictionary<string, string> idAliases = new Dictionary<string, string>();

        public event Action Changed;

        public CoreEditorWorkspace(NoteSessionOptions options = null)
        {
            this.options = options ?? new NoteSessionOptions();
        }

        public bool IsPersistent => true;
        public FolderNode Tree => project?.Tree;

        public IReadOnlyList<DocumentRecord> Documents
        {
            get
            {
                if (project == null) return Array.Empty<DocumentRecord>();
                return project.AllNotes().Select(p => p.Value == CurrentId ? Current : new DocumentRecord
                {
                    id = p.Value,
                    title = p.Name,
                    markdown = "",
                }).ToArray();
            }
        }

        public DocumentRecord Current
        {
            get
            {
                if (session == null) return null;
                return new DocumentRecord
                {
                    id = session.Path.Value,
                    title = session.Path.Name,
                    markdown = session.Text,
                    revision = revision,
                    createdAt = createdAt,
                    updatedAt = updatedAt,
                };
            }
        }

        public SaveState State
        {
            get
            {
                if (errorOverride.Length > 0) return SaveState.Error;
                if (session == null) return SaveState.Saved;
                switch (session.State)
                {
                    case NoteSaveState.Dirty: return SaveState.Dirty;
                    case NoteSaveState.Saving: return SaveState.Saving;
                    case NoteSaveState.Error: return SaveState.Error;
                    default: return SaveState.Saved;
                }
            }
        }

        public string Error => errorOverride.Length > 0 ? errorOverride : (session?.Error ?? "");

        private string CurrentId => session?.Path.Value;

        public async Task OpenProject(string absoluteFolderPath)
        {
            await CloseCurrent();
            project = ProjectWorkspace.Open(absoluteFolderPath, null, options);
            project.TreeChanged += RaiseChanged;
            var first = project.AllNotes().OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Cast<NotePath?>().FirstOrDefault();
            if (first.HasValue) await Activate(await project.OpenNote(first.Value));
            else await CreateDocument();
        }

        public async Task CreateDocument()
        {
            RequireProject();
            await CloseCurrent();
            var path = NotePath.Parse(UniqueName());
            await Activate(await project.CreateNote(path));
        }

        public async Task OpenDocument(string id)
        {
            RequireProject();
            if (!NotePath.TryParse(id, out var path)) throw new ArgumentException("ノートの場所が不正です: " + id, nameof(id));
            if (session != null && session.Path == path) return;
            await CloseCurrent();
            await Activate(await project.OpenNote(path));
        }

        public void UpdateTitle(string title)
        {
            if (session == null) return;
            title = (title ?? "").Trim();
            if (title == session.Path.Name) return;
            if (!NotePath.IsValidName(title))
            {
                SetError("このタイトルはノート名に使えません。");
                return;
            }
            _ = RenameCurrent(title);
        }

        public void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin)
        {
            RequireCurrent(documentId);
            if (origin == ChangeOrigin.Title && markdown == session.Text) return;

            var kind = EvidenceKind.Unreviewed;
            if (origin == ChangeOrigin.Undo) kind = EvidenceKind.Undo;
            else if (origin == ChangeOrigin.Redo) kind = EvidenceKind.Redo;
            else if (origin == ChangeOrigin.Format) kind = EvidenceKind.Format;

            Review(new ChangeCandidate { NewText = markdown, Evidence = new InputEvidence { Kind = kind } });
        }

        public ReviewResult ReviewChange(string documentId, ChangeCandidate candidate)
        {
            RequireCurrent(documentId);
            return Review(candidate);
        }

        public void NotifyCopied(string documentId, int start, int length)
        {
            RequireCurrent(documentId);
            session.NotifyCopied(start, length);
        }

        public async Task FlushSave()
        {
            if (session == null) return;
            await session.Flush();
            if (session.State == NoteSaveState.Error) throw new IOException(session.Error);
        }

        public string ExportMarkdown() => session?.Text ?? "";

        private ReviewResult Review(ChangeCandidate candidate)
        {
            var result = session.Apply(candidate);
            if (result.Approved && result.Ops.Any(o => o.Type != EventType.Blocked))
            {
                revision++;
                updatedAt = DateTime.UtcNow.ToString("O");
            }
            RaiseChanged();
            return result;
        }

        private async Task Activate(NoteSession s, bool keepRevision = false)
        {
            session = s;
            if (!keepRevision) revision = 0;
            createdAt = updatedAt = DateTime.UtcNow.ToString("O");
            errorOverride = "";
            session.StateChanged += OnSessionStateChanged;
            RaiseChanged();
            await Task.CompletedTask;
        }

        private async Task CloseCurrent()
        {
            if (session == null) return;
            var old = session;
            await project.CloseNote(old.Path);
            old.StateChanged -= OnSessionStateChanged;
            session = null;
        }

        private async Task RenameCurrent(string newTitle)
        {
            string oldId = CurrentId;
            try
            {
                var renamed = await project.RenameNote(session.Path, newTitle);
                idAliases[oldId] = renamed.Value;
                await Activate(await project.OpenNote(renamed), keepRevision: true);
            }
            catch (Exception ex)
            {
                SetError("タイトルを変更できませんでした: " + ex.Message);
                if (session == null || session.IsClosed)
                    await Activate(await project.OpenNote(NotePath.Parse(oldId)));
            }
        }

        private void OnSessionStateChanged(NoteSaveState state, string error) => RaiseChanged();

        private void SetError(string message)
        {
            errorOverride = message;
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();

        private void RequireProject()
        {
            if (project == null) throw new InvalidOperationException("プロジェクトが開かれていません。OpenProject を先に呼んでください。");
        }

        // 名前変更の直後など、UI がまだ古い ID を持っている間は、新しい ID と同じ文書として扱う。
        private void RequireCurrent(string documentId)
        {
            if (session == null) throw new InvalidOperationException("文書が開かれていません。");
            if (documentId == CurrentId) return;
            if (documentId != null && idAliases.TryGetValue(documentId, out var now) && now == CurrentId) return;
            throw new InvalidOperationException("文書が切り替わっています。");
        }

        private string UniqueName()
        {
            var taken = new HashSet<string>(project.AllNotes().Select(p => p.Value), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(DefaultTitle)) return DefaultTitle;
            for (int i = 2; ; i++)
            {
                string name = DefaultTitle + " " + i;
                if (!taken.Contains(name)) return name;
            }
        }
    }
}
