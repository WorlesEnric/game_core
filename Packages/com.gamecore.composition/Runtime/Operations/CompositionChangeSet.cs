// GameCore.Composition — the invalidation change set of one composition proposal (GC-013, P-023).
//
// A `CompositionEditPlan` already carries the before/after definitions and a `CompositionDelta`. What a
// *derivation* caller additionally needs is the invalidation view of that proposal: which scopes changed their
// parent or their composition facts, which installations appeared/left/changed, and whether the world-level mode
// moved (which invalidates the world, P-014). This type is that view.
//
// Two things make it worth its own record rather than a re-reading of the delta:
//
//   * it is **complete** over composition facts. `CompositionDelta.Scopes` describes parent changes, but nothing
//     in the delta describes an isolation/exclusion/import edit, because those are per-scope *content* rather
//     than a tree edge. Here they are first-class (`ScopeFactChangeReason.Isolation`, `Exclusions`, `Imports`),
//     which is exactly what "exclusion/isolation edits" need in order to invalidate the derived closure (P-016).
//   * it is **stable and canonical**, so it can be hashed, compared and reported (P-008) — and a caller can
//     consume it without understanding the composition edit vocabulary.
//
// The mode-switch validator below is the same seam seen from the other side: P-014 requires a switch whose
// conflict/migration/budget consequence cannot be honoured to be *rejected as a proposal*, keeping the old mode
// and the old assembly. A pure composition state cannot answer that (eligibility, capabilities and conflicts are
// derivation facts), so the lane consults an optional validator during planning — and a null validator means
// "this lane has no derivation view", which the pure composition tests rely on.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>The stable cause keys a composition change set carries (P-052).</summary>
    public static class CompositionChangeReasons
    {
        /// <summary>Nothing changed.</summary>
        public const string None = "none";

        /// <summary>The world propagation mode changed; a caller may invalidate the world (P-013, P-014).</summary>
        public const string Mode = "mode";

        /// <summary>A scope was created (P-010).</summary>
        public const string ScopeAdded = "scope-add";

        /// <summary>A scope was removed (P-010).</summary>
        public const string ScopeRemoved = "scope-remove";

        /// <summary>A scope subtree moved (P-025).</summary>
        public const string ScopeReparented = "scope-reparent";

        /// <summary>A scope's isolation, exclusions or import grants changed (P-013, P-016).</summary>
        public const string ScopeFacts = "scope-facts";

        /// <summary>An installation was mounted (O-03).</summary>
        public const string InstallAdded = "install-add";

        /// <summary>An installation was unmounted (O-07).</summary>
        public const string InstallRemoved = "install-remove";

        /// <summary>An installation's lifecycle state changed (P-046).</summary>
        public const string InstallLifecycle = "install-lifecycle";

        /// <summary>An installation's immutable configuration changed (P-020).</summary>
        public const string InstallConfig = "install-config";

        /// <summary>An installation's record changed in another way, such as its priority (P-018).</summary>
        public const string InstallRecord = "install-record";
    }

    /// <summary>Which composition fact of one scope changed (P-016).</summary>
    public enum ScopeFactChangeReason
    {
        /// <summary>The scope's parent changed (P-025).</summary>
        Parent = 0,

        /// <summary>The named capability/service isolation sets changed (P-016).</summary>
        Isolation = 1,

        /// <summary>The exclusion set changed (P-016).</summary>
        Exclusions = 2,

        /// <summary>The Conservative-mode import grants changed (P-013).</summary>
        Imports = 3,
    }

    /// <summary>One scope fact change of a proposal (P-016, P-025).</summary>
    public readonly struct ScopeFactChange
    {
        public readonly ScopeId Scope;
        public readonly ScopeFactChangeReason Reason;

        public ScopeFactChange(ScopeId scope, ScopeFactChangeReason reason)
        {
            Scope = scope;
            Reason = reason;
        }

        public override string ToString() => Scope.ToString() + "/" + Reason.ToString();
    }

    /// <summary>The invalidation view of one composition proposal (P-023).</summary>
    public sealed class CompositionChangeSet
    {
        private CompositionChangeSet(
            PropagationMode beforeMode,
            PropagationMode afterMode,
            IReadOnlyList<ScopeEdit>? scopeEdits,
            IReadOnlyList<ScopeFactChange>? scopeFacts,
            IReadOnlyList<InstallEdit>? installEdits,
            IReadOnlyList<ConfigEdit>? configEdits)
        {
            BeforeMode = beforeMode;
            AfterMode = afterMode;
            ScopeEdits = ContractCollections.Freeze(scopeEdits);
            ScopeFacts = ContractCollections.Freeze(scopeFacts);
            InstallEdits = ContractCollections.Freeze(installEdits);
            ConfigEdits = ContractCollections.Freeze(configEdits);
        }

        public PropagationMode BeforeMode { get; }

        public PropagationMode AfterMode { get; }

        /// <summary>Scope additions, removals, moves and fact updates, canonical order (P-010, P-025).</summary>
        public IReadOnlyList<ScopeEdit> ScopeEdits { get; }

        /// <summary>Per-scope fact changes, canonical order (P-013, P-016).</summary>
        public IReadOnlyList<ScopeFactChange> ScopeFacts { get; }

        /// <summary>Installation additions, removals, moves, state changes and record changes (P-046).</summary>
        public IReadOnlyList<InstallEdit> InstallEdits { get; }

        /// <summary>Configuration changes (P-020).</summary>
        public IReadOnlyList<ConfigEdit> ConfigEdits { get; }

        /// <summary>True when the world propagation mode moved, which may invalidate the world (P-014).</summary>
        public bool ModeChanged => BeforeMode != AfterMode;

        /// <summary>
        /// True when the proposal's consequence cannot be bounded to a branch, so a caller must invalidate the
        /// world. Today that is a mode switch, which re-gates every descendant rule (P-013, P-014).
        /// </summary>
        public bool WholeWorld => ModeChanged;

        /// <summary>True when the two definitions are indistinguishable to this change set.</summary>
        public bool IsEmpty =>
            !ModeChanged
            && ScopeEdits.Count == 0
            && ScopeFacts.Count == 0
            && InstallEdits.Count == 0
            && ConfigEdits.Count == 0;

        /// <summary>Stable reason keys of this change set, sorted and duplicate-free (P-052).</summary>
        public IReadOnlyList<string> Reasons()
        {
            List<string> reasons = new List<string>();
            if (ModeChanged)
            {
                reasons.Add(CompositionChangeReasons.Mode);
            }

            for (int i = 0; i < ScopeEdits.Count; i++)
            {
                switch (ScopeEdits[i].Kind)
                {
                    case CompositionEditKind.Add:
                        Add(reasons, CompositionChangeReasons.ScopeAdded);
                        break;
                    case CompositionEditKind.Remove:
                        Add(reasons, CompositionChangeReasons.ScopeRemoved);
                        break;
                    case CompositionEditKind.Reparent:
                        Add(reasons, CompositionChangeReasons.ScopeReparented);
                        break;
                    default:
                        Add(reasons, CompositionChangeReasons.ScopeFacts);
                        break;
                }
            }

            if (ScopeFacts.Count != 0)
            {
                Add(reasons, CompositionChangeReasons.ScopeFacts);
            }

            for (int i = 0; i < InstallEdits.Count; i++)
            {
                switch (InstallEdits[i].Kind)
                {
                    case CompositionEditKind.Add:
                        Add(reasons, CompositionChangeReasons.InstallAdded);
                        break;
                    case CompositionEditKind.Remove:
                        Add(reasons, CompositionChangeReasons.InstallRemoved);
                        break;
                    case CompositionEditKind.Reparent:
                    case CompositionEditKind.Update:
                        Add(reasons, CompositionChangeReasons.InstallLifecycle);
                        break;
                    default:
                        Add(reasons, CompositionChangeReasons.InstallRecord);
                        break;
                }
            }

            if (ConfigEdits.Count != 0)
            {
                Add(reasons, CompositionChangeReasons.InstallConfig);
            }

            reasons.Sort(StringComparer.Ordinal);
            return reasons.AsReadOnly();
        }

        /// <summary>Canonical text of this change set; the audit form used in evidence and diagnostics (P-008).</summary>
        public string Describe()
        {
            StringBuilder text = new StringBuilder();
            text.Append("change{reasons=");
            IReadOnlyList<string> reasons = Reasons();
            if (reasons.Count == 0)
            {
                text.Append(CompositionChangeReasons.None);
            }
            else
            {
                for (int i = 0; i < reasons.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    text.Append(reasons[i]);
                }
            }

            text.Append(";mode=").Append(BeforeMode.ToString()).Append("->").Append(AfterMode.ToString())
                .Append(";scopeEdits=").Append(ScopeEdits.Count.ToString(CultureInfo.InvariantCulture))
                .Append(";scopeFacts=").Append(ScopeFacts.Count.ToString(CultureInfo.InvariantCulture))
                .Append(";installEdits=").Append(InstallEdits.Count.ToString(CultureInfo.InvariantCulture))
                .Append(";configEdits=").Append(ConfigEdits.Count.ToString(CultureInfo.InvariantCulture))
                .Append("}");
            return text.ToString();
        }

        public override string ToString() => Describe();

        /// <summary>
        /// The change set of one proposal: the canonical diff of the two definitions over every composition fact
        /// derivation can observe (parent, isolation, exclusions, imports, installation records, configuration and
        /// the world mode).
        /// </summary>
        public static CompositionChangeSet Of(CompositionState before, CompositionState after)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            List<ScopeEdit> scopeEdits = new List<ScopeEdit>();
            List<ScopeFactChange> scopeFacts = new List<ScopeFactChange>();
            IReadOnlyList<ScopeRecord> afterScopes = after.Scopes.Scopes;
            for (int i = 0; i < afterScopes.Count; i++)
            {
                ScopeRecord record = afterScopes[i];
                if (!before.Scopes.TryGet(record.Scope, out ScopeRecord? previous) || previous == null)
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Add, record.Scope, default(ScopeId), record.Parent));
                    continue;
                }

                if (!previous.Parent.Equals(record.Parent))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Reparent, record.Scope, previous.Parent, record.Parent));
                    scopeFacts.Add(new ScopeFactChange(record.Scope, ScopeFactChangeReason.Parent));
                }

                if (!IsolationEquals(previous.CapabilityIsolation, record.CapabilityIsolation)
                    || !IsolationEquals(previous.ServiceIsolation, record.ServiceIsolation))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Update, record.Scope, record.Parent, record.Parent));
                    scopeFacts.Add(new ScopeFactChange(record.Scope, ScopeFactChangeReason.Isolation));
                }

                if (!ExclusionsEqual(previous.Exclusions, record.Exclusions))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Update, record.Scope, record.Parent, record.Parent));
                    scopeFacts.Add(new ScopeFactChange(record.Scope, ScopeFactChangeReason.Exclusions));
                }

                if (!GrantsEqual(previous.Grants, record.Grants))
                {
                    scopeEdits.Add(new ScopeEdit(CompositionEditKind.Update, record.Scope, record.Parent, record.Parent));
                    scopeFacts.Add(new ScopeFactChange(record.Scope, ScopeFactChangeReason.Imports));
                }
            }

            IReadOnlyList<ScopeRecord> beforeScopes = before.Scopes.Scopes;
            for (int i = 0; i < beforeScopes.Count; i++)
            {
                if (!after.Scopes.TryGet(beforeScopes[i].Scope, out ScopeRecord? _))
                {
                    scopeEdits.Add(new ScopeEdit(
                        CompositionEditKind.Remove, beforeScopes[i].Scope, beforeScopes[i].Parent, default(ScopeId)));
                }
            }

            List<InstallEdit> installEdits = new List<InstallEdit>();
            List<ConfigEdit> configEdits = new List<ConfigEdit>();
            for (int i = 0; i < after.Installs.Count; i++)
            {
                InstallEntry entry = after.Installs[i];
                if (!before.TryGetInstall(entry.Instance, out InstallEntry? previous) || previous == null)
                {
                    installEdits.Add(new InstallEdit(
                        CompositionEditKind.Add, entry.Instance, default(ScopeId), entry.Scope, InstallationState.Disposed, entry.State));
                    continue;
                }

                if (!previous.Scope.Equals(entry.Scope))
                {
                    installEdits.Add(new InstallEdit(
                        CompositionEditKind.Reparent, entry.Instance, previous.Scope, entry.Scope, previous.State, entry.State));
                }
                else if (previous.State != entry.State)
                {
                    installEdits.Add(new InstallEdit(
                        CompositionEditKind.Update, entry.Instance, previous.Scope, entry.Scope, previous.State, entry.State));
                }

                if (!previous.Record.ConfigHash.Equals(entry.Record.ConfigHash)
                    || previous.Record.ConfigRevision.Value != entry.Record.ConfigRevision.Value)
                {
                    configEdits.Add(new ConfigEdit(
                        entry.Instance,
                        previous.Record.ConfigRevision,
                        entry.Record.ConfigRevision,
                        previous.Record.ConfigHash,
                        entry.Record.ConfigHash));
                }
                else if (previous.Record.Priority != entry.Record.Priority)
                {
                    installEdits.Add(new InstallEdit(
                        CompositionEditKind.Update, entry.Instance, previous.Scope, entry.Scope, previous.State, entry.State));
                }
            }

            for (int i = 0; i < before.Installs.Count; i++)
            {
                InstallEntry entry = before.Installs[i];
                if (!after.TryGetInstall(entry.Instance, out InstallEntry? _))
                {
                    installEdits.Add(new InstallEdit(
                        CompositionEditKind.Remove, entry.Instance, entry.Scope, default(ScopeId), entry.State, InstallationState.Disposed));
                }
            }

            return new CompositionChangeSet(before.Mode, after.Mode, scopeEdits, scopeFacts, installEdits, configEdits);
        }

        private static bool IsolationEquals(IsolationSet left, IsolationSet right)
        {
            if (left.AllContracts != right.AllContracts || left.Contracts.Count != right.Contracts.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Contracts.Count; i++)
            {
                if (!left.Contracts[i].Equals(right.Contracts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ExclusionsEqual(IReadOnlyList<ExclusionRule> left, IReadOnlyList<ExclusionRule> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Kind != right[i].Kind
                    || !left[i].TargetId.Equals(right[i].TargetId)
                    || !left[i].AtScope.Equals(right[i].AtScope)
                    || !left[i].AtTarget.Equals(right[i].AtTarget)
                    || left[i].AppliesToSubtree != right[i].AppliesToSubtree)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool GrantsEqual(ScopeGrants? left, ScopeGrants? right)
        {
            IReadOnlyList<CapabilityImport> a = left == null ? Array.Empty<CapabilityImport>() : left.Imports;
            IReadOnlyList<CapabilityImport> b = right == null ? Array.Empty<CapabilityImport>() : right.Imports;
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].CapabilityId.Equals(b[i].CapabilityId)
                    || !a[i].ProviderInstallationId.Equals(b[i].ProviderInstallationId))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Add(List<string> reasons, string reason)
        {
            if (!reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }
    }

    /// <summary>
    /// What one validator decided about a planned proposal (P-014, P-028).
    /// </summary>
    public readonly struct EditValidationResult
    {
        private EditValidationResult(bool accepted, DiagnosticCode code, string detail)
        {
            Accepted = accepted;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>The proposal may be published.</summary>
        public static EditValidationResult Accept { get; } =
            new EditValidationResult(true, DiagnosticCode.None, string.Empty);

        public bool Accepted { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>A refusal with the code the caller reports; the old composition stays published.</summary>
        public static EditValidationResult Refuse(DiagnosticCode code, string detail) =>
            new EditValidationResult(false, code == DiagnosticCode.None ? DiagnosticCode.CapabilityConflict : code, detail);

        public override string ToString() =>
            Accepted ? "Accept" : "Refuse(" + Code.ToString() + ": " + Detail + ")";
    }

    /// <summary>
    /// Validates a planned proposal against facts that live outside the pure composition model.
    /// </summary>
    /// <remarks>
    /// The kernel's own reason for this seam is P-014: a mode switch must be rejected as a *proposal* when it
    /// would expose a composition conflict, so the old mode and the old assembly stay published. Whether a
    /// switch conflicts depends on eligibility, capability contracts and target descriptors — derivation facts
    /// this assembly deliberately does not own — so the lane asks a validator instead of guessing. A null
    /// validator means "no derivation view on this lane": the edit is planned and published as before.
    /// </remarks>
    public interface ICompositionEditValidator
    {
        /// <summary>
        /// Validates one planned proposal. <paramref name="changeSet"/> names what the proposal changed, so a
        /// validator can answer cheaply for everything it does not care about.
        /// </summary>
        EditValidationResult Validate(CompositionState before, CompositionState after, CompositionChangeSet changeSet);
    }
}
