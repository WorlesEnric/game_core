// GameCore.Unity.Adapters.Input — one world's adapter frame: the single object the application pump calls at the two
// points the pump algorithm already names (GC-019).
//
// Normative sources: 04 s3's pump algorithm — "collect adapter input and completed host callbacks; allow one ready
// assembly publication at a protocol boundary; obtain the number of logical steps from the world's temporal driver;
// for each admitted step ...; update presentation from the last published snapshot" — plus 04 s3's "Presentation runs
// on host frames even when a command-driven world is idle; an idle world does not synthesize simulation steps or
// increment `LogicalStepId`", and 04 s7's "Headless compositions omit presentation services and use fake
// asset/input adapters; gameplay cannot depend on an audio device or a renderer to progress."
//
// The frame installs no PlayerLoop node and no MonoBehaviour callback: it is *called by* the existing pump, once
// before the host's own pump (input, asset completions) and once after it (presentation). An idle world therefore
// still ingests input and still presents, and neither call can advance a step.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Views;
using GameCore.Unity.Runtime;

namespace GameCore.Unity.Adapters.Input
{
    /// <summary>
    /// What one world's adapter frame owns: a stamped input ingress, a bounded asset lease table and a committed
    /// output presenter. Every part is optional, because a headless world has no view binder and no device source and
    /// must keep running (04 s7).
    /// </summary>
    public sealed class WorldAdapterFrame : IAdapterFrame
    {
        private readonly UnityWorldHost host;
        private readonly TypedInputIngress ingress;
        private readonly PendingInputCompletionTable? pending;
        private readonly AssetLeaseTable? assets;
        private readonly CommittedOutputPresenter? presenter;
        private readonly IDeviceInputSource? device;
        private readonly InputBindingTable bindings;

        private ulong deviceSequence;
        private int presentPasses;

        public WorldAdapterFrame(
            UnityWorldHost host,
            TypedInputIngress ingress,
            InputBindingTable bindings,
            IDeviceInputSource? device = null,
            PendingInputCompletionTable? pending = null,
            AssetLeaseTable? assets = null,
            CommittedOutputPresenter? presenter = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            this.device = device;
            this.pending = pending;
            this.assets = assets;
            this.presenter = presenter;
            if (!ingress.World.Equals(host.World))
            {
                throw new ArgumentException(
                    "An adapter frame stamps commands for exactly the world it drives (P-004).",
                    nameof(ingress));
            }
        }

        public WorldId World => host.World;

        public TypedInputIngress Ingress => ingress;

        public AssetLeaseTable? Assets => assets;

        public CommittedOutputPresenter? Presenter => presenter;

        /// <summary>Idle/present passes made since creation; the pump's frame count is the authoritative clock.</summary>
        public int PresentPassCount => presentPasses;

        /// <summary>Device samples that no binding matched; counted so an unbound key is visible, not silent.</summary>
        public int UnboundSampleCount { get; private set; }

        public int DeviceSampleCount { get; private set; }

        /// <summary>Asset loads whose completions arrived during an input pass.</summary>
        public int CompletedAssetCount { get; private set; }

        /// <summary>
        /// Collects input and finishes adapter work that belongs before the host's step admission. Input is stamped
        /// with the world, the source and a strictly increasing source sequence, so the same press can never become
        /// two commands (P-037, P-050).
        /// </summary>
        public AdapterFrameReport CollectInput()
        {
            int items = 0;
            var detail = new List<string>();

            if (device != null)
            {
                IReadOnlyList<DeviceInputSample> samples = device.Sample();
                for (int i = 0; i < samples.Count; i++)
                {
                    DeviceInputSample sample = samples[i];
                    DeviceSampleCount++;
                    if (!bindings.TryFind(sample.Kind, sample.Code, out InputCommandBinding? binding) || binding == null)
                    {
                        UnboundSampleCount++;
                        continue;
                    }

                    deviceSequence++;
                    var stamp = new InputSourceStamp(
                        host.World,
                        device.SourceId,
                        deviceSequence,
                        host.CurrentStep,
                        host.CurrentEpoch);
                    InputAdmissionResult admission = ingress.Submit(binding.Bind(stamp, sample));
                    if (admission.Accepted)
                    {
                        items++;
                    }
                    else
                    {
                        detail.Add(admission.ToString());
                    }
                }
            }

            if (assets != null)
            {
                int completed = assets.PumpCompletions();
                CompletedAssetCount += completed;
                items += completed;
            }

            return AdapterFrameReport.Completed(
                "input",
                items,
                views: 0,
                detail: "deviceSamples=" + DeviceSampleCount.ToString(CultureInfo.InvariantCulture)
                    + "; admitted=" + items.ToString(CultureInfo.InvariantCulture)
                    + "; unbound=" + UnboundSampleCount.ToString(CultureInfo.InvariantCulture)
                    + (pending == null ? string.Empty : "; pendingCompletions="
                        + pending.PendingCount.ToString(CultureInfo.InvariantCulture))
                    + (detail.Count == 0 ? string.Empty : "; refused=" + string.Join(" | ", detail.ToArray())));
        }

        /// <summary>
        /// Presents the last committed image. It runs after the host's own pump, reads only committed output and can
        /// neither advance a step nor admit a command (P-045). A world with no presenter is headless and reports so.
        /// </summary>
        public AdapterFrameReport Present()
        {
            presentPasses++;
            if (presenter == null)
            {
                return AdapterFrameReport.Completed(
                    "presentation",
                    items: 0,
                    views: 0,
                    detail: "this world has no view binder, so it is headless and presentation is a no-op (04 s7)");
            }

            PresentationReport report = presenter.Present();
            return AdapterFrameReport.Completed(
                "presentation",
                items: report.Applied,
                views: report.ViewsAfter,
                detail: report.ToString());
        }

        /// <summary>Registers one delayed input completion, so a completion can be validated at completion (P-047).</summary>
        public bool TryRegisterPending(AsyncWorkToken token, out DiagnosticCode code, out string detail) =>
            pending != null
                ? pending.TryRegister(token, out code, out detail)
                : Fail(out code, out detail);

        /// <summary>Completes one delayed input request against the world's real callback gate (P-007, P-047).</summary>
        public InputCompletionResult CompletePending(AsyncWorkToken token, SampledInputCommand command)
        {
            if (pending == null)
            {
                return new InputCompletionResult(
                    InputCompletionOutcome.UnknownRequest,
                    CallbackGateDecision.DiscardRetiredRoute,
                    DiagnosticCode.MissingDependency,
                    "this frame has no pending input table, so the completion cannot be validated (P-047)",
                    null);
            }

            return pending.Complete(token, command);
        }

        /// <summary>
        /// Retires every adapter lease and view of this frame. It is the adapter half of teardown: the world's own
        /// P-048 pass owns gameplay resources, and this releases only what the adapters acquired (P-048, TEST-015).
        /// </summary>
        public AdapterTeardownReport Retire()
        {
            AssetReleaseReport? assetsReport = assets?.Retire();
            int viewsDestroyed = presenter?.DestroyAllViews() ?? 0;
            pending?.Clear();
            return new AdapterTeardownReport(
                assetsReport,
                viewsDestroyed,
                "adapter leases retired and views destroyed; gameplay state is untouched by this call (P-024)");
        }

        private static bool Fail(out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.MissingDependency;
            detail = "this frame has no pending input table (P-047)";
            return false;
        }
    }

    /// <summary>What one adapter teardown did: asset leases retired, views destroyed, and nothing about gameplay.</summary>
    public sealed class AdapterTeardownReport
    {
        public AdapterTeardownReport(AssetReleaseReport? assets, int viewsDestroyed, string detail)
        {
            Assets = assets;
            ViewsDestroyed = viewsDestroyed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Asset lease report, or null when the frame owned no asset table.</summary>
        public AssetReleaseReport? Assets { get; }

        public int ViewsDestroyed { get; }

        public string Detail { get; }

        public override string ToString() =>
            "views=" + ViewsDestroyed.ToString(CultureInfo.InvariantCulture)
            + (Assets == null ? string.Empty : "; assets=" + Assets.ToString());
    }

    /// <summary>
    /// The device half of input sampling. A Unity implementation reads the engine's own input; a headless or test
    /// implementation supplies queued samples. Either way the source is a producer of raw readings, never of
    /// commands: stamping and admission stay with the ingress (04 s7).
    /// </summary>
    public interface IDeviceInputSource
    {
        /// <summary>Stable identity of this sampling source; it owns one sequence namespace (P-050).</summary>
        Id128 SourceId { get; }

        /// <summary>Samples the devices once, in a deterministic order; an empty list is the idle answer.</summary>
        IReadOnlyList<DeviceInputSample> Sample();
    }
}
