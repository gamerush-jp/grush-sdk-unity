using System;
using System.Collections.Generic;
using System.Text;

namespace GRushSdk
{
    /// <summary>抽選の中身の 1 件。確率はベーシスポイント（1 = 0.01%）で書く。</summary>
    [Serializable]
    public struct GRushMockShopOdds
    {
        /// <summary>当たるアイテムの sku。同じモックに置いた買い切りか消費の商品。</summary>
        public string Sku;

        /// <summary>当たる確率のベーシスポイント（1 以上の整数。1 = 0.01%）。1 つの抽選の合計は 10000。</summary>
        public int WeightBp;

        public GRushMockShopOdds(string sku, int weightBp)
        {
            Sku = sku;
            WeightBp = weightBp;
        }
    }

    /// <summary>
    /// エディタ用の作者アイテム販売。<see cref="Define(string, string, GRushShopPrice, GRushShopItemType)"/> と
    /// <see cref="DefineLottery"/> で置いた商品を <c>Shop.ProductsAsync</c> が返す。
    /// <c>Shop.PurchaseAsync</c> は決済画面へ移らず、すぐに付与を Pending に積んで Redirecting を返し、
    /// <c>Shop.PendingReceived</c> を呼ぶ（<see cref="GRushMock.ShopPurchaseStatus"/> が Redirecting のとき）。
    /// GameRush と同じく、買い切りの商品は付与済みなら AlreadyOwned で、消費の商品は何度でも買えて
    /// <c>Shop.OwnedAsync</c> に入らない。抽選の商品は確率どおりに（<see cref="GRushMock.ShopLotteryDraw"/> を置けばそのアイテムを）
    /// 1 つ引いて付与し、当たりが持っている買い切りのアイテムなら、かぶりの扱いに従って代わりのアイテムを渡すか、もう 1 つ渡す
    /// （<c>Shop.OwnedAsync</c> の Count が増える）。sku と ack の id は GameRush と同じ形で確かめる。
    /// <see cref="GRushMock.Reset"/> で付与は消えるが、置いた商品は残る（<see cref="Clear"/> で消す）。
    /// </summary>
    public static class GRushMockShop
    {
        private sealed class ProductRecord
        {
            public string Sku;
            public string Name;
            public int PriceJpy;
            public GRushShopItemType Type;

            // 抽選だけが持つ。
            public GRushMockShopOdds[] Odds;
            public GRushShopDuplicatePolicy Policy;
            public string FallbackSku;
        }

        // GameRush と同じ抽選の規則。
        private const int LotteryBpTotal = 10000;
        private const int LotteryMinEntries = 2;
        private const int LotteryMaxEntries = 30;
        private static readonly Random random = new Random();

        private sealed class GrantRecord
        {
            public string Id;
            public string Sku;
            public string CreatedAt;
            public bool Acked;
            public bool Consumable;
        }

        private static readonly List<ProductRecord> products = new List<ProductRecord>();
        private static readonly List<GrantRecord> grants = new List<GrantRecord>();

        // 付与の合図。GameRush と同じく、ハンドラは1つだけ持ち、ハンドラが無いうちに届いた合図は次に登録したハンドラへ1回だけ渡す。
        private static string signal;
        private static int sequence;
        private static int deliveredSequence;
        private static Action<string> handler;

        /// <summary>
        /// 買い切り（<see cref="GRushShopItemType.Permanent"/>）の商品を置く。
        /// <see cref="Define(string, string, GRushShopPrice, GRushShopItemType)"/> と同じ。
        /// </summary>
        public static bool Define(string sku, string name, GRushShopPrice price)
        {
            return Put(sku, name, (int)price, GRushShopItemType.Permanent);
        }

        /// <summary>
        /// 種類を決めて商品を置く。同じ sku は置き換える。sku が <c>^[a-z0-9][a-z0-9_.-]{0,63}$</c> でないか、
        /// 名前が空か、価格が <see cref="GRushShopPrice"/> に無い値（int から変換した値など）か、
        /// 種類が Permanent でも Consumable でもなければ置かずに false。抽選は <see cref="DefineLottery"/> で置く。
        /// </summary>
        public static bool Define(string sku, string name, GRushShopPrice price, GRushShopItemType type)
        {
            return Put(sku, name, (int)price, type);
        }

        /// <summary>
        /// 買い切りの商品を置く。価格が <see cref="GRushShopPrice"/> の額でなければ置かずに false。
        /// </summary>
        [Obsolete("価格は GRushShopPrice で渡す。Define(string, string, GRushShopPrice) を使う。")]
        public static bool Define(string sku, string name, int priceJpy)
        {
            return Put(sku, name, priceJpy, GRushShopItemType.Permanent);
        }

        /// <summary>
        /// 種類を決めて商品を置く。価格が <see cref="GRushShopPrice"/> の額でなければ置かずに false。
        /// </summary>
        [Obsolete("価格は GRushShopPrice で渡す。Define(string, string, GRushShopPrice, GRushShopItemType) を使う。")]
        public static bool Define(string sku, string name, int priceJpy, GRushShopItemType type)
        {
            return Put(sku, name, priceJpy, type);
        }

        /// <summary>
        /// 抽選の商品を置く。GameRush の Studio で登録するときと同じく、中身と確率を <paramref name="odds"/>（2〜30 件の別々の
        /// sku で、<see cref="GRushMockShopOdds.WeightBp"/> は 1 以上、合計 10000）で、かぶりの扱いを <paramref name="policy"/> で、
        /// <see cref="GRushShopDuplicatePolicy.Fallback"/> のときに代わりに渡すアイテムを <paramref name="fallbackSku"/> で渡す
        /// （<see cref="GRushShopDuplicatePolicy.Stack"/> では null）。同じ sku は置き換える。形が合わなければ置かずに false。
        /// 中身は同じモックに置いた買い切りか消費の商品、<paramref name="fallbackSku"/> は消費の商品で、そうでない抽選は
        /// <c>Shop.ProductsAsync</c> に出ず、買えない（中身より先に抽選を置いてもよい）。
        /// </summary>
        public static bool DefineLottery(
            string sku,
            string name,
            GRushShopPrice price,
            GRushMockShopOdds[] odds,
            GRushShopDuplicatePolicy policy,
            string fallbackSku = null
        )
        {
            if (!IsOdds(odds))
            {
                return false;
            }
            if (policy == GRushShopDuplicatePolicy.Stack ? fallbackSku != null : policy != GRushShopDuplicatePolicy.Fallback || !GRushShopRules.IsSku(fallbackSku))
            {
                return false;
            }
            return Put(sku, name, (int)price, GRushShopItemType.Lottery, (GRushMockShopOdds[])odds.Clone(), policy, fallbackSku);
        }

        private static bool IsOdds(GRushMockShopOdds[] odds)
        {
            if (odds == null || odds.Length < LotteryMinEntries || odds.Length > LotteryMaxEntries)
            {
                return false;
            }
            var seen = new HashSet<string>();
            var total = 0;
            foreach (var entry in odds)
            {
                if (!GRushShopRules.IsSku(entry.Sku) || !seen.Add(entry.Sku) || entry.WeightBp < 1 || entry.WeightBp > LotteryBpTotal)
                {
                    return false;
                }
                total += entry.WeightBp;
            }
            return total == LotteryBpTotal;
        }

        private static bool Put(
            string sku,
            string name,
            int priceJpy,
            GRushShopItemType type,
            GRushMockShopOdds[] odds = null,
            GRushShopDuplicatePolicy policy = GRushShopDuplicatePolicy.None,
            string fallbackSku = null
        )
        {
            if (
                !GRushShopRules.IsSku(sku)
                || string.IsNullOrEmpty(name)
                || !GRushShopRules.IsPrice(priceJpy)
                || GRushWire.ShopItemTypeWire(type) == null
                || (type == GRushShopItemType.Lottery) != (odds != null)
            )
            {
                return false;
            }
            products.RemoveAll(product => product.Sku == sku);
            products.Add(
                new ProductRecord
                {
                    Sku = sku,
                    Name = name,
                    PriceJpy = priceJpy,
                    Type = type,
                    Odds = odds,
                    Policy = policy,
                    FallbackSku = fallbackSku,
                }
            );
            return true;
        }

        // 抽選の中身（買い切りか消費の商品）と代わりのアイテム（消費の商品）が置いてあるか。GameRush は中身か代わりの
        // どれかが販売中でない抽選を返さず、売らない。
        private static bool IsSellable(ProductRecord product)
        {
            if (product.Type != GRushShopItemType.Lottery)
            {
                return true;
            }
            foreach (var entry in product.Odds)
            {
                var content = products.Find(item => item.Sku == entry.Sku);
                if (content == null || content.Type == GRushShopItemType.Lottery)
                {
                    return false;
                }
            }
            if (product.Policy != GRushShopDuplicatePolicy.Fallback)
            {
                return true;
            }
            var fallback = products.Find(item => item.Sku == product.FallbackSku);
            return fallback != null && fallback.Type == GRushShopItemType.Consumable;
        }

        /// <summary>
        /// 確率の表 <paramref name="odds"/> から、<paramref name="sample"/>（0 以上 1 未満）で 1 つを引いて sku を返す。
        /// </summary>
        internal static string Draw(GRushMockShopOdds[] odds, double sample)
        {
            var rest = (int)Math.Floor(sample * LotteryBpTotal);
            foreach (var entry in odds)
            {
                if (rest < entry.WeightBp)
                {
                    return entry.Sku;
                }
                rest -= entry.WeightBp;
            }
            return odds[odds.Length - 1].Sku;
        }

        private static string NameOf(string sku)
        {
            var product = products.Find(item => item.Sku == sku);
            return product == null ? sku : product.Name;
        }

        /// <summary>置いた商品をすべて消す。</summary>
        public static void Clear()
        {
            products.Clear();
        }

        /// <summary>
        /// 購入を経ずに <paramref name="sku"/> の付与を Pending に積み、付与の合図を送る。積んだ付与の ID を返す。
        /// sku の形が違えば何もせず null。置いていない sku でも積める。置いた消費の商品の付与は
        /// <c>Shop.OwnedAsync</c> に入らない。<see cref="DefineLottery"/> で置いた抽選の sku では、購入と同じく 1 つを引いて、
        /// 当たり（かぶったときは代わり）を積む。GameRush は抽選の商品そのものを付与しない。中身か代わりが置かれていない
        /// 抽選の sku では何もせず null。
        /// </summary>
        public static string Grant(string sku)
        {
            var product = products.Find(item => item.Sku == sku);
            if (product != null && product.Type == GRushShopItemType.Lottery)
            {
                if (!IsSellable(product))
                {
                    return null;
                }
                sku = DrawGrant(product).Sku;
            }
            var id = AddGrant(sku);
            if (id != null)
            {
                signal = "{\"sku\":" + GRushWire.Escape(sku) + "}";
                sequence++;
                Deliver();
            }
            return id;
        }

        private static string AddGrant(string sku)
        {
            if (!GRushShopRules.IsSku(sku))
            {
                return null;
            }
            var product = products.Find(item => item.Sku == sku);
            var grant = new GrantRecord
            {
                Id = Guid.NewGuid().ToString(),
                Sku = sku,
                CreatedAt = GRushMockInbox.Timestamp(DateTime.UtcNow),
                Consumable = product != null && product.Type == GRushShopItemType.Consumable,
            };
            grants.Add(grant);
            return grant.Id;
        }

        internal static void Reset()
        {
            grants.Clear();
            signal = null;
            sequence = 0;
            deliveredSequence = 0;
        }

        // null を渡すと、今のハンドラが expected のときだけ外す。別のバックエンドが置き換えた後に、前のバックエンドが外しても消えない。
        internal static void SetHandler(Action<string> next, Action<string> expected)
        {
            if (next == null)
            {
                if (handler == expected)
                {
                    handler = null;
                }
                return;
            }
            handler = next;
            Deliver();
        }

        private static void Deliver()
        {
            var current = handler;
            if (current == null || signal == null || deliveredSequence == sequence)
            {
                return;
            }
            deliveredSequence = sequence;
            current(signal);
        }

        private static GRushRpcResponse Invalid(string message)
        {
            return GRushRpcResponse.Failure(GRushErrorCode.InvalidParams, message);
        }

        internal static GRushRpcResponse HandleProducts()
        {
            if (!GRushMock.ShopEnabled)
            {
                return GRushRpcResponse.Success("{\"enabled\":false,\"products\":[]}");
            }
            var builder = new StringBuilder("{\"enabled\":true,\"products\":[");
            var first = true;
            foreach (var product in products)
            {
                if (!IsSellable(product))
                {
                    continue;
                }
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                builder.Append("{\"sku\":").Append(GRushWire.Escape(product.Sku));
                builder.Append(",\"name\":").Append(GRushWire.Escape(product.Name));
                builder.Append(",\"priceJpy\":").Append(product.PriceJpy);
                builder.Append(",\"type\":");
                builder.Append(GRushWire.Escape(GRushWire.ShopItemTypeWire(product.Type)));
                if (product.Type == GRushShopItemType.Lottery)
                {
                    AppendLottery(builder, product);
                }
                builder.Append('}');
            }
            return GRushRpcResponse.Success(builder.Append("]}").ToString());
        }

        // GameRush と同じく、中身を名前と百分率で、代わりのアイテムを名前つきで返す。
        private static void AppendLottery(StringBuilder builder, ProductRecord product)
        {
            builder.Append(",\"odds\":[");
            for (var index = 0; index < product.Odds.Length; index++)
            {
                var entry = product.Odds[index];
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"sku\":").Append(GRushWire.Escape(entry.Sku));
                builder.Append(",\"name\":").Append(GRushWire.Escape(NameOf(entry.Sku)));
                builder.Append(",\"percent\":");
                builder.Append((entry.WeightBp / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.Append('}');
            }
            builder.Append("],\"duplicatePolicy\":");
            if (product.Policy == GRushShopDuplicatePolicy.Stack)
            {
                builder.Append("\"stack\",\"fallback\":null");
                return;
            }
            builder.Append("\"fallback\",\"fallback\":{\"sku\":").Append(GRushWire.Escape(product.FallbackSku));
            builder.Append(",\"name\":").Append(GRushWire.Escape(NameOf(product.FallbackSku))).Append('}');
        }

        internal static GRushRpcResponse HandlePurchase(string paramsJson)
        {
            var members = GRushMockJson.Members(paramsJson);
            string sku = null;
            string raw;
            if (members != null && members.TryGetValue("sku", out raw))
            {
                GRushMockJson.TryString(raw, out sku);
            }
            if (!GRushShopRules.IsSku(sku))
            {
                return Invalid("Invalid sku.");
            }
            // アプリと LINE では買えない。
            if (!GRushMock.ShopEnabled)
            {
                return GRushRpcResponse.Failure(
                    GRushErrorCode.Unsupported,
                    "Purchases are available only on the GameRush website."
                );
            }
            var product = products.Find(item => item.Sku == sku);
            if (product == null || !IsSellable(product))
            {
                return Invalid("No product for that sku.");
            }
            if (!GRushMock.SignedIn)
            {
                return GRushRpcResponse.Failure(GRushErrorCode.SignInRequired, "Purchases require sign-in.");
            }
            // 買い切りの商品は、Ack 済みを含め付与済みなら買えない。消費と抽選の商品は何度でも買える。
            if (product.Type == GRushShopItemType.Permanent && grants.Exists(grant => grant.Sku == sku))
            {
                return GRushRpcResponse.Success("{\"status\":\"alreadyOwned\"}");
            }
            switch (GRushMock.ShopPurchaseStatus)
            {
                case GRushShopPurchaseStatus.Cancelled:
                    return GRushRpcResponse.Success("{\"status\":\"cancelled\"}");
                case GRushShopPurchaseStatus.AlreadyOwned:
                    return GRushRpcResponse.Success("{\"status\":\"alreadyOwned\"}");
                case GRushShopPurchaseStatus.Unknown:
                    return GRushRpcResponse.Success("{\"status\":\"unknown\"}");
                case GRushShopPurchaseStatus.Purchased:
                    // iOS のアプリと同じく、決済画面へ移らずに付与を Pending に積む。戻りの合図は無い。
                    return GRushRpcResponse.Success(PurchasedJson(product));
                case GRushShopPurchaseStatus.Pending:
                    // 承認を待っている間は、まだ付与しない。
                    return GRushRpcResponse.Success("{\"status\":\"pending\"}");
                default:
                    Grant(product.Type == GRushShopItemType.Lottery ? DrawGrant(product).Sku : sku);
                    return GRushRpcResponse.Success("{\"status\":\"redirecting\"}");
            }
        }

        private struct LotteryDraw
        {
            public string Sku;
            public string DrawnSku;
            public bool Fallback;
        }

        // 抽選を 1 回引く。当たりが持っている買い切りなら、Fallback では代わりのアイテムを、Stack では当たりをもう 1 つ渡す。
        private static LotteryDraw DrawGrant(ProductRecord product)
        {
            string drawn = null;
            foreach (var entry in product.Odds)
            {
                if (entry.Sku == GRushMock.ShopLotteryDraw)
                {
                    drawn = entry.Sku;
                }
            }
            if (drawn == null)
            {
                lock (random)
                {
                    drawn = Draw(product.Odds, random.NextDouble());
                }
            }
            var content = products.Find(item => item.Sku == drawn);
            var fallback =
                product.Policy == GRushShopDuplicatePolicy.Fallback
                && (content == null || content.Type != GRushShopItemType.Consumable)
                && grants.Exists(grant => grant.Sku == drawn);
            return new LotteryDraw
            {
                Sku = fallback ? product.FallbackSku : drawn,
                DrawnSku = drawn,
                Fallback = fallback,
            };
        }

        private static string PurchasedJson(ProductRecord product)
        {
            if (product.Type != GRushShopItemType.Lottery)
            {
                AddGrant(product.Sku);
                return "{\"status\":\"purchased\",\"sku\":" + GRushWire.Escape(product.Sku) + "}";
            }
            var draw = DrawGrant(product);
            AddGrant(draw.Sku);
            return "{\"status\":\"purchased\",\"sku\":" + GRushWire.Escape(draw.Sku)
                + ",\"drawnSku\":" + GRushWire.Escape(draw.DrawnSku)
                + ",\"fallback\":" + (draw.Fallback ? "true" : "false") + "}";
        }

        internal static GRushRpcResponse HandlePending()
        {
            return GrantsJson(false, true, MaxPending, false);
        }

        // GameRush と同じく、消費のアイテムの付与は owned に入れず、買い切りのアイテムごとに 1 件にする。count は持っている数
        // （抽選の Stack で同じアイテムがまた当たると増える）で、id・createdAt・acked は最初の付与のもの。
        internal static GRushRpcResponse HandleOwned()
        {
            return GrantsJson(true, false, int.MaxValue, true);
        }

        // GameRush の pending は1回に 100 件まで返す。
        private const int MaxPending = 100;

        internal static GRushRpcResponse HandleAck(string paramsJson)
        {
            var wire = GRushWire.Parse<GRushIdsWire>(paramsJson);
            if (wire == null || !GRushShopRules.IsAckIds(wire.ids))
            {
                return Invalid("ids must be 1 to 100 grant ids.");
            }
            var ids = new HashSet<string>();
            foreach (var id in wire.ids)
            {
                ids.Add(id.ToLowerInvariant());
            }
            var acked = 0;
            foreach (var grant in grants)
            {
                if (!grant.Acked && ids.Contains(grant.Id.ToLowerInvariant()))
                {
                    grant.Acked = true;
                    acked++;
                }
            }
            return GRushRpcResponse.Success("{\"acked\":" + acked + "}");
        }

        private static GRushRpcResponse GrantsJson(bool includeAcked, bool includeConsumable, int limit, bool perItem)
        {
            var builder = new StringBuilder("{\"grants\":[");
            var first = true;
            var count = 0;
            var seen = new HashSet<string>();
            foreach (var grant in grants)
            {
                if ((grant.Acked && !includeAcked) || (grant.Consumable && !includeConsumable))
                {
                    continue;
                }
                if (perItem && !seen.Add(grant.Sku))
                {
                    continue;
                }
                if (count++ >= limit)
                {
                    break;
                }
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                builder.Append("{\"id\":").Append(GRushWire.Escape(grant.Id));
                builder.Append(",\"sku\":").Append(GRushWire.Escape(grant.Sku));
                builder.Append(",\"createdAt\":").Append(GRushWire.Escape(grant.CreatedAt));
                builder.Append(",\"acked\":").Append(grant.Acked ? "true" : "false");
                if (perItem)
                {
                    var sku = grant.Sku;
                    builder.Append(",\"count\":").Append(grants.FindAll(item => item.Sku == sku).Count);
                }
                builder.Append('}');
            }
            return GRushRpcResponse.Success(builder.Append("]}").ToString());
        }
    }
}
