using System.Collections;
using UnityEditor;

namespace GRushSdk.Editor
{
    internal static class GRushEditorUploader
    {
        // ビルドの presigned URL は一度きりの書き込みなので、
        // 412 は前の試行が届いていた印として成功に扱う。
        private const long ObjectAlreadyWritten = 412;
        private const long SignatureRejected = 403;

        // ビルドの確定には時間がかかることがあるので、ほかの API より長く待つ。
        private const int CompleteTimeoutSeconds = 180;

        public static IEnumerator Run(
            GRushEditorClient client,
            string gameId,
            GRushBuildManifest manifest,
            GRushUploadState state
        )
        {
            state.Reset();
            state.Running = true;

            var prepared = manifest.ForGame(gameId, CanvasSizeFor(manifest));
            if (!prepared.Ok)
            {
                Fail(state, prepared.Error);
                yield break;
            }

            state.TotalBytes = prepared.TotalBytes;
            state.Phase = "ビルドを作成しています…";

            var slot = new GRushHttpSlot();
            yield return client.Post("/api/games/" + gameId + "/builds", prepared.ToRequestBody(), slot);
            if (!slot.Result.Ok || slot.Result.Json == null)
            {
                Fail(
                    state,
                    "ビルドの作成に失敗しました: "
                        + slot.Result.Message()
                        + "（自動では再試行しません。原因を直してからやり直してください）"
                );
                yield break;
            }

            state.BuildId = slot.Result.Json.Get("build").Get("id").AsString(null);
            state.Pending = GRushEditorUploadTickets.From(
                slot.Result.Json.Get("uploadUrls"),
                prepared
            );
            state.NeedsComplete = true;
            if (string.IsNullOrEmpty(state.BuildId) || state.Pending.Count != prepared.Files.Count)
            {
                Fail(state, "アップロード先の応答を読めませんでした。Studio でビルドの状態を確認してください。");
                yield break;
            }
            yield return Resume(client, state);
        }

        /// <summary>
        /// 書き出した <c>index.html</c> の canvas の大きさを送る。Player Settings は書き出しの後に
        /// 変わりうるので、読めないときだけ使う。
        /// </summary>
        public static GRushCanvasSize CanvasSizeFor(GRushBuildManifest manifest)
        {
            return manifest.CanvasSize
                ?? GRushCanvasSize.From(
                    PlayerSettings.defaultWebScreenWidth,
                    PlayerSettings.defaultWebScreenHeight
                );
        }

        public static IEnumerator Resume(GRushEditorClient client, GRushUploadState state)
        {
            state.Running = true;
            state.Cancelled = false;
            state.Error = null;
            state.Phase = "ファイルを送っています…";

            while (state.Pending.Count > 0)
            {
                if (state.Cancelled)
                {
                    FailCancelled(state);
                    yield break;
                }
                var ticket = state.Pending[0];
                state.CurrentPath = ticket.Path;
                state.FileProgress = 0f;
                var outcome = new GRushHttpSlot();
                yield return PutFile(ticket, state, outcome);
                if (outcome.Result == null)
                {
                    yield break;
                }
                state.Pending.RemoveAt(0);
                state.UploadedBytes += ticket.File.Size;
            }

            state.CurrentPath = "";
            yield return Complete(client, state);
        }

        private static IEnumerator PutFile(
            GRushUploadTicket ticket,
            GRushUploadState state,
            GRushHttpSlot outcome
        )
        {
            for (var attempt = 1; attempt <= GRushEditorPutPolicy.Attempts; attempt++)
            {
                if (state.Cancelled)
                {
                    FailCancelled(state);
                    yield break;
                }
                using (
                    var request = GRushEditorHttp.PutFile(
                        ticket.Url,
                        ticket.File,
                        ticket.Headers
                    )
                )
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (state.Cancelled)
                        {
                            request.Abort();
                        }
                        state.FileProgress = request.uploadProgress;
                        yield return null;
                    }
                    var result = GRushHttpResult.From(request);
                    if (result.Ok || result.Status == ObjectAlreadyWritten)
                    {
                        outcome.Result = result;
                        yield break;
                    }
                    if (state.Cancelled)
                    {
                        FailCancelled(state);
                        yield break;
                    }
                    if (result.Status == SignatureRejected)
                    {
                        state.NeedsComplete = false;
                        Fail(
                            state,
                            ticket.Path
                                + ": アップロード用の URL が期限切れです（有効期限1時間）。"
                                + "『アップロードする』をもう一度押すと新しいビルドで送り直します。"
                                + "途中のビルドは24時間後に自動で片付きます。"
                        );
                        yield break;
                    }
                    if (attempt == GRushEditorPutPolicy.Attempts)
                    {
                        Fail(state, ticket.Path + ": 送信に失敗しました: " + result.Message());
                        yield break;
                    }
                }
                var until =
                    EditorApplication.timeSinceStartup
                    + GRushEditorPutPolicy.RetryDelaySeconds(attempt);
                while (!state.Cancelled && EditorApplication.timeSinceStartup < until)
                {
                    yield return null;
                }
            }
        }

        private static IEnumerator Complete(GRushEditorClient client, GRushUploadState state)
        {
            state.Phase = "ビルドを確定しています…";
            var slot = new GRushHttpSlot();
            yield return client.Post(
                "/api/builds/" + state.BuildId + "/complete",
                "{}",
                slot,
                CompleteTimeoutSeconds
            );
            if (slot.Result.Ok)
            {
                state.EntryUrl = slot.Result.Json == null
                    ? null
                    : slot.Result.Json.Get("entryUrl").AsString(null);
                state.LeaderboardWarning = GRushEditorCompleteLeaderboards.FailedMessage(
                    slot.Result.Json
                );
                if (state.LeaderboardWarning != null)
                {
                    UnityEngine.Debug.LogWarning("[GRush] " + state.LeaderboardWarning);
                }
                state.NeedsComplete = false;
                state.Running = false;
                state.Done = true;
                state.Phase = "アップロードが終わりました。";
                yield break;
            }
            var message = slot.Result.Message();
            if (GRushEditorCompleteFailure.RequiresNewBuild(slot.Result.Status, message))
            {
                // 中身の検査に落ちた 422 は、どのファイルかを params.path で返す。
                var path = slot.Result.Json == null
                    ? null
                    : slot.Result.Json.Get("params").Get("path").AsString(null);
                state.NeedsComplete = false;
                Fail(
                    state,
                    "このビルドはもう確定できません（"
                        + message
                        + (string.IsNullOrEmpty(path) ? "" : ": " + path)
                        + "）。『アップロードする』をもう一度押すと新しいビルドで送り直します。"
                );
                yield break;
            }
            Fail(state, "ビルドの確定に失敗しました: " + message);
        }

        private static void FailCancelled(GRushUploadState state)
        {
            Fail(state, "アップロードを中止しました。残り " + state.Pending.Count + " ファイルです。");
        }

        private static void Fail(GRushUploadState state, string message)
        {
            state.Error = message;
            state.Phase = message;
            state.Running = false;
        }
    }
}
