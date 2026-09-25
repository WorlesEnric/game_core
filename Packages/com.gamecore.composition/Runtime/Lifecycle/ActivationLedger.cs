// GameCore.Composition — the P-046 activation ledger: one live activation plus one staged candidate per
// installation.
//
// P-046 makes the staging of a replacement explicit: "Replacement/reconfiguration stages a candidate activation
// while the old Active activation continues; success transitions old activation -> Quiescing -> Retiring and
// candidate -> Active at one publication, with the installation generation unchanged for an in-place update."
// This file is where that sentence becomes data. Two facts are stored per installation, never one:
//
//   * the **current** activation — the one that holds execution authority right now, which keeps running while a
//     candidate is prepared;
//   * the **candidate** activation — staged, inert, and visible as `Preparing` so an operator can see a
//     replacement in flight.
//
// Every edge is decided by `InstallationStateMachine`, so no caller can invent a shortcut into `Active`, revive a
// retired activation, or leave a candidate behind after an abort. An invalid edge is returned as a value
// (`LifecycleTransition`), never thrown, which is what P-051 asks of a control-path operation.
//
// The ledger is pure: it stores identities, states and attempt ordinals. It owns no resource, no gate and no live
// world state; the host applies the gate effects the ledger's transitions name.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// One activation attempt of one installation. An attempt is identified by the operation that produced it, so
    /// a retried replacement is a different attempt and an old operation's attempt can never be mistaken for the
    /// current one.
    /// </summary>
    public sealed class ActivationAttempt
    {
        public ActivationAttempt(
            Id128 attemptId,
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation,
            InstallationState state,
            bool isCandidate,
            uint ordinal)
        {
            AttemptId = attemptId;
            Instance = instance;
            Generation = generation;
            ActivationEpoch = activationEpoch;
            Operation = operation;
            State = state;
            IsCandidate = isCandidate;
            Ordinal = ordinal;
        }

        /// <summary>Process-local attempt identity; never a persisted identity (05 s4).</summary>
        public Id128 AttemptId { get; }

        public PluginInstanceId Instance { get; }

        /// <summary>Unchanged by an in-place replacement; changes only on unmount/remount (P-005).</summary>
        public InstallationGeneration Generation { get; }

        /// <summary>Changes whenever the installation loses or gains execution authority (P-006).</summary>
        public ActivationEpoch ActivationEpoch { get; }

        /// <summary>The operation that staged or activated this attempt (P-050).</summary>
        public OperationId Operation { get; }

        public InstallationState State { get; }

        /// <summary>True for a staged replacement, i.e. an attempt that is not yet the authority (P-046).</summary>
        public bool IsCandidate { get; }

        /// <summary>Monotone ordinal within this ledger, so attempt ordering never depends on a dictionary (P-008).</summary>
        public uint Ordinal { get; }

        public bool HoldsAuthority => !IsCandidate && (State == InstallationState.Active || State == InstallationState.Quiescing);

        public ActivationAttempt With(InstallationState state) =>
            new ActivationAttempt(AttemptId, Instance, Generation, ActivationEpoch, Operation, state, IsCandidate, Ordinal);

        /// <summary>The activation stamp a completion must still match to be dispatched (P-007, P-047).</summary>
        public ActivationStamp Stamp() => new ActivationStamp(Generation, ActivationEpoch);

        public override string ToString() =>
            "attempt(" + Instance.ToString() + ", gen=" + Generation.Value.ToString(CultureInfo.InvariantCulture)
            + ", epoch=" + ActivationEpoch.Value.ToString(CultureInfo.InvariantCulture)
            + ", " + State + (IsCandidate ? ", candidate" : string.Empty) + ")";
    }

    /// <summary>
    /// Per-installation activation state of one composition host. All P-046 installation transitions are requested
    /// here; the ledger decides the edge, records the attempt and reports refusals as values.
    /// </summary>
    public sealed class ActivationLedger
    {
        private readonly Dictionary<Id128, Entry> entries = new Dictionary<Id128, Entry>();
        private readonly List<Id128> canonicalOrder = new List<Id128>();
        private ulong nextAttemptOrdinal;

        /// <summary>Installations with a live activation (current or staged candidate).</summary>
        public int InstallationCount => entries.Count;

        /// <summary>Activations that currently hold authority (Active or Quiescing behind a published removal).</summary>
        public int AuthorityCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < canonicalOrder.Count; i++)
                {
                    Entry entry = entries[canonicalOrder[i]];
                    if (entry.Current != null && entry.Current.HoldsAuthority)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Installations with a staged candidate waiting for the publication boundary.</summary>
        public int StagedCandidateCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < canonicalOrder.Count; i++)
                {
                    if (entries[canonicalOrder[i]].Candidate != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Installs published as waiting at mount because a required provider was absent (P-046).</summary>
        public int WaitingRegistrationCount { get; private set; }

        public int RegisteredActivationCount { get; private set; }

        public int CommittedReplacementCount { get; private set; }

        public int AbortedCandidateCount { get; private set; }

        public int SuspendedCount { get; private set; }

        public int ResumedCount { get; private set; }

        public int RetiredCount { get; private set; }

        public int DisposedCount { get; private set; }

        public int RetractedCount { get; private set; }

        /// <summary>Lifecycle edges refused because the P-046 table does not have them.</summary>
        public int RefusedTransitionCount { get; private set; }

        public DiagnosticCode LastRefusalCode { get; private set; } = DiagnosticCode.None;

        public string LastRefusalDetail { get; private set; } = string.Empty;

        public bool TryGetCurrent(PluginInstanceId instance, out ActivationAttempt? current)
        {
            if (entries.TryGetValue(instance.Value, out Entry entry) && entry.Current != null)
            {
                current = entry.Current;
                return true;
            }

            current = null;
            return false;
        }

        public bool TryGetCandidate(PluginInstanceId instance, out ActivationAttempt? candidate)
        {
            if (entries.TryGetValue(instance.Value, out Entry entry) && entry.Candidate != null)
            {
                candidate = entry.Candidate;
                return true;
            }

            candidate = null;
            return false;
        }

        /// <summary>Every live attempt in canonical installation order: the inspectable P-046 activation table.</summary>
        public IReadOnlyList<ActivationAttempt> Attempts()
        {
            List<ActivationAttempt> all = new List<ActivationAttempt>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                Entry entry = entries[canonicalOrder[i]];
                if (entry.Current != null)
                {
                    all.Add(entry.Current);
                }

                if (entry.Candidate != null)
                {
                    all.Add(entry.Candidate);
                }
            }

            return all;
        }

        /// <summary>Installations whose current activation state is one of the given states, in canonical order.</summary>
        public IReadOnlyList<PluginInstanceId> InstallationsIn(InstallationState state)
        {
            List<PluginInstanceId> found = new List<PluginInstanceId>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                Entry entry = entries[canonicalOrder[i]];
                if (entry.Current != null && entry.Current.State == state)
                {
                    found.Add(new PluginInstanceId(canonicalOrder[i]));
                }
            }

            return found;
        }

        /// <summary>
        /// Activates one installation for the first time (a mount's publication). The installation must have no
        /// live activation: reusing an identity that is still live is a duplicate live stable id (P-004).
        /// </summary>
        public LifecycleTransition Activate(
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current != null && entry.Current.State != InstallationState.Disposed)
            {
                return Refuse(entry.Current.State, InstallationState.Preparing, "the installation already has a live activation (P-004)");
            }

            ActivationAttempt staged = new ActivationAttempt(
                NextAttemptId(),
                instance,
                generation,
                activationEpoch,
                operation,
                InstallationState.Preparing,
                false,
                NextOrdinal());

            entry.Current = staged;
            entry.Candidate = null;
            RegisteredActivationCount++;
            ActivationAttempt active = staged.With(InstallationState.Active);
            entry.Current = active;
            return LifecycleTransition.Permit(InstallationState.Preparing, InstallationState.Active);
        }

        /// <summary>
        /// A mount whose required dependency is absent: registration plus a *published waiting* instance with no
        /// active contributions, which is the diagram's `Registered -> WaitingForDependencies` edge and P-046's
        /// "missing required dependencies yields a published waiting instance, not a half-active plugin". The
        /// installation keeps its identity and configuration, so a provider that later returns resumes it (P-012).
        /// </summary>
        public LifecycleTransition WaitOnRegistration(
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current != null && entry.Current.State != InstallationState.Disposed)
            {
                return Refuse(entry.Current.State, InstallationState.WaitingForDependencies, "the installation already has a live activation (P-004)");
            }

            LifecycleTransition edge = InstallationStateMachine.Request(InstallationState.Registered, InstallationState.WaitingForDependencies);
            if (!edge.Allowed)
            {
                return Refuse(InstallationState.Registered, InstallationState.WaitingForDependencies, "the diagram has no registration-to-waiting edge");
            }

            entry.Current = new ActivationAttempt(
                NextAttemptId(),
                instance,
                generation,
                activationEpoch,
                operation,
                InstallationState.WaitingForDependencies,
                false,
                NextOrdinal());
            entry.Candidate = null;
            RegisteredActivationCount++;
            WaitingRegistrationCount++;
            return edge;
        }

        /// <summary>
        /// Stages a candidate activation while the current Active activation keeps running (P-046). The generation
        /// must match the live activation's: an in-place update never changes it, and transferring authority to a
        /// different installation identity is a replacement mapping this task does not implement (GC-015).
        /// </summary>
        public LifecycleTransition StageCandidate(
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Preparing, "no live activation exists to keep running while a candidate stages");
            }

            if (entry.Candidate != null)
            {
                return Refuse(entry.Candidate.State, InstallationState.Preparing, "a candidate activation is already staged for this installation");
            }

            if (entry.Current.State != InstallationState.Active)
            {
                return Refuse(entry.Current.State, InstallationState.Preparing, "only an Active activation can be replaced while it continues to run (P-046)");
            }

            if (!entry.Current.Generation.Equals(generation))
            {
                return Refuse(entry.Current.State, InstallationState.Preparing, "an in-place replacement keeps the installation generation (P-005)");
            }

            LifecycleTransition edge = InstallationStateMachine.Request(InstallationState.Registered, InstallationState.Preparing);
            if (!edge.Allowed)
            {
                return Refuse(InstallationState.Registered, InstallationState.Preparing, "the diagram has no staging edge");
            }

            entry.Candidate = new ActivationAttempt(
                NextAttemptId(),
                instance,
                generation,
                activationEpoch,
                operation,
                InstallationState.Preparing,
                true,
                NextOrdinal());
            return LifecycleTransition.Permit(InstallationState.Registered, InstallationState.Preparing);
        }

        /// <summary>
        /// Commits a staged candidate: the candidate becomes Active and the displaced activation walks
        /// `Active -> Quiescing -> Retiring` at the same publication (P-046). The two edges are checked against the
        /// diagram, so a ledger that was not in the documented state refuses instead of publishing a guess.
        /// </summary>
        public LifecycleTransition CommitCandidate(PluginInstanceId instance, out ActivationAttempt? displaced)
        {
            displaced = null;
            Entry entry = EntryOf(instance);
            if (entry.Candidate == null)
            {
                return Refuse(entry.Current == null ? InstallationState.Registered : entry.Current.State,
                    InstallationState.Active, "no candidate activation is staged for this installation");
            }

            LifecycleTransition activate = InstallationStateMachine.Request(entry.Candidate.State, InstallationState.Active);
            if (!activate.Allowed)
            {
                return Refuse(entry.Candidate.State, InstallationState.Active, "the candidate cannot become Active");
            }

            if (entry.Current != null)
            {
                LifecycleTransition quiesce = InstallationStateMachine.Request(entry.Current.State, InstallationState.Quiescing);
                if (!quiesce.Allowed)
                {
                    return Refuse(entry.Current.State, InstallationState.Quiescing, "the displaced activation cannot quiesce");
                }

                LifecycleTransition retire = InstallationStateMachine.Request(InstallationState.Quiescing, InstallationState.Retiring);
                if (!retire.Allowed)
                {
                    return Refuse(InstallationState.Quiescing, InstallationState.Retiring, "a quiescing activation must be able to retire");
                }

                displaced = entry.Current.With(InstallationState.Retiring);
                RetiredCount++;
            }

            entry.Current = entry.Candidate.With(InstallationState.Active);
            entry.Candidate = null;
            CommittedReplacementCount++;
            return LifecycleTransition.Permit(InstallationState.Preparing, InstallationState.Active);
        }

        /// <summary>
        /// Aborts a staged candidate before any live write: the candidate becomes `Failed` and the running
        /// activation is untouched, so a failed candidate never costs the old usable activation (P-046).
        /// </summary>
        public LifecycleTransition AbortCandidate(PluginInstanceId instance, out ActivationAttempt? aborted)
        {
            aborted = null;
            Entry entry = EntryOf(instance);
            if (entry.Candidate == null)
            {
                return Refuse(entry.Current == null ? InstallationState.Registered : entry.Current.State,
                    InstallationState.Failed, "no candidate activation is staged for this installation");
            }

            LifecycleTransition edge = InstallationStateMachine.Request(entry.Candidate.State, InstallationState.Failed);
            if (!edge.Allowed)
            {
                return Refuse(entry.Candidate.State, InstallationState.Failed, "a candidate can only fail from Preparing");
            }

            aborted = entry.Candidate.With(InstallationState.Failed);
            entry.Candidate = null;
            AbortedCandidateCount++;
            return LifecycleTransition.Permit(InstallationState.Preparing, InstallationState.Failed);
        }

        /// <summary>Walks `Active -> Quiescing`; the old committed assembly is still visible in this state (06 s1).</summary>
        public LifecycleTransition Quiesce(PluginInstanceId instance)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Quiescing, "no live activation exists");
            }

            LifecycleTransition edge = InstallationStateMachine.Request(entry.Current.State, InstallationState.Quiescing);
            if (!edge.Allowed)
            {
                return Refuse(entry.Current.State, InstallationState.Quiescing, "the installation cannot quiesce from its current state");
            }

            entry.Current = entry.Current.With(InstallationState.Quiescing);
            return edge;
        }

        /// <summary>
        /// `Quiescing -> Active`: the prewrite abort reopens the old gates, which is the one path back to Active
        /// (06 s1). Used when a voluntary teardown fails before any live write.
        /// </summary>
        public LifecycleTransition ReopenFromQuiescing(PluginInstanceId instance)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Active, "no live activation exists");
            }

            LifecycleTransition edge = InstallationStateMachine.Request(entry.Current.State, InstallationState.Active);
            if (!edge.Allowed || entry.Current.State != InstallationState.Quiescing)
            {
                return Refuse(entry.Current.State, InstallationState.Active, "only a quiescing activation can reopen");
            }

            entry.Current = entry.Current.With(InstallationState.Active);
            return edge;
        }

        /// <summary>
        /// `Active -> Quiescing -> Suspended`. Suspension retracts the active contribution but keeps the
        /// installation and its configuration (P-046).
        /// </summary>
        public LifecycleTransition Suspend(PluginInstanceId instance)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Suspended, "no live activation exists");
            }

            if (entry.Candidate != null)
            {
                return Refuse(entry.Candidate.State, InstallationState.Suspended, "a staged candidate must be aborted or committed before a suspend (P-046)");
            }

            LifecycleTransition first = InstallationStateMachine.Request(entry.Current.State, InstallationState.Quiescing);
            if (!first.Allowed)
            {
                return Refuse(entry.Current.State, InstallationState.Quiescing, "the installation cannot quiesce before suspending");
            }

            LifecycleTransition second = InstallationStateMachine.Request(InstallationState.Quiescing, InstallationState.Suspended);
            if (!second.Allowed)
            {
                return Refuse(InstallationState.Quiescing, InstallationState.Suspended, "a quiescing activation must be able to suspend");
            }

            entry.Current = entry.Current.With(InstallationState.Suspended);
            SuspendedCount++;
            return second;
        }

        /// <summary>
        /// `Suspended -> Preparing -> Active`. Resume rederives against the current ancestry, so the caller decides
        /// `Active` from the resolution result; this method records the resumed activation.
        /// </summary>
        public LifecycleTransition Resume(
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Preparing, "no live activation exists");
            }

            LifecycleTransition first = InstallationStateMachine.Request(entry.Current.State, InstallationState.Preparing);
            if (!first.Allowed)
            {
                return Refuse(entry.Current.State, InstallationState.Preparing, "only a suspended or failed activation resumes");
            }

            entry.Current = new ActivationAttempt(
                NextAttemptId(),
                instance,
                generation,
                activationEpoch,
                operation,
                InstallationState.Active,
                false,
                NextOrdinal());
            ResumedCount++;
            return LifecycleTransition.Permit(InstallationState.Preparing, InstallationState.Active);
        }

        /// <summary>
        /// Marks a live activation as waiting for dependencies. This is a *publication-driven* transition, not a
        /// lifecycle operation: P-012 resolves it while the plan is built ("Loss of a required provider
        /// automatically makes affected consumers WaitingForDependencies and retracts their active contributions in
        /// the same plan"). The diagram's only path from Active to WaitingForDependencies runs through Quiescing, so
        /// the ingress of the losing activation closes as the plan publishes and the state lands on Waiting.
        /// </summary>
        public LifecycleTransition WaitForDependencies(PluginInstanceId instance)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.WaitingForDependencies, "no live activation exists");
            }

            if (entry.Current.State == InstallationState.WaitingForDependencies)
            {
                // Already waiting: a repeated provider-loss publication is not a second transition (P-012).
                return LifecycleTransition.Permit(InstallationState.WaitingForDependencies, InstallationState.WaitingForDependencies);
            }

            InstallationState cursor = entry.Current.State;
            if (cursor == InstallationState.Active)
            {
                LifecycleTransition quiesce = InstallationStateMachine.Request(cursor, InstallationState.Quiescing);
                if (!quiesce.Allowed)
                {
                    return Refuse(cursor, InstallationState.Quiescing, "an active activation must quiesce before it waits");
                }

                cursor = InstallationState.Quiescing;
            }

            LifecycleTransition edge = InstallationStateMachine.Request(cursor, InstallationState.WaitingForDependencies);
            if (!edge.Allowed)
            {
                return Refuse(cursor, InstallationState.WaitingForDependencies, "the installation cannot enter WaitingForDependencies from its current state");
            }

            entry.Current = entry.Current.With(InstallationState.WaitingForDependencies);
            RetractedCount++;
            return edge;
        }

        /// <summary>
        /// Marks a waiting activation as resumed automatically by dependency availability (P-012: "Consumers
        /// resume automatically when valid dependencies return"). Explicit suspension never takes this path: a
        /// Suspended activation is not waiting and stays suspended until a resume operation.
        /// </summary>
        public LifecycleTransition ResumeFromWaiting(
            PluginInstanceId instance,
            InstallationGeneration generation,
            ActivationEpoch activationEpoch,
            OperationId operation)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Active, "no live activation exists");
            }

            if (entry.Current.State != InstallationState.WaitingForDependencies)
            {
                return Refuse(entry.Current.State, InstallationState.Active, "only a waiting activation resumes automatically");
            }

            entry.Current = new ActivationAttempt(
                NextAttemptId(),
                instance,
                generation,
                activationEpoch,
                operation,
                InstallationState.Active,
                false,
                NextOrdinal());
            ResumedCount++;
            return LifecycleTransition.Permit(InstallationState.Preparing, InstallationState.Active);
        }

        /// <summary>Retires a live activation whose removal has already published (P-046, P-048).</summary>
        public LifecycleTransition Retire(PluginInstanceId instance)
        {
            Entry entry = EntryOf(instance);
            if (entry.Candidate != null)
            {
                entry.Candidate = null;
                AbortedCandidateCount++;
            }

            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Retiring, "no live activation exists");
            }

            InstallationState origin = entry.Current.State;
            IReadOnlyList<InstallationState>? path;
            if (!InstallationStateMachine.TryTeardownPath(origin, out path) || path == null)
            {
                return Refuse(origin, InstallationState.Retiring, "the installation has no lawful teardown path from " + origin);
            }

            InstallationState cursor = origin;
            for (int i = 0; i < path.Count; i++)
            {
                LifecycleTransition step = InstallationStateMachine.Request(cursor, path[i]);
                if (!step.Allowed)
                {
                    return Refuse(cursor, path[i], "the teardown step is not a legal lifecycle edge (P-046)");
                }

                cursor = path[i];
            }

            entry.Current = entry.Current.With(InstallationState.Retiring);
            RetiredCount++;
            return LifecycleTransition.Permit(origin, InstallationState.Retiring);
        }

        /// <summary>
        /// Settles a retiring installation once every required resource is released (P-048: "An instance becomes
        /// Disposed only when all its required resources are settled"). A still-retained resource refuses the edge.
        /// </summary>
        public LifecycleTransition Settle(PluginInstanceId instance, bool allResourcesSettled)
        {
            Entry entry = EntryOf(instance);
            if (entry.Current == null)
            {
                return Refuse(InstallationState.Registered, InstallationState.Disposed, "no live activation exists");
            }

            if (entry.Current.State == InstallationState.Disposed)
            {
                return LifecycleTransition.Permit(InstallationState.Disposed, InstallationState.Disposed);
            }

            if (entry.Current.State != InstallationState.Retiring)
            {
                return Refuse(entry.Current.State, InstallationState.Disposed, "only a retiring installation becomes Disposed");
            }

            if (!allResourcesSettled)
            {
                return Refuse(InstallationState.Retiring, InstallationState.Disposed, "a retained resource keeps the installation in Retiring (P-048)");
            }

            entry.Current = entry.Current.With(InstallationState.Disposed);
            DisposedCount++;
            return LifecycleTransition.Permit(InstallationState.Retiring, InstallationState.Disposed);
        }

        /// <summary>
        /// Forgets one installation's activation record. Used for a remount, where the stable id is reused with a
        /// new generation and every handle and token of the previous incarnation must be invalid (P-005).
        /// </summary>
        public bool Forget(PluginInstanceId instance)
        {
            if (!entries.TryGetValue(instance.Value, out Entry entry) ||
                (entry.Current != null && entry.Current.State != InstallationState.Disposed))
            {
                return false;
            }

            entries.Remove(instance.Value);
            canonicalOrder.Remove(instance.Value);
            return true;
        }

        private Entry EntryOf(PluginInstanceId instance)
        {
            if (entries.TryGetValue(instance.Value, out Entry entry))
            {
                return entry;
            }

            Entry created = new Entry();
            entries.Add(instance.Value, created);
            canonicalOrder.Add(instance.Value);
            canonicalOrder.Sort(CompareIds);
            return created;
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);

        private Id128 NextAttemptId()
        {
            ulong ordinal = nextAttemptOrdinal;
            nextAttemptOrdinal++;
            return new Id128(ActivationAttemptIdSalt, ordinal + 1UL);
        }

        private uint NextOrdinal()
        {
            uint ordinal = (uint)(nextAttemptOrdinal & 0xFFFFFFFFUL);
            return ordinal;
        }

        private LifecycleTransition Refuse(InstallationState from, InstallationState to, string detail)
        {
            RefusedTransitionCount++;
            LastRefusalCode = DiagnosticCode.OwnershipConflict;
            LastRefusalDetail = detail;
            return LifecycleTransition.Refuse(from, to);
        }

        /// <summary>Category salt of process-local attempt identifiers.</summary>
        public const ulong ActivationAttemptIdSalt = 0x6163746976617465UL;

        private sealed class Entry
        {
            public ActivationAttempt? Current;

            public ActivationAttempt? Candidate;
        }
    }
}
