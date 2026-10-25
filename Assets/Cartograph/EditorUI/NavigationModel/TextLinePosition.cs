using System;

namespace Cartograph.EditorUI.Navigation
{
    public static class TextLinePosition
    {
        // Lines are one-based; offsets are UTF-16, matching TextField selection.
        public static int Offset(string text, int line)
        {
            if (line < 1) throw new ArgumentOutOfRangeException(nameof(line));
            text = text ?? "";
            if (line == 1) return 0;
            int current = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else if (text[i] != '\n') continue;
                if (++current == line) return i + 1;
            }
            return text.Length;
        }

        // Offset の逆。範囲外のオフセットは端に丸める。改行は \n・\r・\r\n のどれも 1 つと数える。
        public static int LineAt(string text, int offset)
        {
            text = text ?? "";
            offset = Math.Max(0, Math.Min(offset, text.Length));
            int line = 1;
            for (int i = 0; i < offset; i++)
            {
                if (text[i] == '\r')
                {
                    // \r\n の間にカーソルがあるときは、まだ前の行。
                    if (i + 1 < text.Length && text[i + 1] == '\n') { if (i + 2 > offset) break; i++; }
                    line++;
                }
                else if (text[i] == '\n') line++;
            }
            return line;
        }
    }
}
