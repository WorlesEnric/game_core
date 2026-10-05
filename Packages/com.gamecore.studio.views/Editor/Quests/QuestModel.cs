// GameCore.Studio.Views - W-VIEW-03 model: a quest read generically, its change sets, and quest.simulate support.
//
// Edits: quest.addStage / quest.addObjective / quest.linkReward (P1.4 tools) and inline `set` of one stage, objective or
// reward (a partial element list, {} = unchanged) or of the title. Simulation runs quest.simulate (a pure tool, through
// ReadOnlyToolInvoker) over a path the view derives for a branch choice: the stages are walked from stage 0 along
// their `next` links, and each stage's objectives that are always needed (branch 0) or belong to the chosen branch are
// satisfied with the matching verb (talk/collect/reach/interact/fact). The result line ("= completed at stage N via
// <branch>") and the RewardGranted lines are parsed into the resulting facts and inventory.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    public sealed class QuestStage
    {
        public QuestStage(int index, string title, string description, int next)
        {
            Index = index;
            Title = title;
            Description = description;
            Next = next;
        }

        public int Index { get; }

        public string Title { get; }

        public string Description { get; }

        /// <summary>-1 = the following stage.</summary>
        public int Next { get; }

        public int NextIndex => Next >= 0 ? Next : Index + 1;
    }

    public sealed class QuestObjective
    {
        public QuestObjective(int index, int stage, string kind, AuthoringRef? target, string targetEntityId, int required, int branch, string text)
        {
            Index = index;
            Stage = stage;
            Kind = kind;
            Target = target;
            TargetEntityId = targetEntityId;
            Required = required;
            Branch = branch;
            Text = text;
        }

        public int Index { get; }

        public int Stage { get; }

        /// <summary>Talk, Collect, Reach, Interact or Fact.</summary>
        public string Kind { get; }

        public AuthoringRef? Target { get; }

        public string TargetEntityId { get; }

        public int Required { get; }

        /// <summary>0 = always needed; n = part of branch n.</summary>
        public int Branch { get; }

        public string Text { get; }
    }

    public sealed class QuestReward
    {
        public QuestReward(int index, string kind, AuthoringRef? target, int value, int branch)
        {
            Index = index;
            Kind = kind;
            Target = target;
            Value = value;
            Branch = branch;
        }

        public int Index { get; }

        /// <summary>Item or Fact.</summary>
        public string Kind { get; }

        public AuthoringRef? Target { get; }

        public int Value { get; }

        public int Branch { get; }
    }

    /// <summary>The parsed outcome of quest.simulate.</summary>
    public sealed class QuestSimulation
    {
        public QuestSimulation(string path, int branch, bool ok, string text, string status, int stage, string branchName, IReadOnlyDictionary<string, int> facts, IReadOnlyDictionary<string, int> inventory, IReadOnlyList<string> rewards)
        {
            Path = path;
            Branch = branch;
            Ok = ok;
            Text = text;
            Status = status;
            Stage = stage;
            BranchName = branchName;
            Facts = facts;
            Inventory = inventory;
            Rewards = rewards;
        }

        public string Path { get; }

        public int Branch { get; }

        /// <summary>The tool ran (the quest may still not complete).</summary>
        public bool Ok { get; }

        public string Text { get; }

        /// <summary>completed, active, failed, inactive (from the "= ..." line), or the refusal code.</summary>
        public string Status { get; }

        public int Stage { get; }

        public string BranchName { get; }

        public bool Completed => string.Equals(Status, "completed", StringComparison.Ordinal);

        public IReadOnlyDictionary<string, int> Facts { get; }

        public IReadOnlyDictionary<string, int> Inventory { get; }

        /// <summary>The RewardGranted descriptions.</summary>
        public IReadOnlyList<string> Rewards { get; }
    }

    /// <summary>One quest as the view shows it.</summary>
    public sealed class QuestDocument
    {
        public const string Type = "quest.quest";

        private QuestDocument(AuthoringRef reference, string name, string title, IReadOnlyList<QuestStage> stages, IReadOnlyList<QuestObjective> objectives, IReadOnlyList<QuestReward> rewards, IReadOnlyList<string> branchNames)
        {
            Ref = reference;
            Name = name;
            Title = title;
            Stages = stages;
            Objectives = objectives;
            Rewards = rewards;
            BranchNames = branchNames;
        }

        public AuthoringRef Ref { get; }

        public string AuthoringId => Ref.AuthoringId ?? string.Empty;

        public string Name { get; }

        public string Title { get; }

        public IReadOnlyList<QuestStage> Stages { get; }

        public IReadOnlyList<QuestObjective> Objectives { get; }

        public IReadOnlyList<QuestReward> Rewards { get; }

        /// <summary>Names of branches 1..n.</summary>
        public IReadOnlyList<string> BranchNames { get; }

        /// <summary>The highest branch any objective or reward uses (or the names list's length).</summary>
        public int BranchCount
        {
            get
            {
                int count = BranchNames.Count;
                foreach (QuestObjective objective in Objectives)
                {
                    count = Math.Max(count, objective.Branch);
                }

                foreach (QuestReward reward in Rewards)
                {
                    count = Math.Max(count, reward.Branch);
                }

                return count;
            }
        }

        public string BranchName(int branch)
        {
            if (branch <= 0)
            {
                return "always";
            }

            return branch <= BranchNames.Count && BranchNames[branch - 1].Length > 0 ? BranchNames[branch - 1] : "branch " + branch.ToString(CultureInfo.InvariantCulture);
        }

        public static QuestDocument? Load(StudioRuntime runtime, AuthoringRef reference)
        {
            UnityEngine.Object? target = AuthoredData.Resolve(runtime, reference, out AuthoringRef? current);
            if (target == null || current == null || !string.Equals(AuthoredData.TypeOf(runtime, target), Type, StringComparison.Ordinal))
            {
                return null;
            }

            List<QuestStage> stages = new List<QuestStage>();
            if (AuthoredData.Read(runtime, target, "stages") is JArray rawStages)
            {
                for (int i = 0; i < rawStages.Count; i++)
                {
                    JToken stage = rawStages[i];
                    stages.Add(new QuestStage(i, AuthoredData.Text(stage["title"]), AuthoredData.Text(stage["description"]), AuthoredData.Int(stage["next"], -1)));
                }
            }

            List<QuestObjective> objectives = new List<QuestObjective>();
            if (AuthoredData.Read(runtime, target, "objectives") is JArray rawObjectives)
            {
                for (int i = 0; i < rawObjectives.Count; i++)
                {
                    JToken objective = rawObjectives[i];
                    objectives.Add(new QuestObjective(
                        i,
                        AuthoredData.Int(objective["stage"]),
                        AuthoredData.Text(objective["kind"]),
                        AuthoredData.Ref(objective["target"]),
                        AuthoredData.Text(objective["targetEntityId"]),
                        AuthoredData.Int(objective["required"], 1),
                        AuthoredData.Int(objective["branch"]),
                        AuthoredData.Text(objective["text"])));
                }
            }

            List<QuestReward> rewards = new List<QuestReward>();
            if (AuthoredData.Read(runtime, target, "rewards") is JArray rawRewards)
            {
                for (int i = 0; i < rawRewards.Count; i++)
                {
                    JToken reward = rawRewards[i];
                    rewards.Add(new QuestReward(i, AuthoredData.Text(reward["kind"]), AuthoredData.Ref(reward["target"]), AuthoredData.Int(reward["value"], 1), AuthoredData.Int(reward["branch"])));
                }
            }

            List<string> branchNames = new List<string>();
            if (AuthoredData.Read(runtime, target, "branchNames") is JArray rawNames)
            {
                foreach (JToken name in rawNames)
                {
                    branchNames.Add(AuthoredData.Text(name));
                }
            }

            return new QuestDocument(current, target.name, AuthoredData.Text(AuthoredData.Read(runtime, target, "title")), stages, objectives, rewards, branchNames);
        }

        /// <summary>A document over explicit data (tests).</summary>
        public static QuestDocument FromData(AuthoringRef reference, string name, IReadOnlyList<QuestStage> stages, IReadOnlyList<QuestObjective> objectives, IReadOnlyList<QuestReward> rewards, IReadOnlyList<string> branchNames)
        {
            return new QuestDocument(reference, name, name, stages, objectives, rewards, branchNames);
        }

        public IReadOnlyList<QuestObjective> ObjectivesOf(int stage)
        {
            List<QuestObjective> found = new List<QuestObjective>();
            foreach (QuestObjective objective in Objectives)
            {
                if (objective.Stage == stage)
                {
                    found.Add(objective);
                }
            }

            return found;
        }

        /// <summary>The stage indices in play order from stage 0 along the next links (each once).</summary>
        public IReadOnlyList<int> StageOrder()
        {
            List<int> order = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            int stage = 0;
            while (stage >= 0 && stage < Stages.Count && seen.Add(stage))
            {
                order.Add(stage);
                stage = Stages[stage].NextIndex;
            }

            return order;
        }
    }

    /// <summary>The Quests view's operations and simulation helpers.</summary>
    public static class QuestEdits
    {
        public const string AddStageTool = "quest.addStage";
        public const string AddObjectiveTool = "quest.addObjective";
        public const string LinkRewardTool = "quest.linkReward";
        public const string SimulateTool = "quest.simulate";

        public static Operation AddStage(QuestDocument quest, string title, string description = "", int next = -1)
        {
            return ViewEdits.Op("op1", AddStageTool, quest.Ref, new JObject { ["title"] = title, ["description"] = description ?? string.Empty, ["next"] = next });
        }

        public static Operation AddObjective(QuestDocument quest, int stage, string kind, AuthoringRef? target, string targetEntityId = "", int required = 1, int branch = 0, string text = "")
        {
            JObject args = new JObject
            {
                ["stage"] = stage,
                ["kind"] = kind,
                ["targetEntityId"] = targetEntityId ?? string.Empty,
                ["required"] = required,
                ["branch"] = branch,
                ["text"] = text ?? string.Empty,
            };
            if (target != null)
            {
                args["target"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(target));
            }

            return ViewEdits.Op("op1", AddObjectiveTool, quest.Ref, args);
        }

        public static Operation LinkReward(QuestDocument quest, string kind, AuthoringRef target, int value = 1, int branch = 0)
        {
            return ViewEdits.Op("op1", LinkRewardTool, quest.Ref, new JObject
            {
                ["kind"] = kind,
                ["target"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(target)),
                ["value"] = value,
                ["branch"] = branch,
            });
        }

        /// <summary>Sets one member of one element of a list field (stages, objectives, rewards).</summary>
        public static Operation SetElement(QuestDocument quest, string list, int index, string member, JToken value)
        {
            int count = list == "stages" ? quest.Stages.Count : (list == "objectives" ? quest.Objectives.Count : quest.Rewards.Count);
            JArray items = new JArray();
            for (int i = 0; i < count; i++)
            {
                items.Add(i == index ? new JObject { [member] = value.DeepClone() } : new JObject());
            }

            return ViewEdits.SetOp("op1", quest.Ref, list, items);
        }

        public static Operation SetTitle(QuestDocument quest, string title) => ViewEdits.SetOp("op1", quest.Ref, "title", title ?? string.Empty);

        /// <summary>The simulate path that satisfies every stage through <paramref name="branch"/> (see the file header).</summary>
        public static string PathFor(QuestDocument quest, int branch, IndexGraph graph)
        {
            List<string> steps = new List<string>();
            foreach (int stage in quest.StageOrder())
            {
                foreach (QuestObjective objective in quest.ObjectivesOf(stage))
                {
                    if (objective.Branch != 0 && objective.Branch != branch)
                    {
                        continue;
                    }

                    string? step = StepFor(objective, graph);
                    if (step != null)
                    {
                        steps.Add(step);
                    }
                }
            }

            return string.Join("; ", steps);
        }

        public static string? StepFor(QuestObjective objective, IndexGraph graph)
        {
            string kind = objective.Kind.ToLowerInvariant();
            string required = objective.Required.ToString(CultureInfo.InvariantCulture);
            if (kind == "interact")
            {
                return objective.TargetEntityId.Length == 0 ? null : "interact:" + objective.TargetEntityId;
            }

            if (objective.Target == null)
            {
                return null;
            }

            switch (kind)
            {
                case "fact":
                    return "fact:" + FactName(objective.Target, graph) + "=" + required;
                case "collect":
                    return "collect:" + TargetName(objective.Target) + "=" + required;
                case "reach":
                    return "reach:" + TargetName(objective.Target);
                case "talk":
                    return "talk:" + TargetName(objective.Target);
                default:
                    return null;
            }
        }

        /// <summary>Runs quest.simulate for a path and parses the outcome.</summary>
        public static QuestSimulation Simulate(ReadOnlyToolInvoker tools, QuestDocument quest, string path, int branch)
        {
            ToolInvocation result = tools.Invoke(SimulateTool, quest.Ref, new JObject { ["path"] = path });
            return Parse(path, branch, result);
        }

        public static QuestSimulation Parse(string path, int branch, ToolInvocation result)
        {
            Dictionary<string, int> facts = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, int> inventory = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> rewards = new List<string>();
            foreach (string raw in path.Split(';'))
            {
                string term = raw.Trim();
                int colon = term.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }

                string verb = term.Substring(0, colon).Trim().ToLowerInvariant();
                string rest = term.Substring(colon + 1).Trim();
                int eq = rest.LastIndexOf('=');
                int value = 1;
                if (eq > 0)
                {
                    int.TryParse(rest.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                    rest = rest.Substring(0, eq).Trim();
                }

                if (verb == "fact")
                {
                    facts[rest] = value;
                }
                else if (verb == "collect")
                {
                    inventory[rest] = value;
                }
            }

            if (!result.Ok)
            {
                return new QuestSimulation(path, branch, false, result.Text, result.Code ?? "Refused", -1, string.Empty, facts, inventory, rewards);
            }

            string status = "unknown";
            int stage = -1;
            string branchName = string.Empty;
            foreach (string raw in result.Text.Split('\n'))
            {
                string line = raw.Trim();
                const string granted = "RewardGranted ";
                int at = line.IndexOf(granted, StringComparison.Ordinal);
                if (at >= 0)
                {
                    string reward = line.Substring(at + granted.Length).Trim();
                    rewards.Add(reward);
                    ApplyReward(reward, facts, inventory);
                }

                if (line.StartsWith("= ", StringComparison.Ordinal))
                {
                    string summary = line.Substring(2);
                    int atStage = summary.IndexOf(" at stage ", StringComparison.Ordinal);
                    status = atStage > 0 ? summary.Substring(0, atStage) : summary;
                    if (atStage > 0)
                    {
                        string after = summary.Substring(atStage + " at stage ".Length);
                        int via = after.IndexOf(" via ", StringComparison.Ordinal);
                        int.TryParse(via >= 0 ? after.Substring(0, via) : after, NumberStyles.Integer, CultureInfo.InvariantCulture, out stage);
                        branchName = via >= 0 ? after.Substring(via + " via ".Length) : string.Empty;
                    }
                }
            }

            return new QuestSimulation(path, branch, true, result.Text, status, stage, branchName, facts, inventory, rewards);
        }

        /// <summary>"item Lantern x1" adds to the inventory, "fact bell_rung = 1" sets a fact.</summary>
        private static void ApplyReward(string reward, Dictionary<string, int> facts, Dictionary<string, int> inventory)
        {
            if (reward.StartsWith("item ", StringComparison.Ordinal))
            {
                string rest = reward.Substring(5);
                int x = rest.LastIndexOf(" x", StringComparison.Ordinal);
                if (x > 0 && int.TryParse(rest.Substring(x + 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    string item = rest.Substring(0, x);
                    inventory.TryGetValue(item, out int held);
                    inventory[item] = held + count;
                }
            }
            else if (reward.StartsWith("fact ", StringComparison.Ordinal))
            {
                string rest = reward.Substring(5);
                int eq = rest.LastIndexOf(" = ", StringComparison.Ordinal);
                if (eq > 0 && int.TryParse(rest.Substring(eq + 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    facts[rest.Substring(0, eq)] = value;
                }
            }
        }

        /// <summary>A summary of a simulation for the panel.</summary>
        public static string Describe(QuestSimulation simulation)
        {
            StringBuilder text = new StringBuilder();
            text.Append(simulation.Status);
            if (simulation.Stage >= 0)
            {
                text.Append(" at stage ").Append(simulation.Stage);
            }

            if (simulation.BranchName.Length > 0)
            {
                text.Append(" via ").Append(simulation.BranchName);
            }

            text.Append("\nfacts: ");
            AppendMap(text, simulation.Facts);
            text.Append("\ninventory: ");
            AppendMap(text, simulation.Inventory);
            text.Append("\nrewards: ").Append(simulation.Rewards.Count == 0 ? "none" : string.Join(", ", simulation.Rewards));
            return text.ToString();
        }

        private static void AppendMap(StringBuilder text, IReadOnlyDictionary<string, int> map)
        {
            if (map.Count == 0)
            {
                text.Append("none");
                return;
            }

            bool first = true;
            foreach (KeyValuePair<string, int> pair in map)
            {
                text.Append(first ? string.Empty : ", ").Append(pair.Key).Append('=').Append(pair.Value);
                first = false;
            }
        }

        private static string FactName(AuthoringRef target, IndexGraph graph)
        {
            IndexNode? node = graph.Node(target.IdentityKey);
            string? name = node == null ? null : IndexGraph.StringField(node, "factName");
            return string.IsNullOrEmpty(name) ? TargetName(target) : name!;
        }

        private static string TargetName(AuthoringRef target) => target.AuthoringId ?? IndexGraph.LeafOf(target);
    }
}
