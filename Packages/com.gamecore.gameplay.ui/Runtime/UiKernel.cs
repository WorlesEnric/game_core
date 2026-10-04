// GameCore.Gameplay.Ui - the UI plugin's kernel half: payloads, the session recipe, the per-world module and the
// command system (P1.5, catalog row 10; P-032, P-042, P-044).
//
// The command system is the only writer of ui.screen / ui.returnTo / ui.message. It decodes each ui.open / ui.close /
// ui.command, asks the pure ScreenFlowRules for the verdict over the committed slots, commits one ScreenChanged event
// and writes the three slots. A refusal is a rejected command plus one trace entry naming the refusal and the inputs
// the rule read (explainability); the slots are untouched.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Ui
{
    /// <summary>A UI command payload: up to two int32 values (open: screen; close: 0; command: action, argument).</summary>
    public readonly struct UiCommandPayload
    {
        public UiCommandPayload(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; }

        public int B { get; }

        public static FrozenPayload EncodeOne(int value) => new GameplayPayloadWriter().Int32(value).Freeze();

        public static FrozenPayload EncodeTwo(int a, int b) => new GameplayPayloadWriter().Int32(a).Int32(b).Freeze();
    }

    /// <summary>Reads one UI command payload of a fixed length.</summary>
    public sealed class UiCommandReader : ICommandPayloadReader<UiCommandPayload>
    {
        private readonly int length;

        public UiCommandReader(SchemaRef schema, int length)
        {
            Schema = schema;
            this.length = length;
        }

        public SchemaRef Schema { get; }

        public UiCommandPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(length))
            {
                throw new FormatException("a UI command of this schema is exactly " + length + " bytes");
            }

            int a = reader.Int32();
            int b = length >= 8 ? reader.Int32() : 0;
            return new UiCommandPayload(a, b);
        }
    }

    public static class UiReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Bind(readers, new UiCommandReader(UiDeclarations.OpenCommand, 4));
            Bind(readers, new UiCommandReader(UiDeclarations.CloseCommand, 4));
            Bind(readers, new UiCommandReader(UiDeclarations.CommandCommand, 8));
        }

        private static void Bind(CommandPayloadReaders readers, UiCommandReader reader)
        {
            if (!readers.TryBind(reader, out string failure))
            {
                throw new InvalidOperationException("UI reader registration failed: " + failure);
            }
        }
    }

    /// <summary>A decoded ScreenChanged event: the session target, the screens before and after, the action and its argument.</summary>
    public readonly struct ScreenChanged
    {
        public const int Length = 32;

        public ScreenChanged(TargetId target, int from, int to, int action, int argument)
        {
            Target = target;
            From = from;
            To = to;
            Action = action;
            Argument = argument;
        }

        public TargetId Target { get; }

        public int From { get; }

        public int To { get; }

        /// <summary>The host action (UiAction) the UI host performs after the commit; 0 for a pure screen change.</summary>
        public int Action { get; }

        public int Argument { get; }

        public UiScreen FromScreen => (UiScreen)From;

        public UiScreen ToScreen => (UiScreen)To;

        public UiAction HostAction => (UiAction)Action;

        public static FrozenPayload Encode(TargetId target, int from, int to, int action, int argument) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(from).Int32(to).Int32(action).Int32(argument).Freeze();

        public static bool TryDecode(CommittedEvent committed, out ScreenChanged decoded)
        {
            decoded = default(ScreenChanged);
            if (committed == null || !committed.Schema.Equals(UiDeclarations.ScreenChangedEvent))
            {
                return false;
            }

            return TryDecode(committed.Payload, out decoded);
        }

        public static bool TryDecode(FrozenPayload payload, out ScreenChanged decoded)
        {
            decoded = default(ScreenChanged);
            if (payload == null || payload.Length != Length)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload.Bytes);
            decoded = new ScreenChanged(new TargetId(reader.Id()), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
            return true;
        }

        public override string ToString() => "ScreenChanged(" + FromScreen + " -> " + ToScreen + (Action == 0 ? string.Empty : ", " + HostAction + " " + Argument) + ")";
    }

    /// <summary>Base layout of the UI session target: an empty owned-slot buffer (slots are seeded after boot).</summary>
    public sealed class UiSessionApplier : ISpawnApplier
    {
        public FactoryKey Key => UiDeclarations.SessionApplier;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>One refused UI command with the inputs the rule read (explain trace).</summary>
    public readonly struct UiRefusalTrace
    {
        public UiRefusalTrace(ulong step, string route, UiRefusal refusal, UiState state, int a, int b, string detail)
        {
            Step = step;
            Route = route;
            Refusal = refusal;
            State = state;
            A = a;
            B = b;
            Detail = detail;
        }

        public ulong Step { get; }

        public string Route { get; }

        public UiRefusal Refusal { get; }

        public UiState State { get; }

        public int A { get; }

        public int B { get; }

        public string Detail { get; }

        public override string ToString() => "step " + Step + " " + Route + "(" + A + "," + B + ") refused " + Refusal + " on " + State + ": " + Detail;
    }

    /// <summary>The UI plugin's state of one world. Instance state only.</summary>
    public sealed class UiModule
    {
        public const int TraceCapacity = 64;

        private readonly List<UiRefusalTrace> trace = new List<UiRefusalTrace>();

        public UiModule(UnityWorldHost host, TargetRegistry registry, TargetId session)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Session = session;
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        /// <summary>The UI session target of this world.</summary>
        public TargetId Session { get; }

        public int Accepted { get; private set; }

        public int Refused { get; private set; }

        /// <summary>The last <see cref="TraceCapacity"/> refusals, oldest first.</summary>
        public IReadOnlyList<UiRefusalTrace> Trace => trace;

        internal void CountAccepted() => Accepted++;

        internal void Record(UiRefusalTrace entry)
        {
            Refused++;
            if (trace.Count == TraceCapacity)
            {
                trace.RemoveAt(0);
            }

            trace.Add(entry);
        }

        public static UiState ReadState(EntityManager entityManager, Entity session)
        {
            return new UiState(
                SlotState.ReadOrDefault(entityManager, session, PresentationSlots.UiOwner, PresentationSlots.Screen, (int)UiScreen.None),
                SlotState.ReadOrDefault(entityManager, session, PresentationSlots.UiOwner, PresentationSlots.ReturnTo, (int)UiScreen.None),
                SlotState.ReadOrDefault(entityManager, session, PresentationSlots.UiOwner, PresentationSlots.Message, 0));
        }

        public static void WriteState(EntityManager entityManager, Entity session, UiState state)
        {
            WriteSlot(entityManager, session, PresentationSlots.Screen, state.Screen);
            WriteSlot(entityManager, session, PresentationSlots.ReturnTo, state.ReturnTo);
            WriteSlot(entityManager, session, PresentationSlots.Message, state.Message);
        }

        private static void WriteSlot(EntityManager entityManager, Entity session, SlotId slot, int value) =>
            SlotState.Write(entityManager, session, PresentationSlots.UiOwner, slot, value);
    }

    /// <summary>The UI command stage: open, close and command.</summary>
    [DisableAutoCreation]
    public partial class UiCommandSystem : SystemBase
    {
        /// <summary>This world's module; set when the UI extension attaches. Until then the stage is idle.</summary>
        public UiModule? Module { get; set; }

        protected override void OnUpdate()
        {
            UiModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(UiDeclarations.Owner);
            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                Handle(module, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(UiDeclarations.Owner);
        }

        private static void Handle(UiModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            string route = message.Route.Equals(UiDeclarations.OpenRoute) ? "ui.open"
                : message.Route.Equals(UiDeclarations.CloseRoute) ? "ui.close"
                : message.Route.Equals(UiDeclarations.UiCommandRoute) ? "ui.command"
                : string.Empty;
            if (route.Length == 0)
            {
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                module.Record(new UiRefusalTrace(plane.ExecutingStep.Value, "unknown", UiRefusal.UnknownScreen, default(UiState), 0, 0, "unknown route"));
                return;
            }

            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<UiCommandPayload>(message.PayloadSchema, payload, out UiCommandPayload command, out string failure)
                != PayloadDecodeOutcome.Decoded)
            {
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                module.Record(new UiRefusalTrace(plane.ExecutingStep.Value, route, UiRefusal.BadArgument, default(UiState), 0, 0, failure));
                return;
            }

            if (!message.Target.Equals(module.Session) || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity session))
            {
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                module.Record(new UiRefusalTrace(plane.ExecutingStep.Value, route, UiRefusal.BadArgument, default(UiState), command.A, command.B,
                    "the target is not this world's UI session"));
                return;
            }

            UiState state = UiModule.ReadState(entityManager, session);
            UiTransition verdict = route == "ui.open" ? ScreenFlowRules.Open(state, command.A)
                : route == "ui.close" ? ScreenFlowRules.Close(state)
                : ScreenFlowRules.Command(state, command.A, command.B);
            if (!verdict.Accepted)
            {
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                module.Record(new UiRefusalTrace(plane.ExecutingStep.Value, route, verdict.Refusal, state, command.A, command.B, verdict.Detail));
                return;
            }

            if (!plane.Commit(
                    message,
                    UiDeclarations.ScreenChangedEvent,
                    ScreenChanged.Encode(message.Target, state.Screen, verdict.Next.Screen, (int)verdict.HostAction, verdict.Argument),
                    plane.ExecutingStep,
                    out string commitFailure))
            {
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                module.Record(new UiRefusalTrace(plane.ExecutingStep.Value, route, UiRefusal.None, state, command.A, command.B, commitFailure));
                return;
            }

            UiModule.WriteState(entityManager, session, verdict.Next);
            module.CountAccepted();
        }
    }
}
