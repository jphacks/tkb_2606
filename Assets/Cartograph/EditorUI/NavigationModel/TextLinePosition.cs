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
    }
}
