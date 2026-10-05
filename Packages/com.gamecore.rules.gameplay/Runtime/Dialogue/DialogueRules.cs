// GameCore.Rules.Gameplay.Dialogue - pure dialogue graph rules (P1.4, catalog row 6).
//
// A dialogue graph is a list of nodes: line (speaker + text), choice (options, each with an optional condition), branch
// (condition -> next / else), action (an action set: its fact actions apply immediately, the rest are delivered once by
// the outbox) and end. Branch and action nodes are resolved automatically; a conversation always rests on a line or a
// choice, or it has ended. The same walker runs in the dialogue stage (over live slots), in the presenter and in the
// dialogue.preview tool (over a fact table), so the preview shows exactly the path the game would take.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Rules.Gameplay.Logic;

namespace GameCore.Rules.Gameplay.Dialogue
{
    /// <summary>The kind of a dialogue node.</summary>
    public enum DialogueNodeKind
    {
        Line = 0,
        Choice = 1,
        Branch = 2,
        Action = 3,
        End = 4,
    }

    /// <summary>One option of a choice node.</summary>
    public sealed class DialogueOptionModel
    {
        public DialogueOptionModel(string text, ConditionSetModel? condition, int next, bool hideWhenUnavailable)
        {
            Text = text ?? string.Empty;
            Condition = condition;
            Next = next;
            HideWhenUnavailable = hideWhenUnavailable;
        }

        public string Text { get; }

        /// <summary>Null = always available.</summary>
        public ConditionSetModel? Condition { get; }

        /// <summary>The node the option leads to (-1 = end the conversation).</summary>
        public int Next { get; }

        public bool HideWhenUnavailable { get; }
    }

    /// <summary>One node of a dialogue graph.</summary>
    public sealed class DialogueNodeModel
    {
        public DialogueNodeModel(
            int index,
            DialogueNodeKind kind,
            int speakerKey,
            string speaker,
            string text,
            string voiceClipRef,
            string portraitRef,
            ConditionSetModel? condition,
            ActionSetModel? actions,
            int next,
            int elseNext,
            IReadOnlyList<DialogueOptionModel>? options)
        {
            Index = index;
            Kind = kind;
            SpeakerKey = speakerKey;
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            VoiceClipRef = voiceClipRef ?? string.Empty;
            PortraitRef = portraitRef ?? string.Empty;
            Condition = condition;
            Actions = actions;
            Next = next;
            ElseNext = elseNext;
            Options = options ?? Array.Empty<DialogueOptionModel>();
        }

        public int Index { get; }

        public DialogueNodeKind Kind { get; }

        public int SpeakerKey { get; }

        public string Speaker { get; }

        public string Text { get; }

        public string VoiceClipRef { get; }

        public string PortraitRef { get; }

        /// <summary>Branch: the condition choosing Next (holds) or ElseNext.</summary>
        public ConditionSetModel? Condition { get; }

        /// <summary>Action: the (flattened) action set.</summary>
        public ActionSetModel? Actions { get; }

        /// <summary>Line/action/branch-true successor (-1 = end).</summary>
        public int Next { get; }

        /// <summary>Branch-false successor (-1 = end).</summary>
        public int ElseNext { get; }

        public IReadOnlyList<DialogueOptionModel> Options { get; }
    }

    /// <summary>A dialogue graph baked to a model.</summary>
    public sealed class DialogueGraphModel
    {
        public DialogueGraphModel(int key, string name, int entry, IReadOnlyList<DialogueNodeModel> nodes, int speakerKey, string speaker)
        {
            Key = key;
            Name = name ?? string.Empty;
            Entry = entry;
            Nodes = nodes ?? Array.Empty<DialogueNodeModel>();
            SpeakerKey = speakerKey;
            Speaker = speaker ?? string.Empty;
        }

        public int Key { get; }

        public string Name { get; }

        public int Entry { get; }

        public IReadOnlyList<DialogueNodeModel> Nodes { get; }

        /// <summary>Default speaker of the graph (its NPC).</summary>
        public int SpeakerKey { get; }

        public string Speaker { get; }

        /// <summary>Words of dialogue.visited.&lt;w&gt; the graph needs (32 nodes per word).</summary>
        public int VisitedWords => (Nodes.Count + 31) / 32;

        public bool IsNode(int index) => index >= 0 && index < Nodes.Count;
    }

    /// <summary>What the walker asks its host: condition evaluation and immediate fact writes.</summary>
    public interface IDialogueHost
    {
        ConditionResult Evaluate(ConditionSetModel? condition);

        int Fact(int factKey);

        void SetFact(int factKey, int value);
    }

    /// <summary>Why a conversation ended.</summary>
    public enum DialogueEndReason
    {
        /// <summary>Not ended.</summary>
        None = -1,
        EndNode = 0,
        Interrupt = 1,
        DeadEnd = 2,
    }

    /// <summary>Where the walker stopped: a line, a choice, or the end; and what it passed on the way.</summary>
    public sealed class DialogueStop
    {
        public DialogueStop(int node, DialogueNodeKind kind, int choiceMask, DialogueEndReason ended, IReadOnlyList<int> visited, IReadOnlyList<int> actionNodes, IReadOnlyList<string> trace)
        {
            Node = node;
            Kind = kind;
            ChoiceMask = choiceMask;
            Ended = ended;
            Visited = visited;
            ActionNodes = actionNodes;
            Trace = trace;
        }

        /// <summary>The resting node (line or choice), or the last node passed when ended (-1 = none).</summary>
        public int Node { get; }

        public DialogueNodeKind Kind { get; }

        /// <summary>Bit i = option i is available (choice nodes).</summary>
        public int ChoiceMask { get; }

        public DialogueEndReason Ended { get; }

        public bool HasEnded => Ended != DialogueEndReason.None;

        /// <summary>Every node passed, in order (including the resting node).</summary>
        public IReadOnlyList<int> Visited { get; }

        /// <summary>The action nodes passed, in order (their fact actions were applied through the host).</summary>
        public IReadOnlyList<int> ActionNodes { get; }

        /// <summary>One readable line per node passed.</summary>
        public IReadOnlyList<string> Trace { get; }

        public int ChoiceCount
        {
            get
            {
                int count = 0;
                for (int mask = ChoiceMask; mask != 0; mask &= mask - 1)
                {
                    count++;
                }

                return count;
            }
        }
    }

    /// <summary>Why a dialogue command was refused.</summary>
    public enum DialogueRefusal
    {
        None = 0,
        NotStarted = 1,
        AlreadyActive = 2,
        BadNode = 3,
        AdvanceAtChoice = 4,
        ChoiceUnavailable = 5,
        NotAtChoice = 6,
    }

    /// <summary>Pure dialogue walking.</summary>
    public static class DialogueRules
    {
        /// <summary>Most automatic nodes resolved in one walk (cycles of branches end as a dead end).</summary>
        public const int MaxAutoSteps = 64;

        /// <summary>Most options a choice may hold (choice mask bits).</summary>
        public const int MaxOptions = 8;

        /// <summary>Most nodes a graph may hold.</summary>
        public const int MaxNodes = 128;

        /// <summary>Walks from <paramref name="start"/> through branch and action nodes to the next line, choice or end.</summary>
        public static DialogueStop Resolve(DialogueGraphModel graph, int start, IDialogueHost host)
        {
            var visited = new List<int>();
            var actions = new List<int>();
            var trace = new List<string>();
            int current = start;
            int last = -1;
            for (int step = 0; step < MaxAutoSteps; step++)
            {
                if (!graph.IsNode(current))
                {
                    return new DialogueStop(last, DialogueNodeKind.End, 0, current < 0 ? DialogueEndReason.EndNode : DialogueEndReason.DeadEnd, visited, actions, trace);
                }

                DialogueNodeModel node = graph.Nodes[current];
                visited.Add(current);
                last = current;
                switch (node.Kind)
                {
                    case DialogueNodeKind.Line:
                        trace.Add(Label(node) + Who(node, graph) + ": \"" + node.Text + "\"");
                        return new DialogueStop(current, DialogueNodeKind.Line, 0, DialogueEndReason.None, visited, actions, trace);
                    case DialogueNodeKind.Choice:
                    {
                        int mask = ChoiceMask(node, host);
                        trace.Add(Label(node) + "choice " + DescribeOptions(node, mask));
                        if (mask == 0)
                        {
                            return new DialogueStop(current, DialogueNodeKind.End, 0, DialogueEndReason.DeadEnd, visited, actions, trace);
                        }

                        return new DialogueStop(current, DialogueNodeKind.Choice, mask, DialogueEndReason.None, visited, actions, trace);
                    }

                    case DialogueNodeKind.Branch:
                    {
                        ConditionResult result = host.Evaluate(node.Condition);
                        trace.Add(Label(node) + "branch " + Describe(node.Condition) + " -> " + (result.Passed ? "yes" : "no"));
                        current = result.Passed ? node.Next : node.ElseNext;
                        break;
                    }

                    case DialogueNodeKind.Action:
                    {
                        actions.Add(current);
                        trace.Add(Label(node) + "action " + DescribeActions(node.Actions));
                        ApplyFacts(node.Actions, host);
                        current = node.Next;
                        break;
                    }

                    default:
                        trace.Add(Label(node) + "end");
                        return new DialogueStop(current, DialogueNodeKind.End, 0, DialogueEndReason.EndNode, visited, actions, trace);
                }
            }

            return new DialogueStop(last, DialogueNodeKind.End, 0, DialogueEndReason.DeadEnd, visited, actions, trace);
        }

        /// <summary>Starts a conversation at the graph's entry.</summary>
        public static DialogueStop Start(DialogueGraphModel graph, IDialogueHost host) => Resolve(graph, graph.Entry, host);

        /// <summary>dialogue.advance from a line.</summary>
        public static DialogueRefusal Advance(DialogueGraphModel graph, int node, IDialogueHost host, out DialogueStop? stop)
        {
            stop = null;
            if (!graph.IsNode(node))
            {
                return DialogueRefusal.BadNode;
            }

            DialogueNodeModel current = graph.Nodes[node];
            if (current.Kind == DialogueNodeKind.Choice)
            {
                return DialogueRefusal.AdvanceAtChoice;
            }

            if (current.Kind != DialogueNodeKind.Line)
            {
                return DialogueRefusal.BadNode;
            }

            stop = Resolve(graph, current.Next, host);
            return DialogueRefusal.None;
        }

        /// <summary>dialogue.choose an option of a choice; the option's condition is re-evaluated now.</summary>
        public static DialogueRefusal Choose(DialogueGraphModel graph, int node, int option, IDialogueHost host, out DialogueStop? stop)
        {
            stop = null;
            if (!graph.IsNode(node))
            {
                return DialogueRefusal.BadNode;
            }

            DialogueNodeModel current = graph.Nodes[node];
            if (current.Kind != DialogueNodeKind.Choice)
            {
                return DialogueRefusal.NotAtChoice;
            }

            if (option < 0 || option >= current.Options.Count || option >= MaxOptions || (ChoiceMask(current, host) & (1 << option)) == 0)
            {
                return DialogueRefusal.ChoiceUnavailable;
            }

            stop = Resolve(graph, current.Options[option].Next, host);
            return DialogueRefusal.None;
        }

        /// <summary>Bit i set = option i's condition holds now.</summary>
        public static int ChoiceMask(DialogueNodeModel node, IDialogueHost host)
        {
            int mask = 0;
            for (int i = 0; i < node.Options.Count && i < MaxOptions; i++)
            {
                if (host.Evaluate(node.Options[i].Condition).Passed)
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        /// <summary>Applies the fact actions of an action node through the host (the rest are for the outbox).</summary>
        public static int ApplyFacts(ActionSetModel? actions, IDialogueHost host)
        {
            if (actions == null)
            {
                return 0;
            }

            int applied = 0;
            for (int i = 0; i < actions.Actions.Count; i++)
            {
                ActionModel action = actions.Actions[i];
                if (!ActionRules.IsFactAction(action.Kind))
                {
                    continue;
                }

                host.SetFact(action.Key, ActionRules.FactValueAfter(action, host.Fact(action.Key)));
                applied++;
            }

            return applied;
        }

        /// <summary>The nodes reachable from the entry when every condition may go either way (graph validation).</summary>
        public static HashSet<int> Reachable(DialogueGraphModel graph)
        {
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            if (graph.IsNode(graph.Entry))
            {
                stack.Push(graph.Entry);
            }

            while (stack.Count > 0)
            {
                int index = stack.Pop();
                if (!graph.IsNode(index) || !seen.Add(index))
                {
                    continue;
                }

                DialogueNodeModel node = graph.Nodes[index];
                stack.Push(node.Next);
                if (node.Kind == DialogueNodeKind.Branch)
                {
                    stack.Push(node.ElseNext);
                }

                for (int i = 0; i < node.Options.Count; i++)
                {
                    stack.Push(node.Options[i].Next);
                }
            }

            return seen;
        }

        /// <summary>Structural problems of a graph: bad entry, dangling edges, too many options or nodes, unreachable nodes.</summary>
        public static List<string> Validate(DialogueGraphModel graph)
        {
            var problems = new List<string>();
            if (graph.Nodes.Count == 0)
            {
                problems.Add("empty: the graph has no nodes");
                return problems;
            }

            if (graph.Nodes.Count > MaxNodes)
            {
                problems.Add("too many nodes: " + graph.Nodes.Count.ToString(CultureInfo.InvariantCulture) + " > " + MaxNodes.ToString(CultureInfo.InvariantCulture));
            }

            if (!graph.IsNode(graph.Entry))
            {
                problems.Add("bad entry: node " + graph.Entry.ToString(CultureInfo.InvariantCulture) + " does not exist");
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                DialogueNodeModel node = graph.Nodes[i];
                CheckEdge(graph, i, "next", node.Next, problems);
                if (node.Kind == DialogueNodeKind.Branch)
                {
                    CheckEdge(graph, i, "else", node.ElseNext, problems);
                }

                if (node.Kind == DialogueNodeKind.Choice)
                {
                    if (node.Options.Count == 0 || node.Options.Count > MaxOptions)
                    {
                        problems.Add("node " + i.ToString(CultureInfo.InvariantCulture) + ": a choice needs 1.." + MaxOptions.ToString(CultureInfo.InvariantCulture) + " options");
                    }

                    for (int o = 0; o < node.Options.Count; o++)
                    {
                        CheckEdge(graph, i, "option " + o.ToString(CultureInfo.InvariantCulture), node.Options[o].Next, problems);
                    }
                }
            }

            HashSet<int> reachable = Reachable(graph);
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (!reachable.Contains(i))
                {
                    problems.Add("unreachable: node " + i.ToString(CultureInfo.InvariantCulture));
                }
            }

            return problems;
        }

        private static void CheckEdge(DialogueGraphModel graph, int node, string what, int target, List<string> problems)
        {
            if (target < -1 || target >= graph.Nodes.Count)
            {
                problems.Add("dangling: node " + node.ToString(CultureInfo.InvariantCulture) + " " + what + " -> " + target.ToString(CultureInfo.InvariantCulture));
            }
        }

        internal static string Label(DialogueNodeModel node) => "[" + node.Index.ToString(CultureInfo.InvariantCulture) + "] ";

        internal static string Who(DialogueNodeModel node, DialogueGraphModel graph) =>
            node.Speaker.Length > 0 ? node.Speaker : (graph.Speaker.Length > 0 ? graph.Speaker : "?");

        internal static string Describe(ConditionSetModel? condition)
        {
            if (condition == null || condition.Conditions.Count == 0)
            {
                return "(always)";
            }

            if (condition.Name.Length > 0 && condition.Conditions.Count > 1)
            {
                return condition.Name;
            }

            var parts = new List<string>();
            for (int i = 0; i < condition.Conditions.Count; i++)
            {
                parts.Add(condition.Conditions[i].Describe());
            }

            return string.Join(condition.Mode == ConditionMode.All ? " and " : " or ", parts);
        }

        internal static string DescribeActions(ActionSetModel? actions)
        {
            if (actions == null || actions.Actions.Count == 0)
            {
                return "(none)";
            }

            var parts = new List<string>();
            for (int i = 0; i < actions.Actions.Count; i++)
            {
                parts.Add(actions.Actions[i].Describe());
            }

            return string.Join(", ", parts);
        }

        internal static string DescribeOptions(DialogueNodeModel node, int mask)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < node.Options.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                bool available = (mask & (1 << i)) != 0;
                builder.Append(i.ToString(CultureInfo.InvariantCulture)).Append(available ? ") " : "x) ").Append('"').Append(node.Options[i].Text).Append('"');
            }

            return builder.ToString();
        }
    }

    /// <summary>A fact table host over a condition state (previews, tests): fact writes overlay the state.</summary>
    public sealed class FactTableHost : IDialogueHost
    {
        private readonly Dictionary<int, int> facts;
        private readonly IConditionState state;
        private readonly IConditionSetLookup? sets;
        private readonly ConditionContext context;

        public FactTableHost(IConditionState baseState, IConditionSetLookup? sets, IDictionary<int, int>? overrides, in ConditionContext context)
        {
            facts = overrides != null ? new Dictionary<int, int>(overrides) : new Dictionary<int, int>();
            state = new FactOverlayState(baseState, facts);
            this.sets = sets;
            this.context = context;
        }

        public IReadOnlyDictionary<int, int> Overrides => facts;

        public ConditionResult Evaluate(ConditionSetModel? condition) => ConditionRules.Evaluate(condition, state, sets, context);

        public int Fact(int factKey) => state.Fact(factKey);

        public void SetFact(int factKey, int value) => facts[factKey] = value;

        public FactTableHost Fork(IConditionState baseState) => new FactTableHost(baseState, sets, facts, context);
    }

    /// <summary>dialogue.preview: every path reachable for a given fact state, as text.</summary>
    public static class DialoguePreview
    {
        /// <summary>Most lines a preview prints.</summary>
        public const int MaxLines = 400;

        /// <summary>
        /// Walks the graph from its entry with <paramref name="facts"/> applied over <paramref name="baseState"/>. Lines,
        /// branches and actions are printed as the game resolves them; at a choice, every available option is followed
        /// (each with its own copy of the facts) and unavailable ones are listed with an x. A node already shown on the
        /// current path is printed as a loop marker instead of being walked again.
        /// </summary>
        public static string Preview(DialogueGraphModel graph, IConditionState baseState, IConditionSetLookup? sets, IDictionary<int, int>? facts)
        {
            var lines = new List<string> { "graph " + graph.Name + " (entry " + graph.Entry.ToString(CultureInfo.InvariantCulture) + ")" };
            var host = new FactTableHost(baseState, sets, facts, new ConditionContext(0, graph.SpeakerKey));
            Walk(graph, graph.Entry, host, baseState, 1, new HashSet<int>(), lines);
            if (lines.Count >= MaxLines)
            {
                lines.Add("... (truncated)");
            }

            return string.Join("\n", lines);
        }

        private static void Walk(DialogueGraphModel graph, int start, FactTableHost host, IConditionState baseState, int depth, HashSet<int> path, List<string> lines)
        {
            string indent = new string(' ', depth * 2);
            int current = start;
            var local = new HashSet<int>(path);
            while (lines.Count < MaxLines)
            {
                DialogueStop stop = DialogueRules.Resolve(graph, current, host);
                for (int i = 0; i < stop.Trace.Count; i++)
                {
                    lines.Add(indent + stop.Trace[i]);
                }

                for (int i = 0; i < stop.Visited.Count; i++)
                {
                    local.Add(stop.Visited[i]);
                }

                if (stop.HasEnded)
                {
                    if (stop.Ended != DialogueEndReason.EndNode || stop.Kind != DialogueNodeKind.End || !graph.IsNode(stop.Node) || graph.Nodes[stop.Node].Kind != DialogueNodeKind.End)
                    {
                        lines.Add(indent + "(conversation ends: " + stop.Ended + ")");
                    }

                    return;
                }

                DialogueNodeModel node = graph.Nodes[stop.Node];
                if (stop.Kind == DialogueNodeKind.Line)
                {
                    if (local.Contains(node.Next))
                    {
                        lines.Add(indent + "(loops back to [" + node.Next.ToString(CultureInfo.InvariantCulture) + "])");
                        return;
                    }

                    current = node.Next;
                    if (current < 0)
                    {
                        lines.Add(indent + "(conversation ends)");
                        return;
                    }

                    continue;
                }

                for (int o = 0; o < node.Options.Count && lines.Count < MaxLines; o++)
                {
                    DialogueOptionModel option = node.Options[o];
                    bool available = (stop.ChoiceMask & (1 << o)) != 0;
                    if (!available)
                    {
                        continue;
                    }

                    lines.Add(indent + "  choose " + o.ToString(CultureInfo.InvariantCulture) + " \"" + option.Text + "\":");
                    if (local.Contains(option.Next))
                    {
                        lines.Add(indent + "    (loops back to [" + option.Next.ToString(CultureInfo.InvariantCulture) + "])");
                        continue;
                    }

                    Walk(graph, option.Next, host.Fork(baseState), baseState, depth + 2, local, lines);
                }

                return;
            }
        }
    }
}
