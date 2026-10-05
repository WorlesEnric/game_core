// GameCore.Studio.Views - W-VIEW-02 model: a dialogue graph read generically (no gameplay type dependency) and the
// change sets the Dialogue view makes.
//
// The document is read through the authoring metadata (DialogueGraphDefinition's [AuthorField]s nodes, edges, entry,
// speaker, npcGraphRef) with the same value codec the engine writes with. Edits are operations of existing tools:
//   dialogue.addLine / dialogue.addChoice   new line / choice nodes (P1.4)
//   dialogue.linkCondition                  a branch condition or a choice option's availability condition
//   set nodes                               text, speaker and option renames: one partial element ({} = unchanged)
//   set edges                               connect / disconnect / re-target a port (the whole edge list)
//   set {nodes, edges}                      remove a node (shifts the later nodes and re-indexes the edges)
//   delete                                  delete the whole graph
// There is no dialogue.link / dialogue.removeNode / dialogue.setLine tool; the generic set equivalents are listed in
// PACKET.md for P3.1. Every operation targets the graph with the stamp the document was read at, so a concurrent edit
// is reported as a conflict instead of being overwritten.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    public sealed class DialogueOption
    {
        public DialogueOption(int index, string text, AuthoringRef? condition, bool hideWhenUnavailable)
        {
            Index = index;
            Text = text;
            Condition = condition;
            HideWhenUnavailable = hideWhenUnavailable;
        }

        public int Index { get; }

        public string Text { get; }

        public AuthoringRef? Condition { get; }

        public bool HideWhenUnavailable { get; }
    }

    public sealed class DialogueNode
    {
        public DialogueNode(int index, string kind, string speaker, string speakerEntityId, string text, AuthoringRef? condition, AuthoringRef? actions, IReadOnlyList<DialogueOption> options)
        {
            Index = index;
            Kind = kind;
            Speaker = speaker;
            SpeakerEntityId = speakerEntityId;
            Text = text;
            Condition = condition;
            Actions = actions;
            Options = options;
        }

        public int Index { get; }

        /// <summary>Line, Choice, Branch, Action or End (the enum member name).</summary>
        public string Kind { get; }

        public string Speaker { get; }

        public string SpeakerEntityId { get; }

        public string Text { get; }

        public AuthoringRef? Condition { get; }

        public AuthoringRef? Actions { get; }

        public IReadOnlyList<DialogueOption> Options { get; }

        public bool Is(string kind) => string.Equals(Kind, kind, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DialogueEdgeInfo
    {
        public DialogueEdgeInfo(int index, int from, string port, int option, int to)
        {
            Index = index;
            From = from;
            Port = port;
            Option = option;
            To = to;
        }

        public int Index { get; }

        public int From { get; }

        /// <summary>Next, Else or Option.</summary>
        public string Port { get; }

        public int Option { get; }

        /// <summary>Target node index; -1 ends the conversation.</summary>
        public int To { get; }

        public string Label => string.Equals(Port, DialogueDocument.PortOption, StringComparison.OrdinalIgnoreCase)
            ? "option " + (Option + 1).ToString(CultureInfo.InvariantCulture)
            : Port.ToLowerInvariant();

        public JObject ToJson() => new JObject { ["from"] = From, ["port"] = Port, ["option"] = Option, ["to"] = To };
    }

    /// <summary>One dialogue graph as the view shows it.</summary>
    public sealed class DialogueDocument
    {
        public const string Type = "dialogue.graph";
        public const string PortNext = "Next";
        public const string PortElse = "Else";
        public const string PortOption = "Option";

        private DialogueDocument(AuthoringRef reference, string name, string authoringId, int entry, string speaker, string speakerEntityId, string npcGraphRef, JArray rawNodes, JArray rawEdges)
        {
            Ref = reference;
            Name = name;
            AuthoringId = authoringId;
            Entry = entry;
            Speaker = speaker;
            SpeakerEntityId = speakerEntityId;
            NpcGraphRef = npcGraphRef;
            RawNodes = rawNodes;
            RawEdges = rawEdges;
            List<DialogueNode> nodes = new List<DialogueNode>();
            for (int i = 0; i < rawNodes.Count; i++)
            {
                JObject node = rawNodes[i] as JObject ?? new JObject();
                List<DialogueOption> options = new List<DialogueOption>();
                if (node["options"] is JArray rawOptions)
                {
                    for (int o = 0; o < rawOptions.Count; o++)
                    {
                        JObject option = rawOptions[o] as JObject ?? new JObject();
                        options.Add(new DialogueOption(o, AuthoredData.Text(option["text"]), AuthoredData.Ref(option["condition"]), AuthoredData.Bool(option["hideWhenUnavailable"])));
                    }
                }

                nodes.Add(new DialogueNode(
                    i,
                    AuthoredData.Text(node["kind"]),
                    AuthoredData.Text(node["speaker"]),
                    AuthoredData.Text(node["speakerEntityId"]),
                    AuthoredData.Text(node["text"]),
                    AuthoredData.Ref(node["condition"]),
                    AuthoredData.Ref(node["actions"]),
                    options));
            }

            List<DialogueEdgeInfo> edges = new List<DialogueEdgeInfo>();
            for (int i = 0; i < rawEdges.Count; i++)
            {
                JObject edge = rawEdges[i] as JObject ?? new JObject();
                edges.Add(new DialogueEdgeInfo(i, AuthoredData.Int(edge["from"]), Normalise(AuthoredData.Text(edge["port"])), AuthoredData.Int(edge["option"]), AuthoredData.Int(edge["to"], -1)));
            }

            Nodes = nodes;
            Edges = edges;
        }

        /// <summary>The graph's ref with the stamp it was read at (edits conflict when the graph changed since).</summary>
        public AuthoringRef Ref { get; }

        public string Name { get; }

        public string AuthoringId { get; }

        public int Entry { get; }

        public string Speaker { get; }

        public string SpeakerEntityId { get; }

        public string NpcGraphRef { get; }

        public IReadOnlyList<DialogueNode> Nodes { get; }

        public IReadOnlyList<DialogueEdgeInfo> Edges { get; }

        public JArray RawNodes { get; }

        public JArray RawEdges { get; }

        /// <summary>Loads the graph behind <paramref name="reference"/>; null when it does not resolve to a dialogue graph.</summary>
        public static DialogueDocument? Load(StudioRuntime runtime, AuthoringRef reference)
        {
            UnityEngine.Object? target = AuthoredData.Resolve(runtime, reference, out AuthoringRef? current);
            if (target == null || current == null || !string.Equals(AuthoredData.TypeOf(runtime, target), Type, StringComparison.Ordinal))
            {
                return null;
            }

            return new DialogueDocument(
                current,
                target.name,
                current.AuthoringId ?? string.Empty,
                AuthoredData.Int(AuthoredData.Read(runtime, target, "entry")),
                AuthoredData.Text(AuthoredData.Read(runtime, target, "speaker")),
                AuthoredData.Text(AuthoredData.Read(runtime, target, "speakerEntityId")),
                AuthoredData.Text(AuthoredData.Read(runtime, target, "npcGraphRef")),
                AuthoredData.Read(runtime, target, "nodes") as JArray ?? new JArray(),
                AuthoredData.Read(runtime, target, "edges") as JArray ?? new JArray());
        }

        /// <summary>A document over explicit data (tests, tools that already hold the JSON).</summary>
        public static DialogueDocument FromData(AuthoringRef reference, string name, int entry, JArray nodes, JArray edges, string speaker = "", string npcGraphRef = "")
        {
            return new DialogueDocument(reference, name, reference.AuthoringId ?? string.Empty, entry, speaker, string.Empty, npcGraphRef, nodes, edges);
        }

        public IReadOnlyList<DialogueEdgeInfo> EdgesFrom(int node)
        {
            List<DialogueEdgeInfo> edges = new List<DialogueEdgeInfo>();
            foreach (DialogueEdgeInfo edge in Edges)
            {
                if (edge.From == node)
                {
                    edges.Add(edge);
                }
            }

            return edges;
        }

        /// <summary>The port a drag-connect from <paramref name="from"/> uses: the first free option, else Else on a branch, else Next.</summary>
        public void DefaultPort(int from, out string port, out int option)
        {
            option = 0;
            port = PortNext;
            if (from < 0 || from >= Nodes.Count)
            {
                return;
            }

            DialogueNode node = Nodes[from];
            if (node.Is("Choice"))
            {
                port = PortOption;
                for (int i = 0; i < node.Options.Count; i++)
                {
                    DialogueEdgeInfo? existing = Find(from, PortOption, i);
                    if (existing == null || existing.To < 0)
                    {
                        option = i;
                        return;
                    }
                }

                option = Math.Max(0, node.Options.Count - 1);
                return;
            }

            if (node.Is("Branch") && Find(from, PortNext, 0) is DialogueEdgeInfo next && next.To >= 0)
            {
                port = PortElse;
            }
        }

        public DialogueEdgeInfo? Find(int from, string port, int option)
        {
            foreach (DialogueEdgeInfo edge in Edges)
            {
                if (edge.From == from && string.Equals(edge.Port, port, StringComparison.OrdinalIgnoreCase) && (!string.Equals(port, PortOption, StringComparison.OrdinalIgnoreCase) || edge.Option == option))
                {
                    return edge;
                }
            }

            return null;
        }

        /// <summary>The node indices a dialogue.preview output names ("[N] ...").</summary>
        public static IReadOnlyCollection<int> NodesNamedIn(string preview)
        {
            HashSet<int> found = new HashSet<int>();
            int at = 0;
            while ((at = preview.IndexOf('[', at)) >= 0)
            {
                int close = preview.IndexOf(']', at);
                if (close < 0)
                {
                    break;
                }

                if (int.TryParse(preview.Substring(at + 1, close - at - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    found.Add(index);
                }

                at = close + 1;
            }

            return found;
        }

        private static string Normalise(string port)
        {
            if (string.Equals(port, PortElse, StringComparison.OrdinalIgnoreCase) || port == "1")
            {
                return PortElse;
            }

            if (string.Equals(port, PortOption, StringComparison.OrdinalIgnoreCase) || port == "2")
            {
                return PortOption;
            }

            return PortNext;
        }
    }

    /// <summary>The Dialogue view's operations (see the file header).</summary>
    public static class DialogueEdits
    {
        public const string AddLineTool = "dialogue.addLine";
        public const string AddChoiceTool = "dialogue.addChoice";
        public const string LinkConditionTool = "dialogue.linkCondition";

        public static Operation AddLine(DialogueDocument document, string text, string speaker = "", int after = -1)
        {
            JObject args = new JObject { ["text"] = text, ["after"] = after };
            if (!string.IsNullOrEmpty(speaker))
            {
                args["speaker"] = speaker;
            }

            return ViewEdits.Op("op1", AddLineTool, document.Ref, args);
        }

        public static Operation AddChoice(DialogueDocument document, IReadOnlyList<string> options, IReadOnlyList<int>? targets = null, int after = -1, string prompt = "")
        {
            JObject args = new JObject { ["options"] = new JArray(options), ["after"] = after, ["prompt"] = prompt ?? string.Empty };
            if (targets != null)
            {
                args["targets"] = new JArray(targets);
            }

            return ViewEdits.Op("op1", AddChoiceTool, document.Ref, args);
        }

        public static Operation LinkCondition(DialogueDocument document, int node, AuthoringRef? condition, int option = -1)
        {
            JObject args = new JObject { ["node"] = node, ["option"] = option };
            args["condition"] = condition == null ? JValue.CreateNull() : StudioJson.ToToken(SemanticIndexService.EdgeRef(condition));
            return ViewEdits.Op("op1", LinkConditionTool, document.Ref, args);
        }

        /// <summary>Connects (or re-targets) the port of <paramref name="from"/> to <paramref name="to"/> (-1 = end): one set of the edge list.</summary>
        public static Operation Connect(DialogueDocument document, int from, string port, int option, int to)
        {
            JArray edges = new JArray();
            bool replaced = false;
            foreach (DialogueEdgeInfo edge in document.Edges)
            {
                bool same = edge.From == from && string.Equals(edge.Port, port, StringComparison.OrdinalIgnoreCase)
                    && (!string.Equals(port, DialogueDocument.PortOption, StringComparison.OrdinalIgnoreCase) || edge.Option == option);
                if (same && !replaced)
                {
                    edges.Add(new DialogueEdgeInfo(edge.Index, from, port, option, to).ToJson());
                    replaced = true;
                }
                else
                {
                    edges.Add(edge.ToJson());
                }
            }

            if (!replaced)
            {
                edges.Add(new DialogueEdgeInfo(edges.Count, from, port, option, to).ToJson());
            }

            return ViewEdits.SetOp("op1", document.Ref, "edges", edges);
        }

        /// <summary>Removes one edge: one set of the edge list.</summary>
        public static Operation Disconnect(DialogueDocument document, int edgeIndex)
        {
            JArray edges = new JArray();
            foreach (DialogueEdgeInfo edge in document.Edges)
            {
                if (edge.Index != edgeIndex)
                {
                    edges.Add(edge.ToJson());
                }
            }

            return ViewEdits.SetOp("op1", document.Ref, "edges", edges);
        }

        /// <summary>Sets one member of one node (text, speaker, speakerEntityId): a partial element list ({} = unchanged).</summary>
        public static Operation SetNodeField(DialogueDocument document, int node, string field, JToken value)
        {
            JArray nodes = new JArray();
            for (int i = 0; i < document.Nodes.Count; i++)
            {
                nodes.Add(i == node ? new JObject { [field] = value.DeepClone() } : new JObject());
            }

            return ViewEdits.SetOp("op1", document.Ref, "nodes", nodes);
        }

        public static Operation SetText(DialogueDocument document, int node, string text) => SetNodeField(document, node, "text", text ?? string.Empty);

        public static Operation SetSpeaker(DialogueDocument document, int node, string speaker) => SetNodeField(document, node, "speaker", speaker ?? string.Empty);

        /// <summary>Renames one option of a choice node.</summary>
        public static Operation RenameOption(DialogueDocument document, int node, int option, string text)
        {
            JArray options = new JArray();
            for (int i = 0; i < document.Nodes[node].Options.Count; i++)
            {
                options.Add(i == option ? new JObject { ["text"] = text ?? string.Empty } : new JObject());
            }

            return SetNodeField(document, node, "options", options);
        }

        /// <summary>
        /// Removes a node: the later nodes shift down one index, edges from or to the node are dropped, the remaining
        /// edges are re-indexed, and the entry moves when it pointed past the node. One set of nodes, edges and entry.
        /// </summary>
        public static Operation RemoveNode(DialogueDocument document, int node)
        {
            JArray nodes = new JArray();
            for (int i = 0; i < document.RawNodes.Count; i++)
            {
                if (i != node)
                {
                    nodes.Add(document.RawNodes[i].DeepClone());
                }
            }

            JArray edges = new JArray();
            foreach (DialogueEdgeInfo edge in document.Edges)
            {
                if (edge.From == node)
                {
                    continue;
                }

                int to = edge.To == node ? -1 : (edge.To > node ? edge.To - 1 : edge.To);
                int from = edge.From > node ? edge.From - 1 : edge.From;
                edges.Add(new DialogueEdgeInfo(edge.Index, from, edge.Port, edge.Option, to).ToJson());
            }

            JObject fields = new JObject { ["nodes"] = nodes, ["edges"] = edges };
            if (document.Entry > node || (document.Entry == node && node >= nodes.Count))
            {
                fields["entry"] = Math.Max(0, document.Entry - 1);
            }

            return ViewEdits.SetFieldsOp("op1", document.Ref, fields);
        }

        /// <summary>Deletes the whole graph asset.</summary>
        public static Operation DeleteGraph(DialogueDocument document) => ViewEdits.Op("op1", BuiltInToolIdsExt.Delete, document.Ref);

        /// <summary>A "fact=value; ..." string for dialogue.preview from overrides.</summary>
        public static string FactsText(IReadOnlyDictionary<string, int> facts)
        {
            List<string> terms = new List<string>();
            foreach (KeyValuePair<string, int> fact in facts)
            {
                terms.Add(fact.Key + "=" + fact.Value.ToString(CultureInfo.InvariantCulture));
            }

            terms.Sort(StringComparer.Ordinal);
            return string.Join("; ", terms);
        }
    }
}
