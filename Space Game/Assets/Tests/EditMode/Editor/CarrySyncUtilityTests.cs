using FriendSlop.Player;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class CarrySyncUtilityTests
    {
        [Test]
        public void ShouldSendPose_ReturnsTrue_ForFirstPose()
        {
            var shouldSend = CarrySyncUtility.ShouldSendPose(
                false,
                0.1f,
                1f,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one,
                Quaternion.Euler(0f, 45f, 0f),
                0.02f,
                1.5f);

            Assert.IsTrue(shouldSend);
        }

        [Test]
        public void ShouldSendPose_ReturnsFalse_BeforeSyncIntervalEvenWhenPoseChanged()
        {
            var shouldSend = CarrySyncUtility.ShouldSendPose(
                true,
                0.1f,
                0.2f,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.5f, 0f, 0f),
                Quaternion.Euler(0f, 30f, 0f),
                0.02f,
                1.5f);

            Assert.IsFalse(shouldSend);
        }

        [Test]
        public void ShouldSendPose_ReturnsTrue_AfterSyncIntervalWhenPositionChangedEnough()
        {
            var shouldSend = CarrySyncUtility.ShouldSendPose(
                true,
                0.3f,
                0.2f,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.05f, 0f, 0f),
                Quaternion.identity,
                0.02f,
                1.5f);

            Assert.IsTrue(shouldSend);
        }

        [Test]
        public void ShouldSendPose_ReturnsTrue_AfterSyncIntervalWhenRotationChangedEnough()
        {
            var shouldSend = CarrySyncUtility.ShouldSendPose(
                true,
                0.3f,
                0.2f,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Quaternion.Euler(0f, 4f, 0f),
                0.02f,
                1.5f);

            Assert.IsTrue(shouldSend);
        }

        [Test]
        public void ShouldSendPose_ReturnsFalse_WhenPoseStaysWithinThresholds()
        {
            var shouldSend = CarrySyncUtility.ShouldSendPose(
                true,
                0.3f,
                0.2f,
                Vector3.zero,
                Quaternion.identity,
                new Vector3(0.005f, 0f, 0f),
                Quaternion.Euler(0f, 0.5f, 0f),
                0.02f,
                1.5f);

            Assert.IsFalse(shouldSend);
        }
    }
}
