#nullable enable
using System;
using System.IO;
using GameCore.Contracts;
using GameCore.Validation.Generated;
using GameCore.Validation.Probe;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Standalone IL2CPP qualification probe. It performs every GC-001 observable check inside a real player
    /// build, writes a structured JSON result, and exits with a distinct code per outcome.
    ///
    /// Exit codes: 0 all positive probes passed; 1 failure; 3 negative mode succeeded.
    /// </summary>
    public static class ProbeRunner
    {
        private const string WorldName = "GameCoreValidationProbeWorld";

        private const int ExpectedEntityCount = 3;

        private const int ExpectedCounterSum = 7 + 11 + 13;

        /// <summary>Scalar the Burst job multiplies; independent of the world-probe counter values.</summary>
        private const int JobSourceValue = 31;

        /// <summary>Payload array length the Burst job adds to its scaled source.</summary>
        private const int JobPayloadLength = 4;

        private const int ExpectedAggregate = JobPayloadLength + (JobSourceValue * 3);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            ProbeArguments arguments = ProbeArguments.Parse(Environment.GetCommandLineArgs());
            if (!arguments.IsProbeInvocation)
            {
                return;
            }

            Run(arguments);
        }

        internal static void Run(ProbeArguments arguments)
        {
            // Every mode runs in the same player and result shape, but under its own task identity.
            ProbeReport report = CreateReport(arguments);

            if (!arguments.HasResultPath)
            {
                Debug.LogError(
                    "[GC001] probe invoked without -probeResult <path>; the probe cannot record evidence. "
                    + "Exiting with code 1.");
                report.FailUnexpectedly("missing -probeResult <path> argument");
                Application.Quit(report.ExitCode);
                return;
            }

            try
            {
                if (arguments.MissingRegistration)
                {
                    RunCatalogIntegrityProbe(report);
                    RunMissingRegistrationProbe(report);
                    report.CompleteNegative();
                }
                else if (arguments.WorldDispatch)
                {
                    ProbeWorldDispatch.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W1Gate)
                {
                    ProbeW1Gate.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W2Gate)
                {
                    ProbeW2Gate.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Narrative)
                {
                    ProbeNarrative.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Gc013)
                {
                    ProbeGc013.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Cards)
                {
                    ProbeCards.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W3Gate)
                {
                    ProbeW3Gate.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W4Profile)
                {
                    ProbeW4Profile.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W4Gate)
                {
                    ProbeW4Gate.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Faults)
                {
                    ProbeFaults.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Gc018)
                {
                    ProbeGc018.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Gc019)
                {
                    ProbeGc019.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W5Gate)
                {
                    ProbeW5Gate.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Traversal)
                {
                    ProbeTraversal.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Gc021)
                {
                    ProbeGc021.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.LifecycleStress)
                {
                    ProbeLifecycleStress.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.Replay)
                {
                    ProbeReplay.Run(report);
                    report.CompletePositive();
                }
                else if (arguments.W6Gate)
                {
                    ProbeW6Gate.Run(report);
                    report.CompletePositive();
                }
                else
                {
                    RunAotRootsProbe(report);
                    RunWorldProbe(report);
                    RunBurstJobProbe(report);
                    RunSerializationProbe(report);
                    RunClosedGenericHandlerProbe(report);
                    RunLateMountProbe(report);
                    RunProductionCatalogProbe(report);
                    RunCatalogIntegrityProbe(report);
                    report.CompletePositive();
                }
            }
            catch (Exception exception)
            {
                report.FailUnexpectedly(
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message);
            }

            WriteReport(report);
            Application.Quit(report.ExitCode);
        }

        /// <summary>
        /// The report identity of one mode. The order of the tests is the order the modes were added and is never
        /// the observable that decides anything: exactly one mode flag is ever set by one player invocation, and a
        /// plain run sets none so it keeps the default positive identity.
        /// </summary>
        private static ProbeReport CreateReport(ProbeArguments arguments)
        {
            if (arguments.WorldDispatch)
            {
                return Named("WorldDispatch", "GC-005");
            }

            if (arguments.W1Gate)
            {
                return Named("W1Gate", "W1-GATE");
            }

            if (arguments.W2Gate)
            {
                return Named("W2Gate", "W2-GATE");
            }

            if (arguments.Narrative)
            {
                return Named("Narrative", "GC-010");
            }

            if (arguments.Gc013)
            {
                return Named("Gc013", "GC-013");
            }

            if (arguments.Cards)
            {
                return Named("Cards", "GC-011");
            }

            if (arguments.W3Gate)
            {
                return Named("W3Gate", "W3-GATE");
            }

            if (arguments.W4Profile)
            {
                return Named("W4Profile", "GC-012");
            }

            if (arguments.W4Gate)
            {
                return Named("W4Gate", "W4-GATE");
            }

            if (arguments.Faults)
            {
                return Named("Faults", "GC-017");
            }

            if (arguments.Gc018)
            {
                return Named("Gc018", "GC-018");
            }

            if (arguments.Gc019)
            {
                return Named("Gc019", "GC-019");
            }

            if (arguments.W5Gate)
            {
                return Named("W5Gate", "W5-GATE");
            }

            if (arguments.Traversal)
            {
                return Named("Traversal", "GC-020");
            }

            if (arguments.Gc021)
            {
                return Named("Gc021", "GC-021");
            }

            if (arguments.LifecycleStress)
            {
                return Named("LifecycleStress", "GC-022");
            }

            if (arguments.Replay)
            {
                return Named("Replay", "GC-023");
            }

            if (arguments.W6Gate)
            {
                return Named("W6Gate", "W6-GATE");
            }

            return new ProbeReport(
                arguments.MissingRegistration ? "MissingRegistration" : "Positive",
                ProbeEnvironment.DeclaredUnityVersion,
                ProbeEnvironment.DeclaredTarget);
        }

        private static ProbeReport Named(string mode, string task)
            => new ProbeReport(
                mode,
                ProbeEnvironment.DeclaredUnityVersion,
                ProbeEnvironment.DeclaredTarget,
                task);

        private static void RunAotRootsProbe(ProbeReport report)
        {
            const string name = "generated-aot-roots";
            try
            {
                ProbeCatalog.RootClosedGenericInstantiations();
                FactoryKey rootedHandlerKey = ProbeCatalog.HandlerRegistrations[0].Key;
                bool registered = ProbeCatalog.HasClosedGenericRoots;
                string detail = "rooted closed generic job ProbeAggregateJob<ProbeVector3Value> and closed generic "
                    + "handler ProbeScalarHandler<ProbeAmount>; handler key=" + rootedHandlerKey
                    + "; closed generic roots present=" + registered;
                bool pass = registered && rootedHandlerKey.Equals(ProbeKeys.ClosedGenericHandlerKey);
                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunWorldProbe(ProbeReport report)
        {
            const string name = "entities-world-entity-query";
            World? world = null;
            try
            {
                world = new World(WorldName);
                EntityManager entityManager = world.EntityManager;

                int[] values = { 7, 11, 13 };
                for (int i = 0; i < values.Length; i++)
                {
                    Entity entity = entityManager.CreateEntity(typeof(ProbeCounter));
                    entityManager.SetComponentData(entity, new ProbeCounter { Value = values[i] });
                }

                using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<ProbeCounter>()))
                using (EntityQuery emptyQuery =
                    entityManager.CreateEntityQuery(ComponentType.ReadOnly<ProbeAbsent>()))
                {
                    int count = query.CalculateEntityCount();
                    bool emptyQueryIsEmpty = emptyQuery.IsEmpty;
                    NativeArray<ProbeCounter> counters =
                        query.ToComponentDataArray<ProbeCounter>(Allocator.Temp);
                    try
                    {
                        int sum = 0;
                        for (int i = 0; i < counters.Length; i++)
                        {
                            sum += counters[i].Value;
                        }

                        string detail = "world='" + world.Name + "'; matched=" + count
                            + "; arrayLength=" + counters.Length + "; sum=" + sum
                            + "; absentComponentQueryIsEmpty=" + emptyQueryIsEmpty;
                        bool pass = count == ExpectedEntityCount
                            && counters.Length == ExpectedEntityCount
                            && sum == ExpectedCounterSum
                            && emptyQueryIsEmpty;
                        report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
                    }
                    finally
                    {
                        counters.Dispose();
                    }
                }
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                if (world != null)
                {
                    world.Dispose();
                }
            }
        }

        private static void RunBurstJobProbe(ProbeReport report)
        {
            const string name = "burst-generic-job";
            try
            {
                bool burstEnabled = ProbeEnvironment.BurstCompilerEnabled;
                NativeArray<ProbeVector3Value> payloads =
                    new NativeArray<ProbeVector3Value>(JobPayloadLength, Allocator.TempJob);
                NativeArray<int> sources = new NativeArray<int>(1, Allocator.TempJob);
                NativeArray<int> result = new NativeArray<int>(1, Allocator.TempJob);
                try
                {
                    for (int i = 0; i < payloads.Length; i++)
                    {
                        payloads[i] = new ProbeVector3Value(i, i + 1, i + 2);
                    }

                    sources[0] = JobSourceValue;
                    var job = new ProbeAggregateJob<ProbeVector3Value>(payloads, sources, result, 3);
                    JobHandle handle = job.Schedule();
                    handle.Complete();

                    int observed = result[0];
                    string detail = "BurstCompiler.IsEnabled=" + burstEnabled
                        + "; observed=" + observed + "; expected=" + ExpectedAggregate
                        + "; completed=true";
                    report.Add(
                        burstEnabled && observed == ExpectedAggregate
                            ? ProbeOutcome.Pass(name, detail)
                            : ProbeOutcome.Fail(
                                name,
                                detail
                                + (burstEnabled
                                    ? "; aggregate mismatch"
                                    : "; Burst is not enabled in this player, so the AOT gate cannot pass")));
                }
                finally
                {
                    payloads.Dispose();
                    sources.Dispose();
                    result.Dispose();
                }
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunSerializationProbe(ProbeReport report)
        {
            const string name = "canonical-bytes-roundtrip";
            try
            {
                var record = new ProbeRecord(
                    0x0102030405060708UL, 0xF1F2F3F4F5F6F7F8UL, 3U, -123456789, 0x5A);
                byte[] encoded = new byte[ProbeCanonicalCodec.EncodedLength];
                ProbeCanonicalCodec.Encode(record, encoded);

                bool bigEndianHigh = encoded[0] == 0x01 && encoded[7] == 0x08;
                bool bigEndianLow = encoded[8] == 0xF1 && encoded[15] == 0xF8;
                bool bigEndianVersion = encoded[16] == 0x00 && encoded[19] == 0x03;
                // -123456789 encodes as unsigned 0xF8A432EB, most significant byte first.
                bool bigEndianValue = encoded[20] == 0xF8 && encoded[23] == 0xEB;
                bool flagsMatch = encoded[24] == 0x5A;

                bool decoded = ProbeCanonicalCodec.TryDecode(encoded, encoded.Length, out ProbeRecord roundTripped);
                bool equal = decoded && roundTripped.Equals(record);
                bool truncatedRejected =
                    !ProbeCanonicalCodec.TryDecode(encoded, encoded.Length - 1, out ProbeRecord _);
                bool oversizeRejected =
                    !ProbeCanonicalCodec.TryDecode(encoded, encoded.Length + 1, out ProbeRecord _);

                string detail = "length=" + encoded.Length
                    + "; bigEndianHigh=" + bigEndianHigh
                    + "; bigEndianLow=" + bigEndianLow
                    + "; bigEndianVersion=" + bigEndianVersion
                    + "; bigEndianIntegerValue=" + bigEndianValue
                    + "; flagsMatch=" + flagsMatch
                    + "; roundTrippedEqual=" + equal
                    + "; truncatedLengthRejected=" + truncatedRejected
                    + "; oversizeLengthRejected=" + oversizeRejected;

                bool pass = bigEndianHigh && bigEndianLow && bigEndianVersion && bigEndianValue
                    && flagsMatch && equal && truncatedRejected && oversizeRejected;
                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunClosedGenericHandlerProbe(ProbeReport report)
        {
            const string name = "generated-closed-generic-handler";
            try
            {
                if (!ProbeCatalog.TryGetHandler(ProbeKeys.ClosedGenericHandlerKey, out var handler)
                    || handler == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no handler registration for "
                            + ProbeKeys.ClosedGenericHandlerKey));
                    return;
                }

                var input = new ProbeAmount(6, 7);
                int observed = handler.Handle(input);
                FactoryKey rooted = ProbeAotRoots.TrackHandler(handler);
                string detail = "handlerKey=" + rooted + "; input=" + input
                    + "; observed=" + observed + "; expected=" + input.Scalar;
                report.Add(
                    observed == input.Scalar && rooted.Equals(ProbeKeys.ClosedGenericHandlerKey)
                        ? ProbeOutcome.Pass(name, detail)
                        : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunLateMountProbe(ProbeReport report)
        {
            const string name = "late-mount-linked-inactive-plugin";
            try
            {
                if (!ProbeCatalog.TryGetPluginFactory(ProbeKeys.FixturePluginKey, out var factory)
                    || factory == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no plugin registration for " + ProbeKeys.FixturePluginKey));
                    return;
                }

                int countBeforeMount = factory.CreatedInstanceCount;
                if (countBeforeMount != 0)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the fixture plugin was already instantiated " + countBeforeMount
                            + " time(s) before the late mount, so it is not linked-but-inactive"));
                    return;
                }

                if (!ProbeCatalog.TryGetHandler(ProbeKeys.ClosedGenericHandlerKey, out var handler)
                    || handler == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no handler registration for "
                            + ProbeKeys.ClosedGenericHandlerKey));
                    return;
                }

                var input = new ProbeAmount(9, 4);

                // First mount: the plugin is created only here, through the generated factory key.
                int firstResult;
                {
                    IProbePlugin instance = factory.Create();
                    firstResult = instance.ExecuteClosedHandler(handler, input);
                }

                int countAfterFirstMount = factory.CreatedInstanceCount;

                // Release the first instance reference (unmount) and mount a second instance through the same key.
                int secondResult;
                {
                    IProbePlugin instance = factory.Create();
                    secondResult = instance.ExecuteClosedHandler(handler, input);
                }

                int countAfterSecondMount = factory.CreatedInstanceCount;

                string detail = "pluginKey=" + factory.PluginKey
                    + "; stableName='" + factory.StableName + "'"
                    + "; createdBeforeMount=" + countBeforeMount
                    + "; createdAfterFirstMount=" + countAfterFirstMount
                    + "; createdAfterSecondMount=" + countAfterSecondMount
                    + "; firstResult=" + firstResult + "; secondResult=" + secondResult
                    + "; expected=" + input.Scalar
                    + "; preservation=" + ProbeEnvironment.FixturePluginPreservation;

                bool pass = firstResult == input.Scalar
                    && secondResult == input.Scalar
                    && countAfterFirstMount == 1
                    && countAfterSecondMount == 2
                    && factory.PluginKey.Equals(ProbeKeys.FixturePlugin);
                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        /// <summary>
        /// Production contract probe: validates the generated registration tables with the production
        /// <see cref="ImmutableCatalog"/>, compares the rebuilt fingerprint with the literal emitted into the
        /// generated file, resolves the fixture plugin and the probe schema by generated key, and round-trips a
        /// value through the generated serializer (P-009, P-028, P-054, P-055).
        /// </summary>
        private static void RunProductionCatalogProbe(ProbeReport report)
        {
            const string name = "production-contract-catalog";
            try
            {
                CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash observed);
                if (build.Catalog == null)
                {
                    report.Add(ProbeOutcome.Fail(
                        name,
                        "the production catalog rejected the generated tables: " + build.Describe()));
                    return;
                }

                bool fingerprintMatches = string.Equals(
                    observed.ToHex(),
                    ProbeCatalog.CatalogFingerprint,
                    StringComparison.Ordinal);

                var probeRecordSchema = new SchemaRef(
                    new SchemaId(new Id128(0x4BF5B435956D00ADUL, 0x605292D344334459UL)),
                    1U);
                CatalogLookup plugin = build.Catalog.Lookup(ProbeCatalog.FixturePluginKey);
                CatalogLookup handler = build.Catalog.Lookup(ProbeCatalog.ClosedGenericHandlerKey);
                CatalogLookup schema = build.Catalog.LookupSchema(probeRecordSchema);
                bool serializerBound = build.Catalog.TryGetSerializer(probeRecordSchema, out ISchemaSerializer? serializer)
                    && serializer != null
                    && serializer!.Schema.Equals(probeRecordSchema);
                bool featureSupported = build.Catalog.SupportedFeatureIds.Count > 0
                    && build.Catalog.SupportsFeature(build.Catalog.SupportedFeatureIds[0]);
                CatalogLookup unknownSchema = build.Catalog.LookupSchema(new SchemaRef(
                    new SchemaId(ProbeKeys.AbsentFixturePlugin.ToId128()),
                    1U));
                bool unknownSchemaRejected = !unknownSchema.Found
                    && unknownSchema.Code == DiagnosticCode.MissingDependency;

                var value = new ProbeCatalog.ProbeRecordValue(0x0102030405060708UL, 0xF1F2F3F4F5F6F7F8UL, 3U, -123456789, 0x5AU);
                var generatedSerializer = new ProbeCatalog.ProbeRecordSerializer();
                byte[] document = generatedSerializer.Serialize(value);
                bool roundTrips = generatedSerializer.TryDeserialize(document, out ProbeCatalog.ProbeRecordValue roundTripped, out EnvelopeError _)
                    && roundTripped.High == value.High
                    && roundTripped.Low == value.Low
                    && roundTripped.Version == value.Version
                    && roundTripped.Value == value.Value
                    && roundTripped.Flags == value.Flags;

                byte[] tampered = (byte[])document.Clone();
                tampered[document.Length - 3] ^= 0xFF;
                bool tamperRejected = !generatedSerializer.TryDeserialize(tampered, out ProbeCatalog.ProbeRecordValue _, out EnvelopeError tamperError)
                    && tamperError == EnvelopeError.ChecksumMismatch;

                byte[] truncated = new byte[document.Length - 1];
                Buffer.BlockCopy(document, 0, truncated, 0, truncated.Length);
                bool truncationRejected = !generatedSerializer.TryDeserialize(truncated, out ProbeCatalog.ProbeRecordValue _, out EnvelopeError truncationError)
                    && truncationError == EnvelopeError.Truncated;

                string detail = "catalogFingerprint=" + observed.ToHex()
                    + "; fingerprintMatchesLiteral=" + fingerprintMatches
                    + "; pluginLookup=" + plugin.Describe()
                    + "; handlerLookup=" + handler.Describe()
                    + "; schemaLookup=" + schema.Describe()
                    + "; serializerBound=" + serializerBound
                    + "; featureSupported=" + featureSupported
                    + "; documentBytes=" + document.Length
                    + "; roundTrips=" + roundTrips
                    + "; tamperRejected=" + tamperRejected
                    + "; truncationRejected=" + truncationRejected
                    + "; unknownSchemaLookup=" + unknownSchema.Describe()
                    + "; supportedFeatures=" + build.Catalog.SupportedFeatureIds.Count;

                bool pass = fingerprintMatches
                    && plugin.Found
                    && handler.Found
                    && handler.Factory != null
                    && handler.Factory.Kind == FactoryKind.Handler
                    && schema.Found
                    && serializerBound
                    && featureSupported
                    && roundTrips
                    && tamperRejected
                    && truncationRejected
                    && unknownSchemaRejected;
                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunCatalogIntegrityProbe(ProbeReport report)
        {
            const string name = "generated-catalog-integrity";
            try
            {
                FactoryKey[] pluginKeys = ProbeCatalog.PluginKeys;
                FactoryKey[] handlerKeys = ProbeCatalog.HandlerKeys;

                bool hasFixture = ContainsKey(pluginKeys, ProbeKeys.FixturePluginKey);
                bool hasHandler = ContainsKey(handlerKeys, ProbeKeys.ClosedGenericHandlerKey);
                bool absentKeyMissing = !ContainsKey(pluginKeys, ProbeKeys.AbsentFixturePluginKey)
                    && !ContainsKey(handlerKeys, ProbeKeys.AbsentFixturePluginKey);
                bool uniqueKeys = AllDistinct(pluginKeys) && AllDistinct(handlerKeys);

                string detail = "generatedFile=" + ProbeCatalog.GeneratedFileName
                    + "; pluginRegistrations=" + pluginKeys.Length
                    + "; handlerRegistrations=" + handlerKeys.Length
                    + "; hasFixturePlugin=" + hasFixture
                    + "; hasClosedGenericHandler=" + hasHandler
                    + "; absentKeyMissing=" + absentKeyMissing
                    + "; keysUnique=" + uniqueKeys
                    + "; keysAreProductionFactoryKeys=true"
                    + "; catalogHash=" + ProbeCatalog.CatalogFileHash
                    + "; hashScope=" + ProbeCatalog.CatalogFileHashScope;

                bool pass = pluginKeys.Length == 1
                    && handlerKeys.Length == 1
                    && hasFixture
                    && hasHandler
                    && absentKeyMissing
                    && uniqueKeys;
                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunMissingRegistrationProbe(ProbeReport report)
        {
            const string name = "missing-registration-detected";
            try
            {
                bool pluginResolved = ProbeCatalog.TryGetPluginFactory(
                    ProbeKeys.AbsentFixturePluginKey, out IProbePluginFactory? factory);
                bool handlerResolved = ProbeCatalog.TryGetHandler(
                    ProbeKeys.AbsentFixturePluginKey, out IProbeHandler<ProbeAmount, int>? handler);
                bool createdAnything = pluginResolved && factory != null && factory.CreatedInstanceCount > 0;

                CatalogBuildResult catalogResult = ProbeCatalog.BuildCatalog();
                CatalogLookup productionLookup = catalogResult.Catalog == null
                    ? CatalogLookup.MissingKey(ProbeKeys.AbsentFixturePluginKey)
                    : catalogResult.Catalog.Lookup(ProbeKeys.AbsentFixturePluginKey);
                bool productionMissReported = !productionLookup.Found
                    && productionLookup.Code == DiagnosticCode.MissingDependency;

                string detail = "requestedKey=" + ProbeKeys.AbsentFixturePluginKey
                    + "; pluginResolved=" + pluginResolved
                    + "; handlerResolved=" + handlerResolved
                    + "; instanceCreated=" + createdAnything
                    + "; productionCatalogLookup=" + productionLookup.Describe()
                    + "; productionMissReported=" + productionMissReported
                    + "; catalogHash=" + ProbeCatalog.CatalogFileHash;
                if (pluginResolved || handlerResolved || createdAnything || handler != null || !productionMissReported)
                {
                    report.Add(ProbeOutcome.Fail(name, detail + "; a missing registration was resolved"));
                    return;
                }

                report.Add(ProbeOutcome.Pass(name, detail));
                report.Add(
                    ProbeOutcome.ExpectedNegative(
                        "missing-registration-report",
                        "the generated catalog and the production immutable catalog returned no factory for stable key "
                        + ProbeKeys.AbsentFixturePluginKey
                        + " (stable name '" + ProbeKeys.AbsentFixturePluginStableName
                        + "'); resolution failed explicitly instead of constructing an instance through reflection"));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static bool ContainsKey(FactoryKey[] keys, FactoryKey key)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Equals(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AllDistinct(FactoryKey[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                for (int j = i + 1; j < keys.Length; j++)
                {
                    if (keys[i].Equals(keys[j]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string Describe(Exception exception)
            => "unhandled " + exception.GetType().FullName + ": " + exception.Message;

        private static void WriteReport(ProbeReport report)
        {
            string? path = ProbeArguments.Parse(Environment.GetCommandLineArgs()).ResultPath;
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[GC001] no result path; nothing written.");
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, report.ToJson());
                Debug.Log("[GC001] probe result (" + report.Result + ", exit " + report.ExitCode
                    + ") written to " + path);
            }
            catch (Exception exception)
            {
                Debug.LogError("[GC001] failed to write probe result to " + path + ": " + exception.Message);
            }
        }
    }
}
