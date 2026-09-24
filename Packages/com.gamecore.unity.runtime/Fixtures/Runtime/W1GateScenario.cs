// GameCore.Unity.Fixtures — W1 integration gate scenario (the Wave 1 exit demonstration).
//
// The gate sentence this file implements, verbatim from
// `docs/game-core/09-implementation-guide.md` (Wave 1 — Shared seams and one owned world):
//
//   "Integrate catalog/DTOs, control host and actual Unity world driver. Create two worlds, admit one operation
//    and execute a guarded fixture stage; prove a thrown postwrite exception stops the next stage and
//    publication."
//
// Every check below runs the real modules — the production immutable catalog over a generated-style registration
// table (GC-003), the real `CompositionHost` control lane (GC-004), the real owned `Unity.Entities.World` through
// `UnityWorldRegistry`/`UnityWorldHost` and the guarded dispatch groups (GC-005), joined by
// `WorldCompositionBridge` — and reads facts from live ECS storage and the lane's ledger, never from a managed
// model of them. No seam stub participates.
//
// The runner is shared by the Unity EditMode test (`W1Gate` under `unity/GameCore.Validation`) and by the
// standalone player probe mode `-probeW1Gate`, so the same scenario is proven in the Editor and in an IL2CPP
// player.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;

namespace GameCore.Unity.Fixtures
{
    /// <summary>One named gate observation: what was checked and the observed values.</summary>
    public sealed class W1GateStep
    {
        public W1GateStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Facts the scenario observed, exposed so a caller can assert on them instead of trusting a boolean. Every
    /// value is read from live module state at the moment named in its own comment.
    /// </summary>
    public sealed class W1GateFacts
    {
        /// <summary>Fingerprint of the catalog the run was given (P-028).</summary>
        public string CatalogFingerprint { get; set; } = string.Empty;

        public int CatalogFactoryCount { get; set; }

        public int CatalogAcceptedDeclarations { get; set; }

        public int CatalogRejectedDeclarations { get; set; }

        /// <summary>Misses reported while resolving a manifest the catalog does not register (P-009).</summary>
        public int ManifestMissCount { get; set; }

        public Id128 WorldASession { get; set; }

        public Id128 WorldBSession { get; set; }

        /// <summary>Live hosts the registry held before the gate created its own worlds (the player's own world).</summary>
        public int RegistryBeforeCreate { get; set; }

        /// <summary>Live hosts in the owned-world registry after both worlds were created.</summary>
        public int RegistryCountAfterCreate { get; set; }

        /// <summary>Assembly epoch the successful admission published on the lane (P-006).</summary>
        public ulong LaneAEpoch { get; set; }

        /// <summary>
        /// World A's own assembly epoch. It advances only through the world's published assembly (04 s5); the
        /// composition epoch the lane published is a separate counter that live publication (GC-008) joins to it.
        /// </summary>
        public ulong WorldAEpoch { get; set; }

        /// <summary>Revision of world B's lane: it admitted nothing (P-002).</summary>
        public ulong LaneBRevision { get; set; }

        /// <summary>Committed logical step of world A after the successful guarded step.</summary>
        public ulong WorldASteps { get; set; }

        /// <summary>Committed logical step of world B after many host frames (zero, P-036/TEST-011).</summary>
        public ulong WorldBSteps { get; set; }

        public int WorldAPublishedImages { get; set; }

        public int WorldBPublishedImages { get; set; }

        public int WorldBStepGroupDispatchRuns { get; set; }

        /// <summary>Ingress and output dispatches of the idle world: presentation keeps running (04 s3).</summary>
        public int WorldBIngressCount { get; set; }

        public int WorldBOutputCount { get; set; }

        /// <summary>Fixture stage dispatch counters read from ECS storage after the last world-A pump.</summary>
        public int AcceptCount { get; set; }

        public int SettleCount { get; set; }

        public int FaultCount { get; set; }

        /// <summary>Zero-proving counter: the stage after the throwing stage never ran (P-031).</summary>
        public int ProjectCount { get; set; }

        /// <summary>Authoritative value written before the throwing stage faulted.</summary>
        public int CounterValue { get; set; }

        /// <summary>True when the committed step image of the successful step is retained.</summary>
        public bool StepOneImagePublished { get; set; }

        /// <summary>False: the step that faulted published no image (P-031, P-044).</summary>
        public bool FaultedStepImagePublished { get; set; }

        public ulong LastPublishedStep { get; set; }

        /// <summary>Operation 1: published by the control lane before execution (O-03).</summary>
        public string OperationOneOutcome { get; set; } = string.Empty;

        public string OperationTwoOutcome { get; set; } = string.Empty;

        /// <summary>True when the second operation's publication result survived its execution fault (P-031).</summary>
        public bool OperationTwoResultRetained { get; set; }

        public string WorldFaultCode { get; set; } = string.Empty;

        public string WorldFaultDetail { get; set; } = string.Empty;

        public int WorldFaultCount { get; set; }

        public string WorldLifecycleAfterFault { get; set; } = string.Empty;

        public ulong LaneAuditRevisionAfterFault { get; set; }

        /// <summary>Jobs retained by the failed step and tracked until safe teardown (P-047, P-048).</summary>
        public int QuarantinedJobs { get; set; }

        public int RetainedHandles { get; set; }

        public int OutstandingJobsBeforeTeardown { get; set; }

        public int SettledJobs { get; set; }

        public int OutstandingJobsAfterTeardown { get; set; }

        public int RetainedResourcesAfterTeardown { get; set; }

        /// <summary>Admission attempts the bridge refused because the world was faulted (P-031).</summary>
        public int WorldRefusalCount { get; set; }

        public string WorldRefusalCode { get; set; } = string.Empty;

        public int LaneRowsAfterRefusal { get; set; }

        /// <summary>Expired rows reported by the lane after it followed the world's committed step (P-006).</summary>
        public int RetentionExpiredRows { get; set; }

        public ulong LaneSyncedStep { get; set; }

        public int RegistryCountAfterTeardown { get; set; }

        /// <summary>Frames world B was pumped (its own counter, so an application pump cannot skew the check).</summary>
        public int WorldBPumpCount { get; set; }

        /// <summary>Job ledger rows outstanding right after the successful step committed.</summary>
        public int OutstandingJobsAfterFirstStep { get; set; }

        /// <summary>
        /// One-line digest of every recorded fact, so the standalone probe can archive the observed values beside
        /// the per-check outcomes without a second result shape.
        /// </summary>
        public string Describe()
        {
            return "catalogFingerprint=" + CatalogFingerprint
                + "; catalogFactoryKeys=" + CatalogFactoryCount
                + "; declaredPlugins=" + CatalogAcceptedDeclarations
                + "; rejectedDeclarations=" + CatalogRejectedDeclarations
                + "; manifestMisses=" + ManifestMissCount
                + "; sessionA=" + WorldASession
                + "; sessionB=" + WorldBSession
                + "; registryAfterCreate=" + RegistryCountAfterCreate
                + "; registryBefore=" + RegistryBeforeCreate
                + "; laneARevision=" + LaneARevision
                + "; laneAEpoch=" + LaneAEpoch
                + "; laneBRevision=" + LaneBRevision
                + "; worldAEpoch=" + WorldAEpoch
                + "; stepsA=" + WorldASteps
                + "; stepsB=" + WorldBSteps
                + "; imagesA=" + WorldAPublishedImages
                + "; imagesB=" + WorldBPublishedImages
                + "; stepGroupRunsB=" + WorldBStepGroupDispatchRuns
                + "; ingressB=" + WorldBIngressCount
                + "; outputB=" + WorldBOutputCount
                + "; pumpsB=" + WorldBPumpCount
                + "; accept=" + AcceptCount
                + "; settle=" + SettleCount
                + "; faultStage=" + FaultCount
                + "; project=" + ProjectCount
                + "; counter=" + CounterValue
                + "; stepOneImagePublished=" + StepOneImagePublished
                + "; faultedStepImagePublished=" + FaultedStepImagePublished
                + "; lastPublishedStep=" + LastPublishedStep
                + "; operationOne=" + OperationOneOutcome
                + "; operationTwo=" + OperationTwoOutcome
                + "; operationTwoResultRetained=" + OperationTwoResultRetained
                + "; worldFaultCode=" + WorldFaultCode
                + "; worldFaultCount=" + WorldFaultCount
                + "; worldLifecycleAfterFault=" + WorldLifecycleAfterFault
                + "; lanePublishedRevisionAfterFault=" + LaneAuditRevisionAfterFault
                + "; quarantinedJobs=" + QuarantinedJobs
                + "; retainedHandles=" + RetainedHandles
                + "; outstandingJobsBeforeTeardown=" + OutstandingJobsBeforeTeardown
                + "; outstandingJobsAfterFirstStep=" + OutstandingJobsAfterFirstStep
                + "; settledJobs=" + SettledJobs
                + "; outstandingJobsAfterTeardown=" + OutstandingJobsAfterTeardown
                + "; retainedResourcesAfterTeardown=" + RetainedResourcesAfterTeardown
                + "; worldRefusals=" + WorldRefusalCount
                + "; worldRefusalCode=" + WorldRefusalCode
                + "; laneRowsAfterRefusal=" + LaneRowsAfterRefusal
                + "; laneSyncedStep=" + LaneSyncedStep
                + "; registryAfterTeardown=" + RegistryCountAfterTeardown;
        }
    }

    /// <summary>Full result of one gate run: the named observations plus the facts they were computed from.</summary>
    public sealed class W1GateScenarioResult
    {
        public W1GateScenarioResult(IReadOnlyList<W1GateStep> steps, W1GateFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<W1GateStep> Steps { get; }

        public W1GateFacts Facts { get; }

        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        public string Describe()
        {
            List<string> failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count + " gate checks passed"
                : failed.Count + " gate check(s) failed: " + string.Join(" | ", failed);
        }
    }

    /// <summary>Runs the Wave 1 integration scenario against real modules only.</summary>
    public static class W1GateScenario
    {
        private const ulong HostTicks = 1_000_000UL;

        /// <summary>Host frames world B is pumped while it must stay idle.</summary>
        private const int IdleFrames = 8;

        /// <summary>
        /// Runs the gate with the hand-written generated-style catalog in this assembly
        /// (<see cref="W1GateCatalog"/>), which is the same table shape the content compiler emits.
        /// </summary>
        public static W1GateScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = W1GateCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the generated-style gate catalog was rejected: " + build.Describe());
            }

            PluginManifest manifest = W1GateManifests.Plain(
                W1GateKeys.PluginType,
                W1GateCatalog.PluginFactoryKey,
                W1GateKeys.CatalogSchema);

            var declarations = new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(manifest, ConfigDocument.Empty),
            };

            W1GateScenarioResult run = Run(
                build.Catalog,
                declarations,
                W1GateKeys.AbsentPluginFactory,
                W1GateKeys.AbsentPluginType,
                W1GateCatalog.Fingerprint());

            // The literal keys of a hand-written table are checked against the production derivation, exactly as the
            // GC-001 probe keys are, so a stable name and its literal cannot drift apart (P-004).
            var steps = new List<W1GateStep>(run.Steps.Count + 1)
            {
                new W1GateStep(
                    "gate-key-derivation",
                    W1GateCatalog.DerivationHolds(),
                    "literals of '" + W1GateCatalog.PluginFactoryStableName + "' and '"
                    + W1GateCatalog.SerializerStableName + "' match StableNameKeyDerivation"),
            };
            steps.AddRange(run.Steps);

            return new W1GateScenarioResult(steps, run.Facts);
        }

        /// <summary>
        /// Runs the gate against one catalog. <paramref name="declarations"/> are the generated-style
        /// declarations the mount path may resolve; <paramref name="absentFactoryKey"/> must be a key that
        /// catalog does not register, so the P-009 miss can be observed; <paramref name="declaredFingerprint"/>
        /// is the fingerprint the catalog's declarations were published with (a generated file's literal, or an
        /// independent recomputation), and the gate fails when the built catalog disagrees (P-028).
        /// </summary>
        public static W1GateScenarioResult Run(
            ICatalog catalog,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            FactoryKey absentFactoryKey,
            PluginTypeId absentPluginType,
            ContentHash declaredFingerprint)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (declarations == null || declarations.Count == 0)
            {
                throw new ArgumentException("the gate needs at least one declared plugin", nameof(declarations));
            }

            return new Executor(catalog, declarations, absentFactoryKey, absentPluginType, declaredFingerprint).Run();
        }

        private sealed class Executor
        {
            private readonly ICatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly FactoryKey absentFactoryKey;
            private readonly PluginTypeId absentPluginType;
            private readonly ContentHash declaredFingerprint;
            private readonly List<W1GateStep> steps = new List<W1GateStep>();
            private readonly W1GateFacts facts = new W1GateFacts();

            private IdSequence sessionSequence = new IdSequence(0x5731474154455345UL);

            private UnityWorldHost? worldA;
            private UnityWorldHost? worldB;
            private CompositionHost? laneA;
            private CompositionHost? laneB;
            private WorldCompositionBridge? bridgeA;
            private WorldCompositionBridge? bridgeB;
            private CatalogManifestSource? manifests;

            private OperationId operationOne;
            private OperationId operationTwo;

            /// <summary>Live hosts the registry held before the gate created its own two worlds (04 s3).</summary>
            private int registryBeforeCreate;

            public Executor(
                ICatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                FactoryKey absentFactoryKey,
                PluginTypeId absentPluginType,
                ContentHash declaredFingerprint)
            {
                this.catalog = catalog;
                this.declarations = declarations;
                this.absentFactoryKey = absentFactoryKey;
                this.absentPluginType = absentPluginType;
                this.declaredFingerprint = declaredFingerprint;
            }

            public W1GateScenarioResult Run()
            {
                CheckCatalogAndDeclarations();
                CreateWorlds();
                CreateLanes();
                PublishOneOperation();
                ExecuteGuardedStageInWorldA();
                ProveWorldBStaysIdle();
                FailAThrowingStage();
                ReportFaultHonestly();
                RefuseAdmissionOnTheFaultedWorld();
                TearDownSafely();

                return new W1GateScenarioResult(steps, facts);
            }

            /// <summary>Generated catalog validated by production contract types, and the P-009 miss behaviour.</summary>
            private void CheckCatalogAndDeclarations()
            {
                const string name = "gate-catalog-and-manifest-source";
                try
                {
                    FactoryKey pluginKey = declarations[0].Manifest.FactoryKey;
                    SchemaRef schema = declarations[0].Manifest.ConfigSchema;

                    CatalogLookup factory = catalog.Lookup(pluginKey);
                    CatalogLookup schemaLookup = catalog.LookupSchema(schema);
                    bool serializerBound = catalog.TryGetSerializer(schema, out ISchemaSerializer? serializer)
                        && serializer != null
                        && serializer!.Schema.Equals(schema);

                    CatalogLookup miss = catalog.Lookup(absentFactoryKey);
                    bool missReported = !miss.Found && miss.Code == DiagnosticCode.MissingDependency;

                    var withAbsent = new List<CatalogPluginDeclaration>(declarations);
                    withAbsent.Add(new CatalogPluginDeclaration(
                        W1GateManifests.Plain(absentPluginType, absentFactoryKey, schema),
                        ConfigDocument.Empty));

                    // The catalog's own fingerprint must equal the value its declarations were published with:
                    // the generated file's emitted literal, or an independent recomputation (P-028).
                    bool fingerprintAsDeclared = catalog.Fingerprint.Equals(declaredFingerprint);

                    manifests = new CatalogManifestSource(catalog, declarations);
                    var withUnregistered = new CatalogManifestSource(catalog, withAbsent);

                    bool accepted = manifests.AcceptedCount == declarations.Count && manifests.Rejected.Count == 0;
                    bool refused = withUnregistered.Rejected.Count == 1
                        && withUnregistered.Rejected[0].Code == DiagnosticCode.MissingDependency;
                    bool unregisteredNotResolved = !withUnregistered.TryGetManifest(absentPluginType, out PluginManifest? resolved)
                        && resolved == null;

                    facts.CatalogFingerprint = catalog.Fingerprint.ToHex();
                    facts.CatalogFactoryCount = catalog.FactoryKeysInCanonicalOrder().Count;
                    facts.CatalogAcceptedDeclarations = manifests.AcceptedCount;
                    facts.CatalogRejectedDeclarations = withUnregistered.Rejected.Count;
                    facts.ManifestMissCount = withUnregistered.ManifestMissCount;

                    bool pass = catalog.Fingerprint.IsEmpty == false
                        && fingerprintAsDeclared
                        && catalog.FactoryKeysInCanonicalOrder().Count > 0
                        && factory.Found
                        && factory.Factory != null
                        && factory.Factory.Kind == FactoryKind.PluginFactory
                        && schemaLookup.Found
                        && serializerBound
                        && missReported
                        && accepted
                        && refused
                        && unregisteredNotResolved;

                    string detail = "fingerprint=" + facts.CatalogFingerprint
                        + "; factoryKeys=" + facts.CatalogFactoryCount
                        + "; pluginLookup=" + factory.Describe()
                        + "; fingerprintAsDeclared=" + fingerprintAsDeclared
                        + "; declaredFingerprint=" + declaredFingerprint.ToHex()
                        + "; serializerBound=" + serializerBound
                        + "; acceptedDeclarations=" + facts.CatalogAcceptedDeclarations
                        + "; rejectedDeclarations=" + facts.CatalogRejectedDeclarations
                        + "; unregisteredMiss=" + withUnregistered.Rejected.Count
                        + "; unregisteredResolved=" + !unregisteredNotResolved
                        + "; unknownKeyLookup=" + miss.Describe();

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>Two real owned Unity worlds, through the GC-005 world host.</summary>
            private void CreateWorlds()
            {
                const string name = "gate-two-owned-worlds";
                try
                {
                    // The player host already owns its application world, so the gate counts its own two worlds
                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId sessionA = NextSession();
                    WorldId sessionB = NextSession();

                    worldA = CreateWorld(sessionA, 1UL, out string failureA);
                    worldB = CreateWorld(sessionB, 2UL, out string failureB);

                    facts.RegistryCountAfterCreate = UnityWorldRegistry.Count;
                    facts.WorldASession = sessionA.Session;
                    facts.WorldBSession = sessionB.Session;

                    bool bothRegistered = UnityWorldRegistry.TryGet(sessionA, out UnityWorldHost? foundA)
                        && ReferenceEquals(foundA, worldA)
                        && UnityWorldRegistry.TryGet(sessionB, out UnityWorldHost? foundB)
                        && ReferenceEquals(foundB, worldB);

                    facts.RegistryBeforeCreate = registryBeforeCreate;
                    bool pass = worldA != null && worldB != null
                        && !sessionA.Session.Equals(sessionB.Session)
                        && facts.RegistryCountAfterCreate == registryBeforeCreate + 2
                        && bothRegistered
                        && worldA!.Lifecycle == WorldLifecycleState.Running
                        && worldB!.Lifecycle == WorldLifecycleState.Running
                        && worldA.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && worldA.CurrentStep.Equals(LogicalStepId.Zero)
                        && worldB.CurrentStep.Equals(LogicalStepId.Zero);

                    string detail = "failureA='" + failureA + "'"
                        + "; failureB='" + failureB + "'"
                        + "; registryBefore=" + facts.RegistryBeforeCreate
                        + "; registryAfter=" + facts.RegistryCountAfterCreate
                        + "; sessionA=" + sessionA.Session
                        + "; sessionB=" + sessionB.Session
                        + "; lifecycleA=" + Describe(worldA)
                        + "; lifecycleB=" + Describe(worldB);

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>A real control lane per world, joined to its world by the integration bridge.</summary>
            private void CreateLanes()
            {
                const string name = "gate-control-lanes-linked";
                try
                {
                    if (worldA == null || worldB == null || manifests == null)
                    {
                        steps.Add(new W1GateStep(name, false, "the two worlds were not created"));
                        return;
                    }

                    laneA = CompositionHost.CreateDefault(worldA.World, W1GateKeys.RootScope(1UL), manifests, null);
                    laneB = CompositionHost.CreateDefault(worldB.World, W1GateKeys.RootScope(2UL), manifests, null);
                    bridgeA = new WorldCompositionBridge(worldA, laneA);
                    bridgeB = new WorldCompositionBridge(worldB, laneB);

                    bool linksMatchTheOwnWorld = ReferenceEquals(bridgeA.World, worldA)
                        && laneA.World.Session.Equals(worldA.World.Session)
                        && laneB!.World.Session.Equals(worldB.World.Session);

                    bool pass = linksMatchTheOwnWorld
                        && laneA.Committed.Revision.Equals(CompositionRevision.Zero)
                        && laneA.Committed.Epoch.Equals(AssemblyEpoch.Zero)
                        && laneB.Committed.Revision.Equals(CompositionRevision.Zero)
                        && laneA.Committed.Mode == PropagationMode.Automatic;

                    facts.LaneARevision = laneA.Committed.Revision.Value;
                    facts.LaneBRevision = laneB.Committed.Revision.Value;

                    string detail = "laneARevision=" + facts.LaneARevision
                        + "; laneAEpoch=" + laneA.Committed.Epoch.Value
                        + "; laneAMode=" + laneA.Committed.Mode
                        + "; laneBRevision=" + facts.LaneBRevision
                        + "; laneARootMatchesWorld=" + laneA.World.Session.Equals(worldA.World.Session);

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>One operation admitted on the real control lane and published at a real boundary.</summary>
            private void PublishOneOperation()
            {
                const string name = "gate-one-operation-admitted";
                try
                {
                    if (worldA == null || laneA == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world A or its lane is missing"));
                        return;
                    }

                    operationOne = new OperationId(worldA.World, W1GateKeys.Issuer, 1UL);
                    CompositionEditPayload payload = W1GatePayloads.Mount(
                        declarations[0].Manifest,
                        W1GateKeys.Instance(1UL),
                        W1GateKeys.RootScope(1UL),
                        declarations[0].SchemaDefaults);

                    WorldAdmissionReport report = bridgeA.SubmitAndExecute(payload, operationOne);

                    facts.LaneARevision = laneA.Committed.Revision.Value;
                    facts.LaneAEpoch = laneA.Committed.Epoch.Value;
                    facts.OperationOneOutcome = report.PublicationOutcome.ToString();

                    bool tokenOwnedByA = report.PublishedToken.HasValue
                        && report.PublishedToken.Value.World.Session.Equals(worldA.World.Session);

                    // The operation identity is the durable retrieval key: its terminal result must be readable
                    // from the lane after publication (P-051).
                    OperationResult? result = laneA.ResultOf(operationOne);
                    bool resultReadable = result != null
                        && result.Outcome == Outcome.Published
                        && result.Code == DiagnosticCode.None
                        && result.PublishedSnapshot.HasValue;
                    bool pass = resultReadable
                        && report.Outcome == BridgeOutcome.Executed
                        && report.Admission == AdmissionKind.Fresh
                        && report.Staged
                        && report.PublicationOutcome == Outcome.Published
                        && report.PublicationCode == DiagnosticCode.None
                        && tokenOwnedByA
                        && report.CommandSubmitted
                        && report.DemandAfter == 1UL
                        && facts.LaneARevision == 1UL
                        && facts.LaneAEpoch == 1UL
                        && laneA.PublicationCount == 1
                        && laneA.FindInstall(W1GateKeys.Instance(1UL)) != null
                        && laneA.OperationLedger.PublishedRevision.Value == 1UL;

                    string detail = "bridgeOutcome=" + report.Outcome
                        + "; admission=" + report.Admission
                        + "; staged=" + report.Staged
                        + "; publication=" + report.PublicationOutcome
                        + "; publicationCode=" + report.PublicationCode
                        + "; token=" + (report.PublishedToken.HasValue ? report.PublishedToken.Value.ToString() : "<none>")
                        + "; resultReadable=" + resultReadable
                        + "; commandSubmitted=" + report.CommandSubmitted
                        + "; demand=" + report.DemandAfter
                        + "; revision=" + facts.LaneARevision
                        + "; epoch=" + facts.LaneAEpoch
                        + "; publications=" + laneA.PublicationCount
                        + "; installationPublished=" + (laneA.FindInstall(W1GateKeys.Instance(1UL)) != null);

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>The admitted operation becomes a guarded step that commits in world A.</summary>
            private void ExecuteGuardedStageInWorldA()
            {
                const string name = "gate-guarded-stage-commits";
                try
                {
                    if (worldA == null || laneA == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world A or its lane is missing"));
                        return;
                    }

                    int imagesBefore = worldA.Publications.PublishedCount;
                    var stepOneToken = new SnapshotToken(worldA.World, AssemblyEpoch.First, LogicalStepId.First);

                    WorldPumpResult pump = worldA.PumpFrame(HostTicks);
                    FixtureWorldState.TryReadTrail(worldA.EntityWorld.EntityManager, out FixtureTrail trail);

                    facts.WorldASteps = worldA.CurrentStep.Value;
                    facts.AcceptCount = trail.AcceptCount;
                    facts.SettleCount = trail.SettleCount;
                    facts.FaultCount = trail.FaultCount;
                    facts.ProjectCount = trail.ProjectCount;
                    facts.CounterValue = FixtureWorldState.ReadCounter(worldA.EntityWorld.EntityManager);
                    facts.WorldAPublishedImages = worldA.Publications.PublishedCount;
                    facts.WorldAEpoch = worldA.CurrentEpoch.Value;
                    facts.LastPublishedStep = worldA.Publications.Last != null
                        ? worldA.Publications.Last!.Token.LogicalStepId.Value : 0UL;
                    facts.StepOneImagePublished = worldA.Publications.HasPublished(stepOneToken);

                    int expired = bridgeA.SyncStepFromWorld();
                    facts.RetentionExpiredRows = expired;
                    facts.LaneSyncedStep = bridgeA.LastSyncedStep.Value;
                    facts.OutstandingJobsAfterFirstStep = worldA.Ledger.OutstandingJobCount;

                    // The scenario runs synchronously inside one call, so nothing else can pump world A and consume
                    // the admitted demand: this frame must have run the step, and the two modules must agree on the
                    // token they published.
                    bool pumpReportedTheStep = pump.Pumped
                        && pump.Advance != null
                        && pump.Advance!.Accepted
                        && pump.Advance.Outcome == Outcome.Published
                        && pump.Advance.PublishedSnapshot.HasValue
                        && pump.Advance.PublishedSnapshot!.Value.Equals(stepOneToken);

                    bool pass = pumpReportedTheStep
                        && facts.WorldASteps == 1UL
                        && worldA.Driver.CommittedStepCount == 1
                        && imagesBefore == 2
                        && facts.StepOneImagePublished
                        && facts.LastPublishedStep == 1UL
                        && facts.AcceptCount == 1
                        && facts.SettleCount == 1
                        && facts.FaultCount == 1
                        && facts.ProjectCount == 1
                        && facts.CounterValue == 111
                        && worldA.StepGroup.TotalDispatchedCount == 4
                        && worldA.Ledger.OutstandingJobCount == 0
                        && worldA.PendingDemand == 0UL
                        && worldA.Lifecycle == WorldLifecycleState.Running
                        && worldA.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && facts.LaneSyncedStep == 1UL;

                    string detail = "outcome=" + (pump.Advance != null ? pump.Advance.Outcome.ToString() : "<none>")
                        + "; step=" + facts.WorldASteps
                        + "; committedSteps=" + worldA.Driver.CommittedStepCount
                        + "; publishedImages=" + facts.WorldAPublishedImages
                        + "; stepOneImagePublished=" + facts.StepOneImagePublished
                        + "; lastPublishedStep=" + facts.LastPublishedStep
                        + "; accept=" + facts.AcceptCount
                        + "; settle=" + facts.SettleCount
                        + "; fault=" + facts.FaultCount
                        + "; project=" + facts.ProjectCount
                        + "; counter=" + facts.CounterValue
                        + "; worldEpoch=" + facts.WorldAEpoch
                        + "; pendingDemand=" + worldA.PendingDemand
                        + "; laneSyncedStep=" + facts.LaneSyncedStep
                        + "; retentionExpiredRows=" + facts.RetentionExpiredRows;

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>World B receives the same kind of frames and still commits zero steps.</summary>
            private void ProveWorldBStaysIdle()
            {
                const string name = "gate-second-world-stays-idle";
                try
                {
                    if (worldB == null || laneB == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world B or its lane is missing"));
                        return;
                    }

                    for (int frame = 1; frame <= IdleFrames; frame++)
                    {
                        worldB.PumpFrame(HostTicks * (ulong)(frame + 1));
                    }

                    FixtureWorldState.TryReadTrail(worldB.EntityWorld.EntityManager, out FixtureTrail trail);

                    facts.WorldBSteps = worldB.CurrentStep.Value;
                    facts.WorldBPublishedImages = worldB.Publications.PublishedCount;
                    facts.WorldBStepGroupDispatchRuns = worldB.StepGroup.DispatchRunCount;
                    facts.WorldBPumpCount = worldB.PumpCount;
                    facts.WorldBIngressCount = trail.IngressCount;
                    facts.WorldBOutputCount = trail.OutputCount;
                    facts.LaneBRevision = laneB.Committed.Revision.Value;

                    // Ingress and output dispatch on every routed frame; the world's own pump counter is the
                    // baseline, so an extra application-pump frame cannot make this check pass or fail falsely.
                    bool pass = facts.WorldBSteps == 0UL
                        && worldB.Driver.CommittedStepCount == 0
                        && facts.WorldBStepGroupDispatchRuns == 0
                        && facts.WorldBPublishedImages == 1
                        && worldB.PumpCount >= IdleFrames
                        && facts.WorldBIngressCount == worldB.PumpCount
                        && facts.WorldBOutputCount == worldB.PumpCount
                        && trail.AcceptCount == 0
                        && trail.ProjectCount == 0
                        && facts.LaneBRevision == 0UL
                        && laneB.PublicationCount == 0
                        && worldB.RetainedDebt.Ticks == 0UL
                        && worldB.Lifecycle == WorldLifecycleState.Running
                        && worldB.PendingDemand == 0UL;

                    string detail = "frames=" + worldB.PumpCount
                        + "; steps=" + facts.WorldBSteps
                        + "; committedSteps=" + worldB.Driver.CommittedStepCount
                        + "; stepGroupDispatchRuns=" + facts.WorldBStepGroupDispatchRuns
                        + "; publishedImages=" + facts.WorldBPublishedImages
                        + "; ingress=" + facts.WorldBIngressCount
                        + "; output=" + facts.WorldBOutputCount
                        + "; accept=" + trail.AcceptCount
                        + "; project=" + trail.ProjectCount
                        + "; laneRevision=" + facts.LaneBRevision
                        + "; lanePublications=" + laneB.PublicationCount;

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>
            /// The fail-stop proof: a managed system writes authoritative state and then throws; the next stage does
            /// not run, the world faults, and no step or snapshot publishes (P-031, P-044).
            /// </summary>
            private void FailAThrowingStage()
            {
                const string name = "gate-thrown-postwrite-exception-fails-stop";
                try
                {
                    if (worldA == null || laneA == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world A or its lane is missing"));
                        return;
                    }

                    if (!FixtureWorldState.SetFaultEnabled(worldA.EntityWorld.EntityManager, true))
                    {
                        steps.Add(new W1GateStep(name, false, "the fixture world state is not seeded"));
                        return;
                    }

                    operationTwo = new OperationId(worldA.World, W1GateKeys.Issuer, 2UL);
                    CompositionEditPayload payload = W1GatePayloads.Mount(
                        declarations[0].Manifest,
                        W1GateKeys.Instance(2UL),
                        W1GateKeys.RootScope(1UL),
                        declarations[0].SchemaDefaults);

                    WorldAdmissionReport admitted = bridgeA.SubmitAndExecute(payload, operationTwo);
                    int imagesBeforeStep = worldA.Publications.PublishedCount;

                    WorldPumpResult pump = worldA.PumpFrame(HostTicks * 2UL);
                    FixtureWorldState.TryReadTrail(worldA.EntityWorld.EntityManager, out FixtureTrail trail);

                    var faultedStepToken = new SnapshotToken(
                        worldA.World,
                        worldA.CurrentEpoch,
                        new LogicalStepId(2UL));

                    facts.AcceptCount = trail.AcceptCount;
                    facts.SettleCount = trail.SettleCount;
                    facts.FaultCount = trail.FaultCount;
                    facts.ProjectCount = trail.ProjectCount;
                    facts.CounterValue = FixtureWorldState.ReadCounter(worldA.EntityWorld.EntityManager);
                    facts.WorldASteps = worldA.CurrentStep.Value;
                    facts.WorldAPublishedImages = worldA.Publications.PublishedCount;
                    facts.FaultedStepImagePublished = worldA.Publications.HasPublished(faultedStepToken);
                    facts.QuarantinedJobs = worldA.Ledger.QuarantinedJobCount;
                    facts.RetainedHandles = worldA.Driver.RetainedJobs.Count;
                    facts.OutstandingJobsBeforeTeardown = worldA.Ledger.OutstandingJobCount;
                    facts.OperationTwoOutcome = admitted.PublicationOutcome.ToString();

                    // Nothing else can pump world A inside this synchronous call, so this frame ran the faulted
                    // step: it must report Faulted, and the state assertions below prove the failed step neither
                    // advanced the step nor published an image (P-031, P-044).
                    bool pumpReportedTheFault = pump.Pumped
                        && pump.Advance != null
                        && !pump.Advance!.Accepted
                        && pump.Advance.Outcome == Outcome.Faulted
                        && pump.Advance.PublishedSnapshot == null;

                    bool pass = admitted.Outcome == BridgeOutcome.Executed
                        && admitted.PublicationOutcome == Outcome.Published
                        && admitted.CommandSubmitted
                        && pumpReportedTheFault
                        && worldA.Driver.IsFaulted
                        && worldA.Driver.FaultCode == DiagnosticCode.ApplyFault
                        && worldA.Lifecycle == WorldLifecycleState.Faulted
                        && facts.WorldASteps == 1UL
                        && worldA.Driver.CommittedStepCount == 1
                        && facts.WorldAPublishedImages == imagesBeforeStep
                        && facts.WorldAPublishedImages == 2
                        && !facts.FaultedStepImagePublished
                        && facts.AcceptCount == 2
                        && facts.SettleCount == 2
                        && facts.FaultCount == 2
                        && facts.ProjectCount == 1
                        && facts.CounterValue == 222
                        && facts.QuarantinedJobs > 0
                        && facts.RetainedHandles > 0
                        && facts.OutstandingJobsBeforeTeardown > 0
                        && worldA.PendingDemand == 0UL;

                    string detail = "admission=" + admitted.Outcome
                        + "/" + admitted.PublicationOutcome
                        + "; commandSubmitted=" + admitted.CommandSubmitted
                        + "; advanceOutcome=" + (pump.Advance != null ? pump.Advance.Outcome.ToString() : "<none>")
                        + "; faultCode=" + worldA.Driver.FaultCode
                        + "; lifecycle=" + worldA.Lifecycle
                        + "; step=" + facts.WorldASteps
                        + "; publishedImages=" + facts.WorldAPublishedImages
                        + "; faultedStepImagePublished=" + facts.FaultedStepImagePublished
                        + "; accept=" + facts.AcceptCount
                        + "; settle=" + facts.SettleCount
                        + "; faultStage=" + facts.FaultCount
                        + "; projectAfterFault=" + facts.ProjectCount
                        + "; counter=" + facts.CounterValue
                        + "; quarantinedJobs=" + facts.QuarantinedJobs
                        + "; retainedHandles=" + facts.RetainedHandles
                        + "; outstandingJobs=" + facts.OutstandingJobsBeforeTeardown
                        + "; pendingDemand=" + worldA.PendingDemand;

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>
            /// The operation status and the world state after the fault: the published composition result stands,
            /// the world reports the fault, the last committed step image is unchanged and the lane only follows the
            /// step the world committed (P-006, P-031, P-051).
            /// </summary>
            private void ReportFaultHonestly()
            {
                const string name = "gate-operation-status-reports-fault-honestly";
                try
                {
                    if (worldA == null || laneA == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world A or its lane is missing"));
                        return;
                    }

                    WorldExecutionReport report = bridgeA.Report(operationTwo);
                    OperationResult? published = laneA.ResultOf(operationTwo);

                    facts.OperationTwoResultRetained = report.HasResult
                        && published != null
                        && published.Outcome == Outcome.Published;
                    facts.WorldFaultCode = DiagnosticCodeText.Of(report.WorldFaultCode);
                    facts.WorldFaultDetail = report.WorldFaultDetail;
                    facts.WorldFaultCount = report.WorldFaultCount;
                    facts.WorldLifecycleAfterFault = report.WorldLifecycle.ToString();
                    facts.LaneAuditRevisionAfterFault = laneA.Committed.Revision.Value;
                    facts.LastPublishedStep = report.LastCommittedToken.HasValue
                        ? report.LastCommittedToken.Value.LogicalStepId.Value : 0UL;

                    bool pass = report.StatusOutcome == OperationReadOutcome.Found
                        && report.HasResult
                        && report.OperationOutcome == Outcome.Published
                        && report.OperationCode == DiagnosticCode.None
                        && report.OperationToken.HasValue
                        && report.OperationToken!.Value.AssemblyEpoch.Equals(new AssemblyEpoch(2UL))
                        && report.WorldFaulted
                        && report.WorldFaultCode == DiagnosticCode.ApplyFault
                        && report.WorldFaultCount == 1
                        && report.WorldLifecycle == WorldLifecycleState.Faulted
                        && report.FaultedAfterPublication
                        && report.LastCommittedStep.Equals(LogicalStepId.First)
                        && report.LastCommittedToken.HasValue
                        && report.LastCommittedToken!.Value.LogicalStepId.Equals(LogicalStepId.First)
                        && report.CommittedStepCount == 1
                        && report.PublishedImageCount == 2
                        && report.WorldFaultDetail.Length > 0
                        && facts.LaneAuditRevisionAfterFault == 2UL;

                    string detail = "status=" + report.StatusOutcome
                        + "; hasResult=" + report.HasResult
                        + "; operationOutcome=" + report.OperationOutcome
                        + "; operationCode=" + report.OperationCode
                        + "; operationToken=" + (report.OperationToken.HasValue ? report.OperationToken.Value.ToString() : "<none>")
                        + "; worldFaulted=" + report.WorldFaulted
                        + "; worldFaultCode=" + facts.WorldFaultCode
                        + "; worldFaultCount=" + facts.WorldFaultCount
                        + "; lifecycle=" + facts.WorldLifecycleAfterFault
                        + "; lastCommittedStep=" + report.LastCommittedStep.Value
                        + "; publishedImages=" + report.PublishedImageCount
                        + "; lanePublishedRevision=" + facts.LaneAuditRevisionAfterFault
                        + "; faultDetail='" + report.WorldFaultDetail + "'";

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>A faulted world admits nothing: the lane gains no row for work it cannot execute (P-031).</summary>
            private void RefuseAdmissionOnTheFaultedWorld()
            {
                const string name = "gate-faulted-world-refuses-admission";
                try
                {
                    if (worldA == null || laneA == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "world A or its lane is missing"));
                        return;
                    }

                    int rowsBefore = laneA.OperationLedger.RowCount;
                    int refusalsBefore = bridgeA.RefusedCount;
                    var operationThree = new OperationId(worldA.World, W1GateKeys.Issuer, 3UL);
                    CompositionEditPayload payload = W1GatePayloads.Mount(
                        declarations[0].Manifest,
                        W1GateKeys.Instance(3UL),
                        W1GateKeys.RootScope(1UL),
                        declarations[0].SchemaDefaults);

                    WorldAdmissionReport refused = bridgeA.SubmitAndExecute(payload, operationThree);

                    facts.LaneRowsAfterRefusal = laneA.OperationLedger.RowCount;
                    facts.WorldRefusalCode = DiagnosticCodeText.Of(refused.RefusalCode);
                    facts.WorldRefusalCount = bridgeA.RefusedCount;
                    facts.WorldASteps = worldA.CurrentStep.Value;

                    bool pass = refused.Outcome == BridgeOutcome.WorldRefused
                        && refused.RefusedByWorld
                        && refused.RefusalCode == DiagnosticCode.ApplyFault
                        && facts.WorldRefusalCount == refusalsBefore + 1
                        && facts.LaneRowsAfterRefusal == rowsBefore
                        && laneA.FindInstall(W1GateKeys.Instance(3UL)) == null
                        && facts.WorldASteps == 1UL
                        && worldA.PendingDemand == 0UL;

                    string detail = "bridgeOutcome=" + refused.Outcome
                        + "; refusalCode=" + facts.WorldRefusalCode
                        + "; refusalDetail='" + refused.RefusalDetail + "'"
                        + "; laneRowsBefore=" + rowsBefore
                        + "; laneRowsAfter=" + facts.LaneRowsAfterRefusal
                        + "; worldRefusals=" + facts.WorldRefusalCount
                        + "; step=" + facts.WorldASteps
                        + "; pendingDemand=" + worldA.PendingDemand;

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>Pending work stays tracked until safe teardown; then storage is released cleanly.</summary>
            private void TearDownSafely()
            {
                const string name = "gate-teardown-settles-and-disposes";
                try
                {
                    if (worldA == null || worldB == null || bridgeA == null)
                    {
                        steps.Add(new W1GateStep(name, false, "the two worlds were not created"));
                        return;
                    }

                    int retainedBefore = worldA.Driver.RetainedJobs.Count;
                    OperationResult stop = worldA.Stop(
                        new OperationId(worldA.World, W1GateKeys.Issuer, 4UL),
                        "W1 gate teardown after the injected post-write fault");

                    facts.SettledJobs = worldA.SettledJobCount;
                    facts.OutstandingJobsAfterTeardown = worldA.Ledger.OutstandingJobCount;
                    facts.RetainedResourcesAfterTeardown = worldA.Ledger.RetainedResourceCount;

                    // The lane follows the step the world committed; the refused admission created no new work.
                    bridgeA.SyncStepFromWorld();
                    facts.LaneSyncedStep = bridgeA.LastSyncedStep.Value;

                    bool stoppedA = stop.Outcome == Outcome.Published
                        && facts.SettledJobs == retainedBefore
                        && retainedBefore == facts.RetainedHandles
                        && facts.OutstandingJobsAfterTeardown == 0
                        && facts.RetainedResourcesAfterTeardown == 0
                        && worldA.Lifecycle == WorldLifecycleState.Disposed
                        && !worldA.IsEntityWorldCreated
                        && worldA.Driver.RetainedJobs.CompletionFailureCount == 0;

                    OperationResult stopB = worldB.Stop(
                        new OperationId(worldB.World, W1GateKeys.Issuer, 5UL),
                        "W1 gate teardown of the idle world");
                    bool stoppedB = stopB.Outcome == Outcome.Published
                        && worldB.Lifecycle == WorldLifecycleState.Disposed
                        && !worldB.IsEntityWorldCreated
                        && facts.WorldBSteps == 0UL;

                    // Both stopped hosts removed themselves from the registry, so it holds exactly the hosts it held
                    // before the gate ran: teardown left no static host reference behind, and a host that was already
                    // registered (the player's application world) is untouched (04 s3, 04 s9).
                    facts.RegistryCountAfterTeardown = UnityWorldRegistry.Count;

                    bool pass = stoppedA
                        && stoppedB
                        && facts.RegistryCountAfterTeardown == registryBeforeCreate;

                    string detail = "stopA=" + stop.Outcome
                        + "; settledJobs=" + facts.SettledJobs
                        + "; retainedHandlesBeforeStop=" + retainedBefore
                        + "; outstandingJobs=" + facts.OutstandingJobsAfterTeardown
                        + "; retainedResources=" + facts.RetainedResourcesAfterTeardown
                        + "; lifecycleA=" + worldA.Lifecycle
                        + "; entityWorldA=" + worldA.IsEntityWorldCreated
                        + "; stopB=" + stopB.Outcome
                        + "; lifecycleB=" + worldB.Lifecycle
                        + "; idleStepsB=" + facts.WorldBSteps
                        + "; laneSyncedStep=" + facts.LaneSyncedStep
                        + "; registryBefore=" + registryBeforeCreate
                        + "; registryAfterTeardown=" + facts.RegistryCountAfterTeardown;

                    steps.Add(new W1GateStep(name, pass, detail));
                }
                catch (Exception exception)
                {
                    // A teardown failure must not leave this gate's own hosts registered for the next test.
                    ReleaseOwnWorlds();
                    facts.RegistryCountAfterTeardown = UnityWorldRegistry.Count;
                    steps.Add(new W1GateStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>
            /// Stops and disposes only this gate's own two worlds, swallowing a blocked teardown. An application
            /// world created before the gate runs is never touched (04 s3).
            /// </summary>
            private void ReleaseOwnWorlds()
            {
                Release(worldA);
                Release(worldB);
            }

            private static void Release(UnityWorldHost? host)
            {
                if (host == null)
                {
                    return;
                }

                try
                {
                    if (host.Lifecycle != WorldLifecycleState.Disposed)
                    {
                        host.Stop(
                            new OperationId(host.World, W1GateKeys.Issuer, 6UL),
                            "W1 gate failure-path release");
                    }

                    if (host.Lifecycle != WorldLifecycleState.Disposed)
                    {
                        host.Dispose();
                    }
                }
                catch (Exception)
                {
                    // A blocked teardown is reported by the step detail, not by masking the original failure.
                }

                UnityWorldRegistry.Remove(host.World);
            }

            private WorldId NextSession() =>
                new WorldId(new Id128(0x5731474154454553UL, sessionSequence.Next()));

            private static UnityWorldHost? CreateWorld(WorldId session, ulong ordinal, out string failure)
            {
                bool created = UnityWorldRegistry.TryCreate(
                    FixtureRegistration.CommandDrivenRequest(
                        session,
                        new OperationId(session, W1GateKeys.Issuer, ordinal),
                        ContentHash.Empty),
                    FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                    out UnityWorldHost? host,
                    out WorldCreateResult result);

                failure = created ? string.Empty : result.Code + ": " + result.Detail;
                return created ? host : null;
            }

            private static string Describe(UnityWorldHost? host) =>
                host == null
                    ? "<none>"
                    : host.Lifecycle + "@step" + host.CurrentStep.Value
                      + "/epoch" + host.CurrentEpoch.Value;

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
