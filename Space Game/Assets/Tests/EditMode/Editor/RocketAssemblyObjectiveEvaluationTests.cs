using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // D-008 / CLAUDE hard-rule 7. The Success path delegates to
    // RoundStateUtility.IsLaunchReady (truth table already covered by
    // RoundStateUtilityTests); these tests pin the objective's OWN branches:
    // the timer-fail decision, the quota cushion, and the fail-disabled gate.
    public class RocketAssemblyObjectiveEvaluationTests
    {
        [Test]
        public void Evaluate_NotLaunchReady_NoTimer_StaysPending()
        {
            WithRocket((round, objective) =>
            {
                round.RocketAssembled.Value = false;
                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TimerExpiredBelowQuota_Fails()
        {
            WithRocket((round, objective) =>
            {
                SetField(round, "roundLengthSeconds", 120f);
                round.Quota.Value = 500;
                round.CollectedValue.Value = 100;
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Failed, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TimerExpiredButQuotaCushionMet_StaysPending()
        {
            WithRocket((round, objective) =>
            {
                SetField(round, "roundLengthSeconds", 120f);
                round.Quota.Value = 500;
                round.CollectedValue.Value = 500; // not < Max(quota, minQuotaToAvoidFail)
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round),
                    "Meeting the quota by timer expiry avoids the rocket-fail.");
            });
        }

        [Test]
        public void Evaluate_MinQuotaToAvoidFail_RaisesTheFailBar()
        {
            WithRocket((round, objective) =>
            {
                SetField(round, "roundLengthSeconds", 120f);
                SetField(objective, "minQuotaToAvoidFail", 800);
                round.Quota.Value = 100; // Max(100, 800) => 800
                round.TimeRemaining.Value = 0f;

                round.CollectedValue.Value = 500;
                Assert.AreEqual(ObjectiveStatus.Failed, objective.Evaluate(round),
                    "500 < Max(quota 100, minQuotaToAvoidFail 800) must fail.");

                round.CollectedValue.Value = 800;
                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TimerExpired_FailDisabled_StaysPending()
        {
            WithRocket((round, objective) =>
            {
                SetField(objective, "failOnTimerExpired", false);
                SetField(round, "roundLengthSeconds", 120f);
                round.Quota.Value = 500;
                round.CollectedValue.Value = 0;
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_RocketAssembledButNoConnectedPlayers_StaysPending()
        {
            WithRocket((round, objective) =>
            {
                // IsLaunchReady requires connected players; with no NetworkManager
                // connected==0, so an assembled rocket alone must not win.
                round.RocketAssembled.Value = true;
                round.PlayersBoarded.Value = 5;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        private static void WithRocket(System.Action<RoundManager, RocketAssemblyObjective> body)
        {
            var roundObject = new GameObject("Round Manager Rocket Test");
            var objective = ScriptableObject.CreateInstance<RocketAssemblyObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                body(round, objective);
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
