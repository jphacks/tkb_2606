using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cartograph.Core;
using Cartograph.EditorUI.Navigation;

namespace Cartograph.EditorUI
{
    public interface IReviewedNoteAccess
    {
        Task<DocumentRecord> LoadDocument(string id);
        Task RenameDocument(string id, string title);
        LabeledText GetLabeledDocument(string id);
        ReviewResult ReviewDocument(string id, long expectedRevision, ChangeCandidate candidate);
    }

    /// <summary>UI adapter with shared, reviewed sessions for both editor panes.</summary>
    public sealed class ProjectEditorWorkspace : IReviewingEditorWorkspace, IProjectNavigationSource,
        ISecondaryNoteEditor, IReviewedNoteAccess
    {
        private sealed class Entry
        {
            public NoteSession Session;
            public long Revision;
            public string Created = DateTime.UtcNow.ToString("O");
            public string Updated = DateTime.UtcNow.ToString("O");
        }
        private readonly Dictionary<string, Entry> open = new Dictionary<string, Entry>();
        private readonly SynchronizationContext context = SynchronizationContext.Current;
        private readonly NoteSessionOptions options;
        private readonly IFileSystem fileSystem;
        private ProjectWorkspace project;
        private string currentId;
        public ProjectEditorWorkspace(NoteSessionOptions options = null, IFileSystem fileSystem = null)
        { this.options = options ?? new NoteSessionOptions(); this.fileSystem = fileSystem; }
        public event Action Changed;
        public bool IsPersistent => true;
        public string ProjectRoot => project?.Root;
        public string ProjectName => project == null ? "プロジェクトを開いてください" : Path.GetFileName(project.Root);
        public FolderNode Tree => project?.Tree;
        public IReadOnlyList<NoteTreeEntry> Notes => project == null ? Array.Empty<NoteTreeEntry>() :
            project.AllNotes().Select(p => new NoteTreeEntry(p.Value, p.Value + ".md", p.Name)).ToArray();
        public IReadOnlyList<DocumentRecord> Documents => project == null ? Array.Empty<DocumentRecord>() :
            project.AllNotes().Select(p => open.TryGetValue(p.Value, out var entry) ? Record(entry) :
                new DocumentRecord { id = p.Value, title = p.Name, markdown = "" }).ToArray();
        public DocumentRecord Current => currentId != null && open.TryGetValue(currentId, out var e) ? Record(e) : null;
        public SaveState State => open.Values.Any(e => e.Session.State == NoteSaveState.Error) ? SaveState.Error :
            open.Values.Any(e => e.Session.State == NoteSaveState.Saving) ? SaveState.Saving :
            open.Values.Any(e => e.Session.State == NoteSaveState.Dirty) ? SaveState.Dirty : SaveState.Saved;
        public string Error => open.Values.FirstOrDefault(e => e.Session.State == NoteSaveState.Error)?.Session.Error ?? "";

        private void Notify()
        {
            // Post avoids re-entering the editor while it is submitting an edit.
            if (context != null) context.Post(_ => Changed?.Invoke(), null);
            else Changed?.Invoke();
        }
        private static DocumentRecord Record(Entry e) => new DocumentRecord
        { id = e.Session.Path.Value, title = e.Session.Path.Name, markdown = e.Session.Text,
            revision = e.Revision, createdAt = e.Created, updatedAt = e.Updated };
        private Entry Require(string id)
        {
            if (id == null || !open.TryGetValue(id, out var e)) throw new InvalidOperationException("ノートが開かれていません。");
            return e;
        }
        private void Attach(NoteSession session)
        {
            open[session.Path.Value] = new Entry { Session = session };
            session.StateChanged += (_, __) => Notify();
        }

        public async Task OpenProject(string absoluteFolderPath)
        {
            await FlushSave();
            var next = ProjectWorkspace.Open(absoluteFolderPath, fileSystem, options);
            if (project != null)
                foreach (var id in open.Keys.ToArray()) await project.CloseNote(NotePath.Parse(id));
            project = next; open.Clear(); currentId = null;
            var first = project.AllNotes().FirstOrDefault();
            if (first.IsValid) await OpenDocument(first.Value);
            Notify();
        }
        public async Task<DocumentRecord> LoadDocument(string id)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            if (!open.ContainsKey(id))
            {
                var source = project;
                var session = await source.OpenNote(NotePath.Parse(id));
                if (!ReferenceEquals(source, project)) throw new OperationCanceledException("プロジェクトが変わりました。");
                if (!open.ContainsKey(id)) Attach(session);
            }
            return Record(Require(id));
        }
        public async Task OpenDocument(string id)
        {
            await FlushSave(); await LoadDocument(id); currentId = id; Notify();
        }
        public async Task CreateDocument()
        {
            if (project == null) throw new InvalidOperationException("先にプロジェクトを開いてください。");
            await FlushSave();
            var taken = new HashSet<string>(project.AllNotes().Select(p => p.Value), StringComparer.OrdinalIgnoreCase);
            string folder = currentId == null ? "" : NotePath.Parse(currentId).FolderPath;
            string prefix = folder.Length == 0 ? "" : folder + "/";
            string id = prefix + "無題のノート"; int number = 2;
            while (taken.Contains(id)) id = prefix + "無題のノート " + number++;
            Attach(await project.CreateNote(NotePath.Parse(id))); currentId = id; Notify();
        }
        public async Task RenameDocument(string id, string title)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            title = (title ?? "").Trim();
            if (!NotePath.IsValidName(title)) throw new ArgumentException("ノート名に使えない文字が含まれています。");
            var path = NotePath.Parse(id);
            if (path.Name == title) return;
            await FlushSave();
            var renamed = await project.RenameNote(path, title);
            open.Remove(id);
            Attach(await project.OpenNote(renamed));
            if (currentId == id) currentId = renamed.Value;
            Notify();
        }
        public void UpdateTitle(string title)
        { throw new InvalidOperationException("名前の変更はRenameDocumentを完了まで待ってください。"); }

        public LabeledText GetLabeledDocument(string id) => Require(id).Session.CloneDocument();
        public ReviewResult ReviewDocument(string id, long expectedRevision, ChangeCandidate candidate)
        {
            var e = Require(id);
            if (e.Revision != expectedRevision) throw new InvalidOperationException("別のペインで更新されています。最新の本文を確認してください。");
            var result = e.Session.Apply(candidate);
            if (result.Approved && result.Ops.Any(o => o.Type != EventType.Blocked))
            { e.Revision++; e.Updated = DateTime.UtcNow.ToString("O"); }
            Notify(); return result;
        }
        public ReviewResult ReviewChange(string documentId, ChangeCandidate candidate)
        { return ReviewDocument(documentId, Require(documentId).Revision, candidate); }
        public void NotifyCopied(string documentId, int start, int length) => Require(documentId).Session.NotifyCopied(start, length);
        public void ApplyEditorChange(string documentId, string markdown, ChangeOrigin origin)
        {
            // Never accept unreviewed typing in the production UI.
            if (origin != ChangeOrigin.Format) throw new InvalidOperationException("入力の証拠をReviewChangeへ渡してください。");
            ReviewChange(documentId, new ChangeCandidate { NewText = markdown, Evidence = new InputEvidence { Kind = EvidenceKind.Format } });
        }
        public DocumentRecord ApplySecondaryEdit(string id, long expectedRevision, string markdown, ChangeOrigin origin)
        { throw new InvalidOperationException("右ペインの編集にもReviewDocumentを使用してください。"); }
        public async Task DeleteDocument(string id)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            await FlushSave();
            try { await project.TrashNote(NotePath.Parse(id)); }
            catch
            {
                open.Remove(id);
                await LoadDocument(id);
                throw;
            }
            open.Remove(id);
            if (currentId == id)
            {
                currentId = null;
                var first = project.AllNotes().FirstOrDefault();
                if (first.IsValid) await OpenDocument(first.Value);
            }
            Notify();
        }
        public async Task CloseProject()
        {
            await FlushSave();
            if (project != null)
                foreach (var id in open.Keys.ToArray()) await project.CloseNote(NotePath.Parse(id));
            project = null; currentId = null; open.Clear(); Notify();
        }
        public async Task FlushSave()
        {
            if (project != null) await project.FlushAll();
        }
        public string ExportMarkdown() => Current?.markdown ?? "";
    }
}
