using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class ArchitectureGuardrailTests
    {
        private const int DefaultRuntimeFileLineLimit = 400;

        private static readonly Dictionary<string, int> ExistingOversizedRuntimeFiles = new()
        {
            ["Assets/Scripts/Round/RoundManager.cs"] = 1000,
            ["Assets/Scripts/Player/NetworkFirstPersonController.cs"] = 739,
            ["Assets/Scripts/Loot/NetworkLootItem.cs"] = 620,
            ["Assets/Scripts/Networking/NetworkSessionManager.cs"] = 621,
            ["Assets/Scripts/Player/PlayerInteractor.cs"] = 529,
            ["Assets/Scripts/UI/FriendSlopUI.BuildUi.cs"] = 517,
            ["Assets/Scripts/UI/FriendSlopUI.cs"] = 477,
        };

        private static readonly HashSet<string> AllowedSingletons = new()
        {
            "Assets/Scripts/Round/RoundManager.cs::Instance",
            "Assets/Scripts/Player/NetworkFirstPersonController.cs::LocalPlayer",
        };

        private static readonly string[] BannedPerFrameSearchCalls =
        {
            "FindObjectsByType",
            "FindFirstObjectByType",
            "FindObjectOfType",
        };

        private static readonly Regex SingletonDeclarationPattern = new(
            @"\b(?:public|internal|private|protected)\s+static\s+(?!event\b)[A-Za-z0-9_<>,\.\[\]\s]+\s+(?<name>Instance|LocalPlayer)\b",
            RegexOptions.Compiled);

        private static readonly Regex FrameMethodPattern = new(
            @"\b(?:public|internal|private|protected)?\s*(?:async\s+)?void\s+(?<name>Update|FixedUpdate|LateUpdate)\s*\(\s*\)",
            RegexOptions.Compiled);

        [Test]
        public void NoNewSingletonStyleGlobals()
        {
            var actual = new HashSet<string>();

            foreach (var path in RuntimeScriptPaths())
            {
                var text = File.ReadAllText(path);
                var relative = ToProjectPath(path);
                foreach (Match match in SingletonDeclarationPattern.Matches(text))
                {
                    actual.Add($"{relative}::{match.Groups["name"].Value}");
                }
            }

            var unexpected = actual.Except(AllowedSingletons).OrderBy(item => item).ToArray();
            Assert.IsEmpty(unexpected,
                "New singleton-style globals need an explicit architecture review. Unexpected declarations:\n  - "
                + string.Join("\n  - ", unexpected));
        }

        [Test]
        public void RuntimeFilesStayUnderLineLimitOrDoNotGrowPastBaseline()
        {
            var failures = new List<string>();

            foreach (var path in RuntimeScriptPaths())
            {
                var relative = ToProjectPath(path);
                var actualLines = File.ReadLines(path).Count();
                var limit = ExistingOversizedRuntimeFiles.TryGetValue(relative, out var baseline)
                    ? baseline
                    : DefaultRuntimeFileLineLimit;

                if (actualLines > limit)
                {
                    var reason = baseline > 0
                        ? $"existing oversized baseline is {baseline}; split before adding more"
                        : $"limit is {DefaultRuntimeFileLineLimit}";
                    failures.Add($"{relative}: {actualLines} lines ({reason}).");
                }
            }

            Assert.IsEmpty(failures,
                "Runtime files must stay small enough for feature agents to reason about:\n  - "
                + string.Join("\n  - ", failures));
        }

        [Test]
        public void FrameMethodsDoNotSearchSceneObjects()
        {
            var failures = new List<string>();

            foreach (var path in RuntimeScriptPaths())
            {
                var source = StripComments(File.ReadAllText(path));
                var relative = ToProjectPath(path);

                foreach (Match match in FrameMethodPattern.Matches(source))
                {
                    var methodBody = TryReadMethodBody(source, match.Index);
                    if (methodBody == null) continue;

                    for (var i = 0; i < BannedPerFrameSearchCalls.Length; i++)
                    {
                        var call = BannedPerFrameSearchCalls[i];
                        if (!methodBody.Contains(call)) continue;

                        var line = CountLinesBefore(source, match.Index);
                        failures.Add($"{relative}:{line} {match.Groups["name"].Value} contains {call}; cache or use a registry.");
                    }
                }
            }

            Assert.IsEmpty(failures,
                "Per-frame scene searches are not allowed in runtime code:\n  - "
                + string.Join("\n  - ", failures));
        }

        [Test]
        public void AsmdefReferencesKeepRuntimeAndUiDirectionClean()
        {
            var runtimeRefs = ReadAsmdefReferences("Assets/Scripts/FriendSlop.Runtime.asmdef");
            Assert.IsFalse(runtimeRefs.Contains("FriendSlop.UI"), "Runtime assembly must not reference UI.");
            Assert.IsFalse(runtimeRefs.Contains("FriendSlop.Editor"), "Runtime assembly must not reference Editor.");

            var uiRefs = ReadAsmdefReferences("Assets/Scripts/UI/FriendSlop.UI.asmdef");
            Assert.IsFalse(uiRefs.Contains("FriendSlop.Editor"), "UI assembly must not reference Editor.");
        }

        [Test]
        public void EditorAssemblyRemainsEditorOnly()
        {
            var editorAsmdef = File.ReadAllText(ProjectPath("Assets/Scripts/Editor/FriendSlop.Editor.asmdef"));
            StringAssert.Contains("\"includePlatforms\"", editorAsmdef);
            StringAssert.Contains("\"Editor\"", editorAsmdef);
        }

        private static IEnumerable<string> RuntimeScriptPaths()
        {
            var scriptsRoot = ProjectPath("Assets/Scripts");
            return Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
                .Select(NormalizePath)
                .Where(path => !path.Contains("/Editor/"))
                .OrderBy(path => path);
        }

        private static HashSet<string> ReadAsmdefReferences(string relativePath)
        {
            var text = File.ReadAllText(ProjectPath(relativePath));
            var references = Regex.Match(text, @"""references""\s*:\s*\[(?<refs>.*?)\]", RegexOptions.Singleline);
            if (!references.Success) return new HashSet<string>();

            return Regex.Matches(references.Groups["refs"].Value, @"""(?<ref>[^""]+)""")
                .Cast<Match>()
                .Select(match => match.Groups["ref"].Value)
                .ToHashSet();
        }

        private static string TryReadMethodBody(string source, int methodIndex)
        {
            var openBrace = source.IndexOf('{', methodIndex);
            if (openBrace < 0) return null;

            var depth = 0;
            for (var i = openBrace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;

                if (depth == 0)
                    return source.Substring(openBrace, i - openBrace + 1);
            }

            return null;
        }

        private static string StripComments(string source)
        {
            source = Regex.Replace(source, @"/\*.*?\*/",
                match => new string('\n', match.Value.Count(c => c == '\n')),
                RegexOptions.Singleline);
            return Regex.Replace(source, @"//.*", string.Empty);
        }

        private static int CountLinesBefore(string source, int index)
        {
            var line = 1;
            for (var i = 0; i < index && i < source.Length; i++)
            {
                if (source[i] == '\n') line++;
            }
            return line;
        }

        private static string ProjectPath(string relativePath)
        {
            return NormalizePath(Path.Combine(ProjectRoot, relativePath));
        }

        private static string ToProjectPath(string absolutePath)
        {
            var normalized = NormalizePath(absolutePath);
            var root = ProjectRoot + "/";
            return normalized.StartsWith(root)
                ? normalized.Substring(root.Length)
                : normalized;
        }

        private static string ProjectRoot => NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
