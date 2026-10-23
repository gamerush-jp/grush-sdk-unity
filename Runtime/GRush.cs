using System;
using System.Threading.Tasks;

namespace GRushSdk
{
    public static class GRush
    {
        public const int RequiredProtocolVersion = 1;
        public const int PlayerStateProtocolVersion = 2;
        public const int ShareProtocolVersion = 3;
        public const int LocaleProtocolVersion = 4;
        public const int AnalyticsProtocolVersion = 6;

        /// <summary>相手探し（<c>PlayerState.SampleAsync</c> と <c>SetMineAsync</c> の rating）に要る版数。</summary>
        public const int MatchmakingProtocolVersion = 7;

        /// <summary>受信箱（<see cref="Inbox"/>）に要る版数。</summary>
        public const int InboxProtocolVersion = 7;

        /// <summary>交換所（<see cref="Trade"/>）に要る版数。</summary>
        public const int TradeProtocolVersion = 7;

        /// <summary>起動パラメータ（<see cref="Launch"/>）に要る版数。</summary>
        public const int LaunchProtocolVersion = 8;

        /// <summary>
        /// 共有に起動パラメータを添える（<c>Share.ShareAsync</c> と <c>ShareScreenAsync</c> の launchParams）のに要る版数。
        /// </summary>
        public const int ShareLaunchProtocolVersion = 9;

        /// <summary>確認用の表示の写しを受け取る（<c>PlayerState.PreviewReceived</c>）のに要る版数。</summary>
        public const int PlayerStatePreviewProtocolVersion = 10;

        public const int MaxMessageBytes = 8 * 1024;

        private static IGRushBackend backend;
        private static GRushPlayerApi player;
        private static GRushNetApi net;
        private static GRushLeaderboardsApi leaderboards;
        private static GRushPlayerStateApi playerState;
        private static GRushShareApi share;
        private static GRushLocaleApi locale;
        private static GRushAnalyticsApi analytics;
        private static GRushInboxApi inbox;
        private static GRushTradeApi trade;
        private static GRushLaunchApi launch;

        public static IGRushBackend Backend
        {
            get
            {
                if (backend == null)
                {
                    backend = GRushBackendFactory.Create();
                }
                return backend;
            }
        }

        public static int ProtocolVersion
        {
            get { return Backend.ProtocolVersion; }
        }

        /// <summary>公開プレイヤー状態を呼べるか。古いランタイムでは false。</summary>
        public static bool IsPlayerStateAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= PlayerStateProtocolVersion; }
        }

        public static bool IsShareAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= ShareProtocolVersion; }
        }

        public static bool IsLocaleAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= LocaleProtocolVersion; }
        }

        /// <summary>分析イベントに要る版数を GameRush が満たすか。GameRush の中で動いているかは Analytics.IsAvailable。</summary>
        public static bool IsAnalyticsAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= AnalyticsProtocolVersion; }
        }

        /// <summary>
        /// 相手探しを呼べるか。false のランタイムでは <c>SampleAsync</c> が Unsupported になり、
        /// <c>SetMineAsync</c> は rating を送らずに payload だけを書く。
        /// </summary>
        public static bool IsMatchmakingAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= MatchmakingProtocolVersion; }
        }

        /// <summary>受信箱を呼べるか。古いランタイムでは false。</summary>
        public static bool IsInboxAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= InboxProtocolVersion; }
        }

        /// <summary>交換所を呼べるか。古いランタイムでは false。</summary>
        public static bool IsTradeAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= TradeProtocolVersion; }
        }

        /// <summary>起動パラメータを読めるか。古いランタイムと GameRush の外では false。</summary>
        public static bool IsLaunchAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= LaunchProtocolVersion; }
        }

        /// <summary>
        /// 確認用の表示の写しを受け取れるか（<c>PlayerState.PreviewReceived</c>）。古いランタイムと GameRush の外では false。
        /// </summary>
        public static bool IsPlayerStatePreviewAvailable
        {
            get { return IsAvailable && Backend.ProtocolVersion >= PlayerStatePreviewProtocolVersion; }
        }

        public static bool IsAvailable
        {
            get { return Backend.IsAvailable && Backend.ProtocolVersion >= RequiredProtocolVersion; }
        }

        public static GRushPlayerApi Player
        {
            get
            {
                if (player == null)
                {
                    player = new GRushPlayerApi();
                }
                return player;
            }
        }

        public static GRushNetApi Net
        {
            get
            {
                if (net == null)
                {
                    net = new GRushNetApi();
                }
                return net;
            }
        }

        public static GRushLeaderboardsApi Leaderboards
        {
            get
            {
                if (leaderboards == null)
                {
                    leaderboards = new GRushLeaderboardsApi();
                }
                return leaderboards;
            }
        }

        public static GRushPlayerStateApi PlayerState
        {
            get
            {
                if (playerState == null)
                {
                    playerState = new GRushPlayerStateApi();
                }
                return playerState;
            }
        }

        public static GRushShareApi Share
        {
            get
            {
                if (share == null)
                {
                    share = new GRushShareApi();
                }
                return share;
            }
        }

        public static GRushLocaleApi Locale
        {
            get
            {
                if (locale == null)
                {
                    locale = new GRushLocaleApi();
                }
                return locale;
            }
        }

        /// <summary>作者のアナリティクスへ送るイベント。どれも同期で bool を返す。</summary>
        public static GRushAnalyticsApi Analytics
        {
            get
            {
                if (analytics == null)
                {
                    analytics = new GRushAnalyticsApi();
                }
                return analytics;
            }
        }

        /// <summary>受信箱。同じゲームのプレイヤーへ小さい構造化メッセージを送り、受け取る。</summary>
        public static GRushInboxApi Inbox
        {
            get
            {
                if (inbox == null)
                {
                    inbox = new GRushInboxApi();
                }
                return inbox;
            }
        }

        /// <summary>交換所。アイテムを出品し、同じゲームの別のプレイヤーが受ける。</summary>
        public static GRushTradeApi Trade
        {
            get
            {
                if (trade == null)
                {
                    trade = new GRushTradeApi();
                }
                return trade;
            }
        }

        /// <summary>
        /// 起動パラメータ。GameRush の URL に <c>gp.</c> を付けて載せたキーと値で、シェアしたリンクから
        /// 特定のアイテムやステージへ飛ばすのに使う。値は信用せず、ゲームの側で確かめてから使う。
        /// </summary>
        public static GRushLaunchApi Launch
        {
            get
            {
                if (launch == null)
                {
                    launch = new GRushLaunchApi();
                }
                return launch;
            }
        }

        /// <summary>
        /// バックエンドを差し替える。null なら実行環境に合うものを作り直す。
        /// 登録済みの <c>Locale.Changed</c> と <c>PlayerState.PreviewReceived</c> は新しいバックエンドの通知を受け続ける。
        /// </summary>
        public static void UseBackend(IGRushBackend replacement)
        {
            var previous = backend as IGRushLocaleBackend;
            if (previous != null)
            {
                previous.SetLocaleChangedHandler(null);
            }
            net = null;
            player = null;
            leaderboards = null;
            share = null;
            analytics = null;
            inbox = null;
            trade = null;
            launch = null;
            backend = replacement ?? GRushBackendFactory.Create();
            // Locale と PlayerState は作り直さない。作者が登録した Changed と PreviewReceived を新しいバックエンドで受け続ける。
            // PlayerState は前のバックエンドから外すのも RebindPreview が受け持つ。
            locale?.Rebind();
            playerState?.RebindPreview();
        }

        internal static Task<GRushResult<string>> CallAsync(string method, string paramsJson)
        {
            var completion = new TaskCompletionSource<GRushResult<string>>();
            if (!IsAvailable)
            {
                completion.SetResult(GRushResult<string>.Unsupported());
                return completion.Task;
            }
            Backend.Call(
                method,
                paramsJson,
                response =>
                    completion.TrySetResult(
                        response.Ok
                            ? GRushResult<string>.Success(response.ResultJson)
                            : GRushResult<string>.Failure(response.Code, response.Message)
                    )
            );
            return completion.Task;
        }
    }

    internal static class GRushBackendFactory
    {
        public static IGRushBackend Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (GRushWebGLBackend.IsPresent())
            {
                return new GRushWebGLBackend();
            }
            return new GRushUnsupportedBackend();
#else
            return new GRushMockBackend();
#endif
        }
    }

    public sealed class GRushPlayerApi
    {
        public Task<GRushResult<GRushPlayer>> GetSelfAsync()
        {
            return CallPlayerAsync("player.getSelf");
        }

        public Task<GRushResult<GRushPlayer>> RequestProfileAsync()
        {
            return CallPlayerAsync("player.requestProfile");
        }

        public Task<GRushResult<GRushPlayer>> RevokeProfileAsync()
        {
            return CallPlayerAsync("player.revokeProfile");
        }

        private static async Task<GRushResult<GRushPlayer>> CallPlayerAsync(string method)
        {
            var response = await GRush.CallAsync(method, null);
            if (!response.Ok)
            {
                return GRushResult<GRushPlayer>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushPlayerWire>(response.Value);
            if (wire == null || string.IsNullOrEmpty(wire.pseudoId))
            {
                return GRushResult<GRushPlayer>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable player."
                );
            }
            return GRushResult<GRushPlayer>.Success(GRushWire.ToPlayer(wire));
        }
    }
}
