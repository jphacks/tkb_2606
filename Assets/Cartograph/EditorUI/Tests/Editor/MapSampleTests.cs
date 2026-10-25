using System;
using System.Linq;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapSampleTests
    {
        [Test]
        public void SampleBuildsAndHasSomeScale()
        {
            MapSnapshot snapshot = null;
            Assert.DoesNotThrow(() => snapshot = MapSample.Build());
            Assert.That(snapshot.Nodes.Count, Is.InRange(25, 35));
            Assert.That(snapshot.Nodes.Select(n => n.File).Distinct().Count(), Is.InRange(12, 16));
        }

        [Test]
        public void SampleCoversEveryRoadMarkAndEmotion()
        {
            var s = MapSample.Build();
            foreach (RoadKind kind in Enum.GetValues(typeof(RoadKind))) Assert.IsTrue(s.Roads.Any(r => r.Kind == kind), "road " + kind);
            Assert.IsTrue(s.Roads.Any(r => r.Auto));
            foreach (MarkKind kind in Enum.GetValues(typeof(MarkKind))) Assert.IsTrue(s.Marks.Any(m => m.Kind == kind), "mark " + kind);
            foreach (Emotion e in Enum.GetValues(typeof(Emotion))) Assert.IsTrue(s.Flowers.Any(f => f.Emotion == e), "emotion " + e);
            Assert.IsTrue(s.Flowers.Any(f => f.Withered));
            Assert.IsNotEmpty(s.Fogs);
            Assert.IsNotEmpty(s.Candidates);
        }

        [Test]
        public void SampleCoversTownsLevelsLandSizesAndSharedLand()
        {
            var s = MapSample.Build();
            Assert.That(s.Nodes.Select(n => n.Town).Distinct().Count(), Is.GreaterThanOrEqualTo(3));
            for (int level = 1; level <= 3; level++) Assert.IsTrue(s.Nodes.Any(n => n.Level == level), "level " + level);
            Assert.IsTrue(s.Nodes.Any(n => n.TypedChars == 0));
            Assert.IsTrue(s.Nodes.Any(n => n.TypedChars > 0 && n.TypedChars < 50));
            Assert.IsTrue(s.Nodes.Any(n => n.TypedChars >= 900));
            Assert.IsTrue(s.Nodes.Any(n => n.TypedChars >= 400 && n.TypedChars < 900));
            Assert.IsTrue(s.Nodes.Any(n => n.TypedChars >= 200 && n.TypedChars < 400));
            Assert.IsTrue(s.Nodes.GroupBy(n => n.File).Any(g => g.Count() >= 3));
            Assert.IsTrue(s.Nodes.Any(n => n.Isolated));
        }

        [Test]
        public void SampleCoversEveryBuildingShapeAndTowerWidth()
        {
            var s = MapSample.Build();
            var sizes = s.Buildings.Select(b => BuildingSizing.Measure(b, null)).ToList();
            foreach (BuildingShape shape in Enum.GetValues(typeof(BuildingShape))) Assert.IsTrue(sizes.Any(z => z.Shape == shape), "shape " + shape);
            for (int width = 1; width <= 3; width++) Assert.IsTrue(sizes.Any(z => z.Shape == BuildingShape.Tower && z.Width == width), "tower width " + width);
            Assert.That(sizes.Where(z => z.Shape == BuildingShape.Tower).Select(z => z.Floors).Distinct().Count(), Is.GreaterThan(1));
        }
    }
}
