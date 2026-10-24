using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Cartograph.Core.Tests
{
    public class ProjectWorkspaceTests
    {
        private static ProjectWorkspace OpenMem(Env env) => ProjectWorkspace.Open(env.Paths.Root, env.Fs, env.Options);

        [Test]
        public void 開くとnotesフォルダができ_3階層の入れ子が階層のまま取れる()
        {
            var env = new Env();
            env.PutNote("大学用/授業/科目A/ノート", "x");
            env.PutNote("大学用/授業/科目A/メモ", "x");
            env.PutNote("大学用/授業/別科目", "x");
            env.PutNote("個人用/日記", "x");
            env.PutNote("ルート", "x");
            env.Fs.Files[env.Paths.NotesDir.Replace('\\', '/') + "/個人用/readme.txt"] = "md ではない";

            var ws = OpenMem(env);
            Assert.IsTrue(env.Fs.DirectoryExists(env.Paths.NotesDir));

            var tree = ws.Tree;
            CollectionAssert.AreEqual(new[] { "個人用", "大学用" }, tree.Folders.Select(f => f.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "ルート" }, tree.Notes.Select(n => n.Name).ToArray());

            var univ = tree.Folders.Single(f => f.Name == "大学用");
            var lecture = univ.Folders.Single();
            Assert.AreEqual("授業", lecture.Name);
            Assert.AreEqual("大学用/授業", lecture.Path);
            Assert.AreEqual("別科目", lecture.Notes.Single().Name);
            var subject = lecture.Folders.Single();
            Assert.AreEqual("大学用/授業/科目A", subject.Path);
            CollectionAssert.AreEqual(new[] { "ノート", "メモ" }, subject.Notes.Select(n => n.Name).ToArray());
            Assert.AreEqual("大学用/授業/科目A/ノート", subject.Notes[0].Path.Value);

            Assert.AreEqual(5, ws.AllNotes().Count);
        }

        [Test]
        public async Task ノートの作成と編集_開き直し()
        {
            var env = new Env();
            var ws = OpenMem(env);
            int treeChanges = 0;
            ws.TreeChanged += () => treeChanges++;

            var note = Env.P("科目/新しいノート");
            var s = await ws.CreateNote(note);
            Assert.IsTrue(ws.NoteExists(note));
            Assert.AreEqual(1, treeChanges);
            Assert.AreEqual("新しいノート", ws.Tree.Folders.Single().Notes.Single().Name);
            Assert.Throws<IOException>(() => ws.CreateNote(note).GetAwaiter().GetResult());

            Typing.TypeString(s, 0, "こんにちは");
            await ws.FlushAll();

            var ws2 = OpenMem(env);
            var again = await ws2.OpenNote(note);
            Assert.AreEqual("こんにちは", again.Text);
            Assert.AreEqual(5, again.TypedCount());
            Assert.AreSame(again, await ws2.OpenNote(note), "同じノートは同じセッションを返す");

            Assert.Throws<FileNotFoundException>(() => ws2.OpenNote(Env.P("ない")));
        }

        [Test]
        public async Task 名前を変えると履歴とスナップショットも一緒に移る()
        {
            var env = new Env(snapshotInterval: 5);
            var ws = OpenMem(env);
            var old = Env.P("科目/旧名");
            var s = await ws.CreateNote(old);
            Typing.TypeString(s, 0, "0123456789");
            await ws.FlushAll();
            Assert.IsNotNull(env.SnapshotJson("科目/旧名"));

            var renamed = await ws.RenameNote(old, "新名");
            Assert.AreEqual("科目/新名", renamed.Value);
            Assert.IsFalse(ws.NoteExists(old));
            Assert.IsNull(env.History("科目/旧名"));
            Assert.IsNull(env.SnapshotJson("科目/旧名"));
            Assert.AreEqual("新名", ws.Tree.Folders.Single().Notes.Single().Name);

            var again = await ws.OpenNote(renamed);
            Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
            Assert.AreEqual(10, again.TypedCount(), "名前を変えても来歴は残る");
        }

        [Test]
        public async Task 名前の変更は既存の名前とぶつかる時と不正な名前を拒否する()
        {
            var env = new Env();
            var ws = OpenMem(env);
            await ws.CreateNote(Env.P("a"));
            await ws.CreateNote(Env.P("b"));
            Assert.Throws<IOException>(() => ws.RenameNote(Env.P("a"), "b").GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => ws.RenameNote(Env.P("a"), "../c").GetAwaiter().GetResult());
            Assert.IsTrue(ws.NoteExists(Env.P("a")));
        }

        [Test]
        public async Task 名前を変えても内部バッファの貼り付け元が追従する()
        {
            var env = new Env();
            var ws = OpenMem(env);
            var a = await ws.CreateNote(Env.P("a"));
            Typing.TypeString(a, 0, "xyz");
            a.NotifyCopied(0, 3);
            await ws.RenameNote(Env.P("a"), "a2");
            Assert.AreEqual("a2", ws.Clipboard.SourceNote);
        }

        [Test]
        public async Task 実ディスクで保存し_別のワークスペースで読み直せる_BOMなし()
        {
            string root = Path.Combine(Path.GetTempPath(), "cartograph-core-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new NoteSessionOptions { SaveDebounceMs = -1 };
                var ws = ProjectWorkspace.Open(root, null, options);
                var note = Env.P("大学用/授業/科目A/ノート");
                var s = await ws.CreateNote(note);
                Typing.TypeString(s, 0, "日本語\nの本文");
                await ws.FlushAll();

                string md = Path.Combine(root, "notes", "大学用", "授業", "科目A", "ノート.md");
                string jsonl = Path.Combine(root, ".cartograph", "history", "大学用", "授業", "科目A", "ノート.jsonl");
                Assert.IsTrue(File.Exists(md));
                Assert.IsTrue(File.Exists(jsonl));
                var bytes = File.ReadAllBytes(md);
                Assert.AreEqual("日本語\nの本文", new UTF8Encoding(false).GetString(bytes));
                Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM を付けない");
                Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length, "一時ファイルが残らない");

                var ws2 = ProjectWorkspace.Open(root, null, options);
                var again = await ws2.OpenNote(note);
                Assert.AreEqual(HistoryStatus.Clean, again.HistoryStatus);
                Assert.AreEqual(6, again.TypedCount(), "改行は数えない");

                var renamed = await ws2.RenameNote(note, "改名");
                Assert.IsTrue(File.Exists(Path.Combine(root, "notes", "大学用", "授業", "科目A", "改名.md")));
                Assert.IsFalse(File.Exists(md));
                Assert.IsFalse(File.Exists(jsonl));
                Assert.IsTrue(File.Exists(Path.Combine(root, ".cartograph", "history", "大学用", "授業", "科目A", "改名.jsonl")));
                Assert.AreEqual(HistoryStatus.Clean, (await ws2.OpenNote(renamed)).HistoryStatus);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
