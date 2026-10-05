// GameCore.Gameplay.Dialogue - the runtime half: conversation starter, presenter and voice line player (P1.4).
//
//   DialogueRunner     IConversationStarter (P1.3's NPC talk interaction calls TryStart) plus Choose/Advance/Interrupt:
//                      each submits one typed dialogue command; nothing changes until the next pump commits it
//   DialoguePresenter  reads the committed dialogue slots after each pump and pushes a DialogueViewModel to the
//                      IDialogueView (P1.5 renders it) whenever the conversation serial changes
//   VoiceLinePlayer    on a new committed line with a voice clip, asks the IVoiceLinePlayer to play it; stops it when
//                      the conversation ends
//
// Presentation reads committed slots only (Studio 02 s5): the presenters never look at the in-step state.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using Seams = GameCore.Gameplay.Contracts.Narrative;

namespace GameCore.Gameplay.Dialogue
{
    /// <summary>Starts and drives conversations with typed commands.</summary>
    public sealed class DialogueRunner : Seams.IConversationStarter
    {
        private readonly NarrativeRuntime runtime;

        public DialogueRunner(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Submitted { get; private set; }

        public int Refused { get; private set; }

        /// <summary>
        /// Submits dialogue.start for <paramref name="dialogueGraphRef"/> (authoring id or name) with the NPC as speaker
        /// and the player as listener. False when the graph is unknown or the command was not admitted.
        /// </summary>
        public bool TryStart(string npcAuthoringId, string dialogueGraphRef)
        {
            if (!runtime.Models.TryResolve(dialogueGraphRef ?? string.Empty, out int key) || !runtime.Models.TryGetGraph(key, out DialogueGraphModel? graph) || graph == null)
            {
                Refused++;
                return false;
            }

            int speaker = NarrativeRefs.EntityKey(npcAuthoringId);
            return Count(runtime.Submitter.Submit(DialogueIds.StartRoute, runtime.Index.StateTarget, DialogueIds.StartCommand,
                NarrativeCommands.DialogueStart(graph.Key, speaker != 0 ? speaker : graph.SpeakerKey, runtime.ActorKey)));
        }

        public bool Choose(int option) =>
            Count(runtime.Submitter.Submit(DialogueIds.ChooseRoute, runtime.Index.StateTarget, DialogueIds.ChooseCommand, NarrativeCommands.DialogueChoose(option)));

        public bool Advance() =>
            Count(runtime.Submitter.Submit(DialogueIds.AdvanceRoute, runtime.Index.StateTarget, DialogueIds.AdvanceCommand, NarrativeCommands.DialogueAdvance()));

        public bool Interrupt() =>
            Count(runtime.Submitter.Submit(DialogueIds.InterruptRoute, runtime.Index.StateTarget, DialogueIds.InterruptCommand, NarrativeCommands.DialogueInterrupt()));

        /// <summary>Sets a fact directly (Studio, tests); <paramref name="requestId"/> 0 = untracked.</summary>
        public bool SetFact(string factName, int value, int requestId) =>
            runtime.Models.TryGetFactByName(factName, out FactModel? fact) && fact != null
            && Count(runtime.Submitter.Submit(DialogueIds.SetFactRoute, runtime.Index.StateTarget, DialogueIds.SetFactCommand,
                NarrativeCommands.SetFact(fact.Key, value, requestId)));

        private bool Count(CommandAdmissionReceipt receipt)
        {
            if (receipt.Admitted)
            {
                Submitted++;
            }
            else
            {
                Refused++;
            }

            return receipt.Admitted;
        }
    }

    /// <summary>The committed conversation as a view model.</summary>
    public static class DialogueViews
    {
        public static DialogueViewModel Read(NarrativeRuntime runtime, ICommittedSlotReader slots)
        {
            TargetId state = runtime.Index.StateTarget;
            OwnerId owner = DialogueIds.Owner;
            int serial = slots.ReadOrDefault(state, owner, DialogueIds.Serial, 0);
            if (slots.ReadOrDefault(state, owner, DialogueIds.Active, 0) == 0
                || !runtime.Models.TryGetGraph(slots.ReadOrDefault(state, owner, DialogueIds.Graph, 0), out DialogueGraphModel? graph) || graph == null)
            {
                return new DialogueViewModel(false, 0, string.Empty, -1, string.Empty, string.Empty, string.Empty, null, string.Empty, false,
                    Array.Empty<DialogueChoiceView>(), serial);
            }

            int nodeIndex = slots.ReadOrDefault(state, owner, DialogueIds.Node, -1);
            if (!graph.IsNode(nodeIndex))
            {
                return DialogueViewModel.Inactive;
            }

            DialogueNodeModel node = graph.Nodes[nodeIndex];
            int mask = slots.ReadOrDefault(state, owner, DialogueIds.ChoiceMask, 0);
            var choices = new List<DialogueChoiceView>();
            if (node.Kind == DialogueNodeKind.Choice)
            {
                for (int i = 0; i < node.Options.Count; i++)
                {
                    bool available = (mask & (1 << i)) != 0;
                    if (!available && node.Options[i].HideWhenUnavailable)
                    {
                        continue;
                    }

                    choices.Add(new DialogueChoiceView(i, node.Options[i].Text, available,
                        available ? string.Empty : (node.Options[i].Condition != null ? node.Options[i].Condition!.Name : string.Empty)));
                }
            }

            string speaker = node.Speaker.Length > 0 ? node.Speaker : graph.Speaker;
            return new DialogueViewModel(true, graph.Key, graph.Name, nodeIndex, node.Kind == DialogueNodeKind.Choice ? "choice" : "line",
                speaker, node.Text, null, node.PortraitRef, node.Kind == DialogueNodeKind.Line, choices, serial);
        }
    }

    /// <summary>Pushes the committed conversation to an IDialogueView.</summary>
    public sealed class DialoguePresenter : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private int lastSerial = -1;

        public DialoguePresenter(NarrativeRuntime runtime, IDialogueView view)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            View = view ?? new NullDialogueView();
        }

        public IDialogueView View { get; set; }

        public string BinderName => "dialogue.presenter";

        public bool IsActive => true;

        public DialogueViewModel Last { get; private set; } = DialogueViewModel.Inactive;

        public int Pushes { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            int serial = slots.ReadOrDefault(runtime.Index.StateTarget, DialogueIds.Owner, DialogueIds.Serial, 0);
            if (serial == lastSerial)
            {
                return 0;
            }

            lastSerial = serial;
            Last = DialogueViews.Read(runtime, slots);
            View.Show(Last);
            Pushes++;
            return 1;
        }
    }

    /// <summary>Plays the voice clip of each newly committed line through an IVoiceLinePlayer.</summary>
    public sealed class VoiceLinePlayer : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private int lastSerial = -1;
        private bool speaking;

        public VoiceLinePlayer(NarrativeRuntime runtime, IVoiceLinePlayer player)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Player = player ?? new NullVoiceLinePlayer();
        }

        public IVoiceLinePlayer Player { get; set; }

        public string BinderName => "dialogue.voice";

        public bool IsActive => true;

        public int Played { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            TargetId state = runtime.Index.StateTarget;
            int serial = slots.ReadOrDefault(state, DialogueIds.Owner, DialogueIds.Serial, 0);
            if (serial == lastSerial)
            {
                return 0;
            }

            lastSerial = serial;
            if (speaking)
            {
                Player.Stop("next line");
                speaking = false;
            }

            if (slots.ReadOrDefault(state, DialogueIds.Owner, DialogueIds.Active, 0) == 0
                || !runtime.Models.TryGetGraph(slots.ReadOrDefault(state, DialogueIds.Owner, DialogueIds.Graph, 0), out DialogueGraphModel? graph) || graph == null)
            {
                return 0;
            }

            int node = slots.ReadOrDefault(state, DialogueIds.Owner, DialogueIds.Node, -1);
            if (!graph.IsNode(node) || graph.Nodes[node].Kind != DialogueNodeKind.Line || graph.Nodes[node].VoiceClipRef.Length == 0)
            {
                return 0;
            }

            DialogueNodeModel line = graph.Nodes[node];
            Player.Play(new VoiceLineRequest(graph.Key, node, line.Speaker.Length > 0 ? line.Speaker : graph.Speaker, line.Text, line.VoiceClipRef, null));
            speaking = true;
            Played++;
            return 1;
        }
    }
}
