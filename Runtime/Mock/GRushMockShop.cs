using System;
using System.Collections.Generic;
using System.Text;

namespace GRushSdk
{
    /// <summary>
    /// エディタ用の作者アイテム販売。<see cref="Define"/> で置いた商品を <c>Shop.ProductsAsync</c> が返す。
    /// <c>Shop.PurchaseAsync</c> は決済画面へ移らず、すぐに付与を Pending に積んで Redirecting を返し、
    /// <c>Shop.PendingReceived</c> を呼ぶ（<see cref="GRushMock.ShopPurchaseStatus"/> が Redirecting のとき）。
    /// sku と ack の id は GameRush と同じ形で確かめる。
    /// <see cref="GRushMock.Reset"/> で付与は消えるが、置いた商品は残る（<see cref="Clear"/> で消す）。
    /// </summary>
    public static class GRushMockShop
    {
        private sealed class ProductRecord
        {
            public string Sku;
            public string Name;
            public int PriceJpy;
        }

        private sealed class GrantRecord
        {
            public string Id;
            public string Sku;
            public string CreatedAt;
            public bool Acked;
        }

        private static readonly List<ProductRecord> products = new List<ProductRecord>();
        private static readonly List<GrantRecord> grants = new List<GrantRecord>();

        // 付与の合図。GameRush と同じく、ハンドラは1つだけ持ち、ハンドラが無いうちに届いた合図は次に登録したハンドラへ1回だけ渡す。
        private static string signal;
        private static int sequence;
        private static int deliveredSequence;
        private static Action<string> handler;

        /// <summary>
        /// 商品を置く。同じ sku は置き換える。sku が <c>^[a-z0-9][a-z0-9_.-]{0,63}$</c> でないか、
        /// 名前が空か、価格が 1 未満なら置かずに false。
        /// </summary>
        public static bool Define(string sku, string name, int priceJpy)
        {
            if (!GRushShopRules.IsSku(sku) || string.IsNullOrEmpty(name) || priceJpy < 1)
            {
                return false;
            }
            products.RemoveAll(product => product.Sku == sku);
            products.Add(new ProductRecord { Sku = sku, Name = name, PriceJpy = priceJpy });
            return true;
        }

        /// <summary>置いた商品をすべて消す。</summary>
        public static void Clear()
        {
            products.Clear();
        }

        /// <summary>
        /// 購入を経ずに <paramref name="sku"/> の付与を Pending に積み、付与の合図を送る。積んだ付与の ID を返す。
        /// sku の形が違えば何もせず null。置いていない sku でも積める。
        /// </summary>
        public static string Grant(string sku)
        {
            if (!GRushShopRules.IsSku(sku))
            {
                return null;
            }
            var grant = new GrantRecord
            {
                Id = Guid.NewGuid().ToString(),
                Sku = sku,
                CreatedAt = GRushMockInbox.Timestamp(DateTime.UtcNow),
            };
            grants.Add(grant);
            signal = "{\"sku\":" + GRushWire.Escape(sku) + "}";
            sequence++;
            Deliver();
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
            for (var index = 0; index < products.Count; index++)
            {
                var product = products[index];
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"sku\":").Append(GRushWire.Escape(product.Sku));
                builder.Append(",\"name\":").Append(GRushWire.Escape(product.Name));
                builder.Append(",\"priceJpy\":").Append(product.PriceJpy).Append('}');
            }
            return GRushRpcResponse.Success(builder.Append("]}").ToString());
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
            if (products.Find(product => product.Sku == sku) == null)
            {
                return Invalid("No product for that sku.");
            }
            if (!GRushMock.SignedIn)
            {
                return GRushRpcResponse.Failure(GRushErrorCode.SignInRequired, "Purchases require sign-in.");
            }
            // Ack 済みを含め、付与済みの sku は買えない。
            if (grants.Exists(grant => grant.Sku == sku))
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
                default:
                    Grant(sku);
                    return GRushRpcResponse.Success("{\"status\":\"redirecting\"}");
            }
        }

        internal static GRushRpcResponse HandlePending()
        {
            return GrantsJson(false, MaxPending);
        }

        internal static GRushRpcResponse HandleOwned()
        {
            return GrantsJson(true, int.MaxValue);
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

        private static GRushRpcResponse GrantsJson(bool includeAcked, int limit)
        {
            var builder = new StringBuilder("{\"grants\":[");
            var first = true;
            var count = 0;
            foreach (var grant in grants)
            {
                if (grant.Acked && !includeAcked)
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
                builder.Append(",\"acked\":").Append(grant.Acked ? "true" : "false").Append('}');
            }
            return GRushRpcResponse.Success(builder.Append("]}").ToString());
        }
    }
}
