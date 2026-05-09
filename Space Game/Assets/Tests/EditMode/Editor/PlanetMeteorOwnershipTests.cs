using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    // Meteors live in exactly one place: Planet_VioletGiant.unity. Nothing in code
    // selects the scene by tier - the MeteorShower MonoBehaviour is hand-authored per
    // scene. The pitfall is template-copying: forking Violet Giant to seed a new
    // tier 3 planet drags its MeteorShower along, and that's how Ice Planet ended up
    // with a meteor shower it shouldn't have. This test catches that copy-paste class
    // of bug at PR time so we don't have to re-hunt it in playtest.
    public class PlanetMeteorOwnershipTests
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ScriptIdentifier = "FriendSlop.Hazards.MeteorShower";
        private const string AllowedScene = "Planet_VioletGiant.unity";

        [Test]
        public void OnlyVioletGiantOwnsAMeteorShower()
        {
            Assert.IsTrue(Directory.Exists(ScenesFolder), $"Expected scenes folder at {ScenesFolder}.");

            var offenders = new List<string>();
            var allowedSawShower = false;

            foreach (var path in Directory.EnumerateFiles(ScenesFolder, "Planet_*.unity"))
            {
                var name = Path.GetFileName(path);
                if (!File.ReadAllText(path).Contains(ScriptIdentifier)) continue;

                if (string.Equals(name, AllowedScene, System.StringComparison.OrdinalIgnoreCase))
                {
                    allowedSawShower = true;
                    continue;
                }

                offenders.Add(name);
            }

            Assert.IsTrue(allowedSawShower,
                $"{AllowedScene} should still have a MeteorShower; if you removed it intentionally, update this test.");
            Assert.IsEmpty(offenders,
                "Meteors are only allowed on Violet Giant. These scenes accidentally inherited a MeteorShower " +
                "(probably by being template-copied from Violet Giant - strip the component before saving):\n  - "
                + string.Join("\n  - ", offenders));
        }
    }
}
