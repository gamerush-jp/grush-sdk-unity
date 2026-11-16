using System.Text.RegularExpressions;

namespace GRushSdk
{
    /// <summary>
    /// モックの <c>net.join</c> が mode と部屋コードと目安の人数と部屋の定員と形を、GameRush と同じ形の規則で検査する。
    /// </summary>
    internal static class GRushMockNetParams
    {
        public const string InvalidModeMessage = "Invalid mode.";
        public const string InvalidRoomCodeMessage = "Invalid room code.";
        public const string InvalidTargetRoomSizeMessage = "Invalid target room size.";
        public const string InvalidRoomConfigMessage = "Invalid maxPeers or topology.";
        public const string RoomMismatchMessage = "The room exists with a different maxPeers or topology.";

        // 定員を渡さない join の定員。
        public const int DefaultMaxPeers = 8;

        // GameRush が受ける定員の上限と、mesh の定員の上限。
        public const int MaxPeersLimit = 100;
        public const int MeshMaxPeers = 16;

        // `$` は末尾の改行の前でも合うので `\z` で終える。
        private static readonly Regex ModePattern = new Regex(
            @"^[A-Za-z0-9._:-]{1,32}\z",
            RegexOptions.CultureInvariant
        );
        private static readonly Regex RoomCodePattern = new Regex(
            @"^[A-Za-z0-9]{4,12}\z",
            RegexOptions.CultureInvariant
        );

        /// <summary>
        /// 通るなら null、弾くならエラーの文言を返す。SDK は空の mode を "default" に、空の部屋コードを
        /// 無いものにして送り、目安と定員の 0 を送らないので、ここでも空と 0 はそう扱う。topology の null と空は未指定。
        /// </summary>
        public static string Validate(
            string mode,
            string roomCode,
            int targetRoomSize,
            int maxPeers,
            string topology
        )
        {
            if (!ModePattern.IsMatch(NormalizeMode(mode)))
            {
                return InvalidModeMessage;
            }
            if (!string.IsNullOrEmpty(roomCode) && !RoomCodePattern.IsMatch(roomCode))
            {
                return InvalidRoomCodeMessage;
            }
            var effectiveMaxPeers = NormalizeMaxPeers(maxPeers);
            if (effectiveMaxPeers < 1 || effectiveMaxPeers > MaxPeersLimit)
            {
                return InvalidRoomConfigMessage;
            }
            if (!string.IsNullOrEmpty(topology) && topology != "mesh" && topology != "hub")
            {
                return InvalidRoomConfigMessage;
            }
            if (NormalizeTopology(topology) == GRushTopology.Mesh && effectiveMaxPeers > MeshMaxPeers)
            {
                return InvalidRoomConfigMessage;
            }
            if (targetRoomSize != 0 && (targetRoomSize < 1 || targetRoomSize > effectiveMaxPeers))
            {
                return InvalidTargetRoomSizeMessage;
            }
            return null;
        }

        public static int NormalizeMaxPeers(int maxPeers)
        {
            return maxPeers == 0 ? DefaultMaxPeers : maxPeers;
        }

        public static GRushTopology NormalizeTopology(string topology)
        {
            return topology == "hub" ? GRushTopology.Hub : GRushTopology.Mesh;
        }

        public static string NormalizeMode(string mode)
        {
            return string.IsNullOrEmpty(mode) ? "default" : mode;
        }

        /// <summary>GameRush と同じく、部屋コードを大文字にそろえる。無ければ null。</summary>
        public static string NormalizeRoomCode(string roomCode)
        {
            return string.IsNullOrEmpty(roomCode) ? null : roomCode.ToUpperInvariant();
        }
    }
}
