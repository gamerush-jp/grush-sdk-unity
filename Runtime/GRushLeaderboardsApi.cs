using System.Threading.Tasks;

namespace GRushSdk
{
    /// <summary>
    /// ランキング。定義は作者が Studio で宣言するもので、ゲームからは作れない
    /// （作らせると値域も並び順も後から書き換えられて異常検知が成立しない）。
    /// ゲームができるのは、宣言済みの枠へ投稿することと読むことだけ。
    ///
    /// スコアは自己申告であり GameRush は値を検証していない。応答の
    /// <c>Verified</c> は常に false で、表示側はそれを隠さないこと。
    /// </summary>
    public sealed class GRushLeaderboardsApi
    {
        public async Task<GRushResult<GRushLeaderboard[]>> ListAsync()
        {
            var response = await GRush.CallAsync("leaderboard.list", null);
            if (!response.Ok)
            {
                return GRushResult<GRushLeaderboard[]>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushLeaderboardListWire>(response.Value);
            if (wire == null || wire.leaderboards == null)
            {
                return GRushResult<GRushLeaderboard[]>.Success(new GRushLeaderboard[0]);
            }
            var boards = new GRushLeaderboard[wire.leaderboards.Length];
            for (var index = 0; index < boards.Length; index++)
            {
                boards[index] = GRushWire.ToLeaderboard(wire.leaderboards[index]);
            }
            return GRushResult<GRushLeaderboard[]>.Success(boards);
        }

        public Task<GRushResult<GRushScoreResult>> SubmitAsync(string key, double value)
        {
            return SubmitAsync(key, value, null, null);
        }

        public Task<GRushResult<GRushScoreResult>> SubmitAsync(
            string key,
            double value,
            string metadataJson
        )
        {
            return SubmitAsync(key, value, metadataJson, null);
        }

        /// <summary>
        /// スコアを投稿する。<paramref name="metadataJson"/> は 1KB 以下の
        /// 生 JSON（省略可）。session は親が付けるのでゲームは指定できない。
        /// 有効プレイが 10 秒に満たないセッションからの投稿は保存されない。
        ///
        /// <paramref name="operationId"/> は同じ投稿の再送を弾く識別子。
        /// **集約が sum のランキングで投稿を再送するなら必ず渡すこと** —
        /// 加算は冪等でないため、応答が失われて送り直すと二重に足され、
        /// 集約後の値からは元へ戻せない。再送では同じ値を渡す。
        ///
        /// <paramref name="metadataJson"/> と <paramref name="operationId"/> の文字列にそのまま入った、
        /// 対になっていないサロゲート（UTF-16 の文字列を文字の途中で切った切れ端）は、U+FFFD に替えて送る。
        /// 切れ端だけが違う値は同じ値になる。<paramref name="metadataJson"/> に JSON のエスケープ
        /// （<c>\ud83d</c> など）で書いた切れ端はそのまま届き、GameRush が <c>InvalidParams</c> で断る。
        /// </summary>
        public async Task<GRushResult<GRushScoreResult>> SubmitAsync(
            string key,
            double value,
            string metadataJson,
            string operationId
        )
        {
            var response = await GRush.CallAsync(
                "leaderboard.submit",
                GRushWire.SubmitParams(key, value, metadataJson, operationId)
            );
            if (!response.Ok)
            {
                return GRushResult<GRushScoreResult>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushScoreResultEnvelope>(response.Value);
            if (wire == null || wire.result == null)
            {
                return GRushResult<GRushScoreResult>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable score result."
                );
            }
            return GRushResult<GRushScoreResult>.Success(
                new GRushScoreResult
                {
                    Accepted = wire.result.accepted,
                    Updated = wire.result.updated,
                    Value = wire.result.value,
                    Rank = wire.result.rank,
                    HasRank = wire.result.rank > 0,
                    Verified = wire.result.verified,
                }
            );
        }

        public Task<GRushResult<GRushLeaderboardPage>> TopAsync(string key)
        {
            return TopAsync(key, 0, 0);
        }

        public Task<GRushResult<GRushLeaderboardPage>> TopAsync(string key, int limit)
        {
            return TopAsync(key, limit, 0);
        }

        public Task<GRushResult<GRushLeaderboardPage>> TopAsync(string key, int limit, int offset)
        {
            return CallPageAsync(
                "leaderboard.top",
                GRushWire.LeaderboardQuery(key, limit, offset, 0)
            );
        }

        public Task<GRushResult<GRushLeaderboardPage>> AroundMeAsync(string key)
        {
            return AroundMeAsync(key, 0);
        }

        /// <summary>
        /// 自分の前後を返す。まだ自分が載っていないときは
        /// <c>Entries</c> が空になる（先頭を返すと「自分が1位」と読めてしまう）。
        /// </summary>
        public Task<GRushResult<GRushLeaderboardPage>> AroundMeAsync(string key, int range)
        {
            return CallPageAsync(
                "leaderboard.aroundMe",
                GRushWire.LeaderboardQuery(key, 0, 0, range)
            );
        }

        /// <summary>
        /// 相互フォローだけに絞ったランキング。ゲームごとのフレンド同意が
        /// 前提で、同意の仕組みが入るまでは <c>ConsentDeclined</c> を返す。
        /// </summary>
        public Task<GRushResult<GRushLeaderboardPage>> FriendsAsync(string key)
        {
            return CallPageAsync(
                "leaderboard.friends",
                GRushWire.LeaderboardQuery(key, 0, 0, 0)
            );
        }

        private static async Task<GRushResult<GRushLeaderboardPage>> CallPageAsync(
            string method,
            string paramsJson
        )
        {
            var response = await GRush.CallAsync(method, paramsJson);
            if (!response.Ok)
            {
                return GRushResult<GRushLeaderboardPage>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushLeaderboardPageEnvelope>(response.Value);
            if (wire == null || wire.leaderboard == null)
            {
                return GRushResult<GRushLeaderboardPage>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable leaderboard."
                );
            }
            return GRushResult<GRushLeaderboardPage>.Success(
                GRushWire.ToLeaderboardPage(wire.leaderboard, response.Value)
            );
        }
    }
}
