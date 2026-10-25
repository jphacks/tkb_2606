using System.Collections.Generic;

namespace Cartowords.MapPresentation
{
    // 「表示サンプル」の地図。地図の見方そのものを説明するため、拠点の見出しが、そこに置かれたものの説明になっている。
    public static class MapSample
    {
        public static MapSnapshot Build()
        {
            var nodes = new List<MapNode>();
            void N(string id, string file, string title, int line, int chars, int level, bool isolated = false)
                => nodes.Add(new MapNode(id, file, title, line, chars, isolated, level));

            // 大学/物理: 同じノートの見出しは、同じ島(土地)に集まる。字数で自然が変わる。
            N("a0", "大学/物理/波の性質", "同じ島の見出し1", 1, 220, 1);
            N("a1", "大学/物理/波の性質", "同じ島の見出し2", 5, 150, 2);
            N("a2", "大学/物理/波の性質", "同じ島の見出し3", 9, 40, 3);
            N("b0", "大学/物理/振動", "森: 900字以上", 1, 1000, 1);
            N("c0", "大学/物理/音", "木: 400字以上", 1, 450, 2);
            N("c1", "大学/物理/音", "ふつうの道(種類なし)", 6, 60, 3);
            // 大学/生物: 道の種類。
            N("d0", "大学/生物/光合成", "前提の道: 先に知る話", 1, 260, 1);
            N("d1", "大学/生物/光合成", "対比の道: くらべる", 5, 130, 2);
            N("e0", "大学/生物/呼吸", "具体例の道: 例えば", 1, 90, 1);
            N("e1", "大学/生物/呼吸", "自動の道: 自動検出", 5, 80, 2);
            N("g0", "大学/生物/観察", "霧: ? や TODO の所", 1, 160, 1);
            N("g1", "大学/生物/観察", "茂み: 200字以上", 6, 260, 2);
            N("t0", "大学/生物/類似", "点線: 関連の候補", 1, 100, 1);
            // 個人: しるし(印)、字数0、孤島、花。
            N("h0", "個人/日記/今日", "理由がない道の印", 1, 300, 1);
            N("i0", "個人/日記/今日", "切れたリンクの印", 5, 120, 2);
            N("k0", "個人/日記/空", "字数0の島", 1, 0, 1);
            N("j0", "個人/孤島/ひとり", "孤立の印・小さな島", 1, 12, 1, true);
            N("m0", "個人/気持ち/花", "花: 喜び", 1, 100, 1);
            N("m1", "個人/気持ち/花", "花: 怒り", 5, 100, 2);
            N("m2", "個人/気持ち/花", "花: 悲しみ", 9, 100, 2);
            N("m3", "個人/気持ち/花", "花: 楽しさ", 13, 100, 2);
            N("m4", "個人/気持ち/花", "枯れた花: 薄れた", 17, 100, 2);
            // 場所: 固有名詞の建物。言及が増えるほど大きくなる。
            N("p0", "場所/建物/小さい", "小屋: 言及が少ない", 1, 150, 1);
            N("p1", "場所/建物/小さい", "家: 少し多い", 5, 150, 2);
            N("q0", "場所/建物/大きい", "山小屋: 横長", 1, 200, 1);
            N("q1", "場所/建物/大きい", "大きな山小屋", 5, 200, 2);
            N("r0", "場所/建物/ビル", "ビル: 幅1〜3", 1, 200, 1);
            N("r1", "場所/建物/ビル", "高いビル: 階が多い", 5, 200, 2);

            // 道は、別のフォルダのノートどうしを多めにつなぎ、交差も見られるようにする。
            var roads = new[]
            {
                new MapRoad("b0", "c1"), new MapRoad("b0", "d0", RoadKind.Premise), new MapRoad("a2", "d1", RoadKind.Contrast),
                new MapRoad("d1", "e0", RoadKind.Example), new MapRoad("d0", "e1", RoadKind.Unspecified, true),
                new MapRoad("c0", "g0", RoadKind.Premise), new MapRoad("t0", "g1"), new MapRoad("h0", "a1"),
                new MapRoad("h0", "i0"), new MapRoad("m0", "h0"), new MapRoad("m2", "e0", RoadKind.Example),
                new MapRoad("k0", "h0", RoadKind.Contrast), new MapRoad("p0", "a0", RoadKind.Premise),
                new MapRoad("p1", "m0", RoadKind.Example), new MapRoad("q0", "d0", RoadKind.Contrast),
                new MapRoad("q1", "p1"), new MapRoad("r0", "c0", RoadKind.Example), new MapRoad("r1", "q0", RoadKind.Premise),
                new MapRoad("a0", "c0", RoadKind.Premise), new MapRoad("a1", "b0", RoadKind.Premise),
                new MapRoad("m1", "d1", RoadKind.Contrast), new MapRoad("m3", "m4"), new MapRoad("m4", "g1", RoadKind.Example),
            };
            var flowers = new[]
            {
                new MapFlower("m0", 2, Emotion.Joy), new MapFlower("m1", 6, Emotion.Anger), new MapFlower("m2", 10, Emotion.Sadness),
                new MapFlower("m3", 14, Emotion.Delight), new MapFlower("m4", 18, Emotion.Joy, true),
            };
            var marks = new[]
            {
                new MapMark("h0", MarkKind.MissingReason, "この道の理由は、まだ書かれていません"),
                new MapMark("i0", MarkKind.BrokenLink, "この道の先は、まだ地図にありません"),
                new MapMark("j0", MarkKind.Isolated, "ここは、まだどこともつながっていません"),
            };

            // ビルの幅(1〜3)は固有名詞のファイル名で決まるので、3 通りの幅が揃うファイル名を探す。
            var towerByWidth = new Dictionary<int, string>();
            for (int i = 0; i < 500 && towerByWidth.Count < 3; i++)
            {
                string file = "人物/塔" + i;
                int width = MapLayout.StableHash(file) % 3 + 1;
                if (!towerByWidth.ContainsKey(width)) towerByWidth[width] = file;
            }
            var buildings = new List<MapBuilding>
            {
                new MapBuilding("p0", "人物/田中", "田中(小屋)", 0, 0, 1),
                new MapBuilding("p1", "人物/鈴木", "鈴木(家)", 0, 1, 0),
                new MapBuilding("q0", "場所/図書館", "図書館(山小屋)", 1, 1, 0),
                new MapBuilding("q1", "場所/研究室", "研究室(大)", 3, 0, 0),
                new MapBuilding("r1", "人物/高層", "高層(階が多い)", 10, 0, 0),
            };
            foreach (var pair in towerByWidth) buildings.Add(new MapBuilding("r0", pair.Value, "幅" + pair.Key + "のビル", 5, 0, 0));

            var fogs = new[] { new MapFog("g0", 3) };
            var candidates = new[] { new MapCandidate("t0", "c0", "大学/生物/類似", "大学/物理/音", 0.6, "「波」「振動」の言葉が重なっています") };
            return new MapSnapshot(new MapRequest("preview", 1, new Dictionary<string, long>()), nodes, roads, fogs, marks, flowers,
                buildings: buildings, candidates: candidates);
        }
    }
}
