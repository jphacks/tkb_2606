using System;
using Cartograph.EditorUI.Navigation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class NoteTreeTests
    {
        [Test]
        public void NoteNamesSortBaseThenNumericSuffixInsideEveryFolder()
        {
            var root = NoteTree.Build(new[] {
                new NoteTreeEntry("ten", "無題のノート 10.md", "無題のノート 10"),
                new NoteTreeEntry("two", "無題のノート 2.md", "無題のノート 2"),
                new NoteTreeEntry("base", "無題のノート.md", "無題のノート"),
                new NoteTreeEntry("nested-ten", "z/ノート10.md", "ノート10"),
                new NoteTreeEntry("nested-base", "z/ノート.md", "ノート"),
                new NoteTreeEntry("nested-two", "z/ノート2.md", "ノート2") });
            Assert.That(root.Children[0].IsFolder, Is.True);
            Assert.That(root.Children[1].Note.DocumentId, Is.EqualTo("base"));
            Assert.That(root.Children[2].Note.DocumentId, Is.EqualTo("two"));
            Assert.That(root.Children[3].Note.DocumentId, Is.EqualTo("ten"));
            Assert.That(root.Children[0].Children[0].Note.DocumentId, Is.EqualTo("nested-base"));
            Assert.That(root.Children[0].Children[1].Note.DocumentId, Is.EqualTo("nested-two"));
            Assert.That(root.Children[0].Children[2].Note.DocumentId, Is.EqualTo("nested-ten"));
        }

        [Test]
        public void NumericNameTiesUseStableOrdinalPathAndDoNotOverflow()
        {
            var root = NoteTree.Build(new[] {
                new NoteTreeEntry("large", "note999999999999999999999999999.md", ""),
                new NoteTreeEntry("two", "note2.md", ""),
                new NoteTreeEntry("zero-two", "note02.md", "") });
            Assert.That(root.Children[0].Note.DocumentId, Is.EqualTo("zero-two"));
            Assert.That(root.Children[1].Note.DocumentId, Is.EqualTo("two"));
            Assert.That(root.Children[2].Note.DocumentId, Is.EqualTo("large"));
        }

        [Test]
        public void EmptyTownsAndNestedEmptyTownsAreVisible()
        {
            var root = NoteTree.Build(Array.Empty<NoteTreeEntry>(), new[] { "大学/授業/科目A" });
            Assert.That(root.Children[0].Name, Is.EqualTo("大学"));
            Assert.That(root.Children[0].Children[0].Children[0].Name, Is.EqualTo("科目A"));
        }
        [Test]
        public void EmptyFolderCannotCollideWithNotePath()
        {
            Assert.Throws<ArgumentException>(() => NoteTree.Build(new[] { new NoteTreeEntry("a", "A.md", "A") }, new[] { "A.md" }));
        }
        [Test]
        public void ThreeNestedFoldersPreserveDocumentIdAndPath()
        {
            var root = NoteTree.Build(new[] { new NoteTreeEntry("note1", "大学用/授業/科目A/ノート.md", "ノート") });
            var node = root.Children[0].Children[0].Children[0].Children[0];
            Assert.That(node.Note.DocumentId, Is.EqualTo("note1"));
            Assert.That(node.Note.RelativePath, Is.EqualTo("大学用/授業/科目A/ノート.md"));
        }

        [Test]
        public void SameFilenameInDifferentFoldersRemainsDistinct()
        {
            var root = NoteTree.Build(new[] { new NoteTreeEntry("a", "A/ノート.md", "ノート"),
                new NoteTreeEntry("b", "B/ノート.md", "ノート") });
            Assert.That(root.Children.Count, Is.EqualTo(2));
            Assert.That(root.Children[0].Children[0].Note.DocumentId, Is.EqualTo("a"));
            Assert.That(root.Children[1].Children[0].Note.DocumentId, Is.EqualTo("b"));
        }

        [Test]
        public void FolderComesBeforeRootNoteAndWindowsSeparatorsNormalize()
        {
            var root = NoteTree.Build(new[] { new NoteTreeEntry("a", "a.md", "a"),
                new NoteTreeEntry("b", "z\\ノート.md", "ノート") });
            Assert.That(root.Children[0].IsFolder, Is.True);
            Assert.That(root.Children[0].Children[0].Path, Is.EqualTo("z/ノート.md"));
        }

        [Test]
        public void EmptyNotesProduceEmptyRoot()
        {
            Assert.That(NoteTree.Build(Array.Empty<NoteTreeEntry>()).Children.Count, Is.EqualTo(0));
        }

        [Test]
        public void InvalidDisplayPathsAreRejected()
        {
            foreach (var path in new[] { "../a.md", "/a.md", "C:\\a.md", "a//b.md", "./a.md", "" })
                Assert.Throws<ArgumentException>(() => new NoteTreeEntry("a", path, "a"));
        }

        [Test]
        public void DuplicateIdentityOrPathAndFileFolderCollisionAreRejected()
        {
            Assert.Throws<ArgumentException>(() => NoteTree.Build(new[] {
                new NoteTreeEntry("a", "one.md", ""), new NoteTreeEntry("a", "two.md", "") }));
            Assert.Throws<ArgumentException>(() => NoteTree.Build(new[] {
                new NoteTreeEntry("a", "same.md", ""), new NoteTreeEntry("b", "same.md", "") }));
            Assert.Throws<ArgumentException>(() => NoteTree.Build(new[] {
                new NoteTreeEntry("a", "a.md", ""), new NoteTreeEntry("b", "a.md/child.md", "") }));
        }
    }
}
