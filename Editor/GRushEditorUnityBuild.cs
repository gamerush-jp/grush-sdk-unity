using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GRushSdk.Editor
{
    internal sealed class GRushCanvasSize
    {
        public int Width;
        public int Height;

        /// <summary>幅か高さが 0 以下なら、基準解像度は無いものとして null を返す。</summary>
        public static GRushCanvasSize From(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }
            return new GRushCanvasSize { Width = width, Height = height };
        }
    }

    internal sealed class GRushUnityRoles
    {
        public string LoaderPath;
        public string FrameworkPath;
        public string CodePath;
        public string DataPath;
        public string SymbolsPath;
    }

    internal sealed class GRushUnityDetection
    {
        public const string NotUnityKind = "not-unity";
        public const string ErrorKind = "error";
        public const string UnityKind = "unity";
        public const string FileMissingCode = "build.unityFileMissing";
        public const string FileDuplicateCode = "build.unityFileDuplicate";

        public string Kind;
        public string ErrorCode;
        public string ErrorRole;
        public GRushUnityRoles Roles;

        public static GRushUnityDetection Error(string code, string role)
        {
            return new GRushUnityDetection
            {
                Kind = ErrorKind,
                ErrorCode = code,
                ErrorRole = role,
            };
        }
    }

    /// <summary>
    /// Unity の WebGL の書き出しを見分け、ロード設定を組み立てる。
    /// </summary>
    internal static class GRushEditorUnityBuild
    {
        private const string LoaderSuffix = ".loader.js";
        private const string TemplateDataPrefix = "TemplateData/";
        private const string StreamingAssetsFolder = "StreamingAssets";
        private const string LoaderRole = "loader";
        private const string FrameworkRole = "framework";
        private const string WasmRole = "wasm";
        private const string DataRole = "data";
        private const string SymbolsRole = "symbols";

        // ロード設定の大きさの見積もりに置く、ゲームの ID の長さ。
        private const int GameIdLength = 36;

        private const RegexOptions HtmlOptions =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;

        private static readonly Regex UnrenderedHtml = new Regex(
            "<!--.*?-->|<script\\b.*?</script\\s*>|<template\\b.*?</template\\s*>",
            HtmlOptions
        );

        private static readonly Regex CanvasTag = new Regex(
            "<canvas\\b((?:\"[^\"]*\"|'[^']*'|[^>\"'])*)>",
            HtmlOptions
        );

        private static readonly Regex HtmlAttribute = new Regex(
            "([^\\s=/\"'>]+)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\"'>]+)))?",
            HtmlOptions
        );

        private static readonly Regex HiddenStyle = new Regex(
            "(?:^|;)\\s*(?:display\\s*:\\s*none|visibility\\s*:\\s*hidden)\\s*(?:!important\\s*)?(?:;|$)",
            HtmlOptions
        );

        private static readonly string[] AmbiguousRoles =
        {
            FrameworkRole,
            WasmRole,
            DataRole,
            SymbolsRole,
        };

        private static readonly string[] RequiredRoles =
        {
            LoaderRole,
            FrameworkRole,
            WasmRole,
            DataRole,
        };

        private static bool MatchesRole(string lower, string baseName)
        {
            return lower.EndsWith(baseName, StringComparison.Ordinal)
                || lower.EndsWith(baseName + ".gz", StringComparison.Ordinal)
                || lower.EndsWith(baseName + ".br", StringComparison.Ordinal)
                || lower.EndsWith(baseName + ".unityweb", StringComparison.Ordinal);
        }

        private static bool IsUnderStreamingAssets(string lower)
        {
            var parts = lower.Split('/');
            for (var index = 0; index < parts.Length - 1; index++)
            {
                if (string.Equals(parts[index], "streamingassets", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string RoleForPath(string path)
        {
            var lower = GRushEditorBuildRules.NormalizeUploadPath(path).ToLowerInvariant();
            if (IsUnderStreamingAssets(lower))
            {
                return null;
            }
            if (lower.EndsWith(LoaderSuffix, StringComparison.Ordinal))
            {
                return LoaderRole;
            }
            if (MatchesRole(lower, ".symbols.json"))
            {
                return SymbolsRole;
            }
            if (MatchesRole(lower, ".framework.js"))
            {
                return FrameworkRole;
            }
            if (MatchesRole(lower, ".wasm"))
            {
                return WasmRole;
            }
            if (MatchesRole(lower, ".data"))
            {
                return DataRole;
            }
            return null;
        }

        private static Dictionary<string, List<string>> CollectRoles(List<string> paths)
        {
            var byRole = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var role = RoleForPath(path);
                if (role == null)
                {
                    continue;
                }
                List<string> list;
                if (!byRole.TryGetValue(role, out list))
                {
                    list = new List<string>();
                    byRole[role] = list;
                }
                list.Add(path);
            }
            return byRole;
        }

        private static List<string> Matches(Dictionary<string, List<string>> byRole, string role)
        {
            List<string> list;
            return byRole.TryGetValue(role, out list) ? list : new List<string>();
        }

        private static List<string> ScopedToLoader(List<string> paths, string loaderPath)
        {
            var normalized = GRushEditorBuildRules.NormalizeUploadPath(loaderPath);
            var slash = normalized.LastIndexOf('/');
            var directory = slash == -1 ? "" : normalized.Substring(0, slash);
            var name = normalized.Substring(slash + 1);
            var baseName = name.Substring(0, name.Length - LoaderSuffix.Length);
            var prefix = directory.Length == 0 ? baseName + "." : directory + "/" + baseName + ".";

            var scoped = new List<string>();
            foreach (var path in paths)
            {
                if (
                    GRushEditorBuildRules
                        .NormalizeUploadPath(path)
                        .StartsWith(prefix, StringComparison.Ordinal)
                )
                {
                    scoped.Add(path);
                }
            }
            return scoped;
        }

        public static GRushUnityDetection Detect(List<string> paths)
        {
            var byRole = CollectRoles(paths);

            var loaders = Matches(byRole, LoaderRole);
            if (loaders.Count == 0)
            {
                return new GRushUnityDetection { Kind = GRushUnityDetection.NotUnityKind };
            }

            if (loaders.Count == 1)
            {
                foreach (var role in AmbiguousRoles)
                {
                    if (Matches(byRole, role).Count > 1)
                    {
                        byRole = CollectRoles(ScopedToLoader(paths, loaders[0]));
                        break;
                    }
                }
            }

            foreach (var role in RequiredRoles)
            {
                var matches = Matches(byRole, role);
                if (matches.Count == 0)
                {
                    return GRushUnityDetection.Error(GRushUnityDetection.FileMissingCode, role);
                }
                if (matches.Count > 1)
                {
                    return GRushUnityDetection.Error(GRushUnityDetection.FileDuplicateCode, role);
                }
            }
            var symbols = Matches(byRole, SymbolsRole);
            if (symbols.Count > 1)
            {
                return GRushUnityDetection.Error(GRushUnityDetection.FileDuplicateCode, SymbolsRole);
            }

            return new GRushUnityDetection
            {
                Kind = GRushUnityDetection.UnityKind,
                Roles = new GRushUnityRoles
                {
                    LoaderPath = Matches(byRole, LoaderRole)[0],
                    FrameworkPath = Matches(byRole, FrameworkRole)[0],
                    CodePath = Matches(byRole, WasmRole)[0],
                    DataPath = Matches(byRole, DataRole)[0],
                    SymbolsPath = symbols.Count == 1 ? symbols[0] : null,
                },
            };
        }

        public static string DeriveStreamingAssetsUrl(string loaderPath)
        {
            var segments = GRushEditorBuildRules.NormalizeUploadPath(loaderPath).Split('/');
            var parts = new List<string>();
            for (var index = 0; index < segments.Length - 2; index++)
            {
                parts.Add(segments[index]);
            }
            parts.Add(StreamingAssetsFolder);
            return string.Join("/", parts);
        }

        /// <summary>ロード設定の JSON を組む。基準解像度が無いときも <c>canvasSize</c> のキーは出す。</summary>
        public static string SynthesizePlayerConfig(
            GRushUnityRoles roles,
            string productName,
            GRushCanvasSize canvasSize
        )
        {
            var builder = new StringBuilder("{\"kind\":\"unity-modern\"");
            AppendText(builder, "loaderPath", roles.LoaderPath);
            AppendText(builder, "dataUrl", roles.DataPath);
            AppendText(builder, "frameworkUrl", roles.FrameworkPath);
            AppendText(builder, "codeUrl", roles.CodePath);
            if (!string.IsNullOrEmpty(roles.SymbolsPath))
            {
                AppendText(builder, "symbolsUrl", roles.SymbolsPath);
            }
            AppendText(builder, "streamingAssetsUrl", DeriveStreamingAssetsUrl(roles.LoaderPath));
            AppendText(builder, "companyName", "GameRush");
            AppendText(builder, "productName", productName);
            AppendText(builder, "productVersion", "1.0");
            builder.Append(",\"canvasSize\":");
            if (canvasSize == null)
            {
                builder.Append("null");
            }
            else
            {
                builder
                    .Append("{\"width\":")
                    .Append(canvasSize.Width.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"height\":")
                    .Append(canvasSize.Height.ToString(CultureInfo.InvariantCulture))
                    .Append('}');
            }
            return builder.Append('}').ToString();
        }

        /// <summary>
        /// ゲームが決まる前に上限を確かめるための、ロード設定の大きさの見積もり。ゲームの ID と
        /// 基準解像度を最大の桁で置いて組むので、実際の大きさを下回らない。
        /// </summary>
        public static long EstimatePlayerConfigBytes(GRushUnityRoles roles)
        {
            var largest = new GRushCanvasSize { Width = int.MaxValue, Height = int.MaxValue };
            var config = SynthesizePlayerConfig(roles, new string('0', GameIdLength), largest);
            return Encoding.UTF8.GetByteCount(config);
        }

        /// <summary>
        /// 書き出した HTML の、表示される最初の canvas の <c>width</c> と <c>height</c> を読む。
        /// どちらも正の整数の canvas が無ければ null を返す。
        /// </summary>
        public static GRushCanvasSize ReadCanvasSize(string html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return null;
            }
            foreach (Match tag in CanvasTag.Matches(UnrenderedHtml.Replace(html, " ")))
            {
                var attributes = ReadAttributes(tag.Groups[1].Value);
                if (IsHidden(attributes))
                {
                    continue;
                }
                var size = GRushCanvasSize.From(
                    PositiveInteger(attributes, "width"),
                    PositiveInteger(attributes, "height")
                );
                if (size != null)
                {
                    return size;
                }
            }
            return null;
        }

        private static Dictionary<string, string> ReadAttributes(string text)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match attribute in HtmlAttribute.Matches(text))
            {
                var name = attribute.Groups[1].Value.ToLowerInvariant();
                if (attributes.ContainsKey(name))
                {
                    continue;
                }
                attributes[name] =
                    attribute.Groups[2].Value + attribute.Groups[3].Value + attribute.Groups[4].Value;
            }
            return attributes;
        }

        private static bool IsHidden(Dictionary<string, string> attributes)
        {
            string style;
            return attributes.ContainsKey("hidden")
                || (attributes.TryGetValue("style", out style) && HiddenStyle.IsMatch(style));
        }

        private static int PositiveInteger(Dictionary<string, string> attributes, string name)
        {
            string value;
            int parsed;
            return
                attributes.TryGetValue(name, out value)
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }

        private static void AppendText(StringBuilder builder, string key, string value)
        {
            builder
                .Append(",\"")
                .Append(key)
                .Append("\":")
                .Append(GRushJsonText.Escape(value));
        }

        public static bool RolesUseUnityweb(GRushUnityRoles roles)
        {
            var paths = new[]
            {
                roles.LoaderPath,
                roles.FrameworkPath,
                roles.CodePath,
                roles.DataPath,
                roles.SymbolsPath,
            };
            foreach (var path in paths)
            {
                if (
                    path != null
                    && path.ToLowerInvariant().EndsWith(".unityweb", StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsTemplateEntry(string path)
        {
            return string.Equals(path, GRushEditorBuildRules.EntryPath, StringComparison.Ordinal)
                || path.StartsWith(TemplateDataPrefix, StringComparison.Ordinal);
        }

        public static bool IsCompressedHtml(string path)
        {
            var parts = path.Split('/');
            var name = parts[parts.Length - 1].ToLowerInvariant();
            if (
                !name.EndsWith(".gz", StringComparison.Ordinal)
                && !name.EndsWith(".br", StringComparison.Ordinal)
            )
            {
                return false;
            }
            return name.Substring(0, name.Length - 3).EndsWith(".html", StringComparison.Ordinal);
        }
    }
}
