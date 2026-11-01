using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GRushSdk
{
    /// <summary>
    /// モックが grush.json を読む。アップロードと同じ規則で読み、誤りはアップロードと同じ
    /// 文言で返すので、手元で確かめた時点で書き損じに気づける。
    /// </summary>
    internal static class GRushMockManifest
    {
        public const string FileName = "grush.json";

        private const int MaxBytes = 64 * 1024;
        private const int MaxLeaderboards = 20;
        private const int MaxTitleLength = 60;
        private const int MaxKeyLength = 64;

        private static readonly HashSet<string> TopLevelFields = new HashSet<string>
        {
            "$schema",
            "leaderboards",
        };

        private static readonly HashSet<string> EntryFields = new HashSet<string>
        {
            "key",
            "title",
            "sort",
            "valueType",
            "aggregation",
            "period",
            "minValue",
            "maxValue",
            "enabled",
        };

        private static readonly string[][] Choices =
        {
            new[] { "sort", "desc", "asc" },
            new[] { "valueType", "int", "float", "duration_ms" },
            new[] { "aggregation", "best", "last", "sum" },
            new[] { "period", "all_time", "daily", "weekly", "monthly" },
        };

        internal sealed class Entry
        {
            public string Key;
            public string Title;
            public string Sort;
            public string ValueType;
            public string Aggregation;
            public string Period;
            public double? MinValue;
            public double? MaxValue;
            public bool Enabled;
        }

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// ファイルのバイト列を読む。GameRush と同じく、上限はバイト数で測り、UTF-8 として
        /// 読めないバイト列は JSON として読めないものとして拒む。
        /// </summary>
        public static string ParseBytes(byte[] bytes, out List<Entry> entries)
        {
            entries = new List<Entry>();
            if (bytes == null)
            {
                return NotObject();
            }
            if (bytes.Length > MaxBytes)
            {
                return TooLarge();
            }
            string text;
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (ArgumentException)
            {
                return NotObject();
            }
            return ParseText(text, out entries);
        }

        /// <summary>読めたら null と定義の一覧を、読めなければエラーの文言を返す。</summary>
        public static string Parse(string json, out List<Entry> entries)
        {
            entries = new List<Entry>();
            if (json == null)
            {
                return NotObject();
            }
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
            {
                return TooLarge();
            }
            return ParseText(json, out entries);
        }

        private static string ParseText(string json, out List<Entry> entries)
        {
            entries = new List<Entry>();
            // GameRush は UTF-8 として読むときに先頭の BOM を1つ外し、JSON として読む前にもう1つ外す。
            for (var removed = 0; removed < 2 && json.Length > 0 && json[0] == '\uFEFF'; removed++)
            {
                json = json.Substring(1);
            }
            object parsed;
            if (!Json.TryParse(json, out parsed))
            {
                return NotObject();
            }
            var root = parsed as Json.Object;
            if (root == null)
            {
                return NotObject();
            }
            foreach (var field in root.Keys)
            {
                if (!TopLevelFields.Contains(field))
                {
                    return FileName + " has an unknown field: " + field;
                }
            }

            object listValue;
            if (!root.TryGet("leaderboards", out listValue) || listValue == null)
            {
                return null;
            }
            var list = listValue as List<object>;
            if (list == null)
            {
                return Invalid("leaderboards");
            }
            if (list.Count > MaxLeaderboards)
            {
                return FileName + " can declare up to " + MaxLeaderboards + " leaderboards.";
            }

            var seen = new HashSet<string>();
            for (var index = 0; index < list.Count; index++)
            {
                Entry entry;
                var error = ReadEntry(list[index], "leaderboards[" + index + "]", seen, out entry);
                if (error != null)
                {
                    entries.Clear();
                    return error;
                }
                entries.Add(entry);
            }
            return null;
        }

        // GameRush と同じ順に確かめる。誤りが複数あっても、アップロードと同じ項目を返す。
        private static string ReadEntry(
            object value,
            string at,
            HashSet<string> seen,
            out Entry entry
        )
        {
            entry = null;
            var item = value as Json.Object;
            if (item == null)
            {
                return Invalid(at);
            }
            foreach (var field in item.Keys)
            {
                if (!EntryFields.Contains(field))
                {
                    return FileName + " has an unknown field: " + at + "." + field;
                }
            }

            var key = item.Get("key") as string;
            if (!IsKey(key))
            {
                return Invalid(at + ".key");
            }
            if (!seen.Add(key))
            {
                return FileName + " declares the leaderboard key more than once: " + key;
            }
            double? min;
            double? max;
            if (!TryNumber(item, "minValue", out min))
            {
                return Invalid(at + ".minValue");
            }
            if (!TryNumber(item, "maxValue", out max))
            {
                return Invalid(at + ".maxValue");
            }
            var title = CollapseTitle(item.Get("title") as string);
            if (title.Length < 1 || title.Length > MaxTitleLength)
            {
                return Invalid(at + ".title");
            }
            var picked = new string[Choices.Length];
            for (var index = 0; index < Choices.Length; index++)
            {
                var choice = Choices[index];
                var text = item.Get(choice[0]) as string;
                if (text == null || Array.IndexOf(choice, text, 1) < 0)
                {
                    return Invalid(at + "." + choice[0]);
                }
                picked[index] = text;
            }

            object enabled;
            if (item.TryGet("enabled", out enabled) && !(enabled is bool))
            {
                return Invalid(at + ".enabled");
            }
            if (min.HasValue != max.HasValue || (min.HasValue && min.Value >= max.Value))
            {
                return Invalid(at + ".minValue / maxValue");
            }

            entry = new Entry
            {
                Key = key,
                Title = title,
                Sort = picked[0],
                ValueType = picked[1],
                Aggregation = picked[2],
                Period = picked[3],
                MinValue = min,
                MaxValue = max,
                Enabled = !(enabled is bool) || (bool)enabled,
            };
            return null;
        }

        private static bool TryNumber(Json.Object item, string field, out double? number)
        {
            number = null;
            object value;
            if (!item.TryGet(field, out value) || value == null)
            {
                return true;
            }
            if (!(value is double) || double.IsInfinity((double)value))
            {
                return false;
            }
            number = (double)value;
            return true;
        }

        private static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            {
                return false;
            }
            foreach (var c in key)
            {
                var allowed =
                    (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '.'
                    || c == '_'
                    || c == ':'
                    || c == '-';
                if (!allowed)
                {
                    return false;
                }
            }
            return true;
        }

        // 改行と制御文字を空白にしてから前後を詰める。GameRush が保存するタイトルと同じ形にする。
        // 長さは GameRush と同じく UTF-16 の単位で測るので、string.Length をそのまま使える。
        // GameRush と同じく、対になっていないサロゲートを先に除く。除いた切れ端の両側の制御文字は、
        // 1つの空白にまとまる。
        private static string CollapseTitle(string title)
        {
            if (title == null)
            {
                return "";
            }
            var builder = new StringBuilder(title.Length);
            var inRun = false;
            for (var index = 0; index < title.Length; index++)
            {
                var c = title[index];
                if (char.IsSurrogate(c))
                {
                    if (!char.IsSurrogatePair(title, index))
                    {
                        continue;
                    }
                    inRun = false;
                    builder.Append(c).Append(title[index + 1]);
                    index++;
                    continue;
                }
                var breaking =
                    char.IsControl(c)
                    || c == '\u2028'
                    || c == '\u2029'
                    || (c >= '\u202A' && c <= '\u202E')
                    || (c >= '\u2066' && c <= '\u2069');
                if (breaking)
                {
                    if (!inRun)
                    {
                        builder.Append(' ');
                    }
                    inRun = true;
                    continue;
                }
                inRun = false;
                builder.Append(c);
            }
            var start = 0;
            var end = builder.Length;
            while (start < end && IsTrimmed(builder[start]))
            {
                start++;
            }
            while (end > start && IsTrimmed(builder[end - 1]))
            {
                end--;
            }
            return builder.ToString(start, end - start);
        }

        // GameRush の trim が外す空白（ECMAScript の WhiteSpace と LineTerminator）。string.Trim は
        // U+FEFF を外さず U+0085 を外すので使わない。制御文字と行の区切りは先に空白へ潰してある。
        private static bool IsTrimmed(char c)
        {
            return c == ' '
                || c == '\u00A0'
                || c == '\u1680'
                || (c >= '\u2000' && c <= '\u200A')
                || c == '\u202F'
                || c == '\u205F'
                || c == '\u3000'
                || c == '\uFEFF';
        }

        private static string TooLarge()
        {
            return FileName + " is too large (" + (MaxBytes / 1024) + "KB at most).";
        }

        private static string NotObject()
        {
            return FileName + " can't be read as a JSON object.";
        }

        private static string Invalid(string field)
        {
            return FileName + " has an invalid value: " + field;
        }

        /// <summary>grush.json を読むためだけの JSON の読み取り。オブジェクトは書かれた順に項目を持つ。</summary>
        internal static class Json
        {
            internal sealed class Object
            {
                private readonly List<KeyValuePair<string, object>> fields =
                    new List<KeyValuePair<string, object>>();

                public IEnumerable<string> Keys
                {
                    get
                    {
                        foreach (var field in fields)
                        {
                            yield return field.Key;
                        }
                    }
                }

                // 同じ名前が2回あれば、JSON.parse と同じく後のほうを使う。
                public void Set(string key, object value)
                {
                    for (var index = 0; index < fields.Count; index++)
                    {
                        if (fields[index].Key == key)
                        {
                            fields[index] = new KeyValuePair<string, object>(key, value);
                            return;
                        }
                    }
                    fields.Add(new KeyValuePair<string, object>(key, value));
                }

                public bool TryGet(string key, out object value)
                {
                    foreach (var field in fields)
                    {
                        if (field.Key == key)
                        {
                            value = field.Value;
                            return true;
                        }
                    }
                    value = null;
                    return false;
                }

                public object Get(string key)
                {
                    object value;
                    return TryGet(key, out value) ? value : null;
                }
            }

            public static bool TryParse(string text, out object value)
            {
                var index = 0;
                if (!ReadValue(text, ref index, out value, 0))
                {
                    return false;
                }
                SkipSpace(text, ref index);
                return index == text.Length;
            }

            private static void SkipSpace(string text, ref int index)
            {
                while (
                    index < text.Length
                    && (text[index] == ' ' || text[index] == '\t' || text[index] == '\n' || text[index] == '\r')
                )
                {
                    index++;
                }
            }

            private static bool ReadValue(string text, ref int index, out object value, int depth)
            {
                value = null;
                SkipSpace(text, ref index);
                if (index >= text.Length || depth > 64)
                {
                    return false;
                }
                var c = text[index];
                if (c == '{')
                {
                    return ReadObject(text, ref index, out value, depth);
                }
                if (c == '[')
                {
                    return ReadArray(text, ref index, out value, depth);
                }
                if (c == '"')
                {
                    string parsed;
                    var ok = ReadString(text, ref index, out parsed);
                    value = parsed;
                    return ok;
                }
                if (Literal(text, ref index, "true"))
                {
                    value = true;
                    return true;
                }
                if (Literal(text, ref index, "false"))
                {
                    value = false;
                    return true;
                }
                if (Literal(text, ref index, "null"))
                {
                    return true;
                }
                return ReadNumber(text, ref index, out value);
            }

            private static bool Literal(string text, ref int index, string literal)
            {
                if (string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                {
                    return false;
                }
                index += literal.Length;
                return true;
            }

            private static bool ReadObject(string text, ref int index, out object value, int depth)
            {
                var result = new Object();
                value = result;
                index++;
                SkipSpace(text, ref index);
                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return true;
                }
                while (true)
                {
                    SkipSpace(text, ref index);
                    string key;
                    if (index >= text.Length || text[index] != '"' || !ReadString(text, ref index, out key))
                    {
                        return false;
                    }
                    SkipSpace(text, ref index);
                    if (index >= text.Length || text[index] != ':')
                    {
                        return false;
                    }
                    index++;
                    object item;
                    if (!ReadValue(text, ref index, out item, depth + 1))
                    {
                        return false;
                    }
                    result.Set(key, item);
                    SkipSpace(text, ref index);
                    if (index >= text.Length)
                    {
                        return false;
                    }
                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }
                    if (text[index] == '}')
                    {
                        index++;
                        return true;
                    }
                    return false;
                }
            }

            private static bool ReadArray(string text, ref int index, out object value, int depth)
            {
                var result = new List<object>();
                value = result;
                index++;
                SkipSpace(text, ref index);
                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return true;
                }
                while (true)
                {
                    object item;
                    if (!ReadValue(text, ref index, out item, depth + 1))
                    {
                        return false;
                    }
                    result.Add(item);
                    SkipSpace(text, ref index);
                    if (index >= text.Length)
                    {
                        return false;
                    }
                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }
                    if (text[index] == ']')
                    {
                        index++;
                        return true;
                    }
                    return false;
                }
            }

            private static bool ReadString(string text, ref int index, out string value)
            {
                value = null;
                var builder = new StringBuilder();
                index++;
                while (index < text.Length)
                {
                    var c = text[index++];
                    if (c == '"')
                    {
                        value = builder.ToString();
                        return true;
                    }
                    if (c < ' ')
                    {
                        return false;
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (index >= text.Length)
                    {
                        return false;
                    }
                    var escape = text[index++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escape);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            int code;
                            if (
                                index + 4 > text.Length
                                || !int.TryParse(
                                    text.Substring(index, 4),
                                    NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture,
                                    out code
                                )
                            )
                            {
                                return false;
                            }
                            builder.Append((char)code);
                            index += 4;
                            break;
                        default:
                            return false;
                    }
                }
                return false;
            }

            private static bool ReadNumber(string text, ref int index, out object value)
            {
                value = null;
                var start = index;
                if (index < text.Length && text[index] == '-')
                {
                    index++;
                }
                if (index >= text.Length || !char.IsDigit(text[index]))
                {
                    return false;
                }
                if (text[index] == '0')
                {
                    index++;
                }
                else
                {
                    Digits(text, ref index);
                }
                if (index < text.Length && text[index] == '.')
                {
                    index++;
                    if (Digits(text, ref index) == 0)
                    {
                        return false;
                    }
                }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                    {
                        index++;
                    }
                    if (Digits(text, ref index) == 0)
                    {
                        return false;
                    }
                }
                double number;
                if (
                    !double.TryParse(
                        text.Substring(start, index - start),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out number
                    )
                )
                {
                    return false;
                }
                value = number;
                return true;
            }

            private static int Digits(string text, ref int index)
            {
                var start = index;
                while (index < text.Length && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                }
                return index - start;
            }
        }
    }
}
