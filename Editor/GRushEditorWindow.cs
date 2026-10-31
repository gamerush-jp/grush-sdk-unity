using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GRushSdk.Editor
{
    public sealed partial class GRushEditorWindow : EditorWindow
    {
        private static readonly string[] BaseScopes =
        {
            "games:read",
            "leaderboards:read",
            "leaderboards:write",
            "builds:write",
        };

        private static readonly string[] VisibilityValues = { "private", "unlisted" };
        private static readonly string[] VisibilityLabels =
        {
            "非公開（private）",
            "限定公開（unlisted・審査なし）",
        };

        private readonly GRushVersionGate gate = new GRushVersionGate();
        private readonly GRushUploadState upload = new GRushUploadState();
        private readonly GRushLeaderboardDraft leaderboard = new GRushLeaderboardDraft();

        private GRushCredentials credentials;
        private GRushEditorLogin login;
        private List<GRushGameSummary> games;
        private bool busy;
        private bool requestGamesCreate;
        private string notice;
        private string error;
        private int selectedGame;
        private string newGameTitle = "";
        private string newGameDescription = "";
        private string newGameHowToPlay = "";
        // EditorStyles は OnGUI の中でしか読めないので、最初に描くときに作る。
        private GUIStyle howToPlayStyle;
        private int newGameVisibility;
        private bool newGameConfirmRights;
        private bool newGameDeclareAllAges;
        private string buildDirectory = "";
        private GRushBuildManifest manifest;
        private Vector2 scroll;

        [MenuItem("GameRush/GameRush ウィンドウ", false, 0)]
        public static void Open()
        {
            GetWindow<GRushEditorWindow>("GameRush").Show();
        }

        [MenuItem("GameRush/ログイン", false, 20)]
        public static void OpenLogin()
        {
            var window = GetWindow<GRushEditorWindow>("GameRush");
            window.Show();
            window.StartLogin();
        }

        [MenuItem("GameRush/ログアウト", false, 21)]
        public static void MenuLogout()
        {
            var confirmed = EditorUtility.DisplayDialog(
                "GameRush",
                "保存したトークンを消します。よろしいですか。",
                "消す",
                "やめる"
            );
            if (!confirmed)
            {
                return;
            }
            GRushEditorCredentials.Clear();
            foreach (var window in Resources.FindObjectsOfTypeAll<GRushEditorWindow>())
            {
                window.credentials = null;
                window.games = null;
                window.Repaint();
            }
        }

        private void OnEnable()
        {
            credentials = GRushEditorCredentials.Load();
            if (credentials != null && credentials.Origin != GRushEditorClient.DefaultOrigin())
            {
                notice = "保存したトークンは別の接続先のものでした。ログインし直してください。";
                credentials = null;
            }
            Run(gate.Check(GRushEditorClient.DefaultOrigin()));
        }

        private void Update()
        {
            if (busy || login != null)
            {
                Repaint();
            }
        }

        private GRushEditorClient Client()
        {
            return new GRushEditorClient(credentials.Origin, credentials.Token);
        }

        private void Run(IEnumerator routine)
        {
            busy = true;
            error = null;
            GRushEditorRoutine.Start(Wrap(routine), OnRoutineError);
        }

        private IEnumerator Wrap(IEnumerator inner)
        {
            yield return inner;
            busy = false;
            Repaint();
        }

        private void OnRoutineError(Exception failure)
        {
            busy = false;
            login = null;
            gate.FailChecking(failure.Message);
            error = failure.Message;
            Debug.LogException(failure);
            Repaint();
        }

        private void StartLogin()
        {
            if (!gate.AllowsWrites)
            {
                error = "対応版の確認が済んでからログインしてください。";
                Repaint();
                return;
            }
            var scopes = new List<string>(BaseScopes);
            if (requestGamesCreate)
            {
                scopes.Add("games:create");
            }
            Run(LoginRoutine(scopes.ToArray()));
        }

        private IEnumerator LoginRoutine(string[] scopes)
        {
            notice = null;
            login = new GRushEditorLogin(GRushEditorClient.DefaultOrigin(), scopes, ClientName());
            yield return login.Run();
            var finished = login;
            login = null;
            if (finished.Error != null)
            {
                error = finished.Error;
                yield break;
            }
            credentials = finished.Credentials;
            notice = "ログインしました。";
            yield return RefreshGames();
        }

        private static string ClientName()
        {
            var name = "Unity Editor / " + Application.productName;
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }
}
