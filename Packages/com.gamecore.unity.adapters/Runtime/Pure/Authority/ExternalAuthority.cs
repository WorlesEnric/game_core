// GameCore.Unity.Adapters — the external physical-authority descriptor and its adapter seam (GC-019).
//
// Normative sources: 00 P-034 ("An engine-owned physical domain is declared as external authority; its ECS data is
// stamped observation, and ECS submits intent through that adapter"), P-002 ("`EngineAdapter` admits observations
// and presents committed output"), P-007 (a stale completion may release its own resources but MUST NOT
// re-acquire execution authority), P-045 (observation is an immutable image with an explicit token) and 04 s7's
// Physics row: "For those bodies, Unity Rigidbody/physics scene state is authoritative for physical pose/velocity.
// ECS owns gameplay state and records the last synchronized physical observation; gameplay systems may not
// independently integrate or overwrite the same pose. Teleports/impulses are commands to the physics adapter. The
// domain schema declares that external-authority exception and synchronization step... Authority mode is fixed per
// body recipe and changes only through a fenced migration. This prevents two writable copies disguised as
// 'synchronization.'"
//
// GC-019 owns the *descriptor and the seam*, not a physics integration (that is GC-020's optional stage). So this
// file provides three things and no simulation:
//   * a declaration per recipe saying which authority owns one domain, defaulting to ECS-owned kinematic, so a card
//     or narrative world needs no physics adapter, no rigidbody and no simulation stage at all (P-059);
//   * a stamped observation envelope, so engine-sampled values carry the sampling step and can never masquerade as an
//     independently writable authoritative copy (TEST-019);
//   * a ledger that refuses the two things 04 s7 forbids: gameplay overwriting an externally owned quantity, and a
//     second authority claiming a domain that already has one.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Authority
{
    /// <summary>Which side owns one motion/pose domain for one recipe (04 s7).</summary>
    public enum MotionAuthority
    {
        /// <summary>
        /// ECS owns the pose and the adapter uses kinematic presentation bodies. This is the default, so a world with
        /// no physics adapter is explicitly ECS-owned rather than "unknown" (04 s7, TEST-019).
        /// </summary>
        EcsOwnedKinematic = 0,

        /// <summary>
        /// The engine's physical solver owns pose/velocity; ECS records the last synchronized observation and
        /// submits intents through the adapter (P-034).
        /// </summary>
        ExternalEngineOwned = 1,
    }

    /// <summary>
    /// The external-authority declaration of one recipe's physical domain. It is versioned composition-adjacent data:
    /// it names the external owner, the synchronization stage and the observation schema, so "who owns pose" is
    /// declared rather than inferred from whichever system wrote last (P-034).
    /// </summary>
    public readonly struct ExternalAuthorityDescriptor
    {
        public readonly DefinitionRef Recipe;

        /// <summary>Stable id of the quantity domain (for example the pose/velocity domain of one recipe class).</summary>
        public readonly Id128 Domain;

        /// <summary>Registered owner of the external domain; exactly one per domain (P-034).</summary>
        public readonly OwnerId ExternalOwner;

        /// <summary>Declared synchronization stage: where observations are consumed and intents applied (P-039).</summary>
        public readonly StageId SynchronizationStage;

        /// <summary>Schema of the stamped observation the adapter publishes (P-054).</summary>
        public readonly SchemaRef ObservationSchema;

        public readonly MotionAuthority Authority;

        public ExternalAuthorityDescriptor(
            DefinitionRef recipe,
            Id128 domain,
            OwnerId externalOwner,
            StageId synchronizationStage,
            SchemaRef observationSchema,
            MotionAuthority authority)
        {
            Recipe = recipe;
            Domain = domain;
            ExternalOwner = externalOwner;
            SynchronizationStage = synchronizationStage;
            ObservationSchema = observationSchema;
            Authority = authority;
        }

        /// <summary>A descriptor with no domain declared: the recipe is ECS-owned and needs no adapter at all.</summary>
        public static ExternalAuthorityDescriptor EcsOwned(DefinitionRef recipe) =>
            new ExternalAuthorityDescriptor(
                recipe,
                Id128.Zero,
                default(OwnerId),
                default(StageId),
                default(SchemaRef),
                MotionAuthority.EcsOwnedKinematic);

        public bool IsDeclared => Authority == MotionAuthority.ExternalEngineOwned && !Domain.IsDefault;

        public override string ToString() =>
            Recipe.ToString() + ":" + Authority + "(" + (IsDeclared ? ExternalOwner.ToString() : "ecs") + ")";
    }

    /// <summary>
    /// One engine-sampled value set, stamped with the world/epoch/step it was sampled at and the authority that owns
    /// it. A downstream reader can therefore tell an observation from an authoritative state (TEST-019).
    /// </summary>
    public readonly struct EngineObservation
    {
        public readonly TargetId Target;
        public readonly Id128 Domain;
        public readonly OwnerId Authority;
        public readonly SnapshotToken Token;
        public readonly SchemaRef Schema;
        public readonly FrozenPayload Values;

        /// <summary>Ordinal of the sample within its step; a step may admit several bodies' samples.</summary>
        public readonly uint SampleOrdinal;

        public EngineObservation(
            TargetId target,
            Id128 domain,
            OwnerId authority,
            SnapshotToken token,
            SchemaRef schema,
            FrozenPayload values,
            uint sampleOrdinal)
        {
            Target = target;
            Domain = domain;
            Authority = authority;
            Token = token;
            Schema = schema;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            SampleOrdinal = sampleOrdinal;
        }

        public LogicalStepId SampledStep => Token.LogicalStepId;

        public AssemblyEpoch SampledEpoch => Token.AssemblyEpoch;

        public bool IsAllocated => !Target.IsDefault && !Domain.IsDefault;

        public override string ToString() =>
            Target.ToString() + "@" + SampledStep.Value.ToString(CultureInfo.InvariantCulture)
            + " by " + Authority.ToString();
    }

    /// <summary>What an intent asks the external authority to do; it is a command, never a write (04 s7).</summary>
    public enum AuthorityIntentKind
    {
        /// <summary>Move a body to an absolute pose, applied by the adapter before its next simulation.</summary>
        Teleport = 0,

        /// <summary>Apply an impulse/velocity change, applied by the adapter before its next simulation.</summary>
        Impulse = 1,
    }

    /// <summary>One intent submitted to the external authority (P-042: a typed command, not an ECS write).</summary>
    public sealed class AuthorityIntent
    {
        public AuthorityIntent(
            OperationId operation,
            TargetId target,
            Id128 domain,
            AuthorityIntentKind kind,
            FrozenPayload payload)
        {
            Operation = operation;
            Target = target;
            Domain = domain;
            Kind = kind;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public OperationId Operation { get; }

        public TargetId Target { get; }

        public Id128 Domain { get; }

        public AuthorityIntentKind Kind { get; }

        public FrozenPayload Payload { get; }

        public override string ToString() => Kind + "(" + Target.ToString() + ")";
    }

    /// <summary>How one intent submission resolved (P-042: admission acceptance is not gameplay success).</summary>
    public enum AuthorityIntentOutcome
    {
        /// <summary>The adapter accepted the intent for application at its declared stage.</summary>
        Accepted = 0,

        /// <summary>No external authority owns that target/domain, so the intent is refused (P-034).</summary>
        RefusedEcsOwned = 1,

        /// <summary>The adapter is installed but cannot take work (no scene, disposed, unavailable).</summary>
        RefusedUnavailable = 2,

        /// <summary>The adapter rejected the intent itself, with its own reason.</summary>
        RefusedByAdapter = 3,
    }

    /// <summary>
    /// The engine seam of an external authority. GC-019 defines it and proves the ownership rules with fixtures;
    /// GC-020 supplies the real physics implementation. An implementation must not be installed for a world that has
    /// no such domain: an absent adapter is the ordinary card/narrative case (P-059).
    /// </summary>
    public interface IExternalAuthorityAdapter
    {
        /// <summary>Stable id of the domain this adapter owns; one adapter per declared domain (P-034).</summary>
        Id128 Domain { get; }

        /// <summary>The external owner identity this adapter reports on every observation it publishes.</summary>
        OwnerId ExternalOwner { get; }

        /// <summary>False when the backend cannot sample or accept intents; sampling then reports, never throws.</summary>
        bool IsAvailable { get; }

        /// <summary>Samples the engine-owned values of one target at one committed step, in the adapter's order.</summary>
        bool TrySample(TargetId target, SnapshotToken token, out EngineObservation observation, out DiagnosticCode code);

        /// <summary>Submits one intent; it is applied at the declared stage, never written by gameplay (04 s7).</summary>
        AuthorityIntentOutcome SubmitIntent(AuthorityIntent intent);
    }

    /// <summary>Outcome of recording or refusing one observation (P-034, P-045).</summary>
    public enum ObservationOutcome
    {
        /// <summary>Recorded as the last synchronized observation of that target and domain.</summary>
        Recorded = 0,

        /// <summary>The sample did not match the declared authority for that domain (P-034).</summary>
        RefusedForeignAuthority = 1,

        /// <summary>The sample is not newer than the recorded one; an older image never overwrites a newer (P-045).</summary>
        RefusedStaleSample = 2,

        /// <summary>The domain is not declared external for that target, so nothing may claim it (P-034).</summary>
        RefusedUndeclaredDomain = 3,

        /// <summary>The observation envelope is degenerate (P-005).</summary>
        RefusedMalformed = 4,
    }

    /// <summary>
    /// The ownership ledger of external domains in one world. It is where "each quantity has one declared authority"
    /// (TEST-019) is enforced as a value: gameplay cannot overwrite an externally owned quantity, a sample cannot
    /// come from an authority that does not own the domain, and a stale sample cannot replace a newer one.
    /// </summary>
    public sealed class ExternalAuthorityLedger
    {
        private readonly Dictionary<Id128, ExternalAuthorityDescriptor> declarations =
            new Dictionary<Id128, ExternalAuthorityDescriptor>();

        private readonly Dictionary<Id128, OwnerId> domainOwnerByDomain = new Dictionary<Id128, OwnerId>();
        private readonly Dictionary<Id128, EngineObservation> recorded = new Dictionary<Id128, EngineObservation>();

        public ExternalAuthorityLedger(WorldId world)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("An authority ledger must name a live world session (P-004).", nameof(world));
            }

            World = world;
        }

        public WorldId World { get; }

        public int DeclarationCount => declarations.Count;

        /// <summary>Declared external domains; empty is the ordinary card/narrative answer (P-059).</summary>
        public int ExternalDomainCount => domainOwnerByDomain.Count;

        public int RecordedObservationCount => recorded.Count;

        public int RecordCount { get; private set; }

        public int RefusedGameplayWriteCount { get; private set; }

        public int RefusedForeignAuthorityCount { get; private set; }

        public int RefusedStaleSampleCount { get; private set; }

        public int RefusedUndeclaredDomainCount { get; private set; }

        public int RefusedMalformedCount { get; private set; }

        public int IntentCount { get; private set; }

        /// <summary>Expensive-looking declarations refused because a domain already has an owner (P-034, P-016).</summary>
        public int DeclarationConflictCount { get; private set; }

        /// <summary>
        /// Declares the authority of one recipe's domain. A second *external* owner for a domain that already has one
        /// is an `OwnershipConflict`, so two writable copies can never be declared as "synchronization" (04 s7).
        /// </summary>
        public bool TryDeclare(ExternalAuthorityDescriptor descriptor, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (descriptor.Recipe.Id.IsDefault)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "an authority declaration must name a recipe (P-004)";
                return false;
            }

            if (!descriptor.IsDeclared)
            {
                // An ECS-owned declaration is a real answer: it records that this recipe needs no external adapter.
                declarations[descriptor.Recipe.Id.Value] = descriptor;
                return true;
            }

            if (domainOwnerByDomain.TryGetValue(descriptor.Domain, out OwnerId existing)
                && !existing.Equals(descriptor.ExternalOwner))
            {
                DeclarationConflictCount++;
                code = DiagnosticCode.OwnershipConflict;
                detail = "domain " + descriptor.Domain.ToString() + " is already owned by "
                    + existing.ToString() + "; two external authorities for one quantity are forbidden (P-034)";
                return false;
            }

            domainOwnerByDomain[descriptor.Domain] = descriptor.ExternalOwner;
            declarations[descriptor.Recipe.Id.Value] = descriptor;
            return true;
        }

        public bool TryDescribe(DefinitionRef recipe, out ExternalAuthorityDescriptor descriptor)
        {
            if (declarations.TryGetValue(recipe.Id.Value, out ExternalAuthorityDescriptor found))
            {
                descriptor = found;
                return true;
            }

            descriptor = default(ExternalAuthorityDescriptor);
            return false;
        }

        /// <summary>
        /// The authority of one recipe, defaulting to ECS-owned. A recipe nobody declared needs no adapter, which is
        /// exactly why card and narrative worlds require no physics (P-059, TEST-019).
        /// </summary>
        public MotionAuthority AuthorityOf(DefinitionRef recipe) =>
            declarations.TryGetValue(recipe.Id.Value, out ExternalAuthorityDescriptor found)
                ? found.Authority
                : MotionAuthority.EcsOwnedKinematic;

        /// <summary>Records one engine-sampled observation after validating authority, monotonicity and shape.</summary>
        public ObservationOutcome Record(EngineObservation observation, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!observation.IsAllocated)
            {
                RefusedMalformedCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "an observation must name a target and a domain (P-005)";
                return ObservationOutcome.RefusedMalformed;
            }

            if (!domainOwnerByDomain.TryGetValue(observation.Domain, out OwnerId owner))
            {
                RefusedUndeclaredDomainCount++;
                code = DiagnosticCode.MissingDependency;
                detail = "domain " + observation.Domain.ToString()
                    + " is not declared external, so no sample may claim it (P-034)";
                return ObservationOutcome.RefusedUndeclaredDomain;
            }

            if (!owner.Equals(observation.Authority))
            {
                RefusedForeignAuthorityCount++;
                code = DiagnosticCode.OwnershipConflict;
                detail = "the sample claims authority " + observation.Authority.ToString()
                    + " but domain " + observation.Domain.ToString() + " is owned by " + owner.ToString() + " (P-034)";
                return ObservationOutcome.RefusedForeignAuthority;
            }

            Id128 key = KeyOf(observation.Target, observation.Domain);
            if (recorded.TryGetValue(key, out EngineObservation previous)
                && !IsNewer(observation.Token, previous.Token))
            {
                RefusedStaleSampleCount++;
                code = DiagnosticCode.StalePlan;
                detail = "the sample is not newer than the recorded observation; an older image never overwrites a"
                    + " newer one (P-045)";
                return ObservationOutcome.RefusedStaleSample;
            }

            recorded[key] = observation;
            RecordCount++;
            return ObservationOutcome.Recorded;
        }

        /// <summary>
        /// The refusal that makes "gameplay may not independently integrate or overwrite the same pose" testable:
        /// a gameplay-side write to an externally owned quantity is refused as an ownership conflict, never applied.
        /// </summary>
        public bool TryWriteFromGameplay(TargetId target, Id128 domain, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!domainOwnerByDomain.ContainsKey(domain))
            {
                // No external owner: ECS owns the quantity, so a gameplay write is the ordinary path.
                return true;
            }

            RefusedGameplayWriteCount++;
            code = DiagnosticCode.OwnershipConflict;
            detail = "domain " + domain.ToString() + " of " + target.ToString()
                + " is engine-owned; gameplay submits an intent instead of writing it (P-034, 04 s7)";
            return false;
        }

        /// <summary>Records one intent submission, so "teleports/impulses are commands" is observable (04 s7).</summary>
        public void NoteIntent(AuthorityIntentOutcome outcome, AuthorityIntent intent)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            IntentCount++;
            _ = outcome;
        }

        /// <summary>The last synchronized observation of one target and domain; its stamp is the sampling step.</summary>
        public bool TryReadObservation(TargetId target, Id128 domain, out EngineObservation observation) =>
            recorded.TryGetValue(KeyOf(target, domain), out observation);

        private static bool IsNewer(SnapshotToken candidate, SnapshotToken current)
        {
            if (!candidate.World.Session.Equals(current.World.Session))
            {
                return false;
            }

            int byStep = candidate.LogicalStepId.CompareTo(current.LogicalStepId);
            if (byStep != 0)
            {
                return byStep > 0;
            }

            return candidate.AssemblyEpoch.CompareTo(current.AssemblyEpoch) > 0;
        }

        private static Id128 KeyOf(TargetId target, Id128 domain)
        {
            unchecked
            {
                return new Id128(target.Value.High ^ domain.High, target.Value.Low ^ domain.Low);
            }
        }
    }

    /// <summary>
    /// Classify one intent against the ledger's declarations before an adapter sees it. It exists so an intent for a
    /// recipe the world declared ECS-owned is refused as a value rather than silently forwarded to an absent adapter
    /// (P-034, P-052).
    /// </summary>
    public static class ExternalAuthority
    {
        public static AuthorityIntentOutcome Classify(
            ExternalAuthorityLedger ledger,
            DefinitionRef recipe,
            IExternalAuthorityAdapter? adapter)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (ledger.AuthorityOf(recipe) != MotionAuthority.ExternalEngineOwned)
            {
                return AuthorityIntentOutcome.RefusedEcsOwned;
            }

            if (adapter == null || !adapter.IsAvailable)
            {
                return AuthorityIntentOutcome.RefusedUnavailable;
            }

            return AuthorityIntentOutcome.Accepted;
        }
    }
}
