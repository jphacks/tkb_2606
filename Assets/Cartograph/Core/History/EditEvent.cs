using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cartograph.Core
{
    public static class EventType
    {
        public const string Insert = "insert";
        public const string Delete = "delete";
        public const string Move = "move";
        public const string Blocked = "blocked";
    }

    public static class EventSource
    {
        public const string Key = "key";
        public const string Ime = "ime";
        public const string Internal = "internal";
        public const string Undo = "undo";
        /// <summary>書式ボタンが挿入した Markdown 記号(仕様書の src 4 種への拡張)。</summary>
        public const string Format = "format";
    }

    /// <summary>
    /// 入力履歴ログの 1 行。位置はすべて UTF-16 単位。
    /// move の Pos は「元の範囲を取り除いた後の本文」での挿入位置。
    /// </summary>
    public sealed class EditEvent
    {
        public int Seq;
        public long T;
        public string Type;
        public int Pos;
        public string Text;     // insert のみ
        public int Len;         // delete / move / blocked(insert は Text.Length)
        public string Src;
        public int From = -1;   // move と internal の元の位置。なければ -1
        public string SrcDoc;   // internal の貼り付け元ノート(同じノートなら null)
        public string Lbl;      // insert の来歴ラベル(省略時は Src から決まる既定値)
    }

    public static class EventJson
    {
        public static string Serialize(EditEvent e)
        {
            var sb = new StringBuilder();
            sb.Append("{\"seq\":").Append(e.Seq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"t\":").Append(e.T.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"type\":");
            MiniJson.AppendQuoted(sb, e.Type);
            sb.Append(",\"pos\":").Append(e.Pos.ToString(CultureInfo.InvariantCulture));
            if (e.Type == EventType.Insert)
            {
                sb.Append(",\"text\":");
                MiniJson.AppendQuoted(sb, e.Text ?? "");
            }
            else
            {
                sb.Append(",\"len\":").Append(e.Len.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(",\"src\":");
            MiniJson.AppendQuoted(sb, e.Src ?? "");
            if (e.From >= 0) sb.Append(",\"from\":").Append(e.From.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(e.SrcDoc))
            {
                sb.Append(",\"srcDoc\":");
                MiniJson.AppendQuoted(sb, e.SrcDoc);
            }
            if (!string.IsNullOrEmpty(e.Lbl))
            {
                sb.Append(",\"lbl\":");
                MiniJson.AppendQuoted(sb, e.Lbl);
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>1 行を読む。必須項目がない、型が違う、未知の type は失敗。未知のキーは無視する。</summary>
        public static bool TryParse(string line, out EditEvent e)
        {
            e = null;
            if (!MiniJson.TryParseObject(line, out var m)) return false;
            if (!TryInt(m, "seq", out int seq) || seq < 1) return false;
            if (!TryLong(m, "t", out long t)) return false;
            if (!TryString(m, "type", out string type)) return false;
            if (!TryInt(m, "pos", out int pos) || pos < 0) return false;

            var ev = new EditEvent { Seq = seq, T = t, Type = type, Pos = pos };
            TryString(m, "src", out ev.Src);

            if (type == EventType.Insert)
            {
                if (!TryString(m, "text", out ev.Text)) return false;
                ev.Len = ev.Text.Length;
            }
            else if (type == EventType.Delete || type == EventType.Move || type == EventType.Blocked)
            {
                if (!TryInt(m, "len", out ev.Len) || ev.Len < 0) return false;
            }
            else
            {
                return false;
            }

            if (m.ContainsKey("from"))
            {
                if (!TryInt(m, "from", out ev.From) || ev.From < 0) return false;
            }
            if (m.ContainsKey("srcDoc") && !TryString(m, "srcDoc", out ev.SrcDoc)) return false;
            if (m.ContainsKey("lbl") && !TryString(m, "lbl", out ev.Lbl)) return false;
            e = ev;
            return true;
        }

        private static bool TryString(Dictionary<string, object> m, string key, out string value)
        {
            value = null;
            return m.TryGetValue(key, out object o) && (value = o as string) != null;
        }

        private static bool TryLong(Dictionary<string, object> m, string key, out long value)
        {
            value = 0;
            if (!m.TryGetValue(key, out object o) || !(o is long l)) return false;
            value = l;
            return true;
        }

        private static bool TryInt(Dictionary<string, object> m, string key, out int value)
        {
            value = 0;
            if (!TryLong(m, key, out long l) || l < int.MinValue || l > int.MaxValue) return false;
            value = (int)l;
            return true;
        }
    }

    /// <summary>ある時点の本文と来歴ラベル列。再生はここから始める。</summary>
    public sealed class Snapshot
    {
        public int Seq;
        public string Text;
        public string Lbl;

        public static Snapshot Of(int seq, LabeledText doc)
            => new Snapshot { Seq = seq, Text = doc.Text, Lbl = doc.EncodeLabels() };

        public bool TryToLabeledText(out LabeledText doc)
            => LabeledText.TryFromEncoded(Text, Lbl, out doc);

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\"seq\":").Append(Seq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"text\":");
            MiniJson.AppendQuoted(sb, Text ?? "");
            sb.Append(",\"lbl\":");
            MiniJson.AppendQuoted(sb, Lbl ?? "");
            sb.Append('}');
            return sb.ToString();
        }

        public static bool TryParse(string json, out Snapshot snapshot)
        {
            snapshot = null;
            if (!MiniJson.TryParseObject(json, out var m)) return false;
            if (!m.TryGetValue("seq", out object so) || !(so is long seq) || seq < 0 || seq > int.MaxValue) return false;
            if (!(m.TryGetValue("text", out object to) && to is string text)) return false;
            if (!(m.TryGetValue("lbl", out object lo) && lo is string lbl)) return false;
            snapshot = new Snapshot { Seq = (int)seq, Text = text, Lbl = lbl };
            return true;
        }
    }
}
