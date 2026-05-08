using FriendSlop.Loot;
using NUnit.Framework;

namespace FriendSlop.Tests.EditMode
{
    public class WeaponServerRulesTests
    {
        [TestCase(true, false, true, true, 1, 1f, 1f, true)]
        [TestCase(false, false, true, true, 1, 1f, 1f, false)]
        [TestCase(true, true, true, true, 1, 1f, 1f, false)]
        [TestCase(true, false, false, true, 1, 1f, 1f, false)]
        [TestCase(true, false, true, false, 1, 1f, 1f, false)]
        [TestCase(true, false, true, true, 0, 1f, 1f, false)]
        [TestCase(true, false, true, true, 1, 0.9f, 1f, false)]
        public void CanLaserFire_RequiresHeldLiveShooterActiveRoundAmmoAndCooldown(
            bool shooterExists,
            bool shooterIsDead,
            bool isHeldByShooter,
            bool roundIsActive,
            int ammo,
            float serverTime,
            float nextAllowedFireTime,
            bool expected)
        {
            Assert.AreEqual(expected, WeaponServerRules.CanLaserFire(
                shooterExists,
                shooterIsDead,
                isHeldByShooter,
                roundIsActive,
                ammo,
                serverTime,
                nextAllowedFireTime));
        }

        [TestCase(true, false, true, true, 1f, 1f, true)]
        [TestCase(false, false, true, true, 1f, 1f, false)]
        [TestCase(true, true, true, true, 1f, 1f, false)]
        [TestCase(true, false, false, true, 1f, 1f, false)]
        [TestCase(true, false, true, false, 1f, 1f, false)]
        [TestCase(true, false, true, true, 0.9f, 1f, false)]
        public void CanBoxingGlovesPunch_RequiresHeldLiveAttackerActiveRoundAndCooldown(
            bool attackerExists,
            bool attackerIsDead,
            bool isHeldByAttacker,
            bool roundIsActive,
            float serverTime,
            float nextAllowedPunchTime,
            bool expected)
        {
            Assert.AreEqual(expected, WeaponServerRules.CanBoxingGlovesPunch(
                attackerExists,
                attackerIsDead,
                isHeldByAttacker,
                roundIsActive,
                serverTime,
                nextAllowedPunchTime));
        }
    }
}
