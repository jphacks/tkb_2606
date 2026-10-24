using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class TextUndoHistoryTests
    {
        [Test]
        public void UndoMetadataSurvivesSelectionUpdatesAndRedo()
        {
            var oldLabels = new object(); var newLabels = new object();
            var h = new TextUndoHistory("a", metadata: oldLabels);
            h.UpdateSelection(1, 1); h.Record("ab", 2, 2, newLabels); h.UpdateSelection(0, 2);
            Assert.That(h.Undo().Metadata, Is.SameAs(oldLabels));
            Assert.That(h.Redo().Metadata, Is.SameAs(newLabels));
        }
        [Test]
        public void JapaneseAndEmojiRoundTripWithSelection()
        {
            var h = new TextUndoHistory("あ😀\nい");
            h.UpdateSelection(1, 3);
            h.Record("あ日本語\nい", 4, 4);
            Assert.That(h.Undo().Text, Is.EqualTo("あ😀\nい"));
            Assert.That(h.Current.Cursor, Is.EqualTo(1));
            Assert.That(h.Current.Selection, Is.EqualTo(3));
            Assert.That(h.Redo().Text, Is.EqualTo("あ日本語\nい"));
        }

        [Test]
        public void EditingAfterUndoDropsRedoBranch()
        {
            var h = new TextUndoHistory("");
            h.Record("A", 1, 1); h.Record("AB", 2, 2); h.Undo();
            h.Record("AC", 2, 2);
            Assert.That(h.CanRedo, Is.False);
            Assert.That(h.Undo().Text, Is.EqualTo("A"));
            Assert.That(h.Redo().Text, Is.EqualTo("AC"));
        }

        [Test]
        public void IndependentDocumentsKeepIndependentHistory()
        {
            var first = new TextUndoHistory("一");
            var second = new TextUndoHistory("二");
            first.Record("一行目", 3, 3); second.Record("二行目", 3, 3);
            first.Undo();
            Assert.That(first.Current.Text, Is.EqualTo("一"));
            Assert.That(second.Current.Text, Is.EqualTo("二行目"));
        }

        [Test]
        public void HistoryCapacityAndCharacterBudgetRetainCurrentText()
        {
            var h = new TextUndoHistory("", 3, 20);
            h.Record("a", 1, 1); h.Record("ab", 2, 2); h.Record("abc", 3, 3);
            h.Undo(); h.Undo();
            Assert.That(h.Current.Text, Is.EqualTo("a"));
            Assert.That(h.CanUndo, Is.False);
            h.Record(new string('字', 10000), 10000, 10000);
            Assert.That(h.Current.Text.Length, Is.EqualTo(10000));
            Assert.That(h.CanUndo, Is.False);
        }

        [Test]
        public void SelectionOnlyChangeDoesNotCreateUndoStep()
        {
            var h = new TextUndoHistory("abc");
            h.Record("abc", 1, 2);
            Assert.That(h.CanUndo, Is.False);
            Assert.That(h.Current.Selection, Is.EqualTo(2));
        }

        [Test]
        public void TenThousandCharactersSurviveUndoRedo()
        {
            var original = new string('あ', 10000);
            var h = new TextUndoHistory(original);
            h.Record(original + "確定", 10002, 10002);
            Assert.That(h.Undo().Text, Is.EqualTo(original));
            Assert.That(h.Redo().Text, Is.EqualTo(original + "確定"));
        }
    }
}
