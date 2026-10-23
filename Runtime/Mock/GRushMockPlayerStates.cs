using System;
using System.Collections.Generic;
using System.Text;

namespace GRushSdk
{
    /// <summary>
    /// エディタ用の「他プレイヤーの公開状態」。実サーバへ繋がずに、他人の進捗を
    /// 読む側のコードを試せるようにする。
    ///
    /// hidden の扱いも再現する。**運営が伏せた状態は get から返らない**ので、
    /// ゲームが「要求した数だけ返る」前提で書いていないかを確かめられる唯一の場所。
    ///
    /// モックの相手（<see cref="GRushMock.Rivals"/>）は、はじめから rating 付きの状態を持つので、
    /// 相手探し（<c>SampleAsync</c>）がそのまま試せる。<see cref="Reset"/> の後も持ち直す。
    /// </summary>
    public static class GRushMockPlayerStates
    {
        private const int SampleDefaultCount = 10;
        private const int SampleMaxCount = 20;
        private const double RatingLimit = 1e9;
        private static readonly double[] RivalRatings = { 1000, 1200, 1500 };

        private sealed class Entry
        {
            public string PayloadJson;
            public bool Hidden;
            public int Revision;
            public bool HasRating;
            public double Rating;
        }

        private static readonly Dictionary<string, Entry> states = new Dictionary<string, Entry>();
        private static readonly Random random = new Random();

        static GRushMockPlayerStates()
        {
            SeedRivals();
        }

        public static void Define(string pseudoId, string payloadJson)
        {
            Define(pseudoId, payloadJson, false);
        }

        public static void Define(string pseudoId, string payloadJson, bool hidden)
        {
            Put(pseudoId, payloadJson, hidden, false, 0);
        }

        /// <summary>rating 付きの状態を置く。相手探しの範囲の絞り込みを試せる。</summary>
        public static void Define(string pseudoId, string payloadJson, bool hidden, double rating)
        {
            Put(pseudoId, payloadJson, hidden, true, rating);
        }

        /// <summary>
        /// GameRush の確認用の表示を真似て、公開プレイヤー状態の写しを1つ渡す。revision は 1、rating は無し、
        /// updatedAt は呼んだ時刻（UTC）。<see cref="Define(string, string)"/> で置いた状態とは別で、そちらは変えない。
        /// </summary>
        public static void Preview(string pseudoId, string payloadJson)
        {
            Preview(pseudoId, payloadJson, 1, null);
        }

        /// <summary>
        /// 確認用の表示の写しを、revision と rating を決めて渡す。<paramref name="rating"/> が null なら rating は無し
        /// （<c>GRushPlayerState.HasRating</c> が false）。
        ///
        /// <c>GRush.PlayerState.PreviewReceived</c> が次の <c>Update</c> で呼ばれる。購読が無いうちに渡した写しは、
        /// 次に購読したときに渡る。同じ写しは1回だけ渡る。
        /// 疑似 ID が空・payload が JSON オブジェクトでない・revision が負・rating が NaN か無限大のときは何もしない。
        /// </summary>
        public static void Preview(string pseudoId, string payloadJson, int revision, double? rating)
        {
            GRushMockPlayerStatePreview.Push(pseudoId, payloadJson, revision, rating);
        }

        /// <summary>置いた状態を消し、モックの相手の状態を置き直す。まだ届いていない確認用の表示の写しも捨てる。</summary>
        public static void Reset()
        {
            states.Clear();
            SeedRivals();
            GRushMockPlayerStatePreview.Reset();
        }

        internal static bool Exists(string pseudoId)
        {
            return pseudoId != null && states.ContainsKey(pseudoId);
        }

        internal static bool IsVisible(string pseudoId)
        {
            Entry entry;
            return pseudoId != null && states.TryGetValue(pseudoId, out entry) && !entry.Hidden;
        }

        /// <summary>
        /// 通報は伏せていない相手の状態にだけ届く。確認でプレイヤーが押す答えは
        /// <see cref="GRushMock.ConfirmPlayerStateReport"/> で決める。
        /// </summary>
        internal static GRushRpcResponse HandleReport(string paramsJson)
        {
            if (!GRushMock.SignedIn)
            {
                return GRushRpcResponse.Failure(GRushErrorCode.SignInRequired, "Reporting requires sign-in.");
            }
            var members = GRushMockJson.Members(paramsJson);
            string pseudoId = null;
            string raw;
            if (members != null && members.TryGetValue("pseudoId", out raw))
            {
                GRushMockJson.TryString(raw, out pseudoId);
            }
            if (!IsVisible(pseudoId))
            {
                return GRushRpcResponse.Failure(GRushErrorCode.InvalidParams, "No player state for that id.");
            }
            return GRushRpcResponse.Success(
                GRushMock.ConfirmPlayerStateReport ? "{\"reported\":true}" : "{\"reported\":false}"
            );
        }

        internal static bool IsRating(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value > -RatingLimit
                && value < RatingLimit;
        }

        private static void Put(
            string pseudoId,
            string payloadJson,
            bool hidden,
            bool hasRating,
            double rating
        )
        {
            states[pseudoId] = new Entry
            {
                PayloadJson = payloadJson,
                Hidden = hidden,
                Revision = 1,
                HasRating = hasRating,
                Rating = rating,
            };
        }

        private static void SeedRivals()
        {
            var rivals = GRushMock.Rivals;
            for (var index = 0; index < rivals.Length; index++)
            {
                Put(rivals[index], "{\"level\":" + ((index + 1) * 5) + "}", false, true, RivalRatings[index]);
            }
        }

        internal static string GetJson(string paramsJson)
        {
            var wire = GRushWire.Parse<GRushPlayerStateQueryWire>(paramsJson);
            var picked = new List<string>();
            if (wire != null && wire.pseudoIds != null)
            {
                foreach (var pseudoId in wire.pseudoIds)
                {
                    Entry entry;
                    // hidden は返さない（実サーバと同じ）。
                    if (pseudoId != null && states.TryGetValue(pseudoId, out entry) && !entry.Hidden)
                    {
                        picked.Add(pseudoId);
                    }
                }
            }
            return ListJson(picked);
        }

        /// <summary>
        /// 実サーバの相手探しと同じく、自分と伏せた状態を除いてランダムに引く。範囲を渡したときは、
        /// rating の無い状態を返さない。params が規則に合わなければ <paramref name="error"/> に文言を入れる。
        /// </summary>
        internal static string SampleJson(string paramsJson, string selfPseudoId, out string error)
        {
            error = null;
            var members = GRushMockJson.Members(string.IsNullOrEmpty(paramsJson) ? "{}" : paramsJson);
            if (members == null)
            {
                error = "Sample params must be a JSON object.";
                return null;
            }
            var count = SampleDefaultCount;
            string raw;
            if (members.TryGetValue("count", out raw) && !GRushMockJson.IsNull(raw))
            {
                if (!GRushMockJson.TryInt(raw, out count) || count < 1 || count > SampleMaxCount)
                {
                    error = "count must be an integer from 1 to " + SampleMaxCount + ".";
                    return null;
                }
            }
            string rawMin;
            string rawMax;
            var minGiven = members.TryGetValue("minRating", out rawMin) && !GRushMockJson.IsNull(rawMin);
            var maxGiven = members.TryGetValue("maxRating", out rawMax) && !GRushMockJson.IsNull(rawMax);
            double min = 0;
            double max = 0;
            if (minGiven != maxGiven)
            {
                error = "minRating and maxRating must be given together.";
                return null;
            }
            if (minGiven)
            {
                if (
                    !GRushMockJson.TryNumber(rawMin, out min)
                    || !GRushMockJson.TryNumber(rawMax, out max)
                    || !IsRating(min)
                    || !IsRating(max)
                )
                {
                    error = "Invalid rating range.";
                    return null;
                }
                if (min > max)
                {
                    error = "minRating must not exceed maxRating.";
                    return null;
                }
            }
            var candidates = new List<string>();
            foreach (var pair in states)
            {
                if (pair.Value.Hidden || pair.Key == selfPseudoId)
                {
                    continue;
                }
                if (minGiven && !(pair.Value.HasRating && pair.Value.Rating >= min && pair.Value.Rating <= max))
                {
                    continue;
                }
                candidates.Add(pair.Key);
            }
            for (var index = candidates.Count - 1; index > 0; index--)
            {
                var other = random.Next(index + 1);
                var swap = candidates[index];
                candidates[index] = candidates[other];
                candidates[other] = swap;
            }
            if (candidates.Count > count)
            {
                candidates.RemoveRange(count, candidates.Count - count);
            }
            return ListJson(candidates);
        }

        private static string ListJson(List<string> pseudoIds)
        {
            var builder = new StringBuilder("{\"states\":[");
            for (var index = 0; index < pseudoIds.Count; index++)
            {
                var entry = states[pseudoIds[index]];
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append('{');
                builder.Append("\"pseudoId\":").Append(GRushWire.Escape(pseudoIds[index]));
                builder.Append(",\"revision\":").Append(entry.Revision);
                builder.Append(",\"updatedAt\":\"\"");
                builder.Append(",\"rating\":").Append(entry.HasRating ? GRushWire.Number(entry.Rating) : "0");
                builder.Append(",\"hasRating\":").Append(entry.HasRating ? "true" : "false");
                builder.Append(",\"payload\":").Append(entry.PayloadJson ?? "null");
                builder.Append('}');
            }
            return builder.Append("]}").ToString();
        }
    }
}
