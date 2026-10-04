// GameCore.Unity.Runtime — SADR-011 (studio): validate-before-commit for the composition lane.
//
// F5 of the studio gap assessment: the lane used to publish a composition edit and only then ask the world to derive
// and publish the assembly for it. When the world refused (a capability conflict, a plan the planner rejected, a
// budget), the lane had already advanced its revision/epoch and the world had not, so the two halves of P-006's one
// publication series diverged and every later edit was refused as stale.
//
// The lane already owns the right seam: `ICompositionEditValidator` is consulted while an edit is *planned*, before
// anything is staged, and a refusal there is an ordinary lane rejection - a settled ledger row, no revision, no epoch,
// the old composition and the old assembly both still published (00 s9, P-028). This file adds two validators for it:
//
//   * `CompositionEditValidatorSet` composes several validators in a fixed order, stops at the first refusal and keeps
//     the structured reason of every validator it asked, so a caller can report *which* check refused and why;
//   * `DerivedAssemblyPreflightValidator` runs the derived-assembly pipeline's own dry run (`Preflight`: derive,
//     translate, plan - everything a publication does before its first live write) against the proposed
//     composition. A proposal the world would refuse is refused on the lane instead.
//
// `DerivationModeSwitchValidator` keeps its behaviour and its counters when it is a member of a set: it is asked
// first, so a mode switch that would expose a conflict is refused with the same code it always reported.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>One validator's verdict on one planned proposal, kept so a refusal can name its source.</summary>
    public sealed class EditValidationReason
    {
        public EditValidationReason(string validator, bool accepted, DiagnosticCode code, string detail)
        {
            Validator = validator ?? string.Empty;
            Accepted = accepted;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable name of the validator that produced this verdict.</summary>
        public string Validator { get; }

        public bool Accepted { get; }

        /// <summary><see cref="DiagnosticCode.None"/> for an acceptance; the refusing module's code otherwise.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>The refusal's witness: the conflict, plan state or input failure that caused it.</summary>
        public string Detail { get; }

        public override string ToString() =>
            Validator + ": " + (Accepted ? "accept" : "refuse(" + DiagnosticCodeText.Of(Code) + ": " + Detail + ")");
    }

    /// <summary>
    /// An ordered set of lane validators (SADR-011). The first refusal wins and later validators are not asked, so the
    /// refusal is deterministic and no expensive check runs for a proposal an earlier one already refused.
    /// </summary>
    public sealed class CompositionEditValidatorSet : ICompositionEditValidator
    {
        private readonly IReadOnlyList<ICompositionEditValidator> validators;
        private IReadOnlyList<EditValidationReason> lastReasons = Array.Empty<EditValidationReason>();

        public CompositionEditValidatorSet(IReadOnlyList<ICompositionEditValidator> validators)
        {
            if (validators == null)
            {
                throw new ArgumentNullException(nameof(validators));
            }

            var copy = new List<ICompositionEditValidator>(validators.Count);
            for (int i = 0; i < validators.Count; i++)
            {
                if (validators[i] == null)
                {
                    throw new ArgumentException("A validator set holds real validators only.", nameof(validators));
                }

                if (ReferenceEquals(validators[i], this))
                {
                    throw new ArgumentException("A validator set cannot contain itself.", nameof(validators));
                }

                copy.Add(validators[i]);
            }

            this.validators = copy.AsReadOnly();
        }

        /// <summary>The member validators in the order they are asked.</summary>
        public IReadOnlyList<ICompositionEditValidator> Validators => validators;

        /// <summary>Proposals this set validated.</summary>
        public int Checks { get; private set; }

        /// <summary>Proposals this set refused; each one kept the published composition (P-028).</summary>
        public int Refusals { get; private set; }

        /// <summary>The verdict of every validator asked for the most recent proposal, in order.</summary>
        public IReadOnlyList<EditValidationReason> LastReasons => lastReasons;

        /// <summary>The refusing verdict of the most recent proposal, or null when it was accepted.</summary>
        public EditValidationReason? LastRefusal { get; private set; }

        /// <summary>The first member of type <typeparamref name="T"/>, or null when there is none.</summary>
        public T? Find<T>() where T : class, ICompositionEditValidator
        {
            for (int i = 0; i < validators.Count; i++)
            {
                if (validators[i] is T match)
                {
                    return match;
                }
            }

            return null;
        }

        public EditValidationResult Validate(CompositionState before, CompositionState after, CompositionChangeSet changeSet)
        {
            Checks++;
            var reasons = new List<EditValidationReason>(validators.Count);
            LastRefusal = null;
            for (int i = 0; i < validators.Count; i++)
            {
                ICompositionEditValidator validator = validators[i];
                EditValidationResult result = validator.Validate(before, after, changeSet);
                var reason = new EditValidationReason(NameOf(validator), result.Accepted, result.Code, result.Detail);
                reasons.Add(reason);
                if (!result.Accepted)
                {
                    Refusals++;
                    LastRefusal = reason;
                    lastReasons = reasons.AsReadOnly();
                    return EditValidationResult.Refuse(result.Code, reason.Validator + ": " + result.Detail);
                }
            }

            lastReasons = reasons.AsReadOnly();
            return EditValidationResult.Accept;
        }

        public override string ToString() =>
            "validatorSet{members=" + validators.Count.ToString(CultureInfo.InvariantCulture)
            + ";checks=" + Checks.ToString(CultureInfo.InvariantCulture)
            + ";refused=" + Refusals.ToString(CultureInfo.InvariantCulture) + "}";

        private static string NameOf(ICompositionEditValidator validator) =>
            validator is DerivedAssemblyPreflightValidator ? DerivedAssemblyPreflightValidator.ValidatorName
                : validator is DerivationModeSwitchValidator ? "mode-switch"
                : validator.GetType().Name;
    }

    /// <summary>
    /// SADR-011 validate-before-commit: refuses on the lane every proposal whose derived assembly the world would
    /// refuse, by running the pipeline's dry run (<see cref="DerivedAssemblyPipeline.Preflight"/>) against the
    /// proposed composition before anything is staged or published.
    /// </summary>
    /// <remarks>
    /// The lane needs its validator at construction and the pipeline needs the lane, so the pipeline is attached
    /// after both exist (<see cref="Attach"/>). An unattached preflight refuses every proposal: a lane that was meant
    /// to validate before committing never publishes unvalidated work by accident.
    /// </remarks>
    public sealed class DerivedAssemblyPreflightValidator : ICompositionEditValidator
    {
        /// <summary>Name this validator reports in a <see cref="CompositionEditValidatorSet"/> reason.</summary>
        public const string ValidatorName = "world-preflight";

        private readonly Id128 issuer;
        private ulong sequence;

        /// <param name="issuer">Issuer identity of the dry-run operations this validator mints (P-050).</param>
        public DerivedAssemblyPreflightValidator(Id128 issuer)
        {
            if (issuer.IsDefault)
            {
                throw new ArgumentException("A preflight issuer is a real identity (P-004).", nameof(issuer));
            }

            this.issuer = issuer;
        }

        /// <summary>The pipeline whose dry run answers for the world; null until <see cref="Attach"/>.</summary>
        public DerivedAssemblyPipeline? Pipeline { get; private set; }

        public int Checks { get; private set; }

        public int Acceptances { get; private set; }

        public int Refusals { get; private set; }

        public DiagnosticCode LastRefusalCode { get; private set; }

        /// <summary>The refusal's witness: the module that refused and its own detail.</summary>
        public string LastRefusalDetail { get; private set; } = string.Empty;

        /// <summary>The most recent dry-run report, accepted or refused.</summary>
        public DerivedAssemblyReport? LastReport { get; private set; }

        /// <summary>Attaches the world's pipeline once; a second, different pipeline is refused (P-004).</summary>
        public void Attach(DerivedAssemblyPipeline pipeline)
        {
            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (Pipeline != null && !ReferenceEquals(Pipeline, pipeline))
            {
                throw new InvalidOperationException(
                    "This preflight is already attached to another world's pipeline (P-004).");
            }

            if (!ReferenceEquals(pipeline.Lane.Validator, this) && !ContainsThis(pipeline.Lane.Validator))
            {
                throw new ArgumentException(
                    "The pipeline's lane does not consult this preflight, so attaching it would validate nothing "
                    + "(SADR-011).",
                    nameof(pipeline));
            }

            Pipeline = pipeline;
        }

        public EditValidationResult Validate(CompositionState before, CompositionState after, CompositionChangeSet changeSet)
        {
            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            Checks++;
            DerivedAssemblyPipeline? pipeline = Pipeline;
            if (pipeline == null)
            {
                return Refuse(
                    DiagnosticCode.MissingDependency,
                    "the world preflight is not attached to a pipeline, so no proposal can be validated before it "
                    + "commits (SADR-011)",
                    null);
            }

            sequence++;
            var operation = new OperationId(after.World, issuer, sequence);
            DerivedAssemblyReport report = pipeline.Preflight(after, operation);
            LastReport = report;
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                return Refuse(report.Code, report.Detail, report);
            }

            Acceptances++;
            return EditValidationResult.Accept;
        }

        public override string ToString() =>
            "worldPreflight{checks=" + Checks.ToString(CultureInfo.InvariantCulture)
            + ";accepted=" + Acceptances.ToString(CultureInfo.InvariantCulture)
            + ";refused=" + Refusals.ToString(CultureInfo.InvariantCulture)
            + ";lastCode=" + DiagnosticCodeText.Of(LastRefusalCode) + "}";

        private EditValidationResult Refuse(DiagnosticCode code, string detail, DerivedAssemblyReport? report)
        {
            Refusals++;
            LastRefusalCode = code == DiagnosticCode.None ? DiagnosticCode.StalePlan : code;
            LastRefusalDetail = detail ?? string.Empty;
            LastReport = report;
            return EditValidationResult.Refuse(LastRefusalCode, LastRefusalDetail);
        }

        private bool ContainsThis(ICompositionEditValidator? validator)
        {
            if (validator is CompositionEditValidatorSet set)
            {
                IReadOnlyList<ICompositionEditValidator> members = set.Validators;
                for (int i = 0; i < members.Count; i++)
                {
                    if (ReferenceEquals(members[i], this))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The preflight a lane consults: the validator itself, or the first preflight member of a validator set.
        /// </summary>
        public static DerivedAssemblyPreflightValidator? Of(ICompositionEditValidator? validator)
        {
            if (validator is DerivedAssemblyPreflightValidator preflight)
            {
                return preflight;
            }

            return validator is CompositionEditValidatorSet set ? set.Find<DerivedAssemblyPreflightValidator>() : null;
        }
    }
}
