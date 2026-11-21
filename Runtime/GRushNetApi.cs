using System;
using System.Threading.Tasks;

namespace GRushSdk
{
    // count は double で受ける。int で受けると、JsonUtility が 1.5 のような値を丸めて読むことがあり、JavaScript と Godot の
    // SDK（整数でない人数は cancelled）と食い違う。
    [Serializable]
    internal class GRushInviteResultWire
    {
        public string status;
        public double count;
    }

    public sealed class GRushNetApi
    {
        private GRushRoom room;
        private bool handlerBound;

        public GRushRoom Room
        {
            get { return room; }
        }

        public Task<GRushResult<GRushRoom>> JoinAsync()
        {
            return JoinAsync("default", null);
        }

        public Task<GRushResult<GRushRoom>> JoinAsync(string mode)
        {
            return JoinAsync(mode, null);
        }

        public Task<GRushResult<GRushRoom>> JoinAsync(string mode, string roomCode)
        {
            return JoinAsync(mode, roomCode, 0);
        }

        /// <summary>
        /// 部屋の目安の人数を渡して部屋へ入る。新しい部屋から最大 8 部屋を確かめ、目安に届いていない部屋のうち
        /// 最も人数の少ない部屋へ入る。確かめた部屋がどれも目安以上なら新しい部屋ができる。
        /// 目安を知らない版の GameRush では無視され、ゲームからは見分けられない。
        /// </summary>
        /// <param name="mode">部屋を分ける名前。</param>
        /// <param name="targetRoomSize">
        /// 目安の人数。1〜8 の整数で、範囲の外は InvalidParams。0 は未指定で、目安を送らない。
        /// </param>
        public Task<GRushResult<GRushRoom>> JoinAsync(string mode, int targetRoomSize)
        {
            return JoinAsync(mode, null, targetRoomSize);
        }

        /// <summary>
        /// 部屋の定員と形・ホストの選び方を選んで部屋へ入る。Mode・RoomCode・TargetRoomSize は
        /// ほかの JoinAsync と同じで、TargetRoomSize の上限は定員になる。
        /// MaxPeers・Topology・HostSelection のどれかを渡すと <see cref="GRush.RoomConfigProtocolVersion"/> が要り、
        /// 満たさない GameRush では入らずに Unsupported を返す。
        /// 部屋コードで指定した部屋が違う定員か形で既にあるときは InvalidParams。
        /// 入った部屋の定員と形は <see cref="GRushRoom.MaxPeers"/> と <see cref="GRushRoom.Topology"/> で確かめる。
        /// </summary>
        /// <param name="options">join の引数。null は何も渡さないのと同じ。</param>
        public Task<GRushResult<GRushRoom>> JoinAsync(GRushJoinOptions options)
        {
            var effective = options ?? new GRushJoinOptions();
            if (effective.UsesRoomConfig && !GRush.IsRoomConfigAvailable)
            {
                return Task.FromResult(GRushResult<GRushRoom>.Unsupported());
            }
            return JoinWithParamsAsync(GRushWire.JoinParams(effective));
        }

        private Task<GRushResult<GRushRoom>> JoinAsync(
            string mode,
            string roomCode,
            int targetRoomSize
        )
        {
            return JoinWithParamsAsync(GRushWire.JoinParams(mode, roomCode, targetRoomSize));
        }

        private async Task<GRushResult<GRushRoom>> JoinWithParamsAsync(string paramsJson)
        {
            if (!GRush.IsAvailable)
            {
                return GRushResult<GRushRoom>.Unsupported();
            }
            BindHandler();
            var previous = room;
            room = null;
            if (previous != null)
            {
                previous.CloseLocally("replaced");
            }
            var response = await GRush.CallAsync("net.join", paramsJson);
            if (!response.Ok)
            {
                return GRushResult<GRushRoom>.Failure(response.Code, response.Message);
            }
            var wire = GRushWire.Parse<GRushJoinWire>(response.Value);
            if (wire == null || string.IsNullOrEmpty(wire.roomId))
            {
                return GRushResult<GRushRoom>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable room."
                );
            }
            room = new GRushRoom(this, wire);
            return GRushResult<GRushRoom>.Success(room);
        }

        /// <summary>
        /// 今いる部屋へ、相互フォローの相手を招く。GameRush が相手を選ぶシートを出し、プレイヤーが送るか閉じると返る。
        /// 招く部屋は今入っている部屋で、ゲームは部屋も相手も選べず、送った人数だけを受け取る。
        /// 選べるのは部屋の空いた席の数と 30 人の小さいほうまで。
        /// 招かれた人が通知から開くと、<see cref="GRushLaunch.Invite"/> にこの部屋が入る。
        ///
        /// シートを出している間もゲームは止まらず、部屋の相手との対戦は続く。
        /// 部屋に居なければ Unavailable、ゲストなら SignInRequired、5 秒に 1 回より多く呼ぶと RateLimited。
        /// GameRush が招待を送れなかったとき（上限・空き席の不足など）は、シートの中で理由を出し、閉じると Cancelled。
        /// シートを出せない面（埋め込みなど）と、<see cref="GRush.IsInviteAvailable"/> が false の古いランタイムと
        /// GameRush の外では Unsupported。
        /// </summary>
        public async Task<GRushResult<GRushInviteResult>> InviteAsync()
        {
            if (!GRush.IsInviteAvailable)
            {
                return GRushResult<GRushInviteResult>.Unsupported();
            }
            var response = await GRush.CallAsync("net.invite", null);
            if (!response.Ok)
            {
                return GRushResult<GRushInviteResult>.Failure(response.Code, response.Message);
            }
            GRushInviteResult result;
            if (!TryReadInvite(response.Value, GRushWire.Parse<GRushInviteResultWire>(response.Value), out result))
            {
                return GRushResult<GRushInviteResult>.Failure(
                    GRushErrorCode.Internal,
                    "GameRush returned an unreadable invite result."
                );
            }
            return GRushResult<GRushInviteResult>.Success(result);
        }

        // 招待の答えを読む。JsonUtility は null・数・配列の JSON からも既定値のオブジェクトを作ることがあるので、
        // 生の JSON がオブジェクトかどうかを先に見る。オブジェクトでない答えは読めない答え（false）で、JavaScript と
        // Godot の SDK と同じく Internal になる。送ったのは status が sent で人数が正の整数のときだけで、ほかは Cancelled。
        internal static bool TryReadInvite(string json, GRushInviteResultWire wire, out GRushInviteResult result)
        {
            result = default(GRushInviteResult);
            if (wire == null || !IsJsonObject(json))
            {
                return false;
            }
            var sent = wire.status == "sent"
                && wire.count >= 1
                && wire.count <= int.MaxValue
                && Math.Floor(wire.count) == wire.count;
            result = new GRushInviteResult
            {
                Status = sent ? GRushInviteStatus.Sent : GRushInviteStatus.Cancelled,
                Count = sent ? (int)wire.count : 0,
            };
            return true;
        }

        private static bool IsJsonObject(string json)
        {
            if (json == null)
            {
                return false;
            }
            foreach (var c in json)
            {
                if (!char.IsWhiteSpace(c))
                {
                    return c == '{';
                }
            }
            return false;
        }

        internal void Forget(GRushRoom closed)
        {
            if (room == closed)
            {
                room = null;
            }
        }

        private void BindHandler()
        {
            if (handlerBound)
            {
                return;
            }
            handlerBound = true;
            GRush.Backend.SetNetEventHandler(OnNetEvent);
        }

        private void OnNetEvent(GRushNetEvent netEvent)
        {
            var current = room;
            if (current != null)
            {
                current.Apply(netEvent);
            }
        }
    }
}
