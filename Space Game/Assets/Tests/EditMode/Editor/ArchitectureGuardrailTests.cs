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
            // Interiors branch additions — pending split into partial classes.
            { "Assets/Scripts/Interiors/Blueprints/BlueprintEditorController.cs", 545 },
            { "Assets/Scripts/Interiors/Blueprints/BlueprintEditorUI.cs", 1005 },
            { "Assets/Scripts/Interiors/InteriorLayoutGenerator.cs", 1727 },
            { "Assets/Scripts/Interiors/InteriorSceneBootstrapper.cs", 832 },
            { "Assets/Scripts/Interiors/InteriorSceneBootstrapper.Furniture.cs", 527 },
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
        public void AsmdefReferencesEnforceLayeredDirection()
        {
            // Target graph (D-006):
            //   Core <- {Networking, SceneManagement} <- Gameplay <- UI <- Editor
            // Networking and SceneManagement are two independently-clean infra leaves above
            // Core with no mutual edge. Both must remain free of Gameplay/UI/Editor refs so
            // the boundary stays the swap surface for the eventual Steamworks migration.

            var coreRefs = ReadAsmdefReferences("Assets/Scripts/Core/Foundation/FriendSlop.Core.asmdef");
            Assert.IsFalse(coreRefs.Contains("FriendSlop.Networking"), "Core assembly must not reference Networking.");
            Assert.IsFalse(coreRefs.Contains("FriendSlop.SceneManagement"), "Core assembly must not reference SceneManagement.");
            Assert.IsFalse(coreRefs.Contains("FriendSlop.Gameplay"), "Core assembly must not reference Gameplay.");
            Assert.IsFalse(coreRefs.Contains("FriendSlop.UI"), "Core assembly must not reference UI.");
            Assert.IsFalse(coreRefs.Contains("FriendSlop.Editor"), "Core assembly must not reference Editor.");
            Assert.IsFalse(coreRefs.Contains("Unity.Netcode.Runtime"), "Core assembly must not reference Netcode.");

            var sceneManagementRefs = ReadAsmdefReferences("Assets/Scripts/SceneManagement/FriendSlop.SceneManagement.asmdef");
            Assert.IsTrue(sceneManagementRefs.Contains("FriendSlop.Core"), "SceneManagement assembly should depend on Core.");
            Assert.IsFalse(sceneManagementRefs.Contains("FriendSlop.Networking"), "SceneManagement assembly must not reference Networking (no mutual infra edge).");
            Assert.IsFalse(sceneManagementRefs.Contains("FriendSlop.Gameplay"), "SceneManagement assembly must not reference Gameplay.");
            Assert.IsFalse(sceneManagementRefs.Contains("FriendSlop.UI"), "SceneManagement assembly must not reference UI.");
            Assert.IsFalse(sceneManagementRefs.Contains("FriendSlop.Editor"), "SceneManagement assembly must not reference Editor.");

            var networkingRefs = ReadAsmdefReferences("Assets/Scripts/Networking/FriendSlop.Networking.asmdef");
            Assert.IsTrue(networkingRefs.Contains("FriendSlop.Core"), "Networking assembly should depend on Core.");
            Assert.IsFalse(networkingRefs.Contains("FriendSlop.SceneManagement"), "Networking assembly must not reference SceneManagement (no mutual infra edge).");
            Assert.IsFalse(networkingRefs.Contains("FriendSlop.Gameplay"), "Networking assembly must not reference Gameplay (this is the Steam swap surface).");
            Assert.IsFalse(networkingRefs.Contains("FriendSlop.UI"), "Networking assembly must not reference UI.");
            Assert.IsFalse(networkingRefs.Contains("FriendSlop.Editor"), "Networking assembly must not reference Editor.");

            var gameplayRefs = ReadAsmdefReferences("Assets/Scripts/FriendSlop.Gameplay.asmdef");
            Assert.IsTrue(gameplayRefs.Contains("FriendSlop.Core"), "Gameplay assembly should depend on Core utilities.");
            Assert.IsTrue(gameplayRefs.Contains("FriendSlop.Networking"), "Gameplay assembly should depend on Networking infra.");
            Assert.IsTrue(gameplayRefs.Contains("FriendSlop.SceneManagement"), "Gameplay assembly should depend on SceneManagement infra.");
            Assert.IsFalse(gameplayRefs.Contains("FriendSlop.UI"), "Gameplay assembly must not reference UI.");
            Assert.IsFalse(gameplayRefs.Contains("FriendSlop.Editor"), "Gameplay assembly must not reference Editor.");

            var uiRefs = ReadAsmdefReferences("Assets/Scripts/UI/FriendSlop.UI.asmdef");
            Assert.IsTrue(uiRefs.Contains("FriendSlop.Core"), "UI assembly should reference Core directly for utility types.");
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
