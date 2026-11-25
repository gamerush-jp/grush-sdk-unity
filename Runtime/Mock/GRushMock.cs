using System;
using System.Collections.Generic;

namespace GRushSdk
{
    public static class GRushMock
    {
        public static bool SignedIn = true;
        public static string DisplayName = "Editor Player";
        public static string AvatarUrl = null;
        public static bool GrantProfileConsent = true;

        /// <summary>
        /// <see cref="GRushPlayer.IsCreator"/> に返す値。エディタで動かしているのは作者なので既定は true。
        /// <see cref="SignedIn"/> が false なら、この設定によらず false。
        /// </summary>
        public static bool IsCreator = true;
        public static double UnreliableDropRate = 0.0;
        public static bool ShareAvailable = true;
        public static GRushShareStatus ShareStatus = GRushShareStatus.Opened;

        /// <summary>受信箱の通報の確認でプレイヤーが押す答え。</summary>
        public static bool ConfirmInboxReport = true;

        /// <summary>交換所の通報の確認でプレイヤーが押す答え。</summary>
        public static bool ConfirmTradeReport = true;

        /// <summary>プレイヤー状態の通報の確認でプレイヤーが押す答え。</summary>
        public static bool ConfirmPlayerStateReport = true;

        /// <summary>
        /// 作者アイテム販売で買える面か。false は Android のアプリと LINE と同じく、<c>Shop.ProductsAsync</c> が
        /// Enabled false の空を返し、<c>Shop.PurchaseAsync</c> が Unsupported になる。
        /// </summary>
        public static bool ShopEnabled = true;

        /// <summary>
        /// <c>Shop.PurchaseAsync</c> が返す結果。Redirecting のときは、すぐに付与を Pending に積んで
        /// <c>Shop.PendingReceived</c> を呼ぶ。Purchased のときは iOS のアプリと同じく、付与を Pending に積むが
        /// <c>Shop.PendingReceived</c> は呼ばない。ほかの値では付与を積まない。ゲストは SignInRequired、
        /// 付与済み（Ack 済みを含む）の買い切りの sku はこの設定によらず AlreadyOwned。
        /// </summary>
        public static GRushShopPurchaseStatus ShopPurchaseStatus = GRushShopPurchaseStatus.Redirecting;

        /// <summary>
        /// <see cref="GRush.Launch"/> が返す起動パラメータ（<c>gp.</c> を除いたキーと値）。
        /// GameRush の制限（キー・値の形と 8 個まで）では確かめずにそのまま返す。<see cref="Reset"/> で空になる。
        /// </summary>
        public static Dictionary<string, string> LaunchParams = new Dictionary<string, string>();

        /// <summary>
        /// <see cref="GRush.Launch"/> の <see cref="GRushLaunch.Invite"/> に入れる招待の部屋の mode。
        /// <see cref="LaunchInviteRoomCode"/> と両方を入れると、招待から開いたときを試せる。
        /// 形が join の規則に合わなければ招待なし（null）として返す。<see cref="Reset"/> で null になる。
        /// </summary>
        public static string LaunchInviteMode = null;

        /// <summary><see cref="GRushLaunch.Invite"/> に入れる招待の部屋の部屋コード。<see cref="Reset"/> で null になる。</summary>
        public static string LaunchInviteRoomCode = null;

        /// <summary>
        /// <c>Net.InviteAsync</c> の答え。Cancelled にすると、送らずにシートを閉じたことにする。
        /// 部屋に居なければ Unavailable、<see cref="SignedIn"/> が false なら SignInRequired で、この設定によらない。
        /// </summary>
        public static GRushInviteStatus InviteStatus = GRushInviteStatus.Sent;

        /// <summary>
        /// <c>Net.InviteAsync</c> で偽のフレンドへ送ったことにする人数。部屋の空いた席の数と 30 の小さいほうまでに絞り、
        /// 0 になれば Cancelled を返す。
        /// </summary>
        public static int InviteCount = 1;

        private static readonly string[] rivals = { "mock-rival-1", "mock-rival-2", "mock-rival-3" };

        /// <summary>
        /// モックの相手の疑似 ID。それぞれ rating 付きの公開プレイヤー状態を持ち、届いたメッセージを
        /// 同じ kind と payload で送り返す。はじめと <see cref="Reset"/> の後は、最初の相手からの
        /// メッセージ1通と、受付中の出品を2件持つ。
        /// </summary>
        public static string[] Rivals
        {
            get { return (string[])rivals.Clone(); }
        }

        /// <summary>
        /// 同じプロセスの中の相手を部屋に入れる。部屋が埋まっていれば null を返す。定員は、ゲームが入る前は 8、
        /// 入った後はその部屋の定員。
        /// </summary>
        public static GRushMockPeer AddPeer(string displayName)
        {
            return AddPeer(displayName, null);
        }

        /// <summary>
        /// 同じプロセスの中の相手を、アバターの URL 付きで部屋に入れる。部屋が埋まっていれば null を返す。
        /// </summary>
        public static GRushMockPeer AddPeer(string displayName, string avatarUrl)
        {
            return GRushMockHub.Instance.AddPeer(displayName, avatarUrl);
        }

        public static void RemovePeer(GRushMockPeer peer)
        {
            GRushMockHub.Instance.RemovePeer(peer);
        }

        /// <summary>
        /// 自分がいる部屋に、部屋が1通を捨てたときの <see cref="GRushRoom.Error"/> を届ける。
        /// 本番と同じく次のフレーム以降に届き、部屋は閉じない。部屋にいなければ何もせず false を返す。
        /// </summary>
        public static bool RaiseRoomError(string code, string message)
        {
            return GRushMockHub.Instance.RaiseError(code, message);
        }

        public static void SetLocale(string locale)
        {
            GRushMockLocale.Set(locale, "user", null);
        }

        public static void SetLocale(string locale, string source, string[] languages)
        {
            GRushMockLocale.Set(locale, source, languages);
        }

        /// <summary>
        /// モックが受けた分析イベント。GameRush へ渡すのと同じ形の JSON で、直近の 200 件を古い順に返す。
        /// Reset で空になる。
        /// </summary>
        public static string[] AnalyticsEvents
        {
            get { return GRushMockAnalytics.Snapshot(); }
        }

        /// <summary>
        /// 表示言語・起動パラメータと招待の部屋・招待の答え・部屋・分析イベント・受信箱・交換所（通報によるブロックを含む）・
        /// 作者アイテム販売の付与をはじめの状態へ戻す。<see cref="GRushMockShop.Define(string, string, GRushShopPrice, GRushShopItemType)"/> で置いた商品は残る。
        /// </summary>
        public static void Reset()
        {
            LaunchParams = new Dictionary<string, string>();
            LaunchInviteMode = null;
            LaunchInviteRoomCode = null;
            InviteStatus = GRushInviteStatus.Sent;
            InviteCount = 1;
            GRushMockAnalytics.Reset();
            GRushMockLocale.Reset();
            GRushMockHub.Reset();
            GRushMockInbox.Reset();
            GRushMockTrades.Reset();
            GRushMockShop.Reset();
        }
    }

    public sealed class GRushMockPeer
    {
        public int Index { get; internal set; }
        public string PseudoId { get; internal set; }
        public string DisplayName { get; internal set; }
        public string AvatarUrl { get; internal set; }

        public event Action<GRushMessage> Received;

        public GRushResult<bool> Send(byte[] payload)
        {
            return Send(payload, GRushChannel.Reliable, GRushRoom.Everyone);
        }

        /// <summary>
        /// 部屋へ送る。hub の部屋でこの相手がホストでなければ、送れるのはホスト宛てと全員宛て（ホストにだけ届く）だけで、
        /// ほかの宛先は送らずに InvalidParams を返す。
        /// </summary>
        public GRushResult<bool> Send(byte[] payload, GRushChannel channel, int to)
        {
            if (payload == null || payload.Length > GRush.MaxMessageBytes)
            {
                return GRushResult<bool>.Failure(GRushErrorCode.InvalidParams, "Payload is invalid.");
            }
            if (!GRushMockHub.Instance.RouteFromPeer(this, payload, channel, to))
            {
                return GRushResult<bool>.Failure(
                    GRushErrorCode.InvalidParams,
                    "targetNotAllowed: send to the host only."
                );
            }
            return GRushResult<bool>.Success(true);
        }

        internal void Deliver(GRushMessage message)
        {
            var handler = Received;
            if (handler != null)
            {
                handler(message);
            }
        }
    }
}
