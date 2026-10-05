// GameCore.Gameplay.Npc - NpcDefinition, BehaviourDefinition, ScheduleDefinition and NpcRoster (P1.3, catalog row 4).
//
// An NPC is an authored entity (AuthoredEntity + EntityDefinition, placed in a region scene and baked like any other)
// whose entity definition an NpcDefinition names. The roster lists the NpcDefinitions of a game; at boot every placed
// entity whose definition one of them names becomes an NPC. The definitions hold presentation and behaviour data in
// authoring units (m, m/s, s) and convert once to the integer rule types (mm, mm/s, ms).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Npc;
using UnityEngine;

namespace GameCore.Gameplay.Npc
{
    /// <summary>An NPC: which entity definition it is, how it moves and talks, its behaviour and schedule.</summary>
    [Authorable("npc.definition", DisplayName = "NPC", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An NPC kind: the entity definition (prefab) it is placed as, its appearance variant, display name, speed, voice, dialogue graph, behaviour and schedule.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/NPC Definition", fileName = "Npc")]
    public sealed class NpcDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Structural = true, Doc = "The entity definition placed for this NPC (its prefab).")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Doc = "Name shown in the bubble and the talk prompt.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Unit = "m/s", Min = 0.1, Max = 10, Doc = "Walking speed.")]
        [SerializeField] private float speed = 1.8f;

        [AuthorField(Unit = "s", Min = 0.5, Max = 600, Doc = "How long a talk holds the NPC when no conversation system answers.")]
        [SerializeField] private float converseSeconds = 4f;

        [AuthorField(Unit = "m", Min = 0.01, Max = 2, Doc = "Arrival radius.")]
        [SerializeField] private float arriveRadius = 0.05f;

        [AuthorRef(Category = AuthorRefCategories.AudioClip, Required = false, Doc = "Voice: a clip id of the project's audio bank (presentation only).")]
        [SerializeField] private string voiceId = string.Empty;

        [AuthorRef(Category = "dialogue.graph", Required = false, Doc = "The dialogue graph a talk starts (a DialogueGraphDefinition).")]
        [SerializeField] private ScriptableObject? dialogue;

        // Legacy (P1.3): the graph as a string id resolved by P1.4 (a graph's npcGraphRef alias, an authoring id or a
        // name). Read only when `dialogue` is empty; authoring.migrateRefs moves it into `dialogue` and clears it.
        [SerializeField, HideInInspector, AuthorRef(Category = "dialogue.graph", Required = false, Doc = "Legacy (P1.3) graph id (a graph's npcGraphRef alias, authoring id or name), read only while `dialogue` is empty; authoring.migrateRefs moves it into `dialogue` and clears it.")] private string dialogueGraph = string.Empty;

        [AuthorRef(Category = "entity.variant", Required = false, Doc = "Appearance: one of the entity definition's variants (empty: the definition itself).")]
        [SerializeField] private VariantDefinition? appearance;

        [AuthorRef(Category = "npc.behaviour", Required = false, Doc = "Standing behaviour (idle when none).")]
        [SerializeField] private BehaviourDefinition? behaviour;

        [AuthorRef(Category = "npc.schedule", Required = false, Doc = "Day schedule (none: the behaviour always applies).")]
        [SerializeField] private ScheduleDefinition? schedule;

        [AuthorField(Min = -100, Max = 100, Doc = "Initial mood.")]
        [SerializeField] private int mood;

        [AuthorField(Min = -100, Max = 100, Doc = "Focus priority against other candidates.")]
        [SerializeField] private int focusPriority = 1;

        [AuthorField(Unit = "m", Min = 0.5, Max = 5, Doc = "Talk range.")]
        [SerializeField] private float talkRange = 2.5f;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public float Speed => speed;

        public string VoiceId => voiceId;

        /// <summary>
        /// The graph reference handed to the conversation starter: the graph's authoring id when <see cref="Dialogue"/>
        /// is set, else the legacy string id (empty: no conversation).
        /// </summary>
        public string DialogueGraph => dialogue is IAuthoredObject graph && graph.AuthoringId.Length > 0 ? graph.AuthoringId : dialogueGraph;

        public ScriptableObject? Dialogue => dialogue;

        /// <summary>The legacy string graph id (P1.3) still stored; empty after authoring.migrateRefs.</summary>
        public string LegacyDialogueGraph => dialogueGraph;

        public VariantDefinition? Appearance => appearance;

        /// <summary>The variant index of <see cref="Appearance"/> in the entity definition (0: the definition itself, -1: not one of its variants).</summary>
        public int AppearanceVariant
        {
            get
            {
                if (appearance == null)
                {
                    return 0;
                }

                if (entity == null)
                {
                    return -1;
                }

                for (int i = 0; i < entity.Variants.Count; i++)
                {
                    if (entity.Variants[i] == appearance)
                    {
                        return i + 1;
                    }
                }

                return -1;
            }
        }

        public BehaviourDefinition? Behaviour => behaviour;

        public ScheduleDefinition? Schedule => schedule;

        public int Mood => mood;

        public int FocusPriority => focusPriority;

        public float TalkRange => talkRange;

        public NpcProfile ToProfile() =>
            new NpcProfile(
                GameplayUnits.ToMillimetres(speed),
                behaviour != null ? behaviour.WaitMilliseconds : NpcProfile.Default.WaitMilliseconds,
                GameplayUnits.ToMilliseconds(converseSeconds),
                Math.Max(1, GameplayUnits.ToMillimetres(arriveRadius)));

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(EntityDefinition? entityDefinition, string shownName, float walkSpeed, string voice, string graph)
        {
            entity = entityDefinition;
            displayName = shownName ?? string.Empty;
            speed = walkSpeed;
            voiceId = voice ?? string.Empty;
            dialogueGraph = graph ?? string.Empty;
        }

        public void SetBehaviour(BehaviourDefinition? value) => behaviour = value;

        /// <summary>npc.setDialogue: the graph object (clears the legacy string id).</summary>
        public void SetDialogue(ScriptableObject? graph)
        {
            dialogue = graph;
            dialogueGraph = string.Empty;
        }

        /// <summary>npc.setAppearance: one of the entity definition's variants, or null for the definition itself.</summary>
        public void SetAppearance(VariantDefinition? variant) => appearance = variant;

        public void SetVoice(string clipId) => voiceId = clipId ?? string.Empty;

        public void SetSchedule(ScheduleDefinition? value) => schedule = value;

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}
