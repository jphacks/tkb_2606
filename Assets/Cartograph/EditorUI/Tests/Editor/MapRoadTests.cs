using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapRoadTests
    {
        private static HashSet<long> Cells(int x0, int y0, int x1, int y1)
        {
            var set = new HashSet<long>();
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) set.Add(MapCell.Key(x, y));
            return set;
        }
        private static HashSet<long> Union(params HashSet<long>[] sets)
        {
            var all = new HashSet<long>();
            foreach (var s in sets) all.UnionWith(s);
            return all;
        }
        private static RoadSpec Spec(string from, HashSet<long> a, string to, HashSet<long> b)
            => new RoadSpec { From = from, To = to, FromRegion = a, ToRegion = b };

        [Test]
        public void CellOfMapsSubCellsToTheirTile()
        {
            Assert.That(MapRoads.CellOf(MapRoads.Sub(0, 1)), Is.EqualTo(MapCell.Key(0, 0)));
            Assert.That(MapRoads.CellOf(MapRoads.Sub(2, 3)), Is.EqualTo(MapCell.Key(1, 1)));
            Assert.That(MapRoads.CellOf(MapRoads.Sub(-1, -2)), Is.EqualTo(MapCell.Key(-1, -1)));
            Assert.That(MapRoads.CellOf(MapRoads.Sub(-3, -4)), Is.EqualTo(MapCell.Key(-2, -2)));
        }

        [Test]
        public void RoadNeverTouchesLandAndLeavesFromTheRegionEdge()
        {
            var a = Cells(0, 0, 0, 0); var b = Cells(10, 0, 10, 0);
            var wall = Cells(5, -1, 5, 1);   // 間にある別の土地を、道は迂回する
            var land = Union(a, b, wall);
            var layout = MapRoads.Build(land, new[] { Spec("a", a, "b", b) });
            Assert.That(layout.Routes.Count, Is.EqualTo(1));
            Assert.That(layout.Mask.Keys.All(sub => !land.Contains(MapRoads.CellOf(sub))), Is.True);
            Assert.That(layout.TouchedCells().All(c => !land.Contains(c)), Is.True);
            var route = layout.Routes[0];
            // 始点と終点は、それぞれの領域に接し、その向きの口が開いている。
            int[] dx = { 0, 1, 0, -1 }, dy = { -1, 0, 1, 0 };
            long first = route.Cells[0], last = route.Cells[route.Cells.Count - 1];
            bool Opens(long cell, int mask, HashSet<long> region)
            {
                for (int d = 0; d < 4; d++)
                    if ((mask & (1 << d)) != 0 && region.Contains(MapRoads.CellOf(MapRoads.Sub(MapCell.X(cell) + dx[d], MapCell.Y(cell) + dy[d])))) return true;
                return false;
            }
            Assert.That(Opens(first, route.Masks[0], a), Is.True);
            Assert.That(Opens(last, route.Masks[route.Masks.Count - 1], b), Is.True);
            // 隣り合うサブマスだけでつながっている。
            for (int i = 1; i < route.Cells.Count; i++)
                Assert.That(System.Math.Abs(MapCell.X(route.Cells[i]) - MapCell.X(route.Cells[i - 1])) +
                            System.Math.Abs(MapCell.Y(route.Cells[i]) - MapCell.Y(route.Cells[i - 1])), Is.EqualTo(1));
        }

        [Test]
        public void RoutingIsDeterministic()
        {
            var a = Cells(0, 0, 1, 1); var b = Cells(9, 7, 10, 8); var c = Cells(2, 9, 3, 10);
            var land = Union(a, b, c, Cells(5, 2, 6, 6));
            var specs = new[] { Spec("a", a, "b", b), Spec("b", b, "c", c), Spec("a", a, "c", c) };
            var one = MapRoads.Build(land, specs); var two = MapRoads.Build(land, specs);
            Assert.That(one.Routes.Count, Is.EqualTo(3));
            for (int i = 0; i < one.Routes.Count; i++)
            {
                Assert.That(two.Routes[i].Cells, Is.EqualTo(one.Routes[i].Cells));
                Assert.That(two.Routes[i].Masks, Is.EqualTo(one.Routes[i].Masks));
            }
            Assert.That(two.Mask.OrderBy(p => p.Key).ToList(), Is.EqualTo(one.Mask.OrderBy(p => p.Key).ToList()));
        }

        [Test]
        public void PerpendicularStraightRoadsCrossWithTheLowerIndexOnTop()
        {
            var a = Cells(0, 0, 0, 0); var b = Cells(10, 0, 10, 0);
            var c = Cells(5, -6, 5, -6); var d = Cells(5, 6, 5, 6);
            var layout = MapRoads.Build(Union(a, b, c, d), new[] { Spec("a", a, "b", b), Spec("c", c, "d", d) });
            Assert.That(layout.Routes.Count, Is.EqualTo(2));
            Assert.That(layout.Crossings.Count, Is.EqualTo(1));
            var crossing = layout.Crossings[0];
            Assert.That(crossing.Over, Is.EqualTo(0));
            Assert.That(crossing.Under, Is.EqualTo(1));
            Assert.That(crossing.OverVertical, Is.False);
            Assert.That(layout.Mask[crossing.Cell], Is.EqualTo(10));   // 上の道(横)だけを描く
            Assert.That(layout.Owner[crossing.Cell], Is.EqualTo(0));
        }

        [Test]
        public void CrossingRuleExcludesEndsSharedNodesAndNonStraightCells()
        {
            Assert.That(MapRoads.IsCrossing(5, false, 10, false, false), Is.True);
            Assert.That(MapRoads.IsCrossing(10, false, 5, false, false), Is.True);
            Assert.That(MapRoads.IsCrossing(5, true, 10, false, false), Is.False);    // 道の端(領域の縁)
            Assert.That(MapRoads.IsCrossing(5, false, 10, true, false), Is.False);
            Assert.That(MapRoads.IsCrossing(5, false, 10, false, true), Is.False);    // 同じ拠点につながる道
            Assert.That(MapRoads.IsCrossing(5, false, 5, false, false), Is.False);    // 平行な合流
            Assert.That(MapRoads.IsCrossing(5, false, 6, false, false), Is.False);    // 曲がり角
            Assert.That(MapRoads.IsCrossing(7, false, 10, false, false), Is.False);   // T 字
        }

        [Test]
        public void RoadsSharingTheSameEndsMergeIntoAJunctionWithoutACrossing()
        {
            var a = Cells(0, 0, 0, 0); var b = Cells(10, 0, 10, 0); var c = Cells(0, 8, 0, 8);
            var land = Union(a, b, c);
            var layout = MapRoads.Build(land, new[] { Spec("a", a, "b", b), Spec("a", a, "b", b), Spec("a", a, "c", c) });
            Assert.That(layout.Routes.Count, Is.EqualTo(3));
            Assert.That(layout.Crossings, Is.Empty);
            // 同じ道を 2 本重ねても、描くマスクは 1 本分と同じ(足しても増えない)。
            var single = MapRoads.Build(land, new[] { Spec("a", a, "b", b) });
            foreach (long sub in single.Routes[0].Cells) Assert.That(layout.Mask[sub] & single.Mask[sub], Is.EqualTo(single.Mask[sub]));
            Assert.That(layout.Owner[single.Routes[0].Cells[3]], Is.EqualTo(0));
        }

        [Test]
        public void AdjacentLandsGetNoRoad()
        {
            var a = Cells(0, 0, 0, 0); var b = Cells(1, 0, 1, 0);
            Assert.That(MapRoads.Build(Union(a, b), new[] { Spec("a", a, "b", b) }).Routes, Is.Empty);
            var overlapping = Cells(0, 0, 1, 0);
            Assert.That(MapRoads.Build(Union(a, overlapping), new[] { Spec("a", a, "b", overlapping) }).Routes, Is.Empty);
        }

        [Test]
        public void WalledInDestinationIsSkipped()
        {
            var a = Cells(0, 0, 0, 0); var b = Cells(10, 0, 10, 0);
            var ring = Cells(9, -1, 11, 1); ring.Remove(MapCell.Key(10, 0));
            var layout = MapRoads.Build(Union(a, b, ring), new[] { Spec("a", a, "b", b) });
            Assert.That(layout.Routes, Is.Empty);
            Assert.That(layout.Mask, Is.Empty);
        }
    }
}
