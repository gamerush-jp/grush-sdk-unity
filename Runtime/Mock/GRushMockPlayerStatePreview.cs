using System;
using System.Globalization;

namespace GRushSdk
{
    // エディタ用の確認用の表示。GameRush と同じく、ハンドラは1つだけ持ち、後から登録したものに置き換える。
    // 同じ写しは1回だけ渡し、ハンドラが無いうちに届いた写しは、次に登録したハンドラへ渡す。
    // 置き換えたハンドラや登録し直したハンドラにも、渡し済みの写しはもう渡さない。
    internal static class GRushMockPlayerStatePreview
    {
        private static string json;
        private static int sequence;
        private static int deliveredSequence;
        private static Action<string> handler;

        // 写しを置いて、ハンドラがあれば渡す。GameRush と同じく、形が崩れていれば何もせずに false を返す。
        public static bool Push(string pseudoId, string payloadJson, int revision, double? rating)
        {
            var payload = payloadJson == null ? null : payloadJson.Trim();
            if (string.IsNullOrEmpty(pseudoId) || GRushMockJson.Members(payload) == null || revision < 0)
            {
                return false;
            }
            if (rating.HasValue && (double.IsNaN(rating.Value) || double.IsInfinity(rating.Value)))
            {
                return false;
            }
            json =
                "{\"state\":{\"pseudoId\":"
                + GRushWire.Escape(pseudoId)
                + ",\"payload\":"
                + payload
                + ",\"revision\":"
                + revision
                + ",\"rating\":"
                + (rating.HasValue ? GRushWire.Number(rating.Value) : "0")
                + ",\"hasRating\":"
                + (rating.HasValue ? "true" : "false")
                + ",\"updatedAt\":"
                + GRushWire.Escape(
                    DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                )
                + "}}";
            sequence++;
            Deliver();
            return true;
        }

        // null を渡すと、今のハンドラが expected のときだけ外す。別のバックエンドが置き換えた後に、前のバックエンドが外しても消えない。
        public static void SetHandler(Action<string> next, Action<string> expected)
        {
            if (next == null)
            {
                if (handler == expected)
                {
                    handler = null;
                }
                return;
            }
            handler = next;
            Deliver();
        }

        public static void Reset()
        {
            json = null;
            sequence = 0;
            deliveredSequence = 0;
        }

        private static void Deliver()
        {
            var current = handler;
            if (current == null || json == null || deliveredSequence == sequence)
            {
                return;
            }
            deliveredSequence = sequence;
            current(json);
        }
    }
}
