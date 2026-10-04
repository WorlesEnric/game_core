// GameCore.Studio fixtures - plugin tools discovered from [AuthorOperation] (catalog export, reflected binding, and a
// tool that throws to exercise AllOrNothing rollback).
#nullable enable
using System;
using GameCore.Studio.Model;

namespace GameCore.Studio.Fixtures
{
    public static class FixtureTools
    {
        [AuthorOperation("fixture.setGreeting", Tier = ToolTier.Configure, Doc = "Set an NPC's greeting.")]
        public static void SetGreeting(FixtureNpcDefinition npc, [AuthorArg(Doc = "The new greeting.")] string greeting)
        {
            npc.greeting = greeting;
        }

        [AuthorOperation("fixture.fail", Tier = ToolTier.Configure, Doc = "Always throws (rollback tests).")]
        public static void Fail(FixtureNpcDefinition npc, [AuthorArg(Doc = "Exception message.")] string message)
        {
            throw new InvalidOperationException(message + " (" + npc.name + ")");
        }
    }
}
