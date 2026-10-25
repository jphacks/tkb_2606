using System.Collections.Generic;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapHoverTests
    {
        [Test]
        public void MarkTextsAreDistinctAndNonEmpty()
        {
            var seen = new HashSet<string>();
            foreach (MarkKind kind in System.Enum.GetValues(typeof(MarkKind)))
            { Assert.That(MapHover.MarkText(kind), Is.Not.Empty); Assert.That(seen.Add(MapHover.MarkText(kind))); }
        }

        [Test]
        public void EmotionWordsMapToFourKanji()
        {
            Assert.That(MapHover.EmotionWord(Emotion.Joy), Is.EqualTo("喜"));
            Assert.That(MapHover.EmotionWord(Emotion.Anger), Is.EqualTo("怒"));
            Assert.That(MapHover.EmotionWord(Emotion.Sadness), Is.EqualTo("哀"));
            Assert.That(MapHover.EmotionWord(Emotion.Delight), Is.EqualTo("楽"));
            Assert.That(MapHover.FlowerText(Emotion.Joy, true, 3), Does.Contain("枯れた花"));
            Assert.That(MapHover.FlowerText(Emotion.Joy, false, 3), Does.Contain("3行目"));
        }

        [Test]
        public void FogTextIncludesLineWhenKnown()
        {
            Assert.That(MapHover.FogText(12), Does.Contain("12行目"));
            Assert.That(MapHover.FogText(0), Does.Not.Contain("行目に"));
        }

        [Test]
        public void FlagTextHasTitleFileAndLevel()
        {
            var text = MapHover.FlagText(new MapNode("n", "町/a.md", "見出し", 1, 5, false, 2));
            Assert.That(text, Does.Contain("見出し").And.Contain("町/a.md").And.Contain("2"));
        }

        [Test]
        public void RoadTextOnlyForKindedRoads()
        {
            Assert.That(MapHover.RoadText(RoadKind.Unspecified), Is.Null);
            Assert.That(MapHover.RoadText(RoadKind.Premise), Does.Contain("前提"));
            Assert.That(MapHover.RoadText(RoadKind.Contrast), Does.Contain("対比"));
            Assert.That(MapHover.RoadText(RoadKind.Example), Does.Contain("具体例"));
        }

        [Test]
        public void PickPrefersHigherPriorityOverLowerWhenOverlapping()
        {
            var targets = new List<HoverTarget>
            {
                HoverTarget.Rect(HoverTier.Fog, "fog", 0, 0, 100, 100),
                HoverTarget.Segment(HoverTier.Candidate, "cand", 0, 50, 100, 50, 8),
                HoverTarget.Rect(HoverTier.Flag, "flag", 40, 40, 60, 60),
                HoverTarget.Rect(HoverTier.Flower, "flower", 45, 45, 55, 55),
                HoverTarget.Rect(HoverTier.Building, "building", 30, 30, 70, 70),
                HoverTarget.Around(HoverTier.Mark, "mark", 50, 50, 5),
            };
            Assert.That(MapHover.Pick(50, 50, targets), Is.EqualTo("mark"));
            Assert.That(MapHover.Pick(58, 50, targets), Is.EqualTo("building"));
            Assert.That(MapHover.Pick(35, 35, targets), Is.EqualTo("building"));
            Assert.That(MapHover.Pick(20, 20, targets), Is.EqualTo("fog"));
            Assert.That(MapHover.Pick(20, 53, targets), Is.EqualTo("fog"));
            Assert.That(MapHover.Pick(200, 200, targets), Is.Null);
        }

        [Test]
        public void PickOrdersFlowerFlagFogCandidate()
        {
            var order = new List<HoverTarget>
            {
                HoverTarget.Segment(HoverTier.Candidate, "cand", 0, 10, 100, 10, 8),
                HoverTarget.Rect(HoverTier.Fog, "fog", 0, 0, 100, 20),
                HoverTarget.Rect(HoverTier.Flag, "flag", 0, 0, 100, 20),
                HoverTarget.Rect(HoverTier.Flower, "flower", 0, 0, 100, 20),
            };
            Assert.That(MapHover.Pick(10, 10, order), Is.EqualTo("flower"));
            order.RemoveAt(3); Assert.That(MapHover.Pick(10, 10, order), Is.EqualTo("flag"));
            order.RemoveAt(2); Assert.That(MapHover.Pick(10, 10, order), Is.EqualTo("fog"));
            order.RemoveAt(1); Assert.That(MapHover.Pick(10, 10, order), Is.EqualTo("cand"));
            Assert.That(MapHover.Pick(10, 30, order), Is.Null);
        }

        [Test]
        public void PickChoosesFrontBuildingAndNearestSameTier()
        {
            var buildings = new List<HoverTarget>
            {
                HoverTarget.Rect(HoverTier.Building, "back", 0, 0, 50, 50),
                HoverTarget.Rect(HoverTier.Building, "front", 0, 20, 50, 80),
            };
            Assert.That(MapHover.Pick(10, 30, buildings), Is.EqualTo("front"));
            var flowers = new List<HoverTarget>
            {
                HoverTarget.Rect(HoverTier.Flower, "far", 0, 0, 40, 40),
                HoverTarget.Rect(HoverTier.Flower, "near", 20, 20, 60, 60),
            };
            Assert.That(MapHover.Pick(40, 40, flowers), Is.EqualTo("near"));
            Assert.That(MapHover.Pick(1, 1, null), Is.Null);
        }

        [Test]
        public void SegmentDistanceClampsToEnds()
        {
            Assert.That(MapHover.DistanceToSegment(5, 3, 0, 0, 10, 0), Is.EqualTo(3f).Within(0.001f));
            Assert.That(MapHover.DistanceToSegment(-3, 4, 0, 0, 10, 0), Is.EqualTo(5f).Within(0.001f));
            Assert.That(MapHover.DistanceToSegment(1, 1, 2, 2, 2, 2), Is.EqualTo(1.4142f).Within(0.001f));
        }

        [Test]
        public void PlaceLabelStaysInsideArea()
        {
            MapHover.PlaceLabel(10, 10, 100, 20, 400, 300, out float left, out float top);
            Assert.That(left, Is.EqualTo(24f)); Assert.That(top, Is.EqualTo(24f));
            MapHover.PlaceLabel(390, 290, 100, 20, 400, 300, out left, out top);
            Assert.That(left + 100, Is.LessThanOrEqualTo(396f)); Assert.That(top + 20, Is.LessThanOrEqualTo(296f));
            Assert.That(left, Is.EqualTo(276f)); Assert.That(top, Is.EqualTo(256f));
            MapHover.PlaceLabel(5, 5, 500, 20, 300, 200, out left, out top);
            Assert.That(left, Is.GreaterThanOrEqualTo(4f)); Assert.That(top, Is.GreaterThanOrEqualTo(4f));
        }
    }
}
