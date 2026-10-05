// Hollowmere - builds a dialogue graph's nodes and edges as the JSON a Studio `set` writes (P3.1).
//
// A graph is authored whole (entry, nodes, edges) so re-authoring is deterministic. Every node lists every field (a
// growing serialized list copies the previous element into new slots, so omitted fields would inherit it). Line
// nodes carry a key; when the media step has produced a voice clip for that key (Media/Voices/<graph>_<key>.wav), the
// line references it.
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.Authoring
{
    /// <summary>One dialogue graph under construction.</summary>
    public sealed class GraphBuilder
    {
        private readonly JArray nodes = new JArray();
        private readonly JArray edges = new JArray();
        private readonly string graph;
        private readonly bool withVoices;

        public GraphBuilder(string graphName, string speaker, bool voices)
        {
            graph = graphName;
            Speaker = speaker;
            withVoices = voices;
        }

        public string Speaker { get; }

        /// <summary>Line keys and their text (the media step speaks them).</summary>
        public List<(string Key, string Speaker, string Text)> Lines { get; } = new List<(string, string, string)>();

        public static string VoicePath(string graph, string key) => HollowmerePaths.Voices + "/" + graph + "_" + key + ".wav";

        public int Line(string key, string text, string speaker = "")
        {
            string who = speaker.Length > 0 ? speaker : Speaker;
            Lines.Add((key, who, text));
            string voice = VoicePath(graph, key);
            bool hasVoice = withVoices && AssetDatabase.LoadAssetAtPath<AudioClip>(voice) != null;
            return Add("Line", speaker, text, hasVoice ? voice : null, null, null, new JArray());
        }

        public int Branch(string conditionPath) => Add("Branch", string.Empty, string.Empty, null, conditionPath, null, new JArray());

        public int Action(string actionSetPath) => Add("Action", string.Empty, string.Empty, null, null, actionSetPath, new JArray());

        public int Choice(string prompt, params (string Text, string? Condition)[] options)
        {
            var list = new JArray();
            foreach (var option in options)
            {
                list.Add(new JObject { ["text"] = option.Text, ["condition"] = option.Condition, ["hideWhenUnavailable"] = false });
            }

            return Add("Choice", string.Empty, prompt, null, null, null, list);
        }

        public void Next(int from, int to) => edges.Add(Edge(from, "Next", 0, to));

        public void Else(int from, int to) => edges.Add(Edge(from, "Else", 0, to));

        public void Option(int from, int option, int to) => edges.Add(Edge(from, "Option", option, to));

        /// <summary>The fields a `set` writes (entry node 0).</summary>
        public JObject Fields(string speakerEntityId) => new JObject
        {
            ["speaker"] = Speaker,
            ["speakerEntityId"] = speakerEntityId,
            ["entry"] = 0,
            ["nodes"] = nodes,
            ["edges"] = edges,
        };

        private int Add(string kind, string speaker, string text, string? voice, string? condition, string? actions, JArray options)
        {
            nodes.Add(new JObject
            {
                ["kind"] = kind,
                ["speaker"] = speaker,
                ["speakerEntityId"] = string.Empty,
                ["text"] = text,
                ["voiceClip"] = voice,
                ["portrait"] = null,
                ["condition"] = condition,
                ["actions"] = actions,
                ["options"] = options,
            });
            return nodes.Count - 1;
        }

        private static JObject Edge(int from, string port, int option, int to) =>
            new JObject { ["from"] = from, ["port"] = port, ["option"] = option, ["to"] = to };
    }
}
