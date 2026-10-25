using System.Collections.Generic;
using System.Diagnostics;

namespace Cartograph.MapUI
{
    // マップの性能計測用。Enabled が false の間は何もしない(Start は 0 を返し、Stop はすぐ戻る)。
    public static class MapProfile
    {
        public sealed class Entry { public int Count; public double TotalMs, MaxMs; }
        public static bool Enabled;
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;
        public static void Stop(string name, long started)
        {
            if (!Enabled || started == 0) return;
            double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (!entries.TryGetValue(name, out var entry)) entries[name] = entry = new Entry();
            entry.Count++; entry.TotalMs += ms; if (ms > entry.MaxMs) entry.MaxMs = ms;
        }
        // 時間ではなく、数を記録する(平均は 1 回あたりの数)。
        public static void Count(string name, double value)
        {
            if (!Enabled) return;
            if (!entries.TryGetValue(name, out var entry)) entries[name] = entry = new Entry();
            entry.Count++; entry.TotalMs += value; if (value > entry.MaxMs) entry.MaxMs = value;
        }
        public static void Reset() => entries.Clear();
        public static string Report()
        {
            var lines = new List<string>();
            foreach (var pair in entries)
                lines.Add(pair.Key + " n=" + pair.Value.Count + " avg=" + (pair.Value.TotalMs / pair.Value.Count).ToString("F2") + "ms max=" + pair.Value.MaxMs.ToString("F2") + "ms");
            lines.Sort(); return string.Join("\n", lines);
        }
    }
}
