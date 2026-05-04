using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Ship
{
    [DisallowMultipleComponent]
    public sealed class ShipEnvironment : MonoBehaviour
    {
        public static readonly List<ShipEnvironment> AllEnvironments = new();

        public static event System.Action<ShipEnvironment> Registered;
        public static event System.Action<ShipEnvironment> Unregistered;

        [SerializeField] private Transform[] shipSpawnPoints;

        public Transform[] ShipSpawnPoints => shipSpawnPoints;

        public static ShipEnvironment Current
        {
            get
            {
                for (var i = 0; i < AllEnvironments.Count; i++)
                {
                    var env = AllEnvironments[i];
                    if (env != null && env.isActiveAndEnabled)
                        return env;
                }

                for (var i = 0; i < AllEnvironments.Count; i++)
                {
                    var env = AllEnvironments[i];
                    if (env != null)
                        return env;
                }

                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CollectLoadedEnvironments()
        {
            AllEnvironments.Clear();
            AllEnvironments.AddRange(
                FindObjectsByType<ShipEnvironment>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        private void Awake()
        {
            if (AllEnvironments.Contains(this)) return;

            AllEnvironments.Add(this);
            Registered?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (AllEnvironments.Remove(this))
                Unregistered?.Invoke(this);
        }

        public void ConfigureSpawnPoints(Transform[] spawnPoints)
        {
            shipSpawnPoints = spawnPoints;
        }
    }
}
