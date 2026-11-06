using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GRushSdk.Editor
{
    internal sealed class GRushBuildOutcome
    {
        public const int Succeeded = 0;
        public const int BuildFailed = 1;
        public const int BadInput = 2;
        public const int Unexpected = 3;
        public const int RuleViolation = 4;

        public int Code;
        public string Message;
        public string OutputPath;
        public List<string> Changes = new List<string>();
        public long TotalBytes;

        public bool Ok => Code == Succeeded;
        public bool OverGuideline => TotalBytes > GRushEditorRecommendedSettings.GuidelineBytes;
    }

    public static class GRushBuild
    {
        public const string OutputArgument = "-grushOutput";
        private const string LogPrefix = "[grush-build] ";

        public static void BuildRecommended()
        {
            GRushBuildOutcome outcome;
            try
            {
                var output = ArgumentValue(OutputArgument);
                outcome = string.IsNullOrEmpty(output)
                    ? Fail(GRushBuildOutcome.BadInput, OutputArgument + " で出力先を指定してください。")
                    : Build(output);
            }
            catch (Exception failure)
            {
                outcome = Fail(GRushBuildOutcome.Unexpected, "想定外の例外: " + failure);
            }
            Log("exit " + outcome.Code + ": " + outcome.Message);
            EditorApplication.Exit(outcome.Code);
        }

        internal static GRushBuildOutcome Build(string output)
        {
            try
            {
                return BuildUnchecked(output);
            }
            catch (Exception failure)
            {
                Debug.LogException(failure);
                return Fail(GRushBuildOutcome.Unexpected, "想定外の例外: " + failure.Message);
            }
        }

        internal static string DefaultOutput()
        {
            return Path.Combine(GRushEditorBuildOutput.ProjectRoot(), "Build", "GameRush");
        }

        private static GRushBuildOutcome BuildUnchecked(string output)
        {
            var resolved = GRushEditorBuildOutput.Resolve(output, out var pathError);
            if (resolved == null)
            {
                return Fail(GRushBuildOutcome.BadInput, pathError);
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                return Fail(
                    GRushBuildOutcome.BadInput,
                    "WebGL Build Support モジュールが入っていません。Unity Hub から追加してください。"
                );
            }
            var scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                return Fail(
                    GRushBuildOutcome.BadInput,
                    "Build Settings に有効なシーンがありません。"
                );
            }

            var changes = GRushEditorRecommendedSettings.ApplyAll();
            foreach (var change in changes)
            {
                Log("set " + change);
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                Log(
                    "ビルドターゲットを "
                        + EditorUserBuildSettings.activeBuildTarget
                        + " から WebGL へ切り替えます。"
                );
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, true);
            }
            Directory.CreateDirectory(resolved);
            Log("output " + resolved);

            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = resolved,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.None,
                }
            );
            var summary = report.summary;
            Log("result=" + summary.result + " errors=" + summary.totalErrors);
            if (summary.result != BuildResult.Succeeded)
            {
                return Finish(
                    GRushBuildOutcome.BuildFailed,
                    "ビルドに失敗しました（" + summary.result + "）。Console のエラーを確認してください。",
                    resolved,
                    changes,
                    0
                );
            }

            var manifest = GRushEditorBuildManifest.Collect(resolved);
            if (!manifest.Ok)
            {
                return Finish(
                    GRushBuildOutcome.RuleViolation,
                    "GameRush へ上げられない出力です: " + manifest.Error,
                    resolved,
                    changes,
                    manifest.TotalBytes
                );
            }
            var outcome = Finish(
                GRushBuildOutcome.Succeeded,
                "ビルドしました: "
                    + manifest.UploadFileCount
                    + " ファイル / "
                    + Megabytes(manifest.UploadBytes),
                resolved,
                changes,
                manifest.UploadBytes
            );
            if (outcome.OverGuideline)
            {
                Log(
                    "warning: "
                        + Megabytes(outcome.TotalBytes)
                        + " は目安の "
                        + Megabytes(GRushEditorRecommendedSettings.GuidelineBytes)
                        + " を超えています。"
                );
            }
            return outcome;
        }

        internal static string Megabytes(long bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        private static string[] EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }
            return scenes.ToArray();
        }

        private static GRushBuildOutcome Fail(int code, string message)
        {
            return Finish(code, message, null, new List<string>(), 0);
        }

        private static GRushBuildOutcome Finish(
            int code,
            string message,
            string output,
            List<string> changes,
            long totalBytes
        )
        {
            Log(message);
            return new GRushBuildOutcome
            {
                Code = code,
                Message = message,
                OutputPath = output,
                Changes = changes,
                TotalBytes = totalBytes,
            };
        }

        private static void Log(string message)
        {
            Debug.Log(LogPrefix + message);
        }

        private static string ArgumentValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (args[index] == name)
                {
                    return args[index + 1];
                }
            }
            return null;
        }
    }
}
