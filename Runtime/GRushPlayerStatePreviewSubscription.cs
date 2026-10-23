using System;

namespace GRushSdk
{
    /// <summary>
    /// 確認用の表示の写しを受け取るバックエンド。<see cref="SetPlayerStatePreviewHandler"/> に null を渡すと
    /// 受け取りをやめる。ハンドラには getMine の応答と同じ形の JSON（<c>{"state":{...}}</c>）を、
    /// 次の <c>Update</c> で渡す。
    /// </summary>
    public interface IGRushPlayerStatePreviewBackend
    {
        void SetPlayerStatePreviewHandler(Action<string> handler);
    }

    // 確認用の表示の購読を、購読の数とバックエンドの差し替えに合わせてつなぐ。
    // 購読が1つ以上あるときだけバックエンドへ登録し、0 になったら外す。
    // バックエンドは写しをその時点のハンドラごと GRushDispatcher に積むので、差し替えの前に積まれた写しが
    // 差し替えの後に届く。登録したときの世代を閉じ込め、今の世代と違う写しは捨てる。
    // 購読を外しただけでは世代を変えない。外す前に積まれた写しは、届いた時点の購読へ渡す（GameRush は
    // 渡し済みの写しを登録し直したハンドラへ渡し直さないので、ここで捨てると取りこぼす）。
    // UnityEngine に依存させないので、テストからこのファイルだけを取り出してコンパイルできる。
    internal sealed class GRushPlayerStatePreviewSubscription
    {
        private readonly Action<string> deliver;
        private IGRushPlayerStatePreviewBackend bound;
        private int generation;

        public GRushPlayerStatePreviewSubscription(Action<string> deliver)
        {
            this.deliver = deliver;
        }

        public bool Subscribed
        {
            get { return bound != null; }
        }

        // backend は確認用の表示を使えないときに null。
        public void Subscribe(IGRushPlayerStatePreviewBackend backend, bool hasListeners)
        {
            if (bound != null || !hasListeners || backend == null)
            {
                return;
            }
            bound = backend;
            var registered = generation;
            backend.SetPlayerStatePreviewHandler(json =>
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
            previous.SetPlayerStatePreviewHandler(null);
        }

        // バックエンドを差し替えた後に呼ぶ。前のバックエンドから外し、そこから届く写しを捨て、新しいバックエンドへつなぎ直す。
        public void Rebind(IGRushPlayerStatePreviewBackend backend, bool hasListeners)
        {
            Unsubscribe();
            generation++;
            Subscribe(backend, hasListeners);
        }
    }
}
