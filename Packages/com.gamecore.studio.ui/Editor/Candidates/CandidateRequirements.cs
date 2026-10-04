// GameCore.Studio.UI - apply-requirement badges and the "what will happen" text of a change set (02 s7, 03 s6 requirements,
// SR-1.8). Badges: Edit-time (applies to authored data now), Live (takes effect in a running world), RequiresPlayStop
// (needs a world rebuild: restart Play), RequiresRecompile (code), RequiresStageVerdict (a staged mechanism package
// waits for the staging lane's verdict, P2.4).
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Studio.Model;

namespace GameCore.Studio.UI
{
    /// <summary>Requirement badges of change sets.</summary>
    public static class CandidateRequirements
    {
        public const string EditTime = "Edit-time";
        public const string Live = "Live";
        public const string RequiresPlayStop = "RequiresPlayStop";
        public const string RequiresRecompile = "RequiresRecompile";
        public const string RequiresStageVerdict = "RequiresStageVerdict";

        /// <summary>The declared requirements, else the maximum over the operations' requirements (Live when absent).</summary>
        public static Requirements Effective(ChangeSet changeSet)
        {
            if (changeSet.Requirements != null)
            {
                return changeSet.Requirements;
            }

            List<RuntimeApply> perOperation = new List<RuntimeApply>();
            foreach (Operation operation in changeSet.Operations)
            {
                perOperation.Add(operation.ApplyRequirement ?? RuntimeApply.Live);
            }

            return Requirements.FromOperations(perOperation);
        }

        /// <summary>True when an operation proposes a mechanism (staged package; P2.4 decides).</summary>
        public static bool NeedsStageVerdict(ChangeSet changeSet)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if (string.Equals(operation.Tool, BuiltInToolIds.MechanismPropose, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static IReadOnlyList<string> Badges(ChangeSet changeSet)
        {
            Requirements requirements = Effective(changeSet);
            List<string> badges = new List<string>();
            bool compile = requirements.Compile || requirements.Max >= RuntimeApply.Compile;
            if (!compile)
            {
                badges.Add(EditTime);
            }

            if (requirements.Max == RuntimeApply.Live && !requirements.WorldRebuild)
            {
                badges.Add(Live);
            }

            if (requirements.Max == RuntimeApply.Rebuild || requirements.WorldRebuild)
            {
                badges.Add(RequiresPlayStop);
            }

            if (compile)
            {
                badges.Add(RequiresRecompile);
            }

            if (NeedsStageVerdict(changeSet) || compile)
            {
                badges.Add(RequiresStageVerdict);
            }

            return badges;
        }

        /// <summary>What Apply will do now, in Play or Edit mode.</summary>
        public static string Explain(ChangeSet changeSet, bool playMode)
        {
            Requirements requirements = Effective(changeSet);
            StringBuilder text = new StringBuilder();
            bool compile = requirements.Compile || requirements.Max >= RuntimeApply.Compile;
            if (compile || NeedsStageVerdict(changeSet))
            {
                text.Append("Code change: nothing is applied to the editor until the staging lane's verdict admits it (verdict pending). ");
            }
            else if (playMode)
            {
                if (requirements.Max == RuntimeApply.Live && !requirements.WorldRebuild)
                {
                    text.Append("Play: live operations go to the running world (one composition edit each, expected revision checked) and the authored data changes too. ");
                }
                else
                {
                    text.Append("Play: the authored data changes now; the running world keeps its current state until Play is stopped and restarted (world rebuild). ");
                }
            }
            else
            {
                text.Append("Edit: the authored assets and scenes change now in one Undo group; ");
                text.Append(requirements.Max == RuntimeApply.Live ? "the next Play uses them directly. " : "the world is rebuilt on the next Play. ");
            }

            if (requirements.Build)
            {
                text.Append("A standalone player needs a new build to show it. ");
            }

            ApplyPolicy policy = changeSet.EffectivePolicy;
            text.Append(policy == ApplyPolicy.AllOrNothing
                ? "AllOrNothing: if any operation fails, everything is rolled back."
                : "BestEffort: operations that fail are reported; the others stay applied.");
            return text.ToString();
        }

        /// <summary>"3 ops: set x2, move x1".</summary>
        public static string Summary(ChangeSet changeSet)
        {
            SortedDictionary<string, int> byTool = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Operation operation in changeSet.Operations)
            {
                byTool.TryGetValue(operation.Tool, out int count);
                byTool[operation.Tool] = count + 1;
            }

            StringBuilder text = new StringBuilder();
            text.Append(changeSet.Operations.Count == 1 ? "1 op" : changeSet.Operations.Count + " ops");
            text.Append(": ");
            bool first = true;
            foreach (KeyValuePair<string, int> pair in byTool)
            {
                if (!first)
                {
                    text.Append(", ");
                }

                first = false;
                text.Append(pair.Key).Append(" x").Append(pair.Value);
            }

            return text.ToString();
        }

        /// <summary>A copy of a change set with another policy (everything else unchanged).</summary>
        public static ChangeSet WithPolicy(ChangeSet changeSet, ApplyPolicy policy)
        {
            return new ChangeSet(
                changeSet.Id,
                changeSet.Schema,
                changeSet.Intent,
                changeSet.Operations,
                changeSet.Selection,
                changeSet.BaseVersions,
                changeSet.Artifacts,
                changeSet.Validation,
                changeSet.Requirements,
                changeSet.Links,
                changeSet.State,
                changeSet.Outcomes,
                policy,
                changeSet.Timestamps);
        }
    }
}
