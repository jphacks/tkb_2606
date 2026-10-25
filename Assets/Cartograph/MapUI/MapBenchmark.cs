using System;
using System.Collections.Generic;
using Cartowords.MapPresentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.MapUI
{
    // 性能計測用の合成データと計測の手順。再生モードで、eval などから呼ぶ。
    public static class MapBenchmark
    {
        // 町 towns 個に、拠点 nodes 個を均等に置き、道 roads 本(町内の鎖と、町をまたぐ線)を張る。
        public static MapSnapshot Build(int nodes = 500, int roads = 1000, int towns = 25)
        {
            var rng = new System.Random(12345);
            var list = new List<MapNode>(); var edges = new List<MapRoad>(); var flowers = new List<MapFlower>();
            var marks = new List<MapMark>(); var fogs = new List<MapFog>(); var buildings = new List<MapBuilding>();
            for (int i = 0; i < nodes; i++)
            {
                int town = i % towns, note = i / towns / 4;
                int chars = rng.Next(5) == 0 ? 0 : rng.Next(40, 1500);
                list.Add(new MapNode("n" + i, "町" + town + "/ノート" + note, "見出し" + i, 1 + i / towns % 4 * 5, chars, false, 1 + i % 3));
                if (chars > 0 && i % 5 < 2) flowers.Add(new MapFlower("n" + i, 2, (Emotion)(i % 4), i % 11 == 0));
                if (i % 40 == 0) fogs.Add(new MapFog("n" + i, 3));
                if (i % 50 == 1) marks.Add(new MapMark("n" + i, (MarkKind)(i / 50 % 3), "メッセージ"));
                if (i % 8 == 0) buildings.Add(new MapBuilding("n" + i, "人物/人" + i % 20, "人" + i % 20, i % 3, i % 5, 1 + i % 7));
            }
            var seen = new HashSet<string>();
            void Link(int a, int b, bool auto)
            {
                if (a == b || !seen.Add(Math.Min(a, b) + "-" + Math.Max(a, b))) return;
                edges.Add(new MapRoad("n" + a, "n" + b, (RoadKind)rng.Next(4), auto));
            }
            // 町内: 同じ町の拠点を順につなぐ。
            for (int i = 0; i + towns < nodes; i++) Link(i, i + towns, i % 3 == 0);
            // 残りは、近い番号どうしと、ランダムな町をまたぐ線。
            int guard = 0;
            while (edges.Count < roads && guard++ < roads * 20)
            {
                int a = rng.Next(nodes);
                Link(a, rng.Next(3) == 0 ? rng.Next(nodes) : Math.Min(nodes - 1, a + 1 + rng.Next(towns * 2)), false);
            }
            return new MapSnapshot(new MapRequest("bench", 1, new Dictionary<string, long>()), list, edges, fogs, marks, flowers, buildings: buildings);
        }

        private static IVisualElementScheduledItem job;
        private static float startedAt; private static int startedFrame;
        // 拡大率を scale にして、毎フレーム再描画を要求し、計測を始める。
        public static void Begin(MapCanvas canvas, float scale)
        {
            canvas.Fit(); canvas.Zoom(scale / canvas.Scale);
            MapProfile.Reset(); MapProfile.Enabled = true;
            startedAt = Time.realtimeSinceStartup; startedFrame = Time.frameCount;
            job?.Pause(); job = canvas.schedule.Execute(() => canvas.MarkDirtyRepaint()); job.Every(1);
        }
        public static string End()
        {
            job?.Pause(); MapProfile.Enabled = false;
            float seconds = Time.realtimeSinceStartup - startedAt; int frames = Time.frameCount - startedFrame;
            return "frames/s=" + (frames / Math.Max(0.001f, seconds)).ToString("F1") + " (" + frames + " frames, " + seconds.ToString("F1") + "s)\n" + MapProfile.Report();
        }
    }
}
