// GameCore.Gameplay.Dialogue - the dialogue plugin's kernel half (P1.4, catalog row 6).
//
// Targets: one narrative state target per world (the conversation and every fact) and one target per graph (its
// visited bits). Routes, all on the state target:
//
//   dialogue.start      graph, speaker, listener   -> Started, [ActionNode, ActionDue..., FactSet...], LineShown | ChoiceOffered | Ended
//   dialogue.choose     option index               -> ChoiceMade, [...], LineShown | ChoiceOffered | Ended
//   dialogue.advance    0                          -> [...], LineShown | ChoiceOffered | Ended
//   dialogue.interrupt  0                          -> [FactSet...], Ended (reason interrupt)
//   narrative.setFact   fact, value, request id    -> FactSet (a replayed request id is rejected as IdempotencyConflict)
//
// A conversation resolves with the pure DialogueRules over a fact overlay: branch nodes read state, action nodes apply
// their fact actions in the step (so a later branch sees them) and every other action of the node is committed as one
// ActionDue event, delivered once by the narrative outbox. Facts that are not persistent return to their initial value
// when the conversation ends. The dialogue stage runs after the world, inventory and quest stages.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Dialogue
{
    /// <summary>Declarations of the dialogue plugin.</summary>
    public static class DialogueDeclarations
    {
        public const string Stem = "dialogue";

        public static readonly StageId Stage = NarrativePluginSpec.StageOf(Stem);

        /// <summary>Domain index of the state target's slots.</summary>
        public const int StateDomain = 0;

        /// <summary>Domain index of a graph target's slots.</summary>
        public const int GraphDomain = 1;

        /// <summary>The dialogue plugin of a content set: conversation slots, one slot per fact, visited words per graph.</summary>
        public static NarrativePluginSpec Spec(NarrativeModelSet models)
        {
            var spec = new NarrativePluginSpec(Stem, NarrativeCatalogNames.Dialogue, DialogueIds.Owner)
                .Route(DialogueIds.StartRoute, DialogueIds.StartCommand, "start", 3)
                .Route(DialogueIds.ChooseRoute, DialogueIds.ChooseCommand, "choose", 1)
                .Route(DialogueIds.AdvanceRoute, DialogueIds.AdvanceCommand, "advance", 1)
                .Route(DialogueIds.InterruptRoute, DialogueIds.InterruptCommand, "interrupt", 1)
                .Route(DialogueIds.SetFactRoute, DialogueIds.SetFactCommand, "set-fact", 3)
                .Slot(DialogueIds.Active, StateDomain, "active")
                .Slot(DialogueIds.Graph, StateDomain, "graph")
                .Slot(DialogueIds.Node, StateDomain, "node")
                .Slot(DialogueIds.Speaker, StateDomain, "speaker")
                .Slot(DialogueIds.Listener, StateDomain, "listener")
                .Slot(DialogueIds.ChoiceCount, StateDomain, "choice-count")
                .Slot(DialogueIds.ChoiceMask, StateDomain, "choice-mask")
                .Slot(DialogueIds.Serial, StateDomain, "serial")
                .Slot(DialogueIds.ReqHead, StateDomain, "req-head");
            for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
            {
                spec.Slot(DialogueIds.Req(i), StateDomain, "req-" + i);
            }

            foreach (FactModel fact in models.Facts)
            {
                spec.Slot(DialogueIds.Fact(fact.Name), StateDomain, "fact-" + fact.Name.Replace('_', '-'));
            }

            int words = Math.Max(1, models.MaxVisitedWords);
            for (int w = 0; w < words; w++)
            {
                spec.Slot(DialogueIds.Visited(w), GraphDomain, "visited-" + w);
            }

            return spec.After(WorldDeclarations.Stage)
                .After(NarrativePluginSpec.StageOf("inventory"))
                .After(NarrativePluginSpec.StageOf("quest"));
        }
    }

    /// <summary>The dialogue module of one world.</summary>
    public sealed class DialogueModule : INarrativeModule, INarrativeWorldAware
    {
        private NarrativeRuntime? runtime;
        private int words = 1;

        public string Name => "dialogue";

        public INarrativeContentConverter? Converter => new DialogueContentConverter();

        public NarrativeRuntime? Runtime => runtime;

        public DialogueRunner? Runner { get; private set; }

        public DialoguePresenter? Presenter { get; private set; }

        public VoiceLinePlayer? Voice { get; private set; }

        /// <summary>The view the presenter pushes to (P1.5 replaces it; a headless null view by default).</summary>
        public IDialogueView View { get; set; } = new NullDialogueView();

        /// <summary>The voice line player (P1.5 replaces it; a null player by default).</summary>
        public IVoiceLinePlayer VoicePlayer { get; set; } = new NullVoiceLinePlayer();

        public int Started { get; private set; }

        public int Ended { get; private set; }

        public int FactsSet { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        public void Declare(NarrativeComposition composition)
        {
            NarrativePluginSpec spec = DialogueDeclarations.Spec(composition.Models);
            words = Math.Max(1, composition.Models.MaxVisitedWords);
            composition.AddPlugin(spec, new ManagedSystemRegistration<DialogueCommandSystem>(spec.CommandSystem, spec.Stage, "GameplayDialogueCommandSystem"));
            composition.AddRecipe(spec.CreateRecipe("state"));
            composition.AddRecipe(spec.CreateRecipe("graph"));
            composition.AddSeed(composition.Index.StateTarget, spec.Recipe("state"), "dialogue-state");
            foreach (DialogueGraphModel graph in composition.Models.Graphs)
            {
                TargetId target = composition.Index.TargetOf(NarrativeTargetKind.Graph, graph.Key);
                composition.AddSeed(target, spec.Recipe("graph"), "graph:" + graph.Name);
            }
        }

        public void Attach(NarrativeRuntime attached, bool seedSlots)
        {
            runtime = attached ?? throw new ArgumentNullException(nameof(attached));
            if (seedSlots)
            {
                TargetId state = attached.Index.StateTarget;
                OwnerId owner = DialogueIds.Owner;
                attached.Seed(state, owner, DialogueIds.Active, 0);
                attached.Seed(state, owner, DialogueIds.Graph, 0);
                attached.Seed(state, owner, DialogueIds.Node, -1);
                attached.Seed(state, owner, DialogueIds.Speaker, 0);
                attached.Seed(state, owner, DialogueIds.Listener, 0);
                attached.Seed(state, owner, DialogueIds.ChoiceCount, 0);
                attached.Seed(state, owner, DialogueIds.ChoiceMask, 0);
                attached.Seed(state, owner, DialogueIds.Serial, 0);
                attached.Seed(state, owner, DialogueIds.ReqHead, 0);
                for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
                {
                    attached.Seed(state, owner, DialogueIds.Req(i), 0);
                }

                foreach (FactModel fact in attached.Models.Facts)
                {
                    attached.Seed(state, owner, DialogueIds.Fact(fact.Name), fact.Initial);
                }

                foreach (DialogueGraphModel graph in attached.Models.Graphs)
                {
                    TargetId target = attached.Index.TargetOf(NarrativeTargetKind.Graph, graph.Key);
                    for (int w = 0; w < words; w++)
                    {
                        attached.Seed(target, owner, DialogueIds.Visited(w), 0);
                    }
                }
            }

            attached.System<DialogueCommandSystem>().Module = this;
        }

        public void OnWorld(NarrativeWorld world)
        {
            NarrativeRuntime rt = world.Runtime;
            Runner = new DialogueRunner(rt);
            world.Conversations = Runner;
            Presenter = new DialoguePresenter(rt, View);
            rt.AddPresenter(Presenter);
            Voice = new VoiceLinePlayer(rt, VoicePlayer);
            rt.AddPresenter(Voice);
        }

        // ---- kernel half --------------------------------------------------------------------------------------

        internal void Run(EntityManager entityManager)
        {
            NarrativeRuntime? rt = runtime;
            WorldMessagePlane? plane = rt != null ? rt.Host.Messages : null;
            if (rt == null || plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(DialogueIds.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(rt, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(DialogueIds.Owner);
        }

        private void Execute(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager em, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<NarrativeCommand>(message.PayloadSchema, payload, out NarrativeCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Malformed++;
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (!message.Target.Equals(rt.Index.StateTarget) || !NarrativeSlots.TryEntity(rt.Registry, em, message.Target, out Entity _))
            {
                Refuse(plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            var step = new DialogueStep(rt, em, message.Target);
            DiagnosticCode refusal;
            if (message.Route.Equals(DialogueIds.StartRoute))
            {
                refusal = step.Start(command[0], command[1], command[2]);
            }
            else if (message.Route.Equals(DialogueIds.ChooseRoute))
            {
                refusal = step.Choose(command[0]);
            }
            else if (message.Route.Equals(DialogueIds.AdvanceRoute))
            {
                refusal = step.Advance();
            }
            else if (message.Route.Equals(DialogueIds.InterruptRoute))
            {
                refusal = step.Interrupt();
            }
            else if (message.Route.Equals(DialogueIds.SetFactRoute))
            {
                refusal = step.SetFact(command[0], command[1], command[2]);
            }
            else
            {
                refusal = DiagnosticCode.Ineligible;
            }

            if (refusal != DiagnosticCode.None)
            {
                Refuse(plane, message, refusal);
                return;
            }

            if (!step.Events.CommitAll(plane, message))
            {
                Refuse(plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            step.Apply();
            Started += step.StartedCount;
            Ended += step.EndedCount;
            FactsSet += step.FactCount;
        }

        private void Refuse(WorldMessagePlane plane, StepMessage message, DiagnosticCode code)
        {
            Refused++;
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }

    /// <summary>
    /// One dialogue command in the step: decides over the live state with a fact overlay, gathers the events and the
    /// slot writes, and applies the writes only after every event was committed.
    /// </summary>
    internal sealed class DialogueStep : IDialogueHost
    {
        private readonly NarrativeRuntime rt;
        private readonly EntityManager em;
        private readonly TargetId state;
        private readonly Dictionary<int, int> facts = new Dictionary<int, int>();
        private readonly Dictionary<int, int> emitted = new Dictionary<int, int>();
        private readonly List<KeyValuePair<SlotId, int>> writes = new List<KeyValuePair<SlotId, int>>();
        private readonly List<KeyValuePair<TargetId, KeyValuePair<SlotId, int>>> graphWrites = new List<KeyValuePair<TargetId, KeyValuePair<SlotId, int>>>();
        private readonly FactOverlayState overlay;
        private int requestId;
        private int listener;
        private int speaker;

        public DialogueStep(NarrativeRuntime runtime, EntityManager entityManager, TargetId stateTarget)
        {
            rt = runtime;
            em = entityManager;
            state = stateTarget;
            overlay = new FactOverlayState(new LiveFactState(runtime, entityManager), facts);
        }

        public StepEventBatch Events { get; } = new StepEventBatch();

        public int StartedCount { get; private set; }

        public int EndedCount { get; private set; }

        public int FactCount { get; private set; }

        // ---- IDialogueHost ----

        public ConditionResult Evaluate(ConditionSetModel? condition) =>
            ConditionRules.Evaluate(condition, overlay, rt.Models, new ConditionContext(listener != 0 ? listener : rt.ActorKey, speaker));

        public int Fact(int factKey) => overlay.Fact(factKey);

        public void SetFact(int factKey, int value) => facts[factKey] = value;

        // ---- commands ----

        public DiagnosticCode Start(int graphKey, int speakerKey, int listenerKey)
        {
            if (Read(DialogueIds.Active, 0) != 0)
            {
                return DiagnosticCode.Ineligible;
            }

            if (!rt.Models.TryGetGraph(graphKey, out DialogueGraphModel? graph) || graph == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            listener = listenerKey != 0 ? listenerKey : rt.ActorKey;
            speaker = speakerKey != 0 ? speakerKey : graph.SpeakerKey;
            Events.Add(DialogueIds.StartedEvent, state, graph.Key, graph.Entry, speaker, listener, 0, 0);
            StartedCount++;
            Write(DialogueIds.Graph, graph.Key);
            Write(DialogueIds.Speaker, speaker);
            Write(DialogueIds.Listener, listener);
            Settle(graph, DialogueRules.Start(graph, this));
            return DiagnosticCode.None;
        }

        public DiagnosticCode Advance()
        {
            if (!TryActive(out DialogueGraphModel? graph) || graph == null)
            {
                return DiagnosticCode.Ineligible;
            }

            DialogueRefusal refusal = DialogueRules.Advance(graph, Read(DialogueIds.Node, -1), this, out DialogueStop? stop);
            if (refusal != DialogueRefusal.None || stop == null)
            {
                return DiagnosticCode.Ineligible;
            }

            Settle(graph, stop);
            return DiagnosticCode.None;
        }

        public DiagnosticCode Choose(int option)
        {
            if (!TryActive(out DialogueGraphModel? graph) || graph == null)
            {
                return DiagnosticCode.Ineligible;
            }

            int node = Read(DialogueIds.Node, -1);
            DialogueRefusal refusal = DialogueRules.Choose(graph, node, option, this, out DialogueStop? stop);
            if (refusal != DialogueRefusal.None || stop == null)
            {
                return DiagnosticCode.Ineligible;
            }

            int next = graph.IsNode(node) && option >= 0 && option < graph.Nodes[node].Options.Count ? graph.Nodes[node].Options[option].Next : -1;
            Events.Add(DialogueIds.ChoiceMadeEvent, state, graph.Key, node, option, next, listener, 0);
            Settle(graph, stop);
            return DiagnosticCode.None;
        }

        public DiagnosticCode Interrupt()
        {
            if (!TryActive(out DialogueGraphModel? graph) || graph == null)
            {
                return DiagnosticCode.Ineligible;
            }

            End(graph, Read(DialogueIds.Node, -1), DialogueIds.EndReasonInterrupt);
            return DiagnosticCode.None;
        }

        public DiagnosticCode SetFact(int factKey, int value, int request)
        {
            if (!rt.Index.TryFactSlot(factKey, out SlotId _))
            {
                return DiagnosticCode.MissingDependency;
            }

            if (RequestRing.IsTracked(request)
                && RequestRing.Contains(NarrativeSlots.ReadRing(rt.Registry, em, state, DialogueIds.Owner, DialogueIds.Req), request))
            {
                return DiagnosticCode.IdempotencyConflict;
            }

            requestId = request;
            facts[factKey] = value;
            EmitFacts(true);
            return DiagnosticCode.None;
        }

        /// <summary>Writes the gathered slots (after the events committed).</summary>
        public void Apply()
        {
            for (int i = 0; i < writes.Count; i++)
            {
                NarrativeSlots.Write(rt.Registry, em, state, DialogueIds.Owner, writes[i].Key, writes[i].Value);
            }

            for (int i = 0; i < graphWrites.Count; i++)
            {
                NarrativeSlots.Write(rt.Registry, em, graphWrites[i].Key, DialogueIds.Owner, graphWrites[i].Value.Key, graphWrites[i].Value.Value);
            }

            NarrativeSlots.PushRing(rt.Registry, em, state, DialogueIds.Owner, DialogueIds.ReqHead, DialogueIds.Req, requestId);
        }

        // ---- internals ----

        private bool TryActive(out DialogueGraphModel? graph)
        {
            graph = null;
            if (Read(DialogueIds.Active, 0) == 0)
            {
                return false;
            }

            listener = Read(DialogueIds.Listener, 0);
            speaker = Read(DialogueIds.Speaker, 0);
            return rt.Models.TryGetGraph(Read(DialogueIds.Graph, 0), out graph) && graph != null;
        }

        private void Settle(DialogueGraphModel graph, DialogueStop stop)
        {
            MarkVisited(graph, stop.Visited);
            for (int i = 0; i < stop.ActionNodes.Count; i++)
            {
                DialogueNodeModel node = graph.Nodes[stop.ActionNodes[i]];
                int set = node.Actions != null ? node.Actions.Key : 0;
                Events.Add(DialogueIds.ActionNodeEvent, state, graph.Key, node.Index, set, speaker, listener, 0);
                NarrativeActions.AddDue(Events, rt.Index.HubTarget, node.Actions, listener, speaker, LogicIds.SourceDialogue, true);
            }

            if (stop.HasEnded)
            {
                End(graph, stop.Node, stop.Ended == DialogueEndReason.DeadEnd ? DialogueIds.EndReasonDeadEnd : DialogueIds.EndReasonEndNode);
                return;
            }

            EmitFacts(false);
            int serial = Read(DialogueIds.Serial, 0) + 1;
            DialogueNodeModel at = graph.Nodes[stop.Node];
            int lineSpeaker = at.SpeakerKey != 0 ? at.SpeakerKey : speaker;
            Write(DialogueIds.Active, 1);
            Write(DialogueIds.Node, stop.Node);
            Write(DialogueIds.Serial, serial);
            Write(DialogueIds.ChoiceMask, stop.ChoiceMask);
            Write(DialogueIds.ChoiceCount, stop.ChoiceCount);
            if (stop.Kind == DialogueNodeKind.Choice)
            {
                Events.Add(DialogueIds.ChoiceOfferedEvent, state, graph.Key, stop.Node, stop.ChoiceCount, stop.ChoiceMask, serial, listener);
            }
            else
            {
                Events.Add(DialogueIds.LineShownEvent, state, graph.Key, stop.Node, lineSpeaker, listener, serial, 0);
            }
        }

        private void End(DialogueGraphModel graph, int lastNode, int reason)
        {
            foreach (FactModel fact in rt.Models.Facts)
            {
                if (!fact.Persistent && overlay.Fact(fact.Key) != fact.Initial)
                {
                    facts[fact.Key] = fact.Initial;
                }
            }

            EmitFacts(false);
            Write(DialogueIds.Active, 0);
            Write(DialogueIds.Node, -1);
            Write(DialogueIds.ChoiceMask, 0);
            Write(DialogueIds.ChoiceCount, 0);
            Write(DialogueIds.Serial, Read(DialogueIds.Serial, 0) + 1);
            Events.Add(DialogueIds.EndedEvent, state, graph.Key, lastNode, speaker, listener, reason, 0);
            EndedCount++;
        }

        /// <summary>One FactSet per changed fact (in key order); the request id travels only on narrative.setFact.</summary>
        private void EmitFacts(bool always)
        {
            var keys = new List<int>(facts.Keys);
            keys.Sort();
            var live = new LiveFactState(rt, em);
            for (int i = 0; i < keys.Count; i++)
            {
                int key = keys[i];
                if (!rt.Index.TryFactSlot(key, out SlotId slot))
                {
                    continue;
                }

                int previous = emitted.TryGetValue(key, out int earlier) ? earlier : live.Fact(key);
                int value = facts[key];
                if (value == previous && !always)
                {
                    continue;
                }

                Write(slot, value);
                emitted[key] = value;
                Events.Add(DialogueIds.FactSetEvent, state, key, value, previous, requestId, 0, 0);
                FactCount++;
            }
        }

        private void MarkVisited(DialogueGraphModel graph, IReadOnlyList<int> visited)
        {
            TargetId target = rt.Index.TargetOf(NarrativeTargetKind.Graph, graph.Key);
            if (target.IsDefault || visited.Count == 0)
            {
                return;
            }

            var words = new Dictionary<int, int>();
            for (int i = 0; i < visited.Count; i++)
            {
                int node = visited[i];
                int word = node / 32;
                if (!words.TryGetValue(word, out int bits))
                {
                    bits = PendingGraph(target, DialogueIds.Visited(word));
                }

                words[word] = bits | (1 << (node % 32));
            }

            foreach (KeyValuePair<int, int> word in words)
            {
                graphWrites.Add(new KeyValuePair<TargetId, KeyValuePair<SlotId, int>>(target, new KeyValuePair<SlotId, int>(DialogueIds.Visited(word.Key), word.Value)));
            }
        }

        private int PendingGraph(TargetId target, SlotId slot)
        {
            for (int i = graphWrites.Count - 1; i >= 0; i--)
            {
                if (graphWrites[i].Key.Equals(target) && graphWrites[i].Value.Key.Equals(slot))
                {
                    return graphWrites[i].Value.Value;
                }
            }

            return NarrativeSlots.Read(rt.Registry, em, target, DialogueIds.Owner, slot, 0);
        }

        private int Read(SlotId slot, int fallback)
        {
            for (int i = writes.Count - 1; i >= 0; i--)
            {
                if (writes[i].Key.Equals(slot))
                {
                    return writes[i].Value;
                }
            }

            return NarrativeSlots.Read(rt.Registry, em, state, DialogueIds.Owner, slot, fallback);
        }

        private void Write(SlotId slot, int value) => writes.Add(new KeyValuePair<SlotId, int>(slot, value));

        /// <summary>The facts as the state target holds them in this step (other state through the committed reader).</summary>
        private sealed class LiveFactState : IConditionState
        {
            private readonly NarrativeRuntime rt;
            private readonly EntityManager em;

            public LiveFactState(NarrativeRuntime runtime, EntityManager entityManager)
            {
                rt = runtime;
                em = entityManager;
            }

            public int NowMs => rt.State.NowMs;

            public int Fact(int factKey)
            {
                int initial = rt.Models.TryGetFact(factKey, out FactModel? fact) && fact != null ? fact.Initial : 0;
                return rt.Index.TryFactSlot(factKey, out SlotId slot)
                    ? NarrativeSlots.Read(rt.Registry, em, rt.Index.StateTarget, DialogueIds.Owner, slot, initial)
                    : initial;
            }

            public int ItemCount(int inventoryKey, int itemKey, int actorKey) => rt.State.ItemCount(inventoryKey, itemKey, actorKey);

            public int Currency(int inventoryKey, int actorKey) => rt.State.Currency(inventoryKey, actorKey);

            public int Quest(int questKey, QuestField field) => rt.State.Quest(questKey, field);

            public int ObjectiveDone(int questKey, int objective) => rt.State.ObjectiveDone(questKey, objective);

            public int RegionOf(int entityKey) => rt.State.RegionOf(entityKey);

            public int NodeVisited(int graphKey, int node) => rt.State.NodeVisited(graphKey, node);

            public int Slot(int entityKey, int slotRef) => rt.State.Slot(entityKey, slotRef);

            public int RuleFired(int ruleKey) => rt.State.RuleFired(ruleKey);
        }
    }

    /// <summary>The dialogue command stage.</summary>
    [DisableAutoCreation]
    public partial class DialogueCommandSystem : SystemBase
    {
        public DialogueModule? Module { get; set; }

        protected override void OnUpdate()
        {
            DialogueModule? module = Module;
            if (module != null)
            {
                module.Run(EntityManager);
            }
        }
    }
}
