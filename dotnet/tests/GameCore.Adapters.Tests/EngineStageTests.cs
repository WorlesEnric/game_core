// GC-020's three engine stages, proven in the plain dotnet suite: the physics-authority gate (one simulation per
// admitted step, whatever the presentation rate), the committed-output audio stage (committed events only, playback
// exactly once, delivery at-least-once, a disabled device is a retryable value) and the committed-output animation
// stage (one frame per strictly newer committed token, bounded root-motion proposals for a later step).
//
// Normative sources: 00 P-034/P-041/P-045, 04 s7's Physics/Audio/Animation rows, REF-A05/REF-A06. Everything here is
// engine-free: a recording backend/sink/pose source stands in for the Unity half, and every double is local to this
// file because the properties under test are the stages' own decisions, not the engine binding's.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Animation;
using GameCore.Unity.Adapters.Audio;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Adapters.Physics;
using NUnit.Framework;

namespace GameCore.Unity.Adapters.Tests
{
    /// <summary>
    /// The physics-authority gate: one engine simulation per admitted action step and never one per host frame
    /// (04 s7's Physics row, REF-A06), with the keyed body table's ownership rules as values, never exceptions.
    /// </summary>
    [TestFixture]
    public sealed class PhysicsAuthorityGateTests
    {
        private static readonly TargetId BodyTarget = new TargetId(new Id128(0x4743303230544152UL, 1UL));
        private static readonly TargetId OtherTarget = new TargetId(new Id128(0x4743303230544152UL, 2UL));
        private static readonly Id128 BodyDomain = new Id128(0x4743303230444F4DUL, 1UL);

        private static readonly PhysicsBodyKey BodyKey = new PhysicsBodyKey(BodyTarget, BodyDomain);
        private static readonly PhysicsBodyKey OtherKey = new PhysicsBodyKey(OtherTarget, BodyDomain);
        private static readonly FrozenPayload TeleportPayload = new FrozenPayload(new byte[] { 1, 2, 3, 4 });

        private const double FixedDeltaSeconds = 0.02d;

        /// <summary>
        /// A backend that counts explicit simulations and advances each declared body by a fixed millimetre step, so
        /// "exactly one simulation per admitted step" is an observable pose as well as an observable count.
        /// </summary>
        private sealed class CountingPhysicsSceneBackend : IPhysicsSceneBackend
        {
            private readonly List<PhysicsBodyKey> order = new List<PhysicsBodyKey>();
            private readonly Dictionary<PhysicsBodyKey, PhysicsPose> poses =
                new Dictionary<PhysicsBodyKey, PhysicsPose>();

            /// <summary>False models the no-scene case: every call must then refuse as a value.</summary>
            public bool IsAvailable { get; set; } = true;

            /// <summary>Explicit simulations this backend performed.</summary>
            public int SimulateCount { get; private set; }

            /// <summary>Intents this backend applied.</summary>
            public int AppliedIntentCount { get; private set; }

            /// <summary>The pose a teleport intent moves a body to.</summary>
            public PhysicsPose TeleportPose { get; set; } = PhysicsPose.Zero;

            /// <summary>Millimetres one simulation advances every body on Z.</summary>
            public int StepAdvanceMillimetres { get; set; } = 10;

            public string SceneName => "counting-physics-scene";

            public int BodyCount => order.Count;

            public bool AutomaticSimulationSuppressed => true;

            public bool TryAddBody(PhysicsBodyKey key, PhysicsPose initial, out DiagnosticCode code, out string detail)
            {
                if (poses.ContainsKey(key))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "this recording scene already holds the body";
                    return false;
                }

                order.Add(key);
                poses.Add(key, initial);
                code = DiagnosticCode.None;
                detail = string.Empty;
                return true;
            }

            public bool TryRemoveBody(PhysicsBodyKey key, out string detail)
            {
                if (!poses.Remove(key))
                {
                    detail = "this recording scene never held the body";
                    return false;
                }

                order.Remove(key);
                detail = string.Empty;
                return true;
            }

            public bool TryReadPose(PhysicsBodyKey key, out PhysicsPose pose) => poses.TryGetValue(key, out pose);

            public bool TryApplyIntent(
                PhysicsBodyKey key,
                AuthorityIntentKind kind,
                FrozenPayload payload,
                out string detail)
            {
                if (!poses.ContainsKey(key))
                {
                    detail = "this recording scene does not hold the body";
                    return false;
                }

                if (payload == null)
                {
                    detail = "an intent carries a payload (P-042)";
                    return false;
                }

                AppliedIntentCount++;
                poses[key] = TeleportPose;
                detail = string.Empty;
                return true;
            }

            public bool TrySimulate(double fixedDeltaSeconds, out string detail)
            {
                SimulateCount++;
                for (int i = 0; i < order.Count; i++)
                {
                    PhysicsPose current = poses[order[i]];
                    var advanced = new PhysicsVector3i(
                        current.Position.X,
                        current.Position.Y,
                        current.Position.Z + StepAdvanceMillimetres);
                    poses[order[i]] = new PhysicsPose(advanced, current.Velocity);
                }

                detail = string.Empty;
                return true;
            }

            /// <summary>The authoritative pose this recording scene currently holds.</summary>
            public PhysicsPose PoseOf(PhysicsBodyKey key) => poses[key];
        }

        /// <summary>One presentation-rate run: the scene, the gate and what the frames decided.</summary>
        private readonly struct AuthorityRun
        {
            public readonly CountingPhysicsSceneBackend Backend;
            public readonly PhysicsAuthorityGate Gate;
            public readonly int Admitted;
            public readonly int Refused;

            public AuthorityRun(CountingPhysicsSceneBackend backend, PhysicsAuthorityGate gate, int admitted, int refused)
            {
                Backend = backend;
                Gate = gate;
                Admitted = admitted;
                Refused = refused;
            }
        }

        /// <summary>
        /// Calls the gate once per presented frame for four admitted steps, at the given presentations per step, and
        /// reports what happened. The same admitted-step sequence is what the host would hand a 30, 60 or 144 Hz
        /// presentation loop (REF-A06).
        /// </summary>
        private static AuthorityRun RunOneFramePerPresentation(int presentationsPerStep)
        {
            var backend = new CountingPhysicsSceneBackend();
            var gate = new PhysicsAuthorityGate(backend);
            Assert.That(
                gate.TryDeclareBody(BodyKey, PhysicsPose.Zero, out DiagnosticCode code, out string detail),
                Is.EqualTo(PhysicsBodyOutcome.Applied),
                code + ": " + detail);

            int admitted = 0;
            int refused = 0;
            for (ulong step = 1UL; step <= 4UL; step++)
            {
                for (int presentation = 0; presentation < presentationsPerStep; presentation++)
                {
                    if (gate.TrySimulateExactlyOnce(step, FixedDeltaSeconds, out string refusal))
                    {
                        admitted++;
                    }
                    else
                    {
                        refused++;
                        Assert.That(refusal, Is.Not.Empty, "every refusal is a value with a reason (P-031).");
                    }
                }
            }

            return new AuthorityRun(backend, gate, admitted, refused);
        }

        /// <summary>
        /// The gate admits one simulation for an admitted step and refuses the repeat, so the scene's own
        /// <c>SimulateCount</c> equals the admitted step count rather than the call count (04 s7).
        /// </summary>
        [Test]
        public void ADuplicateAdmittedStepIsRefusedSoTheSceneSimulatesExactlyOncePerAdmittedStep()
        {
            var backend = new CountingPhysicsSceneBackend();
            var gate = new PhysicsAuthorityGate(backend);

            Assert.That(gate.TrySimulateExactlyOnce(1UL, FixedDeltaSeconds, out string first), Is.True, first);
            Assert.That(gate.TrySimulateExactlyOnce(1UL, FixedDeltaSeconds, out string duplicate), Is.False, duplicate);
            Assert.That(gate.TrySimulateExactlyOnce(2UL, FixedDeltaSeconds, out string second), Is.True, second);
            Assert.That(gate.TrySimulateExactlyOnce(3UL, FixedDeltaSeconds, out string third), Is.True, third);

            Assert.That(gate.DuplicateStepRefusalCount, Is.EqualTo(1));
            Assert.That(duplicate, Is.Not.Empty, "a repeated admitted step says why it was refused.");
            Assert.That(backend.SimulateCount, Is.EqualTo(3), "three admitted steps and four calls: one engine step each.");
            Assert.That(gate.SteppedStepCount, Is.EqualTo(3));
            Assert.That(gate.LastSimulatedStep, Is.EqualTo(3UL));
            Assert.That(gate.HasSimulatedStep(1UL), Is.False, "the newest admitted step is the one that was simulated.");
            Assert.That(gate.HasSimulatedStep(3UL), Is.True);
            Assert.That(gate.RegressingStepRefusalCount, Is.Zero);
        }

        /// <summary>
        /// A step older than the last simulated one is refused as a value, because going backwards means the caller
        /// is not following the world's own committed step sequence (P-036).
        /// </summary>
        [Test]
        public void ARegressingAdmittedStepIsRefusedAndNeverReachesTheScene()
        {
            var backend = new CountingPhysicsSceneBackend();
            var gate = new PhysicsAuthorityGate(backend);

            Assert.That(gate.TrySimulateExactlyOnce(5UL, FixedDeltaSeconds, out _), Is.True);
            Assert.That(gate.TrySimulateExactlyOnce(4UL, FixedDeltaSeconds, out string detail), Is.False, detail);

            Assert.That(gate.RegressingStepRefusalCount, Is.EqualTo(1));
            Assert.That(gate.DuplicateStepRefusalCount, Is.Zero);
            Assert.That(backend.SimulateCount, Is.EqualTo(1), "a regressing step never advances the scene.");
            Assert.That(gate.SteppedStepCount, Is.EqualTo(1));
            Assert.That(gate.LastSimulatedStep, Is.EqualTo(5UL), "the last simulated step is unchanged.");
            Assert.That(detail, Is.Not.Empty);
        }

        /// <summary>
        /// REF-A06: 30, 60 and 144 presentations of one admitted step still produce one engine step per admitted
        /// step and the same final pose, and every extra presented frame is a refusal. A degenerate step, a
        /// non-positive delta and an unavailable scene are refused as values, never thrown (P-031, P-036).
        /// </summary>
        [Test]
        public void PresentationRateNeverAdvancesAuthorityAndADegenerateOrUnavailableSimulationIsRefusedAsAValue()
        {
            AuthorityRun hz30 = RunOneFramePerPresentation(30);
            AuthorityRun hz60 = RunOneFramePerPresentation(60);
            AuthorityRun hz144 = RunOneFramePerPresentation(144);

            Assert.That(hz30.Gate.SteppedStepCount, Is.EqualTo(4));
            Assert.That(hz60.Gate.SteppedStepCount, Is.EqualTo(4));
            Assert.That(hz144.Gate.SteppedStepCount, Is.EqualTo(4));

            Assert.That(hz30.Backend.SimulateCount, Is.EqualTo(4), "30 presentations per step step the scene once per step.");
            Assert.That(hz60.Backend.SimulateCount, Is.EqualTo(4), "60 presentations per step step the scene once per step.");
            Assert.That(hz144.Backend.SimulateCount, Is.EqualTo(4), "144 presentations per step step it once per step.");

            PhysicsPose pose30 = hz30.Backend.PoseOf(BodyKey);
            PhysicsPose pose60 = hz60.Backend.PoseOf(BodyKey);
            PhysicsPose pose144 = hz144.Backend.PoseOf(BodyKey);
            Assert.That(pose60.Position.Z, Is.EqualTo(pose30.Position.Z), "the presentation rate never changes the pose.");
            Assert.That(pose144.Position.Z, Is.EqualTo(pose30.Position.Z));
            Assert.That(pose60.Position, Is.EqualTo(pose30.Position));
            Assert.That(pose144.Position, Is.EqualTo(pose30.Position), "the two higher rates reached the same position.");
            Assert.That(pose144.Velocity, Is.EqualTo(pose30.Velocity));
            Assert.That(pose144.Position.Z, Is.EqualTo(40), "four engine steps advanced the body four times and no more.");

            Assert.That(hz144.Admitted, Is.EqualTo(4));
            Assert.That(hz144.Refused, Is.EqualTo((4 * 144) - 4), "every extra presented frame was refused.");
            Assert.That(hz144.Gate.DuplicateStepRefusalCount, Is.EqualTo((4 * 144) - 4));
            Assert.That(hz144.Gate.RegressingStepRefusalCount, Is.Zero);
            Assert.That(hz144.Gate.RefusedSimulationCount, Is.Zero, "no refusal came from unavailability or a bad delta.");
            Assert.That(hz30.Refused, Is.EqualTo((4 * 30) - 4));

            var noScene = new CountingPhysicsSceneBackend();
            noScene.IsAvailable = false;
            var unavailableGate = new PhysicsAuthorityGate(noScene);

            Assert.That(noScene.IsAvailable, Is.False);
            Assert.That(unavailableGate.TrySimulateExactlyOnce(1UL, FixedDeltaSeconds, out string unavailable), Is.False, unavailable);
            Assert.That(unavailable, Is.Not.Empty);
            Assert.That(unavailableGate.RefusedSimulationCount, Is.EqualTo(1));
            Assert.That(noScene.SimulateCount, Is.Zero);

            Assert.That(unavailableGate.TrySimulateExactlyOnce(0UL, FixedDeltaSeconds, out string zeroStep), Is.False, zeroStep);
            Assert.That(unavailableGate.RefusedSimulationCount, Is.EqualTo(2));
            Assert.That(unavailableGate.TrySimulateExactlyOnce(1UL, 0d, out string zeroDelta), Is.False, zeroDelta);
            Assert.That(unavailableGate.RefusedSimulationCount, Is.EqualTo(3));
            Assert.That(unavailableGate.TrySimulateExactlyOnce(1UL, -FixedDeltaSeconds, out string negativeDelta), Is.False, negativeDelta);
            Assert.That(unavailableGate.RefusedSimulationCount, Is.EqualTo(4), "each degenerate request moved the refusal value.");
            Assert.That(unavailableGate.SteppedStepCount, Is.Zero);
            Assert.That(unavailableGate.LastSimulatedStep, Is.EqualTo(0UL));
            Assert.That(zeroStep, Is.Not.Empty);
            Assert.That(zeroDelta, Is.Not.Empty);
            Assert.That(negativeDelta, Is.Not.Empty);
        }

        /// <summary>
        /// The keyed body table owns each body once: a duplicate declaration and an unknown withdrawal are refused,
        /// a pose round-trips through the authoritative backend in both directions, a payload-less intent is refused
        /// as malformed, and an unavailable scene refuses declarations and simulations without throwing.
        /// </summary>
        [Test]
        public void TheBodyTableRefusesDuplicatesAndUnknownBodiesAndAnUnavailableSceneRefusesAsAValue()
        {
            var backend = new CountingPhysicsSceneBackend();
            var gate = new PhysicsAuthorityGate(backend);
            var initial = new PhysicsPose(new PhysicsVector3i(1, 2, 3), PhysicsVector3i.Zero);

            Assert.That(
                gate.TryDeclareBody(BodyKey, initial, out DiagnosticCode declaredCode, out string declaredDetail),
                Is.EqualTo(PhysicsBodyOutcome.Applied),
                declaredCode + ": " + declaredDetail);
            Assert.That(declaredCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(gate.Bodies.Count, Is.EqualTo(1));
            Assert.That(backend.BodyCount, Is.EqualTo(1));

            Assert.That(
                gate.TryDeclareBody(BodyKey, PhysicsPose.Zero, out DiagnosticCode duplicateCode, out string duplicateDetail),
                Is.EqualTo(PhysicsBodyOutcome.RefusedUnknownBody),
                duplicateDetail);
            Assert.That(duplicateCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(gate.Bodies.Count, Is.EqualTo(1), "the first declaration survives a refused duplicate (P-034).");
            Assert.That(backend.BodyCount, Is.EqualTo(1));
            Assert.That(gate.Bodies[0], Is.EqualTo(BodyKey));

            Assert.That(
                gate.TryDeclareBody(OtherKey, PhysicsPose.Zero, out _, out _),
                Is.EqualTo(PhysicsBodyOutcome.Applied),
                "a different target is a different body.");
            Assert.That(gate.Bodies.Count, Is.EqualTo(2));

            Assert.That(gate.TryWithdrawBody(OtherKey, out string withdrawn), Is.EqualTo(PhysicsBodyOutcome.Applied), withdrawn);
            Assert.That(gate.Bodies.Count, Is.EqualTo(1));
            Assert.That(backend.BodyCount, Is.EqualTo(1));
            Assert.That(
                gate.TryWithdrawBody(OtherKey, out string unknownWithdrawal),
                Is.EqualTo(PhysicsBodyOutcome.RefusedUnknownBody),
                unknownWithdrawal);
            Assert.That(unknownWithdrawal, Is.Not.Empty);

            Assert.That(gate.TryReadPose(BodyKey, out PhysicsPose declaredPose), Is.True);
            Assert.That(declaredPose.Position.X, Is.EqualTo(1));
            Assert.That(declaredPose.Position.Y, Is.EqualTo(2));
            Assert.That(declaredPose.Position.Z, Is.EqualTo(3));

            backend.TeleportPose = new PhysicsPose(new PhysicsVector3i(700, 800, 900), new PhysicsVector3i(0, 0, 50));
            Assert.That(
                gate.TryApplyIntent(BodyKey, AuthorityIntentKind.Teleport, TeleportPayload, out string applied),
                Is.EqualTo(PhysicsBodyOutcome.Applied),
                applied);
            Assert.That(backend.AppliedIntentCount, Is.EqualTo(1));
            Assert.That(gate.TryReadPose(BodyKey, out PhysicsPose teleported), Is.True);
            Assert.That(teleported.Position.X, Is.EqualTo(700), "the engine's pose round-trips out through the gate.");
            Assert.That(teleported.Position.Y, Is.EqualTo(800));
            Assert.That(teleported.Position.Z, Is.EqualTo(900));
            Assert.That(teleported.Velocity.Z, Is.EqualTo(50));

            Assert.That(
                gate.TryApplyIntent(BodyKey, AuthorityIntentKind.Impulse, null!, out string malformed),
                Is.EqualTo(PhysicsBodyOutcome.RefusedMalformedIntent),
                malformed);
            Assert.That(malformed, Is.Not.Empty);
            Assert.That(backend.AppliedIntentCount, Is.EqualTo(1), "a malformed intent never reaches the backend.");

            var noScene = new CountingPhysicsSceneBackend();
            noScene.IsAvailable = false;
            var unavailableGate = new PhysicsAuthorityGate(noScene);

            PhysicsBodyOutcome refusedDeclaration = PhysicsBodyOutcome.Applied;
            Assert.DoesNotThrow(() =>
            {
                refusedDeclaration = unavailableGate.TryDeclareBody(BodyKey, PhysicsPose.Zero, out DiagnosticCode c, out _);
            });
            Assert.That(refusedDeclaration, Is.EqualTo(PhysicsBodyOutcome.RefusedUnavailable));
            Assert.That(unavailableGate.Bodies, Is.Empty);
            Assert.That(noScene.BodyCount, Is.Zero);

            bool refusedSimulation = true;
            string refusalDetail = string.Empty;
            Assert.DoesNotThrow(() =>
            {
                refusedSimulation = unavailableGate.TrySimulateExactlyOnce(1UL, FixedDeltaSeconds, out refusalDetail);
            });
            Assert.That(refusedSimulation, Is.False, "an unavailable scene refuses instead of throwing (P-031).");
            Assert.That(refusalDetail, Is.Not.Empty);
            Assert.That(unavailableGate.RefusedSimulationCount, Is.EqualTo(1));
            Assert.That(noScene.SimulateCount, Is.Zero);
        }
    }

    /// <summary>
    /// The committed-output audio stage: committed events only, one playback per event identity, at-least-once
    /// delivery that keeps a refused event owed, and a cue table that binds one schema to one cue (P-045, 04 s7).
    /// </summary>
    [TestFixture]
    public sealed class CommittedAudioStageTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303230574F52UL, 1UL));
        private static readonly WorldId ForeignWorld = new WorldId(new Id128(0x4743303230574F52UL, 2UL));
        private static readonly SchemaId AudibleSchemaId = new SchemaId(new Id128(0x4743303230534348UL, 1UL));
        private static readonly SchemaId SilentSchemaId = new SchemaId(new Id128(0x4743303230534348UL, 2UL));
        private static readonly SchemaRef AudibleSchema = new SchemaRef(AudibleSchemaId, 1U);
        private static readonly SchemaRef SilentSchema = new SchemaRef(SilentSchemaId, 1U);
        private static readonly Id128 FootstepCue = new Id128(0x4743303230435545UL, 1UL);
        private static readonly Id128 OtherCue = new Id128(0x4743303230435545UL, 2UL);
        private static readonly FrozenPayload CuePayload = new FrozenPayload(new byte[] { 7, 7, 7 });

        /// <summary>
        /// A reader that returns one fixed page for every read, so a replayed page and a double read are the same
        /// observable case (P-045's at-least-once delivery).
        /// </summary>
        private sealed class FixedPageReader : ICommittedEventReader
        {
            private readonly CommittedEventPage page;

            public FixedPageReader(CommittedEventPage page)
            {
                this.page = page;
            }

            /// <summary>Reads this reader served.</summary>
            public int ReadCount { get; private set; }

            /// <summary>The cursor the stage asked from.</summary>
            public EventCursor LastCursor { get; private set; }

            /// <summary>The bound the stage asked for.</summary>
            public int LastMaxEvents { get; private set; }

            public CommittedEventPage Read(EventCursor cursor, int maxEvents)
            {
                ReadCount++;
                LastCursor = cursor;
                LastMaxEvents = maxEvents;
                return page;
            }
        }

        private static CommittedEvent EventOf(WorldId world, ulong sequence, SchemaRef schema) =>
            new CommittedEvent(
                new EventCursor(world, new EventSequence(sequence)),
                schema,
                new AssemblyEpoch(1UL),
                new LogicalStepId(1UL),
                default(OperationId),
                CuePayload);

        private static CommittedEventPage PageOf(params CommittedEvent[] events) =>
            new CommittedEventPage(
                CursorOutcome.Ok,
                events,
                new EventCursor(World, new EventSequence((ulong)events.Length)));

        private static AudioCueTable CueTableForAudibleSchemaOnly()
        {
            var cues = new AudioCueTable();
            Assert.That(cues.TryAdd(AudibleSchema, FootstepCue), Is.True);
            return cues;
        }

        private static CommittedAudioStage StageOver(CommittedEventPage page, IAudioOutputSink sink, out FixedPageReader reader)
        {
            reader = new FixedPageReader(page);
            return new CommittedAudioStage(World, reader, sink, CueTableForAudibleSchemaOnly(), 32);
        }

        /// <summary>
        /// Reading the same committed page twice plays each event exactly once: the second pass is entirely
        /// suppressed, the unbound schema is counted inaudible, and the stage's counters are the exact expected
        /// numbers (P-045's exactly-once playback over at-least-once delivery).
        /// </summary>
        [Test]
        public void AReplayedCommittedPagePlaysEachEventExactlyOnceAndTheSecondPassIsSuppressed()
        {
            CommittedEventPage page = PageOf(
                EventOf(World, 1UL, AudibleSchema),
                EventOf(World, 2UL, SilentSchema),
                EventOf(World, 3UL, AudibleSchema));
            var sink = new RecordingAudioSink();
            CommittedAudioStage stage = StageOver(page, sink, out FixedPageReader reader);

            int playedFirstPass = stage.PresentNextPage(8);

            Assert.That(playedFirstPass, Is.EqualTo(2));
            Assert.That(reader.ReadCount, Is.EqualTo(1));
            Assert.That(reader.LastMaxEvents, Is.EqualTo(8));
            Assert.That(
                reader.LastCursor,
                Is.EqualTo(new EventCursor(World, default(EventSequence))),
                "the first read starts at the world's own zero cursor, never a foreign or arbitrary one.");
            Assert.That(stage.ReadCount, Is.EqualTo(3));
            Assert.That(stage.AudibleCount, Is.EqualTo(2), "only the bound schema is audible.");
            Assert.That(stage.InaudibleCount, Is.EqualTo(1), "the cue-less schema is counted, not silently dropped.");
            Assert.That(stage.PlayedCount, Is.EqualTo(2));
            Assert.That(stage.SuppressedCount, Is.Zero);
            Assert.That(stage.RefusedCount, Is.Zero);
            Assert.That(sink.Plays.Count, Is.EqualTo(2));
            Assert.That(sink.Plays[0].Cue, Is.EqualTo(FootstepCue));
            Assert.That(sink.Plays[1].Cue, Is.EqualTo(FootstepCue));
            Assert.That(
                sink.Plays[0].EventKey,
                Is.EqualTo(CommittedAudioStage.EventKeyOf(EventOf(World, 1UL, AudibleSchema))));
            Assert.That(
                sink.Plays[1].EventKey,
                Is.EqualTo(CommittedAudioStage.EventKeyOf(EventOf(World, 3UL, AudibleSchema))));
            Assert.That(stage.PlayedEventKeys.Count, Is.EqualTo(2));
            Assert.That(stage.PlayedEventKeys[0], Is.EqualTo(sink.Plays[0].EventKey));
            Assert.That(stage.PlayedEventKeys[1], Is.EqualTo(sink.Plays[1].EventKey));

            int playedSecondPass = stage.PresentNextPage(8);

            Assert.That(playedSecondPass, Is.Zero, "at-least-once delivery never becomes a repeated cue (P-045).");
            Assert.That(reader.ReadCount, Is.EqualTo(2));
            Assert.That(stage.ReadCount, Is.EqualTo(6));
            Assert.That(stage.AudibleCount, Is.EqualTo(4), "a replayed event is still audible; it is suppressed, not inaudible.");
            Assert.That(stage.InaudibleCount, Is.EqualTo(2));
            Assert.That(stage.SuppressedCount, Is.EqualTo(2));
            Assert.That(stage.PlayedCount, Is.EqualTo(2));
            Assert.That(sink.Plays.Count, Is.EqualTo(2), "the sink saw no second submission.");
            Assert.That(stage.Cursor, Is.EqualTo(page.NextCursor), "the stage advanced to the page's own next cursor.");
        }

        /// <summary>An event whose cursor belongs to another world's session is skipped: not played, not resolved.</summary>
        [Test]
        public void AnEventStampedByAnotherWorldSessionIsSkippedRatherThanPlayed()
        {
            CommittedEventPage page = PageOf(
                EventOf(ForeignWorld, 1UL, AudibleSchema),
                EventOf(World, 1UL, AudibleSchema));
            var sink = new RecordingAudioSink();
            CommittedAudioStage stage = StageOver(page, sink, out _);

            int played = stage.PresentNextPage(4);

            Assert.That(played, Is.EqualTo(1));
            Assert.That(stage.ReadCount, Is.EqualTo(2), "both events came from the world's own reader.");
            Assert.That(stage.AudibleCount, Is.EqualTo(1), "a foreign world's event is not this stage's to resolve (P-004).");
            Assert.That(stage.InaudibleCount, Is.Zero, "a foreign event is skipped, not miscounted as inaudible.");
            Assert.That(stage.PlayedCount, Is.EqualTo(1));
            Assert.That(sink.Plays.Count, Is.EqualTo(1));
            Assert.That(
                sink.Plays[0].EventKey,
                Is.EqualTo(CommittedAudioStage.EventKeyOf(EventOf(World, 1UL, AudibleSchema))));
            Assert.That(stage.PlayedEventKeys, Does.Not.Contain(CommittedAudioStage.EventKeyOf(EventOf(ForeignWorld, 1UL, AudibleSchema))));
        }

        /// <summary>
        /// The disabled-device path (crash-139): every audible event is refused as unavailable rather than played or
        /// thrown, and the same page plays once the device returns, so the obligation stayed retryable.
        /// </summary>
        [Test]
        public void AnUnavailableAudioDeviceRefusesEveryCueWithoutThrowingAndLeavesThemRetryable()
        {
            CommittedEventPage page = PageOf(
                EventOf(World, 1UL, AudibleSchema),
                EventOf(World, 2UL, AudibleSchema));
            var sink = new RecordingAudioSink();
            sink.Unavailable = true;
            CommittedAudioStage stage = StageOver(page, sink, out _);

            int played = 0;
            Assert.DoesNotThrow(() => { played = stage.PresentNextPage(4); });

            Assert.That(played, Is.Zero);
            Assert.That(sink.IsAvailable, Is.False);
            Assert.That(stage.ReadCount, Is.EqualTo(2));
            Assert.That(stage.AudibleCount, Is.EqualTo(2));
            Assert.That(stage.RefusedCount, Is.EqualTo(2), "the disabled device is a value, not a throw (04 s7).");
            Assert.That(stage.PlayedCount, Is.Zero);
            Assert.That(stage.SuppressedCount, Is.Zero, "a refused event was never played, so it is not a duplicate either.");
            Assert.That(stage.PlayedEventKeys, Is.Empty);
            Assert.That(sink.Plays, Is.Empty);

            sink.Unavailable = false;
            int replayed = stage.PresentNextPage(4);

            Assert.That(replayed, Is.EqualTo(2), "the unplayed events were still owed and now play (P-045).");
            Assert.That(stage.PlayedCount, Is.EqualTo(2));
            Assert.That(stage.RefusedCount, Is.EqualTo(2), "the earlier refusals remain in the audit.");
            Assert.That(sink.Plays.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// A sink rejection is a value, not an exception, and the rejected event stays owed so the next pass plays
        /// it (P-045, 04 s7).
        /// </summary>
        [Test]
        public void ASinkRejectionIsAValueAndTheRejectedEventStaysRetryable()
        {
            CommittedEventPage page = PageOf(EventOf(World, 1UL, AudibleSchema));
            var sink = new RecordingAudioSink();
            sink.RejectNext = true;
            CommittedAudioStage stage = StageOver(page, sink, out _);
            Id128 key = CommittedAudioStage.EventKeyOf(EventOf(World, 1UL, AudibleSchema));

            int played = stage.PresentNextPage(2);

            Assert.That(played, Is.Zero);
            Assert.That(stage.SinkRejectionCount, Is.EqualTo(1));
            Assert.That(stage.PlayedCount, Is.Zero);
            Assert.That(stage.SuppressedCount, Is.Zero);
            Assert.That(stage.PlayedEventKeys, Does.Not.Contain(key), "a rejected event was never recorded as played.");
            Assert.That(sink.RejectionCount, Is.EqualTo(1));
            Assert.That(sink.Plays, Is.Empty);

            int replayed = stage.PresentNextPage(2);

            Assert.That(replayed, Is.EqualTo(1), "the rejected event is still owed and plays on the next pass.");
            Assert.That(stage.PlayedCount, Is.EqualTo(1));
            Assert.That(stage.SinkRejectionCount, Is.EqualTo(1));
            Assert.That(stage.PlayedEventKeys, Does.Contain(key));
            Assert.That(sink.Plays.Count, Is.EqualTo(1));
            Assert.That(sink.Plays[0].PayloadLength, Is.EqualTo(CuePayload.Length));
        }

        /// <summary>
        /// The cue table binds one schema to at most one cue, refuses a duplicate schema without changing its size,
        /// and resolves in canonical schema order so a reader sees declaration-independent order (P-008, P-045).
        /// </summary>
        [Test]
        public void TheCueTableRefusesADuplicateSchemaAndResolvesInCanonicalSchemaOrder()
        {
            var cues = new AudioCueTable();

            Assert.That(cues.TryAdd(SilentSchema, OtherCue), Is.True);
            Assert.That(cues.TryAdd(AudibleSchema, FootstepCue), Is.True);
            Assert.That(cues.TryAdd(AudibleSchema, OtherCue), Is.False, "one schema resolves to one cue, so it cannot play twice.");
            Assert.That(cues.Bindings.Count, Is.EqualTo(2), "a refused binding does not change the table.");

            Assert.That(
                cues.Bindings[0].Schema.Id.Value,
                Is.EqualTo(AudibleSchemaId.Value),
                "declaration order is irrelevant; canonical schema order is what a reader sees (P-008).");
            Assert.That(cues.Bindings[1].Schema.Id.Value, Is.EqualTo(SilentSchemaId.Value));
            Assert.That(cues.Bindings[0].Cue, Is.EqualTo(FootstepCue));

            Assert.That(cues.TryResolve(AudibleSchema, out Id128 resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(FootstepCue));
            Assert.That(
                cues.TryResolve(new SchemaRef(AudibleSchemaId, 9U), out Id128 sameSchemaOtherVersion),
                Is.True);
            Assert.That(sameSchemaOtherVersion, Is.EqualTo(FootstepCue), "schema identity, not its version, selects the cue.");

            var unbound = new SchemaRef(new SchemaId(new Id128(0x4743303230534348UL, 3UL)), 1U);
            Assert.That(cues.TryResolve(unbound, out Id128 none), Is.False);
            Assert.That(none, Is.EqualTo(default(Id128)));
        }
    }

    /// <summary>
    /// The committed-output animation stage: one presentation frame per strictly newer committed token, a sink
    /// refusal reported rather than thrown, and bounded root-motion proposals drained by a later step (P-045, P-043,
    /// 04 s7).
    /// </summary>
    [TestFixture]
    public sealed class CommittedAnimationStageTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303230574F52UL, 0x11UL));
        private static readonly WorldId ForeignWorld = new WorldId(new Id128(0x4743303230574F52UL, 0x12UL));
        private static readonly TargetId PosedTarget = new TargetId(new Id128(0x4743303230544152UL, 1UL));
        private static readonly TargetId SecondTarget = new TargetId(new Id128(0x4743303230544152UL, 2UL));
        private static readonly SnapshotToken FirstToken =
            new SnapshotToken(World, new AssemblyEpoch(1UL), new LogicalStepId(1UL));

        private static IReadOnlyList<CommittedPose> Poses(int count)
        {
            var poses = new List<CommittedPose>(count);
            for (int i = 0; i < count; i++)
            {
                poses.Add(new CommittedPose(PosedTarget, i * 10, 0, 0, 1));
            }

            return poses;
        }

        /// <summary>
        /// One frame is presented per strictly newer committed token: the same token twice is a stale refusal, a
        /// newer step and a higher epoch at the same step are both newer, and another world's session is never
        /// newer (P-045).
        /// </summary>
        [Test]
        public void OneFrameIsPresentedPerStrictlyNewerCommittedTokenAndAStaleOrForeignTokenIsRefused()
        {
            var source = new RecordingPoseSource(FirstToken);
            var sink = new RecordingAnimationSink();
            var stage = new CommittedAnimationStage(source, sink, 8);
            source.Publish(FirstToken, Poses(2));

            Assert.That(stage.TryPresentOnce(out string first), Is.True, first);
            Assert.That(stage.PresentedCount, Is.EqualTo(1));
            Assert.That(stage.LastPresentedToken, Is.EqualTo(FirstToken));
            Assert.That(sink.PresentedTokens.Count, Is.EqualTo(1));
            Assert.That(sink.LastPoseCount, Is.EqualTo(2));

            Assert.That(stage.TryPresentOnce(out string stale), Is.False, stale);
            Assert.That(stage.StaleRefusalCount, Is.EqualTo(1), "the same publication cannot replay a frame (P-045).");
            Assert.That(stage.PresentedCount, Is.EqualTo(1));
            Assert.That(sink.PresentedTokens.Count, Is.EqualTo(1));
            Assert.That(stale, Is.Not.Empty);

            var nextStep = new SnapshotToken(World, new AssemblyEpoch(1UL), new LogicalStepId(2UL));
            source.Publish(nextStep, Poses(1));
            Assert.That(stage.TryPresentOnce(out string newerStep), Is.True, newerStep);
            Assert.That(stage.LastPresentedToken, Is.EqualTo(nextStep));
            Assert.That(sink.LastPoseCount, Is.EqualTo(1), "the newer frame's own poses were the ones applied.");

            var nextEpoch = new SnapshotToken(World, new AssemblyEpoch(2UL), new LogicalStepId(2UL));
            source.Publish(nextEpoch, Poses(3));
            Assert.That(stage.TryPresentOnce(out string newerEpoch), Is.True, newerEpoch);
            Assert.That(stage.PresentedCount, Is.EqualTo(3), "a same-step publication with a higher epoch is newer (P-006).");
            Assert.That(stage.LastPresentedToken, Is.EqualTo(nextEpoch));

            var foreign = new SnapshotToken(ForeignWorld, new AssemblyEpoch(9UL), new LogicalStepId(9UL));
            source.Publish(foreign, Poses(1));
            Assert.That(stage.TryPresentOnce(out string stranger), Is.False, stranger);
            Assert.That(stage.StaleRefusalCount, Is.EqualTo(2), "another world's session is never a newer publication of this one.");
            Assert.That(stage.PresentedCount, Is.EqualTo(3));
            Assert.That(stage.LastPresentedToken, Is.EqualTo(nextEpoch), "a refused pass never becomes the last presented token.");
            Assert.That(sink.PresentedTokens.Count, Is.EqualTo(3));
            Assert.That(sink.PresentedTokens[0], Is.EqualTo(FirstToken));
            Assert.That(sink.PresentedTokens[1], Is.EqualTo(nextStep));
            Assert.That(sink.PresentedTokens[2], Is.EqualTo(nextEpoch));
            Assert.That(stage.UnavailableCount, Is.Zero);
        }

        /// <summary>
        /// An unavailable or refusing presentation sink is reported as a value, presents nothing and claims no
        /// token, and the refused frame is presented once the sink recovers (04 s7).
        /// </summary>
        [Test]
        public void AnUnavailableOrRefusingSinkIsReportedAsAValueAndPresentsNothing()
        {
            var source = new RecordingPoseSource(FirstToken);
            source.Publish(FirstToken, Poses(1));
            var unavailableSink = new RecordingAnimationSink();
            unavailableSink.Unavailable = true;
            var stage = new CommittedAnimationStage(source, unavailableSink, 4);

            Assert.That(stage.TryPresentOnce(out string noAnimator), Is.False, noAnimator);
            Assert.That(noAnimator, Is.Not.Empty);
            Assert.That(stage.UnavailableCount, Is.EqualTo(1));
            Assert.That(stage.PresentedCount, Is.Zero);
            Assert.That(stage.StaleRefusalCount, Is.Zero, "an unavailable sink is not a stale token.");
            Assert.That(unavailableSink.PresentedTokens, Is.Empty);
            Assert.That(stage.LastPresentedToken, Is.EqualTo(default(SnapshotToken)), "a refused pass claimed no token.");

            unavailableSink.Unavailable = false;
            Assert.That(stage.TryPresentOnce(out string recovered), Is.True, recovered);
            Assert.That(stage.UnavailableCount, Is.EqualTo(1));
            Assert.That(stage.PresentedCount, Is.EqualTo(1), "the frame the sink could not take is taken once it can.");
            Assert.That(unavailableSink.PresentedTokens.Count, Is.EqualTo(1));

            var rejectingSink = new RecordingAnimationSink();
            rejectingSink.RejectNext = true;
            var rejectingStage = new CommittedAnimationStage(source, rejectingSink, 4);

            Assert.That(rejectingStage.TryPresentOnce(out string rejected), Is.False, rejected);
            Assert.That(rejected, Is.Not.Empty);
            Assert.That(rejectingSink.RejectionCount, Is.EqualTo(1));
            Assert.That(rejectingStage.UnavailableCount, Is.EqualTo(1), "a sink rejection is a refusal value, not an exception.");
            Assert.That(rejectingStage.PresentedCount, Is.Zero);
            Assert.That(rejectingSink.PresentedTokens, Is.Empty);
            Assert.That(rejectingStage.LastPresentedToken, Is.EqualTo(default(SnapshotToken)));

            Assert.That(rejectingStage.TryPresentOnce(out string retried), Is.True, retried);
            Assert.That(rejectingSink.PresentedTokens.Count, Is.EqualTo(1), "the rejected frame stays available for the next pass.");
        }

        /// <summary>
        /// Root-motion proposals are bounded to the declared capacity: an ineffective one is refused without
        /// counting, an overflowing one is counted rather than dropped, and the queue drains in order for a later
        /// step and frees its capacity (P-043, 04 s7).
        /// </summary>
        [Test]
        public void RootMotionProposalsAreBoundedToTheDeclaredCapacityAndDrainedInOrderForALaterStep()
        {
            var source = new RecordingPoseSource(FirstToken);
            var sink = new RecordingAnimationSink();
            var stage = new CommittedAnimationStage(source, sink, 5);

            var first = new RootMotionProposal(PosedTarget, 1, 0, 0, FirstToken);
            var second = new RootMotionProposal(SecondTarget, 2, 0, 0, FirstToken);
            var third = new RootMotionProposal(PosedTarget, 3, 0, 0, FirstToken);
            var fourth = new RootMotionProposal(PosedTarget, 4, 0, 0, FirstToken);
            var fifth = new RootMotionProposal(SecondTarget, 5, 0, 0, FirstToken);
            var ineffective = new RootMotionProposal(PosedTarget, 0, 0, 0, FirstToken);

            Assert.That(ineffective.IsEffective, Is.False, "an all-zero proposal asks for no displacement.");

            Assert.That(stage.TryOfferRootMotion(first), Is.True);
            Assert.That(stage.TryOfferRootMotion(second), Is.True);
            Assert.That(stage.TryOfferRootMotion(third), Is.True);
            Assert.That(stage.ProposalCount, Is.EqualTo(3));
            Assert.That(stage.PendingProposals.Count, Is.EqualTo(3));

            Assert.That(stage.TryOfferRootMotion(ineffective), Is.False, "an ineffective proposal is refused.");
            Assert.That(stage.ProposalCount, Is.EqualTo(3), "an ineffective proposal is refused without counting.");
            Assert.That(stage.ProposalOverflowCount, Is.Zero, "an ineffective proposal is not an overflow.");
            Assert.That(stage.PendingProposals.Count, Is.EqualTo(3));

            Assert.That(stage.TryOfferRootMotion(fourth), Is.True);
            Assert.That(stage.TryOfferRootMotion(fifth), Is.True);
            Assert.That(stage.PendingProposals.Count, Is.EqualTo(5), "the declared capacity is five.");
            Assert.That(stage.ProposalCount, Is.EqualTo(5));

            Assert.That(stage.TryOfferRootMotion(ineffective), Is.False);
            Assert.That(
                stage.ProposalOverflowCount,
                Is.Zero,
                "effectiveness is decided before capacity, so a no-op never counts as an overflow.");

            var overflowing = new RootMotionProposal(PosedTarget, 6, 0, 0, FirstToken);
            Assert.That(stage.TryOfferRootMotion(overflowing), Is.False);
            Assert.That(
                stage.ProposalOverflowCount,
                Is.EqualTo(1),
                "work that cannot be queued is counted, not silently dropped (P-043).");
            Assert.That(stage.ProposalCount, Is.EqualTo(5));
            Assert.That(stage.PendingProposals.Count, Is.EqualTo(5));
            Assert.That(stage.PresentedCount, Is.Zero, "offering proposals presents nothing and writes no pose.");

            IReadOnlyList<RootMotionProposal> taken = stage.TakePendingForNextStep();

            Assert.That(taken.Count, Is.EqualTo(5));
            Assert.That(taken[0].DeltaX, Is.EqualTo(1));
            Assert.That(taken[1].DeltaX, Is.EqualTo(2), "proposals are drained in the order they were offered.");
            Assert.That(taken[2].DeltaX, Is.EqualTo(3));
            Assert.That(taken[3].DeltaX, Is.EqualTo(4));
            Assert.That(taken[4].DeltaX, Is.EqualTo(5));
            Assert.That(taken[1].Target, Is.EqualTo(SecondTarget));
            Assert.That(taken[0].Source, Is.EqualTo(FirstToken), "the proposal carries its provenance, never authority (P-045).");
            Assert.That(stage.PendingProposals, Is.Empty, "taking them for the next step drains the queue.");
            Assert.That(stage.ProposalCount, Is.EqualTo(5), "the audit count is not rewound by draining.");
            Assert.That(stage.TakePendingForNextStep(), Is.Empty, "a second take has nothing left.");

            var refilled = new RootMotionProposal(PosedTarget, 7, 0, 0, FirstToken);
            Assert.That(stage.TryOfferRootMotion(refilled), Is.True, "draining frees the capacity for the next step.");
            Assert.That(stage.PendingProposals.Count, Is.EqualTo(1));
        }
    }
}
