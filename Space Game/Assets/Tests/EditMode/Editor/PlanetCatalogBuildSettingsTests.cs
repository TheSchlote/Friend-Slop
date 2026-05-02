using System.Collections.Generic;
using FriendSlop.Editor;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Catches the "I added a planet scene but forgot to drag it into Build Settings"
    // failure mode at test time. Without this, the runtime additive load can fail and
    // strand the host in a loading or lobby phase waiting for an environment that will
    // never load.
    public class PlanetCatalogBuildSettingsTests
    {
        [Test]
        public void EveryCatalogPlanetWithScene_PointsAtBuildSettingsEntry()
        {
            AssertNoFailures(
                PlanetSceneValidator.ValidateCatalogPlanetScenesInBuildSettings,
                "Planet scene wiring is out of sync with Build Settings");
        }

        [Test]
        public void EverySceneCatalogPlanetEntry_PointsAtBuildSettingsEntry()
        {
            AssertNoFailures(
                PlanetSceneValidator.ValidateSceneCatalogPlanetEntriesInBuildSettings,
                "Scene catalog Planet entries are out of sync with Build Settings");
        }

        [Test]
        public void EveryCatalogPlanetWithScene_HasRoundReadyEnvironment()
        {
            AssertNoFailures(
                PlanetSceneValidator.ValidateCatalogPlanetScenesRoundReady,
                "Planet scenes are not round-ready");
        }

        [Test]
        public void BootstrapScene_HasShipTeleporterToActivePlanet()
        {
            AssertNoFailures(
                PlanetSceneValidator.ValidateBootstrapShipTeleporter,
                "Bootstrap scene teleporter wiring is invalid");
        }

        private static void AssertNoFailures(System.Action<List<string>> validate, string message)
        {
            var failures = new List<string>();
            validate(failures);
            if (failures.Count > 0)
                Assert.Fail(message + ":\n  - " + string.Join("\n  - ", failures));
        }
    }
}
