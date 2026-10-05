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
    /// <summary>A standing behaviour: idle, a patrol route with waits, or a custom id another system interprets.</summary>
    [Authorable("npc.behaviour", DisplayName = "NPC Behaviour", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A standing NPC behaviour: idle, a looping patrol route (world-space points) with a wait at each point, or a custom behaviour id.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/NPC Behaviour", fileName = "Behaviour")]
    public sealed class BehaviourDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Structural = true, Doc = "Idle, Patrol or Custom.")]
        [SerializeField] private NpcBehaviourKind kind = NpcBehaviourKind.Idle;

        [AuthorField(Unit = "m", Doc = "Patrol points in world space, visited in order and looped.")]
        [SerializeField] private List<Vector3> patrolPoints = new List<Vector3>();

        [AuthorField(Unit = "s", Min = 0, Max = 600, Doc = "Wait at each patrol point.")]
        [SerializeField] private float waitSeconds = 1.5f;

        [AuthorField(Doc = "Custom behaviour id (Custom kind only).")]
        [SerializeField] private string customId = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public NpcBehaviourKind Kind => kind;

        public IReadOnlyList<Vector3> PatrolPoints => patrolPoints;

        public float WaitSeconds => waitSeconds;

        public string CustomId => customId;

        public int WaitMilliseconds => GameplayUnits.ToMilliseconds(waitSeconds);

        /// <summary>The patrol route in millimetres (x, z).</summary>
        public List<PatrolPoint> Route()
        {
            var route = new List<PatrolPoint>(patrolPoints.Count);
            for (int i = 0; i < patrolPoints.Count; i++)
            {
                route.Add(new PatrolPoint(GameplayUnits.ToMillimetres(patrolPoints[i].x), GameplayUnits.ToMillimetres(patrolPoints[i].z)));
            }

            return route;
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(NpcBehaviourKind value, IEnumerable<Vector3>? points, float wait, string custom)
        {
            kind = value;
            patrolPoints = points != null ? new List<Vector3>(points) : new List<Vector3>();
            waitSeconds = wait;
            customId = custom ?? string.Empty;
        }

        public void SetPatrol(IEnumerable<Vector3> points)
        {
            kind = NpcBehaviourKind.Patrol;
            patrolPoints = new List<Vector3>(points);
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
