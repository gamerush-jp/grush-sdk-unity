using System.Text;

namespace GRushSdk
{
    /// <summary>
    /// JavaScript へ渡す文字列を、UTF-8 で運べる形にする。
    /// </summary>
    internal static class GRushTransportText
    {
        /// <summary>
        /// 対になっていないサロゲート（UTF-16 の文字列を文字の途中で切った切れ端）を、U+FFFD 1つに替える。
        /// WebGL では C# の文字列を UTF-8 にして JavaScript へ渡すが、切れ端は UTF-8 で表せない。替えずに
        /// 渡すと、上位の切れ端が次の1文字を巻き込んで消し、閉じる引用符を巻き込むと JSON が壊れる。切れ端を
        /// UTF-8 で運べる文字に替えておけば、どの位置の切れ端も同じ1文字になり、ほかの文字は失われない。
        /// 対のサロゲートと、JSON のエスケープで書いた切れ端（ASCII の字面）はそのまま残す。
        /// </summary>
        public static string ReplaceLoneSurrogates(string text)
        {
            if (text == null)
            {
                return null;
            }
            StringBuilder builder = null;
            for (var index = 0; index < text.Length; index++)
            {
                var c = text[index];
                if (char.IsSurrogate(c) && char.IsSurrogatePair(text, index))
                {
                    if (builder != null)
                    {
                        builder.Append(c).Append(text[index + 1]);
                    }
                    index++;
                    continue;
                }
                if (!char.IsSurrogate(c))
                {
                    if (builder != null)
                    {
                        builder.Append(c);
                    }
                    continue;
                }
                if (builder == null)
                {
                    builder = new StringBuilder(text.Length);
                    builder.Append(text, 0, index);
                }
                builder.Append((char)0xFFFD);
            }
            return builder == null ? text : builder.ToString();
        }
    }
}
