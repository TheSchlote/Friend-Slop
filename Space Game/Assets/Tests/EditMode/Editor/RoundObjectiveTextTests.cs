using FriendSlop.Round;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests.EditMode
{
    public class RoundObjectiveTextTests
    {
        [Test]
        public void QuotaObjective_ResultText_ReportsCollectedAgainstTarget()
        {
            var roundObject = new GameObject("Round Manager Text Test");
            var objective = ScriptableObject.CreateInstance<QuotaObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                round.Quota.Value = 500;

                round.CollectedValue.Value = 600;
                StringAssert.Contains("QUOTA MET", objective.BuildSuccessText(round));

                round.CollectedValue.Value = 450;
                StringAssert.Contains("Collected $450 / $500", objective.BuildFailureText(round));
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void RocketAssemblyObjective_FailureText_ListsPartProgress()
        {
            var roundObject = new GameObject("Round Manager Text Test");
            var objective = ScriptableObject.CreateInstance<RocketAssemblyObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();
                round.HasCockpit.Value = true;
                round.HasWings.Value = false;
                round.HasEngine.Value = false;

                var text = objective.BuildFailureText(round);

                StringAssert.Contains("ROCKET NOT READY", text);
                StringAssert.Contains("Cockpit OK", text);
                StringAssert.Contains("Wings missing", text);
                StringAssert.Contains("Engine missing", text);
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }

        [Test]
        public void SurvivalObjective_SuccessText_UsesSurvivalCopy()
        {
            var roundObject = new GameObject("Round Manager Text Test");
            var objective = ScriptableObject.CreateInstance<SurvivalObjective>();
            try
            {
                var round = roundObject.AddComponent<RoundManager>();

                var text = objective.BuildSuccessText(round);

                StringAssert.Contains("SURVIVED", text);
                StringAssert.Contains("\nCrew survived", text);
            }
            finally
            {
                Object.DestroyImmediate(objective);
                Object.DestroyImmediate(roundObject);
            }
        }
    }
}
