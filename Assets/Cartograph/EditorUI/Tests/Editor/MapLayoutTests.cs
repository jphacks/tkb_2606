using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapLayoutTests
    {
        private static MapSnapshot Snapshot(int count, int towns = 3)
        {
            var nodes = Enumerable.Range(0, count).Select(i => new MapNode("n" + i, "町" + i % towns + "/ノート", "見出し" + i, 1 + i, 50 * i)).ToList();
            var roads = Enumerable.Range(1, count - 1).Select(i => new MapRoad("n" + (i - 1), "n" + i)).ToList();
            return new MapSnapshot(new MapRequest("p", 1, new Dictionary<string, long>()), nodes, roads);
        }

        [Test]
        public void SavedPositionsAreKeptAndNothingIsRecomputedWhenEveryoneIsSaved()
        {
            var snapshot = Snapshot(30);
            var first = MapLayout.Solve(snapshot);
            var second = MapLayout.Solve(snapshot, first);
            foreach (var pair in first) { Assert.That(second[pair.Key].X, Is.EqualTo(pair.Value.X)); Assert.That(second[pair.Key].Y, Is.EqualTo(pair.Value.Y)); }
        }

        [Test]
        public void NewNodesAreAddedWithoutMovingSavedOnes()
        {
            var first = MapLayout.Solve(Snapshot(30));
            var grown = MapLayout.Solve(Snapshot(34), first);
            Assert.That(grown.Count, Is.EqualTo(34));
            foreach (var pair in first) { Assert.That(grown[pair.Key].X, Is.EqualTo(pair.Value.X)); Assert.That(grown[pair.Key].Y, Is.EqualTo(pair.Value.Y)); }
            var cells = grown.Values.Select(p => (int)System.Math.Round(p.X / MapLayout.Tile) + ":" + (int)System.Math.Round(p.Y / MapLayout.Tile)).ToList();
            Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Count), "新しい拠点が、ほかの拠点と同じマスに乗らない");
        }

        [Test]
        public void NewNodePlacementIsDeterministic()
        {
            var first = MapLayout.Solve(Snapshot(30));
            var a = MapLayout.Solve(Snapshot(33), first); var b = MapLayout.Solve(Snapshot(33), first);
            foreach (var pair in a) { Assert.That(b[pair.Key].X, Is.EqualTo(pair.Value.X)); Assert.That(b[pair.Key].Y, Is.EqualTo(pair.Value.Y)); }
        }

        [Test]
        public void OnlyBuildingsAbsentFromThePreviousMapAppear()
        {
            var previous = new HashSet<string> { "a|1", "b|2" };
            Assert.That(BuildingAppearance.NewKeys(previous, new[] { "a|1", "c|3", "b|2", "d|4" }, true), Is.EqualTo(new[] { "c|3", "d|4" }));
            Assert.That(BuildingAppearance.NewKeys(previous, new[] { "a|1" }, true), Is.Empty);
        }
        [Test]
        public void FirstOrUnrelatedMapsHaveNoAppearance()
        {
            Assert.That(BuildingAppearance.NewKeys(new HashSet<string>(), new[] { "a|1" }, false), Is.Empty);
            Assert.That(BuildingAppearance.NewKeys(null, new[] { "a|1" }, true), Is.Empty);
        }
        [Test]
        public void GrowIsHiddenBeforeStartRisesAndEndsAtFullSize()
        {
            Assert.That(BuildingAppearance.Grow(-1), Is.EqualTo(0f));
            float last = 0;
            for (int age = 0; age < BuildingAppearance.Frames; age++)
            { float g = BuildingAppearance.Grow(age); Assert.That(g, Is.GreaterThan(last).And.LessThan(1f)); last = g; }
            Assert.That(BuildingAppearance.Grow(BuildingAppearance.Frames), Is.EqualTo(1f));
            Assert.That(BuildingAppearance.Grow(999), Is.EqualTo(1f));
        }

        [Test]
        public void CellHashIsStableAndSpreadsAcrossChoices()
        {
            Assert.That(MapLayout.CellHash(3, -7), Is.EqualTo(MapLayout.CellHash(3, -7)));
            Assert.That(MapLayout.CellHash(3, 7), Is.Not.EqualTo(MapLayout.CellHash(7, 3)));
            var buckets = new int[4];
            for (int x = 0; x < 40; x++) for (int y = 0; y < 40; y++) buckets[MapLayout.CellHash(x, y) % 4]++;
            Assert.That(buckets.All(n => n > 300 && n < 500), Is.True, string.Join(",", buckets));
        }
    }
}
