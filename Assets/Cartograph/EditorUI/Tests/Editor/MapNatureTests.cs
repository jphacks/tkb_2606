using System;
using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapNatureTests
    {
        private static readonly NatureSettings Settings = NatureSettings.Default;

        private static NatureNode Node(string id, int x, int y, int chars, int level = 2, string town = "町")
            => new NatureNode(id, MapCell.Key(x, y), chars, level, town);
        private static List<NatureItem> Place(IEnumerable<NatureNode> nodes, IEnumerable<MapRoad> roads = null, MapOccupancy occupancy = null, NatureSettings settings = null)
        {
            var list = nodes.ToList();
            return NaturePlacer.Place(list, roads, occupancy ?? MapOccupancy.ForNodes(list.Select(n => n.Cell), null), settings ?? Settings);
        }
        private static string Describe(List<NatureItem> items) => string.Join(";", items.Select(i => i.Kind + "@" + i.X + "," + i.Y));

        [Test]
        public void FillRatioLeavesGapsInsteadOfFillingEveryFreeCell()
        {
            var full = new NatureSettings { MaxFillRatio = 1f, MaxPerNode = 400, CharsPerItem = 1 };
            var half = new NatureSettings { MaxFillRatio = 0.5f, MaxPerNode = 400, CharsPerItem = 1 };
            int all = Place(new[] { Node("a", 0, 0, 2500) }, settings: full).Count;
            int some = Place(new[] { Node("a", 0, 0, 2500) }, settings: half).Count;
            Assert.That(all, Is.GreaterThan(20));
            Assert.That(some, Is.LessThan(all));
            Assert.That(some, Is.EqualTo((int)System.Math.Ceiling(all * 0.5)));
        }

        [Test]
        public void InvalidFillRatioIsNormalized()
        {
            Assert.That(new NatureSettings { MaxFillRatio = float.NaN }.Normalized().MaxFillRatio, Is.EqualTo(0.5f));
            Assert.That(new NatureSettings { MaxFillRatio = 3f }.Normalized().MaxFillRatio, Is.EqualTo(1f));
            Assert.That(new NatureSettings { MaxFillRatio = -1f }.Normalized().MaxFillRatio, Is.EqualTo(0f));
        }

        [Test]
        public void NodesWithoutTextOrTinyLandGetNothing()
        {
            Assert.That(Place(new[] { Node("a", 0, 0, 0) }), Is.Empty);
            // 本文が 100 文字未満の土地は中心の 3x3 だけで、旗と花の置き場になる。
            Assert.That(Place(new[] { Node("a", 0, 0, 99) }), Is.Empty);
        }

        [Test]
        public void SameInputGivesSameResultRegardlessOfNodeOrder()
        {
            var nodes = new[] { Node("a", 0, 0, 500), Node("b", 20, 0, 950, 1), Node("c", 0, 20, 250, 3, "別の町") };
            Assert.That(Describe(Place(nodes.Reverse())), Is.EqualTo(Describe(Place(nodes))));
            Assert.That(Place(nodes), Is.Not.Empty);
        }

        [Test]
        public void ItemsStayOnFreeLandCellsAndNeverOverlap()
        {
            var nodes = new[] { Node("a", 0, 0, 3000), Node("b", 4, 0, 3000) };
            var occupancy = MapOccupancy.ForNodes(nodes.Select(n => n.Cell), new[] { MapCell.Key(2, 2), MapCell.Key(-2, 1) });
            var items = Place(nodes, null, occupancy);
            var seen = new HashSet<long>();
            foreach (var item in items)
            {
                Assert.That(seen.Add(MapCell.Key(item.X, item.Y)), Is.True, "overlap");
                foreach (var n in nodes) Assert.That(Math.Abs(item.X - MapCell.X(n.Cell)) <= 1 && Math.Abs(item.Y - MapCell.Y(n.Cell)) <= 1, Is.False, "on a flag cell");
                Assert.That(occupancy.Kind(item.X, item.Y), Is.EqualTo(OccupyKind.Nature));
            }
            Assert.That(items.Any(i => i.X == 2 && i.Y == 2 || i.X == -2 && i.Y == 1), Is.False, "on a path");
        }

        [Test]
        public void MoreTextAddsItemsWithoutMovingTheExistingOnes()
        {
            var fewer = Place(new[] { Node("a", 0, 0, 100) });
            var more = Place(new[] { Node("a", 0, 0, 190) });
            Assert.That(more.Count, Is.GreaterThan(fewer.Count));
            foreach (var item in fewer) Assert.That(more.Any(m => m.Kind == item.Kind && m.X == item.X && m.Y == item.Y), Is.True);
        }

        [Test]
        public void ThePerNodeAndTotalLimitsAreRespected()
        {
            Assert.That(Place(new[] { Node("a", 0, 0, 100000) }).Count, Is.LessThanOrEqualTo(Settings.MaxPerNode));
            var nodes = Enumerable.Range(0, 60).Select(i => Node("n" + i.ToString("D2"), i * 12, 0, 2000)).ToArray();
            var capped = new NatureSettings { TotalCap = 100 };
            var items = Place(nodes, null, null, capped);
            Assert.That(items.Count, Is.LessThanOrEqualTo(100));
            Assert.That(items.Select(i => i.X / 12).Distinct().Count(), Is.GreaterThan(30), "every node gets a fair share");
        }

        [Test]
        public void LargeTreesStayAwayFromTheCenterAndLevelThreeNeverGrowsTrees()
        {
            var forest = Place(new[] { Node("a", 0, 0, 3000, 1) });
            var large = forest.Where(i => i.IsLargeTree).ToArray();
            Assert.That(large, Is.Not.Empty);
            foreach (var item in large) Assert.That(Math.Max(Math.Abs(item.X), Math.Abs(item.Y)), Is.GreaterThanOrEqualTo(Settings.LargeTreeDistance));
            var shrubs = Place(new[] { Node("a", 0, 0, 3000, 3) });
            Assert.That(shrubs.Any(i => i.IsLargeTree || i.Kind == NatureKind.PineSmall || i.Kind == NatureKind.RoundSmall), Is.False);
        }

        [Test]
        public void TownDecidesTheTreeFamily()
        {
            string odd = null, even = null;
            for (int i = 0; i < 40 && (odd == null || even == null); i++)
            {
                string town = "町" + i;
                if (MapLayout.StableHash(town) % 2 == 0) even = even ?? town; else odd = odd ?? town;
            }
            Assert.That(odd, Is.Not.Null); Assert.That(even, Is.Not.Null);
            bool Round(NatureKind k) => k == NatureKind.RoundSmall || k == NatureKind.RoundLarge;
            bool Pine(NatureKind k) => k == NatureKind.PineSmall || k == NatureKind.PineLarge;
            var round = Place(new[] { Node("a", 0, 0, 3000, 2, even) }).Select(i => i.Kind).ToArray();
            var pine = Place(new[] { Node("a", 0, 0, 3000, 2, odd) }).Select(i => i.Kind).ToArray();
            Assert.That(round.Any(Round) && !round.Any(Pine), Is.True);
            Assert.That(pine.Any(Pine) && !pine.Any(Round), Is.True);
        }

        [Test]
        public void RoadKindsDecideWhatStandsBesideThePath()
        {
            // 土地の半径 2 の拠点の、東の端(2,0)に道が出る。その隣の(2,0)が道の脇になる。
            var nodes = new[] { Node("a", 0, 0, 150), Node("far", 50, 0, 0) };
            MapOccupancy Occupancy() => MapOccupancy.ForNodes(nodes.Select(n => n.Cell), new[] { MapCell.Key(3, 0) });
            NatureKind Beside(RoadKind kind, string from, string to)
                => Place(nodes, new[] { new MapRoad(from, to, kind) }, Occupancy()).Single(i => i.X == 2 && i.Y == 0).Kind;
            Assert.That(Beside(RoadKind.Premise, "far", "a"), Is.EqualTo(NatureKind.PineSmall));
            Assert.That(Beside(RoadKind.Contrast, "a", "far"), Is.EqualTo(NatureKind.Rock));
            Assert.That(Beside(RoadKind.Unspecified, "a", "far"), Is.EqualTo(NatureKind.Bush));
            // 前提の道は、始点の側には松を置かない。
            Assert.That(Beside(RoadKind.Premise, "a", "far"), Is.EqualTo(NatureKind.Bush));
            Assert.That(new[] { NatureKind.MushroomRed, NatureKind.Sprout }, Does.Contain(Beside(RoadKind.Example, "a", "far")));
        }

        [Test]
        public void BrokenSettingsAreNormalized()
        {
            var broken = new NatureSettings { CharsPerItem = 0, MaxPerNode = -1, TotalCap = -5, BushFrom = 900, TreeFrom = 1, ForestFrom = 0, LargeTreeDistance = 0, HideBelowScale = float.NaN };
            var s = broken.Normalized();
            Assert.That(s.CharsPerItem, Is.EqualTo(1));
            Assert.That(s.TreeFrom, Is.GreaterThanOrEqualTo(s.BushFrom));
            Assert.That(s.ForestFrom, Is.GreaterThanOrEqualTo(s.TreeFrom));
            Assert.That(Place(new[] { Node("a", 0, 0, 3000) }, null, null, broken), Is.Empty);
        }
    }
}
