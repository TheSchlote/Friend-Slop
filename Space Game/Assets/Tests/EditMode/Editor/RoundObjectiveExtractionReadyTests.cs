using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // Covers RoundObjective.IsExtractionReady - the predicate the extraction
    // banner latches on. No NetworkManager is created, so AllConnectedBoarded
    // resolves to false (connected == 0); these tests assert the primary-win
    // condition gating only. The "all boarded suppresses banner" path depends
    // on a live NetworkManager and is exercised by boarding/Evaluate coverage
    // plus playtest.
    public class RoundObjectiveExtractionReadyTests
    {
        [Test]
        public void QuotaObjective_NotReady_BelowTarget()
        {
            var roundObject = new GameObject("Round Manager Extraction Test");
            var objective = ScriptableObject.CreateInstance<QuotaObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                round.Quota.Value = 500;
                round.CollectedValue.Value = 499;

                Assert.IsFalse(objective.IsExtractionReady(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void QuotaObjective_Ready_WhenTargetMetAndBoardingPending()
        {
            var roundObject = new GameObject("Round Manager Extraction Test");
            var objective = ScriptableObject.CreateInstance<QuotaObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                round.Quota.Value = 500;
                round.CollectedValue.Value = 500;

                Assert.IsTrue(objective.IsExtractionReady(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void QuotaObjective_NotReady_WhenBoardingNotRequired()
        {
            var roundObject = new GameObject("Round Manager Extraction Test");
            var objective = ScriptableObject.CreateInstance<QuotaObjective>();
            try
            {
                // Boarding off => quota-met resolves immediately, no banner beat.
                SetField(objective, "requireBoarding", false);

                var round = roundObject.AddComponent<RoundManager>();
                round.Quota.Value = 500;
                round.CollectedValue.Value = 600;

                Assert.IsFalse(objective.IsExtractionReady(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void RocketAssemblyObjective_Ready_OnlyWhenAssembled()
        {
            var roundObject = new GameObject("Round Manager Extraction Test");
            var objective = ScriptableObject.CreateInstance<RocketAssemblyObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();

                round.RocketAssembled.Value = false;
                Assert.IsFalse(objective.IsExtractionReady(round));

                round.RocketAssembled.Value = true;
                Assert.IsTrue(objective.IsExtractionReady(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void SurvivalObjective_Ready_OnlyDuringExtractionWindow()
        {
            var roundObject = new GameObject("Round Manager Extraction Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();

                round.IsExtractionWindow.Value = false;
                Assert.IsFalse(objective.IsExtractionReady(round));

                round.IsExtractionWindow.Value = true;
                Assert.IsTrue(objective.IsExtractionReady(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {name} not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
