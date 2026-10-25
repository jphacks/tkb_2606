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
        ISecondaryNoteEditor, IReviewedNoteAccess, IProjectedCopyAccess
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
        // デモプロジェクトを開いている間だけ入る、メモリ上のファイルシステム。入っている間は、ディスクには何も書かない。
        private IFileSystem demoFs;
        private DateTime demoStamp;
        private IFileSystem Fs => demoFs ?? fileSystem ?? new DiskFileSystem();
        public bool IsDemo => demoFs != null;
        private ProjectWorkspace project;
        private string currentId;
        private string bookTitle;
        public string BookTitle => project == null ? "" : bookTitle;
        public void UpdateBookTitle(string value)
        {
            if (project == null) throw new InvalidOperationException("先にプロジェクトを開いてください。");
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Contains("\n") || value.Contains("\r"))
                throw new ArgumentException("題名を1行で入力してください。");
            Fs.WriteAllTextAtomic(Path.Combine(project.Root, ".cartograph", "book-title.txt"), value);
            bookTitle = value; Notify();
        }
        // 拠点の位置の保存(.cartograph/map-layout.txt)。壊れていても、なくても、空の配置として読む。
        private string MapLayoutPath => Path.Combine(project.Root, ".cartograph", "map-layout.txt");
        public IReadOnlyDictionary<string, Cartowords.MapPresentation.MapPoint> LoadMapLayout()
        {
            if (project == null) return new Dictionary<string, Cartowords.MapPresentation.MapPoint>();
            try { return Cartowords.MapPresentation.MapLayoutFile.Parse(Fs.ReadAllText(MapLayoutPath)); }
            catch (IOException) { return new Dictionary<string, Cartowords.MapPresentation.MapPoint>(); }
            catch (UnauthorizedAccessException) { return new Dictionary<string, Cartowords.MapPresentation.MapPoint>(); }
        }
        // 位置の保存は補助的なもの。書けなくても編集を止めないよう、失敗は false で返す。
        public bool SaveMapLayout(IReadOnlyDictionary<string, Cartowords.MapPresentation.MapPoint> positions)
        {
            if (project == null) return false;
            try { Fs.WriteAllTextAtomic(MapLayoutPath, Cartowords.MapPresentation.MapLayoutFile.Serialize(positions)); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        // 却下した関連ノートの候補(.cartograph/related-rejected.txt)。再提案しない。
        private string RejectedPath => Path.Combine(project.Root, ".cartograph", "related-rejected.txt");
        private HashSet<Cartograph.Analysis.RelatedPair> rejectedPairs; private string rejectedRoot;
        public IReadOnlyCollection<Cartograph.Analysis.RelatedPair> RejectedRelatedPairs()
        {
            if (project == null) return Array.Empty<Cartograph.Analysis.RelatedPair>();
            if (rejectedPairs == null || rejectedRoot != project.Root)
            {
                try { rejectedPairs = RelatedRejectFile.Parse(Fs.ReadAllText(RejectedPath)); }
                catch (IOException) { rejectedPairs = new HashSet<Cartograph.Analysis.RelatedPair>(); }
                catch (UnauthorizedAccessException) { rejectedPairs = new HashSet<Cartograph.Analysis.RelatedPair>(); }
                rejectedRoot = project.Root;
            }
            return rejectedPairs;
        }
        /// <summary>候補を却下する。書き込めなくてもこの起動の間は覚えておき、失敗は false で返す。</summary>
        public bool RejectRelated(string first, string second)
        {
            if (project == null) return false;
            RejectedRelatedPairs();
            rejectedPairs.Add(new Cartograph.Analysis.RelatedPair(first, second));
            try { Fs.WriteAllTextAtomic(RejectedPath, RelatedRejectFile.Serialize(rejectedPairs)); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        /// <summary>owner ノートの末尾に other への関連リンクを足す(地図の候補を承認するとき)。</summary>
        public async Task AppendRelatedLink(string owner, string other)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            await LoadDocument(owner); await LoadDocument(other);
            var record = Record(Require(owner));
            var edit = RelatedNoteLink.Insert(record.markdown, (record.markdown ?? "").Length, owner, other, project.AllNotes().Select(p => p.Value));
            if (edit.Text == record.markdown) return;
            int at = (record.markdown ?? "").Length;
            var result = ReviewDocument(owner, record.revision, new ChangeCandidate
            {
                NewText = edit.Text, SelStartBefore = at, SelEndBefore = at,
                Evidence = new InputEvidence { Kind = EvidenceKind.Format }
            });
            if (!result.Approved) throw new InvalidOperationException(result.Reason);
            await FlushSave();
        }
        public ProjectEditorWorkspace(NoteSessionOptions options = null, IFileSystem fileSystem = null)
        { this.options = options ?? new NoteSessionOptions { SaveDebounceMs = 200 }; this.fileSystem = fileSystem; }
        public event Action Changed;
        public bool IsPersistent => !IsDemo;
        // デモの仮想ルートはディスク上に無いので、外には出さない(位置の保存や、最近開いた場所の記録の対象にならない)。
        public string ProjectRoot => IsDemo ? null : project?.Root;
        public string ProjectName => project == null ? "プロジェクトを開いてください" : IsDemo ? DemoProject.Name : Path.GetFileName(project.Root);
        public FolderNode Tree => project?.Tree;
        public IReadOnlyList<NoteTreeEntry> Notes
        {
            get
            {
                if (project == null) return Array.Empty<NoteTreeEntry>();
                bool stale = unchecked(Environment.TickCount - timesStamp) > 1000 || timesProject != project.Root;
                if (stale) { fileTimes.Clear(); timesStamp = Environment.TickCount; timesProject = project.Root; }
                return project.AllNotes().Select(p =>
                {
                    if (!fileTimes.TryGetValue(p.Value, out var times)) fileTimes[p.Value] = times = ReadFileTimes(p.Value);
                    return new NoteTreeEntry(p.Value, p.Value + ".md", p.Name, times.Item1, times.Item2);
                }).ToArray();
            }
        }
        // 索引の並べ替え用。毎回ディスクに触れないよう1秒間キャッシュし、読めなければ「不明」とする。
        private readonly Dictionary<string, Tuple<DateTime, DateTime>> fileTimes = new Dictionary<string, Tuple<DateTime, DateTime>>();
        private int timesStamp; private string timesProject;
        private Tuple<DateTime, DateTime> ReadFileTimes(string id)
        {
            if (IsDemo) return Tuple.Create(demoStamp, demoStamp);
            try
            {
                var file = Path.Combine(project.Root, "notes", id.Replace('/', Path.DirectorySeparatorChar) + ".md");
                if (!File.Exists(file)) return Tuple.Create(DateTime.MinValue, DateTime.MinValue);
                return Tuple.Create(File.GetCreationTimeUtc(file), File.GetLastWriteTimeUtc(file));
            }
            catch (Exception) { return Tuple.Create(DateTime.MinValue, DateTime.MinValue); }
        }
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
            // デモを開いていても、実際のプロジェクトはディスク(または注入された fileSystem)から読む。
            var real = fileSystem ?? new DiskFileSystem();
            absoluteFolderPath = ProjectRootResolver.Resolve(absoluteFolderPath, real);
            var next = ProjectWorkspace.Open(absoluteFolderPath, fileSystem, options);
            string nextTitle = real.ReadAllText(Path.Combine(next.Root, ".cartograph", "book-title.txt"));
            if (project != null)
                foreach (var id in open.Keys.ToArray()) await project.CloseNote(NotePath.Parse(id));
            project = next; demoFs = null; rejectedPairs = null; open.Clear(); currentId = null;
            bookTitle = string.IsNullOrWhiteSpace(nextTitle) ? Path.GetFileName(next.Root) : nextTitle.Trim();
            var first = project.AllNotes().FirstOrDefault();
            if (first.IsValid) await OpenDocument(first.Value);
            Notify();
        }
        /// <summary>
        /// メモリ上だけのデモプロジェクトを開く。ふつうのプロジェクトと同じ ProjectWorkspace で動くが、
        /// ファイルは InMemoryFileSystem の中にだけあり、ディスクには何も書かない。閉じると消える。
        /// </summary>
        public async Task OpenDemo()
        {
            await FlushSave();
            var memory = DemoProject.CreateFileSystem();
            var next = ProjectWorkspace.Open(DemoProject.Root, memory, options);
            if (project != null)
                foreach (var id in open.Keys.ToArray()) await project.CloseNote(NotePath.Parse(id));
            project = next; demoFs = memory; demoStamp = DateTime.UtcNow; rejectedPairs = null; open.Clear(); currentId = null;
            fileTimes.Clear();
            bookTitle = DemoProject.Name;
            await OpenDocument(DemoProject.FirstNote);
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
        public async Task OpenFolder(string folder)
        {
            if (project == null) throw new InvalidOperationException("保存先を開いてください。");
            await FlushSave();
            var first = project.AllNotes().FirstOrDefault(p => p.FolderPath == (folder ?? ""));
            if (first.IsValid) await OpenDocument(first.Value);
            else { currentId = null; Notify(); }
        }
        public async Task CreateDocument()
        { await CreateDocumentInFolder(currentId == null ? "" : NotePath.Parse(currentId).FolderPath); }
        public async Task CreateDocumentInFolder(string folder)
        {
            if (project == null) throw new InvalidOperationException("先にプロジェクトを開いてください。");
            await FlushSave();
            var taken = new HashSet<string>(project.AllNotes().Select(p => p.Value), StringComparer.OrdinalIgnoreCase);
            folder = folder ?? "";
            if (folder.Length > 0) folder = NotePath.Parse(folder + "/placeholder").FolderPath;
            string prefix = folder.Length == 0 ? "" : folder + "/";
            string id = prefix + "無題のノート"; int number = 2;
            while (taken.Contains(id)) id = prefix + "無題のノート " + number++;
            Attach(await project.CreateNote(NotePath.Parse(id))); currentId = id; Notify();
        }
        public void CreateFolder(string parent, string name)
        {
            if (project == null) throw new InvalidOperationException("先に保存先を開いてください。");
            project.CreateFolder(parent, name); Notify();
        }
        public async Task RenameDocument(string id, string title)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            title = (title ?? "").Trim();
            if (!NotePath.IsValidName(title)) throw new ArgumentException("ノート名に使えない文字が含まれています。");
            var path = NotePath.Parse(id);
            if (path.Name == title) return;
            await RelocateDocument(id, path.WithName(title), () => project.RenameNote(path, title));
        }

        /// <summary>ノートを別のフォルダへ移し、他のノートからのリンクを書き換える。folder が空ならルート。</summary>
        public async Task MoveDocument(string id, string folder)
        {
            if (project == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            var path = NotePath.Parse(id);
            var nextPath = path.WithFolder(folder);
            if (nextPath == path) return;
            await RelocateDocument(id, nextPath, () => project.MoveNote(path, folder));
        }

        // 名前変更と移動の共通処理。リンクの書き換え範囲を先に求め、移動のあとで適用する。
        private async Task RelocateDocument(string id, NotePath nextPath, Func<Task<NotePath>> relocate)
        {
            if (project.NoteExists(nextPath)) throw new IOException("同じ名前のノートがあります。");
            var ids = project.AllNotes().Select(p => p.Value).ToArray();
            foreach (var noteId in ids) await LoadDocument(noteId);
            var linkUpdates = new Dictionary<string, List<Tuple<int, int, string>>>();
            foreach (var noteId in ids)
            {
                string text = Record(Require(noteId)).markdown;
                var changes = new List<Tuple<int, int, string>>();
                foreach (var range in RelatedLinkRemoval.GetRanges(text, noteId, id, ids))
                {
                    string target = text.Substring(range.Start + 2, range.Length - 4).Trim();
                    if (target.StartsWith("#", StringComparison.Ordinal)) continue;
                    int suffix = target.IndexOfAny(new[] { '#', '|' });
                    string replacement = "[[" + nextPath.Value + (suffix < 0 ? "" : target.Substring(suffix)) + "]]";
                    changes.Add(Tuple.Create(range.Start, range.Length, replacement));
                }
                linkUpdates[noteId] = changes;
            }
            await FlushSave();
            var renamed = await relocate();
            open.Remove(id);
            Attach(await project.OpenNote(renamed));
            if (currentId == id) currentId = renamed.Value;
            foreach (var pair in linkUpdates)
            {
                string noteId = pair.Key == id ? renamed.Value : pair.Key;
                foreach (var change in pair.Value)
                {
                    var record = Record(Require(noteId));
                    var result = ReviewDocument(noteId, record.revision, new ChangeCandidate
                    {
                        NewText = record.markdown.Remove(change.Item1, change.Item2).Insert(change.Item1, change.Item3),
                        SelStartBefore = change.Item1, SelEndBefore = change.Item1 + change.Item2,
                        Evidence = new InputEvidence { Kind = EvidenceKind.Format }
                    });
                    if (!result.Approved) throw new InvalidOperationException(result.Reason);
                }
            }
            await FlushSave();
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
        public void NotifyProjectedCopied(string documentId, string visibleText, IReadOnlyList<int> sourceOffsets)
        {
            var original = GetLabeledDocument(documentId);
            if (visibleText == null || sourceOffsets == null || visibleText.Length != sourceOffsets.Count)
                throw new ArgumentException("コピー範囲が一致しません。");
            var projected = new LabeledText();
            for (int i = 0; i < sourceOffsets.Count; i++)
            {
                int at = sourceOffsets[i];
                if (at < 0 || at >= original.Length || original.Text[at] != visibleText[i] || (i > 0 && at <= sourceOffsets[i - 1]))
                    throw new ArgumentException("コピー位置が変更されています。");
                projected.Insert(projected.Length, visibleText.Substring(i, 1), original.Slice(at, 1));
            }
            project.Clipboard.Set(documentId, sourceOffsets.Count == 0 ? 0 : sourceOffsets[0], visibleText, projected.Labels);
        }
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
            project = null; demoFs = null; rejectedPairs = null; currentId = null; open.Clear(); Notify();
        }
        public async Task FlushSave()
        {
            if (project != null) await project.FlushAll();
        }
        public string ExportMarkdown() => Current?.markdown ?? "";
    }
}
