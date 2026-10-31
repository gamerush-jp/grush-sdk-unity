using UnityEditor;
using UnityEngine;

namespace GRushSdk.Editor
{
    public sealed partial class GRushEditorWindow
    {
        private void DrawUpload()
        {
            Section("ビルドをアップロード");
            if (!credentials.HasScope("builds:write"))
            {
                EditorGUILayout.HelpBox(
                    "アップロードには builds:write が要ります。ログインし直してください。",
                    MessageType.None
                );
                return;
            }

            EditorGUILayout.BeginHorizontal();
            buildDirectory = EditorGUILayout.TextField("WebGL の出力先", buildDirectory);
            if (GUILayout.Button("参照", GUILayout.Width(60)))
            {
                var picked = EditorUtility.OpenFolderPanel(
                    "WebGL の書き出し先を選ぶ",
                    buildDirectory,
                    ""
                );
                if (!string.IsNullOrEmpty(picked))
                {
                    buildDirectory = picked;
                    Rescan();
                }
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(busy || buildDirectory.Length == 0))
            {
                if (GUILayout.Button("中身を調べる"))
                {
                    Rescan();
                }
            }
            DrawManifest();
            DrawUploadActions();
        }

        private void DrawManifest()
        {
            if (manifest == null)
            {
                return;
            }
            if (!manifest.Ok)
            {
                EditorGUILayout.HelpBox(manifest.Error, MessageType.Error);
                return;
            }
            EditorGUILayout.LabelField(
                "送るもの",
                manifest.Files.Count
                    + " ファイル / "
                    + (manifest.TotalBytes / (1024 * 1024))
                    + " MB"
            );
        }

        private void DrawUploadActions()
        {
            var game = SelectedGame();
            var ready = manifest != null && manifest.Ok && game != null;
            using (new EditorGUI.DisabledScope(busy || !ready))
            {
                if (GUILayout.Button("アップロードする（再試行はしません）"))
                {
                    Run(GRushEditorUploader.Run(Client(), game.Id, manifest, upload));
                }
            }

            if (upload.Running)
            {
                var rect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(rect, upload.Progress, upload.Phase + " " + upload.CurrentPath);
                if (GUILayout.Button("キャンセル"))
                {
                    upload.Cancelled = true;
                }
            }

            if (upload.Error != null)
            {
                EditorGUILayout.HelpBox(upload.Error, MessageType.Error);
            }
            if (upload.CanResume)
            {
                using (new EditorGUI.DisabledScope(busy))
                {
                    var label =
                        upload.Pending.Count > 0
                            ? "残り " + upload.Pending.Count + " ファイルだけ送り直す"
                            : "ビルドの確定だけやり直す";
                    if (GUILayout.Button(label))
                    {
                        Run(GRushEditorUploader.Resume(Client(), upload));
                    }
                }
            }
            if (upload.Done)
            {
                EditorGUILayout.HelpBox(
                    "アップロードが終わりました。限定公開（unlisted）のゲームは、URL を知っている人がすぐ遊べます。"
                        + "一般公開（public）のゲームで審査が要るときは、承認されてから新しいビルドが配信されます。"
                        + "配信の状況は Studio で確認してください。",
                    MessageType.Info
                );
                if (!string.IsNullOrEmpty(upload.EntryUrl))
                {
                    EditorGUILayout.SelectableLabel(upload.EntryUrl, GUILayout.Height(18));
                }
                if (upload.LeaderboardWarning != null)
                {
                    EditorGUILayout.HelpBox(upload.LeaderboardWarning, MessageType.Warning);
                }
            }
        }

        private void DrawLeaderboard()
        {
            Section("ランキングの宣言");
            if (!credentials.HasScope("leaderboards:write"))
            {
                EditorGUILayout.HelpBox(
                    "宣言には leaderboards:write が要ります。ログインし直してください。",
                    MessageType.None
                );
                return;
            }

            leaderboard.Key = EditorGUILayout.TextField("key", leaderboard.Key);
            leaderboard.Title = EditorGUILayout.TextField("表示名", leaderboard.Title);
            leaderboard.SortIndex = EditorGUILayout.Popup(
                "並び",
                leaderboard.SortIndex,
                GRushLeaderboardDraft.Sorts
            );
            leaderboard.ValueTypeIndex = EditorGUILayout.Popup(
                "値の種類",
                leaderboard.ValueTypeIndex,
                GRushLeaderboardDraft.ValueTypes
            );
            leaderboard.AggregationIndex = EditorGUILayout.Popup(
                "集約",
                leaderboard.AggregationIndex,
                GRushLeaderboardDraft.Aggregations
            );
            leaderboard.PeriodIndex = EditorGUILayout.Popup(
                "期間",
                leaderboard.PeriodIndex,
                GRushLeaderboardDraft.Periods
            );
            leaderboard.HasRange = EditorGUILayout.ToggleLeft(
                "値域を決める",
                leaderboard.HasRange
            );
            if (leaderboard.HasRange)
            {
                leaderboard.MinValue = EditorGUILayout.DoubleField("最小", leaderboard.MinValue);
                leaderboard.MaxValue = EditorGUILayout.DoubleField("最大", leaderboard.MaxValue);
            }
            EditorGUILayout.HelpBox(
                "key で冪等に適用します。投稿が1件でもある枠では並び・値の種類・集約・期間を変えられません。",
                MessageType.None
            );

            var game = SelectedGame();
            var ready =
                game != null
                && leaderboard.Key.Trim().Length > 0
                && leaderboard.Title.Trim().Length > 0;
            using (new EditorGUI.DisabledScope(busy || !ready))
            {
                if (GUILayout.Button("宣言する"))
                {
                    Run(DeclareLeaderboardRoutine(game.Id));
                }
            }
        }
    }
}
