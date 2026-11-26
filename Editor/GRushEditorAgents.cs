using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace GRushSdk.Editor
{
    /// <summary>
    /// AI エージェント向けの文書（このパッケージの Documentation~/agents）を、プロジェクトの .gamerush/ へ写し、
    /// プロジェクトの AGENTS.md（と、あれば CLAUDE.md）から指す。エージェントはパッケージの置き場を読まないので、
    /// プロジェクトの中に置く。フォルダの名前と目印は install.json にあり、JavaScript と Godot の SDK も同じものを読む。
    /// </summary>
    internal static class GRushEditorAgents
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [MenuItem("GameRush/AI エージェント向けの文書を入れる", false, 60)]
        public static void MenuInstall()
        {
            // using UnityEditor と並べても名前が曖昧にならないよう、名前空間まで書く。
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GRushEditorAgents).Assembly);
            var source = info == null ? null : Path.Combine(info.resolvedPath ?? "", "Documentation~", "agents");
            if (source == null || !File.Exists(Path.Combine(source, "install.json")))
            {
                EditorUtility.DisplayDialog("GameRush", "SDK に同梱の文書が見つかりません。", "閉じる");
                return;
            }
            var project = GRushEditorBuildOutput.ProjectRoot();
            var confirmed = EditorUtility.DisplayDialog(
                "GameRush",
                "AI エージェント向けの GameRush の文書を " + Path.Combine(project, ".gamerush") +
                " に入れ、プロジェクトの AGENTS.md（と、あれば CLAUDE.md）から指します。前に入れた文書は置き換えます。",
                "入れる",
                "やめる"
            );
            if (!confirmed)
            {
                return;
            }
            try
            {
                var report = Install(source, project, GRushEditorPackage.Name + " " + GRushEditorPackage.Version);
                EditorUtility.DisplayDialog("GameRush", report, "閉じる");
            }
            catch (Exception failure)
            {
                EditorUtility.DisplayDialog("GameRush", "文書を入れられませんでした: " + failure.Message, "閉じる");
            }
        }

        public static string Install(string source, string project, string stamp)
        {
            var spec = GRushJson.Parse(File.ReadAllText(Path.Combine(source, "install.json"), Utf8));
            if (spec == null)
            {
                throw new InvalidDataException("install.json を読めません。");
            }
            var dir = spec.Get("dir").AsString(".gamerush");
            var begin = spec.Get("begin").AsString("");
            var end = spec.Get("end").AsString("");

            // 段を足す先を先に全部確かめ、目印が壊れていれば何も書かずに止める。
            var planned = new List<Planned>();
            foreach (var pointer in spec.Get("pointers").Items)
            {
                var name = pointer.Get("file").AsString("");
                var path = Path.Combine(project, name);
                var exists = File.Exists(path);
                if (!exists && !pointer.Get("create").AsBool(false))
                {
                    continue;
                }
                var bom = exists && HasBom(path);
                var before = exists ? File.ReadAllText(path, Utf8) : "";
                var after = Upsert(before, begin, end, pointer.Get("block").AsString(""));
                if (after == null)
                {
                    throw new InvalidDataException(
                        name + " の目印（" + begin + " と " + end + "）が1組そろっていません。目印の行を直すか消してから、もう一度実行してください。"
                    );
                }
                planned.Add(new Planned { Name = name, FilePath = path, Exists = exists, Bom = bom, Before = before, After = after });
            }

            var dest = Path.Combine(project, dir);
            var destDocs = Path.Combine(dest, "docs");
            // 前の版にあって今の版に無いファイルが残らないよう、docs/ は作り直す。
            if (Directory.Exists(destDocs))
            {
                Directory.Delete(destDocs, true);
            }
            Directory.CreateDirectory(destDocs);

            var agents = File.ReadAllText(Path.Combine(source, "AGENTS.md"), Utf8)
                .Replace(spec.Get("versionPlaceholder").AsString(""), stamp);
            File.WriteAllText(Path.Combine(dest, "AGENTS.md"), agents, Utf8);
            var names = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(source, "docs"), "*.md"))
            {
                names.Add(Path.GetFileName(file));
            }
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                File.Copy(Path.Combine(source, "docs", name), Path.Combine(destDocs, name), true);
            }

            var report = new StringBuilder("文書を入れました: " + dest + "（" + (names.Count + 1) + " ファイル）");
            foreach (var entry in planned)
            {
                if (entry.After != entry.Before)
                {
                    // 元の文書に BOM があれば残す。
                    File.WriteAllText(entry.FilePath, entry.After, entry.Bom ? new UTF8Encoding(true) : Utf8);
                }
                report.Append("\n" + entry.Name + ": " + (!entry.Exists ? "作りました" : entry.After == entry.Before ? "変更なし" : "GameRush の段を足しました"));
            }
            return report.ToString();
        }

        /// <summary>
        /// 目印で囲んだ段を入れるか置き換える。目印が無ければ末尾に足す。改行は元の文書に合わせる。
        /// 目印が片方だけ・2組以上・逆順のときは、置き換えると作者の文を消しかねないので null を返す。
        /// </summary>
        public static string Upsert(string content, string begin, string end, string block)
        {
            var eol = content.Contains("\r\n") ? "\r\n" : "\n";
            var section = begin + eol + block.Replace("\n", eol) + eol + end;
            var begins = CountOf(content, begin);
            var ends = CountOf(content, end);
            if (begins != 0 || ends != 0)
            {
                var start = content.IndexOf(begin, StringComparison.Ordinal);
                var stop = content.IndexOf(end, StringComparison.Ordinal);
                if (begins != 1 || ends != 1 || stop < start)
                {
                    return null;
                }
                return content.Substring(0, start) + section + content.Substring(stop + end.Length);
            }
            if (content.Length == 0)
            {
                return section + eol;
            }
            var separator = content.EndsWith(eol, StringComparison.Ordinal) ? eol : eol + eol;
            return content + separator + section + eol;
        }

        private static int CountOf(string text, string marker)
        {
            var count = 0;
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }
            return count;
        }

        private static bool HasBom(string path)
        {
            var head = new byte[3];
            using (var stream = File.OpenRead(path))
            {
                return stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
            }
        }

        private sealed class Planned
        {
            public string Name;
            public string FilePath;
            public bool Exists;
            public bool Bom;
            public string Before;
            public string After;
        }
    }
}
