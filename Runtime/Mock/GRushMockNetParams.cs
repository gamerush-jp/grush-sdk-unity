using System.Text.RegularExpressions;

namespace GRushSdk
{
    /// <summary>
    /// モックの <c>net.join</c> が mode と部屋コードを、GameRush と同じ形の規則で検査する。
    /// </summary>
    internal static class GRushMockNetParams
    {
        public const string InvalidModeMessage = "Invalid mode.";
        public const string InvalidRoomCodeMessage = "Invalid room code.";

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
        /// 無いものにして送るので、ここでも空はそう扱う。
        /// </summary>
        public static string Validate(string mode, string roomCode)
        {
            if (!ModePattern.IsMatch(NormalizeMode(mode)))
            {
                return InvalidModeMessage;
            }
            if (!string.IsNullOrEmpty(roomCode) && !RoomCodePattern.IsMatch(roomCode))
            {
                return InvalidRoomCodeMessage;
            }
            return null;
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
