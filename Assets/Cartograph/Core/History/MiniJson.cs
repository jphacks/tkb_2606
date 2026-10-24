using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cartograph.Core
{
    /// <summary>
    /// 履歴ログ用の最小の JSON 読み書き。1 階層のオブジェクト(文字列・整数・真偽・null)だけを扱う。
    /// UnityEngine.JsonUtility には依存しない。
    /// </summary>
    internal static class MiniJson
    {
        public static void AppendQuoted(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || char.IsSurrogate(c) && !IsPairedSurrogate(s, i))
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static bool IsPairedSurrogate(string s, int i)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c)) return i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            return i > 0 && char.IsHighSurrogate(s[i - 1]);
        }

        /// <summary>値は string / long / bool / null。入れ子や配列、小数は失敗にする。</summary>
        public static bool TryParseObject(string json, out Dictionary<string, object> map)
        {
            map = null;
            if (json == null) return false;
            int i = 0;
            SkipSpace(json, ref i);
            if (i >= json.Length || json[i] != '{') return false;
            i++;
            var result = new Dictionary<string, object>();
            SkipSpace(json, ref i);
            if (i < json.Length && json[i] == '}')
            {
                i++;
            }
            else
            {
                while (true)
                {
                    SkipSpace(json, ref i);
                    if (!TryParseString(json, ref i, out string key)) return false;
                    SkipSpace(json, ref i);
                    if (i >= json.Length || json[i] != ':') return false;
                    i++;
                    SkipSpace(json, ref i);
                    if (!TryParseValue(json, ref i, out object value)) return false;
                    result[key] = value;
                    SkipSpace(json, ref i);
                    if (i >= json.Length) return false;
                    if (json[i] == ',') { i++; continue; }
                    if (json[i] == '}') { i++; break; }
                    return false;
                }
            }
            SkipSpace(json, ref i);
            if (i != json.Length) return false;
            map = result;
            return true;
        }

        private static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static bool TryParseValue(string s, ref int i, out object value)
        {
            value = null;
            if (i >= s.Length) return false;
            char c = s[i];
            if (c == '"')
            {
                if (!TryParseString(s, ref i, out string str)) return false;
                value = str;
                return true;
            }
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i;
                if (c == '-') i++;
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                if (digits == 0) return false;
                if (i < s.Length && (s[i] == '.' || s[i] == 'e' || s[i] == 'E')) return false;
                if (!long.TryParse(s.Substring(start, i - start), NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out long n)) return false;
                value = n;
                return true;
            }
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; value = true; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; value = false; return true; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; value = null; return true; }
            return false;
        }

        private static bool TryParseString(string s, ref int i, out string result)
        {
            result = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { result = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) return false;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length) return false;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }
    }
}
