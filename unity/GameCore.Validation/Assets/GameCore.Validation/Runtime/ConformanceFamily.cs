// GameCore.Validation.ProbeHost — the GC-024 conformance family contract and the real world one table runs in.
//
// WHAT THIS ADDS ON TOP OF `IW6Family`
//
// The Wave 6 family contract already declares everything one real world of a genre needs: its catalog, its lane
// seed, its live targets, its providers and the payload builders one mount/unmount cycle uses. GC-024 needs exactly
// two things more, and they are the two things that make a table *observed* rather than merely *run*:
//
//   * `Apply(operation, operand)` — execute one named operation of a `ConformanceScript` against a live world, using
//     the genre's OWN payload builders and command readers. The operation keys are the vocabulary
//     `GameCore.ReferenceConformance.ConformanceOperations` declares, so the fixture names no genre payload and the
//     runner contains no genre switch;
//   * `TryReadField(field)` — read one canonical field of `ConformanceFields` out of the live world. A field the
//     genre cannot answer is a reported miss with its reason, never a guessed value, so an unobservable assertion
//     fails instead of passing for the wrong reason.
//
// NO KERNEL MODULE IS RE-IMPLEMENTED HERE. `ConformanceWorld` builds the same module chain `W6GateScenario`'s
// fixture builds — target registry, assembly publisher, live target index/seeder, the control lane and its world
// join, the derivation pipeline and the temporal driver — and it attaches the genre's own runtime through
// `IW6Family.AttachGateRuntime`, which is the very attachment the genre's own scenario makes (P-002, P-043).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Planning;
using GameCore.ReferenceConformance;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>What one conformance operation did: it published, it was refused, or it could not be attempted.</summary>
    public enum ConformanceOperationOutcome
    {
        /// <summary>The operation published its assembly: the row's after state is observable.</summary>
        Published = 0,

        /// <summary>The operation was refused before any live mutation, with the old assembly kept (P-014, P-028).</summary>
        Refused = 1,

        /// <summary>The operation could not be attempted at all: a fixture defect, reported rather than hidden.</summary>
        Unsupported = 2,
    }

    /// <summary>One operation's result, with the diagnostic the world answered it with (P-052).</summary>
    public readonly struct ConformanceOperationResult
    {
        public ConformanceOperationResult(ConformanceOperationOutcome outcome, string detail)
        {
            Outcome = outcome;
            Detail = detail ?? string.Empty;
        }

        /// <summary>What happened.</summary>
        public ConformanceOperationOutcome Outcome { get; }

        /// <summary>The world's own answer: the publication outcome, the refusal code, or the miss reason.</summary>
        public string Detail { get; }

        /// <summary>True when the operation published.</summary>
        public bool Published => Outcome == ConformanceOperationOutcome.Published;

        /// <summary>The value a trace's outcome report carries as the refusal token.</summary>
        public string RefusalToken => Outcome == ConformanceOperationOutcome.Published ? string.Empty : Detail;

        public override string ToString() => Outcome + "(" + Detail + ")";
    }

    /// <summary>
    /// One genre's conformance surface: everything `IW6Family` declares (so a real world of that genre is built
    /// exactly as the earlier gates build it), plus the two genre-specific halves a 07 table's execution needs.
    ///
    /// Both members are the genre's own declarations in action: `Apply` builds a payload the genre's package
    /// declares and publishes it through the ordinary lane, and `TryReadField` reads the genre's own state owners
    /// through the published assembly and the live ECS storage (P-034).
    /// </summary>
    public interface IConformanceFamily : IW6Family
    {
        /// <summary>The conformance label every observation of this family's run is qualified with.</summary>
        string ConformanceLabel { get; }

        /// <summary>
        /// Executes one operation key of a `ConformanceScript` against one live world. <paramref name="operand"/> is
        /// the one scalar the operation sometimes needs (a reconfigured value, a repeated command count); the
        /// operations that need none ignore it.
        /// </summary>
        ConformanceOperationResult Apply(string operation, int operand, ConformanceWorld world);

        /// <summary>
        /// Reads one canonical field of `ConformanceFields` out of one live world. The value is the canonical token
        /// the trace records: a decimal integer, a canonical `(x,y,z)` triple, a set spelling, `none` for an absent
        /// derived row, `true`/`false`, or a stable name. A field this genre does not own, or a field whose target is
        /// not live, is a miss with its reason.
        /// </summary>
        bool TryReadField(string field, ConformanceWorld world, out string value, out string detail);

        /// <summary>
        /// Extra lane-only declarations this genre needs for the conformance tables, beyond its own declared set: a
        /// second provider of the same capability for a retraction row, or a compatible replacement carrying a new
        /// value for a reconfiguration row. They reach the lane's manifest source and never the compiled schedule,
        /// because a rule-bearing provider declares no stage, buffer or state slot of its own (P-009's "an empty
        /// category is explicit"), so the ownership surface a world compiles is unchanged by them.
        /// </summary>
        IReadOnlyList<CatalogPluginDeclaration> ConformanceDeclarations { get; }

        /// <summary>
        /// Prepares the genre's own authoritative state in a just-built world, after its targets were seeded and its
        /// stage runtime was attached and before any table step runs. The card market and the course need nothing;
        /// the narrative slice seeds its world-level quest ledger and installs each declared recipe's base layout,
        /// because a 07 s3.3 row reads that state as its before value and a missing slot is a missing observation
        /// rather than a default (P-032, P-034).
        ///
        /// A genre that cannot do what it promises returns false with the reason, and the runner records that as a
        /// failing observation rather than running a table against state the world does not own.
        /// </summary>
        bool PrepareConformanceWorld(ConformanceWorld world, out string detail);
    }

    /// <summary>
    /// One real world a conformance table executes in: the same module chain every genre gate builds, held together
    /// so a script step can publish an edit, submit a command, pump time and read state.
    /// </summary>
    public sealed class ConformanceWorld : IDisposable
    {
        private const int LaneQueueCapacity = 256;

        private const int LaneRetainedResults = 64;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        private readonly IConformanceFamily family;
        private readonly int targetCapacity;
        private ulong operationSequence;
        private ulong hostTicks;

        private ConformanceWorld(IConformanceFamily family, int targetCapacity)
        {
            this.family = family;
            this.targetCapacity = targetCapacity;
        }

        /// <summary>True when the world was built and its targets were seeded.</summary>
        public bool Ready { get; private set; }

        /// <summary>Why this world could not be built, empty when it was.</summary>
        public string Failure { get; private set; } = string.Empty;

        /// <summary>The world's own host: the sole authority the operations are published through (P-002).</summary>
        public UnityWorldHost? Host { get; private set; }

        /// <summary>The composition lane: the desired scope/install graph an edit is admitted to (P-002).</summary>
        public CompositionHost? Lane { get; private set; }

        /// <summary>The assembly publisher: the committed binding rows a derived read is checked against (P-030).</summary>
        public AssemblyPublisher? Publisher { get; private set; }

        /// <summary>The derivation pipeline: the assembly publication one edit is answered with (P-027).</summary>
        public DerivedAssemblyPipeline? Pipeline { get; private set; }

        /// <summary>The temporal driver: the only thing that advances the world's logical step (P-036).</summary>
        public WorldTimeDriver? Time { get; private set; }

        /// <summary>The live target index the genre's recipes are derived against (P-015).</summary>
        public LiveTargetIndex? Targets { get; private set; }

        /// <summary>The seeder that creates a target's real ECS storage (P-024).</summary>
        public LiveTargetSeeder? Seeder { get; private set; }

        /// <summary>The compiled ownership and schedule descriptor of this world's catalog revision (P-040).</summary>
        public PipelineDescriptorReport? Descriptor { get; private set; }

        /// <summary>The genre's own stage runtime, attached exactly where the genre attaches it (P-043).</summary>
        public W6StageRuntime? Runtime { get; private set; }

        /// <summary>The registered target capacity, so a spawn beyond it is a refusal rather than a crash.</summary>
        public int TargetCapacity => targetCapacity;

        /// <summary>How many host ticks this world's clock has been advanced by (P-038).</summary>
        public ulong HostTicks => hostTicks;

        /// <summary>
        /// Builds one world of a genre: its compiled schedule, its composition root, its seeded targets and the
        /// genre's own stage runtime. Any failure is reported as `Failure` instead of throwing out of a scenario.
        /// </summary>
        public static ConformanceWorld Build(IConformanceFamily family, IdSequence sessions, int targetCapacity)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            var world = new ConformanceWorld(family, targetCapacity);
            try
            {
                world.Create(sessions);
            }
            catch (Exception exception)
            {
                world.Failure = exception.GetType().FullName + ": " + exception.Message;
            }

            return world;
        }

        /// <summary>
        /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has one
        /// publication series, so an edit the world does not answer leaves the lane one publication ahead and every
        /// later adoption would be refused as stale: the two halves are always done together, and a
        /// `NoTargetChange` derivation is answered with the unchanged assembly so the counters stay joined.
        /// </summary>
        public ConformanceOperationResult PublishEdit(CompositionEditPayload payload, string label)
        {
            if (Host == null || Lane == null || Pipeline == null || Publisher == null)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported, label + ": the world or its pipeline is missing");
            }

            OperationId operation = NextOperation();
            EditAdmission admission = Lane.SubmitEdit(payload, operation, Lane.Committed.Revision);
            if (!admission.Staged)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused,
                    label + ": the lane refused the edit (" + admission.Kind + "/"
                    + DiagnosticCodeText.Of(admission.Code) + ")");
            }

            IReadOnlyList<PublishedOperation> published = Lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused,
                    label + ": the publication was refused ("
                    + (published.Count > 0
                        ? published[0].Outcome + "/" + DiagnosticCodeText.Of(published[0].Code)
                        : "none")
                    + ")");
            }

            DerivedAssemblyReport report = Pipeline.PublishDerived(operation);
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused, label + ": the world refused the assembly: " + report.Describe());
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = Publisher.PublishUnchangedAssembly(
                    NextOperation(), Lane.Committed.Revision, Lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    return new ConformanceOperationResult(
                        ConformanceOperationOutcome.Unsupported,
                        label + ": the unchanged assembly publication was refused: " + unchanged.Detail);
                }
            }

            if (!MatchesPublishedAssembly())
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported,
                    label + ": the lane and the world's assembly counters disagree (P-006)");
            }

            return new ConformanceOperationResult(ConformanceOperationOutcome.Published, label);
        }

        /// <summary>
        /// Submits one typed command envelope through the world's own command port and pumps one command-driven step,
        /// so an accepted command commits exactly one logical step (P-036, P-042). The world's own answer is
        /// returned: `Published` means the command was admitted and the pump committed a step.
        /// </summary>
        public ConformanceOperationResult SubmitAndPump(CommandEnvelope envelope, string label)
        {
            if (Host == null || Time == null)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported, label + ": the world or its clock is missing");
            }

            CommandAdmissionReceipt receipt = Host.Submit(envelope);
            if (!receipt.Admitted)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused,
                    label + ": the command was not admitted (" + receipt.Result.Kind + "/"
                    + DiagnosticCodeText.Of(receipt.Result.Reason) + ")");
            }

            hostTicks += family.CyclePumpTicks;
            TimeFrameReport frame = Time.PumpFrame(hostTicks);
            if (frame.StepsCommitted == 0UL)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused,
                    label + ": the pump committed no step (code=" + DiagnosticCodeText.Of(frame.Pump.Code) + ")");
            }

            return new ConformanceOperationResult(
                ConformanceOperationOutcome.Published,
                label + ": committed " + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture) + " step(s)");
        }

        /// <summary>
        /// Advances this world's clock by its genre's declared step without submitting a command, which is how a
        /// fixed-step world integrates one step of motion with no input (P-036, 07 s4.3's zero-input assertion).
        /// </summary>
        public ConformanceOperationResult PumpDeclaredStep(string label)
        {
            if (Host == null || Time == null)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Unsupported, label + ": the world or its clock is missing");
            }

            hostTicks += family.CyclePumpTicks;
            TimeFrameReport frame = Time.PumpFrame(hostTicks);
            if (frame.StepsCommitted == 0UL)
            {
                return new ConformanceOperationResult(
                    ConformanceOperationOutcome.Refused,
                    label + ": the pump committed no step (code=" + DiagnosticCodeText.Of(frame.Pump.Code) + ")");
            }

            return new ConformanceOperationResult(
                ConformanceOperationOutcome.Published,
                label + ": committed " + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture) + " step(s)");
        }

        /// <summary>True when the lane's committed revision/epoch and the world's published assembly are joined.</summary>
        public bool MatchesPublishedAssembly()
        {
            if (Lane == null || Publisher == null || Host == null)
            {
                return false;
            }

            return AssemblyPublisher.MatchesPublishedAssembly(
                Lane.Committed.Revision, Lane.Committed.Epoch, Publisher.PublishedRevision, Host.CurrentEpoch);
        }

        /// <summary>One fresh operation identity of this world, from a bounded increasing sequence (P-050).</summary>
        public OperationId NextOperation()
        {
            operationSequence++;
            return new OperationId(Host!.World, family.Issuer, operationSequence);
        }

        /// <summary>
        /// Stops and disposes this world, so a world that outlives its stage is red rather than silent (P-035).
        /// </summary>
        public Outcome StopAndDispose()
        {
            Outcome outcome = Outcome.NoChange;
            Runtime?.Dispose();
            Runtime = null;
            if (Host != null)
            {
                OperationResult stop = Host.Stop(NextOperation(), "gc-024 conformance world teardown");
                outcome = stop.Outcome;
                Host.Dispose();
                Host = null;
            }

            return outcome;
        }

        /// <summary>Disposes the world if it is still live.</summary>
        public void Dispose()
        {
            if (Host == null && Runtime == null)
            {
                return;
            }

            StopAndDispose();
        }

        private void Create(IdSequence sessions)
        {
            Descriptor = family.CompilePipeline();
            if (!Descriptor.Succeeded
                || Descriptor.Descriptor == null
                || Descriptor.Adaptation == null
                || Descriptor.Compilation == null)
            {
                Failure = "the ownership and schedule pipeline refused: " + Descriptor.Describe();
                return;
            }

            WorldId world = new WorldId(sessions.Next());
            WorldCreateRequest request = family.CreateRequest(world, NextOperationForCreation(world));
            UnityWorldRegistration registration = family.CreateRegistration(Descriptor.Adaptation);
            bool created = UnityWorldRegistry.TryCreate(
                request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
            Host = createdHost;
            if (!created || Host == null)
            {
                Failure = "world creation failed: " + result.Code + ": " + result.Detail;
                return;
            }

            var registry = new TargetRegistry(world, checked((uint)targetCapacity));
            Publisher = new AssemblyPublisher(
                Host, registry, family.CreateRecipes(), family.CreateMigrations(), Descriptor.Descriptor);
            Targets = new LiveTargetIndex(Publisher.Recipes);
            Seeder = new LiveTargetSeeder(Host, registry, Targets);

            Lane = CompositionHost.CreateDefault(
                world,
                family.WorldRootScope,
                new CatalogManifestSource(family.Catalog, LaneDeclarations()),
                null,
                family.LaneSeed);
            _ = new WorldCompositionBridge(Host, Lane, Publisher);

            if (!family.SeedTargets(new Gc013WorldContext(Host, Targets, Seeder)))
            {
                Failure = "the genre refused to seed its declared targets";
                return;
            }

            Runtime = family.AttachGateRuntime(Host, Descriptor, Targets, Seeder);

            IDerivationValueSource valueSource = family.CreateValues();
            Pipeline = new DerivedAssemblyPipeline(
                Host,
                Lane,
                Publisher,
                Targets,
                Seeder,
                valueSource,
                null,
                null,
                Publisher.Migrations,
                new StagedResourceGate(StagedByteCeiling, family.Issuer),
                new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
            Time = new WorldTimeDriver(Host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
            Time.AdoptResourceTable(Descriptor.Adaptation.NativeTable!);

            // The genre's own authoritative state, seeded after its runtime is attached and before any step runs, so
            // the first before-read of a table sees the state 07's "Before" column names (P-032).
            if (!family.PrepareConformanceWorld(this, out string prepareDetail))
            {
                Failure = "preparing the genre's own state was refused: " + prepareDetail;
                return;
            }

            Ready = true;
        }

        /// <summary>
        /// The declarations the lane resolves manifests from: the genre's own set plus this run's extra ones. The
        /// extra declarations are lane-only by construction — they declare a capability rule and no stage, buffer or
        /// state slot — so the compiled ownership surface is what the genre's own `CompilePipeline` produced (P-009).
        /// </summary>
        private IReadOnlyList<CatalogPluginDeclaration> LaneDeclarations()
        {
            IReadOnlyList<CatalogPluginDeclaration> declared = family.Declarations;
            IReadOnlyList<CatalogPluginDeclaration> extra = family.ConformanceDeclarations;
            if (extra == null || extra.Count == 0)
            {
                return declared;
            }

            var merged = new List<CatalogPluginDeclaration>(declared.Count + extra.Count);
            for (int i = 0; i < declared.Count; i++)
            {
                merged.Add(declared[i]);
            }

            for (int i = 0; i < extra.Count; i++)
            {
                merged.Add(extra[i]);
            }

            return merged;
        }

        private OperationId NextOperationForCreation(WorldId world)
        {
            operationSequence++;
            return new OperationId(world, family.Issuer, operationSequence);
        }
    }
}
