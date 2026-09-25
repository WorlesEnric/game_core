// GameCore.Unity.Runtime — the production validator behind `ICompositionEditValidator` (GC-013, P-014).
//
// P-014: "A conflict, missing migration, or exceeded budget rejects the switch and keeps the old mode/assembly."
// 02 s5 adds the reason this cannot live in the pure composition model: "A proposed Conservative→Automatic switch
// may reveal an exclusive conflict; the result is a diagnostic and the old mode intact."
//
// Whether a mode switch is honourable is a *derivation* fact: eligibility, capability contracts, target
// descriptors and the composition policies of the affected slots. So the lane asks this validator during planning,
// before anything is staged, and this validator answers by deriving the *proposed* composition in its own new mode
// and refusing when the engine rejects it. Nothing is published on a refusal, so the old mode and the old active
// assembly stay exactly as they were (00 s9).
//
// Scope of the check: a proposal that does not move the mode is accepted without any work, because every other
// edit is derived and validated by the normal publication path anyway. A proposal whose derivation input cannot be
// built at all is accepted too, deliberately: an unavailable target view refuses *every* proposal at the same
// step of the pipeline, so turning it into a composition rejection would make a mode switch fail for a reason that
// has nothing to do with the mode. The derived-assembly pipeline reports that condition with its own code.
//
// Everything here is pure and side-effect free: it reads two immutable composition states and the caller's value
// source, and it never touches live storage.
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
    /// Rejects a mode switch whose proposed closure cannot be derived, keeping the old mode and assembly (P-014).
    /// </summary>
    public sealed class DerivationModeSwitchValidator : ICompositionEditValidator
    {
        private readonly IDerivationValueSource values;
        private readonly Func<IReadOnlyList<DerivationTarget>> targets;
        private readonly IReadOnlyList<DerivationRuleKeys> ruleKeys;
        private readonly IReadOnlyList<ProviderSelectionOverride> overrides;
        private readonly DerivationOptions options;

        /// <summary>
        /// Creates the validator. <paramref name="targets"/> is read lazily on every check, so a world whose live
        /// target set grew since construction is validated against its current population (P-024).
        /// </summary>
        public DerivationModeSwitchValidator(
            IDerivationValueSource values,
            Func<IReadOnlyList<DerivationTarget>> targets,
            IReadOnlyList<DerivationRuleKeys>? ruleKeys = null,
            IReadOnlyList<ProviderSelectionOverride>? overrides = null,
            DerivationOptions? options = null)
        {
            this.values = values ?? throw new ArgumentNullException(nameof(values));
            this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
            this.ruleKeys = ruleKeys ?? Array.Empty<DerivationRuleKeys>();
            this.overrides = overrides ?? Array.Empty<ProviderSelectionOverride>();
            this.options = options ?? DerivationOptions.Default;
        }

        /// <summary>Mode-switch proposals this validator actually derived.</summary>
        public int Checks { get; private set; }

        /// <summary>Switches this validator refused; every one kept the old mode published (P-014).</summary>
        public int Refusals { get; private set; }

        /// <summary>Switches accepted because the proposed closure derives cleanly.</summary>
        public int Acceptances { get; private set; }

        /// <summary>Diagnostic of the last refusal, for the caller's report; <see cref="DiagnosticCode.None"/> before one.</summary>
        public DiagnosticCode LastRefusalCode { get; private set; }

        /// <summary>Detail of the last refusal, naming the conflict the switch would expose.</summary>
        public string LastRefusalDetail { get; private set; } = string.Empty;

        /// <summary>
        /// Validates one planned proposal. Only a mode move is checked, because only a mode move can change the
        /// eligibility of contributions the committed composition already carries (P-013).
        /// </summary>
        public EditValidationResult Validate(
            CompositionState before,
            CompositionState after,
            CompositionChangeSet changeSet)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            if (changeSet == null || !changeSet.ModeChanged)
            {
                return EditValidationResult.Accept;
            }

            Checks++;
            DerivationInputReport input = CompositionDerivationInput.Build(after, targets(), ruleKeys, overrides);
            if (!input.Succeeded || input.Snapshot == null)
            {
                // An unbuildable input refuses every proposal at the pipeline's own step; refusing here would
                // attribute an unrelated failure to the mode (see the file header).
                return EditValidationResult.Accept;
            }

            DerivationResult derivation = DerivationEngine.Derive(input.Snapshot, values, options, null);
            if (derivation.Accepted)
            {
                Acceptances++;
                return EditValidationResult.Accept;
            }

            Refusals++;
            LastRefusalCode = derivation.DiagnosticCode;
            LastRefusalDetail = "the " + before.Mode.ToString() + " -> " + after.Mode.ToString()
                + " switch would expose a composition the kernel refuses ("
                + derivation.Rejection.ToString(CultureInfo.InvariantCulture) + "): " + Describe(derivation);
            return EditValidationResult.Refuse(LastRefusalCode, LastRefusalDetail);
        }

        /// <summary>One-line audit text of this validator's work, for evidence and diagnostics (P-052).</summary>
        public string Describe() =>
            "modeSwitchValidator{checks=" + Checks.ToString(CultureInfo.InvariantCulture)
            + ";accepted=" + Acceptances.ToString(CultureInfo.InvariantCulture)
            + ";refused=" + Refusals.ToString(CultureInfo.InvariantCulture)
            + ";lastCode=" + DiagnosticCodeText.Of(LastRefusalCode) + "}";

        public override string ToString() => Describe();

        private static string Describe(DerivationResult derivation)
        {
            if (derivation.CompositionFailures.Count != 0)
            {
                return derivation.CompositionFailures[0].ToString();
            }

            if (derivation.ValidationProblems.Count != 0)
            {
                return derivation.ValidationProblems[0].ToString();
            }

            return derivation.Rejection.ToString() + " (no witness retained)";
        }
    }
}
