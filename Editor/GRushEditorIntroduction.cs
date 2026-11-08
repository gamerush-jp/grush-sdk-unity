using System.Globalization;
using System.Text;

namespace GRushSdk.Editor
{
    /// <summary>
    /// 新しいゲームの説明と遊び方の長さ。GameRush は、改行と制御文字を整えて前後の空白を除いた後の
    /// コードポイントの数で、どちらにも 100 文字以上を求める（足りなければ <c>description.tooShort</c> /
    /// <c>howToPlay.tooShort</c>）。手元で同じ数え方をして、作成を送る前に止める。
    /// </summary>
    internal static class GRushEditorIntroduction
    {
        public const int MinLength = 100;
        public const int MaxLength = 2000;

        /// <summary>GameRush が保存する形に整えた後の、コードポイントの数（上限で切った後）。</summary>
        public static int Length(string value)
        {
            var count = CountCodePoints(Normalize(value ?? string.Empty));
            return count > MaxLength ? MaxLength : count;
        }

        public static bool IsLongEnough(string value)
        {
            return Length(value) >= MinLength;
        }

        // GameRush の multiLineText と同じ手順: 対になっていないサロゲートを除き、CR と CRLF を LF にし、
        // 各行の改行・制御文字の並びを1つの空白にして行末を詰め、3つ以上続く改行を2つにし、前後を詰める。
        internal static string Normalize(string value)
        {
            var text = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (char.IsHighSurrogate(c))
                {
                    if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    {
                        text.Append(c).Append(value[index + 1]);
                        index++;
                    }
                    continue;
                }
                if (char.IsLowSurrogate(c))
                {
                    continue;
                }
                if (c == '\r')
                {
                    text.Append('\n');
                    if (index + 1 < value.Length && value[index + 1] == '\n')
                    {
                        index++;
                    }
                    continue;
                }
                text.Append(c);
            }

            var lines = text.ToString().Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                lines[index] = CollapseBreaking(lines[index]).TrimEnd();
            }
            var joined = string.Join("\n", lines);
            while (joined.Contains("\n\n\n"))
            {
                joined = joined.Replace("\n\n\n", "\n\n");
            }
            return joined.Trim();
        }

        private static string CollapseBreaking(string line)
        {
            var result = new StringBuilder(line.Length);
            var inRun = false;
            foreach (var c in line)
            {
                if (IsBreaking(c))
                {
                    if (!inRun)
                    {
                        result.Append(' ');
                    }
                    inRun = true;
                    continue;
                }
                inRun = false;
                result.Append(c);
            }
            return result.ToString();
        }

        private static bool IsBreaking(char c)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.Control
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator
                || (c >= '‪' && c <= '‮')
                || (c >= '⁦' && c <= '⁩');
        }

        private static int CountCodePoints(string value)
        {
            var count = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length)
                {
                    index++;
                }
                count++;
            }
            return count;
        }
    }
}
