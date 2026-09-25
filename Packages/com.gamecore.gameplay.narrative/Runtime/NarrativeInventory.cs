// GameCore.Gameplay.Narrative — the complete name inventory of the narrative slice (P-001, P-059, TEST-021).
//
// A genre-neutrality claim is only checkable if the audited set is complete and fixed. The rules assembly owns the
// content names (`NarrativeRegistrations.AllNames`); this file adds every name the gameplay package itself registers
// — the ECS component inventory, the observability trail domain and its slots and fields, the extra buffer/route/
// order/schema identities that only exist here, the two registered migrations and the routing owners. Together they
// are the inventory a scenario audits before it claims TEST-021's "no actor, vitality or physics schema/stage is
// present".
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>The slice's full registered-name inventory, in a fixed order.</summary>
    public static class NarrativeInventory
    {
        /// <summary>
        /// Names the gameplay package adds to the rules assembly's content names. Nothing here is generated at run
        /// time: a name that is not in this list is not registered by the slice, and a test asserts the count.
        /// </summary>
        public static IReadOnlyList<string> GameplayNames { get; } = new[]
        {
            // The ECS component inventory (P-032, TEST-021).
            "NarrativeTargetMarker",

            // The trail domain: the compiled order's own observability, written by two ordered stages (P-034, P-040).
            "narrative.domain.step-trail",
            "narrative.domain.step-trail.layout",
            "narrative.domain.step-trail.init",
            "narrative.domain.step-trail.config-change",
            "narrative.slot.trail.steps",
            "narrative.slot.trail.projected",
            "narrative.slot.trail.facts",
            "narrative.slot.trail.hooks",
            "narrative.slot.trail.gate-decisions",
            "narrative.field.trail.steps",
            "narrative.field.trail.projected",
            "narrative.field.trail.facts",
            "narrative.field.trail.hooks",
            "narrative.field.trail.gate-decisions",

            // The conversation, gate and encounter slots this package addresses by identity (P-032).
            "narrative.slot.conversation.node",
            "narrative.slot.conversation.status",
            "narrative.field.conversation.node",
            "narrative.field.conversation.status",
            "narrative.slot.gate.decision",
            "narrative.slot.gate.evaluated-version",
            "narrative.field.gate.decision",
            "narrative.field.gate.evaluated-version",
            "narrative.slot.encounter.status",
            "narrative.field.encounter.status",

            // Stage, system, buffer, order, schema and migration identities that only this package declares.
            "narrative.dialogue",
            "narrative.system.dialogue",
            "narrative.buffer.encounter-observed",
            "narrative.order.encounter-observed",
            "narrative.schema.fact-observed",
            "narrative.schema.fact-committed",
            "narrative.migration.conversation.node.v1-v2",
            "narrative.migration.conversation.status.v1-v2",

            // Routing owners, provider words and the world's own identities (P-042, P-050).
            "narrative.owner.ingress",
            "narrative.owner.trail",
            "narrative.recipe.forward-villager",
            "narrative.recipe.forward-villager.definition",
            "narrative.binding.forward",
            "narrative.schema.forward-binding",
            "narrative-forward",
            "narrative.forward-provider",
            "narrative.clock.domain",
            "narrative.issuer",
            "narrative.package",
            "narrative.world-definition",
        };

        /// <summary>The content names of the rules assembly plus this package's own names, in one canonical order.</summary>
        public static IReadOnlyList<string> AllNames()
        {
            var names = new List<string>(NarrativeRegistrations.Count + GameplayNames.Count);
            for (int i = 0; i < NarrativeRegistrations.AllNames.Count; i++)
            {
                names.Add(NarrativeRegistrations.AllNames[i]);
            }

            for (int i = 0; i < GameplayNames.Count; i++)
            {
                names.Add(GameplayNames[i]);
            }

            return names;
        }

        /// <summary>The number of names a neutrality audit examines.</summary>
        public static int Count => NarrativeRegistrations.Count + GameplayNames.Count;

        /// <summary>Audits the whole inventory for genre vocabulary (P-001, TEST-021).</summary>
        public static GenreAuditReport Audit() => NarrativeGenreAudit.Audit(AllNames());

        /// <summary>
        /// Audits the declared ECS component inventory on its own, so "no actor/vitality/physics component exists"
        /// is a claim about the types the slice really registers and not only about its name table (TEST-021).
        /// </summary>
        public static GenreAuditReport AuditComponents() => NarrativeGenreAudit.Audit(NarrativeComponentInventory.Names);
    }
}
