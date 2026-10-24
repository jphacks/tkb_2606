using System;
using System.Collections.Generic;
using System.Text;

namespace Cartograph.Core
{
    public enum HistoryStatus
    {
        /// <summary>履歴を再生した結果が本文と一致した。</summary>
        Clean,
        /// <summary>履歴がない。本文は全体を unknown として扱う。</summary>
        NoHistory,
        /// <summary>履歴が本文より先に進んでいた(自アプリの途中終了)。本文と一致する所まで末尾を外した。</summary>
        RecoveredByTrim,
        /// <summary>本文と食い違い、一致する所もなかった。落ちずに、本文全体を unknown として扱う。</summary>
        ResetToUnknown,
    }

    public sealed class HistoryLoadResult
    {
        public LabeledText Doc;
        public int LastSeq;
        public HistoryStatus Status;
        /// <summary>最初のイベントを追記する前に書くべきスナップショット(履歴のない本文、食い違いのリセット)。</summary>
        public Snapshot PendingBaseline;
        public int CorruptLines;
    }

    /// <summary>
    /// .cartograph/history/ 以下の入力履歴(.jsonl)とスナップショット(.snapshot.json)の読み書き。
    /// 外部のエディターによる食い違いの検出・補正は行わない。食い違った時は本文全体を unknown にして先へ進むだけ。
    /// </summary>
    public sealed class HistoryStore
    {
        private readonly IFileSystem fs;
        private readonly ProjectPaths paths;

        public HistoryStore(IFileSystem fs, ProjectPaths paths)
        {
            this.fs = fs;
            this.paths = paths;
        }

        public HistoryLoadResult Load(NotePath note, string body)
        {
            body = body ?? "";
            Snapshot snap = ReadSnapshot(note);
            var events = ReadEvents(note, out int corrupt);

            LabeledText baseDoc = null;
            if (snap != null && !snap.TryToLabeledText(out baseDoc)) snap = null;
            int baseSeq = snap != null ? snap.Seq : 0;
            if (baseDoc == null) baseDoc = new LabeledText();

            var tail = new List<EditEvent>();
            foreach (var e in events) if (e.Seq > baseSeq) tail.Add(e);

            if (snap == null && events.Count == 0)
            {
                var r = new HistoryLoadResult
                {
                    Doc = new LabeledText(body, Origin.Unknown),
                    LastSeq = 0,
                    Status = HistoryStatus.NoHistory,
                    CorruptLines = corrupt,
                };
                if (body.Length > 0) r.PendingBaseline = Snapshot.Of(0, r.Doc);
                return r;
            }

            int maxSeq = Math.Max(baseSeq, events.Count > 0 ? events[events.Count - 1].Seq : 0);

            // 履歴を先頭から再生し、本文と一致した最後の地点を探す。
            var doc = baseDoc.Clone();
            int match = doc.Text == body ? -1 : -2;   // -1: スナップショットだけで一致
            for (int i = 0; i < tail.Count; i++)
            {
                try { ProvenanceReplayer.Apply(doc, tail[i]); }
                catch (ReplayException) { break; }
                if (doc.Length == body.Length && doc.Text == body) match = i;
            }

            if (match == tail.Count - 1)
            {
                // 最後まで再生して本文と一致(スナップショットだけで一致する場合も含む)
                return new HistoryLoadResult
                {
                    Doc = doc, LastSeq = maxSeq, Status = HistoryStatus.Clean, CorruptLines = corrupt,
                };
            }

            if (match >= -1)
            {
                var trimmed = baseDoc.Clone();
                for (int i = 0; i <= match; i++) ProvenanceReplayer.Apply(trimmed, tail[i]);
                int keepSeq = match >= 0 ? tail[match].Seq : baseSeq;
                TruncateAfter(note, events, keepSeq);
                return new HistoryLoadResult
                {
                    Doc = trimmed, LastSeq = keepSeq, Status = HistoryStatus.RecoveredByTrim, CorruptLines = corrupt,
                };
            }

            var reset = new LabeledText(body, Origin.Unknown);
            return new HistoryLoadResult
            {
                Doc = reset,
                LastSeq = maxSeq,
                Status = HistoryStatus.ResetToUnknown,
                PendingBaseline = Snapshot.Of(maxSeq, reset),
                CorruptLines = corrupt,
            };
        }

        /// <summary>seq が単調に増えるイベントだけを返す。壊れた行・重複した行は読み飛ばす。</summary>
        public List<EditEvent> ReadEvents(NotePath note, out int corruptLines)
        {
            corruptLines = 0;
            var result = new List<EditEvent>();
            string text = fs.ReadAllText(paths.HistoryFile(note));
            if (string.IsNullOrEmpty(text)) return result;

            int prev = 0;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                if (!EventJson.TryParse(line, out var e)) { corruptLines++; continue; }
                if (e.Seq <= prev) continue;
                prev = e.Seq;
                result.Add(e);
            }
            return result;
        }

        public Snapshot ReadSnapshot(NotePath note)
        {
            string json = fs.ReadAllText(paths.SnapshotFile(note));
            if (string.IsNullOrEmpty(json)) return null;
            return Snapshot.TryParse(json, out var s) ? s : null;
        }

        /// <summary>イベントをまとめて 1 回の書き込みで追記する。</summary>
        public void AppendEvents(NotePath note, IReadOnlyList<EditEvent> events)
        {
            if (events.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append(EventJson.Serialize(e)).Append('\n');
            fs.AppendText(paths.HistoryFile(note), sb.ToString());
        }

        public void WriteSnapshot(NotePath note, Snapshot snapshot)
        {
            fs.WriteAllTextAtomic(paths.SnapshotFile(note), snapshot.Serialize());
        }

        private void TruncateAfter(NotePath note, List<EditEvent> events, int keepSeq)
        {
            var sb = new StringBuilder();
            foreach (var e in events)
                if (e.Seq <= keepSeq) sb.Append(EventJson.Serialize(e)).Append('\n');
            fs.WriteAllTextAtomic(paths.HistoryFile(note), sb.ToString());
        }
    }
}
