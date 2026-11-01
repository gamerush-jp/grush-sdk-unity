using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GRushSdk
{
    /// <summary>
    /// エディタ用のランキング。作者が実サーバへ繋がずに投稿と表示を試せないと
    /// ランキングを組み込んだゲームを一切テストできないため、モックは付属品
    /// ではなく本体の一部として扱う。
    ///
    /// 実サーバ側の縛り（値域・自己ベスト集約・自己申告であること）は同じ形で
    /// 再現する。session 束縛と有効プレイ 10 秒はエディタでは意味を持たない
    /// ので省く。
    ///
    /// エディタでは、プロジェクトの直下（Assets の隣）に grush.json があれば、
    /// 最初のランキングの呼び出しの前に読んで枠を宣言する。WebGL ビルドでは同じ
    /// ファイルがビルドの最上位へ写るので、アップロードと手元の確認で同じ定義を使える。
    /// </summary>
    public static class GRushMockLeaderboards
    {
        private sealed class Entry
        {
            public string PseudoId;
            public string DisplayName;
            public string AvatarUrl;
            public bool IsGuest;
            public double Value;
            public bool IsSelf;
        }

        private sealed class Board
        {
            public string Key;
            public string Title;
            public string Sort = "desc";
            public string ValueType = "int";
            public string Aggregation = "best";
            public string Period = "all_time";
            public double? MinValue;
            public double? MaxValue;
            public bool Enabled = true;

            // grush.json から読んだ枠。GameRush は値域の無い枠への投稿を受けないので、それに合わせる。
            public bool RequireRange;
            public string PeriodKey = "";
            public List<Entry> Entries = new List<Entry>();
        }

        /// <summary>モックが読む、ビルドの最上位に置くランキングの定義のファイル名。</summary>
        public const string ManifestFileName = GRushMockManifest.FileName;

        private static readonly Dictionary<string, Board> boards = new Dictionary<string, Board>();
        private static bool manifestRead;

        /// <summary>
        /// エディタで使うランキング枠を宣言する。実環境では Studio か grush.json が持つ役割。
        /// プロジェクトの直下の grush.json を自動で読むときは、同じ key をこちらで宣言してあれば
        /// こちらを使う。<see cref="LoadManifest"/> に渡した定義は、こちらの宣言を置き換える。
        /// <paramref name="period"/> は <c>all_time</c> / <c>daily</c> / <c>weekly</c> /
        /// <c>monthly</c>。<paramref name="enabled"/> が false の枠は、宣言していない key と同じく
        /// 投稿と取得が <see cref="GRushErrorCode.InvalidParams"/> になる。
        /// </summary>
        public static void Define(
            string key,
            string title,
            string sort = "desc",
            string valueType = "int",
            string aggregation = "best",
            double minValue = 0,
            double maxValue = 1000000,
            string period = "all_time",
            bool enabled = true
        )
        {
            Put(
                new Board
                {
                    Key = key,
                    Title = string.IsNullOrEmpty(title) ? key : title,
                    Sort = sort,
                    ValueType = valueType,
                    Aggregation = aggregation,
                    Period = string.IsNullOrEmpty(period) ? "all_time" : period,
                    MinValue = minValue,
                    MaxValue = maxValue,
                    Enabled = enabled,
                }
            );
        }

        /// <summary>
        /// grush.json の中身（JSON の文字列）で枠を宣言し、宣言した key を返す。アップロードと
        /// 同じ規則で読み、誤りは <see cref="GRushErrorCode.InvalidParams"/> とアップロードと同じ
        /// 文言で返す。同じ key を <see cref="Define"/> で宣言してあっても、こちらの定義で置き換える。
        /// これを呼ぶと、プロジェクトの直下の grush.json は自動では読まない。
        /// </summary>
        public static GRushResult<string[]> LoadManifest(string json)
        {
            manifestRead = true;
            List<GRushMockManifest.Entry> entries;
            var error = GRushMockManifest.Parse(json, out entries);
            if (error != null)
            {
                return GRushResult<string[]>.Failure(GRushErrorCode.InvalidParams, error);
            }
            return GRushResult<string[]>.Success(Apply(entries, false).ToArray());
        }

        private static List<string> Apply(List<GRushMockManifest.Entry> entries, bool keepDefined)
        {
            var defined = new List<string>();
            foreach (var entry in entries)
            {
                if (keepDefined && boards.ContainsKey(entry.Key))
                {
                    continue;
                }
                Put(
                    new Board
                    {
                        Key = entry.Key,
                        Title = entry.Title,
                        Sort = entry.Sort,
                        ValueType = entry.ValueType,
                        Aggregation = entry.Aggregation,
                        Period = entry.Period,
                        MinValue = entry.MinValue,
                        MaxValue = entry.MaxValue,
                        Enabled = entry.Enabled,
                        RequireRange = true,
                    }
                );
                defined.Add(entry.Key);
            }
            return defined;
        }

        private static void Put(Board board)
        {
            board.PeriodKey = PeriodKeyOf(board.Period, DateTime.UtcNow);
            boards[board.Key] = board;
        }

        // エディタでだけ、プロジェクトの直下の grush.json を一度読む。無ければ何もしない。
        private static void EnsureManifest()
        {
            if (manifestRead)
            {
                return;
            }
            manifestRead = true;
#if UNITY_EDITOR
            // 文字列で読むと、UTF-16 の BOM のファイルや UTF-8 でないバイト列も通り、上限も文字数で
            // 測ることになる。GameRush と同じくバイト列のまま確かめる。
            byte[] bytes;
            try
            {
                var path = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? "",
                    ManifestFileName
                );
                if (!File.Exists(path))
                {
                    return;
                }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception failure)
            {
                Debug.LogWarning("[GRushMock] " + ManifestFileName + " を読めません: " + failure.Message);
                return;
            }
            List<GRushMockManifest.Entry> entries;
            var error = GRushMockManifest.ParseBytes(bytes, out entries);
            if (error != null)
            {
                Debug.LogWarning("[GRushMock] " + error);
                return;
            }
            Apply(entries, true);
#endif
        }

        // GameRush と同じ期間キー（UTC）。キーが変わったら、新しい期間として空からやり直す。
        internal static string PeriodKeyOf(string period, DateTime utc)
        {
            switch (period)
            {
                case "daily":
                    return utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "monthly":
                    return utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                case "weekly":
                    var date = utc.Date;
                    var day = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                    var thursday = date.AddDays(4 - day);
                    var week = (thursday.DayOfYear - 1) / 7 + 1;
                    return thursday.Year.ToString(CultureInfo.InvariantCulture)
                        + "-W"
                        + week.ToString("00", CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static Board Current(Board board)
        {
            var key = PeriodKeyOf(board.Period, DateTime.UtcNow);
            if (key != board.PeriodKey)
            {
                board.PeriodKey = key;
                board.Entries = new List<Entry>();
            }
            return board;
        }

        // GameRush は宣言していない key と止めた枠を同じく 404 で返す。
        private static GRushRpcResponse? Playable(string key, out Board board)
        {
            EnsureManifest();
            if (!boards.TryGetValue(key, out board))
            {
                return GRushRpcResponse.Failure(
                    GRushErrorCode.InvalidParams,
                    "No mock leaderboard named "
                        + key
                        + ". Declare it in grush.json or call GRushMockLeaderboards.Define first."
                );
            }
            if (!board.Enabled)
            {
                return GRushRpcResponse.Failure(
                    GRushErrorCode.InvalidParams,
                    "The mock leaderboard " + key + " is disabled."
                );
            }
            Current(board);
            return null;
        }

        /// <summary>他プレイヤーの行を積む。順位表示や同意の出し分けの確認に使う。</summary>
        public static void AddRival(
            string key,
            string displayName,
            double value,
            string avatarUrl = null,
            bool isGuest = false
        )
        {
            var board = Current(Ensure(key));
            board.Entries.Add(
                new Entry
                {
                    PseudoId = "mock-rival-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    DisplayName = isGuest ? null : displayName,
                    AvatarUrl = isGuest ? null : avatarUrl,
                    IsGuest = isGuest,
                    Value = value,
                    IsSelf = false,
                }
            );
        }

        /// <summary>
        /// 宣言した枠をすべて消す。次のランキングの呼び出しで、grush.json を読み直す。
        /// </summary>
        public static void Reset()
        {
            boards.Clear();
            manifestRead = false;
        }

        private static Board Ensure(string key)
        {
            EnsureManifest();
            Board board;
            if (!boards.TryGetValue(key, out board))
            {
                board = new Board
                {
                    Key = key,
                    Title = key,
                    MinValue = 0,
                    MaxValue = 1000000,
                };
                Put(board);
            }
            return board;
        }

        internal static bool TryGet(string key, out object handle)
        {
            Board board;
            var found = boards.TryGetValue(key, out board);
            handle = board;
            return found;
        }

        internal static string ListJson()
        {
            EnsureManifest();
            var builder = new StringBuilder("{\"leaderboards\":[");
            var first = true;
            foreach (var board in boards.Values)
            {
                if (!board.Enabled)
                {
                    continue;
                }
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                builder.Append('{');
                builder.Append("\"key\":").Append(GRushWire.Escape(board.Key));
                builder.Append(",\"title\":").Append(GRushWire.Escape(board.Title));
                builder.Append(",\"sort\":").Append(GRushWire.Escape(board.Sort));
                builder.Append(",\"valueType\":").Append(GRushWire.Escape(board.ValueType));
                builder.Append(",\"aggregation\":").Append(GRushWire.Escape(board.Aggregation));
                builder.Append(",\"period\":").Append(GRushWire.Escape(board.Period));
                builder.Append(",\"minValue\":").Append(Num(board.MinValue));
                builder.Append(",\"maxValue\":").Append(Num(board.MaxValue));
                builder.Append('}');
            }
            return builder.Append("]}").ToString();
        }

        // GameRush と同じく、枠を確かめた後に値、metadata、operationId の順に確かめ、値域はその後に確かめる。
        // metadataJson は metadata の生の JSON（無ければ null）。
        internal static GRushRpcResponse Submit(
            string key,
            double value,
            string metadataJson,
            string operationId,
            string selfPseudoId
        )
        {
            Board board;
            var unavailable = Playable(key, out board);
            if (unavailable.HasValue)
            {
                return unavailable.Value;
            }
            var inputError = GRushMockPlayerStatePayload.SubmitInputError(
                board.ValueType,
                value,
                metadataJson,
                operationId
            );
            if (inputError != null)
            {
                return GRushRpcResponse.Failure(GRushErrorCode.InvalidParams, inputError);
            }
            if (board.RequireRange && !board.MinValue.HasValue)
            {
                return GRushRpcResponse.Failure(
                    GRushErrorCode.InvalidParams,
                    "This leaderboard declares no minValue / maxValue, so it accepts no score."
                );
            }
            if (
                (board.MinValue.HasValue && value < board.MinValue.Value)
                || (board.MaxValue.HasValue && value > board.MaxValue.Value)
            )
            {
                return GRushRpcResponse.Failure(
                    GRushErrorCode.InvalidParams,
                    "Score is outside the declared range."
                );
            }

            var self = board.Entries.Find(entry => entry.IsSelf);
            var updated = true;
            if (self == null)
            {
                self = new Entry
                {
                    PseudoId = selfPseudoId,
                    DisplayName = GRushMock.DisplayName,
                    AvatarUrl = GRushMock.AvatarUrl,
                    IsGuest = !GRushMock.SignedIn,
                    Value = value,
                    IsSelf = true,
                };
                board.Entries.Add(self);
            }
            else if (board.Aggregation == "sum")
            {
                self.Value += value;
            }
            else if (board.Aggregation == "best")
            {
                var better =
                    board.Sort == "desc" ? value > self.Value : value < self.Value;
                if (better)
                {
                    self.Value = value;
                }
                else
                {
                    updated = false;
                }
            }
            else
            {
                self.Value = value;
            }

            var ranked = Ranked(board);
            var rank = ranked.FindIndex(entry => entry.IsSelf) + 1;

            var builder = new StringBuilder("{\"result\":{");
            builder.Append("\"accepted\":true");
            builder.Append(",\"updated\":").Append(updated ? "true" : "false");
            builder.Append(",\"value\":").Append(Num(self.Value));
            builder.Append(",\"rank\":").Append(rank);
            builder.Append(",\"verified\":false");
            return GRushRpcResponse.Success(builder.Append("}}").ToString());
        }

        internal static GRushRpcResponse Page(string key, int limit, int offset, bool aroundMe)
        {
            Board board;
            var unavailable = Playable(key, out board);
            if (unavailable.HasValue)
            {
                return unavailable.Value;
            }

            var ranked = Ranked(board);
            var start = offset;
            var count = limit > 0 ? limit : 20;

            if (aroundMe)
            {
                var selfIndex = ranked.FindIndex(entry => entry.IsSelf);
                if (selfIndex < 0)
                {
                    // 自分がまだ載っていないときは空。先頭を返すと「1位」と読める。
                    return GRushRpcResponse.Success(PageJson(board, new List<Entry>(), 0, ranked.Count));
                }
                var range = limit > 0 ? limit : 5;
                start = Math.Max(0, selfIndex - range);
                count = range * 2 + 1;
            }

            var slice = new List<Entry>();
            for (var index = start; index < ranked.Count && slice.Count < count; index++)
            {
                slice.Add(ranked[index]);
            }
            return GRushRpcResponse.Success(PageJson(board, slice, start, ranked.Count));
        }

        private static List<Entry> Ranked(Board board)
        {
            var ranked = new List<Entry>(board.Entries);
            ranked.Sort(
                (a, b) => board.Sort == "asc" ? a.Value.CompareTo(b.Value) : b.Value.CompareTo(a.Value)
            );
            return ranked;
        }

        private static string PageJson(Board board, List<Entry> entries, int startIndex, int total)
        {
            var builder = new StringBuilder("{\"leaderboard\":{");
            builder.Append("\"key\":").Append(GRushWire.Escape(board.Key));
            builder.Append(",\"title\":").Append(GRushWire.Escape(board.Title));
            builder.Append(",\"sort\":").Append(GRushWire.Escape(board.Sort));
            builder.Append(",\"valueType\":").Append(GRushWire.Escape(board.ValueType));
            builder.Append(",\"period\":").Append(GRushWire.Escape(board.Period));
            builder.Append(",\"periodKey\":").Append(GRushWire.Escape(board.PeriodKey));
            builder.Append(",\"verified\":false");
            builder.Append(",\"total\":").Append(total);
            builder.Append(",\"entries\":[");
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append('{');
                builder.Append("\"rank\":").Append(startIndex + index + 1);
                builder.Append(",\"pseudoId\":").Append(GRushWire.Escape(entry.PseudoId ?? ""));
                builder.Append(",\"displayName\":");
                builder.Append(
                    entry.DisplayName == null ? "null" : GRushWire.Escape(entry.DisplayName)
                );
                builder.Append(",\"avatarUrl\":");
                builder.Append(entry.AvatarUrl == null ? "null" : GRushWire.Escape(entry.AvatarUrl));
                builder.Append(",\"isGuest\":").Append(entry.IsGuest ? "true" : "false");
                builder.Append(",\"value\":").Append(Num(entry.Value));
                builder.Append(",\"submittedAt\":\"\"");
                builder.Append(",\"isSelf\":").Append(entry.IsSelf ? "true" : "false");
                builder.Append('}');
            }
            return builder.Append("]}}").ToString();
        }

        private static string Num(double? value)
        {
            return value.HasValue ? Num(value.Value) : "null";
        }

        private static string Num(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
