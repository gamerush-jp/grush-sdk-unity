using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

namespace GRushSdk.Editor
{
    internal sealed class GRushHttpResult
    {
        public long Status;
        public string Body;
        public string Transport;
        public GRushJson Json;

        public bool Ok => Status >= 200 && Status < 300;

        public static GRushHttpResult From(UnityWebRequest request)
        {
            var body = request.downloadHandler == null ? null : request.downloadHandler.text;
            return new GRushHttpResult
            {
                Status = request.responseCode,
                Body = body,
                Transport = request.result == UnityWebRequest.Result.Success ? null : request.error,
                Json = GRushJson.Parse(body),
            };
        }

        public string Message()
        {
            if (Json != null)
            {
                var error = Json["error"];
                if (error != null)
                {
                    return error.AsString(null) ?? "";
                }
                var code = Json["code"];
                if (code != null)
                {
                    return code.AsString(null) ?? "";
                }
            }
            if (Status > 0)
            {
                return "HTTP " + Status;
            }
            return string.IsNullOrEmpty(Transport) ? "通信に失敗しました。" : Transport;
        }
    }

    internal static class GRushEditorHttp
    {
        public const int JsonTimeoutSeconds = 30;

        public static UnityWebRequest Json(string url, string method, string body, string bearer)
        {
            var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(bearer))
            {
                request.SetRequestHeader("Authorization", "Bearer " + bearer);
            }
            request.timeout = JsonTimeoutSeconds;
            return request;
        }

        public static UnityWebRequest PutFile(
            string url,
            GRushBuildFile file,
            Dictionary<string, string> headers
        )
        {
            var request = new UnityWebRequest(url, "PUT");
            // ファイルはディスクから流して送り、ファイル全体をメモリへ読み込まない。
            // 再試行のたびにこのリクエストを作り直し、ディスクから読み直す。
            // ディスクに実体が無いファイルだけ、持っている中身をそのまま送る。
            UploadHandler upload =
                file.Content != null
                    ? (UploadHandler)new UploadHandlerRaw(file.Content)
                    : new UploadHandlerFile(file.FullPath);
            upload.contentType = "application/octet-stream";
            request.uploadHandler = upload;
            request.disposeUploadHandlerOnDispose = true;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = GRushEditorPutPolicy.TimeoutSeconds(file.Size);
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.SetRequestHeader(header.Key, header.Value);
                }
            }
            return request;
        }

        public static IEnumerator Send(UnityWebRequest request, GRushHttpSlot slot)
        {
            using (request)
            {
                yield return request.SendWebRequest();
                slot.Result = GRushHttpResult.From(request);
            }
        }
    }

    internal sealed class GRushHttpSlot
    {
        public GRushHttpResult Result;
    }
}
