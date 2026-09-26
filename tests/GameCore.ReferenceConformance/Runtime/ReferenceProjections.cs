// GameCore.ReferenceConformance — the pure-rule projection of every 07 number the transcribed tables assert.
//
// The tables assert numbers (`12`, `1.02`, `1000 -> 1040 -> 1020`, `4 -> 16`, `+2`, `+5`, `+3`). Those numbers are
// not this fixture's opinions: 07's prose derives them from the gameplay rules packages, so this file recomputes
// each of them *from the rules package that owns it* and compares the result with the value the table carries.
//
// That matters twice over:
//
//   * it makes the fixture falsifiable without a Unity world — a transcription typo, or a rules change that moves a
//     documented number, fails here in a pure dotnet/NUnit run;
//   * it keeps the two halves of the claim separate. This is the pure-rule half; the Unity conformance run is the
//     real-world half, and neither substitutes for the other (P-057, 07's own header).
//
// Nothing here re-implements a rule: every projection delegates to `CardSetRules`, `TraversalMotionRules`,
// `NarrativeFacts`, `NarrativeGateRules`, `NarrativeDialogueRules`, `NarrativeChapters`,
// `NarrativeDerivationPlan` or the vocabulary constants those packages publish. The documented value is read *out
// of* the transcribed table rather than restated here, so the two cannot drift apart.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Rules.Traversal;

namespace GameCore.ReferenceConformance
{
    /// <summary>One recomputed 07 number: where it comes from, what the document says, and what the rules say.</summary>
    public readonly struct ConformanceProjection
    {
        public ConformanceProjection(
            string tableId, string rowId, string field, string documented, string computed, bool agrees)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Documented = documented ?? string.Empty;
            Computed = computed ?? string.Empty;
            Agrees = agrees;
        }

        /// <summary>The table the projection belongs to.</summary>
        public string TableId { get; }

        /// <summary>The 07 row (or precondition row) the document states the number in.</summary>
        public string RowId { get; }

        /// <summary>The canonical field the number is asserted as.</summary>
        public string Field { get; }

        /// <summary>The value the transcribed table asserts.</summary>
        public string Documented { get; }

        /// <summary>The value the owning rules package computes.</summary>
        public string Computed { get; }

        /// <summary>True when the two agree.</summary>
        public bool Agrees { get; }

        public override string ToString() => TableId + "/" + RowId + "/" + Field
            + ": documented " + Documented + ", rules " + Computed + (Agrees ? " (agrees)" : " (DISAGREES)");
    }

    /// <summary>Recomputes every 07 number the transcribed tables assert from the rules package that owns it.</summary>
    public static class ReferenceProjections
    {
        /// <summary>Every projection, in table then row order.</summary>
        public static IReadOnlyList<ConformanceProjection> All()
        {
            var projections = new List<ConformanceProjection>();
            Cards(projections);
            Narrative(projections);
            Traversal(projections);
            return projections;
        }

        /// <summary>Only the projections that disagree, which is the set a failing run reports.</summary>
        public static IReadOnlyList<ConformanceProjection> Disagreements()
        {
            IReadOnlyList<ConformanceProjection> all = All();
            var disagreeing = new List<ConformanceProjection>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].Agrees)
                {
                    disagreeing.Add(all[i]);
                }
            }

            return disagreeing;
        }

        /// <summary>One canonical line per projection, for evidence.</summary>
        public static IReadOnlyList<string> CanonicalLines()
        {
            IReadOnlyList<ConformanceProjection> all = All();
            var lines = new List<string>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ConformanceProjection projection = all[i];
                lines.Add(projection.TableId + "|" + projection.RowId + "|" + projection.Field
                    + "|" + projection.Documented + "|" + projection.Computed
                    + "|" + (projection.Agrees ? "agree" : "disagree"));
            }

            return lines;
        }

        // ------------------------------------------------------------------ card market (07 s2)

        private static void Cards(List<ConformanceProjection> projections)
        {
            // 07:98 / 07:106 — "their next valid sets award 12": the base set score plus the festival's +2, folded
            // by the registered Additive reducer the card package binds to the slot (P-019).
            Project(projections, "cards", "mount-festival", ConformanceFields.SeatNextAward(0U), NextAward(2));
            Project(projections, "cards", "mount-festival", ConformanceFields.SeatNextAward(1U), NextAward(2));

            // 07:53 — "mounting a scoring provider never awards points by itself": the totals are still the seeded
            // base, which is what the row's unchanged total asserts.
            Project(projections, "cards", "mount-festival", ConformanceFields.SeatTotal(0U), SeededSeatScore());

            // 07:100 — "the same source contribution now resolves to +4 ... later sets award 14".
            Project(projections, "cards", "reconfigure-festival", ConformanceFields.SeatBonus(0U), Bonus(2),
                phase: ConformancePhase.Before);
            Project(projections, "cards", "reconfigure-festival", ConformanceFields.SeatBonus(0U), Bonus(4));
            Project(projections, "cards", "reconfigure-festival", ConformanceFields.SeatNextAward(0U), NextAward(4));

            // 07:100 — "existing total is preserved": the score a committed set under +2 left.
            Project(projections, "cards", "reconfigure-festival", ConformanceFields.SeatTotal(0U),
                ScoreAfterOneSet(2));

            // 07:61 / 07:51 — "a nested festival with +3 produces +5 where both providers match", and "retraction
            // removes only the departing source's +2 or +3 entry".
            Project(projections, "cards", "nested-retraction", ConformanceFields.SeatBonus(0U), Bonus(2, 3),
                phase: ConformancePhase.Before);
            Project(projections, "cards", "nested-retraction", ConformanceFields.SeatBonus(0U), Bonus(3));
            Project(projections, "cards", "nested-retraction", ConformanceFields.SeatNextAward(0U), NextAward(3));

            // 07:88 / 07:101 — "SubmitSet(c1,c2,c3) ... changes the score to 16 in the same commit stage", i.e. the
            // rules' own SetScoreDelta at the effective bonus, not a fixture literal.
            Project(projections, "cards", "unmount-festival", ConformanceFields.SeatTotal(0U), ScoreAfterOneSet(2));

            // 07:101 — "the derived bonus entry is removed ... the next set awards 10": no supporter at all, so the
            // reducer folds an empty set and the award is the base score.
            ProjectToken(projections, "cards", "unmount-festival", ConformanceFields.SeatBonus(0U), AbsentContribution());
            Project(projections, "cards", "unmount-festival", ConformanceFields.SeatNextAward(0U), NextAward());

            // 07:102 — "A has bonus +1" after the move: the quiet provider's contribution alone.
            Project(projections, "cards", "reparent-seat-a", ConformanceFields.SeatBonus(0U), Bonus(1));

            // 07:103 — "B loses it": a Conservative-eligible descendant with no opt-in keeps no contribution.
            ProjectToken(projections, "cards", "mode-conservative", ConformanceFields.SeatBonus(1U), AbsentContribution());

            // 07:104 / P-046 — an eligible descendant regains the provider's own contribution.
            Project(projections, "cards", "mode-automatic", ConformanceFields.SeatBonus(1U), Bonus(2));
            Project(projections, "cards", "resume-festival", ConformanceFields.SeatNextAward(0U), NextAward(2));

            // 07:110 / P-046 — "the table executor declares PreserveDormant on last-support loss": the suspension
            // retracts the bonus and the committed total is exactly what a set left behind.
            Pre(projections, "cards", "unmount-festival", 1, ConformanceFields.SeatBonus(0U), Bonus(2));
            Pre(projections, "cards", "unmount-festival", 2, ConformanceFields.SeatTotal(0U), ScoreAfterOneSet(2));

            // 07:88 — "removes exactly those cards": the seeded hand is the fixture's own four cards and one valid
            // set takes the rules' declared three, so the hand size 4 -> 1 is the rules' arithmetic.
            Pre(projections, "cards", "reconfigure-festival", 1, ConformanceFields.SeatHandSize(0U),
                SeededHandSize() - CardSetRules.SetCardCount);

            // 07:86 — an admitted settlement advances the table version and the turn exactly once.
            Pre(projections, "cards", "reconfigure-festival", 1, ConformanceFields.TableTurn, 1);
            Pre(projections, "cards", "reconfigure-festival", 1, ConformanceFields.TableVersion, 2);
        }

        // ------------------------------------------------------------------ chapter quest (07 s3)

        private static void Narrative(List<ConformanceProjection> projections)
        {
            int one = ChapterOrdinal(NarrativeChapters.ChapterOneTag);
            int two = ChapterOrdinal(NarrativeChapters.ChapterTwoTag);

            // 07:172 — "Mara receives Chapter One dialogue; gate east receives the permit condition" and the
            // encounter target is bound by the same chapter: one chapter, its own declared binding ordinal.
            Project(projections, "narrative", "mount-chapter", ConformanceFields.DialogueBinding("npc-mara"), one);
            Project(projections, "narrative", "mount-chapter", ConformanceFields.GateBinding("gate-east"), one);
            Project(projections, "narrative", "mount-chapter", ConformanceFields.EncounterBinding("encounter-oak"), one);

            // 07:137 — "DecorativeCrowdRecipe | None | Ineligible": no binding plan selects that recipe, so the
            // derivation never reaches the crowd prop (P-015).
            ProjectToken(
                projections, "narrative", "mount-chapter", ConformanceFields.DialogueBinding("crowd-prop"),
                SelectsRecipe(NarrativeCompositionNames.DecorativeCrowdRecipe) ? "<selected>" : ConformanceValue.None);

            // 07:173 — "recipes ensure future descendants match automatically": the future villager's recipe is the
            // villager recipe the dialogue rule selects, so its binding is the same chapter's ordinal.
            ProjectToken(
                projections, "narrative", "spawn-villager", ConformanceFields.DialogueBinding("npc-newcomer"),
                SelectsDialogueRuleForVillager() ? ToToken(one) : "<not-selected>");

            // 07:164 — "a valid choice sets `chapter1.bridgePermit = true`" and "the ledger deduplicates mutation
            // requests by their admitted command identity", so one accepted choice advances the fact version once
            // and a duplicate value emits no transition (NarrativeFacts owns both values).
            Project(projections, "narrative", "unmount-chapter", ConformanceFields.BridgePermit, NarrativeFacts.True);
            Project(
                projections, "narrative", "unmount-chapter", ConformanceFields.BridgePermitVersion,
                FactVersionAfterOneTransition());

            // 07:164 — "successful step publication exposes the fact and open gate together": the gate rules own the
            // decision and the version they evaluated.
            Project(
                projections, "narrative", "unmount-chapter", ConformanceFields.GateEastDecision,
                GateDecisionForCommittedPermit());

            // 07:174 — "a registered state disposition closes the session at publication".
            Project(
                projections, "narrative", "unmount-chapter", ConformanceFields.MaraConversationStatus,
                ClosedConversationStatus());

            // 07:175 — "new bindings select Chapter Two definitions" while "Chapter One cannot derive bindings into
            // the sibling Chapter Two" (07:139): the moved target takes Chapter Two's ordinal and the sibling keeps
            // its own.
            Project(projections, "narrative", "reparent-village", ConformanceFields.DialogueBinding("npc-mara"), two);
            Project(projections, "narrative", "reparent-village", ConformanceFields.GateBinding("gate-east"), two);
            Project(projections, "narrative", "reparent-village", ConformanceFields.DialogueBinding("npc-sailor"), two);

            // 07:176 — "gate retains its binding" in Conservative while the automatic descendants lose theirs.
            Project(projections, "narrative", "mode-conservative", ConformanceFields.GateBinding("gate-east"), one);

            // 07:165 — "No admitted command and no registered WakeRequest means no new logical step": the fact a
            // world has never committed reads its declared initial value. The pre2 step's *before* column is that
            // initial value; its own commit is the `unmount-chapter` row's projection above.
            Project(
                projections, "narrative", "unmount-chapter/pre2", ConformanceFields.BridgePermit,
                NarrativeFacts.False, phase: ConformancePhase.Before);
            // 07:164 — the accepted choice the ledger records moves the conversation the dialogue rules' own table
            // describes: Idle -> Requested -> Active.
            Pre(projections, "narrative", "unmount-chapter", 2, ConformanceFields.MaraConversationStatus,
                ActiveConversationStatus());
        }

        // ------------------------------------------------------------------ traversal (07 s4)

        private static void Traversal(List<ConformanceProjection> projections)
        {
            // 07:240 / 07:203 — "additional x acceleration is +2": the modifier's own declared payload value.
            Project(
                projections, "traversal", "mount-tailwind", ConformanceFields.RunnerAccelerationX("runner-a"),
                TraversalVocabulary.TailwindMilli);
            Project(
                projections, "traversal", "spawn-runner-c", ConformanceFields.RunnerAccelerationX("runner-c"),
                TraversalVocabulary.TailwindMilli);
            Project(
                projections, "traversal", "mode-conservative",
                ConformanceFields.RunnerAccelerationX(ConformanceFields.OptedInRunner),
                TraversalVocabulary.TailwindMilli);

            // 07:203 — "CheckpointRecipe declares only its sensor contract, so it cannot receive runner
            // acceleration": the two selector schema identities differ, so no modifier rule reaches a checkpoint.
            ProjectToken(
                projections, "traversal", "mount-tailwind", ConformanceFields.RunnerAccelerationX("checkpoint-1"),
                SelectsCheckpointRecipe() ? "<selected>" : ConformanceValue.None);

            // 07:243 — "the additional x acceleration is -1": the headwind constant.
            Project(
                projections, "traversal", "reparent-runner-subtree",
                ConformanceFields.RunnerAccelerationX("runner-a"), TraversalVocabulary.HeadwindMilli);

            // 07:247 — "from x velocity `1.00`, Tailwind yields `1.04` after one 20 ms step; after a fenced reparent,
            // Headwind yields `1.02` after the next step": the rules' own integrator, at the declared step duration,
            // applied to its own previous result. Both readings are projected, so the whole sequence is checked.
            ProjectToken(
                projections, "traversal", "reparent-runner-subtree",
                ConformanceFields.RunnerVelocity("runner-a"), VelocityAfterTailwind());
            ProjectToken(
                projections, "traversal", "reparent-runner-subtree/pre4",
                ConformanceFields.RunnerVelocity("runner-a"), VelocityAfterHeadwind());

            // 07:242 — the unmount stage integrates ONE step under +2 before the modifier leaves: the script's own
            // precondition declares `1.00 -> 1.04` (07:247's first reading), and 07:242's "acquired speed during
            // prior steps" is that one committed step's speed, not two.
            ProjectToken(
                projections, "traversal", "unmount-tailwind/pre2",
                ConformanceFields.RunnerVelocity("runner-a"), VelocityAfterTailwind());

            // 07:244 — "the automatically eligible runners lose it; neither is teleported or has velocity reset":
            // an automatically eligible runner holds no contribution in Conservative, and the modifier owns no pose
            // or velocity at all (07:205).
            ProjectToken(projections, "traversal", "mode-conservative",
                ConformanceFields.RunnerAccelerationX("runner-a"), AbsentContribution());
            ProjectToken(
                projections, "traversal", "suspend-tailwind",
                ConformanceFields.RunnerAccelerationX("runner-a"), AbsentContribution());
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Compares one rules-computed integer against the value the transcribed table (or the script's precondition
        /// step of that row) carries for that field and phase. A field nothing declares for that phase is skipped:
        /// there is nothing to compare, and a `Preserved` expectation deliberately states no number.
        /// </summary>
        private static void Project(
            List<ConformanceProjection> projections,
            string tableId,
            string rowId,
            string field,
            int computed,
            int ordinal = 1,
            ConformancePhase phase = ConformancePhase.After)
            => ProjectToken(
                projections, tableId, rowId, field, ToToken(computed), ordinal, phase);

        /// <summary>Compares one rules-computed token against the value the transcribed table carries.</summary>
        private static void ProjectToken(
            List<ConformanceProjection> projections,
            string tableId,
            string rowId,
            string field,
            string computed,
            int ordinal = 1,
            ConformancePhase phase = ConformancePhase.After)
        {
            string documented = Declared(tableId, rowId, field, phase, ordinal);
            if (documented.Length == 0)
            {
                // Nothing is declared for this field in this phase: an unrecognised pair, or a `Preserved`
                // expectation whose entire claim is "the operation left it alone".
                return;
            }

            projections.Add(new ConformanceProjection(
                tableId,
                rowId,
                field,
                documented,
                computed,
                string.Equals(documented, computed, StringComparison.Ordinal)));
        }

        /// <summary>Convenience: project one expectation of one of the script's precondition steps.</summary>
        private static void Pre(
            List<ConformanceProjection> projections,
            string tableId,
            string rowId,
            int precondition,
            string field,
            int computed)
            => Project(
                projections,
                tableId,
                rowId + "/pre" + precondition.ToString(CultureInfo.InvariantCulture),
                field,
                computed);

        /// <summary>
        /// The value the transcribed table or script carries for one field of one row and phase. `ordinal` selects
        /// among repeated occurrences of one field inside a row; it is 1 for the ordinary case.
        /// </summary>
        private static string Declared(
            string tableId, string rowId, string field, ConformancePhase phase, int ordinal)
        {
            IReadOnlyList<ConformanceExpectation> expectations = ExpectationsOf(tableId, rowId);
            int seen = 0;
            for (int e = 0; e < expectations.Count; e++)
            {
                if (!string.Equals(expectations[e].Field, field, StringComparison.Ordinal))
                {
                    continue;
                }

                seen++;
                if (seen != ordinal)
                {
                    continue;
                }

                return expectations[e].Kind == ConformanceExpectationKind.Preserved
                    ? string.Empty
                    : expectations[e].Expected(phase);
            }

            return string.Empty;
        }

        /// <summary>
        /// The expectations one row id carries: a table row's own list, or — for a `<row>/pre&lt;n&gt;` id — the
        /// script step that establishes the row's before state. A projection may therefore assert a number the 07
        /// table states in prose inside a precondition, which is where 07:247's numeric sequence lives.
        /// </summary>
        private static IReadOnlyList<ConformanceExpectation> ExpectationsOf(string tableId, string rowId)
        {
            ConformanceTable? table = ReferenceTables.ById(tableId);
            if (table != null)
            {
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    if (string.Equals(table.Rows[r].RowId, rowId, StringComparison.Ordinal))
                    {
                        return table.Rows[r].Expectations;
                    }
                }
            }

            ConformanceScript? script = ReferenceScripts.ById(tableId);
            if (script != null)
            {
                IReadOnlyList<ConformanceStep> steps = script.Steps();
                for (int s = 0; s < steps.Count; s++)
                {
                    if (string.Equals(steps[s].RowId, rowId, StringComparison.Ordinal))
                    {
                        return steps[s].Expectations;
                    }
                }
            }

            return Array.Empty<ConformanceExpectation>();
        }

        /// <summary>The base set score plus a contribution folded by the card package's registered reducer.</summary>
        private static int NextAward(params int[] contributions)
        {
            int bonus = Fold(contributions);
            return bonus == int.MinValue ? int.MinValue : CardSetRules.BaseSetScore + bonus;
        }

        /// <summary>The effective bonus one set of contributions reduces to, folded by the card package's reducer.</summary>
        private static int Bonus(params int[] contributions) => Fold(contributions);

        /// <summary>
        /// The token a derived slot has when no contribution supports it at all. `CardSetRules.TryReduceBonus` folds
        /// an empty set to zero, which is the *value* a zero-contribution slot would carry; the trace's `none` is the
        /// absence of the row itself (P-033: derived component existence is governed by the effective support set),
        /// so the two are different observations and this helper names the absence rather than the zero.
        /// </summary>
        private static string AbsentContribution()
            => Fold(Array.Empty<int>()) == 0 ? ConformanceValue.None : "<contribution>";

        /// <summary>The score one committed set leaves from the card fixture's seeded base at this bonus.</summary>
        private static int ScoreAfterOneSet(int effectiveBonus)
            => SeededSeatScore() + CardSetRules.SetScoreDelta(effectiveBonus);

        /// <summary>The reducer's own answer for no contributions, which is what a zero-supporter slot would carry.</summary>
        private static int NoContribution() => Fold(Array.Empty<int>());

        /// <summary>The reducer's fold over the given contributions, with a refusal reported as its own token.</summary>
        private static int Fold(int[] contributions)
        {
            var values = new List<int>(contributions.Length);
            for (int i = 0; i < contributions.Length; i++)
            {
                values.Add(contributions[i]);
            }

            return CardSetRules.TryReduceBonus(values, out int effective) ? effective : int.MinValue;
        }

        /// <summary>
        /// The score the card slice seeds a seat with. The rules package does not carry the gameplay fixture's
        /// seeding constant, and 07 s2.3 names it directly ("its score is 4"), so this is the one number the
        /// conformance fixture states itself; the Unity run asserts a real world really starts there.
        /// </summary>
        private static int SeededSeatScore() => 4;

        /// <summary>
        /// The cards the card slice seeds a seat with. 07 s2.3 names the shape (`{c1,c2,c3}` plus one spare) and
        /// the preconditions assert the resulting hand size, so the fixture states the four and the rules own the
        /// three a set consumes.
        /// </summary>
        private static int SeededHandSize() => 4;

        /// <summary>The fact version one accepted transition leaves, or a refusal token.</summary>
        private static int FactVersionAfterOneTransition()
        {
            bool first = NarrativeFacts.TryTransition(
                NarrativeFacts.False, NarrativeFacts.True, out int next, out string _);
            bool duplicate = NarrativeFacts.TryTransition(
                NarrativeFacts.True, NarrativeFacts.True, out int unchanged, out string code);
            bool deduplicated = !duplicate
                && unchanged == NarrativeFacts.True
                && string.Equals(code, NarrativeRefusals.FactUnchanged, StringComparison.Ordinal);
            return first && next == NarrativeFacts.True && deduplicated
                ? NarrativeFacts.InitialVersion + 1
                : int.MinValue;
        }

        /// <summary>The gate decision the gate rules derive from the committed permit fact.</summary>
        private static int GateDecisionForCommittedPermit()
        {
            int version = FactVersionAfterOneTransition();
            if (version == int.MinValue)
            {
                return int.MinValue;
            }

            return NarrativeGateRules.TryEvaluate(
                    NarrativeFacts.True, version, out int decision, out int evaluated) && evaluated == version
                ? decision
                : int.MinValue;
        }

        /// <summary>The conversation status a registered close disposition leaves (the rules' own table).</summary>
        private static int ClosedConversationStatus()
            => ConversationStatus(NarrativeConversationStatus.Active, NarrativeConversationStatus.Closed);

        /// <summary>The conversation status an accepted choice leaves (Idle -> Requested -> Active).</summary>
        private static int ActiveConversationStatus()
        {
            int requested = ConversationStatus(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Requested);
            if (requested == int.MinValue)
            {
                return int.MinValue;
            }

            return ConversationStatus(requested, NarrativeConversationStatus.Active);
        }

        private static int ConversationStatus(int from, int to)
            => NarrativeDialogueRules.TryTransition(from, to, out int next, out string code) && code.Length == 0
                ? next
                : int.MinValue;

        /// <summary>True when a binding plan selects the named recipe, so the eligibility claim is the plan's.</summary>
        private static bool SelectsRecipe(string recipeName)
        {
            IReadOnlyList<NarrativeBindingPlan> bindings = NarrativeDerivationPlan.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (string.Equals(bindings[i].SelectorRecipeName, recipeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the dialogue rule selects the villager recipe: 07:173's "new villager receives Chapter One
        /// dialogue" is the claim that a future instance of that recipe matches the same rule the existing ones do.
        /// </summary>
        private static bool SelectsDialogueRuleForVillager()
        {
            IReadOnlyList<NarrativeBindingPlan> bindings = NarrativeDerivationPlan.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (string.Equals(
                        bindings[i].SelectorRecipeName,
                        NarrativeCompositionNames.VillagerRecipe,
                        StringComparison.Ordinal)
                    && string.Equals(
                        bindings[i].CapabilityName,
                        NarrativeCompositionNames.DialogueBindingCapability,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the acceleration rule's selector would also select the checkpoint recipe.</summary>
        private static bool SelectsCheckpointRecipe()
            => string.Equals(
                TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe).ToString(),
                TraversalVocabulary.SelectorSchema(TraversalVocabulary.CheckpointRecipe).ToString(),
                StringComparison.Ordinal);

        private static int ChapterOrdinal(string chapterTag)
            => NarrativeChapters.TryGet(chapterTag, out ChapterDefinition? chapter) && chapter != null
                ? chapter.BindingOrdinal
                : int.MinValue;

        /// <summary>The velocity one 20 ms step under tailwind leaves, from the fixture's seeded speed.</summary>
        private static string VelocityAfterTailwind()
            => Integrate(SeededVelocity(), TraversalVocabulary.TailwindMilli);

        /// <summary>The velocity the next step under headwind leaves, from the tailwind result.</summary>
        private static string VelocityAfterHeadwind()
        {
            bool tailwind = TraversalMotionRules.TryVelocityAfterStep(
                new TraversalVector3i(SeededVelocity(), 0, 0),
                new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0),
                TraversalVocabulary.StepMilliseconds,
                out TraversalVector3i first);
            if (!tailwind)
            {
                return "<refused>";
            }

            bool headwind = TraversalMotionRules.TryVelocityAfterStep(
                first,
                new TraversalVector3i(TraversalVocabulary.HeadwindMilli, 0, 0),
                TraversalVocabulary.StepMilliseconds,
                out TraversalVector3i second);
            return headwind ? Token(second) : "<refused>";
        }

        private static string Integrate(int velocityMilli, int accelerationMilli)
            => TraversalMotionRules.TryVelocityAfterStep(
                new TraversalVector3i(velocityMilli, 0, 0),
                new TraversalVector3i(accelerationMilli, 0, 0),
                TraversalVocabulary.StepMilliseconds,
                out TraversalVector3i next)
                ? Token(next)
                : "<refused>";

        /// <summary>The fixture's seeded baseline horizontal speed, from the traversal vocabulary (07:247's `1.00`).</summary>
        private static int SeededVelocity() => TraversalVocabulary.SeededVelocityMilli;

        /// <summary>One vector as the canonical `(x,y,z)` token the velocity fields use.</summary>
        private static string Token(TraversalVector3i value)
            => "(" + ToToken(value.X) + "," + ToToken(value.Y) + "," + ToToken(value.Z) + ")";

        private static string ToToken(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
