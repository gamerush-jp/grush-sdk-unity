using System.Collections.Generic;
using System.Text;

namespace GRushSdk.Editor
{
    /// <summary>
    /// アップロードできるビルドの規則。GameRush が受け付ける規則に合わせて、送る前に手元で確かめる。
    /// </summary>
    internal static class GRushEditorBuildRules
    {
        public const long MaxBuildBytes = 50L * 1024 * 1024;
        public const int MaxBuildFileCount = 2000;
        public const string GeneratedPlayerConfigPath = "__grush-player.json";
        public const string BuildCanvasSizePath = "__grush-canvas.json";
        public const string EntryPath = "index.html";

        /// <summary>
        /// 1つのパスの UTF-8 のバイト数の上限。超えるパスは保存先のキーの長さを超えて置けない。
        /// </summary>
        public const int MaxBuildPathBytes = 512;

        private static readonly HashSet<string> BuildExtensions = new HashSet<string>
        {
            "html",
            "js",
            "wasm",
            "data",
            "json",
            "png",
            "jpg",
            "jpeg",
            "gif",
            "bmp",
            "css",
            "unityweb",
            "pck",
            "ico",
            "svg",
            "webp",
            "mp3",
            "m4a",
            "aac",
            "ogg",
            "wav",
            "mp4",
            "txt",
            "csv",
            "xml",
            "yml",
            "yaml",
            "ttf",
            "woff",
            "woff2",
            "webmanifest",
            "bytes",
            "bundle",
            "assetbundle",
            "bin",
            "dat",
            "bank",
            "ress",
            "resource",
        };

        private static readonly HashSet<string> CompressedExtensions = new HashSet<string>
        {
            "br",
            "gz",
        };

        public static string NormalizeUploadPath(string path)
        {
            var normalized = (path ?? string.Empty).Replace("\\", "/");
            return normalized.TrimStart('/').Trim();
        }

        public static bool IsIgnoredEntry(string path)
        {
            var normalized = NormalizeUploadPath(path);
            if (normalized.Length == 0 || normalized.StartsWith("__MACOSX/"))
            {
                return true;
            }
            var parts = normalized.Split('/');
            foreach (var part in parts)
            {
                if (part == "__MACOSX")
                {
                    return true;
                }
            }
            var name = parts[parts.Length - 1];
            return name == ".DS_Store" || name == "Thumbs.db";
        }

        /// <summary>
        /// 末尾の <c>.br</c> / <c>.gz</c> は圧縮の印なので1段はがして判定する。
        /// <c>Build/game.wasm.br</c> の拡張子は <c>wasm</c>。
        /// </summary>
        public static string ExtensionForPath(string path)
        {
            var parts = NormalizeUploadPath(path).Split('/');
            var name = parts[parts.Length - 1].ToLowerInvariant();
            if (!name.Contains("."))
            {
                return null;
            }
            var segments = new List<string>(name.Split('.'));
            var extension = Pop(segments);
            if (extension != null && CompressedExtensions.Contains(extension))
            {
                extension = Pop(segments);
            }
            return extension;
        }

        private static string Pop(List<string> segments)
        {
            if (segments.Count == 0)
            {
                return null;
            }
            var last = segments[segments.Count - 1];
            segments.RemoveAt(segments.Count - 1);
            return last;
        }

        public static string ValidateBuildPath(string path)
        {
            var normalized = NormalizeUploadPath(path);
            if (normalized.Length == 0)
            {
                return "パスが空のファイルがあります。";
            }
            if (normalized.StartsWith("/") || normalized.Contains("\\"))
            {
                return normalized + ": パスの区切りは / だけです。";
            }
            foreach (var part in normalized.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == "..")
                {
                    return normalized + ": 使えないパスです。";
                }
            }
            if (HasUnservableCharacter(normalized))
            {
                return normalized + ": パスに使えない文字（制御文字・? # % < > : など）があります。";
            }
            if (Encoding.UTF8.GetByteCount(normalized) > MaxBuildPathBytes)
            {
                return normalized + ": パスが長すぎます（UTF-8 で " + MaxBuildPathBytes + " バイトまで）。";
            }
            var extension = ExtensionForPath(normalized);
            if (extension == null || !BuildExtensions.Contains(extension))
            {
                return normalized + ": この拡張子はアップロードできません。";
            }
            return null;
        }

        // ? と # は URL のパスを切り、% は配信で復号されて別のファイルを指し、制御文字は URL の解決で消えるので、
        // 置けても配信で見つからない。< > : U+2028 U+2029 は正当な用途が無いので合わせて断る。
        private static bool HasUnservableCharacter(string path)
        {
            foreach (var c in path)
            {
                if (c < 0x20 || (c >= 0x7f && c <= 0x9f) || c == '\u2028' || c == '\u2029'
                    || c == '?' || c == '#' || c == '%' || c == '<' || c == '>' || c == ':')
                {
                    return true;
                }
            }
            return false;
        }
    }
}
