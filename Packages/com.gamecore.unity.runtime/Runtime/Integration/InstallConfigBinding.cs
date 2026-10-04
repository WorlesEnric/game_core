// GameCore.Unity.Runtime — SADR-013 (studio): an installation's configuration reaches the systems that run for it.
//
// Before this seam, derivation copied every rule's frozen manifest payload into its contribution and never read the
// installation's composed configuration document, so an O-05 reconfigure changed the install record (config
// revision, config hash, activation epoch) and nothing a system could observe. The chosen mechanism is the smallest
// one that is correct for the existing binding-row path:
//
//   * a catalog declares, per rule, which configuration field supplies that rule's payload (`RuleConfigBinding`);
//   * when the committed composition is translated into a derivation input, the field's value in the installation's
//     *effective* configuration (schema defaults, inherited layer, local patch - P-020) is encoded in the canonical
//     slot encoding and handed to derivation as a `BoundRulePayload`;
//   * derivation contributes those bytes under the rule's unchanged contribution key, so the next publication changes
//     the binding row's value in place (a "changed" contribution, P-017) and every system that reads its binding row
//     sees the new value at its next step. Runtime slot state is untouched (O-05: "numerical config changes preserve
//     runtime counters").
//
// A field that is absent, or explicitly null, leaves the manifest's frozen payload in place (P-020 distinguishes
// null from missing; both mean "no configured override" here). A field of a kind that has no canonical slot
// encoding is a refusal of the derivation input, never a silent fallback to the default.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>
    /// One declared binding: the payload of <see cref="Rule"/> is the value of configuration field
    /// <see cref="ConfigField"/> of the installation that contributes it (SADR-013).
    /// </summary>
    public readonly struct RuleConfigBinding
    {
        public RuleConfigBinding(RuleId rule, Id128 configField)
        {
            if (rule.IsDefault)
            {
                throw new ArgumentException("A configuration binding names a real rule identity (P-004).", nameof(rule));
            }

            if (configField.IsDefault)
            {
                throw new ArgumentException("A configuration binding names a real field key (P-020).", nameof(configField));
            }

            Rule = rule;
            ConfigField = configField;
        }

        public RuleId Rule { get; }

        /// <summary>Stable key of the configuration field whose value becomes the rule's payload.</summary>
        public Id128 ConfigField { get; }

        public override string ToString() => "configBinding(" + Rule.ToString() + "<-" + ConfigField.ToString() + ")";
    }

    /// <summary>Resolves an installation's configuration-bound rule payloads (SADR-013).</summary>
    public static class InstallConfigBinding
    {
        /// <summary>
        /// Encodes one configuration value in the canonical slot encoding a binding row carries: a big-endian int32
        /// for <c>Int32</c>, for a <c>UInt32</c> no larger than <see cref="int.MaxValue"/> and for a <c>Bool</c>
        /// (0/1); the raw bytes for a <c>Bytes</c> field. Every other kind has no slot encoding and is refused.
        /// </summary>
        public static bool TryEncode(ConfigFieldValue value, out FrozenPayload? payload)
        {
            payload = null;
            switch (value.Kind)
            {
                case ConfigValueKind.Int32:
                    payload = IntegrationSlotValues.WriteInt32(value.AsInt32);
                    return true;
                case ConfigValueKind.UInt32:
                    if (value.AsUInt32 > int.MaxValue)
                    {
                        return false;
                    }

                    payload = IntegrationSlotValues.WriteInt32((int)value.AsUInt32);
                    return true;
                case ConfigValueKind.Bool:
                    payload = IntegrationSlotValues.WriteInt32(value.AsBool ? 1 : 0);
                    return true;
                case ConfigValueKind.Bytes:
                    payload = value.AsBytes;
                    return payload != null;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The bound payloads of one installation: every binding whose rule the installation's manifest declares and
        /// whose field is present with a value in the effective configuration. A bound field with no slot encoding
        /// refuses with <see cref="DiagnosticCode.UnsupportedVersion"/> and names the field.
        /// </summary>
        public static bool TryBind(
            InstallEntry entry,
            IReadOnlyList<RuleConfigBinding>? bindings,
            out IReadOnlyList<BoundRulePayload> bound,
            out DiagnosticCode code,
            out string detail)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            bound = Array.Empty<BoundRulePayload>();
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (bindings == null || bindings.Count == 0)
            {
                return true;
            }

            List<BoundRulePayload>? result = null;
            IReadOnlyList<DerivationRule> rules = entry.Manifest.DerivationRules;
            for (int r = 0; r < rules.Count; r++)
            {
                DerivationRule rule = rules[r];
                for (int b = 0; b < bindings.Count; b++)
                {
                    RuleConfigBinding binding = bindings[b];
                    if (!binding.Rule.Equals(rule.RuleId))
                    {
                        continue;
                    }

                    if (!entry.Config.TryGetField(binding.ConfigField, out ConfigFieldValue value)
                        || value.Kind == ConfigValueKind.Null)
                    {
                        // Missing and explicit null both mean "no configured override": the manifest payload stands.
                        break;
                    }

                    if (!TryEncode(value, out FrozenPayload? payload) || payload == null)
                    {
                        code = DiagnosticCode.UnsupportedVersion;
                        detail = "installation " + entry.Instance.ToString() + " binds rule " + rule.RuleId.ToString()
                            + " to configuration field " + binding.ConfigField.ToString() + " whose value "
                            + value.ToString() + " has no canonical slot encoding (SADR-013, 05 s6).";
                        bound = Array.Empty<BoundRulePayload>();
                        return false;
                    }

                    if (result == null)
                    {
                        result = new List<BoundRulePayload>();
                    }

                    result.Add(new BoundRulePayload(rule.RuleId, payload));
                    break;
                }
            }

            if (result != null)
            {
                bound = result.AsReadOnly();
            }

            return true;
        }

        /// <summary>Validates a binding table: one binding per rule, so a rule never has two configured sources.</summary>
        public static bool TryValidate(IReadOnlyList<RuleConfigBinding>? bindings, out string detail)
        {
            detail = string.Empty;
            if (bindings == null)
            {
                return true;
            }

            var seen = new HashSet<Id128>();
            for (int i = 0; i < bindings.Count; i++)
            {
                if (!seen.Add(bindings[i].Rule.Value))
                {
                    detail = "rule " + bindings[i].Rule.ToString() + " is bound to more than one configuration field ("
                        + bindings.Count.ToString(CultureInfo.InvariantCulture) + " bindings); one rule has one source.";
                    return false;
                }
            }

            return true;
        }
    }
}
