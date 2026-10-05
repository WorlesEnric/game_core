// GameCore.Studio.Views - Play Mode reads and commands for the views (live overlays, "Play from here", "Travel here").
//
// There is no host-level command surface for Editor tools in the kernel or the gameplay packages: the running world is
// a GameplayWorld (P1.1) or a NarrativeWorld (P1.4) held by whatever boots the game (Hollowmere's GameBoot exposes
// `World`; the narrative boot owner may expose a `NarrativeWorld`). The views therefore use IGameplayCommandBridge, and
// the default implementation, ReflectionGameplayBridge, finds the running world by reflection and calls the gameplay
// API by member name, so this package keeps no type dependency on gameplay:
//   * an explicitly registered world (StudioViewsSession.RegisterRunningWorld) wins;
//   * otherwise every live MonoBehaviour is scanned for a public instance property or field whose type is named
//     NarrativeWorld or GameplayWorld (a NarrativeWorld is preferred); the scan is throttled to once per second while
//     nothing is found and repeated when the owner is destroyed.
// Reads: facts (NarrativeRuntime.Models.TryGetFactByName + State.Fact), quest status/stage/branch (State.Quest with the
// QuestField member names), objective done flags, dialogue visited bits (State.NodeVisited), the explain ring
// (Explain.Recent) and region residency (GameplayWorld.Streamer.ResidencyOf). Commands: dialogue.start through the
// narrative world's IConversationStarter.TryStart(npc, graph), and world.travel through GameplayWorld.Commands.Travel
// with the world's focus entity as traveller. Everything answers NotAvailable outside Play Mode.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Views
{
    /// <summary>The outcome of a command a view sent to the running game.</summary>
    public enum GameplayCommandStatus
    {
        Submitted,
        NotAvailable,
        Refused,
        Unsupported,
    }

    public sealed class GameplayCommandResult
    {
        public GameplayCommandResult(GameplayCommandStatus status, string detail)
        {
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public GameplayCommandStatus Status { get; }

        public string Detail { get; }

        public bool Ok => Status == GameplayCommandStatus.Submitted;

        public override string ToString() => Status + (Detail.Length > 0 ? ": " + Detail : string.Empty);
    }

    /// <summary>Committed quest slots of one quest.</summary>
    public readonly struct QuestLiveState
    {
        public QuestLiveState(int status, int stage, int branch)
        {
            Status = status;
            Stage = stage;
            Branch = branch;
        }

        /// <summary>0 inactive, 1 active, 2 completed, 3 failed (QuestRules).</summary>
        public int Status { get; }

        public int Stage { get; }

        public int Branch { get; }

        public string StatusName
        {
            get
            {
                switch (Status)
                {
                    case 0: return "inactive";
                    case 1: return "active";
                    case 2: return "completed";
                    case 3: return "failed";
                    default: return "status " + Status;
                }
            }
        }
    }

    /// <summary>One rule decision from the running world's explain ring.</summary>
    public sealed class ExplainEntry
    {
        public ExplainEntry(string ruleRef, string ruleName, long step, bool fired, string reason, string failedCondition, IReadOnlyList<string> inputs)
        {
            RuleRef = ruleRef;
            RuleName = ruleName;
            Step = step;
            Fired = fired;
            Reason = reason;
            FailedCondition = failedCondition;
            Inputs = inputs;
        }

        public string RuleRef { get; }

        public string RuleName { get; }

        public long Step { get; }

        public bool Fired { get; }

        public string Reason { get; }

        public string FailedCondition { get; }

        public IReadOnlyList<string> Inputs { get; }

        public override string ToString() =>
            "step " + Step + " " + RuleName + ": " + (Fired ? "fired" : "skipped (" + Reason + ")") + (FailedCondition.Length > 0 ? ", failed " + FailedCondition : string.Empty);
    }

    /// <summary>What the views read from and send to the running game.</summary>
    public interface IGameplayCommandBridge
    {
        /// <summary>True while a running world is reachable (Play Mode, booted).</summary>
        bool IsAvailable { get; }

        /// <summary>True when the running world carries the narrative plugins (facts, quests, dialogue, rules).</summary>
        bool HasNarrative { get; }

        /// <summary>Where the world was found, or why it was not.</summary>
        string Describe { get; }

        bool TryReadFact(string factName, out int value);

        bool TryReadQuest(string questRef, out QuestLiveState state);

        bool TryReadObjectiveDone(string questRef, int objective, out bool done);

        bool TryReadVisited(string graphRef, int node, out bool visited);

        /// <summary>The most recent rule decisions, newest last.</summary>
        IReadOnlyList<ExplainEntry> RecentExplain(int max);

        /// <summary>The committed residency of a region by authoring id, as the gameplay enum's member name.</summary>
        bool TryResidency(string regionId, out string residency);

        /// <summary>dialogue.start: starts <paramref name="graphRef"/> with the speaker entity (authoring id).</summary>
        GameplayCommandResult StartDialogue(string graphRef, string speakerEntityId);

        /// <summary>world.travel: moves the world's focus entity (the player) to a region.</summary>
        GameplayCommandResult Travel(string regionId);
    }

    /// <summary>The default bridge: finds the running world by reflection (see the file header).</summary>
    public sealed class ReflectionGameplayBridge : IGameplayCommandBridge
    {
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
        private readonly Func<object?> _registered;
        private readonly Func<bool> _isPlaying;
        private object? _gameplay;
        private object? _narrative;
        private UnityEngine.Object? _owner;
        private bool _hasOwner;
        private string _found = "not playing";
        private double _lastScan = -10.0;

        public ReflectionGameplayBridge(Func<object?>? registered = null, Func<bool>? isPlaying = null)
        {
            _registered = registered ?? (() => null);
            _isPlaying = isPlaying ?? (() => EditorApplication.isPlaying);
        }

        public bool IsAvailable => Locate() && _gameplay != null;

        public bool HasNarrative => Locate() && _narrative != null;

        public string Describe
        {
            get
            {
                Locate();
                return _found;
            }
        }

        public bool TryReadFact(string factName, out int value)
        {
            value = 0;
            object? models = NarrativeMember("Models");
            object? state = NarrativeMember("State");
            if (models == null || state == null)
            {
                return false;
            }

            object?[] args = { factName, null };
            if (!(Invoke(models, "TryGetFactByName", args) is bool found) || !found || args[1] == null)
            {
                return false;
            }

            object? key = Get(args[1]!, "Key");
            return key is int factKey && TryInt(Invoke(state, "Fact", new object?[] { factKey }), out value);
        }

        public bool TryReadQuest(string questRef, out QuestLiveState live)
        {
            live = default;
            if (!TryResolveKey(questRef, out int key, out object? state))
            {
                return false;
            }

            MethodInfo? quest = FindMethod(state!.GetType(), "Quest", 2);
            if (quest == null)
            {
                return false;
            }

            Type fieldType = quest.GetParameters()[1].ParameterType;
            if (!fieldType.IsEnum)
            {
                return false;
            }

            if (!TryInt(quest.Invoke(state, new[] { (object)key, Enum.Parse(fieldType, "Status") }), out int status)
                || !TryInt(quest.Invoke(state, new[] { (object)key, Enum.Parse(fieldType, "Stage") }), out int stage)
                || !TryInt(quest.Invoke(state, new[] { (object)key, Enum.Parse(fieldType, "Branch") }), out int branch))
            {
                return false;
            }

            live = new QuestLiveState(status, stage, branch);
            return true;
        }

        public bool TryReadObjectiveDone(string questRef, int objective, out bool done)
        {
            done = false;
            if (!TryResolveKey(questRef, out int key, out object? state))
            {
                return false;
            }

            if (!TryInt(Invoke(state!, "ObjectiveDone", new object?[] { key, objective }), out int value))
            {
                return false;
            }

            done = value != 0;
            return true;
        }

        public bool TryReadVisited(string graphRef, int node, out bool visited)
        {
            visited = false;
            if (!TryResolveKey(graphRef, out int key, out object? state))
            {
                return false;
            }

            if (!TryInt(Invoke(state!, "NodeVisited", new object?[] { key, node }), out int value))
            {
                return false;
            }

            visited = value != 0;
            return true;
        }

        public IReadOnlyList<ExplainEntry> RecentExplain(int max)
        {
            List<ExplainEntry> entries = new List<ExplainEntry>();
            object? explain = Locate() && _narrative != null ? Get(_narrative, "Explain") : null;
            if (explain == null || !(Invoke(explain, "Recent", new object?[] { max }) is IEnumerable records))
            {
                return entries;
            }

            foreach (object? record in records)
            {
                if (record == null)
                {
                    continue;
                }

                List<string> inputs = new List<string>();
                if (Get(record, "Inputs") is IEnumerable list)
                {
                    foreach (object? input in list)
                    {
                        inputs.Add(input?.ToString() ?? string.Empty);
                    }
                }

                entries.Add(new ExplainEntry(
                    Get(record, "RuleRef") as string ?? string.Empty,
                    Get(record, "RuleName") as string ?? string.Empty,
                    Get(record, "Step") is long step ? step : 0L,
                    Get(record, "Fired") is bool fired && fired,
                    Get(record, "Reason") as string ?? string.Empty,
                    Get(record, "FailedCondition") as string ?? string.Empty,
                    inputs));
            }

            return entries;
        }

        public bool TryResidency(string regionId, out string residency)
        {
            residency = string.Empty;
            object? streamer = Locate() && _gameplay != null ? Get(_gameplay, "Streamer") : null;
            if (streamer == null)
            {
                return false;
            }

            object? value = Invoke(streamer, "ResidencyOf", new object?[] { regionId });
            if (value == null)
            {
                return false;
            }

            residency = value.ToString();
            return true;
        }

        public GameplayCommandResult StartDialogue(string graphRef, string speakerEntityId)
        {
            if (!Locate())
            {
                return new GameplayCommandResult(GameplayCommandStatus.NotAvailable, _found);
            }

            object? starter = _narrative == null ? null : Get(_narrative, "Conversations");
            if (starter == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Unsupported, "the running world has no narrative plugins (no NarrativeWorld found; " + _found + ")");
            }

            object? started = Invoke(starter, "TryStart", new object?[] { speakerEntityId ?? string.Empty, graphRef ?? string.Empty });
            if (started == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Unsupported, starter.GetType().Name + " has no TryStart(string, string)");
            }

            bool ok = Get(started, "Started") is bool flag && flag;
            string detail = Get(started, "Detail") as string ?? string.Empty;
            return new GameplayCommandResult(ok ? GameplayCommandStatus.Submitted : GameplayCommandStatus.Refused, ok ? "dialogue.start " + graphRef : detail);
        }

        public GameplayCommandResult Travel(string regionId)
        {
            if (!Locate() || _gameplay == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.NotAvailable, _found);
            }

            object? commands = Get(_gameplay, "Commands");
            object? focus = Get(_gameplay, "Focus");
            if (commands == null || focus == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Unsupported, "the running world exposes no Commands/Focus");
            }

            MethodInfo? travel = FindMethod(commands.GetType(), "Travel", 3);
            if (travel == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Unsupported, commands.GetType().Name + " has no Travel(traveller, region, portal)");
            }

            object? receipt;
            try
            {
                receipt = travel.Invoke(commands, new[] { focus, regionId, string.Empty });
            }
            catch (TargetInvocationException error)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Refused, (error.InnerException ?? error).Message);
            }

            object? result = receipt == null ? null : Get(receipt, "Result");
            string text = result?.ToString() ?? "submitted";
            bool refused = text.IndexOf("Refus", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Reject", StringComparison.OrdinalIgnoreCase) >= 0;
            return new GameplayCommandResult(refused ? GameplayCommandStatus.Refused : GameplayCommandStatus.Submitted, "world.travel " + regionId + ": " + text);
        }

        /// <summary>Forgets the located world (Play Mode exit, a new registration).</summary>
        public void Reset()
        {
            _gameplay = null;
            _narrative = null;
            _owner = null;
            _hasOwner = false;
            _found = "not playing";
            _lastScan = -10.0;
        }

        private bool TryResolveKey(string reference, out int key, out object? state)
        {
            key = 0;
            state = NarrativeMember("State");
            object? models = NarrativeMember("Models");
            if (state == null || models == null || string.IsNullOrEmpty(reference))
            {
                return false;
            }

            object?[] args = { reference, 0 };
            if (!(Invoke(models, "TryResolve", args) is bool found) || !found || !(args[1] is int resolved))
            {
                return false;
            }

            key = resolved;
            return true;
        }

        private object? NarrativeMember(string name)
        {
            if (!Locate() || _narrative == null)
            {
                return null;
            }

            object? runtime = Get(_narrative, "Runtime");
            return runtime == null ? null : Get(runtime, name);
        }

        private bool Locate()
        {
            if (!_isPlaying())
            {
                if (_gameplay != null || _narrative != null)
                {
                    Reset();
                }

                _found = "not playing";
                return false;
            }

            object? registered = _registered();
            if (registered != null)
            {
                Adopt(registered, null, "registered " + registered.GetType().Name);
                return _gameplay != null;
            }

            if (_gameplay != null && (!_hasOwner || _owner != null))
            {
                return true;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastScan < 1.0)
            {
                return _gameplay != null;
            }

            _lastScan = now;
            _gameplay = null;
            _narrative = null;
            _owner = null;
            _hasOwner = false;
            _found = "no running GameplayWorld or NarrativeWorld found on any MonoBehaviour";
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            object? gameplayCandidate = null;
            MonoBehaviour? gameplayOwner = null;
            string gameplayWhere = string.Empty;
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> member in WorldMembers(behaviour))
                {
                    string typeName = member.Value.GetType().Name;
                    if (typeName == "NarrativeWorld")
                    {
                        Adopt(member.Value, behaviour, behaviour.GetType().Name + "." + member.Key);
                        return true;
                    }

                    if (typeName == "GameplayWorld" && gameplayCandidate == null)
                    {
                        gameplayCandidate = member.Value;
                        gameplayOwner = behaviour;
                        gameplayWhere = behaviour.GetType().Name + "." + member.Key;
                    }
                }
            }

            if (gameplayCandidate != null)
            {
                Adopt(gameplayCandidate, gameplayOwner, gameplayWhere);
            }

            return _gameplay != null;
        }

        private void Adopt(object world, UnityEngine.Object? owner, string where)
        {
            _owner = owner;
            _hasOwner = owner != null;
            if (world.GetType().Name == "NarrativeWorld")
            {
                _narrative = world;
                _gameplay = Get(world, "World");
                _found = "NarrativeWorld via " + where;
            }
            else
            {
                _narrative = null;
                _gameplay = world;
                _found = "GameplayWorld via " + where + " (no narrative plugins)";
            }
        }

        private static IEnumerable<KeyValuePair<string, object>> WorldMembers(MonoBehaviour behaviour)
        {
            Type type = behaviour.GetType();
            foreach (PropertyInfo property in type.GetProperties(Public))
            {
                if (property.GetIndexParameters().Length != 0 || !IsWorldType(property.PropertyType))
                {
                    continue;
                }

                object? value;
                try
                {
                    value = property.GetValue(behaviour);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (value != null)
                {
                    yield return new KeyValuePair<string, object>(property.Name, value);
                }
            }

            foreach (FieldInfo field in type.GetFields(Public))
            {
                if (IsWorldType(field.FieldType) && field.GetValue(behaviour) is object value)
                {
                    yield return new KeyValuePair<string, object>(field.Name, value);
                }
            }
        }

        private static bool IsWorldType(Type type) => type.Name == "NarrativeWorld" || type.Name == "GameplayWorld";

        private static object? Get(object target, string name)
        {
            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(name, Public);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(target);
                }
                catch (TargetInvocationException)
                {
                    return null;
                }
            }

            FieldInfo? field = type.GetField(name, Public);
            if (field != null)
            {
                return field.GetValue(target);
            }

            foreach (Type contract in type.GetInterfaces())
            {
                PropertyInfo? declared = contract.GetProperty(name);
                if (declared != null && declared.GetIndexParameters().Length == 0)
                {
                    return declared.GetValue(target);
                }
            }

            return null;
        }

        private static MethodInfo? FindMethod(Type type, string name, int parameters)
        {
            foreach (MethodInfo method in type.GetMethods(Public))
            {
                if (method.Name == name && method.GetParameters().Length == parameters)
                {
                    return method;
                }
            }

            foreach (Type contract in type.GetInterfaces())
            {
                foreach (MethodInfo method in contract.GetMethods())
                {
                    if (method.Name == name && method.GetParameters().Length == parameters)
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private static object? Invoke(object target, string name, object?[] args)
        {
            MethodInfo? method = FindMethod(target.GetType(), name, args.Length);
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(target, args);
            }
            catch (Exception error) when (error is TargetInvocationException || error is ArgumentException)
            {
                return null;
            }
        }

        private static bool TryInt(object? value, out int result)
        {
            if (value is int number)
            {
                result = number;
                return true;
            }

            result = 0;
            return false;
        }
    }
}
