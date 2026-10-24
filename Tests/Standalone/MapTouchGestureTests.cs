using Cartowords.MapPresentation;
using NUnit.Framework;

namespace Cartograph.IntegrationTests
{
    public class MapTouchGestureTests
    {
        [Test]
        public void PinchScalesAroundMovingMidpointAndDoesNotTap()
        {
            var viewport = new MapViewport(); var gesture = new MapTouchGesture();
            var world = viewport.World(new MapPoint(50, 0));
            gesture.Down(4, new MapPoint(0, 0)); gesture.Down(7, new MapPoint(100, 0));
            gesture.Move(7, new MapPoint(200, 0), viewport);
            Assert.That(viewport.Scale, Is.EqualTo(2).Within(.001));
            Assert.That(viewport.Screen(world).X, Is.EqualTo(100).Within(.001));
            Assert.That(gesture.Up(4), Is.False); Assert.That(gesture.Up(7), Is.False);
        }
        [Test]
        public void TwoFingersPanAndOneFingerCanContinueWithoutJump()
        {
            var viewport = new MapViewport(); var gesture = new MapTouchGesture();
            gesture.Down(1, new MapPoint(0, 0)); gesture.Down(2, new MapPoint(100, 0));
            gesture.Move(1, new MapPoint(20, 10), viewport); gesture.Move(2, new MapPoint(120, 10), viewport);
            Assert.That(viewport.Scale, Is.EqualTo(1).Within(.001));
            Assert.That(viewport.OffsetX, Is.EqualTo(20).Within(.001)); Assert.That(viewport.OffsetY, Is.EqualTo(10).Within(.001));
            gesture.Up(1); gesture.Move(2, new MapPoint(125, 15), viewport);
            Assert.That(viewport.OffsetX, Is.EqualTo(25).Within(.001)); Assert.That(gesture.Up(2), Is.False);
        }
        [Test]
        public void TapDragCancelAndDetachAreDistinct()
        {
            var viewport = new MapViewport(); var gesture = new MapTouchGesture();
            gesture.Down(1, new MapPoint(0, 0)); Assert.That(gesture.Up(1), Is.True);
            gesture.Down(1, new MapPoint(0, 0)); gesture.Move(1, new MapPoint(20, 0), viewport);
            Assert.That(gesture.Up(1), Is.False);
            gesture.Down(1, new MapPoint(0, 0)); Assert.That(gesture.Up(1, true), Is.False);
            gesture.Down(1, new MapPoint(0, 0)); gesture.Clear();
            Assert.That(gesture.Move(1, new MapPoint(99, 0), viewport), Is.False); Assert.That(gesture.Count, Is.EqualTo(0));
        }
        [Test]
        public void CoincidentFingersStayFiniteAndZoomClamps()
        {
            var viewport = new MapViewport(); var gesture = new MapTouchGesture();
            gesture.Down(1, new MapPoint(0, 0)); gesture.Down(2, new MapPoint(0, 0));
            gesture.Move(2, new MapPoint(100, 0), viewport);
            gesture.Move(2, new MapPoint(100000, 0), viewport);
            Assert.That(viewport.Scale, Is.EqualTo(3)); Assert.That(float.IsNaN(viewport.OffsetX), Is.False);
            gesture.Move(2, new MapPoint(2, 0), viewport); Assert.That(viewport.Scale, Is.EqualTo(.12f).Within(.001));
        }
    }
}
