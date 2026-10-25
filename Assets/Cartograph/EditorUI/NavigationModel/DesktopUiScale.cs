using System;

namespace Cartograph.EditorUI.Navigation
{
    /// <summary>Keep text readable while reserving room for the three desktop panes.</summary>
    public static class DesktopUiScale
    {
        public static float ForSize(float width, float height)
        {
            if (float.IsNaN(width) || float.IsNaN(height) || float.IsInfinity(width) || float.IsInfinity(height) || width <= 0 || height <= 0) return 1f;
            // Grow more slowly than the display: 1280x720 -> 1, 1920x1080 -> 1.22.
            float preferred = (float)Math.Sqrt(Math.Min(width / 1280f, height / 720f));
            float fit = Math.Min(width / 1024f, height / 600f);
            return Math.Max(0.5f, Math.Min(2f, Math.Min(preferred, fit)));
        }
    }
}