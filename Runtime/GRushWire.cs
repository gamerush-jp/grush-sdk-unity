using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GRushSdk
{
    [Serializable]
    internal class GRushProfileWire
    {
        public string displayName;
        public string avatarUrl;
    }

    [Serializable]
    internal class GRushPlayerWire
    {
        public string pseudoId;
        public bool isGuest;
        public bool profileConsent;
        public GRushProfileWire profile;
    }

    [Serializable]
    internal class GRushPeerWire
    {
        public int index;
        public string pseudoId;
        public string displayName;
        public string avatarUrl;
    }

    [Serializable]
    internal class GRushJoinWire
    {
        public string roomId;
        public string roomCode;
        public int localPeerIndex;
        public GRushPeerWire[] peers;
        public int hostIndex;
        public int epoch;
        public string transport;
        public double serverTimeMs;
    }

    [Serializable]
    internal class GRushErrorWire
    {
        public string code;
        public string message;
    }

    [Serializable]
    internal class GRushPlayerStateWire
    {
        public string pseudoId;
        public int revision;
        public string updatedAt;

        // JsonUtility は null の数値を読めないので、Web の層とモックが hasRating を添える。
        public double rating;
        public bool hasRating;
    }

    [Serializable]
    internal class GRushInboxMessageWire
    {
        public string id;
        public string from;
        public string kind;
        public string payloadJson;
        public string createdAt;
        public string readAt;
    }

    [Serializable]
    internal class GRushInboxPageWire
    {
        public GRushInboxMessageWire[] messages;
        public int unreadCount;
        public string nextCursor;
    }

    [Serializable]
    internal class GRushSentWire
    {
        public bool sent;
    }

    [Serializable]
    internal class GRushUpdatedWire
    {
        public int updated;
    }

    [Serializable]
    internal class GRushAckedWire
    {
        public int acked;
    }

    [Serializable]
    internal class GRushIdsWire
    {
        public string[] ids;
    }

    // 任意の形の offer・want・give は、Web の層とモックが JSON の文字列にして *Json に入れる。
    [Serializable]
    internal class GRushTradeWire
    {
        public string id;
        public string from;
        public string to;
        public string taker;
        public string offerJson;
        public string wantJson;
        public string offerKey;
        public string wantKey;
        public string giveJson;
        public string status;
        public string createdAt;
        public string expiresAt;
        public string settledAt;
        public string role;
        public string action;
    }

    [Serializable]
    internal class GRushTradeEnvelope
    {
        public GRushTradeWire trade;
    }

    [Serializable]
    internal class GRushTradePageWire
    {
        public GRushTradeWire[] trades;
        public string nextCursor;
    }

    [Serializable]
    internal class GRushMyTradesWire
    {
        public GRushTradeWire[] open;
        public GRushTradeWire[] pending;
    }

    [Serializable]
    internal class GRushTradeAcceptWire
    {
        public string outcome;
        public GRushTradeWire trade;
    }

    [Serializable]
    internal class GRushReportResultWire
    {
        public bool reported;
    }

    [Serializable]
    internal class GRushPlayerStateQueryWire
    {
        public string[] pseudoIds;
    }

    [Serializable]
    internal class GRushPlayerStateEnvelope
    {
        public GRushPlayerStateWire state;
    }

    [Serializable]
    internal class GRushPlayerStateListEnvelope
    {
        public GRushPlayerStateWire[] states;
    }

    [Serializable]
    internal class GRushLeaderboardWire
    {
        public string key;
        public string title;
        public string sort;
        public string valueType;
        public string aggregation;
        public string period;
        public double minValue;
        public double maxValue;
    }

    [Serializable]
    internal class GRushLeaderboardListWire
    {
        public GRushLeaderboardWire[] leaderboards;
    }

    [Serializable]
    internal class GRushLeaderboardEntryWire
    {
        public int rank;
        public string pseudoId;
        public string displayName;
        public string avatarUrl;
        public bool isGuest;
        public double value;
        public string submittedAt;
        public bool isSelf;
    }

    [Serializable]
    internal class GRushLeaderboardPageWire
    {
        public string key;
        public string title;
        public string sort;
        public string valueType;
        public string period;
        public string periodKey;
        public bool verified;
        public GRushLeaderboardEntryWire[] entries;
        public int total;
    }

    [Serializable]
    internal class GRushLeaderboardPageEnvelope
    {
        public GRushLeaderboardPageWire leaderboard;
    }

    [Serializable]
    internal class GRushScoreResultWire
    {
        public bool accepted;
        public bool updated;
        public double value;
        public int rank;
        public bool verified;
    }

    [Serializable]
    internal class GRushScoreResultEnvelope
    {
        public GRushScoreResultWire result;
    }

    [Serializable]
    internal class GRushSubmitParamsWire
    {
        public string key;
        public double value;
    }

    [Serializable]
    internal class GRushLeaderboardQueryWire
    {
        public string key;
        public int limit;
        public int offset;
        public int range;
    }

    [Serializable]
    internal class GRushJoinParamsWire
    {
        public string mode;
        public string roomCode;
        public int targetRoomSize;
    }

    internal static class GRushWire
    {
        public static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var c in value)
            {
                if (c == '"' || c == '\\')
                {
                    builder.Append('\\').Append(c);
                }
                else if (c < ' ')
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4"));
                }
                else
                {
                    builder.Append(c);
                }
            }
            return builder.Append('"').ToString();
        }

        public static string JoinParams(string mode, string roomCode, int targetRoomSize)
        {
            var builder = new StringBuilder("{");
            builder.Append("\"mode\":").Append(Escape(string.IsNullOrEmpty(mode) ? "default" : mode));
            if (!string.IsNullOrEmpty(roomCode))
            {
                builder.Append(",\"roomCode\":").Append(Escape(roomCode));
            }
            if (targetRoomSize != 0)
            {
                builder.Append(",\"targetRoomSize\":").Append(targetRoomSize);
            }
            return builder.Append('}').ToString();
        }

        public static GRushPlayer ToPlayer(GRushPlayerWire wire)
        {
            var player = new GRushPlayer
            {
                PseudoId = wire == null ? null : wire.pseudoId,
                IsGuest = wire != null && wire.isGuest,
                ProfileConsent = wire != null && wire.profileConsent,
            };
            if (wire != null && wire.profileConsent && wire.profile != null)
            {
                player.DisplayName = wire.profile.displayName;
                player.AvatarUrl = wire.profile.avatarUrl;
            }
            return player;
        }

        public static GRushPeer ToPeer(GRushPeerWire wire)
        {
            return new GRushPeer
            {
                Index = wire.index,
                PseudoId = wire.pseudoId,
                DisplayName = wire.displayName,
                AvatarUrl = wire.avatarUrl,
            };
        }

        public static string LeaderboardQuery(string key, int limit, int offset, int range)
        {
            var builder = new StringBuilder("{");
            builder.Append("\"key\":").Append(Escape(key ?? string.Empty));
            if (limit > 0)
            {
                builder.Append(",\"limit\":").Append(limit);
            }
            if (offset > 0)
            {
                builder.Append(",\"offset\":").Append(offset);
            }
            if (range > 0)
            {
                builder.Append(",\"range\":").Append(range);
            }
            return builder.Append('}').ToString();
        }

        public static string SubmitParams(
            string key,
            double value,
            string metadataJson,
            string operationId
        )
        {
            var builder = new StringBuilder("{");
            builder.Append("\"key\":").Append(Escape(key ?? string.Empty));
            builder
                .Append(",\"value\":")
                .Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(metadataJson))
            {
                // metadata は呼び出し側が組み立てた生 JSON をそのまま載せる。
                // 壊れた JSON はサーバが 400 で弾く。
                builder.Append(",\"metadata\":").Append(metadataJson);
            }
            if (!string.IsNullOrEmpty(operationId))
            {
                builder.Append(",\"operationId\":").Append(Escape(operationId));
            }
            return builder.Append('}').ToString();
        }

        public static GRushLeaderboard ToLeaderboard(GRushLeaderboardWire wire)
        {
            return new GRushLeaderboard
            {
                Key = wire.key,
                Title = wire.title,
                Sort = wire.sort,
                ValueType = wire.valueType,
                Aggregation = wire.aggregation,
                Period = wire.period,
                MinValue = wire.minValue,
                MaxValue = wire.maxValue,
            };
        }

        public static GRushLeaderboardPage ToLeaderboardPage(
            GRushLeaderboardPageWire wire,
            string rawJson
        )
        {
            var entries = new GRushLeaderboardEntry[wire.entries == null ? 0 : wire.entries.Length];
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = wire.entries[index];
                entries[index] = new GRushLeaderboardEntry
                {
                    Rank = entry.rank,
                    PseudoId = entry.pseudoId,
                    DisplayName = entry.displayName,
                    AvatarUrl = entry.avatarUrl,
                    IsGuest = entry.isGuest,
                    Value = entry.value,
                    SubmittedAt = entry.submittedAt,
                    IsSelf = entry.isSelf,
                };
            }
            return new GRushLeaderboardPage
            {
                Key = wire.key,
                Title = wire.title,
                Sort = wire.sort,
                ValueType = wire.valueType,
                Period = wire.period,
                PeriodKey = wire.periodKey,
                Verified = wire.verified,
                Entries = entries,
                Total = wire.total,
                RawJson = rawJson,
            };
        }

        public static string PlayerStateParams(string payloadJson, int baseRevision)
        {
            return PlayerStateParams(payloadJson, baseRevision, false, null);
        }

        /// <summary>
        /// <paramref name="sendRating"/> が true のときだけ rating を載せる（null は消す）。
        /// false なら rating の項目を持たず、サーバは今の rating を変えない。
        /// </summary>
        public static string PlayerStateParams(
            string payloadJson,
            int baseRevision,
            bool sendRating,
            double? rating
        )
        {
            var builder = new StringBuilder("{");
            if (baseRevision >= 0)
            {
                builder.Append("\"baseRevision\":").Append(baseRevision).Append(',');
            }
            if (sendRating)
            {
                builder
                    .Append("\"rating\":")
                    .Append(rating.HasValue ? Number(rating.Value) : "null")
                    .Append(',');
            }
            // payload は呼び出し側が組み立てた生 JSON をそのまま載せる。
            // 壊れた JSON と 4KB 超はサーバが 400 で弾く。
            // **必ず最後に置く。** そうすると取り出しが「"payload": から
            // 末尾の } まで」で厳密に決まり、中身に何が入っても壊れない。
            builder
                .Append("\"payload\":")
                .Append(string.IsNullOrEmpty(payloadJson) ? "null" : payloadJson);
            return builder.Append('}').ToString();
        }

        public static string PlayerStateReportParams(string pseudoId)
        {
            return "{\"pseudoId\":" + Escape(pseudoId ?? string.Empty) + "}";
        }

        public static string PlayerStateQuery(string[] pseudoIds)
        {
            var builder = new StringBuilder("{\"pseudoIds\":[");
            if (pseudoIds != null)
            {
                for (var index = 0; index < pseudoIds.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    builder.Append(Escape(pseudoIds[index] ?? string.Empty));
                }
            }
            return builder.Append("]}").ToString();
        }

        public static GRushPlayerState ToPlayerState(GRushPlayerStateWire wire)
        {
            return new GRushPlayerState
            {
                PseudoId = wire.pseudoId,
                Revision = wire.revision,
                UpdatedAt = wire.updatedAt,
                Rating = wire.hasRating ? wire.rating : 0,
                HasRating = wire.hasRating,
            };
        }

        public static string Number(double value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // 呼び出し側が組み立てた JSON をそのまま載せる。空は null として送り、ランタイムが弾く。
        private static string Raw(string json)
        {
            return string.IsNullOrEmpty(json) ? "null" : json;
        }

        public static string SampleParams(int count, bool hasRange, double minRating, double maxRating)
        {
            var fields = new List<string>();
            if (count > 0)
            {
                fields.Add("\"count\":" + count);
            }
            if (hasRange)
            {
                fields.Add("\"minRating\":" + Number(minRating));
                fields.Add("\"maxRating\":" + Number(maxRating));
            }
            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        public static string StringParams(string name, string value)
        {
            return "{\"" + name + "\":" + Escape(value ?? string.Empty) + "}";
        }

        public static string IdsParams(string[] ids)
        {
            var builder = new StringBuilder("{\"ids\":[");
            if (ids != null)
            {
                for (var index = 0; index < ids.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }
                    builder.Append(Escape(ids[index] ?? string.Empty));
                }
            }
            return builder.Append("]}").ToString();
        }

        public static string InboxSendParams(string to, string kind, string payloadJson)
        {
            return "{\"to\":"
                + Escape(to ?? string.Empty)
                + ",\"kind\":"
                + Escape(kind ?? string.Empty)
                + ",\"payload\":"
                + Raw(payloadJson)
                + "}";
        }

        public static string InboxListParams(bool unreadOnly, int limit, string cursor)
        {
            var fields = new List<string>();
            if (unreadOnly)
            {
                fields.Add("\"unreadOnly\":true");
            }
            if (limit > 0)
            {
                fields.Add("\"limit\":" + limit);
            }
            if (!string.IsNullOrEmpty(cursor))
            {
                fields.Add("\"cursor\":" + Escape(cursor));
            }
            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        public static string TradeCreateParams(
            string offerJson,
            string wantJson,
            string operationId,
            GRushTradeOptions options
        )
        {
            var fields = new List<string>
            {
                "\"offer\":" + Raw(offerJson),
                "\"want\":" + Raw(wantJson),
                "\"operationId\":" + Escape(operationId ?? string.Empty),
            };
            if (!string.IsNullOrEmpty(options.OfferKey))
            {
                fields.Add("\"offerKey\":" + Escape(options.OfferKey));
            }
            if (!string.IsNullOrEmpty(options.WantKey))
            {
                fields.Add("\"wantKey\":" + Escape(options.WantKey));
            }
            if (!string.IsNullOrEmpty(options.To))
            {
                fields.Add("\"to\":" + Escape(options.To));
            }
            if (options.ExpiresInSec > 0)
            {
                fields.Add("\"expiresInSec\":" + options.ExpiresInSec);
            }
            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        public static string TradeListParams(string offerKey, string wantKey, int limit, string cursor)
        {
            var fields = new List<string>();
            if (!string.IsNullOrEmpty(offerKey))
            {
                fields.Add("\"offerKey\":" + Escape(offerKey));
            }
            if (!string.IsNullOrEmpty(wantKey))
            {
                fields.Add("\"wantKey\":" + Escape(wantKey));
            }
            if (limit > 0)
            {
                fields.Add("\"limit\":" + limit);
            }
            if (!string.IsNullOrEmpty(cursor))
            {
                fields.Add("\"cursor\":" + Escape(cursor));
            }
            return "{" + string.Join(",", fields.ToArray()) + "}";
        }

        public static string TradeAcceptParams(string tradeId, string giveJson)
        {
            return "{\"tradeId\":" + Escape(tradeId ?? string.Empty) + ",\"give\":" + Raw(giveJson) + "}";
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static GRushInboxMessage ToInboxMessage(GRushInboxMessageWire wire)
        {
            return new GRushInboxMessage
            {
                Id = wire.id,
                From = wire.from,
                Kind = wire.kind,
                PayloadJson = string.IsNullOrEmpty(wire.payloadJson) ? "{}" : wire.payloadJson,
                CreatedAt = wire.createdAt,
                ReadAt = NullIfEmpty(wire.readAt),
                IsRead = !string.IsNullOrEmpty(wire.readAt),
            };
        }

        public static GRushTrade ToTrade(GRushTradeWire wire)
        {
            return new GRushTrade
            {
                Id = wire.id,
                From = NullIfEmpty(wire.from),
                To = NullIfEmpty(wire.to),
                Taker = NullIfEmpty(wire.taker),
                OfferJson = string.IsNullOrEmpty(wire.offerJson) ? "{}" : wire.offerJson,
                WantJson = string.IsNullOrEmpty(wire.wantJson) ? "{}" : wire.wantJson,
                OfferKey = NullIfEmpty(wire.offerKey),
                WantKey = NullIfEmpty(wire.wantKey),
                GiveJson = NullIfEmpty(wire.giveJson),
                Status = TradeStatusOf(wire.status),
                CreatedAt = wire.createdAt,
                ExpiresAt = wire.expiresAt,
                SettledAt = NullIfEmpty(wire.settledAt),
            };
        }

        public static GRushTrade[] ToTrades(GRushTradeWire[] wires)
        {
            var source = wires ?? new GRushTradeWire[0];
            var trades = new List<GRushTrade>(source.Length);
            foreach (var wire in source)
            {
                if (wire != null && !string.IsNullOrEmpty(wire.id))
                {
                    trades.Add(ToTrade(wire));
                }
            }
            return trades.ToArray();
        }

        public static GRushPendingTrade ToPendingTrade(GRushTradeWire wire)
        {
            return new GRushPendingTrade
            {
                Trade = ToTrade(wire),
                Role =
                    wire.role == "offerer" ? GRushTradeRole.Offerer
                    : wire.role == "taker" ? GRushTradeRole.Taker
                    : GRushTradeRole.Unknown,
                Action =
                    wire.action == "receiveGive" ? GRushTradeAction.ReceiveGive
                    : wire.action == "returnOffer" ? GRushTradeAction.ReturnOffer
                    : wire.action == "receiveOffer" ? GRushTradeAction.ReceiveOffer
                    : GRushTradeAction.Unknown,
            };
        }

        public static GRushTradeStatus TradeStatusOf(string status)
        {
            switch (status)
            {
                case "open":
                    return GRushTradeStatus.Open;
                case "matched":
                    return GRushTradeStatus.Matched;
                case "cancelled":
                    return GRushTradeStatus.Cancelled;
                case "expired":
                    return GRushTradeStatus.Expired;
                default:
                    return GRushTradeStatus.Unknown;
            }
        }

        public static bool TryTradeOutcome(string outcome, out GRushTradeOutcome value)
        {
            switch (outcome)
            {
                case "matched":
                    value = GRushTradeOutcome.Matched;
                    return true;
                case "taken":
                    value = GRushTradeOutcome.Taken;
                    return true;
                case "expired":
                    value = GRushTradeOutcome.Expired;
                    return true;
                case "cancelled":
                    value = GRushTradeOutcome.Cancelled;
                    return true;
                case "notFound":
                    value = GRushTradeOutcome.NotFound;
                    return true;
                default:
                    value = GRushTradeOutcome.NotFound;
                    return false;
            }
        }

        /// <summary>
        /// <see cref="PlayerStateParams"/> が組んだ params から payload を取り出す。
        /// payload は必ず末尾にあるので、手書きの JSON パーサを持たずに済む。
        /// </summary>
        public static string ExtractPayloadJson(string paramsJson)
        {
            if (string.IsNullOrEmpty(paramsJson))
            {
                return null;
            }
            const string marker = "\"payload\":";
            var start = paramsJson.IndexOf(marker, System.StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }
            start += marker.Length;
            var end = paramsJson.LastIndexOf('}');
            if (end <= start)
            {
                return null;
            }
            return paramsJson.Substring(start, end - start).Trim();
        }

        public static T Parse<T>(string json)
            where T : class
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
