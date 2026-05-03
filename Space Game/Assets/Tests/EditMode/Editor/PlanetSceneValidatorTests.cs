using FriendSlop.Editor;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    public class PlanetSceneValidatorTests
    {
        [Test]
        public void CurrentPlanetScenes_PassValidation()
        {
            Assert.IsTrue(
                PlanetSceneValidator.TryValidate(out var failures),
                "Planet scene validation failed:\n  - " + string.Join("\n  - ", failures));
        }
    }
}
