using Cartograph.EditorUI.Navigation;
using NUnit.Framework;
namespace Cartograph.EditorUI.Tests
{
    public sealed class DesktopUiScaleTests
    {
        [TestCase(1024, 768)]
        [TestCase(1280, 720)]
        [TestCase(1366, 768)]
        [TestCase(1920, 1080)]
        [TestCase(2560, 1440)]
        [TestCase(3840, 2160)]
        public void DesktopSizesKeepTextReadableAndPanesWithinViewport(float width, float height)
        {
            float scale = DesktopUiScale.ForSize(width, height);
            Assert.That(18 * scale, Is.GreaterThanOrEqualTo(16));
            Assert.That(width / scale, Is.GreaterThanOrEqualTo(1024));
            Assert.That(height / scale, Is.GreaterThanOrEqualTo(600));
        }
        [Test]
        public void InvalidSizesUseSafeDefault()
        {
            Assert.That(DesktopUiScale.ForSize(0, 0), Is.EqualTo(1));
            Assert.That(DesktopUiScale.ForSize(float.NaN, 900), Is.EqualTo(1));
        }
        [Test]
        public void LargerDisplayGrowsTextWithoutScalingEveryPixelLinearly()
        {
            Assert.That(DesktopUiScale.ForSize(1920, 1080), Is.GreaterThan(DesktopUiScale.ForSize(1280, 720)));
            Assert.That(DesktopUiScale.ForSize(3840, 2160), Is.LessThan(2));
        }
    }
}
