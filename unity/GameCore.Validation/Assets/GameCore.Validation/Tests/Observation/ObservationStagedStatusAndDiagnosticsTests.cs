// GameCore.Observation.Tests — GC-016: staged status is not published state, structured diagnostics keep their
// precedence, and the fixtures' own rejections are retrievable (O-25, P-026, P-052).
//
// Three observations live here:
//
//   * `StagedStatusReader` distinguishes a staged, unpublished proposal from a published composition result. The
//     bridge's `SubmitAndExecute` drains the lane itself (it calls `Composition.Drain()` before it hands the world
//     its demand), so a staged window is only observable by submitting through `CompositionHost.SubmitEdit` and
//     reading before the drain — which is exactly what the first test does;
//   * `DiagnosticRegistry`/`DiagnosticPrecedence` intern, retrieve and order the REAL diagnostics a real refused
//     composition edit produced, and `Format()` never participates in the order;
//   * `CompositionDiagnosticFeed` records a publication and a rejection built from real values (plan hash, affected
//     counts, revision/epoch pair, diagnostics) and resolves both by retrieval key.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Composition.Diagnostics;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Planning;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using NUnit.Framework;

namespace GameCore.Observation.Tests
{
    /// <summary>Staged-operation status, structured diagnostics and the composition diagnostic feed.</summary>
    [TestFixture]
    public sealed class ObservationStagedStatusAndDiagnosticsTests
    {
        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// A staged proposal is not published state: before the drain the reader reports a staged plan with a real
        /// plan hash and no published snapshot, the committed composition has not moved, and after the drain the
        /// same operation reports the published composition instead (O-25, 00 s9, 05 s5).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void StagedStatus_DistinguishesAStagedPlanFromPublishedState(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                CompositionHost lane = world.Lane;
                CompositionRevision committedBefore = lane.Committed.Revision;
                AssemblyEpoch committedEpochBefore = lane.Committed.Epoch;
                ulong publicationCountBefore = (ulong)lane.PublicationCount;

                OperationId operation = world.NextOperation();
                EditAdmission admission = lane.SubmitEdit(world.StagedEdit, operation, committedBefore);
                Assert.That(admission.Kind, Is.EqualTo(AdmissionKind.Fresh));
                Assert.That(admission.Staged, Is.True,
                    "the family's own next mount is stageable: " + admission.Code + " / "
                    + ObservationFamilyWorld.Describe(admission.Diagnostics));
                Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(admission.Plan, Is.Not.Null);
                Assert.That(admission.Plan!.Succeeded, Is.True);

                var reader = new StagedStatusReader(lane);
                StagedOperationStatus staged = reader.Read(operation);

                Assert.That(reader.ReadCount, Is.EqualTo(1));
                Assert.That(reader.StagedCount, Is.EqualTo(1));
                Assert.That(reader.ExpiredCount, Is.EqualTo(0));
                Assert.That(reader.UnknownCount, Is.EqualTo(0));
                Assert.That(staged.Operation.Equals(operation), Is.True);
                Assert.That(staged.ReadOutcome, Is.EqualTo(OperationReadOutcome.Found));
                Assert.That(staged.Source, Is.EqualTo(StagedStatusSource.StagedPlan));
                Assert.That(staged.HasStagedPlan, Is.True);
                Assert.That(staged.DescribesPublishedState, Is.False,
                    "an unpublished proposal is never state an observer may treat as committed world state (00 s9)");
                Assert.That(staged.PublishedSnapshot, Is.Null);
                Assert.That(staged.StagedPlanHash.IsEmpty, Is.False, "a staged plan has a real semantic hash (05 s4)");
                Assert.That(staged.StagedPlanHash.Equals(admission.Plan!.PlanHash()), Is.True);
                Assert.That(staged.StagedCode, Is.EqualTo(DiagnosticCode.None));
                Assert.That(staged.StagedIsNoChange, Is.False);
                Assert.That(staged.IsTerminal, Is.False, "a staged proposal has not settled yet");
                Assert.That(staged.Outcome, Is.EqualTo(Outcome.Pending));
                Assert.That(staged.StagedActivationCount, Is.EqualTo(admission.Plan.ActivationOrder.Count));
                Assert.That(staged.StagedRetirementCount, Is.EqualTo(admission.Plan.RetiredInstances.Count));
                Assert.That(staged.StagedDispositionCount, Is.EqualTo(admission.Plan.StateDispositions.Count));
                Assert.That(staged.StagedResourceLeaseCount, Is.EqualTo(lane.StagedLeases(operation).Count));
                Assert.That(staged.Detail.Length, Is.GreaterThan(0));

                // The load-bearing distinction: a staged plan never claims to describe published state.
                Assert.That(
                    staged.Source == StagedStatusSource.StagedPlan && staged.DescribesPublishedState,
                    Is.False);

                // The staged tail moved; the committed composition did not.
                Assert.That(lane.Committed.Revision.Equals(committedBefore), Is.True,
                    "a staged proposal increments no published revision (P-006)");
                Assert.That(lane.Committed.Epoch.Equals(committedEpochBefore), Is.True);
                Assert.That(lane.StagedState.Revision.CompareTo(lane.Committed.Revision) > 0, Is.True,
                    "the staged tail is ahead of the committed state, which is why the two are reported apart");
                Assert.That(lane.StagedPlan(operation), Is.Not.Null);

                // Draining publishes the proposal; the world assembly for it is published by the real pipeline.
                DerivedAssemblyReport report = world.PublishStagedAndDerive(out IReadOnlyList<PublishedOperation> published);
                Assert.That(published.Count, Is.EqualTo(1));
                Assert.That(published[0].Operation.Equals(operation), Is.True);
                Assert.That(published[0].Outcome, Is.EqualTo(Outcome.Published));
                Assert.That(published[0].Token, Is.Not.Null);
                Assert.That(report.Outcome, Is.Not.EqualTo(DerivedAssemblyOutcome.Refused), report.Describe());
                Assert.That((ulong)lane.PublicationCount, Is.EqualTo(publicationCountBefore + 1UL));
                Assert.That(lane.Committed.Revision.CompareTo(committedBefore) > 0, Is.True);
                Assert.That(lane.StagedPlan(operation), Is.Null, "a published operation has no staged plan any more");

                StagedOperationStatus settled = reader.Read(operation);
                Assert.That(reader.ReadCount, Is.EqualTo(2));
                Assert.That(settled.HasStagedPlan, Is.False);
                Assert.That(settled.IsTerminal, Is.True);
                Assert.That(settled.Outcome, Is.EqualTo(Outcome.Published));
                Assert.That(settled.Source, Is.EqualTo(StagedStatusSource.PublishedComposition));
                Assert.That(settled.DescribesPublishedState, Is.True);
                Assert.That(settled.PublishedSnapshot, Is.Not.Null);
                Assert.That(settled.PublishedSnapshot!.Value.World.Session.Equals(world.World.Session), Is.True);
                Assert.That(settled.PublishedRevision.CompareTo(committedBefore) > 0, Is.True);
                Assert.That(settled.PublishedEpoch.Equals(world.Host.CurrentEpoch), Is.True,
                    "the settled publication names the epoch the world really published (P-006)");
                Assert.That(settled.StagedPlanHash.IsEmpty, Is.True,
                    "the published status carries no staged plan hash");
                Assert.That(
                    settled.Source == StagedStatusSource.StagedPlan && settled.DescribesPublishedState,
                    Is.False);

                // An operation the lane never admitted is reported as unknown, never as a staged plan.
                StagedOperationStatus unknown = reader.Read(world.NextOperation());
                Assert.That(unknown.Source, Is.EqualTo(StagedStatusSource.None));
                Assert.That(unknown.HasStagedPlan, Is.False);
                Assert.That(unknown.DescribesPublishedState, Is.False);
                Assert.That(unknown.ReadOutcome, Is.EqualTo(OperationReadOutcome.Unknown));
                Assert.That(reader.UnknownCount, Is.EqualTo(1));

                // The bridge drains as part of its own submit, so the staged window is gone by the time it returns:
                // the status it leaves behind names the published composition, never a staged plan.
                WorldAdmissionReport bridged = world.Bridge.SubmitAndExecute(world.StagedEdit, world.NextOperation());
                Assert.That(bridged.Operation.Equals(default(OperationId)), Is.False);
                Assert.That(bridged.LaneRowCount, Is.GreaterThan(0));
                StagedOperationStatus afterBridge = reader.Read(bridged.Operation);
                Assert.That(afterBridge.Source, Is.Not.EqualTo(StagedStatusSource.StagedPlan),
                    "SubmitAndExecute drains the lane, so no staged plan survives it");
            }
        }

        /// <summary>
        /// The real diagnostics of a really refused composition edit are interned, retrievable and ordered by typed
        /// fields only: registering one twice returns the same key, the order of any permutation is identical, and
        /// two diagnostics differing only in their wording occupy the same position with the same canonical hash
        /// (P-052).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void StructuredDiagnostics_InternRetrieveAndOrderWithoutReadingTheFormatting(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                RefusedEdit refused = RefuseOneEdit(world);
                Assert.That(refused.Diagnostics.Count, Is.GreaterThan(0),
                    "a refused edit must carry the smallest known conflicting set (P-052)");

                var registry = new DiagnosticRegistry(16);
                var keys = new List<DiagnosticKey>();
                for (int i = 0; i < refused.Diagnostics.Count; i++)
                {
                    keys.Add(registry.Register(refused.Diagnostics[i]));
                }

                int distinct = DistinctKeyCount(keys);
                Assert.That(distinct, Is.GreaterThan(0));
                Assert.That(registry.Count, Is.EqualTo(distinct),
                    "structurally identical diagnostics share one retained row (P-052)");
                Assert.That(registry.RegisteredCount, Is.EqualTo(keys.Count));
                Assert.That(registry.DroppedCount, Is.EqualTo(0));
                Assert.That(registry.InternedCount, Is.EqualTo(keys.Count - distinct));

                for (int i = 0; i < keys.Count; i++)
                {
                    Assert.That(keys[i].IsNone, Is.False, "a real diagnostic interns to a real key (P-052)");
                    Assert.That(registry.TryGet(keys[i], out DiagnosticEnvelope? stored), Is.True);
                    Assert.That(stored, Is.Not.Null);
                    Assert.That(stored!.Key.Equals(keys[i]), Is.True);
                    Assert.That(stored.Code, Is.EqualTo(refused.Diagnostics[i].Code));
                    Assert.That(stored.CodeText.Length, Is.GreaterThan(0));
                    Assert.That(stored.CodeText, Is.EqualTo(refused.Diagnostics[i].CodeText));
                    Assert.That(stored.Phase, Is.EqualTo(refused.Diagnostics[i].Phase));
                    Assert.That(stored.Operation.Equals(refused.Diagnostics[i].Operation), Is.True);
                    Assert.That(stored.PlanHash.Equals(refused.Diagnostics[i].PlanHash), Is.True);
                    Assert.That(stored.Count, Is.EqualTo(refused.Diagnostics[i].Count));
                    Assert.That(stored.BudgetLimit, Is.EqualTo(refused.Diagnostics[i].BudgetLimit));
                    Assert.That(stored.Retry, Is.EqualTo(refused.Diagnostics[i].Retry));
                    Assert.That(
                        stored.CanonicalHash().Equals(DiagnosticEnvelope.From(refused.Diagnostics[i], keys[i]).CanonicalHash()),
                        Is.True,
                        "the retained payload is the typed fact the diagnostic carried (P-052)");
                    Assert.That(stored.InvolvedIds.Count, Is.EqualTo(refused.Diagnostics[i].InvolvedIds.Count));
                    Assert.That(stored.InvolvedKeys.Count, Is.EqualTo(refused.Diagnostics[i].InvolvedKeys.Count));
                    Assert.That(stored.CanonicalHash().IsEmpty, Is.False);
                }

                // Registering the same fact again returns the same key and reuses the retained row.
                int beforeInterns = registry.InternedCount;
                for (int i = 0; i < refused.Diagnostics.Count; i++)
                {
                    DiagnosticKey again = registry.Register(refused.Diagnostics[i]);
                    Assert.That(again.Equals(keys[i]), Is.True,
                        "the same structured fact interns to one key (P-052)");
                }

                Assert.That(registry.InternedCount, Is.EqualTo(beforeInterns + refused.Diagnostics.Count));
                Assert.That(registry.Count, Is.EqualTo(distinct), "interning adds no row");

                // Ordering reads typed fields only: two independent permutations agree exactly.
                IReadOnlyList<DiagnosticEnvelope> entries = registry.Entries();
                Assert.That(entries.Count, Is.EqualTo(keys.Count));
                IReadOnlyList<DiagnosticEnvelope> reversed = Reverse(entries);
                IReadOnlyList<DiagnosticEnvelope> ordered = DiagnosticPrecedence.Order(entries);
                IReadOnlyList<DiagnosticEnvelope> orderedReversed = DiagnosticPrecedence.Order(reversed);
                Assert.That(ordered.Count, Is.EqualTo(orderedReversed.Count));
                for (int i = 0; i < ordered.Count; i++)
                {
                    Assert.That(ordered[i].Key.Equals(orderedReversed[i].Key), Is.True,
                        "the canonical order must not depend on the input permutation (P-008, P-052)");
                }

                Assert.That(registry.OrderedEntries().Count, Is.EqualTo(ordered.Count));
                for (int i = 0; i < ordered.Count; i++)
                {
                    Assert.That(registry.OrderedEntries()[i].Key.Equals(ordered[i].Key), Is.True);
                }

                // Two diagnostics that differ only in wording are the same fact: same position, same canonical
                // hash, and the order is unaffected by the formatting.
                DiagnosticEnvelope first = entries[0];
                var reworded = new DiagnosticEnvelope(
                    first.Key,
                    first.Code,
                    first.Phase,
                    first.Operation,
                    first.PlanHash,
                    first.InvolvedIds,
                    first.InvolvedKeys,
                    first.Count,
                    first.BudgetLimit,
                    first.Retry,
                    first.Summary + " (wording changed)");
                Assert.That(DiagnosticPrecedence.SamePosition(first, reworded), Is.True,
                    "the summary is never an input to precedence (P-052)");
                Assert.That(DiagnosticPrecedence.Compare(first, reworded), Is.EqualTo(0));
                Assert.That(reworded.CanonicalHash().Equals(first.CanonicalHash()), Is.True,
                    "two diagnostics differing only in wording are one fact");
                Assert.That(reworded.Format().Equals(first.Format()), Is.False,
                    "the formatted line differs, and that difference changes nothing the order reads");

                var withReworded = new List<DiagnosticEnvelope>(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    withReworded.Add(i == 0 ? reworded : entries[i]);
                }

                IReadOnlyList<DiagnosticEnvelope> orderedReworded = DiagnosticPrecedence.Order(Reverse(withReworded));
                Assert.That(orderedReworded.Count, Is.EqualTo(ordered.Count));
                for (int i = 0; i < ordered.Count; i++)
                {
                    Assert.That(orderedReworded[i].CanonicalHash().Equals(ordered[i].CanonicalHash()), Is.True,
                        "Format() output is irrelevant to the canonical order (P-052: logging never changes "
                        + "precedence)");
                }

                // The feed records the real publication/rejection payloads and resolves them by the same keys.
                AssertFeedRecordsRealEvents(world, registry, refused, keys[0]);
            }
        }

        /// <summary>
        /// The feed records one real publication and one real rejection: both resolve by their retrieval key, and
        /// the records carry the same operation, revision/epoch pair, plan hash, affected counts and diagnostic keys
        /// the lane itself reported (P-026, P-052).
        /// </summary>
        private static void AssertFeedRecordsRealEvents(
            ObservationFamilyWorld world,
            DiagnosticRegistry registry,
            RefusedEdit refused,
            DiagnosticKey firstKey)
        {
            var feed = new CompositionDiagnosticFeed(registry, 8);
            Assert.That(feed.Registry, Is.SameAs(registry));
            Assert.That(feed.RecordCount, Is.EqualTo(0));

            DerivedAssemblyReport report = world.LastPlannedReport
                ?? throw new InvalidOperationException(
                    "the " + world.Family + " harness published no planned assembly to record a publication for");
            PlannedPublication plan = report.Plan!;
            ChangePlan changePlan = plan.Plan;
            Assert.That(changePlan.PlanHash.IsEmpty, Is.False, "a planned publication carries its own plan hash");

            var published = new CompositionPublishedEvent(
                changePlan.Operation,
                changePlan.BaseRevision,
                world.Lane.Committed.Revision,
                changePlan.BaseEpoch,
                world.Lane.Committed.Epoch,
                changePlan.PlanHash,
                changePlan.Validity.Affected);

            PublicationDiagnostic publication = feed.RecordPublished(published);
            Assert.That(feed.PublicationCount, Is.EqualTo(1));
            Assert.That(feed.RejectionCount, Is.EqualTo(0));
            Assert.That(feed.RecordCount, Is.EqualTo(1));
            Assert.That(feed.DroppedRecordCount, Is.EqualTo(0));
            Assert.That(publication.Key.IsNone, Is.False);
            Assert.That(publication.Operation.Equals(published.Operation), Is.True);
            Assert.That(publication.OldRevision.Equals(published.OldRevision), Is.True);
            Assert.That(publication.NewRevision.Equals(published.NewRevision), Is.True);
            Assert.That(publication.OldEpoch.Equals(published.OldEpoch), Is.True);
            Assert.That(publication.NewEpoch.Equals(published.NewEpoch), Is.True);
            Assert.That(publication.PlanHash.Equals(published.PlanHash), Is.True);
            Assert.That(publication.Counts.Targets, Is.EqualTo(published.Counts.Targets));
            Assert.That(publication.Counts.Installs, Is.EqualTo(published.Counts.Installs));
            Assert.That(
                publication.Counts.ContributionsAdded, Is.EqualTo(published.Counts.ContributionsAdded));
            Assert.That(
                publication.Counts.ContributionsRetracted, Is.EqualTo(published.Counts.ContributionsRetracted));
            Assert.That(publication.Counts.Stages, Is.EqualTo(published.Counts.Stages));
            Assert.That(feed.TryGetPublication(publication.Key, out PublicationDiagnostic? resolved), Is.True);
            Assert.That(resolved, Is.Not.Null);
            Assert.That(resolved!.PlanHash.Equals(changePlan.PlanHash), Is.True);
            Assert.That(resolved.Counts.Targets, Is.EqualTo(changePlan.Validity.Affected.Targets));

            // An unrecorded key resolves to nothing rather than to a stale record.
            Assert.That(feed.TryGetPublication(DiagnosticKey.None, out PublicationDiagnostic? _), Is.False);

            var rejected = new CompositionRejectedEvent(
                refused.Operation, refused.PlanHash, refused.Diagnostics);
            RejectionDiagnostic rejection = feed.RecordRejected(rejected);
            Assert.That(feed.RejectionCount, Is.EqualTo(1));
            Assert.That(feed.RecordCount, Is.EqualTo(2));
            Assert.That(rejection.Key.IsNone, Is.False);
            Assert.That(rejection.Operation.Equals(refused.Operation), Is.True);
            Assert.That(rejection.PlanHash.Equals(refused.PlanHash), Is.True);
            Assert.That(rejection.Code, Is.EqualTo(refused.Diagnostics[0].Code));
            Assert.That(rejection.CodeText, Is.EqualTo(refused.Diagnostics[0].CodeText));
            Assert.That(rejection.DiagnosticKeys.Count, Is.EqualTo(refused.Diagnostics.Count));
            Assert.That(rejection.DiagnosticCodes.Count, Is.EqualTo(refused.Diagnostics.Count));
            for (int i = 0; i < rejected.Diagnostics.Count; i++)
            {
                Assert.That(rejection.DiagnosticCodes[i], Is.EqualTo(rejected.Diagnostics[i].Code));
                DiagnosticKey key = rejection.DiagnosticKeys[i];
                Assert.That(registry.TryGet(key, out DiagnosticEnvelope? interned), Is.True,
                    "every rejection key resolves in the shared registry (P-052)");
                Assert.That(interned, Is.Not.Null);
                Assert.That(interned!.Code, Is.EqualTo(rejected.Diagnostics[i].Code));
                Assert.That(interned.Operation.Equals(refused.Operation), Is.True);
            }

            Assert.That(rejection.DiagnosticKeys[0].Equals(firstKey), Is.True,
                "the feed interned the same first diagnostic the test registered, so both share one key");

            Assert.That(feed.TryGetRejection(rejection.Key, out RejectionDiagnostic? resolvedRejection), Is.True);
            Assert.That(resolvedRejection, Is.Not.Null);
            Assert.That(resolvedRejection!.DiagnosticKeys.Count, Is.EqualTo(refused.Diagnostics.Count));
            Assert.That(feed.TryGetRejection(DiagnosticKey.None, out RejectionDiagnostic? _), Is.False);

            Assert.That(feed.Publications().Count, Is.EqualTo(1));
            Assert.That(feed.Rejections().Count, Is.EqualTo(1));
            Assert.That(feed.Publications()[0].Key.Equals(publication.Key), Is.True);
            Assert.That(feed.Rejections()[0].Key.Equals(rejection.Key), Is.True);

            // The feed is an observer of the real lane interface and records whatever the lane reports.
            var observer = (ICompositionObserver)feed;
            observer.OnCompositionPublished(published);
            observer.OnCompositionRejected(rejected);
            Assert.That(feed.PublicationCount, Is.EqualTo(2));
            Assert.That(feed.RejectionCount, Is.EqualTo(2));
            Assert.That(feed.RecordCount, Is.EqualTo(2),
                "recording the same typed event again reuses its record instead of adding a row");
            Assert.That(feed.DroppedRecordCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Submits edits until the family's lane really refuses one and the refusal carries structured diagnostics.
        /// Every candidate is a real edit of the family's own declared surface; the code of each attempt is reported
        /// when none is refused, so a failure names what the lane really answered.
        /// </summary>
        private static RefusedEdit RefuseOneEdit(ObservationFamilyWorld world)
        {
            var attempts = new List<string>();
            IReadOnlyList<CompositionEditPayload> candidates = RefusalCandidates(world);
            for (int i = 0; i < candidates.Count; i++)
            {
                CompositionEditPayload payload = candidates[i];
                OperationId operation = world.NextOperation();
                EditAdmission admission = world.Lane.SubmitEdit(
                    payload, operation, world.Lane.Committed.Revision);
                attempts.Add(
                    payload.Subject.ToString() + "=" + admission.Kind.ToString() + "/" + admission.Code.ToString()
                    + "[" + ObservationFamilyWorld.Describe(admission.Diagnostics) + "]");

                if (admission.Staged || admission.Diagnostics.Count == 0)
                {
                    continue;
                }

                Assert.That(admission.Code, Is.Not.EqualTo(DiagnosticCode.None));
                var diagnostics = new List<Diagnostic>(admission.Diagnostics);
                ContentHash planHash = admission.Plan != null ? admission.Plan.PlanHash() : ContentHash.Empty;
                return new RefusedEdit(operation, planHash, diagnostics);
            }

            throw new InvalidOperationException(
                "no " + world.Family + " edit was refused with structured diagnostics; the lane reported: "
                + string.Join(" | ", attempts.ToArray()));
        }

        /// <summary>
        /// The real edits this test proposes, in order: an unmount of an installation this world never mounted, and
        /// a scope creation of a scope the world definition or the market already declared. Both are ordinary
        /// declared edits, so a refusal is the lane's own decision and not a malformed payload.
        /// </summary>
        private static IReadOnlyList<CompositionEditPayload> RefusalCandidates(ObservationFamilyWorld world)
        {
            var candidates = new List<CompositionEditPayload>();
            if (string.Equals(world.Family, ObservationFamilyHarness.NarrativeFamily, StringComparison.Ordinal))
            {
                candidates.Add(Unmount(NarrativeKeys.ForwardInstall));
                candidates.Add(ScopeCreate(NarrativeKeys.VillageScope, NarrativeKeys.ChapterOneScope));
                candidates.Add(Unmount(NarrativeKeys.ChapterTwoInstall));
                return candidates;
            }

            candidates.Add(Unmount(CardTableFixture.QuietScoringInstance));
            candidates.Add(CardTablePayloads.ScopeCreate(
                CardIdentity.Scope(CardVocabulary.LeagueA), world.RootScope, false));
            candidates.Add(Unmount(CardTableFixture.RuleLibraryInstance));
            return candidates;
        }

        /// <summary>O-07 unmount of one installation (P-046), shaped exactly as the family fixtures build it.</summary>
        private static CompositionEditPayload Unmount(PluginInstanceId instance)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.InstallUnmount,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        private static int DistinctKeyCount(IReadOnlyList<DiagnosticKey> keys)
        {
            var seen = new List<DiagnosticKey>();
            for (int i = 0; i < keys.Count; i++)
            {
                bool known = false;
                for (int k = 0; k < seen.Count; k++)
                {
                    if (seen[k].Equals(keys[i]))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    seen.Add(keys[i]);
                }
            }

            return seen.Count;
        }

        /// <summary>O-02 scope creation, shaped exactly as the family fixtures build it (P-010).</summary>
        private static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        private static IReadOnlyList<DiagnosticEnvelope> Reverse(IReadOnlyList<DiagnosticEnvelope> source)
        {
            var reversed = new List<DiagnosticEnvelope>(source.Count);
            for (int i = source.Count - 1; i >= 0; i--)
            {
                reversed.Add(source[i]);
            }

            return reversed;
        }

        /// <summary>One really refused edit: the operation, its plan hash when it had one, and its diagnostics.</summary>
        private readonly struct RefusedEdit
        {
            public RefusedEdit(OperationId operation, ContentHash planHash, IReadOnlyList<Diagnostic> diagnostics)
            {
                Operation = operation;
                PlanHash = planHash;
                Diagnostics = diagnostics;
            }

            public OperationId Operation { get; }

            public ContentHash PlanHash { get; }

            public IReadOnlyList<Diagnostic> Diagnostics { get; }
        }
    }
}
