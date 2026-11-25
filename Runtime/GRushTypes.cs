using System;

namespace GRushSdk
{
    public enum GRushChannel
    {
        Reliable,
        Unreliable,
    }

    [Serializable]
    public struct GRushPlayer
    {
        public string PseudoId;
        public bool IsGuest;
        public bool ProfileConsent;

        /// <summary>
        /// このゲームのオーナーか、承諾済みの共同制作者なら true。GameRush のサーバが決める。
        /// デバッグメニューのような、作者にだけ見せる表示の出し分けに使う。権限ではない。
        /// ゲームのコードは遊ぶ人の手元で動き、書き換えれば true に見せられるので、守りたい操作は
        /// GameRush が確かめるもの（ショップのテスト購入など）に任せる。ゲストは常に false。
        /// </summary>
        public bool IsCreator;

        public string DisplayName;
        public string AvatarUrl;
    }

    [Serializable]
    public struct GRushPeer
    {
        public int Index;
        public string PseudoId;
        public string DisplayName;
        public string AvatarUrl;
    }

    public struct GRushMessage
    {
        public int From;
        public GRushChannel Channel;
        public uint Seq;
        public byte[] Payload;
        public bool IsStale;
    }

    /// <summary>部屋の形。</summary>
    public enum GRushTopology
    {
        /// <summary>全員が全員へ送る。定員は 16 人まで。</summary>
        Mesh,

        /// <summary>
        /// ホストへ集める。ホスト以外が送れるのはホスト宛てと全員宛て（ホストにだけ届く）だけで、
        /// ほかの宛先へ送ると送られずに <see cref="GRushRoom.Error"/>（Code は "targetNotAllowed"）が届く。
        /// </summary>
        Hub,
    }

    /// <summary>ホストの選び方。</summary>
    public enum GRushHostSelection
    {
        /// <summary>最初に入った人がホストになる。</summary>
        Default,

        /// <summary>GameRush が端末の性能と部屋までの遅延からホストを選ぶ。入室の後にホストが替わることがある。</summary>
        Auto,
    }

    /// <summary>
    /// <see cref="GRushNetApi.JoinAsync(GRushJoinOptions)"/> の引数。どの項目も省略でき、省略の値は送らない。
    /// 範囲の確かめは GameRush が行い、範囲の外は InvalidParams で返る。
    /// </summary>
    public sealed class GRushJoinOptions
    {
        /// <summary>部屋を分ける名前。null か空なら "default"。</summary>
        public string Mode;

        /// <summary>入る部屋のコード。null か空なら、空きのある部屋か新しい部屋へ入る。</summary>
        public string RoomCode;

        /// <summary>部屋の目安の人数。1〜定員の整数。0 は未指定。</summary>
        public int TargetRoomSize;

        /// <summary>
        /// 部屋の定員。1〜100 の整数で、<see cref="GRushTopology.Mesh"/> は 16 まで。0 は未指定で、定員は 8。
        /// </summary>
        public int MaxPeers;

        /// <summary>部屋の形。null は未指定で、<see cref="GRushTopology.Mesh"/>。</summary>
        public GRushTopology? Topology;

        /// <summary>ホストの選び方。</summary>
        public GRushHostSelection HostSelection;

        internal bool UsesRoomConfig
        {
            get { return MaxPeers != 0 || Topology.HasValue || HostSelection != GRushHostSelection.Default; }
        }
    }

    public struct GRushRoomStats
    {
        public string Transport;
    }

    /// <summary>
    /// 部屋が1通を捨てた理由。Code は "messageTooLarge"・"rateLimited"・"badFrame"・"targetNotAllowed" で、
    /// ここに無い値が届くこともある。
    /// </summary>
    public struct GRushRoomError
    {
        public string Code;
        public string Message;
    }

    /// <summary>
    /// 他プレイヤーへ見える公開状態のメタ情報。
    ///
    /// **payload はここに入らない。** 任意形状の JSON は
    /// <c>UnityEngine.JsonUtility</c> では型に落とせず、SDK に手書きの
    /// パーサを持たせると壊れ方が読めなくなるため、応答の生 JSON を
    /// <see cref="GRushPlayerStatePage.RawJson"/> として1つだけ渡し、
    /// パースはゲーム側の任意のパーサへ委ねる（ランキングの metadata と同じ理由）。
    /// </summary>
    [Serializable]
    public struct GRushPlayerState
    {
        public string PseudoId;
        public int Revision;
        public string UpdatedAt;

        /// <summary>相手探しに使う数値。<see cref="HasRating"/> が false なら書かれていない（0 は値ではない）。</summary>
        public double Rating;

        public bool HasRating;
    }

    /// <summary>
    /// 公開プレイヤー状態の取得結果。<see cref="RawJson"/> は応答そのもので、
    /// payload が要るゲームはこれを自前のパーサへ渡す。
    /// </summary>
    [Serializable]
    public struct GRushPlayerStatePage
    {
        public GRushPlayerState[] States;
        public bool Exists;
        public string RawJson;
    }

    [Serializable]
    public struct GRushLeaderboard
    {
        public string Key;
        public string Title;
        public string Sort;
        public string ValueType;
        public string Aggregation;
        public string Period;
        public double MinValue;
        public double MaxValue;
    }

    /// <summary>
    /// ランキングの1行。<see cref="DisplayName"/> と <see cref="AvatarUrl"/> は
    /// その相手がこのゲームでの公開に同意しているときだけ入る。ゲストは
    /// 常に匿名なので <see cref="IsGuest"/> を見て固定ラベルを出す。
    /// </summary>
    [Serializable]
    public struct GRushLeaderboardEntry
    {
        public int Rank;
        public string PseudoId;
        public string DisplayName;
        public string AvatarUrl;
        public bool IsGuest;
        public double Value;
        public string SubmittedAt;
        public bool IsSelf;
    }

    /// <summary>
    /// <see cref="Verified"/> は常に false。スコアはゲームの自己申告であり
    /// GameRush は値を検証していないので、表示側はそれを隠さないこと。
    /// </summary>
    [Serializable]
    public struct GRushLeaderboardPage
    {
        public string Key;
        public string Title;
        public string Sort;
        public string ValueType;
        public string Period;
        public string PeriodKey;
        public bool Verified;
        public GRushLeaderboardEntry[] Entries;
        public int Total;

        /// <summary>
        /// 応答の生 JSON。<c>metadata</c> は任意形状の JSON であり
        /// UnityEngine.JsonUtility では型に落とせない（手書きのパーサを
        /// SDK に持たせると壊れ方が読めない）。metadata が要るゲームは
        /// これを自前のパーサへ渡す。Godot 側は JSON を素で扱えるため
        /// <c>metadata</c> をそのまま返しており、非対称なのはこの理由。
        /// </summary>
        public string RawJson;
    }

    [Serializable]
    public struct GRushScoreResult
    {
        public bool Accepted;
        public bool Updated;
        public double Value;
        public int Rank;
        public bool HasRank;
        public bool Verified;
    }

    /// <summary>
    /// 受信箱のメッセージ。<see cref="PayloadJson"/> は送り手が渡した payload の JSON で、
    /// 文字列は識別子（<c>^[A-Za-z0-9_.:-]{1,32}$</c>）、ほかは数値・真偽値・null だけからなる。
    /// 中身はゲームの型へ <c>JsonUtility.FromJson</c> などで読む。
    /// </summary>
    [Serializable]
    public struct GRushInboxMessage
    {
        public string Id;

        /// <summary>送り手の疑似 ID。</summary>
        public string From;

        public string Kind;
        public string PayloadJson;
        public string CreatedAt;

        /// <summary>既読にした日時。未読なら null。</summary>
        public string ReadAt;

        public bool IsRead;
    }

    [Serializable]
    public struct GRushInboxPage
    {
        /// <summary>新しい順。</summary>
        public GRushInboxMessage[] Messages;

        /// <summary>絞り込みによらない未読の数。</summary>
        public int UnreadCount;

        /// <summary>続きがあれば次の <c>ListAsync</c> の cursor に渡す。無ければ null。</summary>
        public string NextCursor;
    }

    /// <summary>出品の状態。知らない値は <see cref="Unknown"/> になる。</summary>
    public enum GRushTradeStatus
    {
        Unknown,
        Open,
        Matched,
        Cancelled,
        Expired,
    }

    public enum GRushTradeRole
    {
        Unknown,
        Offerer,
        Taker,
    }

    /// <summary>
    /// 終わった取引で、手元のセーブへ反映すること。<see cref="Unknown"/> は反映せず、Ack もしない。
    /// </summary>
    public enum GRushTradeAction
    {
        Unknown,

        /// <summary>出品者が、受けた側の give を受け取る。</summary>
        ReceiveGive,

        /// <summary>出品者が、出した offer を手元へ戻す（取り下げ・期限切れ・運営の非表示）。</summary>
        ReturnOffer,

        /// <summary>受けた側が offer を受け取る。</summary>
        ReceiveOffer,
    }

    public enum GRushTradeOutcome
    {
        Matched,

        /// <summary>先に別のプレイヤーが受けた。</summary>
        Taken,

        Expired,
        Cancelled,

        /// <summary>見えない出品（伏せられた・自分の・自分宛てでない指名・ブロック関係にある相手の）。</summary>
        NotFound,
    }

    /// <summary>
    /// 出品。<see cref="OfferJson"/>・<see cref="WantJson"/>・<see cref="GiveJson"/> は JSON で、
    /// 文字列は識別子、ほかは数値・真偽値・null だけからなる。疑似 ID が消えた相手は null。
    /// </summary>
    [Serializable]
    public struct GRushTrade
    {
        public string Id;

        /// <summary>出品者の疑似 ID。</summary>
        public string From;

        /// <summary>指名の出品の相手。公開の出品では null。</summary>
        public string To;

        /// <summary>成立させた相手の疑似 ID。</summary>
        public string Taker;

        public string OfferJson;
        public string WantJson;
        public string OfferKey;
        public string WantKey;

        /// <summary>受けた側が渡したもの。当事者（出品者と受けた側）にだけ入り、ほかの人には null。</summary>
        public string GiveJson;

        public GRushTradeStatus Status;
        public string CreatedAt;
        public string ExpiresAt;
        public string SettledAt;
    }

    [Serializable]
    public struct GRushTradePage
    {
        /// <summary>他人の公開の出品と自分宛ての指名の出品のうち、受付中のもの（新しい順）。</summary>
        public GRushTrade[] Trades;

        public string NextCursor;
    }

    /// <summary>終わったが、まだ Ack していない取引。</summary>
    [Serializable]
    public struct GRushPendingTrade
    {
        public GRushTrade Trade;
        public GRushTradeRole Role;
        public GRushTradeAction Action;
    }

    [Serializable]
    public struct GRushMyTrades
    {
        /// <summary>自分の受付中の出品。</summary>
        public GRushTrade[] Open;

        /// <summary>終わったが、まだ Ack していない取引（終わった順）。</summary>
        public GRushPendingTrade[] Pending;
    }

    [Serializable]
    public struct GRushTradeAcceptResult
    {
        public GRushTradeOutcome Outcome;

        /// <summary><see cref="Trade"/> が入っているか。Matched のとき true。</summary>
        public bool HasTrade;

        public GRushTrade Trade;
    }

    /// <summary>出品の省略できる項目。既定値のままなら送らない。</summary>
    public struct GRushTradeOptions
    {
        /// <summary>一覧の絞り込みに使う識別子（<c>^[A-Za-z0-9._:-]{1,32}$</c>）。</summary>
        public string OfferKey;

        public string WantKey;

        /// <summary>指名の出品の相手の疑似 ID。null なら公開の出品。</summary>
        public string To;

        /// <summary>期限の秒数（600〜604800）。0 なら 3 日。</summary>
        public int ExpiresInSec;
    }

    /// <summary>
    /// 作者のアイテムの種類。作者が商品を登録するときに決め、後から変えられない。
    /// 消費のアイテムを、ゲーム内通貨（ほかのアイテムや機能と交換する対価）と抽選（偶然で中身が決まる仕組み）に使ってはならない。
    /// </summary>
    public enum GRushShopItemType
    {
        /// <summary>この SDK が知らない種類。<see cref="GRushShopProduct.RawType"/> に GameRush が返した値が入る。</summary>
        Unknown,

        /// <summary>
        /// 買い切り。同じプレイヤーは、返金されない限り 1 回だけ買え、持っている間の購入は
        /// <see cref="GRushShopPurchaseStatus.AlreadyOwned"/>。<c>Shop.OwnedAsync</c> に入る。
        /// </summary>
        Permanent,

        /// <summary>
        /// 消費。同じプレイヤーが何度でも買え、買うたびに付与が 1 件ずつ <c>Shop.PendingAsync</c> に入る。
        /// <c>Shop.OwnedAsync</c> には入らない。
        /// </summary>
        Consumable,
    }

    /// <summary>
    /// 作者が商品に付けられる税込みの価格（円）。GameRush はこのどれかの価格だけを登録できる。
    /// 値は円の額そのもので、<c>(int)GRushShopPrice.Yen120</c> は 120。
    /// <c>Shop.ProductsAsync</c> は価格を <see cref="GRushShopProduct.PriceJpy"/>（int）で返す。
    /// </summary>
    public enum GRushShopPrice
    {
        /// <summary>100 円。</summary>
        Yen100 = 100,

        /// <summary>120 円。</summary>
        Yen120 = 120,

        /// <summary>300 円。</summary>
        Yen300 = 300,

        /// <summary>500 円。</summary>
        Yen500 = 500,

        /// <summary>1,000 円。</summary>
        Yen1000 = 1000,

        /// <summary>2,000 円。</summary>
        Yen2000 = 2000,

        /// <summary>3,000 円。</summary>
        Yen3000 = 3000,

        /// <summary>5,000 円。</summary>
        Yen5000 = 5000,
    }

    /// <summary>作者が登録した販売中の商品。名前・価格・種類は作者が Studio か API キーで決める。</summary>
    [Serializable]
    public struct GRushShopProduct
    {
        /// <summary>商品の識別子（<c>^[a-z0-9][a-z0-9_.-]{0,63}$</c>）。</summary>
        public string Sku;

        public string Name;

        /// <summary>税込みの価格（円）。<see cref="GRushShopPrice"/> のどれかの額。</summary>
        public int PriceJpy;

        /// <summary>
        /// 商品の種類。種類を返さない古い GameRush の商品は <see cref="GRushShopItemType.Permanent"/>。
        /// 知らない値は <see cref="GRushShopItemType.Unknown"/> になる。
        /// </summary>
        public GRushShopItemType Type;

        /// <summary>
        /// GameRush が返した種類の文字列（<c>"permanent"</c> か <c>"consumable"</c>）。
        /// 種類を返さない古い GameRush の商品は <c>"permanent"</c>。<see cref="Type"/> が Unknown のときに中身を見る。
        /// </summary>
        public string RawType;
    }

    [Serializable]
    public struct GRushShopCatalog
    {
        /// <summary>
        /// この面で買えるか。Android のアプリと LINE では false で、<see cref="Products"/> は空。
        /// iOS のアプリでは、App Store の価格（100・120・300・500・1,000 円）に合う商品だけが入る。
        /// </summary>
        public bool Enabled;

        public GRushShopProduct[] Products;
    }

    /// <summary>部屋への招待（<c>Net.InviteAsync</c>）で送ったかどうか。</summary>
    public enum GRushInviteStatus
    {
        /// <summary>送らずにシートを閉じた。</summary>
        Cancelled,

        /// <summary>1人以上へ招待を送った。</summary>
        Sent,
    }

    /// <summary>
    /// 部屋への招待（<c>Net.InviteAsync</c>）の結果。返るのは送ったかどうかと人数だけで、誰へ送ったかはゲームへ渡らない。
    /// </summary>
    [Serializable]
    public struct GRushInviteResult
    {
        /// <summary>1人以上へ送ったら <see cref="GRushInviteStatus.Sent"/>、送らずに閉じたら <see cref="GRushInviteStatus.Cancelled"/>。</summary>
        public GRushInviteStatus Status;

        /// <summary>送った人数。<see cref="GRushInviteStatus.Cancelled"/> では 0。</summary>
        public int Count;
    }

    /// <summary>購入の結果。知らない値は <see cref="Unknown"/> になる。</summary>
    public enum GRushShopPurchaseStatus
    {
        /// <summary>この SDK が知らない結果。<see cref="GRushShopPurchaseResult.RawStatus"/> に GameRush が返した値が入る。</summary>
        Unknown,

        /// <summary>決済画面へ移る。ページはすぐ離れる（Web）。</summary>
        Redirecting,

        /// <summary>プレイヤーが購入をやめた。</summary>
        Cancelled,

        /// <summary>同じ買い切りの商品をもう持っている。消費のアイテムでは返らない。</summary>
        AlreadyOwned,

        /// <summary>買い終え、付与が <c>Shop.PendingAsync</c> に入った（iOS のアプリ）。続けて <c>Shop.PendingAsync</c> を読む。</summary>
        Purchased,

        /// <summary>
        /// 支払いの確認や保護者の承認を待っている（iOS のアプリ）。済むと付与が <c>Shop.PendingAsync</c> に入る。
        /// </summary>
        Pending,
    }

    [Serializable]
    public struct GRushShopPurchaseResult
    {
        public GRushShopPurchaseStatus Status;

        /// <summary>GameRush が返した status の文字列（<c>"redirecting"</c> など）。Unknown のときに中身を見る。</summary>
        public string RawStatus;
    }

    /// <summary>
    /// 購入で届いた付与。<see cref="Acked"/> が false のあいだは <c>Shop.PendingAsync</c> に残る。
    /// </summary>
    [Serializable]
    public struct GRushShopGrant
    {
        /// <summary>付与の ID（UUID）。<c>Shop.AckAsync</c> に渡す。</summary>
        public string Id;

        public string Sku;

        /// <summary>付与した日時（ISO 8601）。</summary>
        public string CreatedAt;

        /// <summary>
        /// Ack（ゲームへ届けた）済みか。<c>Shop.PendingAsync</c> の付与では常に false。
        /// </summary>
        public bool Acked;
    }

    public static class GRushChannels
    {
        public const string Reliable = "reliable";
        public const string Unreliable = "unreliable";

        public static string ToWire(GRushChannel channel)
        {
            return channel == GRushChannel.Unreliable ? Unreliable : Reliable;
        }

        public static GRushChannel FromWire(string channel)
        {
            return channel == Unreliable ? GRushChannel.Unreliable : GRushChannel.Reliable;
        }

        public static int ToCode(GRushChannel channel)
        {
            return channel == GRushChannel.Unreliable ? 1 : 0;
        }

        public static GRushChannel FromCode(int code)
        {
            return code == 1 ? GRushChannel.Unreliable : GRushChannel.Reliable;
        }
    }
}
