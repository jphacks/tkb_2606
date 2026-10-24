using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Cartograph.Core.Tests
{
    public class NoteSessionTests
    {
        private static async Task<NoteSession> Reopen(Env env, string note, NoteSession old = null)
        {
            if (old != null) await old.CloseAsync();
            return await env.Open(note);
        }

        [Test]
        public async Task 開いただけではファイルを作らない()
        {
            var env = new Env();
            env.PutNote("n", "既存の本文");
            var s = await env.Open("n");
            await s.Flush();
            Assert.AreEqual(HistoryStatus.NoHistory, s.HistoryStatus);
            Assert.IsNull(env.History("n"));
            Assert.IsNull(env.SnapshotJson("n"));
            Assert.AreEqual(0, s.TypedCount(), "履歴のない文章は全体が unknown");
        }

        [Test]
        public async Task 履歴のない既存mdを開いて打つと_基準スナップショットから始まり再起動後も一致する()
        {
            var env = new Env();
            env.PutNote("n", "既存");
            var s = await env.Open("n");
            Typing.TypeString(s, 2, "ab");
            await s.Flush();

            Assert.AreEqual("既存ab", env.Body("n"));
            Assert.AreEqual(2, env.HistoryLines("n").Count);
            Assert.IsTrue(Snapshot.TryParse(env.SnapshotJson("n"), out var baseline));
            Assert.AreEqual(0, baseline.Seq);
            Assert.AreEqual("既存", baseline.Text);

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual("既存ab", again.Text);
            Assert.AreEqual(2, again.TypedCount());
            Assert.AreEqual("U2T2", again.CloneDocument().EncodeLabels());
            Assert.AreEqual(2, again.LastSeq);
        }

        [Test]
        public async Task 空のノートに打つと本文と履歴が保存され_seqは開き直しても続く()
        {
            var env = new Env();
            env.PutNote("大学用/授業/ノート", "");
            var s = await env.Open("大学用/授業/ノート");
            Typing.TypeString(s, 0, "波");
            Typing.Ime(s, 1, "動です");
            await s.Flush();

            Assert.AreEqual("波動です", env.Body("大学用/授業/ノート"));
            var lines = env.HistoryLines("大学用/授業/ノート");
            Assert.AreEqual(2, lines.Count);
            StringAssert.Contains("\"src\":\"key\"", lines[0]);
            StringAssert.Contains("\"src\":\"ime\"", lines[1]);
            Assert.IsNull(env.SnapshotJson("大学用/授業/ノート"), "空から始めた場合は基準スナップショットなし");

            var again = await Reopen(env, "大学用/授業/ノート", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(4, again.TypedCount());
            Typing.TypeString(again, 4, "。");
            await again.Flush();
            var seqs = env.HistoryLines("大学用/授業/ノート").Select(l => { EventJson.TryParse(l, out var e); return e.Seq; }).ToList();
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, seqs);
        }

        [Test]
        public async Task 外部の貼り付けは遮断され_本文は変わらず履歴に本文は残らない()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "あ");
            var r = Typing.Paste(s, 1, "外部の秘密の文章");
            Assert.IsFalse(r.Approved);
            Assert.AreEqual("あ", s.Text);
            await s.Flush();

            Assert.AreEqual("あ", env.Body("n"));
            string history = env.History("n");
            StringAssert.DoesNotContain("外部の秘密", history);
            Assert.AreEqual(2, env.HistoryLines("n").Count);
            StringAssert.Contains("\"type\":\"blocked\"", env.HistoryLines("n")[1]);
            Assert.AreEqual(NoteSaveState.Saved, s.State, "遮断だけでは Dirty にならない");

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(1, again.TypedCount());
        }

        [Test]
        public async Task ノート間の内部コピーは元のラベルを引き継ぎ_再起動後も保たれる()
        {
            var env = new Env();
            env.PutNote("A", "unknown部分");   
            env.PutNote("B", "");
            var a = await env.Open("A");
            Typing.TypeString(a, a.Text.Length, "打った");
            var b = await env.Open("B");

            a.NotifyCopied(a.Text.Length - 3, 3);   // typed の「打った」
            Assert.IsTrue(Typing.Paste(b, 0, "打った").Approved);
            Assert.AreEqual(3, b.TypedCount());

            a.NotifyCopied(6, 6);   // unknown と typed が混ざる範囲
            string mixed = a.Text.Substring(6, 6);
            Assert.IsTrue(Typing.Paste(b, b.Text.Length, mixed).Approved);

            await a.Flush();
            await b.Flush();
            var expected = b.CloneDocument().EncodeLabels();
            StringAssert.Contains("\"srcDoc\":\"A\"", env.History("B"));

            var bAgain = await Reopen(env, "B", b);
            Assert.AreEqual(HistoryStatus.Clean, bAgain.HistoryStatus);
            Assert.AreEqual(expected, bAgain.CloneDocument().EncodeLabels());
            Assert.AreEqual(b.Text, bAgain.Text);
        }

        [Test]
        public async Task 選択範囲の置換は削除と挿入の2イベントで残り_再生で一致する()
        {
            var env = new Env();
            env.PutNote("n", "abcd");
            var s = await env.Open("n");
            var r = s.Apply(new ChangeCandidate
            {
                NewText = "aXd",
                SelStartBefore = 1,
                SelEndBefore = 3,
                Evidence = new InputEvidence { Kind = EvidenceKind.Key, Text = "X" },
            });
            Assert.IsTrue(r.Approved);
            await s.Flush();
            Assert.AreEqual(2, env.HistoryLines("n").Count);

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual("aXd", again.Text);
            Assert.AreEqual("U1T1U1", again.CloneDocument().EncodeLabels());
        }

        [Test]
        public async Task 元に戻すで消した文字を戻すと_元のラベルが復元される()
        {
            var env = new Env();
            env.PutNote("n", "abc");   // unknown
            var s = await env.Open("n");
            Typing.TypeString(s, 1, "X");   // aXbc : U1 T1 U2
            string labelsBefore = s.CloneDocument().EncodeLabels();
            var saved = s.CloneDocument().Slice(0, 4);

            Typing.Delete(s, 0, 4);          // 全部消す
            Assert.AreEqual("", s.Text);
            var undo = s.Apply(new ChangeCandidate
            {
                NewText = "aXbc",
                SelStartBefore = 0,
                SelEndBefore = 0,
                Evidence = new InputEvidence { Kind = EvidenceKind.Undo, Text = "aXbc", UndoLabels = saved },
            });
            Assert.IsTrue(undo.Approved);
            Assert.AreEqual(labelsBefore, s.CloneDocument().EncodeLabels());

            await s.Flush();
            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(labelsBefore, again.CloneDocument().EncodeLabels());
        }

        [Test]
        public async Task 絵文字を打って消しても履歴が合う()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "a😀b");
            Assert.AreEqual("a😀b", s.Text);
            Typing.Delete(s, 1, 2);   // 絵文字を消す
            await s.Flush();
            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual("ab", again.Text);
        }

        [Test]
        public async Task スナップショットは指定のイベント数ごとに取られ_そこから再生できる()
        {
            var env = new Env(snapshotInterval: 10);
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "0123456789abcdefghijklmno");   // 25 イベント
            await s.Flush();

            Assert.IsTrue(Snapshot.TryParse(env.SnapshotJson("n"), out var snap));
            Assert.AreEqual(20, snap.Seq);
            Assert.AreEqual("0123456789abcdefghij", snap.Text);

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual("0123456789abcdefghijklmno", again.Text);
            Assert.AreEqual(25, again.LastSeq);
            Assert.AreEqual(25, again.TypedCount());
        }

        [Test]
        public async Task 履歴が本文より先に進んだ状態_途中終了_は末尾を外して合わせる()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "abc");
            await s.Flush();
            string bodyBefore = env.Body("n");

            Typing.TypeString(s, 3, "de");
            await s.Flush();
            env.SetBody("n", bodyBefore);   // 履歴を追記した後、本文を置き換える前に落ちた状態

            var again = await Reopen(env, "n");
            Assert.AreEqual(HistoryStatus.RecoveredByTrim, again.HistoryStatus);
            Assert.AreEqual("abc", again.Text);
            Assert.AreEqual(3, again.TypedCount(), "ラベルは失われない");
            Assert.AreEqual(3, again.LastSeq);
            Assert.AreEqual(3, env.HistoryLines("n").Count, "ファイルの末尾も外れている");

            Typing.TypeString(again, 3, "X");
            await again.Flush();
            var third = await Reopen(env, "n", again);
            Assert.AreEqual(HistoryStatus.Clean, third.HistoryStatus);
            Assert.AreEqual("abcX", third.Text);
            Assert.AreEqual(4, third.TypedCount());
        }

        [Test]
        public async Task 本文が別の内容に変わっていたら_落ちずに本文全体をunknownとして続ける()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "abc");
            await s.Flush();

            env.SetBody("n", "外のエディターで書き換えた本文");
            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.ResetToUnknown, again.HistoryStatus);
            Assert.AreEqual("外のエディターで書き換えた本文", again.Text);
            Assert.AreEqual(0, again.TypedCount());
            Assert.AreEqual(3, again.LastSeq, "seq は進み続ける");

            Typing.TypeString(again, again.Text.Length, "Z");
            await again.Flush();
            var third = await Reopen(env, "n", again);
            Assert.AreEqual(HistoryStatus.Clean, third.HistoryStatus);
            Assert.AreEqual(1, third.TypedCount());
            Assert.AreEqual(4, third.LastSeq);
        }

        [Test]
        public async Task 壊れた履歴行は読み飛ばされ_次の追記は新しい行から始まる()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "ab");
            await s.Flush();
            env.SetHistory("n", env.History("n") + "{\"seq\":3,\"t\":1,\"type\":\"ins");   // 書き込み途中で落ちた行

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(1, again.CorruptHistoryLines);
            Assert.AreEqual("ab", again.Text);

            Typing.TypeString(again, 2, "c");
            await again.Flush();
            var third = await Reopen(env, "n", again);
            Assert.AreEqual(HistoryStatus.Clean, third.HistoryStatus);
            Assert.AreEqual("abc", third.Text);
            Assert.AreEqual(3, third.TypedCount());
        }

        [Test]
        public async Task 保存に失敗したら本文を保持してエラーになり_直ったらやり直して重複しない()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            var states = new List<NoteSaveState>();
            s.StateChanged += (st, err) => states.Add(st);

            Typing.TypeString(s, 0, "abc");
            env.Fs.FailWrites = true;
            await s.Flush();
            Assert.AreEqual(NoteSaveState.Error, s.State);
            StringAssert.Contains("保存できませんでした", s.Error);
            Assert.AreEqual("abc", s.Text, "未保存の本文は保持される");
            Assert.AreEqual("", env.Body("n"));

            Typing.TypeString(s, 3, "d");   // 失敗中も編集は続けられる
            env.Fs.FailWrites = false;
            await s.Flush();
            Assert.AreEqual(NoteSaveState.Saved, s.State);
            Assert.AreEqual("", s.Error);
            Assert.AreEqual("abcd", env.Body("n"));
            Assert.Contains(NoteSaveState.Saving, states);

            var again = await Reopen(env, "n", s);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(4, again.TypedCount());
            Assert.AreEqual(4, env.HistoryLines("n").Count);
        }

        [Test]
        public async Task 自動保存は入力が止まってから行われる()
        {
            var env = new Env();
            env.Options.SaveDebounceMs = 150;
            env.PutNote("n", "");
            var s = await env.Open("n");
            Typing.TypeString(s, 0, "abc");
            Assert.AreEqual("", env.Body("n"), "入力直後はまだ保存されない");
            Assert.AreEqual(NoteSaveState.Dirty, s.State);

            for (int i = 0; i < 100 && s.State != NoteSaveState.Saved; i++) await Task.Delay(20);
            Assert.AreEqual(NoteSaveState.Saved, s.State);
            Assert.AreEqual("abc", env.Body("n"));
        }

        [Test]
        public async Task 閉じたノートは編集できない()
        {
            var env = new Env();
            env.PutNote("n", "");
            var s = await env.Open("n");
            await s.CloseAsync();
            Assert.Throws<InvalidOperationException>(() => Typing.TypeAt(s, 0, "a"));
        }

        [Test]
        public async Task 乱数の編集を繰り返しても_開き直した結果が常に一致する()
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5 })
            {
                var env = new Env(snapshotInterval: 7);
                var rnd = new Random(seed);
                env.PutNote("A", "初めからある文章");
                env.PutNote("B", "");
                var a = await env.Open("A");
                var b = await env.Open("B");
                var notes = new[] { a, b };

                for (int step = 0; step < 300; step++)
                {
                    var s = notes[rnd.Next(2)];
                    var other = s == a ? b : a;
                    int len = s.Text.Length;
                    switch (rnd.Next(7))
                    {
                        case 0:
                        case 1:
                        case 2:
                            Typing.TypeString(s, SafePos(s.Text, rnd.Next(len + 1)), rnd.Next(4) == 0 ? "😀" : ((char)('a' + rnd.Next(26))).ToString());
                            break;
                        case 3:
                            Typing.Ime(s, SafePos(s.Text, rnd.Next(len + 1)), "変換" + rnd.Next(10));
                            break;
                        case 4:
                            if (len > 0)
                            {
                                int p = SafePos(s.Text, rnd.Next(len));
                                int e = SafePos(s.Text, Math.Min(len, p + 1 + rnd.Next(4)));
                                Typing.Delete(s, p, e - p);
                            }
                            break;
                        case 5:
                            if (other.Text.Length > 0)
                            {
                                int p = SafePos(other.Text, rnd.Next(other.Text.Length));
                                int e = SafePos(other.Text, Math.Min(other.Text.Length, p + 1 + rnd.Next(5)));
                                if (e > p)
                                {
                                    other.NotifyCopied(p, e - p);
                                    Assert.IsTrue(Typing.Paste(s, SafePos(s.Text, rnd.Next(s.Text.Length + 1)), other.Text.Substring(p, e - p)).Approved);
                                }
                            }
                            break;
                        default:
                            Assert.IsFalse(Typing.Paste(s, SafePos(s.Text, rnd.Next(len + 1)), "外部" + rnd.Next(100)).Approved);
                            break;
                    }
                    if (step % 40 == 39) await a.Flush();
                }

                await a.Flush();
                await b.Flush();
                foreach (var live in notes)
                {
                    var again = await env.Open(live.Path.Value);
                    Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus, "seed " + seed + " " + live.Path);
                    Assert.AreEqual(live.Text, again.Text, "seed " + seed);
                    Assert.AreEqual(live.CloneDocument().EncodeLabels(), again.CloneDocument().EncodeLabels(), "seed " + seed);
                    Assert.AreEqual(live.LastSeq, again.LastSeq);
                    Assert.AreEqual(env.Body(live.Path.Value), live.Text);
                }
            }
        }

        // サロゲートペアの途中に位置を置かない(テキスト欄のカーソルも対の途中には来ない)
        private static int SafePos(string text, int pos)
        {
            if (pos > 0 && pos < text.Length && char.IsLowSurrogate(text[pos]) && char.IsHighSurrogate(text[pos - 1])) return pos - 1;
            return pos;
        }
    }
}
