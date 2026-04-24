using System.Collections;
using FriendSlop.Networking;
using FriendSlop.Round;
using FriendSlop.UI;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FriendSlop.Tests.PlayMode
{
    public class FriendSlopPrototypeSmokeTests
    {
        [UnityTest]
        public IEnumerator PrototypeScene_LoadsCoreRuntimeSystems()
        {
            var operation = SceneManager.LoadSceneAsync("FriendSlopPrototype", LoadSceneMode.Single);
            while (operation != null && !operation.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return null;

            Assert.AreEqual("FriendSlopPrototype", SceneManager.GetActiveScene().name);
            Assert.IsNotNull(NetworkManager.Singleton, "NetworkManager should exist in the prototype scene.");
            Assert.IsNotNull(NetworkSessionManager.Instance, "NetworkSessionManager should exist in the prototype scene.");
            Assert.IsNotNull(FriendSlopUI.Instance, "FriendSlopUI should exist in the prototype scene.");
            Assert.IsNotNull(Object.FindAnyObjectByType<PrototypeNetworkBootstrapper>(), "Prototype bootstrapper should exist in the prototype scene.");

            NetworkSessionManager.Instance.StartLocalHost();
            for (var frame = 0; frame < 60 && RoundManager.Instance == null; frame++)
            {
                yield return null;
            }

            Assert.IsTrue(NetworkManager.Singleton.IsListening, "Local host should start during the smoke test.");
            Assert.IsNotNull(RoundManager.Instance, "RoundManager should spawn after the local host starts.");

            NetworkSessionManager.Instance.Shutdown();
            yield return null;
        }
    }
}
