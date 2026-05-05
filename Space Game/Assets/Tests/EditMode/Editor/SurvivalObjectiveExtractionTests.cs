using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class SurvivalObjectiveExtractionTests
    {
        [Test]
        public void SurvivalObjective_HudStatus_SwitchesToExtractionCopyWhenWindowOpen()
        {
            var roundObject = new GameObject("Round Manager Survival Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                round.TimeRemaining.Value = 90f;
                round.IsExtractionWindow.Value = false;

                StringAssert.Contains("Survive", objective.BuildHudStatus(round));

                round.IsExtractionWindow.Value = true;
                round.TimeRemaining.Value = 25f;
                StringAssert.Contains("EXTRACT", objective.BuildHudStatus(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void SurvivalObjective_Evaluate_DuringExtractionWindow_StaysPendingUntilTimerExpiresOrAllBoarded()
        {
            var roundObject = new GameObject("Round Manager Survival Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                SetField(objective, "requireBoardingOnSurvive", true);
                SetField(objective, "extractionGraceSeconds", 30f);

                var round = roundObject.AddComponent<RoundManager>();
                round.IsExtractionWindow.Value = true;
                round.TimeRemaining.Value = 12f;
                round.PlayersBoarded.Value = 0;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round),
                    "Extraction window with time left and no boarders should remain Pending.");

                round.TimeRemaining.Value = 0f;
                Assert.AreEqual(ObjectiveStatus.Failed, objective.Evaluate(round),
                    "Extraction window expired with nobody boarded should fail the round.");
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void SurvivalObjective_Evaluate_BeforeWindowOpens_StaysPendingWhileSurvivalTimerCountsDown()
        {
            var roundObject = new GameObject("Round Manager Survival Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                SetField(objective, "requireBoardingOnSurvive", true);
                SetField(objective, "extractionGraceSeconds", 30f);

                var round = roundObject.AddComponent<RoundManager>();
                round.IsExtractionWindow.Value = false;
                round.TimeRemaining.Value = 45f;
                round.PlayersBoarded.Value = 0;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void SurvivalObjective_Evaluate_NoBoardingRequired_SucceedsWhenSurvivalTimerExpires()
        {
            var roundObject = new GameObject("Round Manager Survival Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                SetField(objective, "requireBoardingOnSurvive", false);

                var round = roundObject.AddComponent<RoundManager>();
                round.IsExtractionWindow.Value = false;
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Success, objective.Evaluate(round));
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
