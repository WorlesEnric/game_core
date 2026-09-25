// GameCore.Rules.Narrative — the encounter domain's pure rules (07 section 3.1/3.2, P-032).
//
// `EncounterState { SessionId, Status }` is owned by `EncounterRuntime` and is deliberately separate from any actor
// or combat concept: an encounter here is a narrative beat that a committed fact can start, and its hooks are the
// chapter's declared ordered plan (`BeginScene` precedes `OfferChoice`, 07 section 3.1). The rules below are the
// whole domain policy: an encounter is idle, becomes active when the hook condition holds, and its hook plan is
// consumed in declared order. Nothing here needs an actor, a vitality value or a physics body.
#nullable enable
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>Encounter status values of the encounter domain; plain integers, one per declared state.</summary>
    public static class NarrativeEncounterStatus
    {
        /// <summary>No encounter is running for this target.</summary>
        public const int Idle = 0;

        /// <summary>The hook condition holds and the encounter's planned hooks are being consumed in order.</summary>
        public const int Active = 1;

        /// <summary>The encounter completed at its last declared hook.</summary>
        public const int Completed = 2;
    }

    /// <summary>The encounter hook plan of one chapter, and the transitions a committed fact can cause.</summary>
    public static class NarrativeEncounterRules
    {
        /// <summary>Number of declared hooks in a chapter's plan (begin, then offer).</summary>
        public const int HookCount = 2;

        /// <summary>
        /// The hook a chapter's plan runs at one index, in declared order. An index outside the plan is a miss: the
        /// plan is bounded, so a hook ordinal cannot be extrapolated (P-021's bounded output rule).
        /// </summary>
        public static bool TryGetHook(ChapterDefinition chapter, int hookIndex, out string hookDefinition)
        {
            hookDefinition = string.Empty;
            if (chapter == null || hookIndex < 0 || hookIndex >= HookCount)
            {
                return false;
            }

            hookDefinition = chapter.EncounterHookPlan[hookIndex];
            return true;
        }

        /// <summary>
        /// The declared transition a committed fact change causes: an idle encounter becomes active only when the
        /// hook condition holds, and an active encounter completes once its plan is exhausted. A no-op request is
        /// refused, so a repeated fact observation cannot advance an encounter twice (REF-N02's idempotence).
        /// </summary>
        public static bool TryReactToCondition(int currentStatus, bool hookConditionHolds, out int nextStatus)
        {
            nextStatus = currentStatus;
            if (currentStatus == NarrativeEncounterStatus.Idle)
            {
                if (!hookConditionHolds)
                {
                    return false;
                }

                nextStatus = NarrativeEncounterStatus.Active;
                return true;
            }

            if (currentStatus == NarrativeEncounterStatus.Active && !hookConditionHolds)
            {
                nextStatus = NarrativeEncounterStatus.Completed;
                return true;
            }
            return false;
        }



        /// <summary>The same reaction with its stable refusal code (P-052).</summary>
        public static bool TryReactToCondition(
            int currentStatus,
            bool hookConditionHolds,
            out int nextStatus,
            out string refusalCode)
        {
            refusalCode = NarrativeRefusals.None;
            if (TryReactToCondition(currentStatus, hookConditionHolds, out nextStatus))
            {
                return true;
            }

            // Either way the encounter is unchanged: the condition does not hold for an idle encounter, or a live
            // encounter keeps running while its condition still holds. One code says exactly that.
            refusalCode = NarrativeRefusals.EncounterUnchanged;
            return false;
        }

        /// <summary>
        /// The encounter session identity of one committed fact transition: `(target, fact key, fact version)`, so a
        /// duplicate observation of the same transition names the same session and cannot start a second encounter.
        /// It is a stable content-derived string, never a runtime handle (P-004, P-054).
        /// </summary>
        public static string SessionIdentity(string targetStableName, string factKey, int factVersion)
            => targetStableName + "/" + factKey + "/v" + factVersion.ToString(CultureInfo.InvariantCulture);

        /// <summary>Canonical text form of one encounter status, for evidence only.</summary>
        public static string Describe(int status)
        {
            if (status == NarrativeEncounterStatus.Active)
            {
                return "active";
            }

            return status == NarrativeEncounterStatus.Completed ? "completed" : "idle";
        }

        /// <summary>The declared hook plan of one chapter as a read-only list, for declaration and evidence.</summary>
        public static IReadOnlyList<string> HookPlan(ChapterDefinition chapter)
        {
            if (chapter == null)
            {
                throw new System.ArgumentNullException(nameof(chapter));
            }

            return chapter.EncounterHookPlan;
        }
    }
}
