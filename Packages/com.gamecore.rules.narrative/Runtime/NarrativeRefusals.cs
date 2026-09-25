// GameCore.Rules.Narrative — stable refusal codes of the narrative rules (P-052).
//
// A refusal must be machine-readable: a trace, a probe result or a diagnostic comparison that carried the prose of
// a reason would change whenever someone edited a sentence. Each refusal below is a stable code, and the prose stays
// a diagnostic field that is never part of the canonical trace.
#nullable enable

namespace GameCore.Rules.Narrative
{
    /// <summary>Stable codes of every narrative rule refusal, in the shape P-052 requires.</summary>
    public static class NarrativeRefusals
    {
        /// <summary>No refusal; the rule accepted its input.</summary>
        public const string None = "";

        /// <summary>A choice arrived while the conversation was neither idle nor active.</summary>
        public const string ConversationNotIdle = "conversation-not-idle";

        /// <summary>The choice names a dialogue node the conversation does not sit on.</summary>
        public const string NodeMismatch = "node-mismatch";

        /// <summary>The choice ordinal is not declared by the chapter's graph.</summary>
        public const string UndeclaredChoice = "undeclared-choice";

        /// <summary>A fact transition was requested with a value outside the declared domain.</summary>
        public const string FactValueOutOfDomain = "fact-value-out-of-domain";

        /// <summary>A fact transition was requested that would not change the fact.</summary>
        public const string FactUnchanged = "fact-unchanged";

        /// <summary>A fact version below the declared initial version.</summary>
        public const string FactVersionBelowInitial = "fact-version-below-initial";

        /// <summary>A conversation transition was requested from a status it is not legal from.</summary>
        public const string ConversationTransitionIllegal = "conversation-transition-illegal";

        /// <summary>A gate decision value outside the declared domain.</summary>
        public const string GateDecisionOutOfDomain = "gate-decision-out-of-domain";

        /// <summary>An encounter transition was requested that would not change the encounter.</summary>
        public const string EncounterUnchanged = "encounter-unchanged";

        /// <summary>A registered migration refused its source value.</summary>
        public const string MigrationSourceRefused = "migration-source-refused";
    }
}
