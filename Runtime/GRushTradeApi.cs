using System.Threading.Tasks;

namespace GRushSdk
{
    /// <summary>
    /// 交換所。プレイヤーがアイテムを出品し、同じゲームの別のプレイヤーが受ける。出品も受諾も
    /// ゲストができる。GameRush はアイテムの正当性を確かめず、交換が片方だけ成立しないことと、
    /// 終わった取引を取りこぼさないことだけを保証する。
    ///
    /// **offer・want・give で文章や画像は送れない。** どれも JSON オブジェクトで、文字列の値とキーは
    /// 識別子（<c>^[A-Za-z0-9_.:-]{1,32}$</c>）だけ、ほかは数値・真偽値・null。入れ子は最上位を
    /// 1 段目として 4 段まで、値は合計 64 個まで。詰めた JSON で offer 2KB・want 1KB・give 2KB まで。
    /// 利用者どうしの文章や画像のやりとりには使わないこと。
    ///
    /// 取りこぼさないために、次の順で使う。
    /// 1. 出品の前に、出すアイテムを手元のセーブで仮置き（使えない状態）にする。
    /// 2. operationId をセーブに保存してから <see cref="CreateAsync(string, string, string)"/> を呼ぶ。
    /// 3. 応答が失われたら、同じ operationId で呼び直す。同じ operationId の再送は先に作った出品を返す。
    /// 4. <see cref="MineAsync"/> の Pending をセーブに反映し、セーブを永続化してから
    ///    <see cref="AckAsync"/> する（ack に渡せる id は 1 回 50 件まで）。
    /// 5. 反映済みの取引 ID をセーブに持ち、同じ取引を二度反映しない。
    ///
    /// <see cref="GRush.IsTradeAvailable"/> が false の古いランタイムでは、どの呼び出しも Unsupported。
    /// </summary>
    public sealed class GRushTradeApi
    {
        public Task<GRushResult<GRushTrade>> CreateAsync(
            string offerJson,
            string wantJson,
            string operationId
        )
        {
            return CreateAsync(offerJson, wantJson, operationId, default(GRushTradeOptions));
        }

        /// <summary>
        /// 出品する。<paramref name="operationId"/> は <c>^[A-Za-z0-9._:-]{1,64}$</c> の出品の識別子で、
        /// 呼ぶ前にセーブへ保存しておく。<paramref name="options"/> で一覧の絞り込みの key、
        /// 指名の相手、期限を渡せる。
        /// 受付中の出品が 10 件あるときは InvalidParams（待っても直らない。取り下げるか、成立・期限切れを待つ）。
        /// 毎日 30 件の出品の上限は RateLimited（時間が経てば回復する）。
        /// </summary>
        public async Task<GRushResult<GRushTrade>> CreateAsync(
            string offerJson,
            string wantJson,
            string operationId,
            GRushTradeOptions options
        )
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<GRushTrade>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "trade.create",
                GRushWire.TradeCreateParams(offerJson, wantJson, operationId, options)
            );
            return ToSingle(response);
        }

        public Task<GRushResult<GRushTradePage>> ListAsync()
        {
            return ListAsync(null, null, 0, null);
        }

        public Task<GRushResult<GRushTradePage>> ListAsync(string offerKey, string wantKey)
        {
            return ListAsync(offerKey, wantKey, 0, null);
        }

        /// <summary>
        /// 他人の公開の出品と自分宛ての指名の出品のうち、受付中のものを新しい順に返す。
        /// key は null なら絞らない。<paramref name="limit"/> は 1〜50（0 なら 20）。
        /// </summary>
        public async Task<GRushResult<GRushTradePage>> ListAsync(
            string offerKey,
            string wantKey,
            int limit,
            string cursor
        )
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<GRushTradePage>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "trade.list",
                GRushWire.TradeListParams(offerKey, wantKey, limit, cursor)
            );
            if (!response.Ok)
            {
                return GRushResult<GRushTradePage>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushTradePageWire>(response.Value);
            if (wire == null || wire.trades == null)
            {
                return GRushResult<GRushTradePage>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable trade list."
                );
            }
            return GRushResult<GRushTradePage>.Success(
                new GRushTradePage
                {
                    Trades = GRushWire.ToTrades(wire.trades),
                    NextCursor = string.IsNullOrEmpty(wire.nextCursor) ? null : wire.nextCursor,
                }
            );
        }

        /// <summary>
        /// 自分の受付中の出品（Open）と、終わったがまだ Ack していない取引（Pending、終わった順）。
        /// Pending の Action に従ってセーブへ反映し、保存してから <see cref="AckAsync"/> する。
        /// </summary>
        public async Task<GRushResult<GRushMyTrades>> MineAsync()
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<GRushMyTrades>.Unsupported();
            }
            var response = await GRush.CallAsync("trade.mine", "{}");
            if (!response.Ok)
            {
                return GRushResult<GRushMyTrades>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushMyTradesWire>(response.Value);
            if (wire == null || wire.open == null || wire.pending == null)
            {
                return GRushResult<GRushMyTrades>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned unreadable trades."
                );
            }
            var pending = new System.Collections.Generic.List<GRushPendingTrade>();
            foreach (var trade in wire.pending)
            {
                if (trade != null && !string.IsNullOrEmpty(trade.id))
                {
                    pending.Add(GRushWire.ToPendingTrade(trade));
                }
            }
            return GRushResult<GRushMyTrades>.Success(
                new GRushMyTrades
                {
                    Open = GRushWire.ToTrades(wire.open),
                    Pending = pending.ToArray(),
                }
            );
        }

        /// <summary>
        /// 出品を受ける。<paramref name="giveJson"/> は渡すもの。先に取られた・期限切れ・取り下げ・
        /// 見えない出品はエラーではなく Outcome で返る。同じ取引への再送は Matched を返す。
        /// </summary>
        public async Task<GRushResult<GRushTradeAcceptResult>> AcceptAsync(
            string tradeId,
            string giveJson
        )
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<GRushTradeAcceptResult>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "trade.accept",
                GRushWire.TradeAcceptParams(tradeId, giveJson)
            );
            if (!response.Ok)
            {
                return GRushResult<GRushTradeAcceptResult>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushTradeAcceptWire>(response.Value);
            GRushTradeOutcome outcome;
            if (wire == null || !GRushWire.TryTradeOutcome(wire.outcome, out outcome))
            {
                return GRushResult<GRushTradeAcceptResult>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable accept result."
                );
            }
            // JsonUtility は無い入れ子のオブジェクトも空で作るので、id で有無を見る。
            var hasTrade = wire.trade != null && !string.IsNullOrEmpty(wire.trade.id);
            return GRushResult<GRushTradeAcceptResult>.Success(
                new GRushTradeAcceptResult
                {
                    Outcome = outcome,
                    HasTrade = hasTrade,
                    Trade = hasTrade ? GRushWire.ToTrade(wire.trade) : default(GRushTrade),
                }
            );
        }

        /// <summary>
        /// 自分の受付中の出品を取り下げる。期限を過ぎていれば Expired になり、終わっていれば今の状態を
        /// そのまま返す。出したアイテムは Pending の ReturnOffer で戻す。
        /// </summary>
        public async Task<GRushResult<GRushTrade>> CancelAsync(string tradeId)
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<GRushTrade>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "trade.cancel",
                GRushWire.StringParams("tradeId", tradeId)
            );
            return ToSingle(response);
        }

        /// <summary>
        /// Pending の取引の受け取りを確かめ、記録した件数を返す（1 回 50 件まで）。
        /// **セーブへ反映して保存してから呼ぶこと。** Ack した取引は Pending に戻らない。
        /// </summary>
        public async Task<GRushResult<int>> AckAsync(string[] ids)
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<int>.Unsupported();
            }
            var response = await GRush.CallAsync("trade.ack", GRushWire.IdsParams(ids));
            if (!response.Ok)
            {
                return GRushResult<int>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushAckedWire>(response.Value);
            if (wire == null)
            {
                return GRushResult<int>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable count."
                );
            }
            return GRushResult<int>.Success(wire.acked);
        }

        /// <summary>
        /// 出品を通報する。**ゲームは通報 UI を描けない。** 確認は GameRush が出し、プレイヤーが
        /// 承諾したら true。通報すると出品者をブロックし、その相手のメッセージと出品は見えなくなる。
        /// ゲストは SignInRequired。
        /// </summary>
        public async Task<GRushResult<bool>> ReportAsync(string tradeId)
        {
            if (!GRush.IsTradeAvailable)
            {
                return GRushResult<bool>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "trade.report",
                GRushWire.StringParams("tradeId", tradeId)
            );
            if (!response.Ok)
            {
                return GRushResult<bool>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushReportResultWire>(response.Value);
            return GRushResult<bool>.Success(wire != null && wire.reported);
        }

        private static GRushResult<GRushTrade> ToSingle(GRushResult<string> response)
        {
            if (!response.Ok)
            {
                return GRushResult<GRushTrade>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushTradeEnvelope>(response.Value);
            if (wire == null || wire.trade == null || string.IsNullOrEmpty(wire.trade.id))
            {
                return GRushResult<GRushTrade>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable trade."
                );
            }
            return GRushResult<GRushTrade>.Success(GRushWire.ToTrade(wire.trade));
        }
    }
}
