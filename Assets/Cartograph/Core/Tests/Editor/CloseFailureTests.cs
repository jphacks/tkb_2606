using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Cartograph.Core.Tests
{
    public sealed class CloseFailureTests
    {
        [Test]
        public async Task FailedCloseKeepsSessionAndPendingEditsForRetry()
        {
            var env = new Env();
            env.PutNote("A", "");
            var workspace = ProjectWorkspace.Open(env.Paths.Root, env.Fs, env.Options);
            var session = await workspace.OpenNote(Env.P("A"));
            Typing.TypeString(session, 0, "未保存");
            env.Fs.FailWrites = true;
            Assert.ThrowsAsync<IOException>(async () => await workspace.CloseNote(Env.P("A")));
            Assert.That(session.IsClosed, Is.False);
            Assert.That(await workspace.OpenNote(Env.P("A")), Is.SameAs(session));
            Assert.That(session.Text, Is.EqualTo("未保存"));
            env.Fs.FailWrites = false;
            await workspace.CloseNote(Env.P("A"));
            Assert.That(env.Body("A"), Is.EqualTo("未保存"));
            var restored = await workspace.OpenNote(Env.P("A"));
            Assert.That(restored.Text, Is.EqualTo("未保存"));
            Assert.That(restored.TypedCount(), Is.EqualTo(3));
        }

        [Test]
        public async Task FailedFlushPreventsRenameAndKeepsOldPath()
        {
            var env = new Env(); env.PutNote("A", "");
            var workspace = ProjectWorkspace.Open(env.Paths.Root, env.Fs, env.Options);
            var session = await workspace.OpenNote(Env.P("A"));
            Typing.TypeString(session, 0, "入力"); env.Fs.FailWrites = true;
            Assert.ThrowsAsync<IOException>(async () => await workspace.RenameNote(Env.P("A"), "B"));
            Assert.That(workspace.NoteExists(Env.P("A")), Is.True);
            Assert.That(workspace.NoteExists(Env.P("B")), Is.False);
            Assert.That(session.IsClosed, Is.False);
            env.Fs.FailWrites = false; await workspace.FlushAll();
            Assert.That(env.Body("A"), Is.EqualTo("入力"));
        }
    }
}
