using System;
using System.Collections.Generic;
using System.Linq;
using Cartograph.EditorUI.Navigation;
using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.EditorUI.Tests
{
    public sealed class MapFocusTests
    {
        private static MapNode Node(string file, string title, int line, int level = 2)
            => new MapNode(file + "#" + title, file, title, line, 0, false, level);
        private static MapNode NodeId(string id, string file, int line, int level)
            => new MapNode(id, file, id, line, 0, false, level);

        // ---- カーソル位置 → 行 ----
        [Test]
        public void LineAtCountsAllNewlineStyles()
        {
            Assert.That(TextLinePosition.LineAt("a\nb\nc", 0), Is.EqualTo(1));
            Assert.That(TextLinePosition.LineAt("a\nb\nc", 2), Is.EqualTo(2));
            Assert.That(TextLinePosition.LineAt("a\r\nb", 3), Is.EqualTo(2));
            Assert.That(TextLinePosition.LineAt("a\r\nb", 2), Is.EqualTo(1), "CRLF の途中は前の行");
            Assert.That(TextLinePosition.LineAt("a\rb", 2), Is.EqualTo(2));
        }
        [Test]
        public void LineAtClampsAndInvertsOffset()
        {
            Assert.That(TextLinePosition.LineAt(null, 5), Is.EqualTo(1));
            Assert.That(TextLinePosition.LineAt("abc", -3), Is.EqualTo(1));
            Assert.That(TextLinePosition.LineAt("abc\n", 99), Is.EqualTo(2));
            const string text = "😀あ\r\n## 見出し\n本文\n\n末尾";
            for (int line = 1; line <= 5; line++)
                Assert.That(TextLinePosition.LineAt(text, TextLinePosition.Offset(text, line)), Is.EqualTo(line));
        }

        // ---- 行 → 拠点 ----
        [Test]
        public void NodeAtPicksLastHeadingAtOrBeforeTheLine()
        {
            var nodes = new[] { Node("n", "A", 1), Node("n", "B", 5), Node("n", "C", 9), Node("m", "Z", 3) };
            Assert.That(MapFocus.NodeAt(nodes, "n", 1), Is.EqualTo("n#A"));
            Assert.That(MapFocus.NodeAt(nodes, "n", 4), Is.EqualTo("n#A"));
            Assert.That(MapFocus.NodeAt(nodes, "n", 5), Is.EqualTo("n#B"));
            Assert.That(MapFocus.NodeAt(nodes, "n", 100), Is.EqualTo("n#C"));
            Assert.That(MapFocus.NodeAt(nodes, "m", 1), Is.EqualTo("m#Z"));
        }
        [Test]
        public void LinesBeforeTheFirstHeadingBelongToTheFirstNode()
        {
            var nodes = new[] { Node("n", "B", 4), Node("n", "A", 7) };
            Assert.That(MapFocus.NodeAt(nodes, "n", 1), Is.EqualTo("n#B"));
        }
        [Test]
        public void NodeAtReturnsNullWithoutNodes()
        {
            Assert.That(MapFocus.NodeAt(new[] { Node("n", "A", 1) }, "other", 1), Is.Null);
            Assert.That(MapFocus.NodeAt(new MapNode[0], "n", 1), Is.Null);
            Assert.That(MapFocus.NodeAt(null, "n", 1), Is.Null);
            Assert.That(MapFocus.NodeAt(new[] { Node("n", "A", 1) }, null, 1), Is.Null);
        }

        // ---- 自動スクロールの判定 ----
        [Test]
        public void InnerPointNeedsNoScroll()
        {
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(500, 300), 1000, 600, out _, out _), Is.False);
            // 余白 20% のちょうど内側。
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(200, 120), 1000, 600, out _, out _), Is.False);
        }
        [Test]
        public void OuterPointPansToTheCenter()
        {
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(100, 300), 1000, 600, out float dx, out float dy), Is.True);
            Assert.That(dx, Is.EqualTo(400f)); Assert.That(dy, Is.EqualTo(0f));
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(1500, -40), 1000, 600, out dx, out dy), Is.True);
            Assert.That(dx, Is.EqualTo(-1000f)); Assert.That(dy, Is.EqualTo(340f));
        }
        [Test]
        public void DegenerateViewNeverScrolls()
        {
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(-50, -50), 0, 0, out _, out _), Is.False);
            Assert.That(MapAutoScroll.TryDelta(new MapPoint(float.NaN, 0), 1000, 600, out _, out _), Is.False);
        }

        // ---- パンのなめらかな移動 ----
        [Test]
        public void PanTweenSumsToTheTotalAndFinishesOnTime()
        {
            var tween = new MapPanTween(120, -60, 0.25f);
            float sumX = 0, sumY = 0; bool done = false;
            foreach (float t in new[] { 0f, 0.05f, 0.1f, 0.2f, 0.25f, 0.3f })
            {
                done = tween.Advance(t, out float dx, out float dy); sumX += dx; sumY += dy;
                if (t < 0.25f) Assert.That(done, Is.False);
            }
            Assert.That(done, Is.True);
            Assert.That(sumX, Is.EqualTo(120f).Within(0.001f)); Assert.That(sumY, Is.EqualTo(-60f).Within(0.001f));
        }
        [Test]
        public void PanTweenStartsSlowAndIsMonotonic()
        {
            var tween = new MapPanTween(100, 0, 1f);
            tween.Advance(0.1f, out float first, out _);
            tween.Advance(0.5f, out float middle, out _);
            Assert.That(first, Is.LessThan(10f));
            Assert.That(first + middle, Is.EqualTo(50f).Within(0.001f));
        }

        // ---- マップ操作中の抑制 ----
        [Test]
        public void GateIsQuietOnlyAfterTheQuietPeriodAndWhenIdle()
        {
            var gate = new MapInputGate(3f);
            Assert.That(gate.IsQuiet(0f, false), Is.True, "まだ何も操作していない");
            gate.Touch(10f);
            Assert.That(gate.IsQuiet(11f, false), Is.False);
            Assert.That(gate.IsQuiet(12.9f, false), Is.False);
            Assert.That(gate.IsQuiet(13f, false), Is.True);
            Assert.That(gate.IsQuiet(20f, true), Is.False, "ドラッグ中は静かではない");
        }

        // ---- 見出しの改名の対応づけ ----
        [Test]
        public void RenamedLeafIsMatchedAndNothingElseChanges()
        {
            var before = new[] { Node("n", "A", 1), Node("n", "B", 5) };
            var after = new[] { Node("n", "A", 1), Node("n", "B2", 5) };
            var match = MapRename.Match(before, after);
            Assert.That(match.Count, Is.EqualTo(1));
            Assert.That(match["n#B2"], Is.EqualTo("n#B"));
        }
        [Test]
        public void RenamingAParentMatchesItsDescendantsInOrder()
        {
            var before = new[] { NodeId("n#P", "n", 1, 1), NodeId("n#P/C", "n", 3, 2), NodeId("n#P/C/D", "n", 5, 3), NodeId("n#Q", "n", 8, 1) };
            var after = new[] { NodeId("n#P2", "n", 1, 1), NodeId("n#P2/C", "n", 3, 2), NodeId("n#P2/C/D", "n", 5, 3), NodeId("n#Q", "n", 8, 1) };
            var match = MapRename.Match(before, after);
            Assert.That(match.Keys.OrderBy(k => k), Is.EqualTo(new[] { "n#P2", "n#P2/C", "n#P2/C/D" }));
            Assert.That(match["n#P2/C/D"], Is.EqualTo("n#P/C/D"));
        }
        [Test]
        public void AddedOrRemovedHeadingIsNotARename()
        {
            var one = new[] { Node("n", "A", 1), Node("n", "B", 5) };
            var two = new[] { Node("n", "A", 1), Node("n", "B", 5), Node("n", "C", 9) };
            Assert.That(MapRename.Match(one, two), Is.Empty);
            Assert.That(MapRename.Match(two, one), Is.Empty);
        }
        [Test]
        public void ChangedLevelSequenceIsNotARename()
        {
            var before = new[] { NodeId("n#A", "n", 1, 1), NodeId("n#B", "n", 4, 2) };
            var after = new[] { NodeId("n#A", "n", 1, 1), NodeId("n#B2", "n", 4, 3) };
            Assert.That(MapRename.Match(before, after), Is.Empty);
        }
        [Test]
        public void MatchingNeverCrossesFiles()
        {
            var before = new[] { Node("n", "A", 1) };
            var after = new[] { Node("m", "A", 1) };
            Assert.That(MapRename.Match(before, after), Is.Empty);
        }
        [Test]
        public void FirstHeadingAddedToAPlainNoteKeepsTheNote()
        {
            var before = new[] { NodeId("n#", "n", 1, 0) };
            var after = new[] { NodeId("n#Title", "n", 1, 1), NodeId("n#Title/Sub", "n", 4, 2) };
            var match = MapRename.Match(before, after);
            Assert.That(match.Count, Is.EqualTo(1));
            Assert.That(match["n#Title"], Is.EqualTo("n#"));
        }
        [Test]
        public void LastHeadingRemovedKeepsTheNote()
        {
            var before = new[] { NodeId("n#Title", "n", 1, 1) };
            var after = new[] { NodeId("n#", "n", 1, 0) };
            Assert.That(MapRename.Match(before, after)["n#"], Is.EqualTo("n#Title"));
        }
        [Test]
        public void UnchangedOrNullInputsMatchNothing()
        {
            var same = new[] { Node("n", "A", 1) };
            Assert.That(MapRename.Match(same, same), Is.Empty);
            Assert.That(MapRename.Match(null, same), Is.Empty);
            Assert.That(MapRename.Match(same, null), Is.Empty);
        }
    }
}
