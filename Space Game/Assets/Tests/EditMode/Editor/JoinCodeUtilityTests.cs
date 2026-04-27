using System;
using FriendSlop.Networking;
using NUnit.Framework;
using Unity.Services.Relay;

namespace FriendSlop.Tests.EditMode
{
    public class JoinCodeUtilityTests
    {
        [TestCase("abc123", "ABC123")]
        [TestCase(" code: abc123 ", "ABC123")]
        [TestCase("join code: a1b2c3", "A1B2C3")]
        [TestCase("abc 123", "ABC123")]
        [TestCase("abc-123", "ABC123")]
        public void NormalizeJoinTarget_ReturnsUppercaseRelayCode(string input, string expected)
        {
            Assert.AreEqual(expected, JoinCodeUtility.NormalizeJoinTarget(input));
        }

        [TestCase("LAN: 192.168.1.20", "192.168.1.20")]
        [TestCase("localhost", "localhost")]
        public void NormalizeJoinTarget_PreservesLanAddress(string input, string expected)
        {
            Assert.AreEqual(expected, JoinCodeUtility.NormalizeJoinTarget(input));
        }

        [TestCase("ABC123", true)]
        [TestCase("A1B2C3D4E5F6", true)]
        [TestCase("ABCDE", false)]
        [TestCase("ABCDEFGHIJKLM", false)]
        [TestCase("ABC-123", false)]
        [TestCase("", false)]
        public void IsValidRelayJoinCode_EnforcesLengthAndCharacters(string code, bool expected)
        {
            Assert.AreEqual(expected, JoinCodeUtility.IsValidRelayJoinCode(code));
        }

        [Test]
        public void GetFriendlyJoinFailure_MapsMissingCodeToPlayerMessage()
        {
            var exception = new RelayServiceException(RelayExceptionReason.JoinCodeNotFound, "Not Found: join code not found");

            var message = JoinCodeUtility.GetFriendlyJoinFailure(exception, "ABC123");

            Assert.AreEqual("No open game found for code ABC123. Check the code and try again.", message);
        }

        [Test]
        public void GetFriendlyJoinFailure_HidesUnexpectedExceptionDetails()
        {
            var exception = new Exception("raw service schema response");

            var message = JoinCodeUtility.GetFriendlyJoinFailure(exception, "ABC123");

            Assert.AreEqual("Could not join code ABC123. Check the code or ask the host for a fresh one.", message);
        }

        [Test]
        public void GetFriendlyJoinFailure_MapsTimeoutToRetryMessage()
        {
            var exception = new TimeoutException("Relay join timed out.");

            var message = JoinCodeUtility.GetFriendlyJoinFailure(exception, "ABC123");

            Assert.AreEqual("Timed out contacting Relay. Check your internet connection and try again.", message);
        }
    }
}
