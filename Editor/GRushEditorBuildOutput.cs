using System;
using System.IO;
using UnityEngine;

namespace GRushSdk.Editor
{
    internal static class GRushEditorBuildOutput
    {
        private static readonly string[] ClearableEntries =
        {
            "Build",
            "TemplateData",
            "StreamingAssets",
            "index.html",
            "ServiceWorker.js",
            "manifest.webmanifest",
            GRushEditorLeaderboardManifest.FileName,
        };

        private static readonly string[] ProtectedFolders =
        {
            "Assets",
            "Packages",
            "ProjectSettings",
            "Library",
        };

        public static string ProjectRoot()
        {
            return Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
        }

        public static string Resolve(string output, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(output))
            {
                error = "出力先が空です。";
                return null;
            }
            string resolved;
            try
            {
                resolved = Path.GetFullPath(Path.Combine(ProjectRoot(), output.Trim()));
            }
            catch (Exception failure)
            {
                error = "出力先のパスが読めません: " + failure.Message;
                return null;
            }

            var root = Normalize(ProjectRoot());
            var target = Normalize(resolved);
            if (IsInside(root, target))
            {
                error = "出力先にプロジェクトのフォルダそのもの（またはその親）は選べません: " + resolved;
                return null;
            }
            foreach (var folder in ProtectedFolders)
            {
                if (IsInside(target, root + "/" + folder))
                {
                    error = "出力先に " + folder + "/ の中は選べません: " + resolved;
                    return null;
                }
            }
            if (File.Exists(resolved))
            {
                error = "出力先にファイルがあります。フォルダを選んでください: " + resolved;
                return null;
            }
            var trimmed = resolved.TrimEnd('/', '\\');
            var unknown = FirstUnknownEntry(trimmed);
            if (unknown != null)
            {
                error = "GameRush のビルド以外のファイルがあるフォルダは消せません: " + unknown;
                return null;
            }
            return trimmed;
        }

        private static string FirstUnknownEntry(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }
            foreach (var entry in Directory.GetFileSystemEntries(folder))
            {
                var name = Path.GetFileName(entry);
                if (Array.IndexOf(ClearableEntries, name) < 0)
                {
                    return Path.Combine(folder, name);
                }
            }
            return null;
        }

        private static string Normalize(string path)
        {
            var normalized = path.Replace('\\', '/').TrimEnd('/');
            return normalized.Length == 0 ? "/" : normalized;
        }

        private static bool IsInside(string path, string folder)
        {
            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var prefix = folder.EndsWith("/") ? folder : folder + "/";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
