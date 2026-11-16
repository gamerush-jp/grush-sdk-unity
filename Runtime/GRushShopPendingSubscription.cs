using System;

namespace GRushSdk
{
    /// <summary>
    /// 購入の付与が届いた合図（GameRush の <c>grush:shop:pending</c>）を受け取るバックエンド。
    /// <see cref="SetShopPendingHandler"/> に null を渡すと受け取りをやめる。ハンドラには
    /// <c>{"sku":"..."}</c> の JSON を、次の <c>Update</c> で渡す。
    /// </summary>
    public interface IGRushShopBackend
    {
        void SetShopPendingHandler(Action<string> handler);
    }

    // 付与の合図の購読を、購読の数とバックエンドの差し替えに合わせてつなぐ。
    // 考え方は GRushPlayerStatePreviewSubscription と同じ。購読が1つ以上あるときだけバックエンドへ登録し、0 になったら外す。
    // バックエンドは合図をその時点のハンドラごと GRushDispatcher に積むので、差し替えの前に積まれた合図は世代で捨てる。
    // 購読を外しただけでは世代を変えない（GameRush は渡し済みの合図を登録し直したハンドラへ渡し直さない）。
    // UnityEngine に依存させないので、テストからこのファイルだけを取り出してコンパイルできる。
    internal sealed class GRushShopPendingSubscription
    {
        private readonly Action<string> deliver;
        private IGRushShopBackend bound;
        private int generation;

        public GRushShopPendingSubscription(Action<string> deliver)
        {
            this.deliver = deliver;
        }

        public bool Subscribed
        {
            get { return bound != null; }
        }

        // backend はショップを使えないときに null。
        public void Subscribe(IGRushShopBackend backend, bool hasListeners)
        {
            if (bound != null || !hasListeners || backend == null)
            {
                return;
            }
            bound = backend;
            var registered = generation;
            backend.SetShopPendingHandler(json =>
            {
                if (registered == generation)
                {
                    deliver(json);
                }
            });
        }

        public void Unsubscribe()
        {
            var previous = bound;
            if (previous == null)
            {
                return;
            }
            bound = null;
            previous.SetShopPendingHandler(null);
        }

        // バックエンドを差し替えた後に呼ぶ。前のバックエンドから外し、そこから届く合図を捨て、新しいバックエンドへつなぎ直す。
        public void Rebind(IGRushShopBackend backend, bool hasListeners)
        {
            Unsubscribe();
            generation++;
            Subscribe(backend, hasListeners);
        }
    }

    // GameRush と同じ sku と付与 ID の形。UnityEngine に依存させない。
    internal static class GRushShopRules
    {
        public const int MaxSkuLength = 64;
        public const int MaxAckIds = 100;

        // ^[a-z0-9][a-z0-9_.-]{0,63}$
        public static bool IsSku(string sku)
        {
            if (string.IsNullOrEmpty(sku) || sku.Length > MaxSkuLength)
            {
                return false;
            }
            for (var index = 0; index < sku.Length; index++)
            {
                var c = sku[index];
                var alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!alnum && (index == 0 || (c != '_' && c != '.' && c != '-')))
                {
                    return false;
                }
            }
            return true;
        }

        // 大文字と小文字を区別しない UUID（8-4-4-4-12 の16進）。
        public static bool IsUuid(string id)
        {
            if (id == null || id.Length != 36)
            {
                return false;
            }
            for (var index = 0; index < id.Length; index++)
            {
                var c = id[index];
                if (index == 8 || index == 13 || index == 18 || index == 23)
                {
                    if (c != '-')
                    {
                        return false;
                    }
                    continue;
                }
                var hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsAckIds(string[] ids)
        {
            if (ids == null || ids.Length == 0 || ids.Length > MaxAckIds)
            {
                return false;
            }
            foreach (var id in ids)
            {
                if (!IsUuid(id))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
