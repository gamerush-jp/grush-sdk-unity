using UnityEditor;
using UnityEngine;

namespace GRushSdk.Editor
{
    public sealed partial class GRushEditorWindow
    {
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawGate();
            DrawRecommendedBuild();
            if (gate.AllowsWrites)
            {
                DrawAccount();
                if (credentials != null)
                {
                    DrawGames();
                    DrawUpload();
                    DrawLeaderboard();
                }
            }
            DrawMessages();
            EditorGUILayout.EndScrollView();
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private void DrawGate()
        {
            switch (gate.State)
            {
                case GRushGateState.Checking:
                    EditorGUILayout.HelpBox("Editor 拡張の対応版を確認しています…", MessageType.Info);
                    break;
                case GRushGateState.Rejected:
                    EditorGUILayout.HelpBox(gate.RejectedText(), MessageType.Error);
                    break;
                case GRushGateState.Failed:
                    EditorGUILayout.HelpBox(
                        gate.Error + "\n書き込み操作は確認が済むまで使えません。",
                        MessageType.Error
                    );
                    break;
            }
            if (!gate.CanRetry)
            {
                return;
            }
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button("もう一度確認する"))
                {
                    Run(gate.Check(GRushEditorClient.DefaultOrigin()));
                }
            }
        }

        private void DrawAccount()
        {
            Section("アカウント");
            if (credentials == null)
            {
                if (login != null)
                {
                    EditorGUILayout.LabelField(login.Status);
                    if (GUILayout.Button("キャンセル"))
                    {
                        login.Cancel();
                    }
                    return;
                }
                requestGamesCreate = EditorGUILayout.ToggleLeft(
                    "新しいゲームを作れるようにする（games:create）",
                    requestGamesCreate
                );
                EditorGUILayout.HelpBox(
                    "games:create を含めたトークンは、対象ゲームを絞れません。"
                        + "既存のゲームへ上げるだけならチェックを外したままにしてください。",
                    MessageType.None
                );
                using (new EditorGUI.DisabledScope(busy))
                {
                    if (GUILayout.Button("ブラウザでログイン"))
                    {
                        StartLogin();
                    }
                }
                return;
            }

            EditorGUILayout.LabelField("接続先", credentials.Origin);
            EditorGUILayout.LabelField("トークン", credentials.Preview + "…");
            EditorGUILayout.LabelField("権限", string.Join(", ", credentials.Scopes));
            EditorGUILayout.LabelField("有効期限", credentials.ExpiresAt);
            EditorGUILayout.LabelField("保存先", GRushEditorCredentials.FilePath);
            if (credentials.IsExpired())
            {
                EditorGUILayout.HelpBox(
                    "トークンの有効期限が切れています。ログインし直してください。",
                    MessageType.Warning
                );
            }
            if (GUILayout.Button("ログアウト"))
            {
                GRushEditorCredentials.Clear();
                credentials = null;
                games = null;
            }
        }

        private void DrawGames()
        {
            Section("ゲーム");
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button("一覧を取り直す"))
                {
                    Run(RefreshGames());
                }
            }
            if (games != null && games.Count > 0)
            {
                var labels = new string[games.Count];
                for (var index = 0; index < games.Count; index++)
                {
                    labels[index] = games[index].Title + "（" + games[index].Visibility + "）";
                }
                selectedGame = EditorGUILayout.Popup("対象", selectedGame, labels);
                EditorGUILayout.HelpBox(
                    "既存のゲームの公開設定は Editor から変えられません。Studio で操作してください。",
                    MessageType.None
                );
            }
            else if (games != null)
            {
                EditorGUILayout.LabelField("まだゲームがありません。");
            }
            DrawNewGame();
        }

        private void DrawNewGame()
        {
            if (!credentials.HasScope("games:create"))
            {
                EditorGUILayout.HelpBox(
                    "新しいゲームを作るには games:create が要ります。"
                        + "ログアウトしてから「新しいゲームを作れるようにする」を入れてログインし直してください。",
                    MessageType.None
                );
                return;
            }

            Section("新しいゲームを作る");
            newGameTitle = EditorGUILayout.TextField("タイトル", newGameTitle);
            newGameDescription = EditorGUILayout.TextField("説明", newGameDescription);
            EditorGUILayout.LabelField("遊び方");
            if (howToPlayStyle == null)
            {
                howToPlayStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            }
            newGameHowToPlay = EditorGUILayout.TextArea(
                newGameHowToPlay,
                howToPlayStyle,
                GUILayout.MinHeight(54)
            );
            EditorGUILayout.HelpBox(
                "操作方法・ゲームの目的・攻略のコツなどを書いてください。最大 2000 字。ゲーム詳細にそのまま表示されます。",
                MessageType.None
            );
            newGameVisibility = EditorGUILayout.Popup(
                "公開設定",
                newGameVisibility,
                VisibilityLabels
            );
            EditorGUILayout.HelpBox(
                "限定公開（unlisted）は審査しません。ビルドのアップロードが終わると、URL を知っている人がすぐ遊べます。"
                    + "問題のある作品には、通報・運営による公開保留・アカウントの利用停止で対応します。"
                    + "一般公開（public）への申請は Studio から行います。",
                MessageType.Info
            );
            newGameConfirmRights = EditorGUILayout.ToggleLeft(
                "このゲームと、それに含まれる第三者アセット（画像・音源・フォント・ライブラリ等）について、自身の著作物であるか、GameRush への掲載および利用規約 §4 の許諾に必要な一切の権利・許諾を有していることを確認しました",
                newGameConfirmRights,
                EditorStyles.wordWrappedLabel
            );
            newGameDeclareAllAges = EditorGUILayout.ToggleLeft(
                "本投稿ゲームが、本サービス利用規約 §9 およびゲーム掲載ポリシーに定める全年齢向けの内容であることを申告します。",
                newGameDeclareAllAges,
                EditorStyles.wordWrappedLabel
            );
            // 全年齢向けの申告は、非公開で作るときだけ GameRush が求めない。
            var ready =
                newGameTitle.Trim().Length > 0
                && newGameConfirmRights
                && (VisibilityValues[newGameVisibility] == "private" || newGameDeclareAllAges);
            using (new EditorGUI.DisabledScope(busy || !ready))
            {
                if (GUILayout.Button("作成する（再試行はしません）"))
                {
                    Run(CreateGameRoutine());
                }
            }
        }

        private void DrawMessages()
        {
            EditorGUILayout.Space(8);
            if (error != null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            if (notice != null)
            {
                EditorGUILayout.HelpBox(notice, MessageType.Info);
            }
        }
    }
}
