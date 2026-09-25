// GameCore.Unity.Runtime.Recovery — recovery from initial definitions into a NEW world (GC-017).
//
// Normative sources: 00 P-002 (`WorldHost` is the sole authority for world lifecycle and creation), P-004
// (`WorldId` is a fresh 128-bit session id, never reused, including on checkpoint restore), P-005 (destroy/recreate
// invalidates handles even when a stable ID is restored), P-031 (a world that faulted after its first live write
// never resumes: its last committed image is inspectable but its live storage is unavailable except to controlled
// teardown/recovery), P-049 ("recovery creates a new `WorldId` from a verified checkpoint or initial catalog,
// validates/rebuilds composition and recipes, restores state, then reopens admission; old callbacks/handles never
// become valid") and P-035 (a created world becomes Running only after an initial validated assembly publication).
//
// Two limits are stated rather than implied, because the difference between them is the whole point of a recovery
// contract:
//
//   * **The source world is never resumed and never repaired.** Recovery reads the source's identity, definition and
//     fault state; it does not touch its ECS storage, does not pump it and does not clear its fault latch. A source
//     that is still `Running` or `Paused` is refused outright: a live world is a reconfiguration or a checkpoint
//     capture, not a recovery, and pretending otherwise would be a silent second writer of one state domain.
//   * **It restores the world's *initial* state, and claims nothing more.** Initial definitions seed a fresh world
//     from the catalog; no gameplay state, clock or random stream is carried across. Carrying committed state across
//     a recovery is checkpoint restore, which is GC-018's and is deliberately not implemented here (GC-017's
//     definition of done says the supported recovery source is explicitly initial definitions until checkpoint work
//     integrates).
//
// `IRecoveryRepair` is the seam P-049's "validates/rebuilds composition and recipes" needs, and it is also the
// deterministic failure boundary of TEST-016's last row: a destination whose reference repair fails must never be
// exposed as a running published world, so the repair runs *before* any world is created, and a failure leaves the
// registry exactly as it was.
#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Runtime.Recovery
{
    /// <summary>
    /// The step P-049 requires before a recovered world is exposed: validate and rebuild the composition, the
    /// recipes and the reference tables of the destination from the recovery source. The initial-definition source
    /// of V1 rebuilds them from the compiled catalog; GC-018 supplies the checkpoint source's reference repair.
    /// A repair reports failure as a <see cref="DiagnosticCode"/>, never as a partially built world.
    /// </summary>
    public interface IRecoveryRepair
    {
        /// <summary>Validates and rebuilds the destination's references; false rejects the recovery.</summary>
        bool TryRepair(RecoveryRequest request, out DiagnosticCode code, out string detail);
    }

    /// <summary>
    /// One recovery attempt. The destination session is caller-reserved and fresh (P-050): the host keeps the
    /// reservation for the process lifetime, so a failed or cancelled attempt is never retried under the same id.
    /// </summary>
    public sealed class RecoveryRequest
    {
        public RecoveryRequest(
            WorldId source,
            WorldId destination,
            WorldDefinitionId definition,
            TemporalModel temporalModel,
            PropagationMode mode,
            ContentHash catalogHash,
            OperationId operation,
            FixedStepSettings? fixedStep)
        {
            Source = source;
            Destination = destination;
            Definition = definition;
            TemporalModel = temporalModel;
            Mode = mode;
            CatalogHash = catalogHash;
            Operation = operation;
            FixedStep = fixedStep;
        }

        /// <summary>The world incarnation being recovered from; it is faulted, stopping or disposed, never running.</summary>
        public WorldId Source { get; }

        /// <summary>The fresh incarnation the recovered world lives in; never the source's session (P-004).</summary>
        public WorldId Destination { get; }

        /// <summary>The world definition the recovered world is created from (its initial catalog).</summary>
        public WorldDefinitionId Definition { get; }

        public TemporalModel TemporalModel { get; }

        public PropagationMode Mode { get; }

        public ContentHash CatalogHash { get; }

        /// <summary>The operation identity this recovery is admitted under (P-050).</summary>
        public OperationId Operation { get; }

        /// <summary>Required for <see cref="TemporalModel.FixedStep"/>; null for a command-driven world.</summary>
        public FixedStepSettings? FixedStep { get; }

        /// <summary>
        /// A request is well formed when both sessions are allocated, the destination is a different incarnation,
        /// and the definition is set. Nothing else about the source is assumed here; <see cref="Recover"/> checks
        /// the source's real lifecycle and definition against the registry.
        /// </summary>
        public bool IsValid =>
            !Source.Session.IsDefault
            && !Destination.Session.IsDefault
            && !Source.Session.Equals(Destination.Session)
            && !Definition.IsDefault
            && (TemporalModel != TemporalModel.FixedStep || (FixedStep != null && FixedStep.IsValid));

        /// <summary>The world-creation request of the destination; the only way this type reaches the host (O-01).</summary>
        public WorldCreateRequest ToCreateRequest() =>
            new WorldCreateRequest(Destination, Definition, TemporalModel, Mode, CatalogHash, Operation, FixedStep);

        public override string ToString() =>
            "recovery(" + Source.Session.ToString() + " -> " + Destination.Session.ToString() + ")";
    }

    /// <summary>
    /// What one recovery attempt did. Every field is read from the real modules: the source's lifecycle and fault
    /// state before and after, the catalogue/registry counts around the creation, and the destination host when one
    /// was created and exposed.
    /// </summary>
    public sealed class RecoveryReport
    {
        public RecoveryReport(
            RecoveryRequest request,
            WorldLifecycleState sourceLifecycleBefore,
            DiagnosticCode sourceFaultCode,
            int sourceFaultCountBefore,
            WorldLifecycleState sourceLifecycleAfter,
            int sourceFaultCountAfter,
            Outcome outcome,
            DiagnosticCode code,
            string detail,
            UnityWorldHost? destinationHost,
            int repairsAttempted,
            int registryCountBefore,
            int registryCountAfter)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            SourceLifecycleBefore = sourceLifecycleBefore;
            SourceFaultCode = sourceFaultCode;
            SourceFaultCountBefore = sourceFaultCountBefore;
            SourceLifecycleAfter = sourceLifecycleAfter;
            SourceFaultCountAfter = sourceFaultCountAfter;
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            DestinationHost = destinationHost;
            RepairsAttempted = repairsAttempted;
            RegistryCountBefore = registryCountBefore;
            RegistryCountAfter = registryCountAfter;
        }

        public RecoveryRequest Request { get; }

        /// <summary>The source's lifecycle when the attempt began; `Running`/`Paused` reject the attempt.</summary>
        public WorldLifecycleState SourceLifecycleBefore { get; }

        /// <summary>The source's fault code, so a recovery names what it recovered from (P-049).</summary>
        public DiagnosticCode SourceFaultCode { get; }

        public int SourceFaultCountBefore { get; }

        /// <summary>The source's lifecycle after the attempt; unchanged is the required outcome.</summary>
        public WorldLifecycleState SourceLifecycleAfter { get; }

        public int SourceFaultCountAfter { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The recovered world, or null when nothing was created or nothing was exposed.</summary>
        public UnityWorldHost? DestinationHost { get; }

        /// <summary>Reference-repair attempts made; one when a repair was supplied.</summary>
        public int RepairsAttempted { get; }

        public int RegistryCountBefore { get; }

        public int RegistryCountAfter { get; }

        /// <summary>True only when a fresh world was created, exposed and is Running on its initial assembly.</summary>
        public bool Recovered =>
            Outcome == Outcome.Published
            && DestinationHost != null
            && DestinationHost.Lifecycle == WorldLifecycleState.Running
            && DestinationHost.CurrentEpoch.Equals(AssemblyEpoch.First)
            && DestinationHost.CurrentStep.Equals(LogicalStepId.Zero);

        /// <summary>True when a destination host exists, whether or not it is still the exposed running world.</summary>
        public bool DestinationExposed => DestinationHost != null;

        /// <summary>True when the source kept its pre-attempt lifecycle and fault count: it never resumed.</summary>
        public bool SourceUnchanged =>
            SourceLifecycleBefore == SourceLifecycleAfter && SourceFaultCountBefore == SourceFaultCountAfter;

        /// <summary>True when the destination is a different world incarnation from the source (P-004).</summary>
        public bool DestinationIsFreshIncarnation =>
            DestinationHost != null && !DestinationHost.World.Session.Equals(Request.Source.Session);

        public string Describe() =>
            "outcome=" + Outcome
            + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")")
            + "; source=" + Request.Source.Session.ToString()
            + "/" + SourceLifecycleBefore
            + "->" + SourceLifecycleAfter
            + "; fault=" + DiagnosticCodeText.Of(SourceFaultCode)
            + "(" + SourceFaultCountBefore.ToString(CultureInfo.InvariantCulture)
            + "->" + SourceFaultCountAfter.ToString(CultureInfo.InvariantCulture) + ")"
            + "; destination=" + (DestinationHost != null ? DestinationHost.World.Session.ToString() : "<none>")
            + "; repairs=" + RepairsAttempted.ToString(CultureInfo.InvariantCulture)
            + "; registry=" + RegistryCountBefore.ToString(CultureInfo.InvariantCulture)
            + "->" + RegistryCountAfter.ToString(CultureInfo.InvariantCulture)
            + (Detail.Length == 0 ? string.Empty : "; " + Detail)
            + "; " + Request.ToString();

        public override string ToString() => Outcome + "(" + DiagnosticCodeText.Of(Code) + "): " + Detail;
    }

    /// <summary>
    /// The V1 recovery entry point for the initial-definition source (P-049). One call is one attempt: it never
    /// retries, never resumes the source, and never exposes a destination whose repair or initial publication did
    /// not complete.
    /// </summary>
    public static class InitialDefinitionRecovery
    {
        /// <summary>
        /// Recovers one world from its initial definitions into a fresh incarnation.
        /// </summary>
        /// <param name="request">The caller-reserved destination and the source it recovers from.</param>
        /// <param name="registration">The destination's composition root; the caller owns it, this call never replaces it.</param>
        /// <param name="repair">
        /// The reference-repair step of P-049, or null when the destination has no reference tables to rebuild (which
        /// is the V1 initial-definition shape: the definition is the compiled catalog, and creation validates it).
        /// </param>
        public static RecoveryReport Recover(
            RecoveryRequest request,
            UnityWorldRegistration registration,
            IRecoveryRepair? repair = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            GameCoreThreading.RequireMainThread("InitialDefinitionRecovery.Recover");

            WorldId source = request.Source;
            WorldId destination = request.Destination;
            int registryBefore = UnityWorldRegistry.Count;

            if (!request.IsValid)
            {
                return Refuse(
                    request,
                    WorldLifecycleState.Created,
                    DiagnosticCode.None,
                    0,
                    DiagnosticCode.UnsupportedVersion,
                    "the recovery request is not well formed: the destination must be a fresh allocated session "
                    + "different from the source, and the definition must be set (P-004, P-050).",
                    registryBefore,
                    registryBefore,
                    0);
            }

            if (!UnityWorldRegistry.TryGet(source, out UnityWorldHost? sourceHost) || sourceHost == null)
            {
                // The source is gone: there is nothing to recover *from*, and the caller's handle is stale rather
                // than this call's fault (P-005).
                return Refuse(
                    request,
                    WorldLifecycleState.Created,
                    DiagnosticCode.None,
                    0,
                    DiagnosticCode.StaleHandle,
                    "no live world owns source session " + source.Session.ToString()
                    + "; recovery reads a faulted world, and an unregistered session is a stale handle (P-049).",
                    registryBefore,
                    registryBefore,
                    0);
            }

            WorldLifecycleState sourceBefore = sourceHost.Lifecycle;
            DiagnosticCode sourceFault = sourceHost.FaultCode;
            int sourceFaultCount = sourceHost.FaultCount;

            if (sourceBefore == WorldLifecycleState.Running || sourceBefore == WorldLifecycleState.Paused)
            {
                // A live world is not a recovery source. Recovering "over" it would put two writers on one state
                // domain and would claim a failover that never happened (P-002, P-049).
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    DiagnosticCode.TooLate,
                    "source world " + sourceHost.DiagnosticName + " is " + sourceBefore
                    + "; recovery is refused for a live world — stop it first, or recover from the world that actually"
                    + " faulted (P-049, P-031).",
                    registryBefore,
                    UnityWorldRegistry.Count,
                    0);
            }

            if (!sourceHost.Request.Definition.Equals(request.Definition))
            {
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    DiagnosticCode.MissingDependency,
                    "the request's definition is not the source world's definition; a recovered world is rebuilt from "
                    + "the same initial definitions, and a different definition is a different world (P-049).",
                    registryBefore,
                    UnityWorldRegistry.Count,
                    0);
            }

            if (UnityWorldRegistry.TryGet(destination, out UnityWorldHost? existing) && existing != null)
            {
                // The destination session is already owned. P-050 reserves a fresh session per attempt; reusing a
                // live one would make the recovered world a second owner of an existing incarnation.
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    DiagnosticCode.IdempotencyConflict,
                    "destination session " + destination.Session.ToString()
                    + " is already owned by a live world; a recovery attempt reserves a fresh session (P-050).",
                    registryBefore,
                    UnityWorldRegistry.Count,
                    0);
            }

            // The reference-repair boundary (TEST-016's last row, P-049). It runs before anything is created, so a
            // failure cannot leave an incomplete destination behind: there is no destination yet.
            int repairsAttempted = 0;
            if (repair != null)
            {
                repairsAttempted = 1;
                if (!repair.TryRepair(request, out DiagnosticCode repairCode, out string repairDetail))
                {
                    return Refuse(
                        request,
                        sourceBefore,
                        sourceFault,
                        sourceFaultCount,
                        repairCode == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : repairCode,
                        "reference repair refused the recovered world: " + repairDetail
                        + "; no destination was created, so no incomplete world can become the running one (P-049).",
                        registryBefore,
                        UnityWorldRegistry.Count,
                        repairsAttempted);
                }
            }

            bool created = UnityWorldRegistry.TryCreate(
                request.ToCreateRequest(),
                registration,
                out UnityWorldHost? destinationHost,
                out WorldCreateResult createResult);

            int registryAfter = UnityWorldRegistry.Count;

            if (!created || destinationHost == null)
            {
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    createResult.Code == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : createResult.Code,
                    "the recovered world was not created: " + createResult.Detail,
                    registryBefore,
                    registryAfter,
                    repairsAttempted);
            }

            if (destinationHost.Lifecycle != WorldLifecycleState.Running
                || !destinationHost.CurrentEpoch.Equals(AssemblyEpoch.First)
                || !destinationHost.CurrentStep.Equals(LogicalStepId.Zero))
            {
                // A destination that did not reach its initial validated publication is not a world anyone may
                // observe; it is torn down instead of being exposed as a partially built one (P-035, P-049).
                string lifecycle = destinationHost.Lifecycle.ToString();
                ulong epoch = destinationHost.CurrentEpoch.Value;
                destinationHost.Dispose();
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    DiagnosticCode.ApplyFault,
                    "the destination was created but never became the running world on its initial assembly "
                    + "(lifecycle=" + lifecycle + ", epoch=" + epoch.ToString(CultureInfo.InvariantCulture)
                    + "); it was disposed instead of being exposed (P-035, P-049).",
                    registryBefore,
                    UnityWorldRegistry.Count,
                    repairsAttempted);
            }

            if (destinationHost.World.Session.Equals(source.Session))
            {
                destinationHost.Dispose();
                return Refuse(
                    request,
                    sourceBefore,
                    sourceFault,
                    sourceFaultCount,
                    DiagnosticCode.StaleHandle,
                    "the created world reused the source's session; a recovered world is a new incarnation (P-004).",
                    registryBefore,
                    UnityWorldRegistry.Count,
                    repairsAttempted);
            }

            // The source is re-read, not assumed: a recovery that resumed it would be the defect this contract
            // exists to exclude.
            UnityWorldRegistry.TryGet(source, out UnityWorldHost? sourceAfter);

            return new RecoveryReport(
                request,
                sourceBefore,
                sourceFault,
                sourceFaultCount,
                sourceAfter != null ? sourceAfter.Lifecycle : sourceHost.Lifecycle,
                sourceAfter != null ? sourceAfter.FaultCount : sourceHost.FaultCount,
                Outcome.Published,
                DiagnosticCode.None,
                "recovered from initial definitions into session " + destinationHost.World.Session.ToString()
                + " at revision " + destinationHost.PublishedCompositionRevision.Value.ToString(CultureInfo.InvariantCulture)
                + "/epoch " + destinationHost.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                + "; the source stays " + sourceHost.Lifecycle + " and is never resumed (P-049).",
                destinationHost,
                repairsAttempted,
                registryBefore,
                registryAfter);
        }

        private static RecoveryReport Refuse(
            RecoveryRequest request,
            WorldLifecycleState sourceBefore,
            DiagnosticCode sourceFault,
            int sourceFaultCount,
            DiagnosticCode code,
            string detail,
            int registryBefore,
            int registryAfter,
            int repairsAttempted)
        {
            UnityWorldRegistry.TryGet(request.Source, out UnityWorldHost? sourceAfter);
            return new RecoveryReport(
                request,
                sourceBefore,
                sourceFault,
                sourceFaultCount,
                sourceAfter != null ? sourceAfter.Lifecycle : sourceBefore,
                sourceAfter != null ? sourceAfter.FaultCount : sourceFaultCount,
                Outcome.Rejected,
                code,
                detail,
                null,
                repairsAttempted,
                registryBefore,
                registryAfter);
        }
    }
}
