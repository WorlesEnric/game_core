// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Generated-shape source: emitted from the identity/version tables in docs/game-core/05-contracts-and-data-model.md
// and P-004/P-005 in docs/game-core/00-core-protocols.md. GC-003 replaces it with catalog-generated
// output that must reproduce this surface; never hand-edit members.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>Composition-revision counter; increments only when a composition proposal publishes (P-006).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct CompositionRevision : IEquatable<CompositionRevision>, IComparable<CompositionRevision>
    {
        public static readonly CompositionRevision Zero = new CompositionRevision(0UL);
        public static readonly CompositionRevision First = new CompositionRevision(1UL);
        public static readonly CompositionRevision MaxValue = new CompositionRevision(ulong.MaxValue);

        public readonly ulong Value;

        public CompositionRevision(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out CompositionRevision next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new CompositionRevision(Value + 1UL);
            return true;
        }

        public bool Equals(CompositionRevision other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is CompositionRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(CompositionRevision other) => Value.CompareTo(other.Value);
        public static bool operator ==(CompositionRevision left, CompositionRevision right) => left.Equals(right);
        public static bool operator !=(CompositionRevision left, CompositionRevision right) => !left.Equals(right);
        public static bool operator <(CompositionRevision left, CompositionRevision right) => left.Value < right.Value;
        public static bool operator >(CompositionRevision left, CompositionRevision right) => left.Value > right.Value;
        public static bool operator <=(CompositionRevision left, CompositionRevision right) => left.Value <= right.Value;
        public static bool operator >=(CompositionRevision left, CompositionRevision right) => left.Value >= right.Value;
        public override string ToString() => "CompositionRevision(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Assembly-epoch counter; increments at the same publication as the revision (P-006).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct AssemblyEpoch : IEquatable<AssemblyEpoch>, IComparable<AssemblyEpoch>
    {
        public static readonly AssemblyEpoch Zero = new AssemblyEpoch(0UL);
        public static readonly AssemblyEpoch First = new AssemblyEpoch(1UL);
        public static readonly AssemblyEpoch MaxValue = new AssemblyEpoch(ulong.MaxValue);

        public readonly ulong Value;

        public AssemblyEpoch(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out AssemblyEpoch next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new AssemblyEpoch(Value + 1UL);
            return true;
        }

        public bool Equals(AssemblyEpoch other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AssemblyEpoch other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(AssemblyEpoch other) => Value.CompareTo(other.Value);
        public static bool operator ==(AssemblyEpoch left, AssemblyEpoch right) => left.Equals(right);
        public static bool operator !=(AssemblyEpoch left, AssemblyEpoch right) => !left.Equals(right);
        public static bool operator <(AssemblyEpoch left, AssemblyEpoch right) => left.Value < right.Value;
        public static bool operator >(AssemblyEpoch left, AssemblyEpoch right) => left.Value > right.Value;
        public static bool operator <=(AssemblyEpoch left, AssemblyEpoch right) => left.Value <= right.Value;
        public static bool operator >=(AssemblyEpoch left, AssemblyEpoch right) => left.Value >= right.Value;
        public override string ToString() => "AssemblyEpoch(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Activation-epoch counter; changes when an installation loses or gains execution authority (P-006).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct ActivationEpoch : IEquatable<ActivationEpoch>, IComparable<ActivationEpoch>
    {
        public static readonly ActivationEpoch Zero = new ActivationEpoch(0UL);
        public static readonly ActivationEpoch First = new ActivationEpoch(1UL);
        public static readonly ActivationEpoch MaxValue = new ActivationEpoch(ulong.MaxValue);

        public readonly ulong Value;

        public ActivationEpoch(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out ActivationEpoch next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new ActivationEpoch(Value + 1UL);
            return true;
        }

        public bool Equals(ActivationEpoch other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is ActivationEpoch other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(ActivationEpoch other) => Value.CompareTo(other.Value);
        public static bool operator ==(ActivationEpoch left, ActivationEpoch right) => left.Equals(right);
        public static bool operator !=(ActivationEpoch left, ActivationEpoch right) => !left.Equals(right);
        public static bool operator <(ActivationEpoch left, ActivationEpoch right) => left.Value < right.Value;
        public static bool operator >(ActivationEpoch left, ActivationEpoch right) => left.Value > right.Value;
        public static bool operator <=(ActivationEpoch left, ActivationEpoch right) => left.Value <= right.Value;
        public static bool operator >=(ActivationEpoch left, ActivationEpoch right) => left.Value >= right.Value;
        public override string ToString() => "ActivationEpoch(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Committed logical-step counter; increments only at successful simulation commit (P-006).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct LogicalStepId : IEquatable<LogicalStepId>, IComparable<LogicalStepId>
    {
        public static readonly LogicalStepId Zero = new LogicalStepId(0UL);
        public static readonly LogicalStepId First = new LogicalStepId(1UL);
        public static readonly LogicalStepId MaxValue = new LogicalStepId(ulong.MaxValue);

        public readonly ulong Value;

        public LogicalStepId(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out LogicalStepId next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new LogicalStepId(Value + 1UL);
            return true;
        }

        public bool Equals(LogicalStepId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is LogicalStepId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(LogicalStepId other) => Value.CompareTo(other.Value);
        public static bool operator ==(LogicalStepId left, LogicalStepId right) => left.Equals(right);
        public static bool operator !=(LogicalStepId left, LogicalStepId right) => !left.Equals(right);
        public static bool operator <(LogicalStepId left, LogicalStepId right) => left.Value < right.Value;
        public static bool operator >(LogicalStepId left, LogicalStepId right) => left.Value > right.Value;
        public static bool operator <=(LogicalStepId left, LogicalStepId right) => left.Value <= right.Value;
        public static bool operator >=(LogicalStepId left, LogicalStepId right) => left.Value >= right.Value;
        public override string ToString() => "LogicalStepId(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Installation-generation counter; changes on unmount/remount, never on ordinary reconfigure (P-005).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct InstallationGeneration : IEquatable<InstallationGeneration>, IComparable<InstallationGeneration>
    {
        public static readonly InstallationGeneration Zero = new InstallationGeneration(0UL);
        public static readonly InstallationGeneration First = new InstallationGeneration(1UL);
        public static readonly InstallationGeneration MaxValue = new InstallationGeneration(ulong.MaxValue);

        public readonly ulong Value;

        public InstallationGeneration(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out InstallationGeneration next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new InstallationGeneration(Value + 1UL);
            return true;
        }

        public bool Equals(InstallationGeneration other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is InstallationGeneration other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(InstallationGeneration other) => Value.CompareTo(other.Value);
        public static bool operator ==(InstallationGeneration left, InstallationGeneration right) => left.Equals(right);
        public static bool operator !=(InstallationGeneration left, InstallationGeneration right) => !left.Equals(right);
        public static bool operator <(InstallationGeneration left, InstallationGeneration right) => left.Value < right.Value;
        public static bool operator >(InstallationGeneration left, InstallationGeneration right) => left.Value > right.Value;
        public static bool operator <=(InstallationGeneration left, InstallationGeneration right) => left.Value <= right.Value;
        public static bool operator >=(InstallationGeneration left, InstallationGeneration right) => left.Value >= right.Value;
        public override string ToString() => "InstallationGeneration(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Immutable content/definition revision (P-006).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct DefinitionRevision : IEquatable<DefinitionRevision>, IComparable<DefinitionRevision>
    {
        public static readonly DefinitionRevision Zero = new DefinitionRevision(0UL);
        public static readonly DefinitionRevision First = new DefinitionRevision(1UL);
        public static readonly DefinitionRevision MaxValue = new DefinitionRevision(ulong.MaxValue);

        public readonly ulong Value;

        public DefinitionRevision(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out DefinitionRevision next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new DefinitionRevision(Value + 1UL);
            return true;
        }

        public bool Equals(DefinitionRevision other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is DefinitionRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(DefinitionRevision other) => Value.CompareTo(other.Value);
        public static bool operator ==(DefinitionRevision left, DefinitionRevision right) => left.Equals(right);
        public static bool operator !=(DefinitionRevision left, DefinitionRevision right) => !left.Equals(right);
        public static bool operator <(DefinitionRevision left, DefinitionRevision right) => left.Value < right.Value;
        public static bool operator >(DefinitionRevision left, DefinitionRevision right) => left.Value > right.Value;
        public static bool operator <=(DefinitionRevision left, DefinitionRevision right) => left.Value <= right.Value;
        public static bool operator >=(DefinitionRevision left, DefinitionRevision right) => left.Value >= right.Value;
        public override string ToString() => "DefinitionRevision(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Committed-event sequence; unique per world within retention (P-045).</summary>
    /// <remarks>Overflow policy is a protocol decision made by callers (P-005: reject further allocation and
    /// require world recreation, never wraparound). This seam exposes the pure arithmetic predicate only.</remarks>
    public readonly struct EventSequence : IEquatable<EventSequence>, IComparable<EventSequence>
    {
        public static readonly EventSequence Zero = new EventSequence(0UL);
        public static readonly EventSequence First = new EventSequence(1UL);
        public static readonly EventSequence MaxValue = new EventSequence(ulong.MaxValue);

        public readonly ulong Value;

        public EventSequence(ulong value)
        {
            Value = value;
        }

        public bool TryIncrement(out EventSequence next)
        {
            if (Value == ulong.MaxValue)
            {
                next = this;
                return false;
            }

            next = new EventSequence(Value + 1UL);
            return true;
        }

        public bool Equals(EventSequence other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is EventSequence other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(EventSequence other) => Value.CompareTo(other.Value);
        public static bool operator ==(EventSequence left, EventSequence right) => left.Equals(right);
        public static bool operator !=(EventSequence left, EventSequence right) => !left.Equals(right);
        public static bool operator <(EventSequence left, EventSequence right) => left.Value < right.Value;
        public static bool operator >(EventSequence left, EventSequence right) => left.Value > right.Value;
        public static bool operator <=(EventSequence left, EventSequence right) => left.Value <= right.Value;
        public static bool operator >=(EventSequence left, EventSequence right) => left.Value >= right.Value;
        public override string ToString() => "EventSequence(" + Value.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
