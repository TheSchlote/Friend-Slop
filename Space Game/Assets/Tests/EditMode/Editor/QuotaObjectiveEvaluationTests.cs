using System.Reflection;
using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    // D-008 / CLAUDE hard-rule 7: every RoundObjective subclass needs Evaluate coverage.
    // RoundObjectiveTextTests only exercises QuotaObjective's copy; the win/lose branches
    // (target resolution, boarding gate, timer-fail) were untested before this.
    public class QuotaObjectiveEvaluationTests
    {
        [Test]
        public void Evaluate_CollectedMeetsTarget_NoBoardingRequired_Succeeds()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                round.Quota.Value = 500;

                round.CollectedValue.Value = 500;
                Assert.AreEqual(ObjectiveStatus.Success, objective.Evaluate(round));

                round.CollectedValue.Value = 600;
                Assert.AreEqual(ObjectiveStatus.Success, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_CollectedBelowTarget_NoTimer_StaysPending()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                round.Quota.Value = 500;
                round.CollectedValue.Value = 499;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TimerExpiredBelowTarget_Fails()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                SetField(round, "roundLengthSeconds", 120f); // HasActiveTimer => roundLengthSeconds > 0
                round.Quota.Value = 500;
                round.CollectedValue.Value = 100;
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Failed, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TimerExpiredBelowTarget_FailDisabled_StaysPending()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                SetField(objective, "failOnTimerExpired", false);
                SetField(round, "roundLengthSeconds", 120f);
                round.Quota.Value = 500;
                round.CollectedValue.Value = 100;
                round.TimeRemaining.Value = 0f;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_QuotaOverride_TakesPrecedenceOverRoundQuota()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                SetField(objective, "quotaOverride", 1000);
                round.Quota.Value = 200; // would be met by 300 if it were the target

                round.CollectedValue.Value = 300;
                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round),
                    "Override target (1000) must be used, not round.Quota (200).");

                round.CollectedValue.Value = 1000;
                Assert.AreEqual(ObjectiveStatus.Success, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_TargetFloorsAtOne_WhenRoundQuotaIsZero()
        {
            WithQuota((round, objective) =>
            {
                SetField(objective, "requireBoarding", false);
                round.Quota.Value = 0; // ResolveTarget => Mathf.Max(1, 0)

                round.CollectedValue.Value = 0;
                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));

                round.CollectedValue.Value = 1;
                Assert.AreEqual(ObjectiveStatus.Success, objective.Evaluate(round));
            });
        }

        [Test]
        public void Evaluate_BoardingRequired_NotEveryoneBoarded_StaysPending()
        {
            WithQuota((round, objective) =>
            {
                // requireBoarding defaults true; with no NetworkManager connected==0, so the
                // boarding gate can never be satisfied and quota-met must NOT resolve Success.
                round.Quota.Value = 100;
                round.CollectedValue.Value = 500;
                round.PlayersBoarded.Value = 0;

                Assert.AreEqual(ObjectiveStatus.Pending, objective.Evaluate(round));
            });
        }

        private static void WithQuota(System.Action<RoundManager, QuotaObjective> body)
        {
            var roundObject = new GameObject("Round Manager Quota Test");
            var objective = ScriptableObject.CreateInstance<QuotaObjective>();
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
