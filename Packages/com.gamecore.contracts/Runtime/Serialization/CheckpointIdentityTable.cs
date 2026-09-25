// GameCore.Contracts - checkpoint identity table and reference repair plan (GC-018). Normative sources:
// docs/game-core/00-core-protocols.md P-004 (stable identities, and installation/spawn keys are stored in
// checkpoints), P-005 (a persisted reference is a stable identity plus required version, never a runtime handle)
// and 05 s6 ("entity references serialize as stable target IDs, fixed schema requirements and optional/null
// semantics; a two-pass restore first creates identity mappings, then patches references and validates referential
// integrity. Missing required targets reject; optional references become the declared None state with
// diagnostics.").
//
// The table is the first pass of that two-pass restore: it collects every identity a document declares, proves
// each category has no duplicate live identity (P-004) and exposes the lookup the second pass uses to check a
// reference. Nothing here allocates a world, an entity or a lease.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>How a reference in the document resolved against the identities the document declared (05 s6).</summary>
    public enum ReferenceResolution
    {
        /// <summary>The referenced identity is declared in this document.</summary>
        Resolved = 0,

        /// <summary>The reference is explicitly optional and resolved to its declared None state (05 s6).</summary>
        OptionalAbsent = 1,

        /// <summary>A required reference names an identity this document does not declare: reject (05 s6).</summary>
        Missing = 2,

        /// <summary>A required reference is the all-zero identity, which is never a valid catalog identity (P-004).</summary>
        Undeclared = 3,
    }

    /// <summary>
    /// The first-pass identity table of one checkpoint document. Every identity category the format stores is
    /// indexed in canonical <see cref="Id128"/> order, so a lookup or a duplicate report does not depend on
    /// document order (P-008).
    /// </summary>
    public sealed class CheckpointIdentityTable
    {
        private readonly Dictionary<Id128, int> scopes = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> installs = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> targets = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> owners = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> slots = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> clocks = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> buffers = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> rngStreams = new Dictionary<Id128, int>();
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();

        private readonly WorldId world;

        private CheckpointIdentityTable(WorldId world)
        {
            this.world = world;
        }

        /// <summary>World the table was built for; used to stamp the diagnostics it reports (P-052).</summary>
        public WorldId World => world;

        public int ScopeCount => scopes.Count;

        public int InstallCount => installs.Count;

        public int TargetCount => targets.Count;

        public int OwnerCount => owners.Count;

        public int SlotCount => slots.Count;

        /// <summary>Installation instances this document declares (P-004: a spawn/installation key is saved).</summary>
        public IReadOnlyList<Id128> InstallIds => Sorted(installs);

        public IReadOnlyList<Id128> TargetIds => Sorted(targets);

        public IReadOnlyList<Id128> ScopeIds => Sorted(scopes);

        /// <summary>Diagnostics collected while building the table, including duplicate-identity rejections (P-052).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

        /// <summary>
        /// Builds the table from a document's decoded values. The caller supplies the values it already read, so a
        /// restore can build the table from exactly the categories it intends to restore rather than decoding a
        /// second time.
        /// </summary>
        public static CheckpointIdentityTable Build(
            WorldId world,
            IReadOnlyList<ScopeRecordValue>? scopes,
            IReadOnlyList<InstallRecordValue>? installs,
            IReadOnlyList<TargetRecordValue>? targets,
            IReadOnlyList<SlotRecordValue>? slots,
            IReadOnlyList<ClockRecordValue>? clocks,
            IReadOnlyList<MessageRecordValue>? messages,
            IReadOnlyList<RngRecordValue>? rngStreams,
            OperationId operation)
        {
            var table = new CheckpointIdentityTable(world);
            table.AddScopes(scopes, operation);
            table.AddInstalls(installs, operation);
            table.AddTargets(targets, operation);
            table.AddSlots(slots, operation);
            table.AddClocks(clocks, operation);
            table.AddMessages(messages, operation);
            table.AddRngStreams(rngStreams, operation);
            return table;
        }

        /// <summary>True when this document declares the scope identity (P-010).</summary>
        public bool HasScope(ScopeId scope) => !scope.Value.IsDefault && scopes.ContainsKey(scope.Value);

        /// <summary>True when this document declares the installation identity (P-004, P-046).</summary>
        public bool HasInstall(PluginInstanceId instance) =>
            !instance.Value.IsDefault && installs.ContainsKey(instance.Value);

        /// <summary>True when this document declares the target identity (P-004).</summary>
        public bool HasTarget(TargetId target) => !target.Value.IsDefault && targets.ContainsKey(target.Value);

        /// <summary>True when this document declares the owner state domain of a slot (P-034).</summary>
        public bool HasOwner(OwnerId owner) => !owner.Value.IsDefault && owners.ContainsKey(owner.Value);

        /// <summary>True when this document declares the slot identity (P-032).</summary>
        public bool HasSlot(SlotId slot) => !slot.Value.IsDefault && slots.ContainsKey(slot.Value);

        /// <summary>True when this document declares the clock identity (P-038).</summary>
        public bool HasClock(Id128 clock) => !clock.IsDefault && clocks.ContainsKey(clock);

        /// <summary>True when this document declares the buffer identity a message names (P-043).</summary>
        public bool HasBuffer(BufferId buffer) => !buffer.Value.IsDefault && buffers.ContainsKey(buffer.Value);

        /// <summary>True when this document declares the random stream identity (P-008).</summary>
        public bool HasRngStream(Id128 stream) => !stream.IsDefault && rngStreams.ContainsKey(stream);

        /// <summary>
        /// Validates a required reference. A required reference must be non-default and declared, otherwise the
        /// document is refused (05 s6). Optional references resolve to their None state.
        /// </summary>
        public ReferenceResolution ResolveRequired(Id128 identity, IdentityCategory category)
        {
            if (identity.IsDefault)
            {
                return ReferenceResolution.Undeclared;
            }

            Dictionary<Id128, int> index = IndexOf(category);
            return index.ContainsKey(identity) ? ReferenceResolution.Resolved : ReferenceResolution.Missing;
        }

        /// <summary>
        /// Validates every structural reference of the document against this table and records a diagnostic for each
        /// unresolved required one (05 s6). Returns true when nothing is unresolved.
        /// </summary>
        public bool ValidateReferences(
            IReadOnlyList<ScopeRecordValue>? scopes,
            IReadOnlyList<InstallRecordValue>? installs,
            IReadOnlyList<TargetRecordValue>? targets,
            IReadOnlyList<SlotRecordValue>? slots,
            IReadOnlyList<SelectionRecordValue>? selections,
            IReadOnlyList<GrantRecordValue>? grants,
            IReadOnlyList<ClockRecordValue>? clocks,
            IReadOnlyList<CommandRecordValue>? commands,
            IReadOnlyList<MessageRecordValue>? messages)
        {
            bool valid = true;

            if (scopes != null)
            {
                for (int i = 0; i < scopes.Count; i++)
                {
                    ScopeRecordValue scope = scopes[i];
                    if (scope.Scope.Value.IsDefault)
                    {
                        valid &= Reject("scope", "a scope record carries the all-zero identity (P-004).");
                        continue;
                    }

                    // A non-root scope must name a parent this document declares: a dangling parent would rebuild a
                    // different tree from the one that was captured (P-010).
                    if (!scope.IsRoot && !HasScope(scope.Parent))
                    {
                        valid &= Reject(
                            "scope",
                            "scope " + scope.Scope.ToString() + " names parent " + scope.Parent.ToString()
                            + " which the document does not declare (P-010).");
                    }
                }
            }

            if (installs != null)
            {
                for (int i = 0; i < installs.Count; i++)
                {
                    InstallRecordValue install = installs[i];
                    if (install.Instance.Value.IsDefault || install.PluginType.Value.IsDefault)
                    {
                        valid &= Reject("install", "an installation record carries an all-zero identity (P-004).");
                        continue;
                    }

                    if (!HasScope(install.Scope))
                    {
                        valid &= Reject(
                            "install",
                            "installation " + install.Instance.ToString() + " names scope "
                            + install.Scope.ToString() + " which the document does not declare (P-010).");
                    }
                }
            }

            if (selections != null)
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    SelectionRecordValue selection = selections[i];
                    if (!HasInstall(selection.Instance) || !HasInstall(selection.Provider))
                    {
                        valid &= Reject(
                            "selection",
                            "a selection of " + selection.Instance.ToString() + " names provider "
                            + selection.Provider.ToString()
                            + " which the document does not declare as an installation (P-011).");
                    }
                }
            }

            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    TargetRecordValue target = targets[i];
                    if (target.Target.Value.IsDefault)
                    {
                        valid &= Reject("target", "a target record carries the all-zero identity (P-004).");
                        continue;
                    }

                    if (target.Recipe.Id.Value.IsDefault || target.Recipe.Schema.Id.Value.IsDefault)
                    {
                        valid &= Reject(
                            "target",
                            "target " + target.Target.ToString()
                            + " carries an all-zero recipe or schema identity (P-015).");
                        continue;
                    }

                    if (!HasScope(target.Scope))
                    {
                        valid &= Reject(
                            "target",
                            "target " + target.Target.ToString() + " names owner scope " + target.Scope.ToString()
                            + " which the document does not declare (P-010).");
                    }
                }
            }

            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    SlotRecordValue slot = slots[i];
                    if (slot.Key.Target.Value.IsDefault || slot.Key.Owner.Value.IsDefault
                        || slot.Key.Slot.Value.IsDefault)
                    {
                        valid &= Reject("slot", "a state slot record carries an all-zero identity part (P-032).");
                        continue;
                    }

                    // A slot whose target is absent would be orphaned state: the protocol addresses state by
                    // (TargetId, OwnerId, SlotId), so an unresolvable target is a corrupt reference (P-032).
                    if (!HasTarget(slot.Key.Target))
                    {
                        valid &= Reject(
                            "slot",
                            "slot " + slot.Key.ToString() + " names target " + slot.Key.Target.ToString()
                            + " which the document does not declare (P-032).");
                    }
                }
            }

            if (grants != null)
            {
                for (int i = 0; i < grants.Count; i++)
                {
                    GrantRecordValue grant = grants[i];
                    if (grant.Scope.Value.IsDefault)
                    {
                        valid &= Reject("grant", "a grant record carries the all-zero scope identity (P-016).");
                        continue;
                    }

                    if (!HasScope(grant.Scope))
                    {
                        valid &= Reject(
                            "grant",
                            "a grant names scope " + grant.Scope.ToString()
                            + " which the document does not declare (P-016).");
                    }

                    if (grant.Grant == GrantKind.TargetOptIn && !HasTarget(grant.Target))
                    {
                        valid &= Reject(
                            "grant",
                            "an opt-in names target " + grant.Target.ToString()
                            + " which the document does not declare (P-013).");
                    }
                }
            }

            if (clocks != null)
            {
                for (int i = 0; i < clocks.Count; i++)
                {
                    ClockRecordValue clock = clocks[i];
                    if (clock.ClockId.IsDefault)
                    {
                        valid &= Reject("clock", "a clock record carries the all-zero clock identity (P-038).");
                        continue;
                    }

                    if (clock.Row == ClockRowKind.Wake && clock.WakeId.IsDefault)
                    {
                        valid &= Reject("clock", "a wake record carries the all-zero wake identity (P-038).");
                    }
                }
            }

            if (commands != null)
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    CommandRecordValue command = commands[i];
                    if (command.IssuerId.IsDefault || command.Route.Value.IsDefault)
                    {
                        valid &= Reject(
                            "command",
                            "a queued command carries an all-zero issuer or route identity (P-042, P-050).");
                        continue;
                    }

                    if (!HasTarget(command.Target))
                    {
                        valid &= Reject(
                            "command",
                            "a queued command names target " + command.Target.ToString()
                            + " which the document does not declare; the domain version guard cannot be validated"
                            + " against an unknown target (P-042).");
                    }
                }
            }

            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    MessageRecordValue message = messages[i];
                    if (message.Buffer.Value.IsDefault || message.Producer.RegistrationKey.IsDefault)
                    {
                        valid &= Reject(
                            "message",
                            "a next-step message carries an all-zero buffer or producer identity (P-043).");
                        continue;
                    }

                    if (!HasBuffer(message.Buffer))
                    {
                        valid &= Reject(
                            "message",
                            "a next-step message names buffer " + message.Buffer.ToString()
                            + " which the document does not declare (P-043).");
                    }

                    if (!message.HasRequest)
                    {
                        continue;
                    }

                    // A message that carries a request names a target that must still exist, or the restored world
                    // would re-admit work for a target it never rebuilt (P-037).
                    if (!HasTarget(message.Target))
                    {
                        valid &= Reject(
                            "message",
                            "a next-step message carries a request for target " + message.Target.ToString()
                            + " which the document does not declare (P-037).");
                    }
                }
            }

            return valid;
        }

        /// <summary>
        /// Records one corrupt-reference diagnostic and returns false, so a caller can accumulate every finding
        /// instead of stopping at the first (P-052).
        /// </summary>
        private bool Reject(string summary, string detail)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticCode.MissingDependency,
                OperationPhase.Validation,
                default(OperationId),
                summary + ": " + detail));
            return false;
        }

        private void AddScopes(IReadOnlyList<ScopeRecordValue>? values, OperationId operation)
            => AddUnique(scopes, CheckpointRecordKind.Scope, values == null ? 0 : values.Count, operation,
                (int i) => values![i].Scope.Value);

        /// <summary>
        /// Indexes every installation identity the document declares. Owners are indexed from the slot records
        /// instead: a state domain is declared by the slots that own it, and collecting owners here would invent an
        /// owner the document never wrote (P-032, P-034).
        /// </summary>
        private void AddInstalls(IReadOnlyList<InstallRecordValue>? values, OperationId operation)
            => AddUnique(installs, CheckpointRecordKind.Install, values == null ? 0 : values.Count, operation,
                (int i) => values![i].Instance.Value);

        private void AddTargets(IReadOnlyList<TargetRecordValue>? values, OperationId operation)
            => AddUnique(targets, CheckpointRecordKind.Target, values == null ? 0 : values.Count, operation,
                (int i) => values![i].Target.Value);

        private void AddSlots(IReadOnlyList<SlotRecordValue>? values, OperationId operation)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                StateSlotKey key = values[i].Key;
                CountUnique(slots, CheckpointRecordKind.Slot, key.Slot.Value, operation, i);
                CountUnique(owners, CheckpointRecordKind.Slot, key.Owner.Value, operation, i);
            }
        }

        private void AddClocks(IReadOnlyList<ClockRecordValue>? values, OperationId operation)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                ClockRecordValue clock = values[i];
                if (clock.IsDeclaration)
                {
                    CountUnique(clocks, CheckpointRecordKind.Clock, clock.ClockId, operation, i);
                }
            }
        }

        private void AddMessages(IReadOnlyList<MessageRecordValue>? values, OperationId operation)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                CountUnique(
                    buffers,
                    CheckpointRecordKind.Message,
                    values[i].Buffer.Value,
                    operation,
                    i);
            }
        }

        private void AddRngStreams(IReadOnlyList<RngRecordValue>? values, OperationId operation)
            => AddUnique(rngStreams, CheckpointRecordKind.Rng, values == null ? 0 : values.Count, operation,
                (int i) => values![i].StreamId);

        private void AddUnique(
            Dictionary<Id128, int> index,
            CheckpointRecordKind kind,
            int count,
            OperationId operation,
            Func<int, Id128> identityOf)
        {
            for (int i = 0; i < count; i++)
            {
                CountUnique(index, kind, identityOf(i), operation, i);
            }
        }

        /// <summary>
        /// Records one identity, rejecting a duplicate live identity in the category (P-004). A duplicate is
        /// recorded as a diagnostic and the first occurrence stays authoritative, so a later duplicate cannot
        /// silently replace it.
        /// </summary>
        private void CountUnique(
            Dictionary<Id128, int> index,
            CheckpointRecordKind kind,
            Id128 identity,
            OperationId operation,
            int ordinal)
        {
            if (identity.IsDefault)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCode.MissingDependency,
                    OperationPhase.Validation,
                    operation,
                    "a " + kind + " record at index " + ordinal.ToString(CultureInfo.InvariantCulture)
                    + " carries the all-zero identity, which is never a catalog identity (P-004)."));
                return;
            }

            if (index.ContainsKey(identity))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticCode.OwnershipConflict,
                    OperationPhase.Validation,
                    operation,
                    "the document declares identity " + identity.ToString() + " in category " + kind
                    + " more than once; one world rejects duplicate live stable identities (P-004)."));
                return;
            }

            index.Add(identity, ordinal);
        }

        private Dictionary<Id128, int> IndexOf(IdentityCategory category)
        {
            switch (category)
            {
                case IdentityCategory.Scope: return scopes;
                case IdentityCategory.Install: return installs;
                case IdentityCategory.Target: return targets;
                case IdentityCategory.Owner: return owners;
                case IdentityCategory.Slot: return slots;
                case IdentityCategory.Clock: return clocks;
                case IdentityCategory.Buffer: return buffers;
                case IdentityCategory.RngStream: return rngStreams;
                default: return targets;
            }
        }

        private static IReadOnlyList<Id128> Sorted(Dictionary<Id128, int> index)
        {
            var keys = new List<Id128>(index.Keys);
            keys.Sort();
            return keys;
        }
    }

    /// <summary>One identity category a checkpoint reference can name (P-004).</summary>
    public enum IdentityCategory
    {
        Scope = 0,
        Install = 1,
        Target = 2,
        Owner = 3,
        Slot = 4,
        Clock = 5,
        Buffer = 6,
        RngStream = 7,
    }
}
