// GameCore.Unity.Adapters.Contract.Tests - Edit Mode contract tests of the GC-019 adapter seams.
//
// These tests drive the frozen adapter API through its real Unity-side entry points with the shipped fixtures: the
// per-world adapter frame the application pump calls, the Unity device input source, the `Resources` asset backend,
// the GameObject/Transform binder and the committed-image projection/presentation path. Nothing here starts a
// second update path - the tests call exactly the methods the pump calls.
//
// Status: NotRun (pending orchestrator build host).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Fixtures;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Unity.Adapters.Tests.Contract
{
    /// <summary>
    /// TEST-019/TEST-002 contract tests. Every assertion reads a counter, a decision or a value the adapter itself
    /// reports: no wall clock, no randomness, no dependence on dictionary enumeration order.
    /// </summary>
    [TestFixture]
    public sealed class AdapterContractUnityTests
    {
        private const ulong SessionSalt = 0x4743303139414441UL;
        private const int UnboundDeviceCode = 999;
        private const int BoundDeviceCode = 65;
        private const string MissingAssetPath = "GameCoreTests/NoSuchAssetForGc019";

        private static readonly Id128 Issuer = new Id128(0x4953535545524755UL, 0x303139UL);
        private static readonly Id128 DeviceSourceId = new Id128(0x4445564943453031UL, 0x0000000000000001UL);
        private static readonly WorldId PresentWorld = new WorldId(new Id128(0x50524553454E5431UL, 0x0000000000000001UL));
        private static readonly WorldId AssetWorld = new WorldId(new Id128(0x4153534554474331UL, 0x0000000000000001UL));
        private static readonly WorldId AbsentWorld = new WorldId(new Id128(0x414253454E54574FU, 0x0000000000000001UL));
        private static readonly WorldId OtherAbsentWorld = new WorldId(new Id128(0x414253454E54574FU, 0x0000000000000002UL));

        private static ulong sessionSequence;

        private readonly List<UnityWorldHost> ownedHosts = new List<UnityWorldHost>();

        private bool pumpWasEnabled;

        private GameObject? container;

        [SetUp]
        public void SetUp()
        {
            pumpWasEnabled = GameCoreApplicationPump.IsEnabled;

            // The application pump is the one update path; tests drive the registers themselves so every count is
            // exact (04 s3).
            GameCoreApplicationPump.IsEnabled = false;
            AdapterFrameRegistry.Reset();
            ResourcePaths.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AdapterFrameRegistry.Reset();
            ResourcePaths.Reset();

            for (int i = ownedHosts.Count - 1; i >= 0; i--)
            {
                UnityWorldHost host = ownedHosts[i];
                host.Stop(new OperationId(host.World, Issuer, 99UL), "edit mode test teardown");
                host.Dispose();
            }

            ownedHosts.Clear();

            if (container != null)
            {
                UnityEngine.Object.DestroyImmediate(container);
                container = null;
            }

            UnityWorldRegistry.ResetAll();
            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }

        // ---------------------------------------------------------------- adapter frame registry

        [Test]
        [Timeout(60000)]
        public void ARegisteredFrameReceivesBothAdapterCallsForItsWorld()
        {
            UnityWorldHost host = CreateCommandWorld();
            var device = new QueuedDeviceInputSource(DeviceSourceId);
            var ingress = new TypedInputIngress(host.World, host);
            var frame = new WorldAdapterFrame(host, ingress, new InputBindingTable(), device);

            Assert.That(AdapterFrameRegistry.Register(frame), Is.False, "the first registration of a world is not a replacement");
            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.Worlds().Count, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.TryGet(host.World, out IAdapterFrame? registered), Is.True);
            Assert.That(registered, Is.SameAs(frame));

            // The pump's input point runs the frame before the host admits this frame's steps.
            AdapterFrameReport input = AdapterFrameRegistry.CollectInput(host.World);
            Assert.That(input.Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
            Assert.That(input.Ran, Is.True);
            Assert.That(input.Adapter, Is.EqualTo("input"));
            Assert.That(device.SampleCount, Is.EqualTo(1), "the frame sampled its device exactly once");
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.LastInputReport.Adapter, Is.EqualTo("input"));

            // The pump's presentation point runs the frame after the host pump.
            AdapterFrameReport presentation = AdapterFrameRegistry.Present(host.World);
            Assert.That(presentation.Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
            Assert.That(presentation.Adapter, Is.EqualTo("presentation"));
            Assert.That(presentation.Views, Is.EqualTo(0), "a frame with no presenter is headless and presents nothing");
            Assert.That(frame.PresentPassCount, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.PresentationCalls, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.LastPresentationReport.Adapter, Is.EqualTo("presentation"));
            Assert.That(AdapterFrameRegistry.FaultedCalls, Is.EqualTo(0));

            // A second frame for the same world is an explicit replacement, never a silent shadow (P-004).
            var replacement = new WorldAdapterFrame(host, new TypedInputIngress(host.World, host), new InputBindingTable());
            Assert.That(AdapterFrameRegistry.Register(replacement), Is.True);
            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.TryGet(host.World, out IAdapterFrame? current), Is.True);
            Assert.That(current, Is.SameAs(replacement));

            Assert.That(AdapterFrameRegistry.Unregister(host.World), Is.True);
            Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.Unregister(host.World), Is.False);
        }

        [Test]
        [Timeout(60000)]
        public void AWorldWithNoRegisteredFrameReportsSkippedAndAnotherWorldsFrameIsNotDriven()
        {
            var frame = new StubAdapterFrame(AbsentWorld);
            Assert.That(AdapterFrameRegistry.Register(frame), Is.False);

            AdapterFrameReport input = AdapterFrameRegistry.CollectInput(OtherAbsentWorld);
            Assert.That(input.Outcome, Is.EqualTo(AdapterFrameOutcome.Skipped));
            Assert.That(input.Ran, Is.False);
            Assert.That(input.Adapter, Is.Empty);
            Assert.That(input.Detail, Is.Not.Empty);
            Assert.That(frame.InputCalls, Is.EqualTo(0), "a frame is never driven for a world it does not belong to (P-004)");

            AdapterFrameReport presentation = AdapterFrameRegistry.Present(OtherAbsentWorld);
            Assert.That(presentation.Outcome, Is.EqualTo(AdapterFrameOutcome.Skipped));
            Assert.That(frame.PresentCalls, Is.EqualTo(0));
            Assert.That(AdapterFrameRegistry.SkippedCalls, Is.EqualTo(2));

            // The registered world still reaches its own frame.
            Assert.That(AdapterFrameRegistry.CollectInput(AbsentWorld).Items, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.Present(AbsentWorld).Views, Is.EqualTo(2));
            Assert.That(frame.InputCalls, Is.EqualTo(1));
            Assert.That(frame.PresentCalls, Is.EqualTo(1));
            Assert.That(AdapterFrameRegistry.InputCalls, Is.EqualTo(2));
            Assert.That(AdapterFrameRegistry.PresentationCalls, Is.EqualTo(2));
        }

        [Test]
        [Timeout(60000)]
        public void AFaultingFrameIsCountedInsteadOfAbortingTheFrameCall()
        {
            var frame = new FaultingAdapterFrame(AbsentWorld);
            Assert.That(AdapterFrameRegistry.Register(frame), Is.False);

            AdapterFrameReport input = AdapterFrameRegistry.CollectInput(AbsentWorld);
            Assert.That(input.Outcome, Is.EqualTo(AdapterFrameOutcome.Faulted));
            Assert.That(input.Ran, Is.False);
            Assert.That(input.Adapter, Is.EqualTo(nameof(FaultingAdapterFrame)));

            AdapterFrameReport presentation = AdapterFrameRegistry.Present(AbsentWorld);
            Assert.That(presentation.Outcome, Is.EqualTo(AdapterFrameOutcome.Faulted));
            Assert.That(AdapterFrameRegistry.FaultedCalls, Is.EqualTo(2), "an adapter defect cannot abort a host frame (P-031)");
            Assert.That(AdapterFrameRegistry.SkippedCalls, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- input ingress

        [Test]
        [Timeout(60000)]
        public void UnboundDeviceSamplesAreCountedAndAPlaneLessWorldRefusalIsReportedAsAValue()
        {
            UnityWorldHost host = CreateCommandWorld();
            Assert.That(host.Messages, Is.Null, "the fixture registration declares no message plane");

            var device = new QueuedDeviceInputSource(DeviceSourceId)
                .Enqueue(DeviceInputKind.Button, UnboundDeviceCode, 1)
                .Enqueue(DeviceInputKind.Button, BoundDeviceCode, 1);

            var bindings = new InputBindingTable().Add(new InputCommandBinding(
                InputDeviceKind.Button, BoundDeviceCode, Route(1UL), Target(1UL), Schema(1UL), null));
            var ingress = new TypedInputIngress(host.World, host);
            var frame = new WorldAdapterFrame(host, ingress, bindings, device);

            AdapterFrameReport report = frame.CollectInput();

            Assert.That(report.Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
            Assert.That(frame.DeviceSampleCount, Is.EqualTo(2));
            Assert.That(frame.UnboundSampleCount, Is.EqualTo(1), "an undeclared device code is counted, never bound");
            Assert.That(ingress.SourceCount, Is.EqualTo(1), "only the bound sample reaches the world's command port");
            Assert.That(ingress.RetainedAdmissionCount, Is.EqualTo(1), "the offered request keeps its recorded result for idempotent retry");
            Assert.That(ingress.AdmittedCount, Is.EqualTo(0));
            Assert.That(ingress.RefusedCount, Is.EqualTo(1), "the world refuses: it declares no command plane (P-042)");
            Assert.That(report.Items, Is.EqualTo(0));
            Assert.That(report.Detail, Does.Contain("refused="), "the refusal is reported in the frame's own report");
            Assert.That(report.Detail, Does.Contain("unbound=1"));
            Assert.That(host.PendingDemand, Is.EqualTo(0UL), "a refused command creates no step demand");

            // The world's own port answers with a value rather than throwing.
            CommandAdmissionReceipt receipt = host.Submit(new CommandEnvelope(
                new OperationId(host.World, Issuer, 77UL),
                Route(1UL),
                Target(1UL),
                Schema(1UL),
                null,
                new FrozenPayload(CommandPayloadCodec.Int32(1))));

            Assert.That(receipt.Admitted, Is.False);
            Assert.That(receipt.Result.Kind, Is.EqualTo(RequestResultKind.Rejected));
            Assert.That(receipt.Result.Reason, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(host.PendingDemand, Is.EqualTo(0UL));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
        }

        [Test]
        [Timeout(60000)]
        public void UnityDeviceInputSourceDeclaresItsKeysAndSuppressesSamplingOnDemand()
        {
            var declared = new List<KeyCode> { KeyCode.Z, KeyCode.A, KeyCode.Space };
            var source = new UnityDeviceInputSource(DeviceSourceId, declared);

            Assert.That(source.SourceId, Is.EqualTo(DeviceSourceId));
            Assert.That(source.DeclaredKeyCount, Is.EqualTo(3), "the declared list is content: an undeclared key is never sampled");
            Assert.That(source.SampleCount, Is.EqualTo(0));
            Assert.That(source.EdgeCount, Is.EqualTo(0));

            source.SuppressSampling = true;

            IReadOnlyList<DeviceInputSample> first = source.Sample();
            Assert.That(first.Count, Is.EqualTo(0), "a suppressed source reports no reading");
            Assert.That(source.SampleCount, Is.EqualTo(1), "a sampling pass is counted even when it yields nothing");
            Assert.That(source.EdgeCount, Is.EqualTo(0));

            Assert.That(source.Sample().Count, Is.EqualTo(0));
            Assert.That(source.SampleCount, Is.EqualTo(2));
            Assert.That(source.EdgeCount, Is.EqualTo(0), "a suppressed pass samples no key edge");

            // A source that declares nothing can never report a reading.
            var empty = new UnityDeviceInputSource(new Id128(0x4445564943453032UL, 0x0000000000000001UL), null);
            Assert.That(empty.DeclaredKeyCount, Is.EqualTo(0));
            empty.SuppressSampling = true;
            Assert.That(empty.Sample().Count, Is.EqualTo(0));
            Assert.That(empty.SampleCount, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- assets

        [Test]
        [Timeout(60000)]
        public void ResourcePathsAndUnityResourcesAssetBackendHandleAMissingAssetAsAValue()
        {
            var declared = new ResourceKey(Key(0x51UL));
            var undeclared = new ResourceKey(Key(0x52UL));

            ResourcePaths.Declare(declared, MissingAssetPath);
            Assert.That(ResourcePaths.Count, Is.EqualTo(1));
            Assert.That(ResourcePaths.TryGet(declared, out string? declaredPath), Is.True);
            Assert.That(declaredPath, Is.EqualTo(MissingAssetPath));
            Assert.That(ResourcePaths.TryGet(undeclared, out string? undeclaredPath), Is.False);
            Assert.That(undeclaredPath, Is.Null, "a miss is reported rather than guessed (P-015)");

            var backend = new UnityResourcesAssetBackend();
            Assert.That(backend.OutstandingLoadCount, Is.EqualTo(0));

            // A key with no declared path is refused as a value: no load is started for it and none is leaked.
            var undeclaredRequest = new AssetLoadRequest(undeclared, AssetToken(), new FrozenPayload(Array.Empty<byte>()));
            bool undeclaredBegan = backend.TryBeginLoad(
                undeclaredRequest,
                out long undeclaredHandle,
                out DiagnosticCode undeclaredCode,
                out string undeclaredDetail);

            Assert.That(undeclaredBegan, Is.False);
            Assert.That(undeclaredCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(undeclaredDetail, Is.Not.Empty);
            Assert.That(undeclaredHandle, Is.EqualTo(0L));
            Assert.That(backend.OutstandingLoadCount, Is.EqualTo(0));

            // A declared path this project does not ship is still a value: refused, or a load that never becomes
            // ready. A throw here fails the test, which is the point: the backend answers rather than faults.
            var request = new AssetLoadRequest(declared, AssetToken(), new FrozenPayload(Array.Empty<byte>()));

            bool began = backend.TryBeginLoad(request, out long handle, out DiagnosticCode code, out string detail);

            if (!began)
            {
                Assert.That(code, Is.Not.EqualTo(DiagnosticCode.None));
                Assert.That(detail, Is.Not.Empty);
                Assert.That(backend.OutstandingLoadCount, Is.EqualTo(0));
            }
            else
            {
                Assert.That(handle, Is.Not.EqualTo(0L));
                Assert.That(backend.OutstandingLoadCount, Is.EqualTo(1));

                AssetLoadPoll poll = backend.Poll(handle);
                for (int i = 0; i < 32 && poll.Status == AssetLoadStatus.Pending; i++)
                {
                    poll = backend.Poll(handle);
                }

                Assert.That(poll.Status, Is.Not.EqualTo(AssetLoadStatus.Ready),
                    "an asset the project does not ship can never report a ready payload");

                backend.Release(handle);
                Assert.That(backend.OutstandingLoadCount, Is.EqualTo(0), "Release drops the backend's own reference exactly once");
            }

            // A poll for a handle the backend never issued is a terminal failure, not an eternal pending answer.
            int pollsBefore = backend.PollCount;
            AssetLoadPoll unknown = backend.Poll(987654321L);
            Assert.That(unknown.Status, Is.EqualTo(AssetLoadStatus.Failed));
            Assert.That(unknown.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(unknown.Detail, Is.Not.Empty);
            Assert.That(backend.PollCount, Is.EqualTo(pollsBefore + 1));
        }

        // ---------------------------------------------------------------- views and presentation

        [Test]
        [Timeout(60000)]
        public void GameObjectViewBinderTracksLiveViewsAndReportsRefusalsAsValues()
        {
            container = new GameObject("gc019-view-container");
            var binder = new GameObjectViewBinder(container.transform);
            Assert.That(binder.Container, Is.EqualTo(container.transform));

            var key = new ViewKey(Target(0x60UL), 0U);
            var parentKey = new ViewKey(Target(0x61UL), 0U);
            var token = new SnapshotToken(PresentWorld, AssemblyEpoch.First, new LogicalStepId(7UL));

            bool createdOk = binder.TryCreate(key, out long handle, out string createdDetail);
            Assert.That(createdOk, Is.True, createdDetail);
            Assert.That(binder.CreatedCount, Is.EqualTo(1));
            Assert.That(binder.LiveObjects().Count, Is.EqualTo(1));

            // Apply writes presentation-side fields only: the object name records the committed token and the first
            // committed field scales it. No gameplay state is reachable from here (P-034).
            binder.Apply(handle, new PresentationApplyData(
                key, token, Scope(0x60UL), new[] { new PresentationField(Capability(0x70UL), 0U, 7) }));

            Assert.That(binder.ApplyCount, Is.EqualTo(1));
            GameObject applied = binder.LiveObjects()[0];
            Assert.That(applied.transform.localScale.x, Is.EqualTo(7f));
            Assert.That(applied.name, Does.Contain("-s7-"));
            Assert.That(applied.name, Does.Contain("-e1"));
            Assert.That(applied.transform.parent, Is.EqualTo(container.transform));

            // An apply for a handle the binder does not own is counted and creates nothing (P-024).
            binder.Apply(4242L, new PresentationApplyData(key, token, Scope(0x60UL), null));
            Assert.That(binder.ApplyCount, Is.EqualTo(2));
            Assert.That(binder.LiveViewCount, Is.EqualTo(1));

            // An explicit visual reparent is presentation only; it is not composition (P-010).
            bool parentCreated = binder.TryCreate(parentKey, out long parentHandle, out string parentCreateDetail);
            Assert.That(parentCreated, Is.True, parentCreateDetail);

            bool parented = binder.TrySetVisualParent(handle, parentHandle, out string parentDetail);
            Assert.That(parented, Is.True, parentDetail);
            Assert.That(applied.transform.parent, Is.EqualTo(binder.LiveObjects()[1].transform));
            Assert.That(binder.TrySetVisualParent(987654L, parentHandle, out string missingDetail), Is.False);
            Assert.That(missingDetail, Is.Not.Empty);
            Assert.That(binder.VisualParentChangeCount, Is.EqualTo(1), "a refused reparent changes nothing");

            // A refusal is a value the caller records, never a crash and never a phantom view.
            binder.RefuseCreate = true;
            Assert.That(binder.TryCreate(new ViewKey(Target(0x62UL), 0U), out long refusedHandle, out string refusedCreateDetail), Is.False);
            Assert.That(refusedHandle, Is.EqualTo(0L));
            Assert.That(refusedCreateDetail, Is.Not.Empty);
            Assert.That(binder.LiveViewCount, Is.EqualTo(2));
            Assert.That(binder.CreatedCount, Is.EqualTo(2));

            binder.RefuseDestroy = true;
            Assert.That(binder.TryDestroy(handle, out string refusedDestroyDetail), Is.False);
            Assert.That(refusedDestroyDetail, Is.Not.Empty);
            Assert.That(binder.LiveViewCount, Is.EqualTo(2));
            Assert.That(binder.DestroyedCount, Is.EqualTo(0));

            binder.RefuseDestroy = false;
            bool destroyedOk = binder.TryDestroy(handle, out string destroyDetail);
            Assert.That(destroyedOk, Is.True, destroyDetail);
            Assert.That(binder.LiveViewCount, Is.EqualTo(1));
            Assert.That(binder.LiveObjects().Count, Is.EqualTo(1));

            // A repeated destroy is not a second destroy (P-050).
            bool repeatedOk = binder.TryDestroy(handle, out string repeatDetail);
            Assert.That(repeatedOk, Is.True, repeatDetail);
            Assert.That(binder.DestroyedCount, Is.EqualTo(1));
        }

        [Test]
        [Timeout(60000)]
        public void LiveAssemblyImageBuilderProjectsRowsAndThePresenterRefusesUncommittedTargets()
        {
            TargetId bound = Target(0x80UL);
            TargetId withoutRows = Target(0x81UL);
            TargetId absent = Target(0x82UL);
            ScopeId scope = Scope(0x80UL);
            DefinitionRef recipe = new DefinitionRef(
                new DefinitionId(Key(0x83UL)), Schema(0x84UL), DefinitionRevision.First);

            ITargetScopeIndex index = new TargetScopeTable().With(bound, scope, recipe);

            SnapshotToken token1 = new SnapshotToken(PresentWorld, AssemblyEpoch.First, new LogicalStepId(3UL));
            SnapshotToken token2 = new SnapshotToken(PresentWorld, AssemblyEpoch.First, new LogicalStepId(4UL));
            SnapshotToken token3 = new SnapshotToken(PresentWorld, AssemblyEpoch.First, new LogicalStepId(5UL));

            CommittedAssemblyImage image1 = LiveAssemblyImageBuilder.Build(
                View(token1, TableWithValue(new[] { bound, withoutRows }, bound, 11)), index);
            CommittedAssemblyImage image2 = LiveAssemblyImageBuilder.Build(
                View(token2, TableWithValue(new[] { bound, withoutRows }, bound, 21)), index);
            CommittedAssemblyImage image3 = LiveAssemblyImageBuilder.Build(
                View(token3, TableWithValue(new[] { bound }, bound, 31)), index);

            Assert.That(image1.Token, Is.EqualTo(token1));
            Assert.That(image1.TargetCount, Is.EqualTo(2), "every declared target of the view is in the image");
            Assert.That(image1.FieldCount, Is.EqualTo(2));
            Assert.That(image1.TryGet(bound, out CommittedTargetEntry? entry), Is.True);
            Assert.That(entry!.Fields.Count, Is.EqualTo(2));
            Assert.That(entry.Fields[0].Capability, Is.EqualTo(Capability(0x90UL)));
            Assert.That(entry.Fields[0].OutputSlot, Is.EqualTo(0U));
            Assert.That(entry.Fields[0].Value, Is.EqualTo(11));
            Assert.That(entry.Fields[1].OutputSlot, Is.EqualTo(1U));
            Assert.That(entry.Fields[1].Value, Is.EqualTo(12));
            Assert.That(entry.CompositionParent, Is.EqualTo(scope));
            Assert.That(entry.Recipe, Is.EqualTo(recipe));
            Assert.That(image1.TryGet(withoutRows, out CommittedTargetEntry? emptyEntry), Is.True);
            Assert.That(emptyEntry!.Fields.Count, Is.EqualTo(0));
            Assert.That(image1.TryGet(absent, out CommittedTargetEntry? absentEntry), Is.False, "a target the view does not carry is not in the image");
            Assert.That(absentEntry, Is.Null);

            var registry = new ViewRegistry(PresentWorld, 4U);
            var source = new CommittedImageSource(PresentWorld);
            var binder = new RecordingViewBinder();
            var presenter = new CommittedOutputPresenter(registry, source, binder);

            Assert.That(source.IsAvailable, Is.False, "a source with no installed image is unavailable");
            Assert.That(source.Refresh(null), Is.False);
            Assert.That(source.Refresh(CommittedAssemblyImage.Empty), Is.False, "an image with no world incarnation is not this world's image");
            Assert.That(source.IsAvailable, Is.False);
            Assert.That(source.RefreshCount, Is.EqualTo(1), "only a non-null image counts as a refresh");

            Assert.That(source.Refresh(image1), Is.True);
            Assert.That(source.IsAvailable, Is.True);
            Assert.That(source.CommittedTargetCount, Is.EqualTo(2));
            Assert.That(source.TryGetCompositionParent(bound, out ScopeId parent), Is.True);
            Assert.That(parent, Is.EqualTo(scope));

            // Views exist for committed targets only; the image is the membership answer (P-024).
            Assert.That(presenter.CreateView(bound, 0U, out ViewRecord? createdBound), Is.EqualTo(ViewCreateOutcome.Created));
            Assert.That(createdBound, Is.Not.Null);
            Assert.That(presenter.CreateView(withoutRows, 0U, out ViewRecord? createdEmpty), Is.EqualTo(ViewCreateOutcome.Created));
            Assert.That(createdEmpty, Is.Not.Null);
            Assert.That(presenter.CreateView(absent, 0U, out ViewRecord? refused), Is.EqualTo(ViewCreateOutcome.Refused));
            Assert.That(refused, Is.Null, "a target the committed image does not carry gets no view");
            Assert.That(registry.UnassembledTargetRefusalCount, Is.EqualTo(1));
            Assert.That(registry.LiveViewCount, Is.EqualTo(2));

            // Both views exist, so the first pass examines both of them: one answer per live view (P-045).
            PresentationReport firstPass = presenter.Present();
            Assert.That(firstPass.Outcome, Is.EqualTo(PresentationOutcome.Presented));
            Assert.That(firstPass.Applied + firstPass.StaleRefused, Is.EqualTo(2), "every live view is examined exactly once per pass");

            // The next committed image reaches both views in canonical target order.
            Assert.That(source.Refresh(image2), Is.True);
            PresentationReport presented = presenter.Present();
            Assert.That(presented.Outcome, Is.EqualTo(PresentationOutcome.Presented));
            Assert.That(presented.Applied, Is.EqualTo(2));
            Assert.That(presented.StaleRefused, Is.EqualTo(0));
            Assert.That(presented.ViewsAfter, Is.EqualTo(2));
            Assert.That(binder.Applies.Count, Is.GreaterThanOrEqualTo(2));

            bool boundApplied = false;
            bool emptyTargetApplied = false;
            for (int applyIndex = 0; applyIndex < binder.Applies.Count; applyIndex++)
            {
                PresentationApplyData apply = binder.Applies[applyIndex];
                if (!apply.Token.Equals(token2))
                {
                    continue;
                }

                if (apply.Key.Target.Equals(bound))
                {
                    boundApplied = true;
                    Assert.That(apply.Fields.Count, Is.EqualTo(2), "both committed rows of the target became presentation fields");
                    Assert.That(apply.Fields[0].OutputSlot, Is.EqualTo(0U));
                    Assert.That(apply.Fields[0].Value, Is.EqualTo(21));
                    Assert.That(apply.Fields[1].Value, Is.EqualTo(22));
                    Assert.That(apply.CompositionParent, Is.EqualTo(scope));
                }
                else if (apply.Key.Target.Equals(withoutRows))
                {
                    emptyTargetApplied = true;
                    Assert.That(apply.Fields.Count, Is.EqualTo(0), "a target whose view carries no row is presented with no field");
                }
            }

            Assert.That(boundApplied, Is.True, "the committed image reached the target that carries rows");
            Assert.That(emptyTargetApplied, Is.True, "the committed image reached the declared target with no rows");

            // Presenting twice from one image does not re-apply it: presentation cannot advance anything (P-036).
            PresentationReport repeated = presenter.Present();
            Assert.That(repeated.Applied, Is.EqualTo(0));
            Assert.That(repeated.StaleRefused, Is.EqualTo(2));
            Assert.That(presenter.ObservedTokenCount, Is.EqualTo(2),
                "two committed images were observed across three calls: presenting one image twice is not a second observation");
            Assert.That(presenter.PresentCallCount, Is.EqualTo(3));

            // A target that leaves the committed image is no longer presented: its views are orphaned and destroyed.
            Assert.That(source.Refresh(image3), Is.True);
            PresentationReport orphaned = presenter.Present();
            Assert.That(orphaned.Applied, Is.EqualTo(1));
            Assert.That(orphaned.OrphanedDestroyed, Is.EqualTo(1));
            Assert.That(orphaned.ViewsAfter, Is.EqualTo(1));
            Assert.That(registry.LiveViewCount, Is.EqualTo(1));
            Assert.That(registry.OrphanedDestroyCount, Is.EqualTo(1));
            Assert.That(binder.LiveHandles().Count, Is.EqualTo(1));
            Assert.That(binder.DestroyedCount, Is.EqualTo(1));
            Assert.That(source.TryRead(withoutRows, out PresentationTarget? gone), Is.False, "a target the image does not carry cannot be projected");
            Assert.That(gone, Is.Null);

            // A stored target set and an empty live index report the same answer: nothing to project.
            var liveIndex = new LiveTargetScopeIndex(new LiveTargetIndex(new SpawnRecipeCatalog(null)));
            Assert.That(liveIndex.Targets.Count, Is.EqualTo(0));
            Assert.That(liveIndex.TryGetScope(bound, out ScopeId noScope), Is.False);
            Assert.That(noScope.IsDefault, Is.True);
            Assert.That(liveIndex.TryGetRecipe(bound, out DefinitionRef noRecipe), Is.False);
            Assert.That(noRecipe.Id.IsDefault, Is.True);

            CommittedAssemblyImage liveBuilt = LiveAssemblyImageBuilder.Build(
                View(token1, TableWithValue(new[] { bound }, bound, 11)), liveIndex);
            Assert.That(liveBuilt.TargetCount, Is.EqualTo(1));
            Assert.That(liveBuilt.TryGet(bound, out CommittedTargetEntry? liveEntry), Is.True);
            Assert.That(liveEntry!.Fields.Count, Is.EqualTo(2), "rows still map to presentation fields when the index holds no scope");
            Assert.That(liveEntry.CompositionParent.IsDefault, Is.True);
            Assert.That(liveEntry.Recipe.Id.IsDefault, Is.True);
        }

        // ---------------------------------------------------------------- helpers

        private static PublishedWorldView View(SnapshotToken token, TargetBindingTable bindings) =>
            new PublishedWorldView(
                token.AssemblyEpoch,
                CompositionRevision.First,
                token,
                null,
                bindings,
                null,
                CompiledSchedule.Empty,
                null,
                1);

        /// <summary>Two rows for one target (output slots 0 and 1) plus one declared target with no rows.</summary>
        private static TargetBindingTable TableWithValue(IReadOnlyList<TargetId> declaredTargets, TargetId target, int value)
        {
            var rows = new List<TargetBindingRow>
            {
                new TargetBindingRow(
                    target,
                    Capability(0x90UL),
                    1U,
                    0U,
                    value,
                    new ProviderInstallationId(Key(0x91UL)),
                    1UL,
                    0,
                    Schema(0x92UL)),
                new TargetBindingRow(
                    target,
                    Capability(0x90UL),
                    1U,
                    1U,
                    value + 1,
                    new ProviderInstallationId(Key(0x91UL)),
                    1UL,
                    1,
                    Schema(0x92UL)),
            };

            return new TargetBindingTable(declaredTargets, rows);
        }

        private UnityWorldHost CreateCommandWorld()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = new OperationId(world, Issuer, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: true),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            ownedHosts.Add(host!);
            return host!;
        }

        private static AsyncWorkToken AssetToken() =>
            new AsyncWorkToken(
                new OperationId(AssetWorld, Issuer, 5UL),
                new PluginInstanceId(Key(0x53UL)),
                InstallationGeneration.First,
                ActivationEpoch.Zero,
                1U);

        private static Id128 Key(ulong ordinal) => new Id128(0x47433031394B4559UL, ordinal);

        private static TargetId Target(ulong ordinal) => new TargetId(Key(ordinal));

        private static ScopeId Scope(ulong ordinal) => new ScopeId(Key(ordinal));

        private static CapabilityId Capability(ulong ordinal) => new CapabilityId(Key(ordinal));

        private static RouteId Route(ulong ordinal) => new RouteId(Key(ordinal));

        private static SchemaRef Schema(ulong ordinal) => new SchemaRef(new SchemaId(Key(ordinal)), 1U);

        /// <summary>A frame that records the calls the registry made, without touching an engine.</summary>
        private sealed class StubAdapterFrame : IAdapterFrame
        {
            public StubAdapterFrame(WorldId world)
            {
                World = world;
            }

            public WorldId World { get; }

            public int InputCalls { get; private set; }

            public int PresentCalls { get; private set; }

            public AdapterFrameReport CollectInput()
            {
                InputCalls++;
                return AdapterFrameReport.Completed("stub", 1, 0, "stub input");
            }

            public AdapterFrameReport Present()
            {
                PresentCalls++;
                return AdapterFrameReport.Completed("stub", 0, 2, "stub presentation");
            }
        }

        /// <summary>A defective frame: the registry must contain the failure, not propagate it (P-031).</summary>
        private sealed class FaultingAdapterFrame : IAdapterFrame
        {
            public FaultingAdapterFrame(WorldId world)
            {
                World = world;
            }

            public WorldId World { get; }

            public AdapterFrameReport CollectInput() =>
                throw new InvalidOperationException("adapter defect in the input point");

            public AdapterFrameReport Present() =>
                throw new InvalidOperationException("adapter defect in the presentation point");
        }
    }
}
