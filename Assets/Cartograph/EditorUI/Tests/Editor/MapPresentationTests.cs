using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapPresentationTests
    {
        private static MapRequest Request(long generation = 1, string project = "p", long revision = 2)
            => new MapRequest(project, generation, new Dictionary<string, long> { { "町/A", revision } });
        private static MapSnapshot Snapshot(MapRequest request)
            => new MapSnapshot(request, new[] { new MapNode("a", "町/A", "A", 3, 0) });

        [Test]
        public void SnapshotCopiesCollectionsAndRevisionDictionary()
        {
            var revisions = new Dictionary<string, long> { { "町/A", 2 } };
            var nodes = new List<MapNode> { new MapNode("a", "町/A", "A", 1, 0) };
            var snapshot = new MapSnapshot(new MapRequest("p", 1, revisions), nodes);
            revisions["町/A"] = 99; nodes.Clear();
            Assert.That(snapshot.Nodes.Count, Is.EqualTo(1));
            Assert.That(snapshot.Request.NoteRevisions["町/A"], Is.EqualTo(2));
        }
        [Test]
        public void OldProjectOldGenerationAndStaleRevisionAreRejected()
        {
            var gate = new MapSnapshotGate(); gate.Reset("p");
            var versions = Request().NoteRevisions;
            Assert.That(gate.TryAccept(Snapshot(Request(project: "old")), versions), Is.False);
            Assert.That(gate.TryAccept(Snapshot(Request(revision: 1)), versions), Is.False);
            Assert.That(gate.TryAccept(Snapshot(Request(2)), versions), Is.True);
            Assert.That(gate.TryAccept(Snapshot(Request(1)), versions), Is.False);
            Assert.That(gate.TryAccept(Snapshot(Request(2)), versions), Is.False);
            gate.Reset("new");
            Assert.That(gate.TryAccept(Snapshot(Request(1, "new")), versions), Is.True);
        }
        [Test]
        public void UnknownSchemaAndDeletedNotesCannotReplaceCurrentMap()
        {
            var gate = new MapSnapshotGate(); gate.Reset("p");
            var future = new MapSnapshot(Request(), new[] { new MapNode("a", "町/A", "A", 1, 0) }, schemaVersion: 99);
            Assert.That(gate.TryAccept(future, Request().NoteRevisions), Is.False);
            Assert.That(gate.TryAccept(Snapshot(Request()), new Dictionary<string, long>()), Is.False);
        }
        [Test]
        public void InvalidNodeReferencesAndDuplicateIdsAreRejected()
        {
            var node = new MapNode("a", "町/A", "A", 1, 0);
            Assert.Throws<ArgumentException>(() => new MapSnapshot(Request(), new[] { node, node }));
            Assert.Throws<ArgumentException>(() => new MapSnapshot(Request(), new[] { node }, new[] { new MapRoad("a", "missing") }));
            Assert.Throws<ArgumentException>(() => new MapSnapshot(Request(), new[] { node }, flowers: new[] { new MapFlower("a", 1, (Emotion)99) }));
            Assert.Throws<ArgumentException>(() => new MapNode("a", "町/A", "A", 0, 0));
        }
        [Test]
        public void ZoomKeepsWorldPointUnderPointerAndRoundTripsAfterPan()
        {
            var view = new MapViewport(); view.Pan(120, -80);
            var anchor = new MapPoint(250, 200); var before = view.World(anchor);
            view.Zoom(1.5f, anchor); var after = view.World(anchor);
            Assert.That(after.X, Is.EqualTo(before.X).Within(0.001));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.001));
            var roundTrip = view.Screen(after);
            Assert.That(roundTrip.X, Is.EqualTo(anchor.X).Within(0.001));
        }
        [Test]
        public void ZoomIsBoundedAndEmptyFitRemainsFinite()
        {
            var view = new MapViewport(); view.Zoom(100000, new MapPoint(0, 0));
            Assert.That(view.Scale, Is.EqualTo(3));
            view.Zoom(0.000001f, new MapPoint(0, 0)); Assert.That(view.Scale, Is.EqualTo(0.12f));
            view.Zoom(float.NaN, new MapPoint(0, 0)); Assert.That(float.IsNaN(view.Scale), Is.False);
            view.Fit(Array.Empty<MapPoint>(), 600, 800);
            Assert.That(view.Screen(new MapPoint(0, 0)).X, Is.EqualTo(300));
        }
        [Test]
        public void UnknownTextOpensOneTileAndGrowthIsBounded()
        {
            Assert.That(MapLayout.LandRadius(0), Is.EqualTo(0));
            Assert.That(MapLayout.LandRadius(100), Is.GreaterThan(MapLayout.LandRadius(1)));
            Assert.That(MapLayout.LandRadius(int.MaxValue), Is.LessThanOrEqualTo(4));
        }
        [Test]
        public void LayoutIsDeterministicTileAlignedAndDoesNotOverlapCenters()
        {
            var nodes = Enumerable.Range(0, 35).Select(i => new MapNode("n" + i, "町/A", "A", i + 1, 100)).ToArray();
            var first = MapLayout.Solve(new MapSnapshot(Request(), nodes));
            var second = MapLayout.Solve(new MapSnapshot(Request(), nodes.Reverse()));
            Assert.That(first.Values.Select(p => p.X + ":" + p.Y).Distinct().Count(), Is.EqualTo(35));
            foreach (var node in nodes)
            {
                Assert.That(first[node.Id].X, Is.EqualTo(second[node.Id].X));
                Assert.That(first[node.Id].Y % MapLayout.Tile, Is.EqualTo(0));
            }
        }
    }
}
