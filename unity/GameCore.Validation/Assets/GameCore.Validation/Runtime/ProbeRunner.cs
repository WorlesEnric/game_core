#nullable enable
using System;
using System.IO;
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
            // The GC-005 owned-world mode runs in the same player and reports into the same result shape, but under
            // its own task id so the GC-001 outcome is never restated as GC-005 evidence.
            ProbeReport report = arguments.WorldDispatch
                ? new ProbeReport(
                    "WorldDispatch",
                    ProbeEnvironment.DeclaredUnityVersion,
                    ProbeEnvironment.DeclaredTarget,
                    "GC-005")
                : new ProbeReport(
                    arguments.MissingRegistration ? "MissingRegistration" : "Positive",
                    ProbeEnvironment.DeclaredUnityVersion,
                    ProbeEnvironment.DeclaredTarget);

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
                else
                {
                    RunAotRootsProbe(report);
                    RunWorldProbe(report);
                    RunBurstJobProbe(report);
                    RunSerializationProbe(report);
                    RunClosedGenericHandlerProbe(report);
                    RunLateMountProbe(report);
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

        private static void RunAotRootsProbe(ProbeReport report)
        {
            const string name = "generated-aot-roots";
            try
            {
                ProbeKey rootedHandlerKey = ProbeCatalog.RootClosedGenericInstantiations();
                bool registered = ProbeCatalog.HasGenericJobRegistration;
                string detail = "rooted closed generic job ProbeAggregateJob<ProbeVector3Value> and closed generic "
                    + "handler ProbeScalarHandler<ProbeAmount>; handler key=" + rootedHandlerKey
                    + "; RegisterGenericJobType present=" + registered;
                bool pass = registered && rootedHandlerKey.Equals(ProbeKeys.ClosedGenericHandler);
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
                if (!ProbeCatalog.TryGetHandler(ProbeKeys.ClosedGenericHandler, out var handler)
                    || handler == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no handler registration for "
                            + ProbeKeys.ClosedGenericHandler));
                    return;
                }

                var input = new ProbeAmount(6, 7);
                int observed = handler.Handle(input);
                ProbeKey rooted = ProbeAotRoots.TrackHandler(handler);
                string detail = "handlerKey=" + rooted + "; input=" + input
                    + "; observed=" + observed + "; expected=" + input.Scalar;
                report.Add(
                    observed == input.Scalar && rooted.Equals(ProbeKeys.ClosedGenericHandler)
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
                if (!ProbeCatalog.TryGetPluginFactory(ProbeKeys.FixturePlugin, out var factory)
                    || factory == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no plugin registration for " + ProbeKeys.FixturePlugin));
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

                if (!ProbeCatalog.TryGetHandler(ProbeKeys.ClosedGenericHandler, out var handler)
                    || handler == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            name,
                            "the generated catalog has no handler registration for "
                            + ProbeKeys.ClosedGenericHandler));
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

        private static void RunCatalogIntegrityProbe(ProbeReport report)
        {
            const string name = "generated-catalog-integrity";
            try
            {
                ProbeKey[] pluginKeys = ProbeCatalog.PluginKeys;
                ProbeKey[] handlerKeys = ProbeCatalog.HandlerKeys;

                bool hasFixture = ContainsKey(pluginKeys, ProbeKeys.FixturePlugin);
                bool hasHandler = ContainsKey(handlerKeys, ProbeKeys.ClosedGenericHandler);
                bool absentKeyMissing = !ContainsKey(pluginKeys, ProbeKeys.AbsentFixturePlugin)
                    && !ContainsKey(handlerKeys, ProbeKeys.AbsentFixturePlugin);
                bool uniqueKeys = AllDistinct(pluginKeys) && AllDistinct(handlerKeys);

                string detail = "generatedFile=" + ProbeCatalog.GeneratedFileName
                    + "; pluginRegistrations=" + pluginKeys.Length
                    + "; handlerRegistrations=" + handlerKeys.Length
                    + "; hasFixturePlugin=" + hasFixture
                    + "; hasClosedGenericHandler=" + hasHandler
                    + "; absentKeyMissing=" + absentKeyMissing
                    + "; keysUnique=" + uniqueKeys
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
                    ProbeKeys.AbsentFixturePlugin, out IProbePluginFactory? factory);
                bool handlerResolved = ProbeCatalog.TryGetHandler(
                    ProbeKeys.AbsentFixturePlugin, out IProbeHandler<ProbeAmount, int>? handler);
                bool createdAnything = pluginResolved && factory != null && factory.CreatedInstanceCount > 0;

                string detail = "requestedKey=" + ProbeKeys.AbsentFixturePlugin
                    + "; pluginResolved=" + pluginResolved
                    + "; handlerResolved=" + handlerResolved
                    + "; instanceCreated=" + createdAnything
                    + "; catalogHash=" + ProbeCatalog.CatalogFileHash;
                if (pluginResolved || handlerResolved || createdAnything || handler != null)
                {
                    report.Add(ProbeOutcome.Fail(name, detail + "; a missing registration was resolved"));
                    return;
                }

                report.Add(ProbeOutcome.Pass(name, detail));
                report.Add(
                    ProbeOutcome.ExpectedNegative(
                        "missing-registration-report",
                        "the generated catalog returned no factory for stable key " + ProbeKeys.AbsentFixturePlugin
                        + " (stable name '" + ProbeKeys.AbsentFixturePluginStableName
                        + "'); resolution failed explicitly instead of constructing an instance through reflection"));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static bool ContainsKey(ProbeKey[] keys, ProbeKey key)
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

        private static bool AllDistinct(ProbeKey[] keys)
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
