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
    /// <summary>One phase of a day: from a start time, a standing behaviour (and optionally a place to walk to first).</summary>
    [Serializable]
    public sealed class SchedulePhaseEntry
    {
        [AuthorField(Doc = "Phase name (e.g. day, night).")]
        public string name = string.Empty;

        [AuthorField(Unit = "s", Min = 0, Doc = "Start of the phase, seconds after the day starts.")]
        public float startSeconds;

        [AuthorField(Doc = "Standing behaviour during the phase.")]
        public NpcBehaviourKind behaviour = NpcBehaviourKind.Idle;

        [AuthorField(Doc = "Walk to the location when the phase starts.")]
        public bool hasLocation;

        [AuthorField(Unit = "m", Doc = "Where to walk at the phase start (world space).")]
        public Vector3 location;
    }

    /// <summary>A day schedule: phases by world time (step-derived, see NpcRoster.StepMilliseconds).</summary>
    [Authorable("npc.schedule", DisplayName = "NPC Schedule", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A repeating day of phases by world time; each phase sets the NPC's standing behaviour and may send it somewhere first.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/NPC Schedule", fileName = "Schedule")]
    public sealed class ScheduleDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Unit = "s", Min = 1, Max = 86400, Doc = "Length of one day.")]
        [SerializeField] private float dayLengthSeconds = 120f;

        [AuthorField(Unit = "s", Min = 0, Max = 86400, Doc = "World time of day at step 0.")]
        [SerializeField] private float startOffsetSeconds;

        [AuthorField(Doc = "Phases in ascending start order; the first starts at 0.")]
        [SerializeField] private List<SchedulePhaseEntry> phases = new List<SchedulePhaseEntry>();

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public float DayLengthSeconds => dayLengthSeconds;

        public float StartOffsetSeconds => startOffsetSeconds;

        public IReadOnlyList<SchedulePhaseEntry> Phases => phases;

        public int DayLengthMilliseconds => GameplayUnits.ToMilliseconds(dayLengthSeconds);

        public int StartOffsetMilliseconds => GameplayUnits.ToMilliseconds(startOffsetSeconds);

        public List<SchedulePhase> ToPhases()
        {
            var list = new List<SchedulePhase>(phases.Count);
            for (int i = 0; i < phases.Count; i++)
            {
                SchedulePhaseEntry entry = phases[i];
                list.Add(new SchedulePhase(
                    GameplayUnits.ToMilliseconds(entry.startSeconds),
                    (int)entry.behaviour,
                    entry.hasLocation,
                    GameplayUnits.ToMillimetres(entry.location.x),
                    GameplayUnits.ToMillimetres(entry.location.z)));
            }

            return list;
        }

        public bool IsWellFormed => ScheduleRules.IsWellFormed(DayLengthMilliseconds, ToPhases());

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(float dayLength, float startOffset, IEnumerable<SchedulePhaseEntry> entries)
        {
            dayLengthSeconds = dayLength;
            startOffsetSeconds = startOffset;
            phases = new List<SchedulePhaseEntry>(entries);
        }

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
