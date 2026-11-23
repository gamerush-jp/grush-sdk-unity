using System;
using System.Threading.Tasks;

namespace GRushSdk
{
    /// <summary>
    /// 作者アイテム販売。作者が登録した商品をプレイヤーが買い、届いた付与をゲームがセーブへ反映する。
    /// 商品名・価格・種類は作者が Studio か API キーで登録し、ゲームは <see cref="ProductsAsync"/> で読む。
    ///
    /// 種類は買い切り（<see cref="GRushShopItemType.Permanent"/>）と消費（<see cref="GRushShopItemType.Consumable"/>）の
    /// 2 つ。消費のアイテムは何度でも買え、<see cref="OwnedAsync"/> に入らない。消費のアイテムを、ゲーム内通貨
    /// （ほかのアイテムや機能と交換する対価）と抽選（偶然で中身が決まる仕組み）に使ってはならない。
    ///
    /// 購入できるのは Web の GameRush と iOS のアプリ。Android のアプリと LINE では <see cref="PurchaseAsync"/> が
    /// Unsupported、<see cref="ProductsAsync"/> は Enabled が false で空で、<see cref="PendingAsync"/>・
    /// <see cref="AckAsync"/>・<see cref="OwnedAsync"/> も Unsupported。
    ///
    /// 取りこぼさないために、次の順で使う。
    /// 1. <see cref="PurchaseAsync"/> を呼ぶ前にセーブを保存する。Redirecting が返るとページはすぐ決済画面へ移る。
    /// 2. 起動時、<see cref="PendingReceived"/> のとき、<see cref="PurchaseAsync"/> が Purchased を返したときに
    ///    <see cref="PendingAsync"/> を読む。
    /// 3. 付与をセーブへ反映して保存してから <see cref="AckAsync"/> する。Ack は「ゲームへ届けた」ことを表し、
    ///    Ack するまで付与は Pending に残る。
    /// 4. 反映した付与の ID をセーブに持ち、同じ付与を二度反映しない。消費のアイテムは <see cref="OwnedAsync"/> で
    ///    組み直せないので、特に大事。
    ///
    /// ゲームの中での使用は改ざんを防げない。GameRush が保証するのは、払った人に付与が届くことまで。
    ///
    /// <see cref="GRush.IsShopAvailable"/> が false（<c>protocolVersion</c> 12 未満）では、どの呼び出しも
    /// Unsupported で、<see cref="PendingReceived"/> は呼ばれない。
    /// </summary>
    public sealed class GRushShopApi
    {
        private Action<string> pendingReceived;
        private GRushShopPendingSubscription pendingSubscription;

        /// <summary>
        /// 決済から戻って付与が届いたときに呼ばれる。引数は買った商品の sku。受け取ったら
        /// <see cref="PendingAsync"/> を読んで反映する。
        ///
        /// 次の <c>Update</c> で呼ばれる。購読より前に届いた合図は、購読したときに1回だけ渡る。
        /// 購読が1つ以上あるあいだだけ GameRush から受け取り、すべて外すと受け取りをやめる。
        /// 合図を取りこぼしても付与は Pending に残るので、起動時にも <see cref="PendingAsync"/> を読む。
        /// <see cref="GRush.UseBackend"/> でバックエンドを差し替えても、登録済みの購読は続く。
        /// </summary>
        public event Action<string> PendingReceived
        {
            add
            {
                pendingReceived += value;
                PendingSubscription().Subscribe(PendingBackend(), pendingReceived != null);
            }
            remove
            {
                pendingReceived -= value;
                if (pendingReceived == null)
                {
                    PendingSubscription().Unsubscribe();
                }
            }
        }

        // GRush.UseBackend の後に呼ぶ。登録済みの PendingReceived を新しいバックエンドへつなぎ直す。
        internal void RebindPending()
        {
            PendingSubscription().Rebind(PendingBackend(), pendingReceived != null);
        }

        private GRushShopPendingSubscription PendingSubscription()
        {
            if (pendingSubscription == null)
            {
                pendingSubscription = new GRushShopPendingSubscription(OnPending);
            }
            return pendingSubscription;
        }

        private static IGRushShopBackend PendingBackend()
        {
            return GRush.IsShopAvailable ? GRush.Backend as IGRushShopBackend : null;
        }

        private void OnPending(string json)
        {
            var handler = pendingReceived;
            if (handler == null)
            {
                return;
            }
            var wire = GRushWire.Parse<GRushShopPendingWire>(json);
            handler(wire == null || string.IsNullOrEmpty(wire.sku) ? null : wire.sku);
        }

        /// <summary>
        /// 販売中の商品と、その種類（<see cref="GRushShopProduct.Type"/>）。Android のアプリと LINE では Enabled が false で、
        /// Products は空。
        /// </summary>
        public async Task<GRushResult<GRushShopCatalog>> ProductsAsync()
        {
            if (!GRush.IsShopAvailable)
            {
                return GRushResult<GRushShopCatalog>.Unsupported();
            }
            var response = await GRush.CallAsync("shop.products", "{}");
            if (!response.Ok)
            {
                return GRushResult<GRushShopCatalog>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushShopProductsWire>(response.Value);
            if (wire == null || wire.products == null)
            {
                return GRushResult<GRushShopCatalog>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable product list."
                );
            }
            return GRushResult<GRushShopCatalog>.Success(
                new GRushShopCatalog
                {
                    Enabled = wire.enabled,
                    // 買えない面では、ランタイムが商品を返しても見せない。
                    Products = wire.enabled
                        ? GRushWire.ToShopProducts(wire.products)
                        : new GRushShopProduct[0],
                }
            );
        }

        /// <summary>
        /// <paramref name="sku"/> の商品を買う。**呼ぶ前にセーブを保存すること。** Redirecting が返ると
        /// ページはすぐ決済画面へ移り、付与は戻ってから Pending に届く（Web）。iOS のアプリでは Purchased
        /// （付与が Pending に入った）か Pending（確かめ・承認の待ち）が返る。買い切りの商品をもう持っていれば
        /// AlreadyOwned で、消費の商品では返らない。sku が
        /// <c>^[a-z0-9][a-z0-9_.-]{0,63}$</c> でなければ送らずに InvalidParams。Android のアプリと LINE では Unsupported。
        /// GameRush の確認と決済を待つので、ほかの呼び出しより長く（最大 10 分）待つ。
        /// </summary>
        public async Task<GRushResult<GRushShopPurchaseResult>> PurchaseAsync(string sku)
        {
            if (!GRush.IsShopAvailable)
            {
                return GRushResult<GRushShopPurchaseResult>.Unsupported();
            }
            if (!GRushShopRules.IsSku(sku))
            {
                return GRushResult<GRushShopPurchaseResult>.Failure(
                    GRushErrorCode.InvalidParams,
                    "Invalid sku."
                );
            }
            var response = await GRush.CallAsync("shop.purchase", GRushWire.StringParams("sku", sku));
            if (!response.Ok)
            {
                return GRushResult<GRushShopPurchaseResult>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushShopPurchaseWire>(response.Value);
            if (wire == null)
            {
                return GRushResult<GRushShopPurchaseResult>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable purchase result."
                );
            }
            // 新しい GameRush が足した status でも購入は進んでいることがあるので、失敗にせず Unknown と生の値で返す。
            return GRushResult<GRushShopPurchaseResult>.Success(
                new GRushShopPurchaseResult
                {
                    Status = GRushWire.ShopPurchaseStatusOf(wire.status),
                    RawStatus = wire.status,
                }
            );
        }

        /// <summary>
        /// まだ Ack していない付与。1 回に最大 100 件。セーブへ反映して保存してから <see cref="AckAsync"/> し、
        /// 残りがあればもう一度読む。
        /// </summary>
        public Task<GRushResult<GRushShopGrant[]>> PendingAsync()
        {
            return GrantsAsync("shop.pending");
        }

        /// <summary>
        /// 付与をゲームへ届けたことを記録し、記録した件数を返す。<paramref name="ids"/> は 1〜100 件の付与 ID（UUID）で、
        /// 合わなければ送らずに InvalidParams。**セーブへ反映して保存し、反映した付与の ID をセーブに持ってから呼ぶこと。**
        /// Ack した付与は Pending に戻らない。
        /// </summary>
        public async Task<GRushResult<int>> AckAsync(string[] ids)
        {
            if (!GRush.IsShopAvailable)
            {
                return GRushResult<int>.Unsupported();
            }
            if (!GRushShopRules.IsAckIds(ids))
            {
                return GRushResult<int>.Failure(
                    GRushErrorCode.InvalidParams,
                    "ids must be 1 to 100 grant ids."
                );
            }
            var response = await GRush.CallAsync("shop.ack", GRushWire.IdsParams(ids));
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
        /// このゲームで受け取った買い切りの付与のすべて（Ack 済みを含む）。返金された付与と、消費のアイテムの付与は入らない。
        /// 機種を替えたときなど、セーブを組み直すときに読む。
        /// </summary>
        public Task<GRushResult<GRushShopGrant[]>> OwnedAsync()
        {
            return GrantsAsync("shop.owned");
        }

        private static async Task<GRushResult<GRushShopGrant[]>> GrantsAsync(string method)
        {
            if (!GRush.IsShopAvailable)
            {
                return GRushResult<GRushShopGrant[]>.Unsupported();
            }
            var response = await GRush.CallAsync(method, "{}");
            if (!response.Ok)
            {
                return GRushResult<GRushShopGrant[]>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushShopGrantsWire>(response.Value);
            if (wire == null || wire.grants == null)
            {
                return GRushResult<GRushShopGrant[]>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned unreadable grants."
                );
            }
            return GRushResult<GRushShopGrant[]>.Success(GRushWire.ToShopGrants(wire.grants));
        }
    }
}
