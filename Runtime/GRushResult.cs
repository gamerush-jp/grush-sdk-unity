namespace GRushSdk
{
    public enum GRushErrorCode
    {
        None,
        Unsupported,
        Unavailable,
        Timeout,
        RateLimited,
        SignInRequired,
        ConsentDeclined,
        InvalidParams,
        Internal,
        /// <summary>
        /// 確認用の表示（<c>GRush.PlayerState.PreviewReceived</c>）を購読していないので、他のプレイヤーの公開プレイヤー状態を読めない。
        /// </summary>
        PreviewRequired,
    }

    public readonly struct GRushResult<T>
    {
        public readonly bool Ok;
        public readonly T Value;
        public readonly GRushErrorCode Code;
        public readonly string Message;

        private GRushResult(bool ok, T value, GRushErrorCode code, string message)
        {
            Ok = ok;
            Value = value;
            Code = code;
            Message = message;
        }

        public static GRushResult<T> Success(T value)
        {
            return new GRushResult<T>(true, value, GRushErrorCode.None, null);
        }

        public static GRushResult<T> Failure(GRushErrorCode code, string message)
        {
            return new GRushResult<T>(false, default, code, message);
        }

        public static GRushResult<T> Unsupported()
        {
            return Failure(GRushErrorCode.Unsupported, "GameRush GameAPI is not available here.");
        }
    }

    public static class GRushErrorCodes
    {
        public static GRushErrorCode Parse(string code)
        {
            switch (code)
            {
                case "unsupportedMethod":
                    return GRushErrorCode.Unsupported;
                case "unavailable":
                    return GRushErrorCode.Unavailable;
                case "timeout":
                    return GRushErrorCode.Timeout;
                case "rateLimited":
                    return GRushErrorCode.RateLimited;
                case "signInRequired":
                    return GRushErrorCode.SignInRequired;
                case "consentDeclined":
                    return GRushErrorCode.ConsentDeclined;
                case "invalidParams":
                    return GRushErrorCode.InvalidParams;
                case "previewRequired":
                    return GRushErrorCode.PreviewRequired;
                default:
                    return GRushErrorCode.Internal;
            }
        }
    }
}
