// GameCore.Gameplay.Npc - the npc plugin as a world extension (P1.3).
//
// Validate turns the roster and the baked manifest into NPC records: every placed entity whose definition an
// NpcDefinition of the roster names is an NPC (in manifest order, sorted by key in the module). Attach seeds the eleven
// npc slots of every NPC from its baked placement (unless the root comes from a restore) and hands the system its
// module. An NPC with a schedule is seeded with schedulePhase = -1 so that its first update enters the current phase.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Npc;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Npc
{
    /// <summary>The npc plugin's world extension.</summary>
    public sealed class NpcWorldExtension : IGameplayWorldExtension
    {
        private readonly List<GameplayPluginMount> plugins;
        private readonly List<GameplaySystem> systems;
        private readonly List<NpcRecord> records = new List<NpcRecord>();
        private readonly Dictionary<TargetId, ManifestEntity> placements = new Dictionary<TargetId, ManifestEntity>();

        public NpcWorldExtension(NpcRoster? roster)
        {
            Roster = roster;
            plugins = new List<GameplayPluginMount>
            {
                new GameplayPluginMount(new CatalogPluginDeclaration(NpcDeclarations.Manifest(), ConfigDocument.Empty), NpcDeclarations.Instance),
            };
            systems = new List<GameplaySystem>
            {
                new GameplaySystem(NpcDeclarations.CommandSystem, NpcDeclarations.CommandSystemRegistration()),
            };
        }

        public string Name => "npc";

        public NpcRoster? Roster { get; }

        public NpcModule? Module { get; private set; }

        public IReadOnlyList<NpcRecord> Records => records;

        public IReadOnlyList<GameplayPluginMount> Plugins => plugins;

        public IReadOnlyList<GameplaySystem> Systems => systems;

        public IReadOnlyList<CommandRoute> Routes => NpcDeclarations.Routes();

        public IReadOnlyList<MessageBufferDescriptor> Lanes => NpcDeclarations.Lanes();

        public void BindReaders(CommandPayloadReaders readers) => NpcReaders.BindInto(readers);

        public void Validate(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            records.Clear();
            placements.Clear();
            if (Roster == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Roster.Npcs.Count; i++)
            {
                NpcDefinition npc = Roster.Npcs[i];
                if (npc == null || npc.Entity == null)
                {
                    throw new InvalidOperationException(PlayerNpcInteractionCodes.NpcMissingEntityDefinition + ": roster "
                        + Roster.name + " lists an NPC without an entity definition");
                }

                if (!seen.Add(npc.Entity.AuthoringId))
                {
                    throw new InvalidOperationException(PlayerNpcInteractionCodes.NpcDuplicateEntityDefinition + ": two NPCs of roster "
                        + Roster.name + " name entity definition " + npc.Entity.name);
                }
            }

            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                ManifestEntity entity = manifest.Entities[i];
                NpcDefinition? npc = Roster.FindByEntityDefinition(entity.definitionId);
                if (npc == null || !AuthoringIds.IsValid(entity.authoringId))
                {
                    continue;
                }

                BehaviourDefinition? behaviour = npc.Behaviour;
                ScheduleDefinition? schedule = npc.Schedule;
                var record = new NpcRecord(
                    AuthoringIds.TargetIdFor(entity.authoringId),
                    entity.authoringId,
                    entity.name,
                    npc,
                    npc.ToProfile(),
                    behaviour != null ? behaviour.Route() : new List<PatrolPoint>(),
                    schedule != null ? schedule.ToPhases() : new List<SchedulePhase>(),
                    schedule != null ? schedule.DayLengthMilliseconds : 0,
                    schedule != null ? schedule.StartOffsetMilliseconds : 0);
                records.Add(record);
                placements[record.Target] = entity;
            }
        }

        public void Attach(GameplayWorld world, bool seedSlots)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            GameApplicationRoot root = world.Root;
            if (seedSlots)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    Seed(root, records[i], placements[records[i].Target]);
                }
            }

            NpcCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<NpcCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException("the npc command system is not registered in world " + root.Host.DiagnosticName);
            }

            int step = Roster != null ? Roster.StepMilliseconds : 20;
            int stride = Roster != null ? Roster.UnloadedStride : 8;
            Module = new NpcModule(root.Host, root.Registry, step, stride, records)
            {
                IsRegionResident = key =>
                    world.Worlds.TryRegion(key, out RegionRecord? region) && region != null
                    && world.Slots.ReadOrDefault(region.Target, GameplaySlots.WorldOwner, GameplaySlots.Residency, 0) == (int)RegionResidency.Resident,
            };
            system.Module = Module;
        }

        public void Detach(GameplayWorld world)
        {
        }

        private static void Seed(GameApplicationRoot root, NpcRecord record, ManifestEntity placement)
        {
            int behaviourCode = record.Definition != null && record.Definition.Behaviour != null
                ? (int)record.Definition.Behaviour.Kind
                : NpcSlots.Idle;
            NpcSnapshot placed = NpcRules.Placed(
                placement.x,
                placement.z,
                placement.yaw,
                (NpcStateCode)behaviourCode,
                record.Route,
                record.Definition != null ? record.Definition.Mood : 0);
            if (record.HasSchedule)
            {
                placed = placed.With(schedulePhase: -1);
            }

            Write(root, record.Target, NpcSlots.State, placed.State);
            Write(root, record.Target, NpcSlots.Behaviour, placed.Behaviour);
            Write(root, record.Target, NpcSlots.PatrolIndex, placed.PatrolIndex);
            Write(root, record.Target, NpcSlots.Mood, placed.Mood);
            Write(root, record.Target, NpcSlots.SchedulePhase, placed.SchedulePhase);
            Write(root, record.Target, NpcSlots.TargetX, placed.TargetX);
            Write(root, record.Target, NpcSlots.TargetZ, placed.TargetZ);
            Write(root, record.Target, NpcSlots.PosX, placed.PosX);
            Write(root, record.Target, NpcSlots.PosZ, placed.PosZ);
            Write(root, record.Target, NpcSlots.Yaw, placed.Yaw);
            Write(root, record.Target, NpcSlots.TimerMs, placed.TimerMilliseconds);
        }

        private static void Write(GameApplicationRoot root, TargetId target, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(target, NpcSlots.Owner, slot, GameplaySlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding npc " + target + " failed: " + code + ": " + detail);
            }
        }
    }
}
