#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using UnityEngine;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Registers the application composition root of the qualification player. It runs at
    /// <c>SubsystemRegistration</c>, i.e. before the Entities bootstrap performs world initialization, so the single
    /// application bootstrap can create the fixture world inside the IL2CPP player (04 s3, 04 s9).
    /// </summary>
    public static class ProbeComposition
    {
        /// <summary>Application world shape: a fault-capable command-driven fixture with the fault flag disabled.</summary>
        public static GameCoreApplicationCompositionRoot CreateRoot()
        {
            return new GameCoreApplicationCompositionRoot(
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                FixtureRegistration.CommandWorldDefinition,
                TemporalModel.CommandDriven,
                null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterApplicationComposition()
        {
            GameCoreApplicationComposition.RootFactory = CreateRoot;
        }
    }

    /// <summary>
    /// GC-005 standalone probe: owned worlds and guarded execution dispatch running inside the built IL2CPP player.
    /// Every check reads real Entities storage (component counters written by the fixture systems), never a managed
    /// model, and reports into the same structured JSON result as the GC-001 probes.
    /// </summary>
    public static class ProbeWorldDispatch
    {
        private const ulong SessionSalt = 0x50524F4245574743UL;

        private static readonly Id128 Issuer = new Id128(0x50524F4245495355UL, 1UL);

        private static ulong sessionSequence;

        public static void Run(ProbeReport report)
        {
            RunBootstrapAndLoopRouteProbe(report);
            RunTwoWorldsIndependentProbe(report);
            RunCommandDrivenIdleProbe(report);
            RunNoDoubleUpdateProbe(report);
            RunFixedStepDebtProbe(report);
            RunGuardedFailStopProbe(report);
        }

        private static WorldId NextSession()
        {
            sessionSequence++;
            return new WorldId(new Id128(SessionSalt, sessionSequence));
        }

        private static OperationId NextOperation(WorldId world, ulong sequence)
            => new OperationId(world, Issuer, sequence);

        private static UnityWorldHost? CreateCommandWorld(bool includeFaultStage, out string failure)
        {
            WorldId world = NextSession();
            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, NextOperation(world, 1UL), ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            if (!created || host == null)
            {
                failure = "world creation failed: " + result.Code + " " + result.Detail;
                return null;
            }

            failure = string.Empty;
            return host;
        }

        private static UnityWorldHost? CreateFixedWorld(out string failure)
        {
            WorldId world = NextSession();
            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.FixedStepRequest(world, NextOperation(world, 1UL), ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.FixedStep, includeFaultStage: false),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            if (!created || host == null)
            {
                failure = "fixed-step world creation failed: " + result.Code + " " + result.Detail;
                return null;
            }

            failure = string.Empty;
            return host;
        }

        private static void ReleaseWorld(UnityWorldHost host, ulong operationSequence)
        {
            if (host == null)
            {
                return;
            }

            host.Stop(NextOperation(host.World, operationSequence), "probe teardown");
            host.Dispose();
        }

        private static bool TryReadTrail(UnityWorldHost host, out FixtureTrail trail)
            => FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out trail);

        private static void RunBootstrapAndLoopRouteProbe(ProbeReport report)
        {
            const string name = "world-bootstrap-and-loop-route";
            try
            {
                int bootstrapCount = GameCoreApplicationBootstrap.BootstrapCount;
                string worldName = GameCoreApplicationBootstrap.LastWorldName;
                int routesBefore = GameCorePlayerLoopInstaller.CountInstalledNodes();
                int routesAfterInstall = GameCorePlayerLoopInstaller.EnsureInstalled();
                int registryCount = UnityWorldRegistry.Count;
                bool namesIncarnation = worldName.Length > 0 && worldName.IndexOf(':') >= 0;

                bool pass = bootstrapCount == 1
                    && routesBefore == 1
                    && routesAfterInstall == 1
                    && registryCount >= 1
                    && namesIncarnation;

                string detail = "bootstraps=" + bootstrapCount
                    + "; loopRoutesBeforeInstall=" + routesBefore
                    + "; loopRoutesAfterInstall=" + routesAfterInstall
                    + "; registeredWorlds=" + registryCount
                    + "; applicationWorld='" + worldName + "'"
                    + "; fallbacks=" + GameCoreApplicationBootstrap.FallbackCount
                    + "; bootstrapCode=" + GameCoreApplicationBootstrap.LastCode;

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
        }

        private static void RunTwoWorldsIndependentProbe(ProbeReport report)
        {
            const string name = "world-two-worlds-independent";
            UnityWorldHost? first = null;
            UnityWorldHost? second = null;
            try
            {
                first = CreateCommandWorld(true, out string firstFailure);
                second = CreateCommandWorld(true, out string secondFailure);
                if (first == null || second == null)
                {
                    report.Add(ProbeOutcome.Fail(name, firstFailure + " | " + secondFailure));
                    return;
                }

                first.NotifyCommandAdmitted(1U);
                second.NotifyCommandAdmitted(1U);
                first.PumpFrame(1_000_000UL);
                second.PumpFrame(1_000_000UL);

                first.NotifyCommandAdmitted(1U);
                first.PumpFrame(2_000_000UL);
                second.PumpFrame(2_000_000UL);

                bool sessionsDiffer = !first.World.Session.Equals(second.World.Session);
                bool firstAdvanced = first.CurrentStep.Value == 2UL;
                bool secondStayed = second.CurrentStep.Value == 1UL;
                TryReadTrail(first, out FixtureTrail firstTrail);
                TryReadTrail(second, out FixtureTrail secondTrail);

                bool countersIndependent = firstTrail.AcceptCount == 2 && secondTrail.AcceptCount == 1;
                bool idleWorldStillPresents = secondTrail.IngressCount == 2 && secondTrail.OutputCount == 2;

                bool pass = sessionsDiffer && firstAdvanced && secondStayed && countersIndependent && idleWorldStillPresents;
                string detail = "sessionA=" + first.World.Session + "; sessionB=" + second.World.Session
                    + "; stepsA=" + first.CurrentStep.Value + "; stepsB=" + second.CurrentStep.Value
                    + "; acceptA=" + firstTrail.AcceptCount + "; acceptB=" + secondTrail.AcceptCount
                    + "; ingressB=" + secondTrail.IngressCount + "; outputB=" + secondTrail.OutputCount;

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                ReleaseWorld(first, 90UL);
                ReleaseWorld(second, 91UL);
            }
        }

        private static void RunCommandDrivenIdleProbe(ProbeReport report)
        {
            const string name = "world-command-driven-idle-zero-steps";
            UnityWorldHost? host = null;
            try
            {
                host = CreateCommandWorld(true, out string failure);
                if (host == null)
                {
                    report.Add(ProbeOutcome.Fail(name, failure));
                    return;
                }

                const int frames = 60;
                for (int frame = 1; frame <= frames; frame++)
                {
                    host.PumpFrame((ulong)frame * 16_000UL);
                }

                TryReadTrail(host, out FixtureTrail trail);
                bool pass = host.CurrentStep.Value == 0UL
                    && host.Driver.CommittedStepCount == 0
                    && host.StepGroup.DispatchRunCount == 0
                    && host.Publications.PublishedCount == 1
                    && trail.AcceptCount == 0
                    && trail.IngressCount == frames
                    && trail.OutputCount == frames
                    && host.RetainedDebt.Ticks == 0UL;

                string detail = "frames=" + frames
                    + "; steps=" + host.CurrentStep.Value
                    + "; dispatchRuns=" + host.StepGroup.DispatchRunCount
                    + "; publishedImages=" + host.Publications.PublishedCount
                    + "; acceptCount=" + trail.AcceptCount
                    + "; ingressCount=" + trail.IngressCount
                    + "; outputCount=" + trail.OutputCount
                    + "; retainedDebtTicks=" + host.RetainedDebt.Ticks;

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                ReleaseWorld(host, 90UL);
            }
        }

        private static void RunNoDoubleUpdateProbe(ProbeReport report)
        {
            const string name = "world-no-system-double-update";
            UnityWorldHost? host = null;
            try
            {
                host = CreateCommandWorld(true, out string failure);
                if (host == null)
                {
                    report.Add(ProbeOutcome.Fail(name, failure));
                    return;
                }

                int entries = host.StepGroup.InstalledPlan.Entries.Count;
                const int steps = 3;
                for (int step = 1; step <= steps; step++)
                {
                    host.NotifyCommandAdmitted(1U);
                    host.PumpFrame((ulong)step * 16_000UL);
                }

                TryReadTrail(host, out FixtureTrail trail);
                int counter = FixtureWorldState.ReadCounter(host.EntityWorld.EntityManager);

                bool pass = entries == 4
                    && host.CurrentStep.Value == steps
                    && host.StepGroup.TotalDispatchedCount == steps * entries
                    && host.Publications.PublishedCount == steps + 1
                    && trail.AcceptCount == steps
                    && trail.SettleCount == steps
                    && trail.FaultCount == steps
                    && trail.ProjectCount == steps
                    && counter == steps * 111
                    && host.Ledger.OutstandingJobCount == 0;

                string detail = "entriesPerStep=" + entries
                    + "; steps=" + host.CurrentStep.Value
                    + "; totalDispatched=" + host.StepGroup.TotalDispatchedCount
                    + "; publishedImages=" + host.Publications.PublishedCount
                    + "; accept=" + trail.AcceptCount
                    + "; settle=" + trail.SettleCount
                    + "; fault=" + trail.FaultCount
                    + "; project=" + trail.ProjectCount
                    + "; counter=" + counter
                    + "; outstandingJobs=" + host.Ledger.OutstandingJobCount;

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                ReleaseWorld(host, 90UL);
            }
        }

        private static void RunFixedStepDebtProbe(ProbeReport report)
        {
            const string name = "world-fixed-step-debt-and-clock";
            UnityWorldHost? host = null;
            try
            {
                host = CreateFixedWorld(out string failure);
                if (host == null)
                {
                    report.Add(ProbeOutcome.Fail(name, failure));
                    return;
                }

                host.PumpFrame(0UL);
                WorldPumpResult first = host.PumpFrame(FixtureRegistration.HostTicksPerSecond);
                double elapsed = host.EntityWorld.Time.ElapsedTime;
                TryReadTrail(host, out FixtureTrail trail);

                bool pass = host.HostTicksPerSecond == FixtureRegistration.HostTicksPerSecond
                    && first.Sample.AdmittedSteps == 4UL
                    && host.CurrentStep.Value == 4UL
                    && host.RetainedDebt.Ticks == 9_600_000UL
                    && trail.TickCount == 4
                    && Math.Abs(elapsed - 0.03) < 1e-6;

                string detail = "hostTicksPerSecond=" + host.HostTicksPerSecond
                    + "; hostTimeOrigin=" + host.HostTimeOrigin
                    + "; admittedSteps=" + first.Sample.AdmittedSteps
                    + "; steps=" + host.CurrentStep.Value
                    + "; retainedDebtTicks=" + host.RetainedDebt.Ticks
                    + "; unmanagedTickCount=" + trail.TickCount
                    + "; worldElapsedSeconds=" + elapsed.ToString("F6", CultureInfo.InvariantCulture);

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                ReleaseWorld(host, 90UL);
            }
        }

        private static void RunGuardedFailStopProbe(ProbeReport report)
        {
            const string name = "world-guarded-fail-stop";
            const string teardownName = "world-guarded-fail-stop-teardown";
            UnityWorldHost? host = null;
            try
            {
                host = CreateCommandWorld(true, out string failure);
                if (host == null)
                {
                    report.Add(ProbeOutcome.Fail(name, failure));
                    return;
                }

                if (!FixtureWorldState.SetFaultEnabled(host.EntityWorld.EntityManager, true))
                {
                    report.Add(ProbeOutcome.Fail(name, "the fixture world state is not seeded"));
                    return;
                }

                host.NotifyCommandAdmitted(1U);
                WorldPumpResult pump = host.PumpFrame(1_000_000UL);
                TryReadTrail(host, out FixtureTrail trail);
                int counter = FixtureWorldState.ReadCounter(host.EntityWorld.EntityManager);
                int retainedJobs = host.Driver.RetainedJobs.Count;

                bool stepNotPublished = !host.Publications.HasPublished(
                    new SnapshotToken(host.World, AssemblyEpoch.First, LogicalStepId.First));

                bool pass = pump.Pumped
                    && pump.Advance != null
                    && !pump.Advance!.Accepted
                    && pump.Advance.Outcome == Outcome.Faulted
                    && pump.Advance.PublishedSnapshot == null
                    && host.Driver.IsFaulted
                    && host.Driver.FaultCode == DiagnosticCode.ApplyFault
                    && host.Lifecycle == WorldLifecycleState.Faulted
                    && host.CurrentStep.Value == 0UL
                    && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                    && host.Publications.PublishedCount == 1
                    && stepNotPublished
                    && trail.AcceptCount == 1
                    && trail.SettleCount == 1
                    && trail.FaultCount == 1
                    && trail.ProjectCount == 0
                    && counter == 111
                    && host.Ledger.OutstandingJobCount > 0
                    && host.Ledger.QuarantinedJobCount > 0
                    && retainedJobs > 0;

                string detail = "outcome=" + pump.Advance?.Outcome
                    + "; faultCode=" + host.Driver.FaultCode
                    + "; lifecycle=" + host.Lifecycle
                    + "; step=" + host.CurrentStep.Value
                    + "; epoch=" + host.CurrentEpoch.Value
                    + "; publishedImages=" + host.Publications.PublishedCount
                    + "; failedStepImagePublished=" + !stepNotPublished
                    + "; accept=" + trail.AcceptCount + "; settle=" + trail.SettleCount
                    + "; fault=" + trail.FaultCount + "; project=" + trail.ProjectCount
                    + "; counter=" + counter
                    + "; outstandingJobs=" + host.Ledger.OutstandingJobCount
                    + "; quarantinedJobs=" + host.Ledger.QuarantinedJobCount
                    + "; retainedHandles=" + retainedJobs
                    + "; faultDetail='" + host.Driver.FaultDetail + "'";

                report.Add(pass ? ProbeOutcome.Pass(name, detail) : ProbeOutcome.Fail(name, detail));

                // Teardown evidence: the pending job is settled and only then is storage released.
                OperationResult stop = host.Stop(NextOperation(host.World, 91UL), "probe teardown after fault");
                bool teardownPass = stop.Outcome == Outcome.Published
                    && host.SettledJobCount == retainedJobs
                    && host.Ledger.OutstandingJobCount == 0
                    && host.Ledger.RetainedResourceCount == 0
                    && host.Lifecycle == WorldLifecycleState.Disposed
                    && !host.IsEntityWorldCreated;

                string teardownDetail = "stopOutcome=" + stop.Outcome
                    + "; settledJobs=" + host.SettledJobCount
                    + "; outstandingJobs=" + host.Ledger.OutstandingJobCount
                    + "; retainedResources=" + host.Ledger.RetainedResourceCount
                    + "; lifecycle=" + host.Lifecycle
                    + "; entityWorldCreated=" + host.IsEntityWorldCreated
                    + "; completionFailures=" + host.Driver.RetainedJobs.CompletionFailureCount;

                report.Add(teardownPass ? ProbeOutcome.Pass(teardownName, teardownDetail) : ProbeOutcome.Fail(teardownName, teardownDetail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(name, Describe(exception)));
            }
            finally
            {
                ReleaseWorld(host, 92UL);
            }
        }

        private static string Describe(Exception exception)
            => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
    }
}
