// GameCore.Gameplay.Audio - the audio plugin's kernel half: payloads, the session recipe, the per-world module and the
// command system (P1.5, catalog row 11; P-032, P-042, P-044).
//
// The command system is the only writer of the audio slots. Music states and ambience zones are validated against the
// sets the world was built with (the game's MusicStateDefinitions and the regions that have an AmbienceDefinition);
// volumes by the pure volume rules. One-shot cues (sfx, voice, stop) are validated for shape and committed as events.
// A refusal is a rejected command plus one trace entry with the inputs the rule read.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Audio;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Audio
{
    /// <summary>An audio command payload of up to four int32 values.</summary>
    public readonly struct AudioCommandPayload
    {
        public AudioCommandPayload(int a, int b, int c, int d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public static FrozenPayload Encode(params int[] values)
        {
            var writer = new GameplayPayloadWriter();
            for (int i = 0; i < values.Length; i++)
            {
                writer.Int32(values[i]);
            }

            return writer.Freeze();
        }
    }

    /// <summary>Reads one audio command payload of an exact length (4, 8 or 16 bytes).</summary>
    public sealed class AudioCommandReader : ICommandPayloadReader<AudioCommandPayload>
    {
        private readonly int length;

        public AudioCommandReader(SchemaRef schema, int length)
        {
            Schema = schema;
            this.length = length;
        }

        public SchemaRef Schema { get; }

        public AudioCommandPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(length))
            {
                throw new FormatException("an audio command of this schema is exactly " + length + " bytes");
            }

            int count = length / 4;
            int a = count > 0 ? reader.Int32() : 0;
            int b = count > 1 ? reader.Int32() : 0;
            int c = count > 2 ? reader.Int32() : 0;
            int d = count > 3 ? reader.Int32() : 0;
            return new AudioCommandPayload(a, b, c, d);
        }
    }

    public static class AudioReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Bind(readers, new AudioCommandReader(AudioDeclarations.SetMusicStateCommand, 8));
            Bind(readers, new AudioCommandReader(AudioDeclarations.SetAmbienceZoneCommand, 4));
            Bind(readers, new AudioCommandReader(AudioDeclarations.SetVolumeCommand, 8));
            Bind(readers, new AudioCommandReader(AudioDeclarations.PlaySfxCommand, 16));
            Bind(readers, new AudioCommandReader(AudioDeclarations.PlayVoiceCommand, 8));
            Bind(readers, new AudioCommandReader(AudioDeclarations.StopVoiceCommand, 4));
        }

        private static void Bind(CommandPayloadReaders readers, AudioCommandReader reader)
        {
            if (!readers.TryBind(reader, out string failure))
            {
                throw new InvalidOperationException("audio reader registration failed: " + failure);
            }
        }
    }

    /// <summary>
    /// A decoded audio event: the session target and four int32 values. MusicStateChanged (from, to, stinger, 0),
    /// AmbienceChanged (from, to, 0, 0), VolumeChanged (channel, value, old, 0), SfxPlayed (sfx, x, y, z),
    /// VoicePlayed (clip, speaker, 0, 0), VoiceStopped (0, 0, 0, 0).
    /// </summary>
    public readonly struct AudioEvent
    {
        public const int Length = 32;

        public AudioEvent(SchemaRef schema, TargetId target, int a, int b, int c, int d)
        {
            Schema = schema;
            Target = target;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public SchemaRef Schema { get; }

        public TargetId Target { get; }

        public int A { get; }

        public int B { get; }

        public int C { get; }

        public int D { get; }

        public static FrozenPayload Encode(TargetId target, int a, int b, int c, int d) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(a).Int32(b).Int32(c).Int32(d).Freeze();

        /// <summary>Decodes a committed event of one of the six audio event schemas.</summary>
        public static bool TryDecode(CommittedEvent committed, out AudioEvent decoded)
        {
            decoded = default(AudioEvent);
            if (committed == null || committed.Payload.Length != Length || !IsAudioEvent(committed.Schema))
            {
                return false;
            }

            var reader = new GameplayPayloadReader(committed.Payload.Bytes);
            decoded = new AudioEvent(committed.Schema, new TargetId(reader.Id()), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
            return true;
        }

        public static bool IsAudioEvent(SchemaRef schema) =>
            schema.Equals(AudioDeclarations.MusicStateChangedEvent) || schema.Equals(AudioDeclarations.AmbienceChangedEvent)
            || schema.Equals(AudioDeclarations.VolumeChangedEvent) || schema.Equals(AudioDeclarations.SfxPlayedEvent)
            || schema.Equals(AudioDeclarations.VoicePlayedEvent) || schema.Equals(AudioDeclarations.VoiceStoppedEvent);
    }

    /// <summary>Base layout of the audio session target: an empty owned-slot buffer.</summary>
    public sealed class AudioSessionApplier : ISpawnApplier
    {
        public FactoryKey Key => AudioDeclarations.SessionApplier;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>One refused audio command with the inputs the rule read.</summary>
    public readonly struct AudioRefusalTrace
    {
        public AudioRefusalTrace(ulong step, string route, AudioRefusal refusal, int current, int a, int b, string detail)
        {
            Step = step;
            Route = route;
            Refusal = refusal;
            Current = current;
            A = a;
            B = b;
            Detail = detail;
        }

        public ulong Step { get; }

        public string Route { get; }

        public AudioRefusal Refusal { get; }

        /// <summary>The committed value the rule compared against.</summary>
        public int Current { get; }

        public int A { get; }

        public int B { get; }

        public string Detail { get; }

        public override string ToString() => "step " + Step + " " + Route + "(" + A + "," + B + ") refused " + Refusal + " (current " + Current + "): " + Detail;
    }

    /// <summary>The audio plugin's state of one world. Instance state only.</summary>
    public sealed class AudioModule
    {
        public const int TraceCapacity = 64;

        private readonly List<AudioRefusalTrace> trace = new List<AudioRefusalTrace>();

        public AudioModule(UnityWorldHost host, TargetRegistry registry, TargetId session, IReadOnlyCollection<int> musicStates, IReadOnlyCollection<int> zones)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Session = session;
            MusicStates = musicStates ?? Array.Empty<int>();
            Zones = zones ?? Array.Empty<int>();
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        public TargetId Session { get; }

        /// <summary>Known music state keys (silence, 0, is always allowed).</summary>
        public IReadOnlyCollection<int> MusicStates { get; }

        /// <summary>Region keys that have an ambience.</summary>
        public IReadOnlyCollection<int> Zones { get; }

        public int Accepted { get; private set; }

        public int Refused { get; private set; }

        public IReadOnlyList<AudioRefusalTrace> Trace => trace;

        internal void CountAccepted() => Accepted++;

        internal void Record(AudioRefusalTrace entry)
        {
            Refused++;
            if (trace.Count == TraceCapacity)
            {
                trace.RemoveAt(0);
            }

            trace.Add(entry);
        }
    }

    /// <summary>The audio command stage.</summary>
    [DisableAutoCreation]
    public partial class AudioCommandSystem : SystemBase
    {
        /// <summary>This world's module; set when the audio extension attaches. Until then the stage is idle.</summary>
        public AudioModule? Module { get; set; }

        protected override void OnUpdate()
        {
            AudioModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(AudioDeclarations.Owner);
            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                Handle(module, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(AudioDeclarations.Owner);
        }

        private static void Handle(AudioModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            string route = RouteName(message.Route);
            byte[] payload = plane.PayloadOf(message);
            if (route.Length == 0
                || plane.Readers.TryRead<AudioCommandPayload>(message.PayloadSchema, payload, out AudioCommandPayload command, out string failure)
                != PayloadDecodeOutcome.Decoded)
            {
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                module.Record(new AudioRefusalTrace(plane.ExecutingStep.Value, route, AudioRefusal.OutOfRange, 0, 0, 0, "undecodable audio command"));
                return;
            }

            if (!message.Target.Equals(module.Session) || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity session))
            {
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                module.Record(new AudioRefusalTrace(plane.ExecutingStep.Value, route, AudioRefusal.OutOfRange, 0, command.A, command.B,
                    "the target is not this world's audio session"));
                return;
            }

            if (message.Route.Equals(AudioDeclarations.SetMusicStateRoute))
            {
                int current = Read(entityManager, session, PresentationSlots.MusicState, MusicStateRules.Silence);
                AudioRefusal refusal = MusicStateRules.Check(current, command.A, module.MusicStates);
                if (Refuse(module, plane, message, route, refusal, current, command))
                {
                    return;
                }

                int stinger = MusicStateRules.PlaysStinger(command.A, command.B) ? command.B : 0;
                if (Commit(module, plane, message, route, current, command, AudioDeclarations.MusicStateChangedEvent, current, command.A, stinger, 0))
                {
                    Write(entityManager, session, PresentationSlots.MusicState, command.A);
                }
            }
            else if (message.Route.Equals(AudioDeclarations.SetAmbienceZoneRoute))
            {
                int current = Read(entityManager, session, PresentationSlots.AmbienceZone, AmbienceRules.NoZone);
                AudioRefusal refusal = AmbienceRules.Check(current, command.A, module.Zones);
                if (Refuse(module, plane, message, route, refusal, current, command))
                {
                    return;
                }

                if (Commit(module, plane, message, route, current, command, AudioDeclarations.AmbienceChangedEvent, current, command.A, 0, 0))
                {
                    Write(entityManager, session, PresentationSlots.AmbienceZone, command.A);
                }
            }
            else if (message.Route.Equals(AudioDeclarations.SetVolumeRoute))
            {
                bool known = PresentationSlots.TryVolumeSlot(command.A, out SlotId slot);
                int current = known ? Read(entityManager, session, slot, 0) : 0;
                AudioRefusal refusal = VolumeRules.Check(command.A, command.B, current);
                if (Refuse(module, plane, message, route, refusal, current, command))
                {
                    return;
                }

                if (Commit(module, plane, message, route, current, command, AudioDeclarations.VolumeChangedEvent, command.A, command.B, current, 0))
                {
                    Write(entityManager, session, slot, command.B);
                }
            }
            else if (message.Route.Equals(AudioDeclarations.PlaySfxRoute))
            {
                if (Refuse(module, plane, message, route, command.A == 0 ? AudioRefusal.UnknownState : AudioRefusal.None, 0, command))
                {
                    return;
                }

                Commit(module, plane, message, route, 0, command, AudioDeclarations.SfxPlayedEvent, command.A, command.B, command.C, command.D);
            }
            else if (message.Route.Equals(AudioDeclarations.PlayVoiceRoute))
            {
                if (Refuse(module, plane, message, route, command.A == 0 ? AudioRefusal.UnknownState : AudioRefusal.None, 0, command))
                {
                    return;
                }

                Commit(module, plane, message, route, 0, command, AudioDeclarations.VoicePlayedEvent, command.A, command.B, 0, 0);
            }
            else
            {
                Commit(module, plane, message, route, 0, command, AudioDeclarations.VoiceStoppedEvent, 0, 0, 0, 0);
            }
        }

        private static string RouteName(RouteId route)
        {
            if (route.Equals(AudioDeclarations.SetMusicStateRoute))
            {
                return "audio.setMusicState";
            }

            if (route.Equals(AudioDeclarations.SetAmbienceZoneRoute))
            {
                return "audio.setAmbienceZone";
            }

            if (route.Equals(AudioDeclarations.SetVolumeRoute))
            {
                return "audio.setVolume";
            }

            if (route.Equals(AudioDeclarations.PlaySfxRoute))
            {
                return "audio.playSfx";
            }

            if (route.Equals(AudioDeclarations.PlayVoiceRoute))
            {
                return "audio.playVoice";
            }

            return route.Equals(AudioDeclarations.StopVoiceRoute) ? "audio.stopVoice" : string.Empty;
        }

        private static bool Refuse(AudioModule module, WorldMessagePlane plane, StepMessage message, string route, AudioRefusal refusal, int current, AudioCommandPayload command)
        {
            if (refusal == AudioRefusal.None)
            {
                return false;
            }

            plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
            module.Record(new AudioRefusalTrace(plane.ExecutingStep.Value, route, refusal, current, command.A, command.B, refusal.ToString()));
            return true;
        }

        private static bool Commit(
            AudioModule module,
            WorldMessagePlane plane,
            StepMessage message,
            string route,
            int current,
            AudioCommandPayload command,
            SchemaRef schema,
            int a,
            int b,
            int c,
            int d)
        {
            if (!plane.Commit(message, schema, AudioEvent.Encode(message.Target, a, b, c, d), plane.ExecutingStep, out string failure))
            {
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                module.Record(new AudioRefusalTrace(plane.ExecutingStep.Value, route, AudioRefusal.None, current, command.A, command.B, failure));
                return false;
            }

            module.CountAccepted();
            return true;
        }

        private static int Read(EntityManager entityManager, Entity session, SlotId slot, int fallback) =>
            SlotState.ReadOrDefault(entityManager, session, PresentationSlots.AudioOwner, slot, fallback);

        private static void Write(EntityManager entityManager, Entity session, SlotId slot, int value) =>
            SlotState.Write(entityManager, session, PresentationSlots.AudioOwner, slot, value);
    }
}
