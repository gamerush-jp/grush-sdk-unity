#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using AOT;

namespace GRushSdk
{
    internal sealed class GRushWebGLBackend
        : IGRushBackend,
            IGRushShareBackend,
            IGRushLocaleBackend,
            IGRushPlayerStatePreviewBackend,
            IGRushLaunchBackend,
            IGRushAnalyticsBackend
    {
        private delegate void RpcCallback(int requestId, int ok, IntPtr json, int length);
        private delegate void NetCallback(
            int kind,
            int from,
            int channel,
            int seq,
            IntPtr data,
            int length
        );

        [DllImport("__Internal")]
        private static extern int GRushJsPresent();

        [DllImport("__Internal")]
        private static extern int GRushJsProtocolVersion();

        [DllImport("__Internal")]
        private static extern void GRushJsInit(RpcCallback onRpc, NetCallback onNet);

        [DllImport("__Internal")]
        private static extern void GRushJsCall(int requestId, string method, string paramsJson);

        [DllImport("__Internal")]
        private static extern void GRushJsSend(byte[] payload, int length, int channel, int to);

        [DllImport("__Internal")]
        private static extern void GRushJsShare(
            int requestId,
            string paramsJson,
            byte[] image,
            int length
        );

        private delegate void LocaleCallback(IntPtr json, int length);

        [DllImport("__Internal")]
        private static extern string GRushJsLocaleCurrent();

        [DllImport("__Internal")]
        private static extern void GRushJsLocaleSubscribe(LocaleCallback onLocale);

        [DllImport("__Internal")]
        private static extern string GRushJsLaunchCurrent();

        private delegate void PlayerStatePreviewCallback(IntPtr json, int length);

        [DllImport("__Internal")]
        private static extern void GRushJsPlayerStatePreviewSubscribe(
            PlayerStatePreviewCallback onPreview
        );

        [DllImport("__Internal")]
        private static extern void GRushJsPlayerStatePreviewUnsubscribe();

        private static readonly PlayerStatePreviewCallback PlayerStatePreviewHandler =
            OnPlayerStatePreview;
        private static Action<string> playerStatePreviewHandler;

        private static readonly LocaleCallback LocaleHandler = OnLocale;
        private static Action<string> localeHandler;
        private static readonly RpcCallback RpcHandler = OnRpc;
        private static readonly NetCallback NetHandler = OnNet;
        private static readonly Dictionary<int, Action<GRushRpcResponse>> Pending =
            new Dictionary<int, Action<GRushRpcResponse>>();
        private static Action<GRushNetEvent> netHandler;
        private static int nextRequestId = 1;
        private static bool initialized;

        public static bool IsPresent()
        {
            return GRushJsPresent() == 1;
        }

        public bool IsAvailable
        {
            get { return IsPresent(); }
        }

        public int ProtocolVersion
        {
            get { return GRushJsProtocolVersion(); }
        }

        public void Call(string method, string paramsJson, Action<GRushRpcResponse> onDone)
        {
            EnsureInitialized();
            var requestId = nextRequestId++;
            if (onDone != null)
            {
                Pending[requestId] = onDone;
            }
            GRushJsCall(
                requestId,
                method,
                GRushTransportText.ReplaceLoneSurrogates(paramsJson ?? string.Empty)
            );
        }

        public void Share(string paramsJson, byte[] image, Action<GRushRpcResponse> onDone)
        {
            EnsureInitialized();
            var requestId = nextRequestId++;
            if (onDone != null)
            {
                Pending[requestId] = onDone;
            }
            GRushJsShare(
                requestId,
                GRushTransportText.ReplaceLoneSurrogates(paramsJson ?? string.Empty),
                image,
                image == null ? 0 : image.Length
            );
        }

        public void Send(byte[] payload, int count, GRushChannel channel, int to)
        {
            EnsureInitialized();
            GRushJsSend(payload, count, GRushChannels.ToCode(channel), to);
        }

        public void SetNetEventHandler(Action<GRushNetEvent> handler)
        {
            EnsureInitialized();
            netHandler = handler;
        }

        public string LocaleCurrentJson()
        {
            return GRushJsLocaleCurrent();
        }

        public void SetLocaleChangedHandler(Action<string> handler)
        {
            var first = localeHandler == null;
            localeHandler = handler;
            if (first && handler != null)
            {
                GRushJsLocaleSubscribe(LocaleHandler);
            }
        }

        // GameRush の onPreview はハンドラを1つだけ持つので、JS への登録はハンドラの有無が変わったときだけにする。
        public void SetPlayerStatePreviewHandler(Action<string> handler)
        {
            var had = playerStatePreviewHandler != null;
            playerStatePreviewHandler = handler;
            if (!had && handler != null)
            {
                GRushJsPlayerStatePreviewSubscribe(PlayerStatePreviewHandler);
            }
            else if (had && handler == null)
            {
                GRushJsPlayerStatePreviewUnsubscribe();
            }
        }

        public string LaunchCurrentJson()
        {
            return GRushJsLaunchCurrent();
        }

        public bool IsAnalyticsAvailable
        {
            get { return GRushWebGLAnalytics.IsAvailable(); }
        }

        public bool LogAnalytics(GRushAnalyticsEvent analyticsEvent)
        {
            return GRushWebGLAnalytics.Log(analyticsEvent);
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }
            initialized = true;
            GRushJsInit(RpcHandler, NetHandler);
        }

        [MonoPInvokeCallback(typeof(RpcCallback))]
        private static void OnRpc(int requestId, int ok, IntPtr json, int length)
        {
            Action<GRushRpcResponse> pending;
            if (!Pending.TryGetValue(requestId, out pending))
            {
                return;
            }
            Pending.Remove(requestId);
            var text = ReadString(json, length);
            GRushDispatcher.Post(() => pending(ok == 1 ? Succeeded(text) : Failed(text)));
        }

        [MonoPInvokeCallback(typeof(NetCallback))]
        private static void OnNet(int kind, int from, int channel, int seq, IntPtr data, int length)
        {
            var handler = netHandler;
            if (handler == null)
            {
                return;
            }
            var netEvent = new GRushNetEvent
            {
                Kind = (GRushNetEventKind)kind,
                From = from,
                Channel = GRushChannels.FromCode(channel),
                Seq = unchecked((uint)seq),
            };
            if (kind == (int)GRushNetEventKind.Message)
            {
                netEvent.Payload = ReadBytes(data, length);
            }
            else
            {
                netEvent.Detail = ReadString(data, length);
            }
            GRushDispatcher.Post(() => handler(netEvent));
        }

        [MonoPInvokeCallback(typeof(LocaleCallback))]
        private static void OnLocale(IntPtr json, int length)
        {
            var handler = localeHandler;
            if (handler == null)
            {
                return;
            }
            var text = ReadString(json, length);
            GRushDispatcher.Post(() => handler(text));
        }

        [MonoPInvokeCallback(typeof(PlayerStatePreviewCallback))]
        private static void OnPlayerStatePreview(IntPtr json, int length)
        {
            var handler = playerStatePreviewHandler;
            if (handler == null)
            {
                return;
            }
            var text = ReadString(json, length);
            GRushDispatcher.Post(() => handler(text));
        }

        private static GRushRpcResponse Succeeded(string text)
        {
            return GRushRpcResponse.Success(text);
        }

        private static GRushRpcResponse Failed(string text)
        {
            var wire = GRushWire.Parse<GRushErrorWire>(text);
            return GRushRpcResponse.Failure(
                GRushErrorCodes.Parse(wire == null ? null : wire.code),
                wire == null ? "GameRush API call failed." : wire.message
            );
        }

        private static byte[] ReadBytes(IntPtr pointer, int length)
        {
            if (pointer == IntPtr.Zero || length <= 0)
            {
                return Array.Empty<byte>();
            }
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }

        private static string ReadString(IntPtr pointer, int length)
        {
            var bytes = ReadBytes(pointer, length);
            return bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
        }
    }
}
#endif
