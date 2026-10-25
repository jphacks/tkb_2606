using System.Collections.Generic;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapLayoutFileTests
    {
        private static Dictionary<string, MapPoint> Layout() => new Dictionary<string, MapPoint>
        {
            { "町/A#見出し", new MapPoint(3 * MapLayout.Tile, -4 * MapLayout.Tile) },
            { "B#", new MapPoint(0, 12 * MapLayout.Tile) },
        };

        [Test]
        public void RoundTripKeepsIdsAndTilePositions()
        {
            var parsed = MapLayoutFile.Parse(MapLayoutFile.Serialize(Layout()));
            Assert.That(MapLayoutFile.Same(parsed, Layout()), Is.True);
        }

        [Test]
        public void SerializationIsOrderedAndStable()
        {
            var reversed = new Dictionary<string, MapPoint>();
            foreach (var pair in Layout()) reversed[pair.Key] = pair.Value;
            Assert.That(MapLayoutFile.Serialize(reversed), Is.EqualTo(MapLayoutFile.Serialize(Layout())));
            Assert.That(MapLayoutFile.Serialize(Layout()), Does.StartWith(MapLayoutFile.Header + "\n"));
        }

        [Test]
        public void IdsMayContainTabsAndSpaces()
        {
            var layout = new Dictionary<string, MapPoint> { { "a\tb c#d\te", new MapPoint(32, 64) } };
            Assert.That(MapLayoutFile.Parse(MapLayoutFile.Serialize(layout)).ContainsKey("a\tb c#d\te"), Is.True);
        }

        [Test]
        public void BrokenLinesAreSkippedAndWrongVersionIsIgnored()
        {
            string text = MapLayoutFile.Header + "\r\n1\t2\tok\r\nbad\r\n1\tx\tbad2\r\n\t\t\r\n7\t8\t\r\n-3\t4\tnegative\r\n";
            var parsed = MapLayoutFile.Parse(text);
            Assert.That(parsed.Count, Is.EqualTo(2));
            Assert.That(parsed["negative"].X, Is.EqualTo(-3 * MapLayout.Tile));
            Assert.That(MapLayoutFile.Parse("other 9\n1\t2\tok\n").Count, Is.EqualTo(0));
            Assert.That(MapLayoutFile.Parse(null).Count, Is.EqualTo(0));
            Assert.That(MapLayoutFile.Parse("").Count, Is.EqualTo(0));
        }

        [Test]
        public void UnsavablePositionsAndIdsAreDropped()
        {
            var layout = new Dictionary<string, MapPoint>
            {
                { "ok", new MapPoint(32, 32) }, { "", new MapPoint(0, 0) }, { "multi\nline", new MapPoint(0, 0) },
                { "nan", new MapPoint(float.NaN, 0) },
            };
            var parsed = MapLayoutFile.Parse(MapLayoutFile.Serialize(layout));
            Assert.That(parsed.Keys, Is.EquivalentTo(new[] { "ok" }));
        }

        [Test]
        public void SameDetectsAnyDifference()
        {
            var other = Layout(); other["B#"] = new MapPoint(32, 12 * MapLayout.Tile);
            Assert.That(MapLayoutFile.Same(Layout(), other), Is.False);
            other = Layout(); other.Remove("B#");
            Assert.That(MapLayoutFile.Same(Layout(), other), Is.False);
            Assert.That(MapLayoutFile.Same(Layout(), Layout()), Is.True);
        }
    }
}
