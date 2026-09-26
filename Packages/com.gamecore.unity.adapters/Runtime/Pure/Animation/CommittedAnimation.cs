// GameCore.Unity.Adapters.Animation — the optional animation output stage: committed pose snapshots drive
// presentation, and root-motion intent is consumed by the movement owner (GC-020).
//
// Normative sources: 04 s7's Animation row — "Optional marker/root-motion proposals copied into a typed input buffer |
// Animator state is presentation authority unless a gameplay plugin explicitly declares an external authority
// contract. Root-motion intent is consumed by the movement owner" — plus P-045 ("Observers see immutable images at
// (epoch, step) publication only") and 04 s7's "Presentation work never causes a second authoritative simulation
// update."
//
// The stage is engine-free: it reads a committed pose view and produces (a) one presentation frame per committed
// publication and (b) at most one root-motion proposal, which a gameplay package may consume as an input for its
// NEXT admitted step. It never writes gameplay state and never advances a step, so an animation failure cannot
// rewind or duplicate authority (P-031, 04 s7).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Unity.Adapters.Animation
{
    /// <summary>One target's committed pose, as presentation reads it from the published image (04 s7).</summary>
    public readonly struct CommittedPose
    {
        /// <summary>The stable target this pose belongs to.</summary>
        public readonly TargetId Target;

        /// <summary>Position in millimetres (this build's declared presentation unit).</summary>
        public readonly int PositionX;

        /// <summary>Position Y in millimetres.</summary>
        public readonly int PositionY;

        /// <summary>Position Z in millimetres.</summary>
        public readonly int PositionZ;

        /// <summary>1 while the owner reports the body on the ground, so a locomotion state can be selected.</summary>
        public readonly byte Grounded;

        /// <summary>Builds one committed pose.</summary>
        public CommittedPose(TargetId target, int x, int y, int z, byte grounded)
        {
            Target = target;
            PositionX = x;
            PositionY = y;
            PositionZ = z;
            Grounded = grounded;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Target.ToString() + "(" + PositionX.ToString(CultureInfo.InvariantCulture)
            + "," + PositionY.ToString(CultureInfo.InvariantCulture)
            + "," + PositionZ.ToString(CultureInfo.InvariantCulture) + ")"
            + (Grounded != 0 ? "G" : "A");
    }

    /// <summary>
    /// The committed pose view one animation pass reads. An implementation projects the world's published committed
    /// output (a snapshot/lease); it must never expose writable ECS state (P-045).
    /// </summary>
    public interface ICommittedPoseSource
    {
        /// <summary>The token of the committed image this source currently holds.</summary>
        SnapshotToken Token { get; }

        /// <summary>Poses of one committed image, in canonical target order.</summary>
        IReadOnlyList<CommittedPose> Poses { get; }
    }

    /// <summary>
    /// The presentation sink an animation pass drives. Implementations are engine adapters; a recording
    /// implementation exists so the stage's rules are testable with no Animator, no clip and no player (04 s7).
    /// </summary>
    public interface IAnimationPresentationSink
    {
        /// <summary>Stable identity of this sink, for diagnostics.</summary>
        string SinkName { get; }

        /// <summary>False when the sink cannot present; a pass then reports and applies nothing.</summary>
        bool IsAvailable { get; }

        /// <summary>Applies one committed presentation frame. Never throws; a refusal is a value (04 s7).</summary>
        bool TryPresent(SnapshotToken token, IReadOnlyList<CommittedPose> poses, out string detail);
    }

    /// <summary>One root-motion proposal: a movement intent an animation produced for a LATER admitted step.</summary>
    public readonly struct RootMotionProposal
    {
        /// <summary>The target the proposal is about.</summary>
        public readonly TargetId Target;

        /// <summary>Horizontal displacement the animation proposes, in millimetres.</summary>
        public readonly int DeltaX;

        /// <summary>Vertical displacement the animation proposes, in millimetres.</summary>
        public readonly int DeltaY;

        /// <summary>Forward displacement the animation proposes, in millimetres.</summary>
        public readonly int DeltaZ;

        /// <summary>The committed token the proposal was derived from; it is provenance, never authority (P-045).</summary>
        public readonly SnapshotToken Source;

        /// <summary>Builds one proposal.</summary>
        public RootMotionProposal(TargetId target, int dx, int dy, int dz, SnapshotToken source)
        {
            Target = target;
            DeltaX = dx;
            DeltaY = dy;
            DeltaZ = dz;
            Source = source;
        }

        /// <summary>True when the proposal names a target and asks for actual displacement.</summary>
        public bool IsEffective => !Target.IsDefault && (DeltaX != 0 || DeltaY != 0 || DeltaZ != 0);

        /// <inheritdoc />
        public override string ToString() =>
            Target.ToString() + "+(" + DeltaX.ToString(CultureInfo.InvariantCulture)
            + "," + DeltaY.ToString(CultureInfo.InvariantCulture)
            + "," + DeltaZ.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The committed-output animation stage: one presentation frame per committed publication and a bounded queue of
    /// root-motion proposals for the NEXT admitted step.
    ///
    /// It is deliberately not a simulation stage. It reads only the committed image at a strictly newer token,
    /// presents it, and offers at most <c>capacity</c> pending proposals to the movement owner. Its proposals never
    /// write pose: the movement owner decides whether to consume them in a later step (04 s7's "Root-motion intent is
    /// consumed by the movement owner").
    /// </summary>
    public sealed class CommittedAnimationStage
    {
        private readonly ICommittedPoseSource source;
        private readonly IAnimationPresentationSink sink;
        private readonly List<RootMotionProposal> pending = new List<RootMotionProposal>();

        private readonly int capacity;

        private SnapshotToken lastPresented;
        private byte hasPresented;

        /// <summary>Builds the stage over one committed pose source and one presentation sink.</summary>
        public CommittedAnimationStage(ICommittedPoseSource source, IAnimationPresentationSink sink, int capacity = 16)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary>Presentation passes that applied a frame, i.e. the sink accepted a strictly newer image.</summary>
        public int PresentedCount { get; private set; }

        /// <summary>Passes refused because the committed token was not newer than the presented one (P-045).</summary>
        public int StaleRefusalCount { get; private set; }

        /// <summary>Passes refused because the sink reported itself unavailable.</summary>
        public int UnavailableCount { get; private set; }

        /// <summary>Proposals this stage could not queue because the bounded next-step queue was full (P-043).</summary>
        public int ProposalOverflowCount { get; private set; }

        /// <summary>Proposals this stage queued, in order.</summary>
        public int ProposalCount { get; private set; }

        /// <summary>Pending proposals for a later admitted step, in queue order (canonical, never dictionary order).</summary>
        public IReadOnlyList<RootMotionProposal> PendingProposals => pending;

        /// <summary>The committed token this stage last presented.</summary>
        public SnapshotToken LastPresentedToken => lastPresented;

        /// <summary>
        /// Presents the committed image once. A token that is not strictly newer than the presented one is refused,
        /// so presenting twice from the same publication cannot replay a frame; an unavailable sink is reported
        /// without throwing, so presentation can never fail a host frame (04 s7).
        /// </summary>
        public bool TryPresentOnce(out string detail)
        {
            detail = string.Empty;
            SnapshotToken token = source.Token;
            if (hasPresented != 0 && !IsNewer(token, lastPresented))
            {
                StaleRefusalCount++;
                detail = "the committed token is not newer than the presented one, so nothing is presented (P-045)";
                return false;
            }

            if (!sink.IsAvailable)
            {
                UnavailableCount++;
                detail = "the animation presentation sink is unavailable (this is the ordinary headless case; 04 s7)";
                return false;
            }

            if (!sink.TryPresent(token, source.Poses, out detail))
            {
                UnavailableCount++;
                return false;
            }

            lastPresented = token;
            hasPresented = 1;
            PresentedCount++;
            return true;
        }

        /// <summary>
        /// Offers one root-motion proposal for a later step. A proposal beyond the declared capacity is refused and
        /// counted rather than silently dropped (P-043's "no silent drop for work that must be drained").
        /// </summary>
        public bool TryOfferRootMotion(RootMotionProposal proposal)
        {
            if (!proposal.IsEffective)
            {
                return false;
            }

            if (pending.Count >= capacity)
            {
                ProposalOverflowCount++;
                return false;
            }

            pending.Add(proposal);
            ProposalCount++;
            return true;
        }

        /// <summary>
        /// Takes the pending proposals for one admitted step, in queue order, and clears them. The movement owner
        /// calls this from its own stage and decides whether to consume them; nothing here writes pose (04 s7).
        /// </summary>
        public IReadOnlyList<RootMotionProposal> TakePendingForNextStep()
        {
            var taken = new List<RootMotionProposal>(pending);
            pending.Clear();
            return taken;
        }

        private static bool IsNewer(SnapshotToken candidate, SnapshotToken current)
        {
            if (!candidate.World.Session.Equals(current.World.Session))
            {
                return false;
            }

            int byStep = candidate.LogicalStepId.CompareTo(current.LogicalStepId);
            if (byStep != 0)
            {
                return byStep > 0;
            }

            return candidate.AssemblyEpoch.CompareTo(current.AssemblyEpoch) > 0;
        }
    }

    /// <summary>
    /// A pose source over a caller-supplied committed image: the recording implementation the engine-free suite and
    /// the headless probe use, so the stage's rules are provable without an Animator (04 s7, GC-020's audio/animation
    /// note).
    /// </summary>
    public sealed class RecordingPoseSource : ICommittedPoseSource
    {
        private readonly List<CommittedPose> poses = new List<CommittedPose>();

        /// <summary>Builds a source at one committed token.</summary>
        public RecordingPoseSource(SnapshotToken token)
        {
            Token = token;
        }

        /// <summary>The token of the committed image this source holds.</summary>
        public SnapshotToken Token { get; private set; }

        /// <summary>Poses of one committed image, in insertion order.</summary>
        public IReadOnlyList<CommittedPose> Poses => poses;

        /// <summary>Advances the source to a newer committed token with a fresh pose set.</summary>
        public void Publish(SnapshotToken token, IReadOnlyList<CommittedPose>? published)
        {
            Token = token;
            poses.Clear();
            if (published != null)
            {
                for (int i = 0; i < published.Count; i++)
                {
                    poses.Add(published[i]);
                }
            }
        }
    }

    /// <summary>A presentation sink that records frames instead of driving an Animator (04 s7's headless case).</summary>
    public sealed class RecordingAnimationSink : IAnimationPresentationSink
    {
        private readonly List<SnapshotToken> tokens = new List<SnapshotToken>();

        /// <summary>When true, the sink reports no animator (headless).</summary>
        public bool Unavailable { get; set; }

        /// <summary>When true, the next presentation is refused by the sink itself.</summary>
        public bool RejectNext { get; set; }

        /// <inheritdoc />
        public string SinkName => "recording-animation-sink";

        /// <inheritdoc />
        public bool IsAvailable => !Unavailable;

        /// <summary>Tokens presented, in order.</summary>
        public IReadOnlyList<SnapshotToken> PresentedTokens => tokens;

        /// <summary>Poses applied by the last presentation.</summary>
        public int LastPoseCount { get; private set; }

        /// <summary>Rejections this sink performed.</summary>
        public int RejectionCount { get; private set; }

        /// <inheritdoc />
        public bool TryPresent(SnapshotToken token, IReadOnlyList<CommittedPose> poses, out string detail)
        {
            detail = string.Empty;
            if (Unavailable)
            {
                detail = "this recording sink reports no animator";
                return false;
            }

            if (RejectNext)
            {
                RejectNext = false;
                RejectionCount++;
                detail = "this recording sink was configured to reject one presentation";
                return false;
            }

            tokens.Add(token);
            LastPoseCount = poses?.Count ?? 0;
            return true;
        }
    }
}
