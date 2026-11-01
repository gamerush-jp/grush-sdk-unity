using System.Collections.Generic;
using System.Text;

namespace GRushSdk
{
    /// <summary>
    /// モックの <c>playerState.setMine</c> が payload を検査する。サーバと同じく、
    /// JSON オブジェクトであること、配列とオブジェクトの入れ子が 3000 段以下であること、
    /// キーと文字列に対になっていないサロゲートが無いこと、空白を除いた JSON の UTF-8 の
    /// バイト数が 4KB 以下であることを、この順に求める。
    /// </summary>
    internal static class GRushMockPlayerStatePayload
    {
        public const int MaxBytes = 4 * 1024;

        /// <summary>
        /// 入れ子の段数の上限。値を囲む配列とオブジェクトの数で数え、スカラーは 0 段、
        /// <c>[]</c> と <c>{}</c> は 1 段。
        /// </summary>
        public const int MaxDepth = 3000;

        public const string NotObjectMessage = "Player state payload must be a JSON object.";
        public const string TooDeepMessage = "Player state payload is nested too deeply.";
        public const string LoneSurrogateMessage =
            "Player state payload must not contain lone surrogates.";
        public const string TooLargeMessage = "Player state payload is too large.";

        // GameRush は辿る値の数にも上限を置くが、ここでは数えない。payload は JSON の文字列で来るので
        // 値の数は文字数を超えず、その上限に届く payload は 4KB を大きく超えて、同じ InvalidParams で弾かれる。

        /// <summary>通るなら null、弾くならエラーの文言を返す。</summary>
        public static string Validate(string payloadJson)
        {
            var scanner = ScanObject(payloadJson);
            if (scanner == null)
            {
                return NotObjectMessage;
            }
            if (scanner.Deepest > MaxDepth)
            {
                return TooDeepMessage;
            }
            if (scanner.LoneSurrogate)
            {
                return LoneSurrogateMessage;
            }
            return scanner.Bytes > MaxBytes ? TooLargeMessage : null;
        }

        /// <summary>
        /// <paramref name="text"/> に、対になっていないサロゲート（UTF-16 の文字列を文字の途中で
        /// 切った切れ端）があるか。
        /// </summary>
        internal static bool HasLoneSurrogate(string text)
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (!char.IsSurrogate(text[index]))
                {
                    continue;
                }
                if (!char.IsSurrogatePair(text, index))
                {
                    return true;
                }
                index++;
            }
            return false;
        }

        /// <summary>
        /// <paramref name="json"/> が1つの JSON オブジェクトなら、サーバが payload を
        /// JSON.stringify し直したときの UTF-8 のバイト数を見積もって返す。作者が整形した
        /// JSON の空白は数えず、数値は JavaScript の数値の文字列化と同じ書き方に直して数える。
        /// 文字列のエスケープは書いたまま数えるため、<c>\u3042</c> のような書き方では
        /// サーバより多く数える（多く数える側の差は、実サーバで通る payload をモックで弾くだけ）。
        /// </summary>
        public static bool TryMeasureObject(string json, out long bytes)
        {
            var scanner = ScanObject(json);
            bytes = scanner == null ? 0 : scanner.Bytes;
            return scanner != null;
        }

        // 1つの JSON オブジェクトとして読めたら、読み終えた Scanner を返す。読めなければ null。
        private static Scanner ScanObject(string json)
        {
            if (json == null)
            {
                return null;
            }
            var scanner = new Scanner(json);
            scanner.SkipWhitespace();
            if (scanner.Peek() != '{' || !scanner.Document())
            {
                return null;
            }
            scanner.SkipWhitespace();
            return scanner.AtEnd ? scanner : null;
        }

        /// <summary>
        /// 数値の字句（構文は確かめ済み）を、JavaScript の Number::toString が書く文字数に直す。
        /// 有効数字が 15 桁までの 10 進数は倍精度を往復しても同じ桁に戻るので、最短の表記は字句の
        /// 有効数字と同じになる。
        /// 16 桁以上は丸めで桁数も指数も変わりうるので、17 桁で、指数が繰り上がった場合も含めた
        /// 長いほうを返す（多く数える側に倒す）。
        /// </summary>
        internal static int NumberLength(string literal)
        {
            var index = 0;
            var negative = literal[0] == '-';
            if (negative)
            {
                index++;
            }
            var digits = new StringBuilder();
            var point = 0;
            while (index < literal.Length && literal[index] >= '0' && literal[index] <= '9')
            {
                digits.Append(literal[index]);
                point++;
                index++;
            }
            if (index < literal.Length && literal[index] == '.')
            {
                index++;
                while (index < literal.Length && literal[index] >= '0' && literal[index] <= '9')
                {
                    digits.Append(literal[index]);
                    index++;
                }
            }
            long exponent = 0;
            if (index < literal.Length && (literal[index] == 'e' || literal[index] == 'E'))
            {
                index++;
                var exponentNegative = false;
                if (literal[index] == '+' || literal[index] == '-')
                {
                    exponentNegative = literal[index] == '-';
                    index++;
                }
                while (index < literal.Length)
                {
                    // 指数は 10^15 で止める。字句の長さ（int の範囲）で決まる小数点の位置を足しても
                    // 正確に足し合わせられ、止めたときの値は倍精度の範囲を大きく外れる（サーバでは
                    // 0 か非有限になる）ので、指数の表記で数えても少なく数えない。
                    if (exponent < 100000000000000)
                    {
                        exponent = exponent * 10 + (literal[index] - '0');
                    }
                    index++;
                }
                if (exponentNegative)
                {
                    exponent = -exponent;
                }
            }

            var text = digits.ToString();
            var first = 0;
            while (first < text.Length && text[first] == '0')
            {
                first++;
            }
            if (first == text.Length)
            {
                // 0 と -0 はどちらも "0" になる。
                return 1;
            }
            var last = text.Length;
            while (text[last - 1] == '0')
            {
                last--;
            }
            var k = last - first;
            // 値は 0.d1d2…dk × 10^n。
            var n = point - first + exponent;
            var length = k <= 15
                ? FormattedLength(k, n)
                : System.Math.Max(FormattedLength(17, n), FormattedLength(17, n + 1));
            return (negative ? 1 : 0) + length;
        }

        // ECMAScript の Number::toString(10) の書き方で、有効数字 k 桁・値 0.d1…dk × 10^n を書いた文字数。
        private static int FormattedLength(int k, long n)
        {
            if (k <= n && n <= 21)
            {
                return (int)n;
            }
            if (0 < n && n <= 21)
            {
                return k + 1;
            }
            if (-6 < n && n <= 0)
            {
                return 2 + (int)-n + k;
            }
            var e = System.Math.Abs(n - 1);
            return (k == 1 ? 1 : k + 1) + 2 + e.ToString().Length;
        }

        private sealed class Scanner
        {
            private readonly string text;
            private int position;

            public long Bytes;

            // 読んだ中で最も深い入れ子の段数。
            public int Deepest;

            // キーか文字列の値に、エスケープを解いた後で対になっていないサロゲートがあったか。
            public bool LoneSurrogate;

            public Scanner(string text)
            {
                this.text = text;
            }

            public bool AtEnd
            {
                get { return position >= text.Length; }
            }

            public char Peek()
            {
                return AtEnd ? '\0' : text[position];
            }

            public void SkipWhitespace()
            {
                while (!AtEnd)
                {
                    var c = text[position];
                    if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                    {
                        return;
                    }
                    position++;
                }
            }

            // 入れ子は再帰ではなく閉じ括弧の積み上げで追う。深い入れ子でも再帰がスタックを
            // 使い切って Editor ごと落ちない。深さの上限は、読み終えてから Validate が見る。
            public bool Document()
            {
                var closers = new List<char>();
                while (true)
                {
                    SkipWhitespace();
                    var c = Peek();
                    if (c == '{' || c == '[')
                    {
                        Deepest = System.Math.Max(Deepest, closers.Count + 1);
                        var close = c == '{' ? '}' : ']';
                        Take(1);
                        SkipWhitespace();
                        if (Peek() == close)
                        {
                            Take(1);
                        }
                        else
                        {
                            closers.Add(close);
                            if (close == '}' && !Key())
                            {
                                return false;
                            }
                            continue;
                        }
                    }
                    else if (!Scalar(c))
                    {
                        return false;
                    }

                    // 値を1つ読み終えた。閉じられる入れ子を閉じ、次の要素へ進む。
                    while (true)
                    {
                        if (closers.Count == 0)
                        {
                            return true;
                        }
                        SkipWhitespace();
                        var top = closers[closers.Count - 1];
                        var next = Peek();
                        if (next == top)
                        {
                            Take(1);
                            closers.RemoveAt(closers.Count - 1);
                            continue;
                        }
                        if (next != ',')
                        {
                            return false;
                        }
                        Take(1);
                        if (top == '}' && !Key())
                        {
                            return false;
                        }
                        break;
                    }
                }
            }

            private bool Key()
            {
                SkipWhitespace();
                if (Peek() != '"' || !String())
                {
                    return false;
                }
                SkipWhitespace();
                if (Peek() != ':')
                {
                    return false;
                }
                Take(1);
                return true;
            }

            private bool Scalar(char c)
            {
                switch (c)
                {
                    case '"':
                        return String();
                    case 't':
                        return Literal("true");
                    case 'f':
                        return Literal("false");
                    case 'n':
                        return Literal("null");
                    default:
                        return Number();
                }
            }

            // サーバは JSON を読んだ後の文字列を見るので、エスケープを解いた UTF-16 の並びで
            // サロゲートが対になっているかを確かめる。上位と下位を別々のエスケープで書いた対も、
            // 生の文字とエスケープを並べた対も、1つの文字として通す。
            private bool String()
            {
                var start = position;
                var decoded = new StringBuilder();
                position++;
                while (!AtEnd)
                {
                    var c = text[position];
                    if (c == '"')
                    {
                        position++;
                        Bytes += Encoding.UTF8.GetByteCount(text.Substring(start, position - start));
                        if (HasLoneSurrogate(decoded.ToString()))
                        {
                            LoneSurrogate = true;
                        }
                        return true;
                    }
                    if (c < 0x20)
                    {
                        return false;
                    }
                    if (c == '\\')
                    {
                        if (!Escape(decoded))
                        {
                            return false;
                        }
                        continue;
                    }
                    decoded.Append(c);
                    position++;
                }
                return false;
            }

            // エスケープを1つ読み、表す文字を decoded に足す。
            private bool Escape(StringBuilder decoded)
            {
                position++;
                if (AtEnd)
                {
                    return false;
                }
                var c = text[position];
                position++;
                switch (c)
                {
                    case '"':
                    case '\\':
                    case '/':
                        decoded.Append(c);
                        return true;
                    case 'b':
                        decoded.Append('\b');
                        return true;
                    case 'f':
                        decoded.Append('\f');
                        return true;
                    case 'n':
                        decoded.Append('\n');
                        return true;
                    case 'r':
                        decoded.Append('\r');
                        return true;
                    case 't':
                        decoded.Append('\t');
                        return true;
                }
                if (c != 'u' || position + 4 > text.Length)
                {
                    return false;
                }
                var code = 0;
                for (var i = 0; i < 4; i++)
                {
                    var digit = HexValue(text[position + i]);
                    if (digit < 0)
                    {
                        return false;
                    }
                    code = code * 16 + digit;
                }
                decoded.Append((char)code);
                position += 4;
                return true;
            }

            private bool Literal(string word)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
                {
                    return false;
                }
                Take(word.Length);
                return true;
            }

            private bool Number()
            {
                var start = position;
                if (Peek() == '-')
                {
                    position++;
                }
                if (Peek() == '0')
                {
                    position++;
                }
                else if (!Digits())
                {
                    return false;
                }
                if (Peek() == '.')
                {
                    position++;
                    if (!Digits())
                    {
                        return false;
                    }
                }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    position++;
                    if (Peek() == '+' || Peek() == '-')
                    {
                        position++;
                    }
                    if (!Digits())
                    {
                        return false;
                    }
                }
                Bytes += NumberLength(text.Substring(start, position - start));
                return true;
            }

            private bool Digits()
            {
                var start = position;
                while (!AtEnd && text[position] >= '0' && text[position] <= '9')
                {
                    position++;
                }
                return position > start;
            }

            private void Take(int length)
            {
                position += length;
                Bytes += length;
            }

            // 16 進の1桁の値。16 進の数字でなければ -1。
            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9')
                {
                    return c - '0';
                }
                if (c >= 'a' && c <= 'f')
                {
                    return c - 'a' + 10;
                }
                if (c >= 'A' && c <= 'F')
                {
                    return c - 'A' + 10;
                }
                return -1;
            }
        }
    }
}
