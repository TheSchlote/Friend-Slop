using System.Reflection;
using FriendSlop.Core;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class CarrySurfaceUtilityTests
    {
        private GameObject worldObject;
        private GameObject volumeObject;

        [TearDown]
        public void TearDown()
        {
            if (volumeObject != null)
            {
                var volume = volumeObject.GetComponent<FlatGravityVolume>();
                if (volume != null) InvokeLifecycle(volume, "OnDisable");
                Object.DestroyImmediate(volumeObject);
                volumeObject = null;
            }

            if (worldObject != null)
            {
                var world = worldObject.GetComponent<SphereWorld>();
                if (world != null) InvokeLifecycle(world, "OnDisable");
                Object.DestroyImmediate(worldObject);
                worldObject = null;
            }
        }

        [Test]
        public void ClampTargetAboveSurface_PushesUndergroundTargetToSurfaceClearance()
        {
            var world = CreateWorld(new Vector3(1000f, 0f, 0f));
            var undergroundPosition = world.Center + Vector3.right * (world.Radius - 2f);

            var clamped = CarrySurfaceUtility.ClampTargetAboveSurface(undergroundPosition);

            Assert.GreaterOrEqual(world.GetSurfaceDistance(clamped), 0.09f);
            Assert.Less(Vector3.Distance(world.GetUp(undergroundPosition), world.GetUp(clamped)), 0.001f);
        }

        [Test]
        public void ClampTargetAboveSurface_LeavesFlatGravityVolumeTargetUnchanged()
        {
            var world = CreateWorld(new Vector3(2000f, 0f, 0f));
            var target = world.Center + Vector3.right * (world.Radius - 2f);
            volumeObject = new GameObject("Flat Gravity Volume");
            volumeObject.transform.position = target;
            InvokeLifecycle(volumeObject.AddComponent<FlatGravityVolume>(), "OnEnable");

            var clamped = CarrySurfaceUtility.ClampTargetAboveSurface(target);

            Assert.AreEqual(target, clamped);
        }

        private SphereWorld CreateWorld(Vector3 center)
        {
            worldObject = new GameObject("Sphere World");
            worldObject.transform.position = center;
            var world = worldObject.AddComponent<SphereWorld>();
            InvokeLifecycle(world, "OnEnable");
            return world;
        }

        private static void InvokeLifecycle(MonoBehaviour component, string methodName)
        {
            component.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(component, null);
        }
    }
}
