using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapBuildingTests
    {
        private static MapBuilding B(string node, string file, int strong = 0, int medium = 0, int weak = 1)
            => new MapBuilding(node, file, null, strong, medium, weak);
        private static readonly BuildingSettings Settings = BuildingSettings.Default;

        [Test]
        public void ScoreWeighsHeadingsAboveBoldAbovePlainText()
        {
            Assert.That(BuildingSizing.Score(B("n", "人物/田中", 1, 1, 1), Settings), Is.EqualTo(3 + 2 + 1));
        }

        [TestCase(1, BuildingShape.Hut, 1, 1)]
        [TestCase(2, BuildingShape.Hut, 1, 1)]
        [TestCase(3, BuildingShape.House, 2, 2)]
        [TestCase(7, BuildingShape.House, 2, 2)]
        [TestCase(8, BuildingShape.Lodge3x2, 3, 2)]
        [TestCase(14, BuildingShape.Lodge3x2, 3, 2)]
        [TestCase(15, BuildingShape.Lodge3x3, 3, 3)]
        [TestCase(24, BuildingShape.Lodge3x3, 3, 3)]
        public void WeakMentionsMapToShapeByThreshold(int weak, BuildingShape shape, int width, int depth)
        {
            var size = BuildingSizing.Measure(B("n", "人物/田中", 0, 0, weak), Settings);
            Assert.That(size.Shape, Is.EqualTo(shape));
            Assert.That(size.Width, Is.EqualTo(width));
            Assert.That(size.Depth, Is.EqualTo(depth));
        }

        [Test]
        public void TowerGainsFloorsWithScoreUpToTheLimit()
        {
            Assert.That(BuildingSizing.Measure(B("n", "a", 0, 0, 25), Settings).Floors, Is.EqualTo(1));
            Assert.That(BuildingSizing.Measure(B("n", "a", 0, 0, 35), Settings).Floors, Is.EqualTo(2));
            Assert.That(BuildingSizing.Measure(B("n", "a", 0, 0, 9999), Settings).Floors, Is.EqualTo(Settings.MaxFloors));
            var tower = BuildingSizing.Measure(B("n", "a", 0, 0, 25), Settings);
            Assert.That(tower.Depth, Is.EqualTo(1));
            Assert.That(tower.Width, Is.InRange(1, 3));
            Assert.That(BuildingSizing.Measure(B("n", "a", 0, 0, 25), Settings).Width, Is.EqualTo(tower.Width));
        }

        [Test]
        public void BrokenSettingsAreNormalizedAndNeverThrow()
        {
            var broken = new BuildingSettings { HouseFrom = 10, LodgeFrom = 2, LodgeBigFrom = 1, TowerFrom = 0, FloorStep = 0, MaxFloors = 0, MaxPerNode = -3, WeakWeight = 0 };
            var size = BuildingSizing.Measure(B("n", "a", 0, 0, 1), broken);
            Assert.That(size.Shape, Is.EqualTo(BuildingShape.Hut));
            Assert.That(broken.Normalized().MaxPerNode, Is.EqualTo(1));
            Assert.That(BuildingSizing.Measure(B("n", "a", 0, 0, 99), broken).Shape, Is.EqualTo(BuildingShape.Tower));
        }

        [Test]
        public void InvalidBuildingIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MapBuilding("n", "f", "x", 0, 0, 0));
            Assert.Throws<ArgumentException>(() => new MapBuilding("", "f", "x", 1, 0, 0));
            Assert.That(new MapBuilding("n", "人物/田中", null, 1, 0, 0).Name, Is.EqualTo("田中"));
        }

        private static Dictionary<string, long> Nodes(params (string, int, int)[] nodes)
            => nodes.ToDictionary(n => n.Item1, n => MapCell.Key(n.Item2, n.Item3));

        [Test]
        public void BuildingsAvoidNodeCentersPathsAndEachOther()
        {
            var nodes = Nodes(("a", 0, 0));
            var paths = new HashSet<long> { MapCell.Key(2, 0), MapCell.Key(2, 1) };
            var buildings = new[] { B("a", "x/1", 0, 0, 10), B("a", "x/2", 0, 0, 4), B("a", "x/3", 0, 0, 1) };
            var placed = BuildingPlacer.Place(buildings, nodes, paths, Settings);
            Assert.That(placed.Count, Is.EqualTo(3));
            var cells = new HashSet<long>();
            foreach (var p in placed)
                for (int dx = 0; dx < p.Size.Width; dx++)
                for (int dy = 0; dy < p.Size.Depth; dy++)
                {
                    long key = MapCell.Key(p.X + dx, p.Y + dy);
                    Assert.That(cells.Add(key), Is.True, "overlap between buildings");
                    Assert.That(paths.Contains(key), Is.False, "on a path");
                    Assert.That(Math.Abs(p.X + dx) <= 1 && Math.Abs(p.Y + dy) <= 1, Is.False, "on the flag and flower cells");
                }
        }

        [Test]
        public void BiggestBuildingGetsTheClosestSpotAndPlacementIsDeterministic()
        {
            var nodes = Nodes(("a", 10, 10));
            var buildings = new[] { B("a", "x/small", 0, 0, 1), B("a", "x/big", 0, 0, 16) };
            var first = BuildingPlacer.Place(buildings, nodes, null, Settings);
            var second = BuildingPlacer.Place(buildings.Reverse(), nodes, null, Settings);
            Assert.That(first[0].Building.File, Is.EqualTo("x/big"));
            Assert.That(second.Select(p => (p.Building.File, p.X, p.Y)), Is.EqualTo(first.Select(p => (p.Building.File, p.X, p.Y))));
        }

        [Test]
        public void PlacedBuildingsAreReservedInTheSharedOccupancyMap()
        {
            var nodes = Nodes(("a", 0, 0));
            var occupancy = MapOccupancy.ForNodes(nodes.Values, new[] { MapCell.Key(5, 5) });
            Assert.That(occupancy.Kind(0, 0), Is.EqualTo(OccupyKind.Flag));
            Assert.That(occupancy.Kind(5, 5), Is.EqualTo(OccupyKind.Path));
            var placed = BuildingPlacer.PlaceOn(new[] { B("a", "x/1", 0, 0, 10) }, nodes, occupancy, Settings).Single();
            Assert.That(occupancy.Kind(placed.X, placed.Y), Is.EqualTo(OccupyKind.Building));
            Assert.That(occupancy.Reserve(placed.X, placed.Y, OccupyKind.Nature), Is.False);
            Assert.That(occupancy.Kind(placed.X, placed.Y), Is.EqualTo(OccupyKind.Building));
        }

        [Test]
        public void ExtraBuildingsBeyondTheLimitAreDropped()
        {
            var nodes = Nodes(("a", 0, 0));
            var buildings = Enumerable.Range(0, 9).Select(i => B("a", "x/" + i, 0, 0, i + 1)).ToArray();
            var placed = BuildingPlacer.Place(buildings, nodes, null, Settings);
            Assert.That(placed.Count, Is.EqualTo(Settings.MaxPerNode));
            Assert.That(placed.Select(p => p.Size.Score).Min(), Is.EqualTo(4));
        }

        [Test]
        public void BuildingsOfNeighbouringNodesDoNotOverlapAndUnknownNodesAreIgnored()
        {
            var nodes = Nodes(("a", 0, 0), ("b", 3, 0));
            var buildings = new[] { B("a", "x/1", 0, 0, 20), B("b", "x/2", 0, 0, 20), B("zzz", "x/3") };
            var placed = BuildingPlacer.Place(buildings, nodes, null, Settings);
            Assert.That(placed.Count, Is.EqualTo(2));
            var seen = new HashSet<long>();
            foreach (var p in placed)
                for (int dx = 0; dx < p.Size.Width; dx++)
                for (int dy = 0; dy < p.Size.Depth; dy++)
                {
                    Assert.That(seen.Add(MapCell.Key(p.X + dx, p.Y + dy)), Is.True);
                    // 隣の拠点の旗と花のマス(中心の 3x3)にも載らない。
                    Assert.That(Math.Abs(p.X + dx - 3) <= 1 && Math.Abs(p.Y + dy) <= 1, Is.False);
                }
        }
    }
}
