using System.Collections.Generic;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapCharacterTests
    {
        private static HashSet<long> Block(int x0, int y0, int w, int h)
        {
            var set = new HashSet<long>();
            for (int x = x0; x < x0 + w; x++) for (int y = y0; y < y0 + h; y++) set.Add(MapCell.Key(x, y));
            return set;
        }
        private static MapNpc Make(string file, HashSet<long> land, MapOccupancy occupancy = null, ICollection<long> blocked = null)
            => MapNpc.BuildOne(file, land, occupancy, blocked);
        private static string Describe(MapNpc npc, double t) { var p = npc.At(t); return p.X + "," + p.Y + "," + p.Facing + "," + p.Frame; }

        [Test]
        public void KindIsStableAndInRange()
        {
            Assert.That(MapNpc.KindOf("町/a.md"), Is.EqualTo(MapNpc.KindOf("町/a.md")));
            Assert.That(MapNpc.KindOf("町/a.md"), Is.EqualTo(MapLayout.StableHash("町/a.md") % 8));
            foreach (var name in new[] { "a", "b", "町/c.md", "z.md", "long/path/note.md" })
                Assert.That(MapNpc.KindOf(name), Is.InRange(0, 7));
            Assert.That(Make("町/a.md", Block(0, 0, 8, 8)).Kind, Is.EqualTo(MapNpc.KindOf("町/a.md")));
        }

        [Test]
        public void SameInputGivesSamePoseAtAnyTime()
        {
            var a = Make("n.md", Block(0, 0, 10, 10)); var b = Make("n.md", Block(0, 0, 10, 10));
            for (double t = 0; t < 120; t += 0.37) Assert.That(Describe(a, t), Is.EqualTo(Describe(b, t)));
            Assert.That(Describe(a, 5.5), Is.EqualTo(Describe(a, 5.5)));
        }

        [Test]
        public void BuildOrderOfFilesDoesNotMatter()
        {
            var land = new Dictionary<string, HashSet<long>> { ["a.md"] = Block(0, 0, 8, 8), ["b.md"] = Block(20, 0, 8, 8) };
            var reversed = new Dictionary<string, HashSet<long>> { ["b.md"] = Block(20, 0, 8, 8), ["a.md"] = Block(0, 0, 8, 8) };
            Assert.That(MapNpc.Build(reversed, null).Select(n => n.File + Describe(n, 3)), Is.EqualTo(MapNpc.Build(land, null).Select(n => n.File + Describe(n, 3))));
        }

        [Test]
        public void NpcStaysOnFreeLandCells()
        {
            var land = Block(0, 0, 12, 12);
            var occupancy = new MapOccupancy();
            occupancy.ReserveAround(MapCell.Key(5, 5), 1, OccupyKind.Flag);
            var blocked = new HashSet<long> { MapCell.Key(2, 2), MapCell.Key(8, 3), MapCell.Key(3, 9) };
            var npc = Make("walker.md", land, occupancy, blocked);
            Assert.That(npc.Wanders, Is.True);
            for (double t = 0; t < 300; t += 0.05)
            {
                var p = npc.At(t);
                // 位置は軸に沿って歩くので、必ずどちらかの座標が整数で、足元のマスは歩ける所。
                Assert.That(p.X == System.Math.Round(p.X) || p.Y == System.Math.Round(p.Y), Is.True);
                int cx = (int)System.Math.Round(p.X), cy = (int)System.Math.Round(p.Y);
                foreach (var c in new[] { (cx, cy), ((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y)), ((int)System.Math.Ceiling(p.X), (int)System.Math.Ceiling(p.Y)) })
                {
                    long key = MapCell.Key(c.Item1, c.Item2);
                    Assert.That(land.Contains(key), Is.True, "off land at " + t);
                    Assert.That(occupancy.IsFree(c.Item1, c.Item2), Is.True, "occupied at " + t);
                    Assert.That(blocked.Contains(key), Is.False, "blocked at " + t);
                }
            }
        }

        [Test]
        public void WaypointsAreFreeLandCells()
        {
            var land = Block(0, 0, 9, 9);
            var occupancy = new MapOccupancy();
            occupancy.ReserveAround(MapCell.Key(4, 4), 1, OccupyKind.Flag);
            var npc = Make("w.md", land, occupancy);
            Assert.That(npc.Waypoints.Count, Is.GreaterThan(1));
            foreach (long w in npc.Waypoints)
            {
                Assert.That(land.Contains(w), Is.True);
                Assert.That(occupancy.IsFree(MapCell.X(w), MapCell.Y(w)), Is.True);
            }
        }

        [Test]
        public void FacingFollowsMovementAndPausesFaceDown()
        {
            var npc = Make("face.md", Block(0, 0, 10, 10));
            var prev = npc.At(0); bool sawMove = false;
            for (double t = 0.02; t < 120; t += 0.02)
            {
                var p = npc.At(t);
                if (!p.Moving)
                {
                    Assert.That(p.Facing, Is.EqualTo(NpcFacing.Down)); Assert.That(p.Frame, Is.EqualTo(0));
                }
                else
                {
                    float dx = p.X - prev.X, dy = p.Y - prev.Y;
                    if (prev.Moving && (System.Math.Abs(dx) > 1e-4 || System.Math.Abs(dy) > 1e-4) && (System.Math.Abs(dx) < 1e-5 || System.Math.Abs(dy) < 1e-5) && System.Math.Abs(dx) < 0.5f && System.Math.Abs(dy) < 0.5f)
                    {
                        sawMove = true;
                        var expected = System.Math.Abs(dx) > System.Math.Abs(dy) ? (dx > 0 ? NpcFacing.Right : NpcFacing.Left) : (dy > 0 ? NpcFacing.Down : NpcFacing.Up);
                        Assert.That(p.Facing, Is.EqualTo(expected), "at " + t);
                    }
                    Assert.That(p.Frame, Is.InRange(0, MapNpc.WalkFrames - 1));
                }
                prev = p;
            }
            Assert.That(sawMove, Is.True);
        }

        [Test]
        public void WalkSpeedAndPauseLengthsMatchSpec()
        {
            var npc = Make("speed.md", Block(0, 0, 10, 10));
            double step = 0.01, pauseRun = 0; bool wasMoving = true, seenMove = false; float maxSpeed = 0; var prev = npc.At(0);
            double minPause = double.MaxValue, maxPause = 0;
            for (double t = step; t < 200; t += step)
            {
                var p = npc.At(t);
                if (p.Moving)
                {
                    if (!wasMoving && seenMove && pauseRun > 0) { minPause = System.Math.Min(minPause, pauseRun); maxPause = System.Math.Max(maxPause, pauseRun); }
                    pauseRun = 0; seenMove = true;
                    float d = System.Math.Abs(p.X - prev.X) + System.Math.Abs(p.Y - prev.Y);
                    if (prev.Moving) maxSpeed = System.Math.Max(maxSpeed, (float)(d / step));
                }
                else pauseRun += step;
                wasMoving = p.Moving; prev = p;
            }
            Assert.That(maxSpeed, Is.EqualTo(MapNpc.TilesPerSecond).Within(0.1f));
            Assert.That(minPause, Is.GreaterThanOrEqualTo(0.95));
            Assert.That(maxPause, Is.LessThanOrEqualTo(3.05));
        }

        [Test]
        public void WalkFrameAdvancesWhileMoving()
        {
            var npc = Make("frames.md", Block(0, 0, 10, 10));
            var seen = new HashSet<int>();
            for (double t = 0; t < 120; t += 0.01) { var p = npc.At(t); if (p.Moving) seen.Add(p.Frame); }
            Assert.That(seen.Count, Is.EqualTo(MapNpc.WalkFrames));
        }

        [Test]
        public void DisconnectedOrTinyLandStandsStill()
        {
            Assert.That(Make("none.md", new HashSet<long>()), Is.Null);
            var single = Make("one.md", Block(3, 3, 1, 1));
            Assert.That(single.Wanders, Is.False);
            for (double t = 0; t < 30; t += 1.7) { var p = single.At(t); Assert.That((p.X, p.Y), Is.EqualTo((3f, 3f))); Assert.That(p.Moving, Is.False); }
            // 2 つの島に分かれた土地では、最初の経由点と同じ島の中だけを歩く。
            var island = Block(0, 0, 4, 4); island.UnionWith(Block(10, 0, 4, 4));
            var npc = Make("two.md", island);
            long start = npc.Waypoints[0]; bool left = MapCell.X(start) < 5;
            for (double t = 0; t < 200; t += 0.1) { var p = npc.At(t); Assert.That(p.X < 5, Is.EqualTo(left)); }
        }

        [Test]
        public void FlagOnlyLandFallsBackToNearbyFreeCells()
        {
            var land = Block(5, 5, 1, 1);
            var occupancy = new MapOccupancy();
            occupancy.ReserveAround(MapCell.Key(5, 5), 1, OccupyKind.Flag);
            Assert.That(Make("flag.md", land, occupancy), Is.Null);
            var nearby = new HashSet<long>();
            MapNpc.AddNearby(nearby, MapCell.Key(5, 5), MapNpc.NearbyRange);
            var npc = MapNpc.BuildOne("flag.md", land, occupancy, null, nearby);
            Assert.That(npc, Is.Not.Null);
            foreach (long w in npc.Waypoints)
            {
                Assert.That(occupancy.IsFree(MapCell.X(w), MapCell.Y(w)), Is.True);
                Assert.That(nearby.Contains(w), Is.True);
            }
            for (double t = 0; t < 100; t += 0.1)
            {
                var p = npc.At(t);
                Assert.That(System.Math.Abs(p.X - 5), Is.LessThanOrEqualTo(2.001f));
                Assert.That(System.Math.Abs(p.Y - 5), Is.LessThanOrEqualTo(2.001f));
            }
        }

        [Test]
        public void BuildGivesEveryFileAnNpcAndAvoidsOtherLand()
        {
            var land = new Dictionary<string, HashSet<long>> { ["a.md"] = Block(5, 5, 1, 1), ["b.md"] = Block(7, 5, 1, 1) };
            var occupancy = new MapOccupancy();
            occupancy.ReserveAround(MapCell.Key(5, 5), 1, OccupyKind.Flag);
            occupancy.ReserveAround(MapCell.Key(7, 5), 1, OccupyKind.Flag);
            var near = new Dictionary<string, HashSet<long>> { ["a.md"] = new HashSet<long>(), ["b.md"] = new HashSet<long>() };
            MapNpc.AddNearby(near["a.md"], MapCell.Key(5, 5), MapNpc.NearbyRange);
            MapNpc.AddNearby(near["b.md"], MapCell.Key(7, 5), MapNpc.NearbyRange);
            var npcs = MapNpc.Build(land, occupancy, null, near);
            Assert.That(npcs.Count, Is.EqualTo(2));
            var again = MapNpc.Build(land, occupancy, null, near);
            Assert.That(again.Select(n => n.File + Describe(n, 4.2)), Is.EqualTo(npcs.Select(n => n.File + Describe(n, 4.2))));
            foreach (long w in npcs.First(n => n.File == "a.md").Waypoints) Assert.That(land["b.md"].Contains(w), Is.False);
        }

        [Test]
        public void EachFileGetsOneNpc()
        {
            var land = new Dictionary<string, HashSet<long>>();
            for (int i = 0; i < 12; i++) land["f" + i + ".md"] = Block(i * 10, 0, 5, 5);
            var npcs = MapNpc.Build(land, null);
            Assert.That(npcs.Count, Is.EqualTo(12));
            Assert.That(npcs.Select(n => n.File).Distinct().Count(), Is.EqualTo(12));
            Assert.That(npcs.All(n => n.Kind == MapNpc.KindOf(n.File)), Is.True);
        }
    }
}
