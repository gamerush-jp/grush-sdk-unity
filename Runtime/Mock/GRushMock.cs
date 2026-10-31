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
        /// <see cref="GRush.Launch"/> が返す起動パラメータ（<c>gp.</c> を除いたキーと値）。
        /// GameRush の制限（キー・値の形と 8 個まで）では確かめずにそのまま返す。<see cref="Reset"/> で空になる。
        /// </summary>
        public static Dictionary<string, string> LaunchParams = new Dictionary<string, string>();

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
        /// 同じプロセスの中の相手を部屋に入れる。部屋が埋まっていれば null を返す。
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
        /// 表示言語・起動パラメータ・部屋・分析イベント・受信箱・交換所（通報によるブロックを含む）を
        /// はじめの状態へ戻す。
        /// </summary>
        public static void Reset()
        {
            LaunchParams = new Dictionary<string, string>();
            GRushMockAnalytics.Reset();
            GRushMockLocale.Reset();
            GRushMockHub.Reset();
            GRushMockInbox.Reset();
            GRushMockTrades.Reset();
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

        public GRushResult<bool> Send(byte[] payload, GRushChannel channel, int to)
        {
            if (payload == null || payload.Length > GRush.MaxMessageBytes)
            {
                return GRushResult<bool>.Failure(GRushErrorCode.InvalidParams, "Payload is invalid.");
            }
            GRushMockHub.Instance.Route(Index, payload, payload.Length, channel, to);
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
