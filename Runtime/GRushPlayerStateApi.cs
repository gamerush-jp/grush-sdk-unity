using System;
using System.Threading.Tasks;

namespace GRushSdk
{
    /// <summary>
    /// 公開プレイヤー状態（他プレイヤーへ見せる進捗）。
    ///
    /// **これは他プレイヤーへ見えるユーザー生成コンテンツである。** 通報と
    /// モデレーションの対象になるため、payload は人間が読める構造化 JSON に
    /// 限られる。4KB を超えるものや base64 のような読めない文字列を含むものは
    /// サーバが弾く。
    ///
    /// クラウドセーブ（本人だけが読む非公開データ）とは別の入れ物であり、
    /// この経路からクラウドセーブは一切読めない。
    ///
    /// payload は任意形状の JSON で <c>UnityEngine.JsonUtility</c> では型に
    /// 落とせないため、**応答の生 JSON を <c>RawJson</c> として渡し、パースは
    /// ゲーム側の任意のパーサへ委ねる**（ランキングの metadata と同じ理由）。
    /// </summary>
    public sealed class GRushPlayerStateApi
    {
        private Action<GRushPlayerStatePage> previewReceived;
        private GRushPlayerStatePreviewSubscription previewSubscription;

        /// <summary>
        /// GameRush の確認用の表示で、ある公開プレイヤー状態の写しが渡されたときに呼ばれる。
        /// 受け取ったら、その状態が他プレイヤーにどう見えるかを、普段と同じ描き方で描く。
        ///
        /// 渡す <see cref="GRushPlayerStatePage"/> は <see cref="GetMineAsync"/> と同じ形で、
        /// <c>States</c> に1件、<c>Exists</c> が true、<c>RawJson</c> は <c>{"state":{...}}</c>
        /// （payload はここから読む）。自分の状態とは限らない。
        ///
        /// 次の <c>Update</c> で呼ばれる。同じ写しは1回だけ渡り、購読より前に届いた写しは購読したときに渡る。
        /// 購読が1つ以上あるあいだだけ GameRush から受け取り、すべて外すと受け取りをやめる。
        /// 普段のプレイでは呼ばれない。<see cref="GRush.IsPlayerStatePreviewAvailable"/> が false
        /// （<c>protocolVersion</c> 10 未満）と GameRush の外では呼ばれず、例外も投げない。
        /// <see cref="GRush.UseBackend"/> でバックエンドを差し替えても、登録済みの購読は続く。
        /// </summary>
        public event Action<GRushPlayerStatePage> PreviewReceived
        {
            add
            {
                previewReceived += value;
                PreviewSubscription().Subscribe(PreviewBackend(), previewReceived != null);
            }
            remove
            {
                previewReceived -= value;
                if (previewReceived == null)
                {
                    PreviewSubscription().Unsubscribe();
                }
            }
        }

        // GRush.UseBackend の後に呼ぶ。登録済みの PreviewReceived を新しいバックエンドへつなぎ直す。
        internal void RebindPreview()
        {
            PreviewSubscription().Rebind(PreviewBackend(), previewReceived != null);
        }

        // フィールドの初期化子からは OnPreview を渡せないので、最初に使うときに作る。
        private GRushPlayerStatePreviewSubscription PreviewSubscription()
        {
            if (previewSubscription == null)
            {
                previewSubscription = new GRushPlayerStatePreviewSubscription(OnPreview);
            }
            return previewSubscription;
        }

        private static IGRushPlayerStatePreviewBackend PreviewBackend()
        {
            return GRush.IsPlayerStatePreviewAvailable
                ? GRush.Backend as IGRushPlayerStatePreviewBackend
                : null;
        }

        private void OnPreview(string json)
        {
            var page = SinglePage(json);
            var handler = previewReceived;
            if (page.Exists && handler != null)
            {
                handler(page);
            }
        }

        /// <summary>自分の状態を読む。運営が伏せていても本人には返る。</summary>
        public async Task<GRushResult<GRushPlayerStatePage>> GetMineAsync()
        {
            if (!GRush.IsPlayerStateAvailable)
            {
                return GRushResult<GRushPlayerStatePage>.Unsupported();
            }
            var response = await GRush.CallAsync("playerState.getMine", null);
            return ToSingle(response);
        }

        public Task<GRushResult<GRushPlayerStatePage>> SetMineAsync(string payloadJson)
        {
            return SetMineAsync(payloadJson, -1);
        }

        /// <summary>
        /// 自分の状態を書く。<paramref name="payloadJson"/> は JSON オブジェクトの
        /// 生文字列（4KB 以下）。<paramref name="baseRevision"/> に 0 以上を渡すと
        /// 楽観ロックになり、他所で更新されていれば <c>InvalidParams</c> で戻る。
        /// rating は今の値のまま変えない。
        /// </summary>
        public Task<GRushResult<GRushPlayerStatePage>> SetMineAsync(
            string payloadJson,
            int baseRevision
        )
        {
            return WriteMineAsync(payloadJson, baseRevision, false, null);
        }

        /// <summary>
        /// 自分の状態と、相手探しに使う rating（-1e9 より大きく 1e9 より小さい数値）を書く。
        /// <paramref name="rating"/> に null を渡すと rating を消す。
        /// <see cref="GRush.IsMatchmakingAvailable"/> が false の古いランタイムでは rating を送らず、
        /// payload だけを書く。true でも古い GameRush 本体では rating が黙って捨てられるので、
        /// 書けたかは返る状態の <c>HasRating</c> と <c>Rating</c> で確かめる。
        /// NaN と無限大は送らずに InvalidParams で返す。
        /// </summary>
        public Task<GRushResult<GRushPlayerStatePage>> SetMineAsync(
            string payloadJson,
            int baseRevision,
            double? rating
        )
        {
            return WriteMineAsync(payloadJson, baseRevision, GRush.IsMatchmakingAvailable, rating);
        }

        /// <summary>
        /// 相手探し。同じゲームの他プレイヤーの公開状態をランダムに返す（既定 10 件）。
        /// 自分と運営が伏せた状態は返らず、名前も返らない。
        /// <see cref="GRush.IsMatchmakingAvailable"/> が false なら Unsupported。
        /// </summary>
        public Task<GRushResult<GRushPlayerStatePage>> SampleAsync()
        {
            return SampleCoreAsync(0, false, 0, 0);
        }

        /// <summary><paramref name="count"/> は 1〜20。</summary>
        public Task<GRushResult<GRushPlayerStatePage>> SampleAsync(int count)
        {
            return SampleCoreAsync(count, false, 0, 0);
        }

        /// <summary>
        /// rating が <paramref name="minRating"/> 以上 <paramref name="maxRating"/> 以下の状態から
        /// 引く。範囲を渡すと、rating の無い状態は返らない。
        /// </summary>
        public Task<GRushResult<GRushPlayerStatePage>> SampleAsync(
            int count,
            double minRating,
            double maxRating
        )
        {
            return SampleCoreAsync(count, true, minRating, maxRating);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static async Task<GRushResult<GRushPlayerStatePage>> WriteMineAsync(
            string payloadJson,
            int baseRevision,
            bool sendRating,
            double? rating
        )
        {
            if (!GRush.IsPlayerStateAvailable)
            {
                return GRushResult<GRushPlayerStatePage>.Unsupported();
            }
            // NaN と無限大は JSON の数値に書けない。そのまま組み立てると壊れた JSON が届き、rating と分からない文言で返る。
            if (sendRating && rating.HasValue && !IsFinite(rating.Value))
            {
                return GRushResult<GRushPlayerStatePage>.Failure(
                    GRushErrorCode.InvalidParams,
                    "rating must be a finite number or null."
                );
            }
            var response = await GRush.CallAsync(
                "playerState.setMine",
                GRushWire.PlayerStateParams(payloadJson, baseRevision, sendRating, rating)
            );
            return ToSingle(response);
        }

        private static async Task<GRushResult<GRushPlayerStatePage>> SampleCoreAsync(
            int count,
            bool hasRange,
            double minRating,
            double maxRating
        )
        {
            if (!GRush.IsMatchmakingAvailable)
            {
                return GRushResult<GRushPlayerStatePage>.Unsupported();
            }
            if (hasRange && (!IsFinite(minRating) || !IsFinite(maxRating)))
            {
                return GRushResult<GRushPlayerStatePage>.Failure(
                    GRushErrorCode.InvalidParams,
                    "minRating and maxRating must be finite numbers."
                );
            }
            var response = await GRush.CallAsync(
                "playerState.sample",
                GRushWire.SampleParams(count, hasRange, minRating, maxRating)
            );
            return ToList(response);
        }

        /// <summary>
        /// 他プレイヤーの状態を疑似IDで引く（1回 50 件まで）。
        /// **運営が伏せたものは返らないので、要求した数だけ返る前提で書かないこと。**
        /// </summary>
        public async Task<GRushResult<GRushPlayerStatePage>> GetAsync(string[] pseudoIds)
        {
            if (!GRush.IsPlayerStateAvailable)
            {
                return GRushResult<GRushPlayerStatePage>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "playerState.get",
                GRushWire.PlayerStateQuery(pseudoIds)
            );
            return ToList(response);
        }

        private static GRushResult<GRushPlayerStatePage> ToList(GRushResult<string> response)
        {
            if (!response.Ok)
            {
                return GRushResult<GRushPlayerStatePage>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushPlayerStateListEnvelope>(response.Value);
            var source = wire == null || wire.states == null
                ? new GRushPlayerStateWire[0]
                : wire.states;
            var states = new GRushPlayerState[source.Length];
            for (var index = 0; index < states.Length; index++)
            {
                states[index] = GRushWire.ToPlayerState(source[index]);
            }
            return GRushResult<GRushPlayerStatePage>.Success(
                new GRushPlayerStatePage
                {
                    States = states,
                    Exists = states.Length > 0,
                    RawJson = response.Value,
                }
            );
        }

        /// <summary>
        /// 他プレイヤーの状態を通報する。**ゲームは通報 UI を描けない。**
        /// 確認ダイアログは親（信頼済み UI）が出し、プレイヤーが承諾したときだけ
        /// 通報が送られる。戻り値は「通報が送られたか」。
        /// </summary>
        public async Task<GRushResult<bool>> ReportAsync(string pseudoId)
        {
            if (!GRush.IsPlayerStateAvailable)
            {
                return GRushResult<bool>.Unsupported();
            }
            var response = await GRush.CallAsync(
                "playerState.report",
                GRushWire.PlayerStateReportParams(pseudoId)
            );
            if (!response.Ok)
            {
                return GRushResult<bool>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushReportResultWire>(response.Value);
            return GRushResult<bool>.Success(wire != null && wire.reported);
        }

        private static GRushResult<GRushPlayerStatePage> ToSingle(GRushResult<string> response)
        {
            if (!response.Ok)
            {
                return GRushResult<GRushPlayerStatePage>.Failure(response.Code, response.Message);
            }
            return GRushResult<GRushPlayerStatePage>.Success(SinglePage(response.Value));
        }

        // {"state":{...}} を1件のページにする。state が無ければ（まだ書いていない場合は state: null）Exists が false。
        private static GRushPlayerStatePage SinglePage(string json)
        {
            var wire = GRushWire.Parse<GRushPlayerStateEnvelope>(json);
            if (wire == null || wire.state == null || string.IsNullOrEmpty(wire.state.pseudoId))
            {
                return new GRushPlayerStatePage
                {
                    States = new GRushPlayerState[0],
                    Exists = false,
                    RawJson = json,
                };
            }
            return new GRushPlayerStatePage
            {
                States = new[] { GRushWire.ToPlayerState(wire.state) },
                Exists = true,
                RawJson = json,
            };
        }
    }
}
