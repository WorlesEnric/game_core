// GameCore.Gameplay.Logic.Editor - helpers shared by the narrative authoring tools of the four P1.4 packages.
//
//   CreateAsset    create a narrative definition asset (beside a content set, or at an explicit path), mint its id,
//                  register Undo and list it on the content set
//   Closure        a definition plus everything it references (transitively)
//   Models         the validated model set of a closure (every package converter), with its problems
//   ParseState     a test state "fact.bell_rung=1; item.old_coin=3; currency=5; quest.drowned_bell.stage=2; now=1000"
//
// Tools refuse with an ArgumentException whose message starts with the GP-* code, like the P1.1 tools.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Logic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Logic.Editor
{
    /// <summary>Shared helpers of the narrative tools.</summary>
    public static class NarrativeAuthoring
    {
        private static readonly Regex CodePattern = new Regex("GP-[A-Z]{3}-[0-9]{3}", RegexOptions.CultureInvariant);

        /// <summary>
        /// Creates a definition asset of type <typeparamref name="T"/>. Without a path it goes to
        /// &lt;content set dir&gt;/&lt;folder&gt;/&lt;name&gt;.asset. The asset is listed on the set (when given).
        /// </summary>
        public static T CreateAsset<T>(GameplayContentSet? set, string name, string folder, string assetPath, string undoName)
            where T : NarrativeDefinitionAsset
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ContentMissingReference + ": a definition needs a name");
            }

            string path = assetPath ?? string.Empty;
            if (path.Length == 0)
            {
                string setPath = set != null ? AssetDatabase.GetAssetPath(set) : string.Empty;
                if (string.IsNullOrEmpty(setPath))
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.ContentSetMissingWorld + ": pass a content set asset or an asset path");
                }

                string directory = Path.GetDirectoryName(setPath)!.Replace('\\', '/') + "/" + folder;
                EnsureFolder(directory);
                path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + name + ".asset");
            }

            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            asset.EnsureAuthoringId();
            AssetDatabase.CreateAsset(asset, path);
            Undo.RegisterCreatedObjectUndo(asset, undoName);
            if (set != null)
            {
                Undo.RecordObject(set, undoName);
                set.Add(asset);
                EditorUtility.SetDirty(set);
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }

        public static void EnsureFolder(string directory)
        {
            if (AssetDatabase.IsValidFolder(directory))
            {
                return;
            }

            string parent = Path.GetDirectoryName(directory)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(directory));
        }

        /// <summary>The definitions plus every narrative definition they reference, in discovery order.</summary>
        public static List<ScriptableObject> Closure(IEnumerable<ScriptableObject> roots)
        {
            var result = new List<ScriptableObject>();
            var seen = new HashSet<int>();
            var queue = new Queue<ScriptableObject>();
            foreach (ScriptableObject root in roots)
            {
                if (root != null && root is INarrativeDefinition && seen.Add(root.GetInstanceID()))
                {
                    queue.Enqueue(root);
                }
            }

            while (queue.Count > 0)
            {
                ScriptableObject next = queue.Dequeue();
                result.Add(next);
                foreach (ScriptableObject referenced in NarrativeBake.References(next))
                {
                    if (referenced is INarrativeDefinition && seen.Add(referenced.GetInstanceID()))
                    {
                        queue.Enqueue(referenced);
                    }
                }
            }

            return result;
        }

        /// <summary>The converted (unfrozen) model set of the closure of <paramref name="roots"/>.</summary>
        public static NarrativeModelSet Models(string worldId, params ScriptableObject[] roots)
        {
            NarrativeConversion conversion = NarrativeContent.Convert(worldId, Closure(roots), NarrativeBake.Converters());
            return conversion.Models;
        }

        /// <summary>The problems of a definition's closure as diagnostics (code taken from the problem text).</summary>
        public static List<GameplayDiagnostic> Diagnose(ScriptableObject definition)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (definition == null)
            {
                return diagnostics;
            }

            NarrativeModelSet models = Models(string.Empty, definition);
            string subject = definition is IAuthoredObject authored ? authored.AuthoringId : definition.name;
            for (int i = 0; i < models.Problems.Count; i++)
            {
                string problem = models.Problems[i];
                Match code = CodePattern.Match(problem);
                diagnostics.Add(new GameplayDiagnostic(code.Success ? code.Value : NarrativeDiagnosticCodes.ContentMissingReference, subject, problem));
            }

            return diagnostics;
        }

        /// <summary>Throws the first diagnostic of a definition (tools call it after a change).</summary>
        public static void ThrowIfInvalid(ScriptableObject definition)
        {
            List<GameplayDiagnostic> diagnostics = Diagnose(definition);
            if (diagnostics.Count > 0)
            {
                throw new ArgumentException(diagnostics[0].Code + ": " + diagnostics[0].Message);
            }
        }

        /// <summary>
        /// Parses a test state. Terms (separated by ';' or ','): <c>fact.&lt;name&gt;=v</c>, <c>item.&lt;item ref&gt;=n</c>,
        /// <c>currency=n</c>, <c>quest.&lt;quest ref&gt;.status|stage|branch=v</c>, <c>visited.&lt;graph ref&gt;.&lt;node&gt;=0|1</c>,
        /// <c>now=ms</c>, <c>fired=n</c>, <c>cooldown=ms</c>, <c>counter=n</c> (the last three go to <paramref name="rule"/>).
        /// </summary>
        public static StateSnapshot ParseState(string text, NarrativeModelSet models, out RuleState rule)
        {
            var state = new StateSnapshot();
            int fired = 0;
            int cooldown = 0;
            int counter = 0;
            if (models.PlayerInventory != null)
            {
                state.ActorInventoryKey = models.PlayerInventory.Key;
            }

            foreach (FactModel fact in models.Facts)
            {
                state.SetFact(fact.Key, fact.Initial);
            }

            string[] terms = (text ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < terms.Length; i++)
            {
                string term = terms[i].Trim();
                int eq = term.IndexOf('=');
                if (eq <= 0 || !int.TryParse(term.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": '" + term + "' is not name=integer");
                }

                string name = term.Substring(0, eq).Trim();
                if (name == "now")
                {
                    state.NowMs = value;
                }
                else if (name == "currency")
                {
                    state.SetCurrency(state.ActorInventoryKey, value);
                }
                else if (name == "fired")
                {
                    fired = value;
                }
                else if (name == "cooldown")
                {
                    cooldown = value;
                }
                else if (name == "counter")
                {
                    counter = value;
                }
                else if (name.StartsWith("fact.", StringComparison.Ordinal))
                {
                    string fact = name.Substring(5);
                    if (!models.TryGetFactByName(fact, out FactModel? model) || model == null)
                    {
                        throw new ArgumentException(NarrativeDiagnosticCodes.FactUnknown + ": no fact '" + fact + "'");
                    }

                    state.SetFact(model.Key, value);
                }
                else if (name.StartsWith("item.", StringComparison.Ordinal))
                {
                    state.SetItem(state.ActorInventoryKey, Resolve(models, name.Substring(5), NarrativeDiagnosticCodes.ItemUnknown), value);
                }
                else if (name.StartsWith("quest.", StringComparison.Ordinal))
                {
                    string rest = name.Substring(6);
                    int dot = rest.LastIndexOf('.');
                    if (dot <= 0)
                    {
                        throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": '" + term + "' is quest.<quest>.status|stage|branch=v");
                    }

                    int quest = Resolve(models, rest.Substring(0, dot), NarrativeDiagnosticCodes.QuestUnknown);
                    string field = rest.Substring(dot + 1);
                    QuestField which = field == "status" ? QuestField.Status : field == "stage" ? QuestField.Stage : QuestField.Branch;
                    state.SetQuest(quest, which, value);
                }
                else if (name.StartsWith("visited.", StringComparison.Ordinal))
                {
                    string rest = name.Substring(8);
                    int dot = rest.LastIndexOf('.');
                    if (dot <= 0 || !int.TryParse(rest.Substring(dot + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int node))
                    {
                        throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": '" + term + "' is visited.<graph>.<node>=0|1");
                    }

                    state.SetVisited(Resolve(models, rest.Substring(0, dot), NarrativeDiagnosticCodes.GraphUnknown), node, value != 0);
                }
                else
                {
                    throw new ArgumentException(NarrativeDiagnosticCodes.ConditionInvalid + ": unknown state term '" + name + "'");
                }
            }

            rule = new RuleState(fired, cooldown, counter);
            return state;
        }

        private static int Resolve(NarrativeModelSet models, string reference, string code)
        {
            if (models.TryResolve(reference, out int key))
            {
                return key;
            }

            throw new ArgumentException(code + ": nothing named '" + reference + "' in the content");
        }
    }
}
