using System.Threading.Tasks;

namespace GRushSdk
{
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

        private async Task<GRushResult<GRushRoom>> JoinAsync(
            string mode,
            string roomCode,
            int targetRoomSize
        )
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
            var response = await GRush.CallAsync(
                "net.join",
                GRushWire.JoinParams(mode, roomCode, targetRoomSize)
            );
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
