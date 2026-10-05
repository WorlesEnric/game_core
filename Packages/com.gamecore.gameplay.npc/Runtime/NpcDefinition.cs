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
        Doc = "An NPC kind: the entity definition (prefab) it is placed as, its display name, speed, voice, dialogue graph, behaviour and schedule.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/NPC Definition", fileName = "Npc")]
    public sealed class NpcDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The entity definition placed for this NPC (its prefab).")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Doc = "Name shown in the bubble and the talk prompt.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Unit = "m/s", Min = 0.1, Max = 10, Doc = "Walking speed.")]
        [SerializeField] private float speed = 1.8f;

        [AuthorField(Unit = "s", Min = 0.5, Max = 600, Doc = "How long a talk holds the NPC when no conversation system answers.")]
        [SerializeField] private float converseSeconds = 4f;

        [AuthorField(Unit = "m", Min = 0.01, Max = 2, Doc = "Arrival radius.")]
        [SerializeField] private float arriveRadius = 0.05f;

        [AuthorField(Doc = "Voice id (audio, P1.5).")]
        [SerializeField] private string voiceId = string.Empty;

        [AuthorRef(Category = "dialogue.graph", Required = false, Doc = "Dialogue graph reference (a string id resolved by P1.4).")]
        [SerializeField] private string dialogueGraph = string.Empty;

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

        public string DialogueGraph => dialogueGraph;

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
