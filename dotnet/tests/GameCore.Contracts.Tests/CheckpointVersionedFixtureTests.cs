// Versioned checkpoint fixtures (GC-018). Normative sources: docs/game-core/00-core-protocols.md P-053 ("explicit
// queue/outbox disposition ... never ambiguously omitted") and P-054 ("migration paths must be unique for a
// requested source/target pair; ambiguity is rejected"; "unknown required fields/schema versions reject").
//
// The transition table and the queued-command policy matrix are committed data under
// tests/GameCore.CheckpointFixtures/Data, not literals in this file, so a build host can extend the table without
// editing C#. This test derives real identities from the fixture's stable names (StableNameKeyDerivation.Derive)
// and drives the production planner (CheckpointMigrationRegistry.Plan/TryPlanAll) and the production checkpoint
// header (HeaderRecordValue, CheckpointCounts, CheckpointFormat) with it.
//
// The queue cases are checked against the header fields a capture writes from its queue decision. The decision
// struct itself (GameCore.Execution.Persistence.QueueDisposition) and CheckpointCaptureRequest live in
// GameCore.Execution (Packages/com.gamecore.unity.runtime/Runtime/Pure/Persistence/CheckpointCapture.cs), which
// this test project's csproj deliberately does not reference (its reference set is frozen for GC-018), so the
// end-to-end capture - request policy -> disposition -> header fields -> framed Command records - is proven by
// the GC-018 Unity scenario and by the execution-side capture tests, not here. What is asserted here is that the
// committed matrix agrees with that documented rule and with the header record the capture fills from it: the
// policy field round-trips the requested policy, the header's command count equals the number of Command records
// the policy includes, its rejected count equals the number explicitly cancelled, and the two still account for
// every offered command (the "never ambiguously omitted" invariant of P-053).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>
    /// The committed GC-018 transition table and queue-policy matrix are the source of truth for these two
    /// behaviours: every case names the steps that are registered and the exact plan or disposition the
    /// production implementation must answer with.
    /// </summary>
    [TestFixture]
    public sealed class CheckpointVersionedFixtureTests
    {
        private const string MigrationFixturePath = "tests/GameCore.CheckpointFixtures/Data/migration-paths.json";
        private const string QueueFixturePath = "tests/GameCore.CheckpointFixtures/Data/queue-policy.json";
        private const string MigrationFixtureFormat = "gamecore.checkpoint-fixtures/migration-paths/1";
        private const string QueueFixtureFormat = "gamecore.checkpoint-fixtures/queue-policy/1";

        /// <summary>Key version every fixture step is registered at; one key resolves to one handler (P-009).</summary>
        private const uint FixtureStepKeyVersion = 1U;

        [Test]
        public void CommittedMigrationFixturePlansEveryDocumentedTransition()
        {
            MigrationFixture fixture = ReadMigrationFixture();

            Assert.That(fixture.Format, Is.EqualTo(MigrationFixtureFormat),
                MigrationFixturePath + " declares an unexpected fixture format.");
            Assert.That(fixture.SchemaStableName, Is.EqualTo(CheckpointFormat.DocumentSchemaStableName),
                MigrationFixturePath + ": the subject schema must be the checkpoint container document schema; a "
                + "transition table for another schema would not describe this format (P-054).");

            var subject = new SchemaId(StableNameKeyDerivation.Derive(fixture.SchemaStableName));
            Assert.That(subject, Is.EqualTo(CheckpointFormat.DocumentSchema.Id),
                MigrationFixturePath + ": the subject schema stable name must derive to "
                + "CheckpointFormat.DocumentSchema.Id (P-004).");

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenOutcomes = new HashSet<MigrationPlanOutcome>();
            int beyondChainBound = 0;
            int crossSchema = 0;

            foreach (MigrationCase migrationCase in fixture.Cases)
            {
                string context = Describe(migrationCase);
                Assert.That(seenIds.Add(migrationCase.Id), Is.True,
                    context + ": case ids must be unique in the fixture.");
                Assert.That(migrationCase.RequirementIds, Is.Not.Empty,
                    context + ": the case must name the protocol requirement it pins.");
                Assert.That(migrationCase.TestIds, Is.Not.Empty,
                    context + ": the case must name the traceability test that owns it.");
                seenOutcomes.Add(migrationCase.ExpectedOutcome);

                SchemaId destinationId = subject;
                string? targetStableName = migrationCase.TargetSchemaStableName;
                if (targetStableName != null)
                {
                    Assert.That(StableNameKeyDerivation.IsCanonicalStableName(targetStableName), Is.True,
                        context + ": toSchemaStableName must be a canonical stable name (P-004).");
                    destinationId = new SchemaId(StableNameKeyDerivation.Derive(targetStableName));
                    Assert.That(destinationId, Is.Not.EqualTo(subject),
                        context + ": a cross-schema case must name a destination schema other than the subject.");
                    crossSchema++;
                }

                var from = new SchemaRef(subject, migrationCase.FromVersion);
                var to = new SchemaRef(destinationId, migrationCase.ToVersion);

                // A version gap wider than the registry's search bound is a hard refusal (P-022, P-054).
                long span = (long)migrationCase.ToVersion - (long)migrationCase.FromVersion;
                if (span > CheckpointMigrationRegistry.MaxChainLength)
                {
                    beyondChainBound++;
                    Assert.That(migrationCase.ExpectedOutcome, Is.EqualTo(MigrationPlanOutcome.Unreachable),
                        context + ": a gap beyond MaxChainLength can only be refused as Unreachable.");
                }

                var steps = new List<ISchemaMigrationStep>();
                foreach (MigrationStep step in migrationCase.Steps)
                {
                    Assert.That(StableNameKeyDerivation.IsCanonicalStableName(step.KeyStableName), Is.True,
                        context + ": every step keyStableName must be a canonical stable name (P-004).");
                    steps.Add(new FixtureMigrationStep(
                        step.KeyStableName,
                        new SchemaRef(subject, step.FromVersion),
                        new SchemaRef(subject, step.ToVersion)));
                }

                var registry = new CheckpointMigrationRegistry(steps);
                Assert.That(registry.IsWellFormed, Is.True,
                    context + ": registration must accept every declared step; rejections: "
                    + string.Join(" | ", registry.Rejections));
                Assert.That(registry.Steps.Count, Is.EqualTo(migrationCase.Steps.Count),
                    context + ": every declared step must be registered exactly once (P-009).");

                MigrationPlan plan = registry.Plan(from, to);

                Assert.That(plan.Outcome, Is.EqualTo(migrationCase.ExpectedOutcome),
                    context + ": unexpected migration outcome; plan detail: " + plan.Detail);
                Assert.That(plan.Code, Is.EqualTo(migrationCase.ExpectedCode),
                    context + ": the refusal must carry the expected protocol code (P-052).");
                Assert.That(plan.PathCount, Is.EqualTo(migrationCase.ExpectedPathCount),
                    context + ": unexpected distinct-chain count (P-054).");
                Assert.That(plan.IsRunnable, Is.EqualTo(IsRunnableOutcome(migrationCase.ExpectedOutcome)),
                    context + ": only Current or a unique chain may be executed (P-054).");
                Assert.That(plan.SchemaId, Is.EqualTo(subject),
                    context + ": the plan is for the subject schema.");
                Assert.That(plan.From.Version, Is.EqualTo(migrationCase.FromVersion),
                    context + ": the plan must report the version the document declares.");
                Assert.That(plan.To.Version, Is.EqualTo(migrationCase.ToVersion),
                    context + ": the plan must report the version the catalog declares.");
                Assert.That(plan.Detail, Does.Contain(migrationCase.ExpectedDetailContains),
                    context + ": the detail must name the branch that produced the outcome (P-052); detail: "
                    + plan.Detail);
                Assert.That(plan.Steps.Count, Is.EqualTo(migrationCase.ExpectedStepFromVersions.Count),
                    context + ": unexpected chain length; only a unique plan carries steps (P-054).");

                for (int i = 0; i < migrationCase.ExpectedStepFromVersions.Count; i++)
                {
                    Assert.That(plan.Steps[i].From.Version, Is.EqualTo(migrationCase.ExpectedStepFromVersions[i]),
                        context + ": step " + i.ToString(CultureInfo.InvariantCulture)
                        + " must read the expected version, and the chain must be in execution order (P-054).");
                }

                if (plan.Steps.Count != 0)
                {
                    Assert.That(plan.Steps[0].From.Version, Is.EqualTo(migrationCase.FromVersion),
                        context + ": the chain must start at the version the document declares (P-054).");
                    Assert.That(plan.Steps[plan.Steps.Count - 1].To.Version, Is.EqualTo(migrationCase.ToVersion),
                        context + ": the chain must end at the version the catalog declares (P-054).");
                }

                // TryPlanAll answers the same question for a whole restore: it plans a runnable pair and refuses a
                // non-runnable one with the first refusal, so one reason is reported instead of a list (P-052).
                var captured = new List<SchemaRef> { from };
                var allocated = new List<SchemaRef> { to };
                bool planned = registry.TryPlanAll(
                    captured, allocated, out IReadOnlyList<MigrationPlan> plans, out MigrationPlan? refused,
                    out string allDetail);

                Assert.That(planned, Is.EqualTo(plan.IsRunnable),
                    context + ": TryPlanAll must accept exactly the pairs whose plan is runnable.");
                if (planned)
                {
                    int expectedPlans = migrationCase.ExpectedOutcome == MigrationPlanOutcome.Unique ? 1 : 0;
                    Assert.That(plans.Count, Is.EqualTo(expectedPlans),
                        context + ": TryPlanAll must return one plan per pair that needs a migration.");
                    for (int i = 0; i < plans.Count; i++)
                    {
                        Assert.That(plans[i].Outcome, Is.EqualTo(migrationCase.ExpectedOutcome),
                            context + ": every planned pair must carry the expected outcome.");
                    }
                }
                else
                {
                    Assert.That(refused, Is.Not.Null,
                        context + ": a refused TryPlanAll must report the refusal; detail: " + allDetail);
                    Assert.That(refused!.Outcome, Is.EqualTo(migrationCase.ExpectedOutcome),
                        context + ": TryPlanAll must refuse with the same outcome as Plan.");
                    Assert.That(refused!.PathCount, Is.EqualTo(migrationCase.ExpectedPathCount),
                        context + ": TryPlanAll must report the same distinct-chain count.");
                    Assert.That(plans, Is.Empty,
                        context + ": a refused TryPlanAll must return no plans.");
                }
            }

            // The fixture only documents something if it covers every outcome and the two structural refusals.
            foreach (MigrationPlanOutcome outcome in new[]
            {
                MigrationPlanOutcome.Current,
                MigrationPlanOutcome.Unique,
                MigrationPlanOutcome.Ambiguous,
                MigrationPlanOutcome.Unreachable,
                MigrationPlanOutcome.UnknownSchema,
            })
            {
                Assert.That(seenOutcomes, Does.Contain(outcome),
                    MigrationFixturePath + ": the transition table must document the '" + outcome
                    + "' outcome (P-054).");
            }

            Assert.That(beyondChainBound, Is.EqualTo(1),
                MigrationFixturePath + ": the table must document exactly one case whose version gap exceeds "
                + "CheckpointMigrationRegistry.MaxChainLength (P-022).");
            Assert.That(crossSchema, Is.EqualTo(1),
                MigrationFixturePath + ": the table must document exactly one cross-schema request (P-054).");
        }

        [Test]
        public void CommittedQueuePolicyFixtureMatchesTheHeaderEveryCaptureWrites()
        {
            QueueFixture fixture = ReadQueueFixture();

            Assert.That(fixture.Format, Is.EqualTo(QueueFixtureFormat),
                QueueFixturePath + " declares an unexpected fixture format.");

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var covered = new HashSet<string>(StringComparer.Ordinal);

            foreach (QueueCase queueCase in fixture.Cases)
            {
                string context = Describe(queueCase);
                Assert.That(seenIds.Add(queueCase.Id), Is.True,
                    context + ": case ids must be unique in the fixture.");
                Assert.That(queueCase.RequirementIds, Is.Not.Empty,
                    context + ": the case must name the protocol requirement it pins.");
                Assert.That(queueCase.TestIds, Is.Not.Empty,
                    context + ": the case must name the traceability test that owns it.");
                Assert.That(queueCase.OfferedCommands, Is.GreaterThanOrEqualTo(0),
                    context + ": offeredCommands is a count of queued commands.");
                Assert.That(queueCase.ExpectedIncluded, Is.GreaterThanOrEqualTo(0),
                    context + ": expectedIncluded is a count of commands.");
                Assert.That(queueCase.ExpectedRejected, Is.GreaterThanOrEqualTo(0),
                    context + ": expectedRejected is a count of commands.");

                // The capture's rule (P-053, 06 s7): IncludeQueued records every offered command, RejectQueued
                // cancels every one and counts it; a command is never silently omitted.
                int included = queueCase.Policy == CheckpointQueuePolicy.IncludeQueued ? queueCase.OfferedCommands : 0;
                int rejected = queueCase.OfferedCommands - included;

                Assert.That(queueCase.ExpectedIncluded, Is.EqualTo(included),
                    context + ": the fixture's included count must follow the policy rule (P-053).");
                Assert.That(queueCase.ExpectedRejected, Is.EqualTo(rejected),
                    context + ": the fixture's rejected count must follow the policy rule (P-053).");
                Assert.That(queueCase.ExpectedIncluded + queueCase.ExpectedRejected,
                    Is.EqualTo(queueCase.OfferedCommands),
                    context + ": every offered command must be either included or explicitly rejected "
                    + "(QueueDisposition.IsAccountedFor, P-053).");
                Assert.That(queueCase.ExpectedHeaderCommandCount, Is.EqualTo(queueCase.ExpectedIncluded),
                    context + ": the header declares one Command record per included command (P-053).");

                // The header a capture builds from that decision: policy field, rejected count, command count.
                HeaderRecordValue header = BuildHeader(queueCase);

                Assert.That(header.Policy, Is.EqualTo(queueCase.Policy),
                    context + ": the header's policy field must round-trip the requested CheckpointQueuePolicy "
                    + "(the capture writes (uint)request.QueuePolicy).");
                Assert.That((long)header.CommandCount, Is.EqualTo((long)queueCase.ExpectedHeaderCommandCount),
                    context + ": the header's command count is the number of Command records written (P-053).");
                Assert.That((long)header.RejectedQueuedCount, Is.EqualTo((long)queueCase.ExpectedRejected),
                    context + ": the header records how many queued commands were explicitly rejected (P-053).");
                Assert.That((long)header.CommandCount + (long)header.RejectedQueuedCount,
                    Is.EqualTo((long)queueCase.OfferedCommands),
                    context + ": the header itself must account for every offered command (P-053).");

                // The document reader compares the header's declaration with the records actually framed; the
                // count the policy produced must be the count a document carrying those records declares.
                CheckpointCounts counts = BuildCounts(queueCase.ExpectedIncluded);
                Assert.That(counts.Of(CheckpointRecordKind.Command), Is.EqualTo(queueCase.ExpectedHeaderCommandCount),
                    context + ": the document's Command count must equal the header's command count (P-053).");
                Assert.That(header.CountsMatch(
                        0, 0, 0, 0, 0, 0, 0, counts.Commands, 0, 0, 0), Is.True,
                    context + ": the header must match a document that carries exactly the included commands.");
                Assert.That(header.CountsMatch(
                        0, 0, 0, 0, 0, 0, 0, counts.Commands + 1, 0, 0, 0), Is.False,
                    context + ": a header declaring a command the document does not carry must not match (P-053).");

                // The included commands are framed as Command records, the kind the header counts (P-054).
                int commandFieldId = CheckpointFormat.FieldIdOf(CheckpointRecordKind.Command);
                Assert.That(CheckpointFormat.TryKindOfField(commandFieldId, out CheckpointRecordKind kind), Is.True,
                    context + ": field id " + commandFieldId.ToString(CultureInfo.InvariantCulture)
                    + " must name a declared record kind.");
                Assert.That(kind, Is.EqualTo(CheckpointRecordKind.Command),
                    context + ": the included commands are framed as Command records (P-053).");

                covered.Add(PolicyToken(queueCase.Policy) + "/"
                    + queueCase.OfferedCommands.ToString(CultureInfo.InvariantCulture));
            }

            // A fixture that documents nothing fails: both policies must be pinned at 0, 1 and 3 commands.
            foreach (string policy in new[] { "RejectQueued", "IncludeQueued" })
            {
                foreach (int offered in new[] { 0, 1, 3 })
                {
                    Assert.That(covered,
                        Does.Contain(policy + "/" + offered.ToString(CultureInfo.InvariantCulture)),
                        QueueFixturePath + ": the matrix must document policy " + policy + " at "
                        + offered.ToString(CultureInfo.InvariantCulture) + " offered command(s).");
                }
            }
        }

        /// <summary>
        /// The policy ordinals are part of the format, not an implementation detail: the header stores the ordinal
        /// and a reader casts it back, so these two values cannot drift without breaking committed documents
        /// (P-054).
        /// </summary>
        [Test]
        public void QueuePolicyOrdinalsAreTheFrozenFormatValues()
        {
            Assert.That((int)CheckpointQueuePolicy.RejectQueued, Is.EqualTo(0),
                "RejectQueued must stay the default policy ordinal (P-053, 06 s7).");
            Assert.That((int)CheckpointQueuePolicy.IncludeQueued, Is.EqualTo(1),
                "IncludeQueued must stay the second policy ordinal (P-053).");
        }

        private static string Describe(MigrationCase migrationCase) =>
            MigrationFixturePath + " case '" + migrationCase.Id + "' (" + migrationCase.Title + ")";

        private static string Describe(QueueCase queueCase) =>
            QueueFixturePath + " case '" + queueCase.Id + "' (" + queueCase.Title + ")";

        private static string PolicyToken(CheckpointQueuePolicy policy) =>
            policy == CheckpointQueuePolicy.IncludeQueued ? "IncludeQueued" : "RejectQueued";

        /// <summary>The documented executability rule (P-054): only Current or a unique chain may be executed.</summary>
        private static bool IsRunnableOutcome(MigrationPlanOutcome outcome) =>
            outcome == MigrationPlanOutcome.Current || outcome == MigrationPlanOutcome.Unique;

        /// <summary>The header fields the capture writes from one queue case's policy decision (P-053, 06 s7).</summary>
        private static HeaderRecordValue BuildHeader(QueueCase queueCase) =>
            new HeaderRecordValue(
                0x1111111111111111UL,
                0x2222222222222222UL,
                0x3333333333333333UL,
                0x4444444444444444UL,
                CheckpointFormat.ProtocolMajor,
                CheckpointFormat.ProtocolMinor,
                (uint)TemporalModel.CommandDriven,
                5UL,
                60UL,
                4U,
                true,
                7UL,
                3UL,
                12.5d,
                9UL,
                (uint)PropagationMode.Conservative,
                0x5555555555555555UL,
                0x6666666666666666UL,
                0x7777777777777777UL,
                0x8888888888888888UL,
                (uint)queueCase.Policy,
                11UL,
                (uint)queueCase.ExpectedRejected,
                13UL,
                0U,
                0U,
                0U,
                0U,
                0U,
                0U,
                0U,
                (uint)queueCase.ExpectedHeaderCommandCount,
                0U,
                0U,
                0U,
                15UL,
                16UL,
                17UL,
                0U);

        /// <summary>A document carrying exactly <paramref name="commands"/> Command records and nothing else.</summary>
        private static CheckpointCounts BuildCounts(int commands) =>
            new CheckpointCounts(0, 0, 0, 0, 0, 0, 0, commands, 0, 0, 0);

        private static MigrationFixture ReadMigrationFixture()
        {
            using (JsonDocument document = ReadFixtureDocument(MigrationFixturePath))
            {
                JsonElement root = document.RootElement;
                string format = RequireString(root, "format", MigrationFixturePath);
                string schemaStableName = RequireString(root, "schemaStableName", MigrationFixturePath);
                var cases = new List<MigrationCase>();

                foreach (JsonElement element in RequireArray(root, "cases", MigrationFixturePath).EnumerateArray())
                {
                    cases.Add(ParseMigrationCase(element, cases.Count));
                }

                CheckNonEmpty(cases.Count, MigrationFixturePath + ": 'cases' must document at least one transition.");
                return new MigrationFixture(format, schemaStableName, cases);
            }
        }

        private static QueueFixture ReadQueueFixture()
        {
            using (JsonDocument document = ReadFixtureDocument(QueueFixturePath))
            {
                JsonElement root = document.RootElement;
                string format = RequireString(root, "format", QueueFixturePath);
                var cases = new List<QueueCase>();

                foreach (JsonElement element in RequireArray(root, "cases", QueueFixturePath).EnumerateArray())
                {
                    cases.Add(ParseQueueCase(element, cases.Count));
                }

                CheckNonEmpty(cases.Count, QueueFixturePath + ": 'cases' must document at least one policy case.");
                return new QueueFixture(format, cases);
            }
        }

        private static MigrationCase ParseMigrationCase(JsonElement element, int index)
        {
            string context = MigrationFixturePath + " case " + index.ToString(CultureInfo.InvariantCulture);
            string id = RequireString(element, "id", context);
            context = MigrationFixturePath + " case '" + id + "'";

            string title = RequireString(element, "title", context);
            IReadOnlyList<string> requirementIds = RequireStringArray(element, "requirementIds", context, true);
            IReadOnlyList<string> testIds = RequireStringArray(element, "testIds", context, true);
            string? targetSchemaStableName = OptionalString(element, "toSchemaStableName");

            var steps = new List<MigrationStep>();
            foreach (JsonElement step in RequireArray(element, "steps", context).EnumerateArray())
            {
                string stepContext = context + " step " + steps.Count.ToString(CultureInfo.InvariantCulture);
                steps.Add(new MigrationStep(
                    RequireString(step, "keyStableName", stepContext),
                    RequireVersion(step, "fromVersion", stepContext),
                    RequireVersion(step, "toVersion", stepContext)));
            }

            uint fromVersion = RequireVersion(element, "fromVersion", context);
            uint toVersion = RequireVersion(element, "toVersion", context);
            MigrationPlanOutcome outcome = RequireOutcome(element, "expectedOutcome", context);
            DiagnosticCode code = RequireCode(element, "expectedCode", context);
            int pathCount = RequireInt(element, "expectedPathCount", context);

            var chain = new List<uint>();
            foreach (JsonElement version in RequireArray(element, "expectedStepFromVersions", context)
                .EnumerateArray())
            {
                if (!version.TryGetUInt32(out uint value))
                {
                    Assert.Fail(context + ": 'expectedStepFromVersions' must contain unsigned integers.");
                    continue;
                }

                chain.Add(value);
            }

            string detail = RequireString(element, "expectedDetailContains", context);
            return new MigrationCase(
                id, title, requirementIds, testIds, targetSchemaStableName, steps, fromVersion, toVersion,
                outcome, code, pathCount, chain, detail);
        }

        private static QueueCase ParseQueueCase(JsonElement element, int index)
        {
            string context = QueueFixturePath + " case " + index.ToString(CultureInfo.InvariantCulture);
            string id = RequireString(element, "id", context);
            context = QueueFixturePath + " case '" + id + "'";

            string title = RequireString(element, "title", context);
            IReadOnlyList<string> requirementIds = RequireStringArray(element, "requirementIds", context, true);
            IReadOnlyList<string> testIds = RequireStringArray(element, "testIds", context, true);
            int offered = RequireInt(element, "offeredCommands", context);
            CheckpointQueuePolicy policy = RequirePolicy(element, "policy", context);
            int included = RequireInt(element, "expectedIncluded", context);
            int rejected = RequireInt(element, "expectedRejected", context);
            int headerCommandCount = RequireInt(element, "expectedHeaderCommandCount", context);
            return new QueueCase(
                id, title, requirementIds, testIds, offered, policy, included, rejected, headerCommandCount);
        }

        /// <summary>
        /// Opens one committed fixture. A missing file is reported as a failed assertion with the searched path,
        /// not as an IO exception, because a fixture that was never committed is a test-data defect (P-054).
        /// </summary>
        private static JsonDocument ReadFixtureDocument(string relativePath)
        {
            string root = RepoLayout.FindRoot();
            string path = RepoLayout.Resolve(root, relativePath);
            if (!File.Exists(path))
            {
                Assert.Fail("The committed checkpoint fixture is missing: " + path + " (expected the repository "
                    + "root reported by RepoLayout.FindRoot to contain " + relativePath + ").");
            }

            // Assert.Fail throws an AssertionException, so the read below is reached only when the file exists.
            return JsonDocument.Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Reads a required non-empty string property, with the canonical stable-name rule applied by the caller
        /// where the text names a schema or a key (P-004).
        /// </summary>
        private static string RequireString(JsonElement element, string name, string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                Assert.Fail(context + ": '" + name + "' must be a non-empty string.");
                return string.Empty;
            }

            string? text = value.GetString();
            if (string.IsNullOrEmpty(text))
            {
                Assert.Fail(context + ": '" + name + "' must be a non-empty string.");
                return string.Empty;
            }

            return text;
        }

        /// <summary>Reads an optional string property; null means the property is absent.</summary>
        private static string? OptionalString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                Assert.Fail("'" + name + "' must be a string when present.");
                return null;
            }

            return value.GetString();
        }

        private static JsonElement RequireArray(JsonElement element, string name, string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                Assert.Fail(context + ": '" + name + "' must be an array.");
                return default(JsonElement);
            }

            return value;
        }

        /// <summary>
        /// Reads a string array. When <paramref name="requireDocumented"/> is set the array must name at least one
        /// requirement or test id, so a case that documents nothing fails instead of passing silently.
        /// </summary>
        private static IReadOnlyList<string> RequireStringArray(
            JsonElement element,
            string name,
            string context,
            bool requireDocumented)
        {
            var values = new List<string>();
            foreach (JsonElement item in RequireArray(element, name, context).EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    Assert.Fail(context + ": every '" + name + "' element must be a string.");
                    continue;
                }

                string? text = item.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    Assert.Fail(context + ": every '" + name + "' element must be a non-empty string.");
                    continue;
                }

                values.Add(text);
            }

            if (requireDocumented && values.Count == 0)
            {
                Assert.Fail(context + ": '" + name + "' must name at least one requirement or test id; a case "
                    + "that documents nothing is a fixture defect.");
            }

            return values;
        }

        private static int RequireInt(JsonElement element, string name, string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number))
            {
                Assert.Fail(context + ": '" + name + "' must be an integer.");
                return 0;
            }

            return number;
        }

        private static uint RequireVersion(JsonElement element, string name, string context)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || !value.TryGetUInt32(out uint version))
            {
                Assert.Fail(context + ": '" + name + "' must be an unsigned integer schema version (P-054).");
                return 0U;
            }

            return version;
        }

        private static MigrationPlanOutcome RequireOutcome(JsonElement element, string name, string context)
        {
            string text = RequireString(element, name, context);
            if (!Enum.TryParse<MigrationPlanOutcome>(text, false, out MigrationPlanOutcome outcome)
                || !Enum.IsDefined(typeof(MigrationPlanOutcome), outcome))
            {
                Assert.Fail(context + ": '" + name + "' names no MigrationPlanOutcome: '" + text + "'.");
            }

            return outcome;
        }

        private static DiagnosticCode RequireCode(JsonElement element, string name, string context)
        {
            string text = RequireString(element, name, context);
            if (!Enum.TryParse<DiagnosticCode>(text, false, out DiagnosticCode code)
                || !Enum.IsDefined(typeof(DiagnosticCode), code))
            {
                Assert.Fail(context + ": '" + name + "' names no DiagnosticCode: '" + text + "'.");
            }

            return code;
        }

        private static CheckpointQueuePolicy RequirePolicy(JsonElement element, string name, string context)
        {
            string text = RequireString(element, name, context);
            if (!Enum.TryParse<CheckpointQueuePolicy>(text, false, out CheckpointQueuePolicy policy)
                || !Enum.IsDefined(typeof(CheckpointQueuePolicy), policy))
            {
                Assert.Fail(context + ": '" + name + "' names no CheckpointQueuePolicy: '" + text + "'.");
            }

            return policy;
        }

        private static void CheckNonEmpty(int count, string message)
        {
            if (count == 0)
            {
                Assert.Fail(message);
            }
        }

        /// <summary>
        /// One registered directed migration step, built exactly as a generated catalog registers one: the key is
        /// derived from the step's stable name and both ends name the case's schema (P-004, P-054).
        /// </summary>
        private sealed class FixtureMigrationStep : ISchemaMigrationStep
        {
            internal FixtureMigrationStep(string keyStableName, SchemaRef from, SchemaRef to)
            {
                Key = new FactoryKey(
                    StableNameKeyDerivation.Derive(keyStableName), FixtureStepKeyVersion);
                From = from;
                To = to;
            }

            public FactoryKey Key { get; }

            public SchemaRef From { get; }

            public SchemaRef To { get; }
        }

        private sealed class MigrationFixture
        {
            internal MigrationFixture(string format, string schemaStableName, IReadOnlyList<MigrationCase> cases)
            {
                Format = format;
                SchemaStableName = schemaStableName;
                Cases = cases;
            }

            internal string Format { get; }

            internal string SchemaStableName { get; }

            internal IReadOnlyList<MigrationCase> Cases { get; }
        }

        private sealed class MigrationStep
        {
            internal MigrationStep(uint fromVersion, uint toVersion, string keyStableName)
            {
                FromVersion = fromVersion;
                ToVersion = toVersion;
                KeyStableName = keyStableName;
            }

            internal uint FromVersion { get; }

            internal uint ToVersion { get; }

            internal string KeyStableName { get; }
        }

        private sealed class MigrationCase
        {
            internal MigrationCase(
                string id,
                string title,
                IReadOnlyList<string> requirementIds,
                IReadOnlyList<string> testIds,
                string? targetSchemaStableName,
                IReadOnlyList<MigrationStep> steps,
                uint fromVersion,
                uint toVersion,
                MigrationPlanOutcome expectedOutcome,
                DiagnosticCode expectedCode,
                int expectedPathCount,
                IReadOnlyList<uint> expectedStepFromVersions,
                string expectedDetailContains)
            {
                Id = id;
                Title = title;
                RequirementIds = requirementIds;
                TestIds = testIds;
                TargetSchemaStableName = targetSchemaStableName;
                Steps = steps;
                FromVersion = fromVersion;
                ToVersion = toVersion;
                ExpectedOutcome = expectedOutcome;
                ExpectedCode = expectedCode;
                ExpectedPathCount = expectedPathCount;
                ExpectedStepFromVersions = expectedStepFromVersions;
                ExpectedDetailContains = expectedDetailContains;
            }

            internal string Id { get; }

            internal string Title { get; }

            internal IReadOnlyList<string> RequirementIds { get; }

            internal IReadOnlyList<string> TestIds { get; }

            internal string? TargetSchemaStableName { get; }

            internal IReadOnlyList<MigrationStep> Steps { get; }

            internal uint FromVersion { get; }

            internal uint ToVersion { get; }

            internal MigrationPlanOutcome ExpectedOutcome { get; }

            internal DiagnosticCode ExpectedCode { get; }

            internal int ExpectedPathCount { get; }

            internal IReadOnlyList<uint> ExpectedStepFromVersions { get; }

            internal string ExpectedDetailContains { get; }
        }

        private sealed class QueueFixture
        {
            internal QueueFixture(string format, IReadOnlyList<QueueCase> cases)
            {
                Format = format;
                Cases = cases;
            }

            internal string Format { get; }

            internal IReadOnlyList<QueueCase> Cases { get; }
        }

        private sealed class QueueCase
        {
            internal QueueCase(
                string id,
                string title,
                IReadOnlyList<string> requirementIds,
                IReadOnlyList<string> testIds,
                int offeredCommands,
                CheckpointQueuePolicy policy,
                int expectedIncluded,
                int expectedRejected,
                int expectedHeaderCommandCount)
            {
                Id = id;
                Title = title;
                RequirementIds = requirementIds;
                TestIds = testIds;
                OfferedCommands = offeredCommands;
                Policy = policy;
                ExpectedIncluded = expectedIncluded;
                ExpectedRejected = expectedRejected;
                ExpectedHeaderCommandCount = expectedHeaderCommandCount;
            }

            internal string Id { get; }

            internal string Title { get; }

            internal IReadOnlyList<string> RequirementIds { get; }

            internal IReadOnlyList<string> TestIds { get; }

            internal int OfferedCommands { get; }

            internal CheckpointQueuePolicy Policy { get; }

            internal int ExpectedIncluded { get; }

            internal int ExpectedRejected { get; }

            internal int ExpectedHeaderCommandCount { get; }
        }
    }
}
