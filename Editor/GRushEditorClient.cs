using System;
using System.Collections;

namespace GRushSdk.Editor
{
    internal sealed class GRushEditorClient
    {
        public const string ProductionOrigin = "https://gamerush.jp";

        // 以前の本番の接続先。いまは ProductionOrigin へ転送されるだけで、同じ API のトークンが使える。
        private const string LegacyProductionOrigin = "https://gamerush.cc";

        public readonly string Origin;
        private readonly string token;

        public GRushEditorClient(string origin, string token)
        {
            Origin = origin;
            this.token = token;
        }

        /// <summary>
        /// 手元の dev サーバへ向けるときだけ <c>GRUSH_API_ORIGIN</c> を使う。
        /// トークンは接続先ごとに別物なので、保存したものと接続先が食い違えば
        /// ログインし直しになる。
        /// 以前の本番の接続先を指定したときは ProductionOrigin に読み替える。
        /// </summary>
        public static string DefaultOrigin()
        {
            var configured = Environment.GetEnvironmentVariable("GRUSH_API_ORIGIN");
            if (string.IsNullOrEmpty(configured))
            {
                return ProductionOrigin;
            }
            return CanonicalOrigin(configured.TrimEnd('/'));
        }

        /// <summary>
        /// 以前の本番の接続先で保存したトークンを、ログインし直さずに使い続けるために読み替える。
        /// </summary>
        public static string CanonicalOrigin(string origin)
        {
            return origin == LegacyProductionOrigin ? ProductionOrigin : origin;
        }

        public IEnumerator Get(string path, GRushHttpSlot slot)
        {
            return Send(path, "GET", null, slot, GRushEditorHttp.JsonTimeoutSeconds);
        }

        public IEnumerator Post(
            string path,
            string body,
            GRushHttpSlot slot,
            int timeoutSeconds = GRushEditorHttp.JsonTimeoutSeconds
        )
        {
            return Send(path, "POST", body, slot, timeoutSeconds);
        }

        public IEnumerator Put(string path, string body, GRushHttpSlot slot)
        {
            return Send(path, "PUT", body, slot, GRushEditorHttp.JsonTimeoutSeconds);
        }

        private IEnumerator Send(
            string path,
            string method,
            string body,
            GRushHttpSlot slot,
            int timeoutSeconds
        )
        {
            var request = GRushEditorHttp.Json(Origin + path, method, body, token);
            request.timeout = timeoutSeconds;
            return GRushEditorHttp.Send(request, slot);
        }
    }
}
