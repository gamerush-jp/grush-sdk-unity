using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace GRushSdk.Editor
{
    internal sealed class GRushGameSummary
    {
        public string Id;
        public string Title;
        public string Visibility;
        public string ReviewStatus;
        public string RequestedVisibility;
    }

    internal sealed class GRushLeaderboardDraft
    {
        public string Key = "";
        public string Title = "";
        public int SortIndex;
        public int ValueTypeIndex;
        public int AggregationIndex;
        public int PeriodIndex;
        public bool HasRange;
        public double MinValue;
        public double MaxValue = 1000000;

        public static readonly string[] Sorts = { "desc", "asc" };
        public static readonly string[] ValueTypes = { "int", "float", "duration_ms" };
        public static readonly string[] Aggregations = { "best", "last", "sum" };
        public static readonly string[] Periods = { "all_time", "daily", "weekly", "monthly" };

        public string ToRequestBody()
        {
            var builder = new StringBuilder("{");
            builder.Append("\"title\":").Append(GRushJsonText.Escape(Title));
            builder.Append(",\"sort\":").Append(GRushJsonText.Escape(Sorts[SortIndex]));
            builder
                .Append(",\"valueType\":")
                .Append(GRushJsonText.Escape(ValueTypes[ValueTypeIndex]));
            builder
                .Append(",\"aggregation\":")
                .Append(GRushJsonText.Escape(Aggregations[AggregationIndex]));
            builder.Append(",\"period\":").Append(GRushJsonText.Escape(Periods[PeriodIndex]));
            if (HasRange)
            {
                builder.Append(",\"minValue\":").Append(GRushJsonText.Number(MinValue));
                builder.Append(",\"maxValue\":").Append(GRushJsonText.Number(MaxValue));
            }
            return builder.Append('}').ToString();
        }
    }

    internal static class GRushEditorApi
    {
        public static IEnumerator ListGames(
            GRushEditorClient client,
            Action<List<GRushGameSummary>> onDone,
            Action<string> onError
        )
        {
            var slot = new GRushHttpSlot();
            yield return client.Get("/api/games", slot);
            if (!slot.Result.Ok || slot.Result.Json == null)
            {
                onError("ゲームの一覧を取得できませんでした: " + slot.Result.Message());
                yield break;
            }
            var games = new List<GRushGameSummary>();
            foreach (var game in slot.Result.Json.Get("games").Items)
            {
                games.Add(
                    new GRushGameSummary
                    {
                        Id = game.Get("id").AsString(""),
                        Title = game.Get("title").AsString(""),
                        Visibility = game.Get("visibility").AsString(""),
                        ReviewStatus = game.Get("review_status").AsString(""),
                        RequestedVisibility = game.Get("requested_visibility").AsString(null),
                    }
                );
            }
            onDone(games);
        }

        // 空の説明と遊び方も送る。サーバが空を「未記入」として保存する。
        internal static string CreateGameBody(
            string title,
            string description,
            string howToPlay,
            string visibility,
            bool declareAllAges
        )
        {
            var body = new StringBuilder("{");
            body.Append("\"title\":").Append(GRushJsonText.Escape(title));
            body.Append(",\"description\":").Append(GRushJsonText.Escape(description));
            body.Append(",\"howToPlay\":").Append(GRushJsonText.Escape(howToPlay));
            body.Append(",\"visibility\":").Append(GRushJsonText.Escape(visibility));
            body.Append(",\"acceptTerms\":true");
            body.Append(",\"confirmAssetRights\":true");
            if (declareAllAges)
            {
                body.Append(",\"declareAllAges\":true");
            }
            return body.Append('}').ToString();
        }

        public static IEnumerator CreateGame(
            GRushEditorClient client,
            string title,
            string description,
            string howToPlay,
            string visibility,
            bool declareAllAges,
            Action<GRushGameSummary> onDone,
            Action<string> onError
        )
        {
            var slot = new GRushHttpSlot();
            yield return client.Post(
                "/api/games",
                CreateGameBody(title, description, howToPlay, visibility, declareAllAges),
                slot
            );
            var errorCode =
                slot.Result.Json == null ? "" : slot.Result.Json.Get("code").AsString("");
            if (!slot.Result.Ok && errorCode == "rights.confirmRequired")
            {
                onError(
                    "サーバが素材の権利の確認を受け付けませんでした。GameRush SDK を最新版に更新してから、もう一度作成してください。"
                );
                yield break;
            }
            // 手元の数え方とずれて GameRush が下限で断ったときも、どの欄が足りないかを日本語で返す。
            if (!slot.Result.Ok && (errorCode == "description.tooShort" || errorCode == "howToPlay.tooShort"))
            {
                onError(
                    "ゲームを作れませんでした。"
                        + (errorCode == "description.tooShort" ? "説明" : "遊び方")
                        + "は、改行と前後の空白を除いて "
                        + GRushEditorIntroduction.MinLength
                        + " 文字以上にしてください。"
                );
                yield break;
            }
            if (!slot.Result.Ok && errorCode == "ageRating.declarationRequired")
            {
                onError(
                    "ゲームを作れませんでした。全年齢向けの内容であることの申告が要ります。申告のチェックを入れてから、もう一度作成してください。"
                );
                yield break;
            }
            if (!slot.Result.Ok || slot.Result.Json == null)
            {
                onError(
                    "ゲームを作れませんでした: "
                        + slot.Result.Message()
                        + "（自動では再試行しません。Studio に作られていないか確認してください）"
                );
                yield break;
            }
            var game = slot.Result.Json.Get("game");
            onDone(
                new GRushGameSummary
                {
                    Id = game.Get("id").AsString(""),
                    Title = game.Get("title").AsString(title),
                    Visibility = game.Get("visibility").AsString(""),
                    ReviewStatus = game.Get("review_status").AsString(""),
                    RequestedVisibility = game.Get("requested_visibility").AsString(null),
                }
            );
        }

        public static IEnumerator DeclareLeaderboard(
            GRushEditorClient client,
            string gameId,
            GRushLeaderboardDraft draft,
            Action onDone,
            Action<string> onError
        )
        {
            var slot = new GRushHttpSlot();
            yield return client.Put(
                "/api/games/" + gameId + "/leaderboards/" + Uri.EscapeDataString(draft.Key),
                draft.ToRequestBody(),
                slot
            );
            if (!slot.Result.Ok)
            {
                onError("ランキングを宣言できませんでした: " + slot.Result.Message());
                yield break;
            }
            onDone();
        }
    }
}
