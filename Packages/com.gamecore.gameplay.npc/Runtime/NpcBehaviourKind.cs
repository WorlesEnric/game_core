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
    /// <summary>Standing behaviour kinds an author can pick (the npc.state codes of standing behaviours).</summary>
    public enum NpcBehaviourKind
    {
        Idle = NpcSlots.Idle,
        Patrol = NpcSlots.Patrol,
        Custom = NpcSlots.Custom,
    }
}
