using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FriendSlop.Tests.EditMode
{
    public class PlanetSceneOrchestratorTests
    {
        [Test]
        public void MissingPlanetScenePath_LogsWarningOnce()
        {
            const string missingScenePath = "Assets/Scenes/DefinitelyMissingPlanetScene.unity";
            var orchestratorObject = new GameObject("Planet Scene Orchestrator Test");
            try
            {
                var orchestrator = orchestratorObject.AddComponent<PlanetSceneOrchestrator>();
                var warnMethod = typeof(PlanetSceneOrchestrator).GetMethod("WarnMissingPlanetSceneIfNeeded",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.IsNotNull(warnMethod, "Expected a testable missing-scene warning path.");
                LogAssert.Expect(LogType.Warning,
                    $"RoundManager: planet scene '{missingScenePath}' is not in Build Settings; skipping additive load. " +
                    "(Run Tools/Friend Slop/Extract Tier 1 Into Scene to author it.)");

                Assert.IsTrue((bool)warnMethod.Invoke(orchestrator, new object[] { missingScenePath }));
                Assert.IsTrue((bool)warnMethod.Invoke(orchestrator, new object[] { missingScenePath }));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(orchestratorObject);
            }
        }
    }
}
