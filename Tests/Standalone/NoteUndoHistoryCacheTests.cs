using NUnit.Framework;
using Cartograph.EditorUI;

public sealed class NoteUndoHistoryCacheTests
{
    [Test]
    public void SwitchingNotesRetainsEachHistoryAndSelection()
    {
        var cache = new NoteUndoHistoryCache();
        var a = cache.GetOrCreate("a", "", new object());
        a.Record("あ", 1, 1);
        cache.GetOrCreate("b", "other", new object()).Record("other!", 6, 6);
        var restored = cache.GetOrCreate("a", "あ", new object());
        Assert.That(restored, Is.SameAs(a));
        Assert.That(restored.CanUndo, Is.True);
        Assert.That(restored.Current.Cursor, Is.EqualTo(1));
        Assert.That(restored.Undo().Text, Is.Empty);
    }
    [Test]
    public void RedoBranchSurvivesSwitchingAwayAndBack()
    {
        var cache = new NoteUndoHistoryCache();
        var a = cache.GetOrCreate("a", "a", null);
        a.Record("ab", 2, 2); a.Undo();
        cache.GetOrCreate("b", "b", null);
        Assert.That(cache.GetOrCreate("a", "a", null).Redo().Text, Is.EqualTo("ab"));
    }
    [Test]
    public void AuthoritativeContentChangeDiscardsStaleUndoMetadata()
    {
        var cache = new NoteUndoHistoryCache();
        var a = cache.GetOrCreate("a", "a", "old labels");
        a.Record("ab", 2, 2, "updated labels");
        var next = cache.GetOrCreate("a", "external edit", "new labels");
        Assert.That(next, Is.Not.SameAs(a));
        Assert.That(next.CanUndo, Is.False);
        Assert.That(next.Current.Metadata, Is.EqualTo("new labels"));
    }
    [Test]
    public void ProjectResetDiscardsHistoriesEvenWhenPathsAndTextMatch()
    {
        var cache = new NoteUndoHistoryCache();
        var previous = cache.GetOrCreate("same/path", "", null);
        previous.Record("text", 4, 4);
        cache.Clear();
        Assert.That(cache.GetOrCreate("same/path", "text", null).CanUndo, Is.False);
    }
}
