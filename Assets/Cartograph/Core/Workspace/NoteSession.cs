using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cartograph.Core
{
    public enum NoteSaveState { Saved, Dirty, Saving, Error }

    public sealed class NoteSessionOptions
    {
        /// <summary>入力が止まってから自動保存するまでの時間。負数なら自動保存せず Flush だけ。</summary>
        public int SaveDebounceMs = 500;
        /// <summary>何イベントごとにスナップショットを取るか。</summary>
        public int SnapshotInterval = 1000;
        public Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 開いている 1 つのノートの状態。編集の審査、来歴の更新、履歴と本文の保存を受け持つ。
    /// UI からの呼び出しはメインスレッド。Apply は同期で、結果をすぐ返す。
    /// 保存は「履歴の追記 → 本文の置き換え」の順。途中で落ちても履歴が本文より先に進むだけで、
    /// 読み込み時に末尾を外して合わせられる。
    /// </summary>
    public sealed class NoteSession
    {
        private readonly NotePath path;
        private readonly ProjectPaths paths;
        private readonly IFileSystem fs;
        private readonly HistoryStore history;
        private readonly InternalClipboard clipboard;
        private readonly NoteSessionOptions options;

        private readonly object gate = new object();
        private readonly SemaphoreSlim flushLock = new SemaphoreSlim(1, 1);

        private readonly LabeledText doc;
        private int lastSeq;
        private List<EditEvent> pending = new List<EditEvent>();
        private Snapshot pendingBaseline;
        private Snapshot pendingPeriodic;
        private bool bodyDirty;
        private CancellationTokenSource debounce;
        private NoteSaveState state = NoteSaveState.Saved;
        private string error = "";
        private bool closed;

        public event Action<NoteSaveState, string> StateChanged;

        public NotePath Path => path;
        public HistoryStatus HistoryStatus { get; }
        public int CorruptHistoryLines { get; }

        private NoteSession(NotePath path, ProjectPaths paths, IFileSystem fs, InternalClipboard clipboard,
            NoteSessionOptions options, HistoryLoadResult loaded)
        {
            this.path = path;
            this.paths = paths;
            this.fs = fs;
            this.clipboard = clipboard;
            this.options = options;
            history = new HistoryStore(fs, paths);
            doc = loaded.Doc;
            lastSeq = loaded.LastSeq;
            pendingBaseline = loaded.PendingBaseline;
            HistoryStatus = loaded.Status;
            CorruptHistoryLines = loaded.CorruptLines;
        }

        public static async Task<NoteSession> OpenAsync(NotePath note, ProjectPaths paths, IFileSystem fs,
            InternalClipboard clipboard, NoteSessionOptions options = null)
        {
            options = options ?? new NoteSessionOptions();
            var loaded = await Task.Run(() =>
            {
                string body = fs.ReadAllText(paths.NoteFile(note)) ?? "";
                var r = new HistoryStore(fs, paths).Load(note, body);
                return r;
            });
            return new NoteSession(note, paths, fs, clipboard ?? new InternalClipboard(), options, loaded);
        }

        public string Text { get { lock (gate) return doc.Text; } }
        public int LastSeq { get { lock (gate) return lastSeq; } }
        public NoteSaveState State { get { lock (gate) return state; } }
        public string Error { get { lock (gate) return error; } }
        public bool IsClosed { get { lock (gate) return closed; } }

        /// <summary>本文と来歴ラベルの複製(解析側が本文量を数える用)。</summary>
        public LabeledText CloneDocument() { lock (gate) return doc.Clone(); }

        public int TypedCount() { lock (gate) return doc.TypedCount(); }

        /// <summary>変更を審査し、許可なら本文・来歴・履歴を更新する。拒否なら何も変えず、戻し方を返す。</summary>
        public ReviewResult Apply(ChangeCandidate candidate)
        {
            ReviewResult review;
            bool schedule;
            lock (gate)
            {
                if (closed) throw new InvalidOperationException("閉じたノートは編集できません。");
                review = InputReviewer.Review(doc.Text, path.Value, candidate, clipboard);
                bool textChanged = false;
                foreach (var op in review.Ops)
                {
                    var e = BuildEvent(op);
                    ProvenanceReplayer.Apply(doc, e);
                    pending.Add(e);
                    if (op.Type != EventType.Blocked) textChanged = true;
                    if (options.SnapshotInterval > 0 && e.Seq % options.SnapshotInterval == 0)
                        pendingPeriodic = Snapshot.Of(e.Seq, doc);
                }
                if (textChanged)
                {
                    bodyDirty = true;
                    state = NoteSaveState.Dirty;
                    error = "";
                }
                schedule = pending.Count > 0 || bodyDirty;
            }
            if (review.Ops.Count > 0) RaiseStateChanged();
            if (schedule) ScheduleSave();
            return review;
        }

        /// <summary>範囲を内部バッファに預ける(コピー・切り取り)。元のラベルも一緒に持つ。</summary>
        public void NotifyCopied(int start, int length)
        {
            lock (gate)
            {
                if (start < 0 || length < 0 || start > doc.Length || length > doc.Length - start)
                    throw new ArgumentOutOfRangeException(nameof(start));
                if (length == 0) return;
                clipboard.Set(path.Value, start, doc.Text.Substring(start, length), doc.Slice(start, length));
            }
        }

        public async Task Flush()
        {
            await flushLock.WaitAsync();
            try { await FlushCore(); }
            finally { flushLock.Release(); }
        }

        public async Task CloseAsync()
        {
            lock (gate) { debounce?.Cancel(); }
            await Flush();
            lock (gate) { closed = true; }
        }

        private EditEvent BuildEvent(PlannedOp op)
        {
            var e = new EditEvent
            {
                Seq = ++lastSeq,
                T = options.Clock(),
                Type = op.Type,
                Pos = op.Pos,
                Src = op.Src,
                From = op.From,
                SrcDoc = op.SrcDoc,
            };
            if (op.Type == EventType.Insert)
            {
                e.Text = op.Text;
                e.Len = op.Text.Length;
                var labels = op.Labels != null && op.Labels.Length == op.Text.Length
                    ? op.Labels
                    : LabeledSlice.Uniform(op.Text.Length, Origin.Unknown);
                e.Lbl = NeedsLabel(op.Src, labels) ? labels.Encode() : null;
            }
            else
            {
                e.Len = op.Len;
            }
            return e;
        }

        // key/ime の全 typed と、format の全 unknown は既定値なので lbl を省く。それ以外は必ず書く。
        private static bool NeedsLabel(string src, LabeledSlice labels)
        {
            if ((src == EventSource.Key || src == EventSource.Ime) && labels.IsUniform(Origin.Typed)) return false;
            if (src == EventSource.Format && labels.IsUniform(Origin.Unknown)) return false;
            return true;
        }

        private void ScheduleSave()
        {
            if (options.SaveDebounceMs < 0) return;
            CancellationTokenSource cts;
            lock (gate)
            {
                debounce?.Cancel();
                cts = debounce = new CancellationTokenSource();
            }
            _ = DebouncedFlush(cts.Token);
        }

        private async Task DebouncedFlush(CancellationToken token)
        {
            try
            {
                await Task.Delay(options.SaveDebounceMs, token);
                await Flush();
            }
            catch (OperationCanceledException) { }   // 新しい入力が来た
            catch (Exception) { }                    // 失敗は State=Error と Error で伝わる
        }

        private async Task FlushCore()
        {
            List<EditEvent> events;
            Snapshot baseline, periodic;
            string body;
            bool writeBody;
            lock (gate)
            {
                if (pending.Count == 0 && !bodyDirty && pendingPeriodic == null) return;
                events = pending;
                pending = new List<EditEvent>();
                // 基準スナップショットは、最初のイベントを追記する前にだけ必要
                baseline = events.Count > 0 ? pendingBaseline : null;
                if (baseline != null) pendingBaseline = null;
                periodic = pendingPeriodic;
                pendingPeriodic = null;
                writeBody = bodyDirty;
                bodyDirty = false;
                body = doc.Text;
                state = NoteSaveState.Saving;
            }
            RaiseStateChanged();

            try
            {
                await Task.Run(() =>
                {
                    if (baseline != null) history.WriteSnapshot(path, baseline);
                    history.AppendEvents(path, events);
                    if (periodic != null) history.WriteSnapshot(path, periodic);
                    if (writeBody) fs.WriteAllTextAtomic(paths.NoteFile(path), body);
                });
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    // 書けなかった分を元に戻す。次の Flush でやり直す(読み込み側は重複した行を読み飛ばす)。
                    events.AddRange(pending);
                    pending = events;
                    if (baseline != null && pendingBaseline == null) pendingBaseline = baseline;
                    if (periodic != null && pendingPeriodic == null) pendingPeriodic = periodic;
                    if (writeBody) bodyDirty = true;
                    state = NoteSaveState.Error;
                    error = "保存できませんでした: " + ex.Message;
                }
                RaiseStateChanged();
                return;
            }

            bool more;
            lock (gate)
            {
                more = pending.Count > 0 || bodyDirty || pendingPeriodic != null;
                error = "";
                state = bodyDirty || pending.Count > 0 ? NoteSaveState.Dirty : NoteSaveState.Saved;
            }
            RaiseStateChanged();
            if (more) ScheduleSave();
        }

        private void RaiseStateChanged()
        {
            NoteSaveState s;
            string e;
            lock (gate) { s = state; e = error; }
            StateChanged?.Invoke(s, e);
        }
    }
}
