// GameCore.Planning — the state-authority validator (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-028 (state ownership and buffer contracts are validated
// before preparation), P-032, P-033, P-034, P-040 and docs/game-core/03-runtime-and-execution.md s4.
//
// What this validates, and what it deliberately does not:
//   * exactly one logical owner per authoritative domain: two writers with different owners on one schema reject
//     (`OwnershipConflict`) unless ownership is explicitly transferred, which is a plan disposition, not a
//     declaration;
//   * writers of one owner sharing a domain need a directed order proof — a declared required edge or the compiled
//     stage order — or provably mutually exclusive generated partitions, otherwise `AmbiguousOrder` (P-040);
//   * generated partitions are derived deterministically from stable identities, so a rerun produces the same ids
//     and two different writers can never claim one partition by accident (P-008, P-034);
//   * field-to-component ownership: one physical owner per component layout and one slot owner per field (P-033);
//   * declared slot policies are complete before any migration executor exists (a `TransferTo` without a transfer
//     policy, a `Reset` without an explicit reason, a default migration key), which is what lets an early slice
//     validate a plan it cannot yet apply (P-032, 09 GC-007).
//
// This validator is pure: it reads declarations, allocates its own result and never touches live world state.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>The single logical owner of one authoritative domain and the writers that act for it (P-034).</summary>
    public readonly struct DomainOwnerRecord
    {
        public readonly SchemaRef Domain;
        public readonly OwnerId Owner;

        /// <summary>Writers claiming this domain, in canonical stage/system-key order.</summary>
        public readonly IReadOnlyList<FactoryKey> Writers;

        public DomainOwnerRecord(SchemaRef domain, OwnerId owner, IReadOnlyList<FactoryKey>? writers)
        {
            Domain = domain;
            Owner = owner;
            Writers = ContractCollections.Freeze(writers);
        }

        public int WriterCount => Writers.Count;

        /// <summary>True when the writers disagreed and no single owner could be resolved (P-034).</summary>
        public bool IsContested => Owner.Value.IsDefault;

        public override string ToString()
            => Domain.ToString() + " -> " + Owner.ToString()
                + " (" + Writers.Count.ToString(CultureInfo.InvariantCulture) + " writer(s))";
    }

    /// <summary>
    /// Resolved authority of one declaration set: the owner of every written domain, the partition claim of every
    /// writer, and the physical component ownership. A generated apply path reads this instead of re-deriving
    /// ownership per frame (P-034).
    /// </summary>
    public sealed class OwnerAuthorityMap
    {
        public static readonly OwnerAuthorityMap Empty =
            new OwnerAuthorityMap(null, null, ComponentOwnershipMap.Empty);

        private readonly List<DomainOwnerRecord> domains;
        private readonly List<PartitionAssignment> partitions;

        internal OwnerAuthorityMap(
            List<DomainOwnerRecord>? domains,
            List<PartitionAssignment>? partitions,
            ComponentOwnershipMap? components)
        {
            this.domains = domains ?? new List<DomainOwnerRecord>();
            this.partitions = partitions ?? new List<PartitionAssignment>();
            Components = components ?? ComponentOwnershipMap.Empty;
        }

        public int DomainCount => domains.Count;

        public int PartitionCount => partitions.Count;

        /// <summary>Written domains in canonical schema order; a contested domain reports a default owner.</summary>
        public IReadOnlyList<DomainOwnerRecord> Domains => domains;

        /// <summary>Every partition claim in canonical order, explicit and generated (P-034).</summary>
        public IReadOnlyList<PartitionAssignment> Partitions => partitions;

        public ComponentOwnershipMap Components { get; }

        public bool TryGetDomainOwner(SchemaRef domain, out OwnerId owner)
        {
            for (int i = 0; i < domains.Count; i++)
            {
                if (domains[i].Domain.Id.Value.Equals(domain.Id.Value))
                {
                    owner = domains[i].Owner;
                    return !owner.Value.IsDefault;
                }
            }

            owner = default(OwnerId);
            return false;
        }

        public IReadOnlyList<FactoryKey> WritersOf(SchemaRef domain)
        {
            for (int i = 0; i < domains.Count; i++)
            {
                if (domains[i].Domain.Id.Value.Equals(domain.Id.Value))
                {
                    return domains[i].Writers;
                }
            }

            return Array.Empty<FactoryKey>();
        }

        public bool TryGetPartition(SchemaRef domain, FactoryKey writer, out PartitionAssignment assignment)
        {
            for (int i = 0; i < partitions.Count; i++)
            {
                PartitionAssignment candidate = partitions[i];
                if (candidate.Domain.Id.Value.Equals(domain.Id.Value)
                    && candidate.Writer.RegistrationKey.Equals(writer.RegistrationKey))
                {
                    assignment = candidate;
                    return true;
                }
            }

            assignment = default(PartitionAssignment);
            return false;
        }

        public override string ToString()
            => "domains=" + domains.Count.ToString(CultureInfo.InvariantCulture)
                + ", partitions=" + partitions.Count.ToString(CultureInfo.InvariantCulture)
                + ", components=" + Components.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Outcome of validating one authority declaration set. Rejections are values, never exceptions.</summary>
    public sealed class OwnershipReport
    {
        internal OwnershipReport(bool isValid, IReadOnlyList<Diagnostic> diagnostics, OwnerAuthorityMap map)
        {
            IsValid = isValid;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            Map = map ?? OwnerAuthorityMap.Empty;
        }

        /// <summary>True only when no diagnostic was produced.</summary>
        public bool IsValid { get; }

        /// <summary>Structured rejections in canonical order; empty when accepted.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>Best-effort resolved authority; complete only when <see cref="IsValid"/> is true.</summary>
        public OwnerAuthorityMap Map { get; }

        public string Describe()
        {
            if (IsValid)
            {
                return "accepted " + Map.DomainCount.ToString(CultureInfo.InvariantCulture) + " domain(s), "
                    + Map.PartitionCount.ToString(CultureInfo.InvariantCulture) + " partition(s)";
            }

            if (Diagnostics.Count == 0)
            {
                return "rejected without a diagnostic";
            }

            string[] parts = new string[Diagnostics.Count];
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                parts[i] = Diagnostics[i].CodeText + "(" + Diagnostics[i].Summary + ")";
            }

            return string.Join("; ", parts);
        }
    }

    /// <summary>Pure validator of one authority declaration set (P-028, P-034, P-040).</summary>
    public static class OwnerAuthorityValidator
    {
        /// <summary>
        /// Validates the declared writers, slots and physical layouts, generating partition ids where the declared
        /// multiplicity asks for them.
        /// </summary>
        public static OwnershipReport Validate(OwnerAuthorityDeclaration declaration)
        {
            if (declaration == null)
            {
                throw new ArgumentNullException(nameof(declaration));
            }

            var diagnostics = new List<Diagnostic>();
            WriterDeclaration[] writers = SortWriters(declaration, diagnostics);
            ValidateWriterDeclarations(writers, diagnostics);

            var bySchema = new Dictionary<Id128, DomainAccumulator>();
            var domains = new List<DomainAccumulator>();
            CollectDomains(writers, domains, bySchema, diagnostics);

            var partitions = new List<PartitionAssignment>();
            var partitionIndex = new Dictionary<DomainWriterKey, PartitionAssignment>();
            GeneratePartitions(writers, partitions, partitionIndex, diagnostics);
            OwnershipOrdering.SortAssignments(partitions);

            ValidateWriterOrderAndPartitions(declaration, writers, partitionIndex, diagnostics);
            ValidateSlots(declaration.Slots, bySchema, diagnostics);

            ComponentOwnershipMap.TryBuild(
                declaration.Components,
                declaration.Slots,
                out ComponentOwnershipMap? components,
                out IReadOnlyList<Diagnostic> componentDiagnostics);
            for (int i = 0; i < componentDiagnostics.Count; i++)
            {
                diagnostics.Add(componentDiagnostics[i]);
            }

            ValidateSlotLayoutsAgainstComponents(declaration, diagnostics);

            var records = new List<DomainOwnerRecord>(domains.Count);
            for (int i = 0; i < domains.Count; i++)
            {
                DomainAccumulator domain = domains[i];
                OwnerId owner = domain.OwnerDecided && !domain.OwnerConflict ? domain.Owner : default(OwnerId);
                records.Add(new DomainOwnerRecord(domain.Domain, owner, domain.Writers));
            }

            records.Sort(CompareDomainRecords);
            diagnostics.Sort(DiagnosticOrder.Instance);

            var map = new OwnerAuthorityMap(records, partitions, components);
            return new OwnershipReport(diagnostics.Count == 0, diagnostics, map);
        }

        private static WriterDeclaration[] SortWriters(OwnerAuthorityDeclaration declaration, List<Diagnostic> diagnostics)
        {
            var writers = new List<WriterDeclaration>(declaration.Writers.Count);
            for (int i = 0; i < declaration.Writers.Count; i++)
            {
                WriterDeclaration writer = declaration.Writers[i];
                if (!writer.IsDeclared)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        "an authority declaration names a default zero system key; only generated registrations "
                        + "declare writers (P-039)"));
                    continue;
                }

                writers.Add(writer);
            }

            writers.Sort((left, right) => CompareWriters(declaration, left, right));

            var unique = new List<WriterDeclaration>(writers.Count);
            for (int i = 0; i < writers.Count; i++)
            {
                if (i != 0 && SameWriter(writers[i - 1], writers[i]))
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.OwnershipConflict,
                        "system " + writers[i].SystemKey + " is declared as a writer twice; one precompiled system "
                        + "type has one scheduling instance per world in V1 (P-039)"));
                    continue;
                }

                unique.Add(writers[i]);
            }

            return unique.ToArray();
        }

        private static void ValidateWriterDeclarations(WriterDeclaration[] writers, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < writers.Length; i++)
            {
                WriterDeclaration writer = writers[i];
                if (writer.Owner.Value.IsDefault)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        "system " + writer.SystemKey + " in stage " + writer.Stage
                        + " declares a default zero owner; every registered writer acts for one state owner (P-034)"));
                }

                if (writer.Access.Declarations.Count == 0)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.OwnershipConflict,
                        "system " + writer.SystemKey + " declares no access set; undeclared access rejects before "
                        + "activation (P-034, P-039)"));
                }

                for (int a = 0; a < writer.Access.Declarations.Count; a++)
                {
                    if (writer.Access.Declarations[a].Schema.Id.Value.IsDefault)
                    {
                        diagnostics.Add(Reject(
                            DiagnosticCode.MissingDependency,
                            "system " + writer.SystemKey + " declares access on a default zero schema"));
                    }
                }
            }
        }

        private static void CollectDomains(
            WriterDeclaration[] writers,
            List<DomainAccumulator> domains,
            Dictionary<Id128, DomainAccumulator> bySchema,
            List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < writers.Length; i++)
            {
                WriterDeclaration writer = writers[i];
                IReadOnlyList<SchemaRef> schemas = writer.DeclaredWriteSchemas();
                for (int s = 0; s < schemas.Count; s++)
                {
                    SchemaRef schema = schemas[s];
                    if (schema.Id.Value.IsDefault)
                    {
                        continue;
                    }

                    if (!bySchema.TryGetValue(schema.Id.Value, out DomainAccumulator? accumulator) || accumulator == null)
                    {
                        accumulator = new DomainAccumulator(schema);
                        bySchema.Add(schema.Id.Value, accumulator);
                        domains.Add(accumulator);
                    }

                    accumulator.Claims.Add(new DomainClaim(writer.Owner, writer.SystemKey));
                }
            }

            // One owner per domain, reported in canonical owner order so the diagnostic is independent of the order
            // the writers were declared or sorted in (P-008, P-034).
            for (int i = 0; i < domains.Count; i++)
            {
                DomainAccumulator domain = domains[i];
                domain.Claims.Sort(CompareClaims);
                for (int c = 0; c < domain.Claims.Count; c++)
                {
                    DomainClaim claim = domain.Claims[c];
                    if (!domain.OwnerDecided)
                    {
                        domain.Owner = claim.Owner;
                        domain.OwnerDecided = true;
                    }

                    if (!ContainsWriter(domain.Writers, claim.Writer))
                    {
                        domain.Writers.Add(claim.Writer);
                    }
                }

                for (int c = 0; c < domain.Claims.Count; c++)
                {
                    DomainClaim claim = domain.Claims[c];
                    if (!claim.Owner.Equals(domain.Owner))
                    {
                        domain.OwnerConflict = true;
                        break;
                    }
                }

                if (!domain.OwnerConflict)
                {
                    continue;
                }

                diagnostics.Add(Reject(
                    DiagnosticCode.OwnershipConflict,
                    "domain " + domain.Domain + " is claimed by " + domain.Owner.ToString() + " (" + domain.FirstWriterOf(domain.Owner)
                    + ") and by " + domain.SecondOwner().ToString() + " (" + domain.FirstWriterOf(domain.SecondOwner())
                    + "); one authoritative domain has exactly one owner unless ownership is explicitly transferred (P-034)"));
            }
            for (int i = 0; i < domains.Count; i++)
            {
                domains[i].Writers.Sort((left, right) => left.RegistrationKey.CompareTo(right.RegistrationKey));
            }
        }

        private static int CompareClaims(DomainClaim left, DomainClaim right)
        {
            int byOwner = left.Owner.Value.CompareTo(right.Owner.Value);
            return byOwner != 0 ? byOwner : left.Writer.RegistrationKey.CompareTo(right.Writer.RegistrationKey);
        }

        private static bool ContainsWriter(List<FactoryKey> writers, FactoryKey writer)
        {
            for (int i = 0; i < writers.Count; i++)
            {
                if (writers[i].RegistrationKey.Equals(writer.RegistrationKey))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateWriterOrderAndPartitions(
            OwnerAuthorityDeclaration declaration,
            WriterDeclaration[] writers,
            Dictionary<DomainWriterKey, PartitionAssignment> partitionIndex,
            List<Diagnostic> diagnostics)
        {
            Dictionary<Id128, List<Id128>> edges = BuildRequiredEdges(writers);

            for (int i = 0; i < writers.Length; i++)
            {
                WriterDeclaration left = writers[i];
                IReadOnlyList<SchemaRef> leftSchemas = left.DeclaredWriteSchemas();
                for (int j = i + 1; j < writers.Length; j++)
                {
                    WriterDeclaration right = writers[j];
                    for (int s = 0; s < leftSchemas.Count; s++)
                    {
                        SchemaRef domain = leftSchemas[s];
                        if (!WritesDomain(right, domain))
                        {
                            continue;
                        }

                        PartitionAssignment leftPartition = PartitionFor(partitionIndex, left, domain);
                        PartitionAssignment rightPartition = PartitionFor(partitionIndex, right, domain);
                        if (PartitionIdGenerator.ProvablyDisjoint(leftPartition, rightPartition))
                        {
                            continue;
                        }

                        if (IsOrdered(declaration, left, right, edges))
                        {
                            continue;
                        }

                        diagnostics.Add(Reject(
                            DiagnosticCode.AmbiguousOrder,
                            "domain " + domain + " is written by " + left.SystemKey + " (stage " + left.Stage
                            + ") and " + right.SystemKey + " (stage " + right.Stage
                            + ") with neither a directed required edge, a compiled stage order, nor a validated "
                            + "mutually exclusive partition; the scheduler never invents gameplay order (P-034, P-040)"));
                    }
                }
            }
        }

        private static void GeneratePartitions(
            WriterDeclaration[] writers,
            List<PartitionAssignment> partitions,
            Dictionary<DomainWriterKey, PartitionAssignment> index,
            List<Diagnostic> diagnostics)
        {
            var generator = new PartitionIdGenerator();

            for (int i = 0; i < writers.Length; i++)
            {
                WriterDeclaration writer = writers[i];
                IReadOnlyList<SchemaRef> schemas = writer.DeclaredWriteSchemas();
                for (int s = 0; s < schemas.Count; s++)
                {
                    SchemaRef domain = schemas[s];
                    Id128 declared = writer.DeclaredPartition(domain);
                    if (!declared.IsDefault)
                    {
                        Add(partitions, index, new PartitionAssignment(domain, writer.Owner, writer.SystemKey, declared, generated: false));
                        continue;
                    }

                    if (!writer.GeneratesPartitions)
                    {
                        continue;
                    }

                    Id128 generated = generator.Next(writer.Owner, domain, writer.SystemKey);
                    if (generated.IsDefault)
                    {
                        // A default id never proves disjointness; it is a rejection, not an implicit whole-domain claim.
                        diagnostics.Add(Reject(
                            DiagnosticCode.MissingDependency,
                            "the partition derivation of " + writer.SystemKey + " on " + domain
                            + " produced a default zero id, which never proves disjointness (P-034)"));
                        continue;
                    }

                    Add(partitions, index, new PartitionAssignment(domain, writer.Owner, writer.SystemKey, generated, generated: true));
                }
            }
        }

        private static void Add(
            List<PartitionAssignment> partitions,
            Dictionary<DomainWriterKey, PartitionAssignment> index,
            PartitionAssignment assignment)
        {
            var key = new DomainWriterKey(assignment.Domain, assignment.Writer);
            if (index.ContainsKey(key))
            {
                return;
            }

            index.Add(key, assignment);
            partitions.Add(assignment);
        }

        private static PartitionAssignment PartitionFor(
            Dictionary<DomainWriterKey, PartitionAssignment> index,
            WriterDeclaration writer,
            SchemaRef domain)
        {
            if (index.TryGetValue(new DomainWriterKey(domain, writer.SystemKey), out PartitionAssignment assignment))
            {
                return assignment;
            }

            return new PartitionAssignment(domain, writer.Owner, writer.SystemKey, Id128.Zero, generated: false);
        }

        private static void ValidateSlots(
            IReadOnlyList<SlotAuthorityDeclaration> slots,
            Dictionary<Id128, DomainAccumulator> bySchema,
            List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                SlotAuthorityDeclaration slot = slots[i];
                string name = "state slot " + slot.SlotId;

                if (slot.SlotId.Value.IsDefault)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero state-slot id is not a catalog identity"));
                    continue;
                }

                if (slot.Owner.Value.IsDefault)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        name + " declares a default zero owner; every authoritative slot names one owner (P-034)"));
                }

                if (slot.Schema.Id.Value.IsDefault)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        name + " declares a default zero schema"));
                    continue;
                }

                if (bySchema.TryGetValue(slot.Schema.Id.Value, out DomainAccumulator? domain)
                    && domain != null
                    && domain.OwnerDecided
                    && !domain.OwnerConflict
                    && !domain.Owner.Equals(slot.Owner))
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.OwnershipConflict,
                        name + " is owned by " + slot.Owner + " but domain " + slot.Schema
                        + " is written by owner " + domain.Owner
                        + "; a slot and its writers share one owner (P-034)"));
                }

                if (slot.LastSupport == LastSupportPolicy.TransferTo && !slot.HasTransferPolicy)
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.MissingDependency,
                        name + " declares TransferTo without a registered owner-transfer policy (P-032)"));
                }

                if (slot.Options.ResetPermitted && string.IsNullOrEmpty(slot.Options.ResetReason))
                {
                    diagnostics.Add(Reject(
                        DiagnosticCode.OwnershipConflict,
                        name + " permits a reset without recording the explicit reason P-032 requires"));
                }

                for (int m = 0; m < slot.MigrationKeys.Count; m++)
                {
                    if (slot.MigrationKeys[m].RegistrationKey.IsDefault)
                    {
                        diagnostics.Add(Reject(
                            DiagnosticCode.MissingDependency,
                            name + " declares a default zero migration key"));
                    }
                }
            }
        }

        private static void ValidateSlotLayoutsAgainstComponents(
            OwnerAuthorityDeclaration declaration,
            List<Diagnostic> diagnostics)
        {
            if (declaration.Components.Count == 0)
            {
                return;
            }

            for (int i = 0; i < declaration.Slots.Count; i++)
            {
                SlotAuthorityDeclaration slot = declaration.Slots[i];
                for (int c = 0; c < declaration.Components.Count; c++)
                {
                    ComponentLayoutDeclaration component = declaration.Components[c];
                    if (!component.Component.Id.Value.Equals(slot.Schema.Id.Value))
                    {
                        continue;
                    }

                    if (!component.Owner.Equals(slot.Owner))
                    {
                        diagnostics.Add(Reject(
                            DiagnosticCode.OwnershipConflict,
                            "state slot " + slot.SlotId + " is owned by " + slot.Owner + " but component "
                            + component.Component + " is physically owned by " + component.Owner
                            + "; one physical owner is required (P-033)"));
                    }
                }
            }
        }

        private static bool WritesDomain(WriterDeclaration writer, SchemaRef domain)
        {
            for (int i = 0; i < writer.Access.Declarations.Count; i++)
            {
                AccessDeclaration access = writer.Access.Declarations[i];
                if (access.Mode != AccessMode.Read && access.Schema.Id.Value.Equals(domain.Id.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static Dictionary<Id128, List<Id128>> BuildRequiredEdges(WriterDeclaration[] writers)
        {
            var edges = new Dictionary<Id128, List<Id128>>();
            var declared = new HashSet<Id128>();
            for (int i = 0; i < writers.Length; i++)
            {
                declared.Add(writers[i].SystemKey.RegistrationKey);
            }

            for (int i = 0; i < writers.Length; i++)
            {
                WriterDeclaration writer = writers[i];
                Id128 from = writer.SystemKey.RegistrationKey;
                if (from.IsDefault || edges.ContainsKey(from))
                {
                    continue;
                }

                var targets = new List<Id128>();
                CollectEdges(writer.RequiredBeforeSystems, declared, targets);
                CollectEdges(writer.RequiredAfterSystems, declared, targets);
                edges[from] = targets;
            }

            return edges;
        }

        private static void CollectEdges(IReadOnlyList<FactoryKey> keys, HashSet<Id128> declared, List<Id128> targets)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                Id128 key = keys[i].RegistrationKey;
                if (!key.IsDefault && declared.Contains(key) && !targets.Contains(key))
                {
                    targets.Add(key);
                }
            }
        }

        /// <summary>
        /// True when the compiled plan orders these two writers: a directed required edge inside one stage, or two
        /// distinct positions in the compiled stage order. Without a stage order a declaration must prove
        /// disjointness with partitions instead (P-040).
        /// </summary>
        private static bool IsOrdered(
            OwnerAuthorityDeclaration declaration,
            WriterDeclaration left,
            WriterDeclaration right,
            Dictionary<Id128, List<Id128>> edges)
        {
            int leftPosition = declaration.StagePosition(left.Stage);
            int rightPosition = declaration.StagePosition(right.Stage);
            if (leftPosition >= 0 && rightPosition >= 0 && leftPosition != rightPosition)
            {
                return true;
            }

            Id128 leftId = left.SystemKey.RegistrationKey;
            Id128 rightId = right.SystemKey.RegistrationKey;
            if (leftId.IsDefault || rightId.IsDefault)
            {
                return false;
            }

            return HasPath(edges, leftId, rightId) || HasPath(edges, rightId, leftId);
        }

        private static bool HasPath(Dictionary<Id128, List<Id128>> edges, Id128 from, Id128 to)
        {
            var visited = new HashSet<Id128>();
            var frontier = new List<Id128> { from };
            while (frontier.Count != 0)
            {
                Id128 current = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);
                if (current.Equals(to) && !current.Equals(from))
                {
                    return true;
                }

                if (!visited.Add(current))
                {
                    continue;
                }

                if (!edges.TryGetValue(current, out List<Id128>? targets) || targets == null)
                {
                    continue;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    frontier.Add(targets[i]);
                }
            }

            return false;
        }

        private static bool SameWriter(WriterDeclaration left, WriterDeclaration right)
            => left.SystemKey.RegistrationKey.Equals(right.SystemKey.RegistrationKey);

        private static int CompareWriters(OwnerAuthorityDeclaration declaration, WriterDeclaration left, WriterDeclaration right)
        {
            int leftPosition = Normalize(declaration.StagePosition(left.Stage));
            int rightPosition = Normalize(declaration.StagePosition(right.Stage));
            int byPosition = leftPosition.CompareTo(rightPosition);
            if (byPosition != 0)
            {
                return byPosition;
            }

            int byStage = left.Stage.Value.CompareTo(right.Stage.Value);
            if (byStage != 0)
            {
                return byStage;
            }

            int byKey = left.SystemKey.RegistrationKey.CompareTo(right.SystemKey.RegistrationKey);
            return byKey != 0 ? byKey : left.Owner.Value.CompareTo(right.Owner.Value);
        }

        private static int Normalize(int position) => position < 0 ? int.MaxValue : position;

        private static int CompareDomainRecords(DomainOwnerRecord left, DomainOwnerRecord right)
            => left.Domain.Id.Value.CompareTo(right.Domain.Id.Value);

        private static Diagnostic Reject(DiagnosticCode code, string summary)
            => Diagnostic.Create(code, OperationPhase.Validation, default(OperationId), summary);

        /// <summary>Mutable per-domain accumulation; only this validator observes it.</summary>
        private sealed class DomainAccumulator
        {
            internal DomainAccumulator(SchemaRef domain)
            {
                Domain = domain;
            }

            internal SchemaRef Domain { get; }

            internal OwnerId Owner { get; set; }

            internal bool OwnerDecided { get; set; }

            internal bool OwnerConflict { get; set; }

            /// <summary>Writer keys of this domain in canonical (ascending key) order (P-008).</summary>
            internal List<FactoryKey> Writers { get; } = new List<FactoryKey>();

            internal List<DomainClaim> Claims { get; } = new List<DomainClaim>();

            internal FactoryKey FirstWriterOf(OwnerId owner)
            {
                for (int i = 0; i < Claims.Count; i++)
                {
                    if (Claims[i].Owner.Equals(owner))
                    {
                        return Claims[i].Writer;
                    }
                }

                return default(FactoryKey);
            }

            /// <summary>The second distinct owner in canonical order; default when only one owner claims the domain.</summary>
            internal OwnerId SecondOwner()
            {
                for (int i = 0; i < Claims.Count; i++)
                {
                    if (!Claims[i].Owner.Equals(Owner))
                    {
                        return Claims[i].Owner;
                    }
                }

                return default(OwnerId);
            }
        }

        /// <summary>One writer's ownership claim on one domain, before conflicts are resolved (P-034).</summary>
        private readonly struct DomainClaim
        {
            internal readonly OwnerId Owner;
            internal readonly FactoryKey Writer;

            internal DomainClaim(OwnerId owner, FactoryKey writer)
            {
                Owner = owner;
                Writer = writer;
            }
        }

        /// <summary>Canonical (domain, writer) lookup key of one partition claim.</summary>
        private readonly struct DomainWriterKey : IEquatable<DomainWriterKey>
        {
            private readonly Id128 domain;
            private readonly Id128 writer;

            internal DomainWriterKey(SchemaRef domain, FactoryKey writer)
            {
                this.domain = domain.Id.Value;
                this.writer = writer.RegistrationKey;
            }

            public bool Equals(DomainWriterKey other)
                => domain.Equals(other.domain) && writer.Equals(other.writer);

            public override bool Equals(object? obj) => obj is DomainWriterKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (domain.GetHashCode() * 397) ^ writer.GetHashCode();
                }
            }
        }

        /// <summary>Canonical diagnostic order: code, then summary, then the first named identity (P-008).</summary>
        private sealed class DiagnosticOrder : IComparer<Diagnostic>
        {
            internal static readonly DiagnosticOrder Instance = new DiagnosticOrder();

            public int Compare(Diagnostic? x, Diagnostic? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                int byCode = ((int)x.Code).CompareTo((int)y.Code);
                if (byCode != 0)
                {
                    return byCode;
                }

                int bySummary = string.CompareOrdinal(x.Summary, y.Summary);
                if (bySummary != 0)
                {
                    return bySummary;
                }

                Id128 leftId = x.InvolvedIds.Count != 0 ? x.InvolvedIds[0] : Id128.Zero;
                Id128 rightId = y.InvolvedIds.Count != 0 ? y.InvolvedIds[0] : Id128.Zero;
                return leftId.CompareTo(rightId);
            }
        }
    }
}
