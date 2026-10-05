// Hollowmere - StudioAuthor: how the P3.1 authoring script talks to the Studio edit engine.
//
// Every content step is one change set applied through ChangeSetEngine.Apply (validated, journaled under
// Studio/History, undoable) with origin Manual and an intent that starts with "[P3.1:<step id>]". A step whose intent
// prefix already has an Applied journal entry is skipped, which makes AuthorAll idempotent: a second run applies no
// change set and changes no asset. Operations name their targets by AuthoringRef (built from the loaded object) and
// their reference arguments by asset path, which the engine resolves through the semantic index.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.Authoring
{
    /// <summary>Applies P3.1 steps as journaled change sets (see the file header).</summary>
    public sealed class StudioAuthor
    {
        public const string Prefix = "[P3.1:";

        private readonly HashSet<string> applied = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> log = new List<string>();
        private int ops;

        public StudioAuthor(StudioRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Refresh();
        }

        public StudioRuntime Runtime { get; }

        /// <summary>Steps applied by this run.</summary>
        public List<string> AppliedNow { get; } = new List<string>();

        /// <summary>Steps skipped because the journal already holds them.</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>Applied change sets per tool id (this run).</summary>
        public Dictionary<string, int> ToolCounts { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public IReadOnlyList<string> Log => log;

        /// <summary>Re-reads the journal's applied P3.1 step ids.</summary>
        public void Refresh()
        {
            applied.Clear();
            foreach (JournalRecord record in Runtime.Journal.List())
            {
                if (record.State == ChangeSetState.Applied && record.Intent.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    int end = record.Intent.IndexOf(']');
                    if (end > Prefix.Length)
                    {
                        applied.Add(record.Intent.Substring(Prefix.Length, end - Prefix.Length));
                    }
                }
            }
        }

        public bool IsApplied(string step) => applied.Contains(step);

        /// <summary>Why the narrative definition types cannot be authored through the Studio yet (null = they can).</summary>
        public string? NarrativeBlocked { get; private set; }

        /// <summary>Steps not attempted, with the reason.</summary>
        public List<string> Blocked { get; } = new List<string>();

        public void Block(string steps, string reason)
        {
            Blocked.Add(steps + ": " + reason);
        }

        /// <summary>Describes each type through the Studio identity; the first failure blocks the narrative steps.</summary>
        public void CheckNarrativeTypes(params Type[] types)
        {
            NarrativeBlocked = null;
            foreach (Type type in types)
            {
                try
                {
                    Runtime.Identity.Describe(type);
                }
                catch (InvalidOperationException error)
                {
                    NarrativeBlocked = type.Name + ": " + error.Message;
                    return;
                }
            }
        }

        /// <summary>
        /// Applies one step: <paramref name="build"/> produces the operations (called only when the step is not journaled
        /// yet). Throws with the engine's diagnostics when the change set is not Applied.
        /// </summary>
        public bool Step(string step, string intent, Func<List<Operation>> build, IReadOnlyList<ArtifactRef>? artifacts = null)
        {
            if (applied.Contains(step))
            {
                Skipped.Add(step);
                return false;
            }

            List<Operation> operations = build();
            if (operations.Count == 0)
            {
                Skipped.Add(step);
                return false;
            }

            var changeSet = new ChangeSet(
                IdDerivation.NewChangeSetId(),
                ChangeSet.SchemaId,
                new Intent(Prefix + step + "] " + intent, IntentOrigin.Manual),
                operations,
                artifacts: artifacts);
            ApplyReport report = Runtime.Engine.Apply(changeSet);
            if (!report.Ok)
            {
                var problems = new StringBuilder();
                foreach (Diagnostic diagnostic in report.Diagnostics)
                {
                    problems.Append("\n  ").Append(diagnostic.Code).Append(": ").Append(diagnostic.Message);
                    if (!string.IsNullOrEmpty(diagnostic.Hint))
                    {
                        problems.Append(" (").Append(diagnostic.Hint).Append(')');
                    }
                }

                foreach (OperationOutcome outcome in report.Outcomes)
                {
                    problems.Append("\n  op ").Append(outcome.OpId).Append(' ').Append(outcome.Status.ToString());
                }

                throw new InvalidOperationException("P3.1 step " + step + " was not applied (" + report.State + "):" + problems);
            }

            applied.Add(step);
            AppliedNow.Add(step);
            for (int i = 0; i < operations.Count; i++)
            {
                ToolCounts[operations[i].Tool] = ToolCounts.TryGetValue(operations[i].Tool, out int n) ? n + 1 : 1;
            }

            ops += operations.Count;
            log.Add(step + ": " + operations.Count.ToString(CultureInfo.InvariantCulture) + " ops, " + report.Milliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms");
            return true;
        }

        public int OperationsApplied => ops;

        // ------------------------------------------------------------------ operation builders

        /// <summary>The AuthoringRef of a loaded object (asset or scene object).</summary>
        public AuthoringRef Ref(UnityEngine.Object target)
        {
            if (target == null)
            {
                throw new InvalidOperationException("P3.1: a null target has no AuthoringRef");
            }

            bool inScene = target is GameObject go ? go.scene.IsValid() : target is Component c && c.gameObject.scene.IsValid();
            return Runtime.Resolver.BuildRef(target, inScene ? GameCore.Studio.Model.AuthorScope.Instance : GameCore.Studio.Model.AuthorScope.Definition, true)
                ?? throw new InvalidOperationException("P3.1: " + target.name + " (" + target.GetType().Name + ") has no AuthoringRef");
        }

        /// <summary>The AuthoringRef of the asset at a path.</summary>
        public AuthoringRef Ref(string assetPath)
        {
            UnityEngine.Object? asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException("P3.1: no asset at " + assetPath);
            }

            return Ref(asset);
        }

        public static Operation Create(string opId, string type, string path, string name, string authoringId, JObject? fields = null)
        {
            var args = new JObject { ["type"] = type, ["path"] = path, ["name"] = name, ["authoringId"] = authoringId };
            if (fields != null)
            {
                args["fields"] = fields;
            }

            return new Operation(opId, "create", null, args);
        }

        public static Operation Set(string opId, AuthoringRef target, JObject fields) =>
            new Operation(opId, "set", target, new JObject { ["fields"] = fields });

        public static Operation Assign(string opId, AuthoringRef target, string field, string valuePath, bool append = false)
        {
            var args = new JObject { ["field"] = field, ["value"] = valuePath };
            if (append)
            {
                args["append"] = true;
            }

            return new Operation(opId, "assign", target, args);
        }

        public static Operation Call(string opId, string tool, AuthoringRef? target, JObject args) => new Operation(opId, tool, target, args);

        /// <summary>An operation that runs after <paramref name="dependsOn"/> (its refs may name objects those ops create).</summary>
        public static Operation After(Operation op, params string[] dependsOn) => new Operation(op.OpId, op.Tool, op.Target, op.Args, dependsOn);

        /// <summary>Retains generated bytes in Studio/Artifacts (content addressed) for an asset.import.</summary>
        public ArtifactRef Retain(byte[] bytes, string mediaType, string name, string producerOp, string role)
        {
            var artifact = new ArtifactRef(ContentStamp.Sha256Hex(bytes), mediaType, bytes.LongLength, name, new ArtifactProducer(op: producerOp, provider: "procedural"), role);
            Runtime.Artifacts.Put(bytes, artifact);
            return artifact;
        }

        /// <summary>A verified import of a retained artifact to an asset path.</summary>
        public static Operation Import(string opId, string path, ArtifactRef artifact, JObject? importer = null)
        {
            var args = new JObject { ["path"] = path, ["artifact"] = new JObject { ["artifact"] = artifact.Reference } };
            if (importer != null)
            {
                args["importer"] = importer;
            }

            return new Operation(opId, "asset.import", null, args);
        }

        // ------------------------------------------------------------------ values

        /// <summary>A stable authoring id for a P3.1 object (UUID-shaped, derived from its key).</summary>
        public static string Id(string key)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("hollowmere.p3_1." + key));
                hash[6] = (byte)((hash[6] & 0x0F) | 0x40);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
                var hex = new StringBuilder(36);
                for (int i = 0; i < 16; i++)
                {
                    if (i == 4 || i == 6 || i == 8 || i == 10)
                    {
                        hex.Append('-');
                    }

                    hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }

        public static JArray V3(float x, float y, float z) => new JArray(x, y, z);

        public static JArray V2(float x, float y) => new JArray(x, y);

        /// <summary>A JSON string, or a JSON null for a null string (Newtonsoft turns a null string into a String token).</summary>
        public static JToken Str(string? value) => value == null ? JValue.CreateNull() : new JValue(value);

        public static JArray Color(float r, float g, float b, float a = 1f) => new JArray(r, g, b, a);

        public static JArray V3(Vector3 v) => new JArray(v.x, v.y, v.z);

        public static JArray Color(Color c) => new JArray(c.r, c.g, c.b, c.a);

        public static JArray Strings(params string[] values) => new JArray(values);
    }
}
