using System;
using System.Collections.Generic;

namespace Cartowords.MapPresentation
{
    /// <summary>ホバーで説明を出す対象の種類。数が小さいほど優先される。</summary>
    public enum HoverTier { Mark = 0, Building = 1, Flower = 2, Flag = 3, Fog = 4, Candidate = 5 }

    /// <summary>世界座標での、ホバー対象 1 つ。四角か線分(関連の候補の点線)のどちらか。</summary>
    public readonly struct HoverTarget
    {
        public readonly HoverTier Tier;
        public readonly string Text;
        public readonly bool IsSegment;
        // 四角: (X0,Y0)-(X1,Y1)。線分: (X0,Y0)-(X1,Y1) と、線からの距離 Reach 以内。
        public readonly float X0, Y0, X1, Y1, Reach;
        private HoverTarget(HoverTier tier, string text, bool segment, float x0, float y0, float x1, float y1, float reach)
        { Tier = tier; Text = text ?? ""; IsSegment = segment; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; Reach = reach; }
        public static HoverTarget Rect(HoverTier tier, string text, float left, float top, float right, float bottom)
            => new HoverTarget(tier, text, false, Math.Min(left, right), Math.Min(top, bottom), Math.Max(left, right), Math.Max(top, bottom), 0);
        public static HoverTarget Around(HoverTier tier, string text, float cx, float cy, float halfSize)
            => Rect(tier, text, cx - halfSize, cy - halfSize, cx + halfSize, cy + halfSize);
        public static HoverTarget Segment(HoverTier tier, string text, float ax, float ay, float bx, float by, float reach)
            => new HoverTarget(tier, text, true, ax, ay, bx, by, reach);
    }

    /// <summary>地図の上の記号にポインタを置いた時の説明文を決める。UnityEngine に依存しない。</summary>
    public static class MapHover
    {
        public static string MarkText(MarkKind kind)
        {
            switch (kind)
            {
                case MarkKind.MissingReason: return "「!」この道の理由は、まだ書かれていません";
                case MarkKind.BrokenLink: return "リンク切れ(宛先のノートが見つかりません)";
                case MarkKind.Isolated: return "どこともつながっていない孤島のノートです";
                default: return "";
            }
        }
        public static string EmotionWord(Emotion emotion)
        {
            switch (emotion)
            {
                case Emotion.Joy: return "喜";
                case Emotion.Anger: return "怒";
                case Emotion.Sadness: return "哀";
                case Emotion.Delight: return "楽";
                default: return "";
            }
        }
        public static string FlowerText(Emotion emotion, bool withered, int line)
        {
            string word = EmotionWord(emotion), where = line > 0 ? "(" + line + "行目)" : "";
            return withered
                ? "枯れた花「" + word + "」: その感情語が消えたか、変わりました" + where
                : "花「" + word + "」: 「" + word + "」を表す言葉を書いた行です" + where;
        }
        public static string FogText(int line)
            => (line > 0 ? line + "行目に" : "") + "「?」や TODO: を書いた行があります。書き直すと晴れます";
        public static string FlagText(MapNode node)
        {
            string title = string.IsNullOrEmpty(node.Title) ? "(見出しなし)" : node.Title;
            string level = node.Level > 0 ? "  見出しレベル" + node.Level : "";
            return "拠点: " + title + "  [" + node.File + "]" + level;
        }
        public static string RoadText(RoadKind kind)
        {
            switch (kind)
            {
                case RoadKind.Premise: return "道: 前提";
                case RoadKind.Contrast: return "道: 対比";
                case RoadKind.Example: return "道: 具体例";
                default: return null;
            }
        }
        public static string BuildingText(string name, int count) => name + "  ×" + count;

        /// <summary>世界座標の点にある対象のうち、優先順位がいちばん高いものの文。なければ null。</summary>
        public static string Pick(float x, float y, IReadOnlyList<HoverTarget> targets)
        {
            string best = null; int bestTier = int.MaxValue; float bestScore = float.MaxValue;
            if (targets == null) return null;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i]; int tier = (int)t.Tier;
                if (tier > bestTier) continue;
                float score;
                if (t.IsSegment)
                {
                    score = DistanceToSegment(x, y, t.X0, t.Y0, t.X1, t.Y1);
                    if (score > t.Reach) continue;
                }
                else
                {
                    if (x < t.X0 || x > t.X1 || y < t.Y0 || y > t.Y1) continue;
                    // 建物は、いちばん手前(下)のもの。それ以外は、中心にいちばん近いもの。
                    score = t.Tier == HoverTier.Building ? -t.Y1
                        : (x - (t.X0 + t.X1) / 2) * (x - (t.X0 + t.X1) / 2) + (y - (t.Y0 + t.Y1) / 2) * (y - (t.Y0 + t.Y1) / 2);
                }
                if (tier < bestTier || score < bestScore) { best = t.Text; bestTier = tier; bestScore = score; }
            }
            return best;
        }

        public static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay, len2 = abx * abx + aby * aby;
            float t = len2 < 0.0001f ? 0 : ((px - ax) * abx + (py - ay) * aby) / len2;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            float dx = px - (ax + abx * t), dy = py - (ay + aby * t);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>ラベルのおおよその大きさ(文字は約 11px の全角として数える)。</summary>
        public static void EstimateSize(string text, float maxWidth, out float width, out float height)
        {
            int length = text == null ? 0 : text.Length;
            float inner = Math.Max(1, maxWidth - 8);
            width = Math.Min(maxWidth, length * 11f + 8);
            int lines = Math.Max(1, (int)Math.Ceiling(length * 11f / inner));
            height = lines * 15f + 4;
        }

        /// <summary>ポインタの右下に置くが、はみ出す時は左・上へ回し、かならず画面の中に収める。</summary>
        public static void PlaceLabel(float pointerX, float pointerY, float width, float height,
            float areaWidth, float areaHeight, out float left, out float top, float gap = 14f, float margin = 4f)
        {
            left = pointerX + gap; top = pointerY + gap;
            if (left + width > areaWidth - margin) left = pointerX - gap - width;
            if (top + height > areaHeight - margin) top = pointerY - gap - height;
            left = Math.Max(margin, Math.Min(left, areaWidth - margin - width));
            top = Math.Max(margin, Math.Min(top, areaHeight - margin - height));
            if (left < margin) left = margin;
            if (top < margin) top = margin;
        }
    }
}
