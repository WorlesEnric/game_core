// GameCore.Unity.Adapters.Fixtures — deterministic doubles for the GC-019 adapter seams.
//
// These are test doubles for ports the *production* code defines: the asset backend, the view binder, the device
// input source and the external-authority adapter. They are deterministic by construction (no clock, no randomness,
// no frame count), rule-checked rather than mocked, and Unity-free so the identical sources execute in the plain
// dotnet suite and in the Unity EditMode suite (repo convention: `Fixtures/Runtime` + its own asmdef).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;

namespace GameCore.Unity.Adapters.Fixtures
{
    /// <summary>
    /// Deterministic in-memory asset backend. A load completes only when the fixture says so, so "the token is
    /// validated at completion" is testable in both orders without waiting on a clock (P-007).
    /// </summary>
    public sealed class DeterministicAssetBackend : IAssetBackend
    {
        private readonly Dictionary<long, Load> loads = new Dictionary<long, Load>();
        private readonly List<long> order = new List<long>();

        private long nextHandle;
        private int payloadCounter;

        /// <summary>When true, every `TryBeginLoad` is refused as a value (P-029, P-049).</summary>
        public bool RefuseLoads { get; set; }

        /// <summary>When true, the next completed load reports failure instead of a payload (P-049).</summary>
        public bool FailNextCompletion { get; set; }

        /// <summary>When true, releasing a handle throws once; the lease must be quarantined (P-048).</summary>
        public bool FailNextRelease { get; set; }

        public int BeginCount { get; private set; }

        public int RefusedCount { get; private set; }

        public int PollCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int OutstandingLoadCount => loads.Count;

        public int FailureInjectionCount { get; private set; }

        public int ReleaseFaultCount { get; private set; }

        /// <summary>Handles whose release has run, in order; proves dispose-at-most-once (P-048).</summary>
        public List<long> ReleasedHandles { get; } = new List<long>();

        public bool TryBeginLoad(AssetLoadRequest request, out long handle, out DiagnosticCode code, out string detail)
        {
            handle = 0L;
            code = DiagnosticCode.None;
            detail = string.Empty;
            BeginCount++;
            if (RefuseLoads)
            {
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the deterministic backend was configured to refuse every load";
                return false;
            }

            nextHandle++;
            handle = nextHandle;
            loads.Add(handle, new Load(request.Resource));
            order.Add(handle);
            return true;
        }

        public AssetLoadPoll Poll(long handle)
        {
            PollCount++;
            if (!loads.TryGetValue(handle, out Load load))
            {
                return AssetLoadPoll.Failed(
                    DiagnosticCode.ResourceUnavailable,
                    "no such load handle; the load was released or never started (P-005)");
            }

            if (!load.Completed)
            {
                return AssetLoadPoll.Pending();
            }

            if (load.Failed)
            {
                return AssetLoadPoll.Failed(
                    DiagnosticCode.ResourceUnavailable,
                    "the deterministic backend was configured to fail this load");
            }

            return AssetLoadPoll.Ready(load.Payload!);
        }

        public void Release(long handle)
        {
            if (FailNextRelease)
            {
                FailNextRelease = false;
                ReleaseFaultCount++;
                throw new InvalidOperationException("the deterministic backend was configured to fail this release");
            }

            if (!loads.Remove(handle))
            {
                return;
            }

            order.Remove(handle);
            ReleaseCount++;
            ReleasedHandles.Add(handle);
        }

        /// <summary>Completes one pending load with a deterministic payload; false when the handle is unknown.</summary>
        public bool Complete(long handle)
        {
            if (!loads.TryGetValue(handle, out Load load))
            {
                return false;
            }

            payloadCounter++;
            var bytes = new byte[4];
            bytes[0] = (byte)(payloadCounter >> 24);
            bytes[1] = (byte)(payloadCounter >> 16);
            bytes[2] = (byte)(payloadCounter >> 8);
            bytes[3] = (byte)payloadCounter;
            loads[handle] = new Load(load.Resource, completed: true, failed: FailNextCompletion, payload: new FrozenPayload(bytes));
            FailNextCompletion = false;
            return true;
        }

        /// <summary>Marks one pending load as failed; its completion must expose nothing (P-049).</summary>
        public bool Fail(long handle)
        {
            if (!loads.TryGetValue(handle, out Load load))
            {
                return false;
            }

            loads[handle] = new Load(load.Resource, completed: true, failed: true, payload: null);
            FailureInjectionCount++;
            return true;
        }

        public long HandleAt(int index) => order[index];

        private readonly struct Load
        {
            public readonly ResourceKey Resource;
            public readonly bool Completed;
            public readonly bool Failed;
            public readonly FrozenPayload? Payload;

            public Load(ResourceKey resource, bool completed = false, bool failed = false, FrozenPayload? payload = null)
            {
                Resource = resource;
                Completed = completed;
                Failed = failed;
                Payload = payload;
            }
        }
    }

    /// <summary>
    /// Recording view binder. It creates no engine object: a handle is a counter, so the identical fixture run in
    /// plain dotnet and in Unity observes the same numbers (P-054: the handle is never an identity).
    /// </summary>
    public sealed class RecordingViewBinder : IViewBinder
    {
        private readonly Dictionary<long, Entry> live = new Dictionary<long, Entry>();
        private readonly List<long> order = new List<long>();

        private long nextHandle;

        public bool RefuseCreate { get; set; }

        public bool RefuseDestroy { get; set; }

        /// <summary>Parents set by `TrySetVisualParent`, as child-to-parent handle pairs, in call order.</summary>
        public List<ParentChange> ParentChanges { get; } = new List<ParentChange>();

        /// <summary>Applied values, in call order; the presentation side of the observation, never authority.</summary>
        public List<PresentationApplyData> Applies { get; } = new List<PresentationApplyData>();

        public int LiveViewCount => live.Count;

        public int ApplyCount { get; private set; }

        public int CreatedCount { get; private set; }

        public int DestroyedCount { get; private set; }

        public int RefusedCreateCount { get; private set; }

        public int RefusedDestroyCount { get; private set; }

        public bool TryCreate(ViewKey key, out long handle, out string detail)
        {
            handle = 0L;
            detail = string.Empty;
            if (RefuseCreate)
            {
                RefusedCreateCount++;
                detail = "the recording binder was configured to refuse creation";
                return false;
            }

            nextHandle++;
            handle = nextHandle;
            live.Add(handle, new Entry(key));
            order.Add(handle);
            CreatedCount++;
            return true;
        }

        public bool TryDestroy(long handle, out string detail)
        {
            detail = string.Empty;
            if (RefuseDestroy)
            {
                RefusedDestroyCount++;
                detail = "the recording binder was configured to refuse destruction";
                return false;
            }

            if (!live.Remove(handle))
            {
                return true;
            }

            order.Remove(handle);
            DestroyedCount++;
            return true;
        }

        public bool TrySetVisualParent(long handle, long parentHandle, out string detail)
        {
            detail = string.Empty;
            if (!live.ContainsKey(handle))
            {
                detail = "no live view for that handle (P-005)";
                return false;
            }

            ParentChanges.Add(new ParentChange(handle, parentHandle));
            return true;
        }

        public void Apply(long handle, PresentationApplyData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            ApplyCount++;
            Applies.Add(data);
        }

        /// <summary>Live handles in creation order; zero after teardown proves complete view cleanup (TEST-015).</summary>
        public IReadOnlyList<long> LiveHandles() => new List<long>(order);

        /// <summary>One recorded visual reparent (child handle, parent handle).</summary>
        public readonly struct ParentChange
        {
            public readonly long Child;
            public readonly long Parent;

            public ParentChange(long child, long parent)
            {
                Child = child;
                Parent = parent;
            }

            public override string ToString() =>
                Child.ToString(CultureInfo.InvariantCulture) + "->" + Parent.ToString(CultureInfo.InvariantCulture);
        }

        private readonly struct Entry
        {
            public readonly ViewKey Key;

            public Entry(ViewKey key)
            {
                Key = key;
            }
        }
    }

    /// <summary>A binder that refuses every view: the headless proof that a world runs with no presentation (04 s7).</summary>
    public sealed class HeadlessViewBinder : IViewBinder
    {
        public int Refusals { get; private set; }

        public int LiveViewCount => 0;

        public int ApplyCount => 0;

        public bool TryCreate(ViewKey key, out long handle, out string detail)
        {
            handle = 0L;
            Refusals++;
            detail = "this composition is headless: no presentation object exists (04 s7)";
            return false;
        }

        public bool TryDestroy(long handle, out string detail)
        {
            detail = string.Empty;
            return true;
        }

        public bool TrySetVisualParent(long handle, long parentHandle, out string detail)
        {
            detail = "this composition is headless";
            return false;
        }

        public void Apply(long handle, PresentationApplyData data) => _ = data;
    }

    /// <summary>Queued device samples: the same readings on every run, in the order they were queued (P-008).</summary>
    public sealed class QueuedDeviceInputSource : IDeviceInputSource
    {
        private readonly List<DeviceInputSample> queued = new List<DeviceInputSample>();
        private readonly List<DeviceInputSample> buffer = new List<DeviceInputSample>();

        public QueuedDeviceInputSource(Id128 sourceId)
        {
            if (sourceId.IsDefault)
            {
                throw new ArgumentException("A queued source needs a stable identity (P-004).", nameof(sourceId));
            }

            SourceId = sourceId;
        }

        public Id128 SourceId { get; }

        public int SampleCount { get; private set; }

        public QueuedDeviceInputSource Enqueue(DeviceInputKind kind, int code, int value)
        {
            queued.Add(new DeviceInputSample(kind == DeviceInputKind.Button ? InputDeviceKind.Button : InputDeviceKind.Axis, code, value));
            return this;
        }

        /// <summary>One pass yields the queued readings and clears them; an empty queue is the idle answer.</summary>
        public IReadOnlyList<DeviceInputSample> Sample()
        {
            SampleCount++;
            buffer.Clear();
            for (int i = 0; i < queued.Count; i++)
            {
                buffer.Add(queued[i]);
            }

            queued.Clear();
            return buffer;
        }
    }

    /// <summary>Kind reported when queueing a sample; a fixture-side vocabulary, not a second production enum.</summary>
    public enum DeviceInputKind
    {
        Button = 0,
        Axis = 1,
    }

    /// <summary>
    /// External-authority double. It samples the values it was told to sample and records the intents it accepted,
    /// so a fixture can show that gameplay submitted an intent rather than writing pose (04 s7).
    /// </summary>
    public sealed class RecordingExternalAuthorityAdapter : IExternalAuthorityAdapter
    {
        private readonly Dictionary<Id128, FrozenPayload> valuesByTarget = new Dictionary<Id128, FrozenPayload>();
        private readonly List<AuthorityIntent> intents = new List<AuthorityIntent>();

        public RecordingExternalAuthorityAdapter(Id128 domain, OwnerId externalOwner)
        {
            if (domain.IsDefault)
            {
                throw new ArgumentException("An external adapter owns a declared domain (P-034).", nameof(domain));
            }

            Domain = domain;
            ExternalOwner = externalOwner;
        }

        public Id128 Domain { get; }

        public OwnerId ExternalOwner { get; }

        public bool IsAvailable { get; set; } = true;

        /// <summary>When false, `TrySample` reports unavailable instead of a value (P-049).</summary>
        public bool CanSample { get; set; } = true;

        public int SampleCount { get; private set; }

        public int IntentCount => intents.Count;

        public IReadOnlyList<AuthorityIntent> Intents => intents;

        /// <summary>Which authority the adapter stamps on its observations; default is its own owner (P-034).</summary>
        public OwnerId ReportedAuthority { get; set; }

        public void DeclareValue(TargetId target, FrozenPayload values) => valuesByTarget[target.Value] = values;

        public bool TrySample(TargetId target, SnapshotToken token, out EngineObservation observation, out DiagnosticCode code)
        {
            SampleCount++;
            code = DiagnosticCode.None;
            if (!CanSample)
            {
                observation = default(EngineObservation);
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            FrozenPayload payload = valuesByTarget.TryGetValue(target.Value, out FrozenPayload found)
                ? found
                : new FrozenPayload(CommandPayloadCodec.Int32(0));
            OwnerId authority = ReportedAuthority.IsDefault ? ExternalOwner : ReportedAuthority;
            observation = new EngineObservation(
                target,
                Domain,
                authority,
                token,
                new SchemaRef(new SchemaId(StableKey("gc019.observation.schema")), 1U),
                payload,
                (uint)SampleCount);
            return true;
        }

        public AuthorityIntentOutcome SubmitIntent(AuthorityIntent intent)
        {
            if (intent == null)
            {
                throw new ArgumentNullException(nameof(intent));
            }

            if (!IsAvailable)
            {
                return AuthorityIntentOutcome.RefusedUnavailable;
            }

            intents.Add(intent);
            return AuthorityIntentOutcome.Accepted;
        }

        private static Id128 StableKey(string name)
        {
            // A stable, deterministic identity for a fixture schema: FNV-1a over the ordinal bytes (P-008).
            ulong hash = 1469598103934665603UL;
            for (int i = 0; i < name.Length; i++)
            {
                unchecked
                {
                    hash ^= name[i];
                    hash *= 1099511628211UL;
                }
            }

            return new Id128(0x4743303139464958UL, hash == 0UL ? 1UL : hash);
        }
    }
}
