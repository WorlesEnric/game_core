// GameCore.Gameplay.Save - the Studio operations of the save plugin (plugin catalog row 12, SADR-012 (studio)).
//
// `save.inspect(slot)` reads and verifies one slot and previews the forward migration a restore would run, without
// restoring it. `save.testRoundTrip` captures the running world, restores the capture into a new world, captures that
// world and compares the canonical slot hashes and logical steps; nothing is written to disk and the running world is
// kept. Both are read-only with respect to the game's content, so they carry no change set.
//
// P1.7b (B5): an Editor assembly (GameCore.Gameplay.Save.Editor) using the GameCore.Gameplay.Contracts mirror
// attributes, so the runtime package depends on no Studio package.
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.App;

namespace GameCore.Gameplay.Save
{
    /// <summary>The save plugin's Studio tools (catalog row 12).</summary>
    public static class SaveStudioOperations
    {
        /// <summary>Reads, verifies and previews one save slot.</summary>
        [AuthorOperation("save.inspect", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(SaveValidator),
            Doc = "Read and verify one save slot (header, checksums, catalog compatibility) and preview the forward slot migration a restore would run.")]
        public static SaveSlotInspection Inspect(
            SaveSchemaDefinition schema,
            [AuthorArg(Doc = "The save slot name.")] string slot,
            SaveService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _ = schema;
            return service.Inspect(slot);
        }

        /// <summary>Capture, restore into a new world, capture again, compare.</summary>
        [AuthorOperation("save.testRoundTrip", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(SaveValidator),
            Doc = "Capture the running world, restore it into a new world and compare canonical slot hashes and logical steps; writes nothing and keeps the running world.")]
        public static SaveRoundTripReport TestRoundTrip(SaveSchemaDefinition schema, SaveService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            _ = schema;
            return service.TestRoundTrip();
        }
    }
}
