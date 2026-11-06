using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GRushSdk.Editor
{
    internal sealed class GRushBuildFile
    {
        public string Path;
        public string FullPath;
        public long Size;

        // ディスクに実体が無いファイルの中身。あれば FullPath の代わりにこれを送る。
        public byte[] Content;
    }

    internal sealed class GRushBuildManifest
    {
        public List<GRushBuildFile> Files = new List<GRushBuildFile>();
        public long TotalBytes;
        public string Error;
        public GRushUnityRoles Roles;
        public bool UsesDecompressionFallback;

        // 書き出した index.html から読んだ基準解像度。読めなければ null。
        public GRushCanvasSize CanvasSize;

        // ゲームが決まると足すロード設定まで含めた、送るファイルの数と合計の見積もり。
        public int UploadFileCount;
        public long UploadBytes;

        public bool Ok => Error == null;

        /// <summary>
        /// エンジンと、ファイルごとの <c>path</c> と <c>size</c> だけを送る。<c>encoding</c> は付けない。
        /// Unity が書き出した <c>Build/x.wasm.br</c> の圧縮の形式は、ファイル名の末尾が表す。
        /// </summary>
        public string ToRequestBody()
        {
            var builder = new StringBuilder("{\"engine\":\"unity\",\"files\":[");
            for (var index = 0; index < Files.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }
                builder.Append("{\"path\":").Append(GRushJsonText.Escape(Files[index].Path));
                builder.Append(",\"size\":").Append(Files[index].Size).Append('}');
            }
            return builder.Append("]}").ToString();
        }

        /// <summary>
        /// ロード設定を先頭に足した、送る内容の manifest を返す。製品名にはゲームの ID を渡すので、
        /// 送り先のゲームが決まってから組む。上限に掛かれば <see cref="Error"/> を立てて返す。
        /// </summary>
        public GRushBuildManifest ForGame(string gameId, GRushCanvasSize canvasSize)
        {
            var content = Encoding.UTF8.GetBytes(
                GRushEditorUnityBuild.SynthesizePlayerConfig(Roles, gameId, canvasSize)
            );
            var prepared = new GRushBuildManifest
            {
                Roles = Roles,
                UsesDecompressionFallback = UsesDecompressionFallback,
                CanvasSize = CanvasSize,
                TotalBytes = TotalBytes + content.Length,
            };
            prepared.Files.Add(
                new GRushBuildFile
                {
                    Path = GRushEditorBuildRules.GeneratedPlayerConfigPath,
                    Size = content.Length,
                    Content = content,
                }
            );
            prepared.Files.AddRange(Files);
            prepared.UploadFileCount = prepared.Files.Count;
            prepared.UploadBytes = prepared.TotalBytes;
            prepared.Error = GRushEditorBuildManifest.Validate(prepared);
            return prepared;
        }
    }

    internal static class GRushEditorBuildManifest
    {
        public static GRushBuildManifest Collect(string rootDirectory)
        {
            var manifest = new GRushBuildManifest();
            if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                manifest.Error = "ビルド出力ディレクトリが見つかりません。";
                return manifest;
            }

            var root = Path.GetFullPath(rootDirectory).TrimEnd('/', '\\');
            var fullPaths = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var fullPath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = GRushEditorBuildRules.NormalizeUploadPath(
                    Path.GetFullPath(fullPath).Substring(root.Length).TrimStart('/', '\\')
                );
                if (GRushEditorBuildRules.IsIgnoredEntry(relative))
                {
                    continue;
                }
                if (fullPaths.ContainsKey(relative))
                {
                    manifest.Error = relative + ": 同じパスが2つあります。";
                    return manifest;
                }
                fullPaths[relative] = fullPath;
            }

            var paths = new List<string>(fullPaths.Keys);
            var detection = GRushEditorUnityBuild.Detect(paths);
            manifest.Error = DetectionError(detection, root);
            if (manifest.Error != null)
            {
                return manifest;
            }
            if (fullPaths.ContainsKey(GRushEditorBuildRules.GeneratedPlayerConfigPath))
            {
                manifest.Error =
                    GRushEditorBuildRules.GeneratedPlayerConfigPath
                    + " は GameRush が作るファイルです。出力先から消してください。";
                return manifest;
            }
            manifest.Roles = detection.Roles;
            manifest.UsesDecompressionFallback = GRushEditorUnityBuild.RolesUseUnityweb(
                detection.Roles
            );
            manifest.CanvasSize = ReadCanvasSize(fullPaths);

            foreach (var path in paths)
            {
                if (IsReplacedByGeneratedEntry(path))
                {
                    continue;
                }
                var pathError = GRushEditorBuildRules.ValidateBuildPath(path);
                if (pathError != null)
                {
                    manifest.Error = pathError;
                    return manifest;
                }

                var size = new FileInfo(fullPaths[path]).Length;
                manifest.TotalBytes += size;
                manifest.Files.Add(
                    new GRushBuildFile
                    {
                        Path = path,
                        FullPath = fullPaths[path],
                        Size = size,
                    }
                );
            }

            manifest.UploadFileCount = manifest.Files.Count + 1;
            manifest.UploadBytes =
                manifest.TotalBytes + GRushEditorUnityBuild.EstimatePlayerConfigBytes(detection.Roles);
            manifest.Error = Validate(manifest);
            return manifest;
        }

        private static GRushCanvasSize ReadCanvasSize(Dictionary<string, string> fullPaths)
        {
            string entry;
            if (!fullPaths.TryGetValue(GRushEditorBuildRules.EntryPath, out entry))
            {
                return null;
            }
            try
            {
                return GRushEditorUnityBuild.ReadCanvasSize(File.ReadAllText(entry));
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool IsReplacedByGeneratedEntry(string path)
        {
            return GRushEditorUnityBuild.IsTemplateEntry(path)
                || GRushEditorUnityBuild.IsCompressedHtml(path)
                || path == GRushEditorBuildRules.BuildCanvasSizePath;
        }

        /// <summary>
        /// 書き出し先の直下の1階層下にローダーが無いなら、選ぶべきフォルダを名指しで返す。
        /// パスを黙って書き換えると、送ったものと手元の中身が食い違ったまま気づけなくなる。
        /// </summary>
        private static string DetectionError(GRushUnityDetection detection, string root)
        {
            if (detection.Kind == GRushUnityDetection.NotUnityKind)
            {
                return "Unity のローダー（.loader.js）が見つかりません。WebGL の書き出し先ディレクトリを選んでください。";
            }
            if (detection.Kind == GRushUnityDetection.ErrorKind)
            {
                return detection.ErrorCode == GRushUnityDetection.FileMissingCode
                    ? detection.ErrorRole + " のファイルがありません。"
                    : detection.ErrorRole + " が複数あります。";
            }
            var parts = detection.Roles.LoaderPath.Split('/');
            if (parts.Length == 2)
            {
                return null;
            }
            var expected = OutputRootFor(root, parts);
            return expected == null
                ? "ローダーの場所が WebGL の書き出しの形と違います。WebGL の書き出し先ディレクトリを選んでください。"
                : "ローダーの場所が WebGL の書き出しの形と違います。" + expected + " を選んでください。";
        }

        private static string OutputRootFor(string root, string[] loaderParts)
        {
            if (loaderParts.Length == 1)
            {
                return Path.GetDirectoryName(root);
            }
            var expected = root;
            for (var index = 0; index < loaderParts.Length - 2; index++)
            {
                expected = Path.Combine(expected, loaderParts[index]);
            }
            return expected;
        }

        internal static string Validate(GRushBuildManifest manifest)
        {
            if (manifest.Files.Count == 0)
            {
                return "アップロードできるファイルがありません。";
            }
            if (manifest.UploadFileCount > GRushEditorBuildRules.MaxBuildFileCount)
            {
                return "ファイルが多すぎます（上限 "
                    + GRushEditorBuildRules.MaxBuildFileCount
                    + " 個、いまは "
                    + manifest.UploadFileCount
                    + " 個）。";
            }
            if (manifest.UploadBytes > GRushEditorBuildRules.MaxBuildBytes)
            {
                return "ビルドが大きすぎます（上限 "
                    + GRushEditorBuildRules.MaxBuildBytes / (1024 * 1024)
                    + " MB、いまは "
                    + manifest.UploadBytes / (1024 * 1024)
                    + " MB）。";
            }
            return null;
        }
    }
}
