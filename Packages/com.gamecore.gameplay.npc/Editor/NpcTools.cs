// GameCore.Gameplay.Npc.Editor - npc authoring operations and the npc validator (P1.3, Studio 03 s4/s5).
//
//   npc.addAt         place an NPC (its definition's entity definition) at a location in the active region scene
//   npc.setPatrol     set the patrol points of an NPC's behaviour (creating the behaviour asset beside it when missing)
//   npc.setSchedule   assign a (well-formed) day schedule to an NPC
//   npc.setBehaviour  assign a standing behaviour to an NPC
//   npc.setDialogue   assign the dialogue graph a talk starts (P1.7b)
//   npc.setAppearance select one of the NPC's entity variants as its appearance (P1.7b)
//
// NpcRefMigration (authoring.migrateRefs, P1.7b) moves the P1.3 string dialogue graph ids into the typed reference.
//
// Every tool validates before it changes anything, records Undo, marks what it edited dirty and refuses with an
// ArgumentException whose message starts with the GP-* code.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Npc.Editor
{
    /// <summary>The npc.* authoring operations.</summary>
    public static class NpcTools
    {
        [AuthorOperation("npc.addAt", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "world.region", RequiresOnTarget = "npc.definition",
            Doc = "Places an NPC (its definition's entity) at a location (m) and heading (deg) in the active region scene.")]
        public static AuthoredEntity AddAt(
            NpcDefinition npc,
            [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 location,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Object name; defaults to the NPC's display name.")] string name = "")
        {
            return AddAtIn(SceneManager.GetActiveScene(), npc, location, yaw, name);
        }

        /// <summary><see cref="AddAt"/> into an explicit scene (authoring scripts and tests).</summary>
        public static AuthoredEntity AddAtIn(Scene scene, NpcDefinition npc, Vector3 location, float yaw, string name)
        {
            if (npc == null || npc.Entity == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition with an entity definition is required");
            }

            AuthoredEntity entity = EntityTools.PlaceIn(scene, npc.Entity, location, yaw, string.IsNullOrEmpty(name) ? npc.DisplayName : name);
            IAuthoredRegion? region = entity.Region;
            if (region == null || !region.ContainsPoint(location.x, location.y, location.z))
            {
                Undo.DestroyObjectImmediate(entity.gameObject);
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcNotPlaced + ": the location lies outside the scene's region");
            }

            return entity;
        }

        [AuthorOperation("npc.setPatrol", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "npc.definition",
            Doc = "Sets an NPC's patrol route (world-space points, looped); its behaviour becomes Patrol.")]
        public static BehaviourDefinition SetPatrol(
            NpcDefinition npc,
            [AuthorArg(Unit = "m", Doc = "Patrol points (at least one), world space.")] Vector3[] points,
            [AuthorArg(Unit = "s", Min = 0, Max = 600, Required = false, Doc = "Wait at each point.")] float waitSeconds = 1.5f)
        {
            if (npc == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition is required");
            }

            if (points == null || points.Length == 0)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcPatrolEmpty + ": a patrol needs at least one point");
            }

            BehaviourDefinition? behaviour = npc.Behaviour;
            if (behaviour == null)
            {
                behaviour = ScriptableObject.CreateInstance<BehaviourDefinition>();
                behaviour.EnsureAuthoringId();
                string npcPath = AssetDatabase.GetAssetPath(npc);
                string directory = string.IsNullOrEmpty(npcPath) ? "Assets" : Path.GetDirectoryName(npcPath)!.Replace('\\', '/');
                AssetDatabase.CreateAsset(behaviour, AssetDatabase.GenerateUniqueAssetPath(directory + "/" + npc.name + "Behaviour.asset"));
                Undo.RegisterCreatedObjectUndo(behaviour, "npc.setPatrol");
                Undo.RecordObject(npc, "npc.setPatrol");
                npc.SetBehaviour(behaviour);
                EditorUtility.SetDirty(npc);
            }

            Undo.RecordObject(behaviour, "npc.setPatrol");
            behaviour.Configure(NpcBehaviourKind.Patrol, points, waitSeconds, behaviour.CustomId);
            EditorUtility.SetDirty(behaviour);
            return behaviour;
        }

        [AuthorOperation("npc.setSchedule", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "npc.definition",
            Doc = "Assigns a day schedule (phases by world time) to an NPC; null removes it.")]
        public static void SetSchedule(NpcDefinition npc, [AuthorArg(Category = "npc.schedule", Required = false)] ScheduleDefinition? schedule)
        {
            if (npc == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition is required");
            }

            if (schedule != null && !schedule.IsWellFormed)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcScheduleMalformed
                    + ": phase starts must ascend strictly and lie inside the day (the last phase wraps past midnight)");
            }

            Undo.RecordObject(npc, "npc.setSchedule");
            npc.SetSchedule(schedule);
            EditorUtility.SetDirty(npc);
        }

        [AuthorOperation("npc.setBehaviour", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "npc.definition",
            Doc = "Assigns a standing behaviour (idle, patrol or custom) to an NPC; null makes it idle.")]
        public static void SetBehaviour(NpcDefinition npc, [AuthorArg(Category = "npc.behaviour", Required = false)] BehaviourDefinition? behaviour)
        {
            if (npc == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition is required");
            }

            if (behaviour != null && behaviour.Kind == NpcBehaviourKind.Patrol && behaviour.PatrolPoints.Count == 0)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcPatrolEmpty + ": a patrol behaviour needs at least one point");
            }

            Undo.RecordObject(npc, "npc.setBehaviour");
            npc.SetBehaviour(behaviour);
            EditorUtility.SetDirty(npc);
        }

        [AuthorOperation("npc.setDialogue", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "npc.definition",
            Doc = "Assigns the dialogue graph a talk with the NPC starts; null removes it (the NPC has nothing to say).")]
        public static void SetDialogue(
            NpcDefinition npc,
            [AuthorArg(Category = "dialogue.graph", Required = false, Doc = "The dialogue graph (a DialogueGraphDefinition).")] ScriptableObject? graph)
        {
            if (npc == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition is required");
            }

            if (graph != null && !AuthoredAssetLookup.IsNullOrOfType(graph, "dialogue.graph"))
            {
                throw new ArgumentException(AuthoringHardeningCodes.WrongReferenceCategory + ": " + graph.name + " is not a dialogue.graph");
            }

            Undo.RecordObject(npc, "npc.setDialogue");
            npc.SetDialogue(graph);
            EditorUtility.SetDirty(npc);
        }

        [AuthorOperation("npc.setAppearance", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(NpcValidator), Requires = "npc.definition",
            Doc = "Selects the NPC's appearance: one of its entity definition's variants; null uses the definition itself.")]
        public static void SetAppearance(
            NpcDefinition npc,
            [AuthorArg(Category = "entity.variant", Required = false, Doc = "A variant of the NPC's entity definition.")] VariantDefinition? variant)
        {
            if (npc == null || npc.Entity == null)
            {
                throw new ArgumentException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": an NPC definition with an entity definition is required");
            }

            if (variant != null && !Contains(npc.Entity.Variants, variant))
            {
                throw new ArgumentException(AuthoringHardeningCodes.NpcAppearanceNotVariant + ": " + variant.name + " is not a variant of " + npc.Entity.name);
            }

            Undo.RecordObject(npc, "npc.setAppearance");
            npc.SetAppearance(variant);
            EditorUtility.SetDirty(npc);
        }

        private static bool Contains(IReadOnlyList<VariantDefinition> variants, VariantDefinition variant)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i] == variant)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>authoring.migrateRefs for NPC definitions: string dialogue graph ids become the typed dialogue reference.</summary>
    public sealed class NpcRefMigration : IAuthoringRefMigration
    {
        public string MigrationId => "npc.dialogueGraph";

        public AuthoringMigrationReport Migrate(IReadOnlyList<string> folders, bool apply)
        {
            var report = new AuthoringMigrationReport(MigrationId);
            IReadOnlyList<ScriptableObject>? graphs = null;
            IReadOnlyList<NpcDefinition> npcs = AuthoredAssetLookup.AssetsOf<NpcDefinition>(folders);
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcDefinition npc = npcs[i];
                string legacy = npc.LegacyDialogueGraph;
                if (legacy.Length == 0)
                {
                    continue;
                }

                if (graphs == null)
                {
                    graphs = AuthoredAssetLookup.AssetsOfType("dialogue.graph", null);
                }

                string where = AuthoredAssetLookup.Describe(npc) + ": dialogueGraph '" + legacy + "'";
                ScriptableObject? graph = npc.Dialogue != null ? npc.Dialogue : AuthoredAssetLookup.Resolve(legacy, graphs, out bool _);
                if (graph == null)
                {
                    report.AddUnresolved(where + " names no dialogue graph (or more than one)");
                    continue;
                }

                report.AddChanged(where + " -> dialogue " + AuthoredAssetLookup.Describe(graph));
                if (apply)
                {
                    Undo.RecordObject(npc, "authoring.migrateRefs");
                    npc.SetDialogue(graph);
                    EditorUtility.SetDirty(npc);
                }
            }

            return report;
        }
    }

    /// <summary>Validates NPC rosters and definitions.</summary>
    [AuthorValidator("npc.validator", Codes = new[]
    {
        PlayerNpcInteractionCodes.NpcMissingEntityDefinition,
        PlayerNpcInteractionCodes.NpcDuplicateEntityDefinition,
        PlayerNpcInteractionCodes.NpcSpeedOutOfRange,
        PlayerNpcInteractionCodes.NpcPatrolEmpty,
        PlayerNpcInteractionCodes.NpcScheduleMalformed,
        AuthoringHardeningCodes.NpcAppearanceNotVariant,
        AuthoringHardeningCodes.LegacyReference,
        AuthoringHardeningCodes.WrongReferenceCategory,
    })]
    public static class NpcValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(NpcRoster roster)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (roster == null)
            {
                return diagnostics;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < roster.Npcs.Count; i++)
            {
                NpcDefinition npc = roster.Npcs[i];
                if (npc == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcMissingEntityDefinition, roster.AuthoringId, roster.name + " lists a missing NPC"));
                    continue;
                }

                if (npc.Entity != null && !seen.Add(npc.Entity.AuthoringId))
                {
                    diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcDuplicateEntityDefinition, npc.AuthoringId,
                        npc.name + " shares its entity definition with another NPC of " + roster.name));
                }

                diagnostics.AddRange(Validate(npc));
            }

            return diagnostics;
        }

        public static IReadOnlyList<GameplayDiagnostic> Validate(NpcDefinition npc)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (npc == null)
            {
                return diagnostics;
            }

            if (npc.Entity == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcMissingEntityDefinition, npc.AuthoringId, npc.name + " names no entity definition"));
            }

            if (npc.Speed < 0.1f || npc.Speed > 10f)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcSpeedOutOfRange, npc.AuthoringId, npc.name + " walks outside 0.1..10 m/s"));
            }

            if (npc.Behaviour != null && npc.Behaviour.Kind == NpcBehaviourKind.Patrol && npc.Behaviour.PatrolPoints.Count == 0)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcPatrolEmpty, npc.AuthoringId, npc.name + " patrols an empty route"));
            }

            if (npc.Schedule != null && !npc.Schedule.IsWellFormed)
            {
                diagnostics.Add(new GameplayDiagnostic(PlayerNpcInteractionCodes.NpcScheduleMalformed, npc.AuthoringId, npc.name + " has a malformed schedule"));
            }

            if (npc.Appearance != null && npc.AppearanceVariant < 0)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.NpcAppearanceNotVariant, npc.AuthoringId,
                    npc.name + " appears as " + npc.Appearance.name + ", which is not a variant of its entity definition"));
            }

            if (npc.Dialogue == null && npc.LegacyDialogueGraph.Length > 0)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.LegacyReference, npc.AuthoringId,
                    npc.name + " names its dialogue graph by the string '" + npc.LegacyDialogueGraph + "'; run authoring.migrateRefs"));
            }

            if (!AuthoredAssetLookup.IsNullOrOfType(npc.Dialogue, "dialogue.graph"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, npc.AuthoringId, npc.name + "'s dialogue is not a dialogue.graph"));
            }

            return diagnostics;
        }
    }

    /// <summary>Contributes the npc plugin, its command system, layout and schemas to the bake's catalog description.</summary>
    public sealed class NpcCatalogContributor : GameCore.Gameplay.Compile.IGameplayCatalogContributor
    {
        public GameCore.Gameplay.Compile.GameplayCatalogContribution Contribution =>
            new GameCore.Gameplay.Compile.GameplayCatalogContribution("com.gamecore.gameplay.npc", NpcDeclarations.CatalogSchemas, NpcDeclarations.CatalogEntries);
    }
}
