// GameCore.Studio.Model - typed change sets (docs/studio/03-authoring-contracts.md s6, 02 s5, SADR-008/009).
// A change set is Unity-free data: the one apply path used by inspectors, gizmos, graph views and agents. It is
// immutable; lifecycle progress (state, outcomes, links, timestamps) produces a new instance through With* copies.
#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>One authoring edit request and its journal record (03 s6). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ChangeSet
    {
        /// <summary>Schema identifier of this document shape.</summary>
        public const string SchemaId = "gamecore.studio.changeset/1";

        [JsonConstructor]
        public ChangeSet(
            string id,
            string schema,
            Intent intent,
            IReadOnlyList<Operation> operations,
            SelectionSnapshot? selection = null,
            IReadOnlyList<BaseVersion>? baseVersions = null,
            IReadOnlyList<ArtifactRef>? artifacts = null,
            IReadOnlyList<ValidationScenario>? validation = null,
            Requirements? requirements = null,
            Links? links = null,
            ChangeSetState? state = null,
            IReadOnlyList<OperationOutcome>? outcomes = null,
            ApplyPolicy? policy = null,
            Timestamps? timestamps = null)
        {
            Id = ModelLists.NotEmpty(id, nameof(id));
            Schema = ModelLists.NotEmpty(schema, nameof(schema));
            Intent = ModelLists.NotNull(intent, nameof(intent));
            Operations = ModelLists.Required(operations, nameof(operations));
            Selection = selection;
            BaseVersions = ModelLists.Optional(baseVersions, nameof(baseVersions));
            Artifacts = ModelLists.Optional(artifacts, nameof(artifacts));
            Validation = ModelLists.Optional(validation, nameof(validation));
            Requirements = requirements;
            Links = links;
            State = state;
            Outcomes = ModelLists.Optional(outcomes, nameof(outcomes));
            Policy = policy;
            Timestamps = timestamps;
        }

        /// <summary>Change-set id: <c>cs_</c> + ULID (see <see cref="IdDerivation.NewChangeSetId"/>); primary key of History.</summary>
        [JsonProperty("id", Required = Required.Always)]
        [SchemaHint(Pattern = StudioPatterns.ChangeSetId)]
        public string Id { get; }

        [JsonProperty("schema", Required = Required.Always)]
        [SchemaHint(Const = SchemaId)]
        public string Schema { get; }

        [JsonProperty("intent", Required = Required.Always)]
        public Intent Intent { get; }

        [JsonProperty("selection", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public SelectionSnapshot? Selection { get; }

        /// <summary>Read dependencies: the stamps the plan was made against.</summary>
        [JsonProperty("baseVersions", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<BaseVersion>? BaseVersions { get; }

        [JsonProperty("operations", Required = Required.Always)]
        public IReadOnlyList<Operation> Operations { get; }

        [JsonProperty("artifacts", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<ArtifactRef>? Artifacts { get; }

        [JsonProperty("validation", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<ValidationScenario>? Validation { get; }

        [JsonProperty("requirements", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public Requirements? Requirements { get; }

        /// <summary>Filled as they happen.</summary>
        [JsonProperty("links", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public Links? Links { get; }

        /// <summary>Lifecycle state; absent means <see cref="ChangeSetState.Requested"/>.</summary>
        [JsonProperty("state", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ChangeSetState? State { get; }

        [JsonProperty("outcomes", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<OperationOutcome>? Outcomes { get; }

        /// <summary>Partial-failure policy; absent means <see cref="ApplyPolicy.AllOrNothing"/>.</summary>
        [JsonProperty("policy", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ApplyPolicy? Policy { get; }

        [JsonProperty("timestamps", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public Timestamps? Timestamps { get; }

        public ChangeSetState EffectiveState => State ?? ChangeSetState.Requested;

        public ApplyPolicy EffectivePolicy => Policy ?? ApplyPolicy.AllOrNothing;

        /// <summary>The operation with <paramref name="opId"/>, or null.</summary>
        public Operation? FindOperation(string opId)
        {
            for (int i = 0; i < Operations.Count; i++)
            {
                if (string.Equals(Operations[i].OpId, opId, StringComparison.Ordinal))
                {
                    return Operations[i];
                }
            }

            return null;
        }

        public ChangeSet WithState(ChangeSetState state) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, Validation, Requirements, Links, state, Outcomes, Policy, Timestamps);

        public ChangeSet WithOutcomes(IReadOnlyList<OperationOutcome>? outcomes) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, Validation, Requirements, Links, State, outcomes, Policy, Timestamps);

        public ChangeSet WithLinks(Links? links) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, Validation, Requirements, links, State, Outcomes, Policy, Timestamps);

        public ChangeSet WithTimestamps(Timestamps? timestamps) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, Validation, Requirements, Links, State, Outcomes, Policy, timestamps);

        public ChangeSet WithValidation(IReadOnlyList<ValidationScenario>? validation) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, validation, Requirements, Links, State, Outcomes, Policy, Timestamps);

        public ChangeSet WithRequirements(Requirements? requirements) =>
            new ChangeSet(Id, Schema, Intent, Operations, Selection, BaseVersions, Artifacts, Validation, requirements, Links, State, Outcomes, Policy, Timestamps);
    }

    /// <summary>What the user (or agent) asked for. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Intent
    {
        [JsonConstructor]
        public Intent(string text, IntentOrigin origin, string? voiceTranscriptId = null)
        {
            Text = ModelLists.NotNull(text, nameof(text));
            Origin = origin;
            VoiceTranscriptId = voiceTranscriptId;
        }

        [JsonProperty("text", Required = Required.Always)]
        public string Text { get; }

        [JsonProperty("voiceTranscriptId", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? VoiceTranscriptId { get; }

        [JsonProperty("origin", Required = Required.Always)]
        public IntentOrigin Origin { get; }
    }

    /// <summary>A read dependency: the stamp of an authored thing the plan relied on. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class BaseVersion
    {
        [JsonConstructor]
        public BaseVersion(AuthoringRef @ref, string stamp)
        {
            Ref = ModelLists.NotNull(@ref, nameof(@ref));
            Stamp = ModelLists.NotEmpty(stamp, nameof(stamp));
        }

        [JsonProperty("ref", Required = Required.Always)]
        public AuthoringRef Ref { get; }

        [JsonProperty("stamp", Required = Required.Always)]
        [SchemaHint(Pattern = StudioPatterns.Stamp)]
        public string Stamp { get; }
    }

    /// <summary>One tool invocation inside a change set. Immutable; <see cref="Args"/> is a private copy, treat as read-only.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Operation
    {
        [JsonConstructor]
        public Operation(
            string opId,
            string tool,
            AuthoringRef? target = null,
            JObject? args = null,
            IReadOnlyList<string>? dependsOn = null,
            Preconditions? preconditions = null,
            RuntimeApply? applyRequirement = null)
        {
            OpId = ModelLists.NotEmpty(opId, nameof(opId));
            Tool = ModelLists.NotEmpty(tool, nameof(tool));
            Target = target;
            Args = ModelLists.CopyObject(args);
            DependsOn = ModelLists.Optional(dependsOn, nameof(dependsOn));
            Preconditions = preconditions;
            ApplyRequirement = applyRequirement;
        }

        [JsonProperty("opId", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string OpId { get; }

        [JsonProperty("tool", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Tool { get; }

        [JsonProperty("target", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public AuthoringRef? Target { get; }

        [JsonProperty("args", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public JObject? Args { get; }

        [JsonProperty("dependsOn", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? DependsOn { get; }

        /// <summary>Absent means <see cref="Model.Preconditions.Stamp"/>.</summary>
        [JsonProperty("preconditions", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public Preconditions? Preconditions { get; }

        /// <summary>Planner's declared runtime requirement; absent means the tool's own.</summary>
        [JsonProperty("applyRequirement", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public RuntimeApply? ApplyRequirement { get; }

        public Preconditions EffectivePreconditions => Preconditions ?? Model.Preconditions.Stamp;
    }

    /// <summary>A content-addressed artifact a change set carries (generated media, imports). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ArtifactRef
    {
        [JsonConstructor]
        public ArtifactRef(
            string sha256,
            string mediaType,
            long bytes,
            string? name = null,
            ArtifactProducer? producer = null,
            string? role = null,
            ArtifactImport? import = null)
        {
            Sha256 = ModelLists.NotEmpty(sha256, nameof(sha256));
            MediaType = ModelLists.NotEmpty(mediaType, nameof(mediaType));
            Bytes = bytes;
            Name = name;
            Producer = producer;
            Role = role;
            Import = import;
        }

        /// <summary>Bare lowercase hex digest; operations reference it as <see cref="Reference"/>.</summary>
        [JsonProperty("sha256", Required = Required.Always)]
        [SchemaHint(Pattern = StudioPatterns.Sha256Hex)]
        public string Sha256 { get; }

        [JsonProperty("name", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; }

        [JsonProperty("mediaType", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string MediaType { get; }

        [JsonProperty("bytes", Required = Required.Always)]
        [SchemaHint(Minimum = 0)]
        public long Bytes { get; }

        [JsonProperty("producer", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ArtifactProducer? Producer { get; }

        /// <summary>Role in the change (e.g. <c>voiceLine</c>).</summary>
        [JsonProperty("role", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Role { get; }

        [JsonProperty("import", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ArtifactImport? Import { get; }

        /// <summary>The form operations use to point at this artifact: <c>sha256:</c> + digest.</summary>
        public string Reference => ContentStamp.Prefix + Sha256;
    }

    /// <summary>Who produced an artifact (etos task and op, provider and model). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ArtifactProducer
    {
        [JsonConstructor]
        public ArtifactProducer(string? etosTask = null, string? op = null, string? provider = null, string? model = null)
        {
            EtosTask = etosTask;
            Op = op;
            Provider = provider;
            Model = model;
        }

        [JsonProperty("etosTask", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? EtosTask { get; }

        [JsonProperty("op", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Op { get; }

        [JsonProperty("provider", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Provider { get; }

        [JsonProperty("model", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Model { get; }
    }

    /// <summary>How an artifact is imported as a Unity asset (importer type plus settings). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ArtifactImport
    {
        [JsonConstructor]
        public ArtifactImport(string type, JObject? settings = null)
        {
            Type = ModelLists.NotEmpty(type, nameof(type));
            Settings = ModelLists.CopyObject(settings);
        }

        [JsonProperty("type", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Type { get; }

        [JsonProperty("settings", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public JObject? Settings { get; }
    }

    /// <summary>A validation scenario attached to a change set and its status. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ValidationScenario
    {
        [JsonConstructor]
        public ValidationScenario(string scenario, ScenarioStatus status, string? detail = null)
        {
            Scenario = ModelLists.NotEmpty(scenario, nameof(scenario));
            Status = status;
            Detail = detail;
        }

        [JsonProperty("scenario", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Scenario { get; }

        [JsonProperty("status", Required = Required.Always)]
        public ScenarioStatus Status { get; }

        [JsonProperty("detail", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Detail { get; }
    }

    /// <summary>The strongest runtime requirement of a change set and the follow-up work it implies. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Requirements
    {
        [JsonConstructor]
        public Requirements(RuntimeApply max, bool worldRebuild, bool compile, bool build)
        {
            Max = max;
            WorldRebuild = worldRebuild;
            Compile = compile;
            Build = build;
        }

        [JsonProperty("max", Required = Required.Always)]
        public RuntimeApply Max { get; }

        [JsonProperty("worldRebuild", Required = Required.Always)]
        public bool WorldRebuild { get; }

        [JsonProperty("compile", Required = Required.Always)]
        public bool Compile { get; }

        [JsonProperty("build", Required = Required.Always)]
        public bool Build { get; }

        /// <summary>The requirements implied by a set of per-operation runtime requirements.</summary>
        public static Requirements FromOperations(IEnumerable<RuntimeApply> perOperation)
        {
            if (perOperation == null)
            {
                throw new ArgumentNullException(nameof(perOperation));
            }

            RuntimeApply max = RuntimeApply.Live;
            bool rebuild = false;
            bool compile = false;
            bool build = false;
            foreach (RuntimeApply apply in perOperation)
            {
                if (apply > max)
                {
                    max = apply;
                }

                rebuild |= apply == RuntimeApply.Rebuild;
                compile |= apply == RuntimeApply.Compile;
                build |= apply == RuntimeApply.Build;
            }

            return new Requirements(max, rebuild, compile, build);
        }
    }

    /// <summary>Cross-system links filled as they happen (etos tasks, parent change set, GameCore operation ids). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Links
    {
        [JsonConstructor]
        public Links(IReadOnlyList<string>? etosTasks = null, string? parent = null, IReadOnlyList<string>? gameCoreOps = null)
        {
            EtosTasks = ModelLists.Optional(etosTasks, nameof(etosTasks));
            Parent = parent;
            GameCoreOps = ModelLists.Optional(gameCoreOps, nameof(gameCoreOps));
        }

        [JsonProperty("etosTasks", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? EtosTasks { get; }

        /// <summary>Parent change-set id (a retry or follow-up), or null.</summary>
        [JsonProperty("parent", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(Pattern = StudioPatterns.ChangeSetId)]
        public string? Parent { get; }

        [JsonProperty("gameCoreOps", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? GameCoreOps { get; }
    }

    /// <summary>Per-operation apply outcome, with the inverse used by journal undo (SADR-009). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class OperationOutcome
    {
        [JsonConstructor]
        public OperationOutcome(
            string opId,
            OutcomeStatus status,
            string? code = null,
            string? detail = null,
            IReadOnlyList<string>? gameCoreOps = null,
            OperationUndo? undo = null)
        {
            OpId = ModelLists.NotEmpty(opId, nameof(opId));
            Status = status;
            Code = code;
            Detail = detail;
            GameCoreOps = ModelLists.Optional(gameCoreOps, nameof(gameCoreOps));
            Undo = undo;
        }

        [JsonProperty("opId", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string OpId { get; }

        [JsonProperty("status", Required = Required.Always)]
        public OutcomeStatus Status { get; }

        /// <summary>Diagnostic code for Refused/Failed (a <see cref="DiagnosticCodes"/> value); empty or absent otherwise.</summary>
        [JsonProperty("code", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Code { get; }

        [JsonProperty("detail", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Detail { get; }

        [JsonProperty("gameCoreOps", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? GameCoreOps { get; }

        [JsonProperty("undo", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public OperationUndo? Undo { get; }
    }

    /// <summary>The inverse operation payload recorded at apply time. Immutable; treat <see cref="Inverse"/> as read-only.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class OperationUndo
    {
        [JsonConstructor]
        public OperationUndo(JObject? inverse = null)
        {
            Inverse = ModelLists.CopyObject(inverse);
        }

        [JsonProperty("inverse", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public JObject? Inverse { get; }
    }

    /// <summary>Lifecycle timestamps as ISO-8601 UTC text (kept as text so journals round-trip byte-for-byte). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Timestamps
    {
        [JsonConstructor]
        public Timestamps(string? requested = null, string? candidate = null, string? applied = null)
        {
            Requested = requested;
            Candidate = candidate;
            Applied = applied;
        }

        [JsonProperty("requested", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Requested { get; }

        [JsonProperty("candidate", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Candidate { get; }

        [JsonProperty("applied", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Applied { get; }
    }
}
