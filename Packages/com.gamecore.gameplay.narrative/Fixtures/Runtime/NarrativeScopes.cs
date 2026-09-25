// GameCore.Gameplay.Narrative.Fixtures — the chapter-quest world definition's declared scope tree (P-010, P-006).
//
// 07 section 3.1's composition is a tree, and a world definition declares the scopes its content lives in. The tree
// is therefore part of the *initial* composition: the lane opens with it and publishes nothing for it, which is what
// keeps the composition and assembly counters on one series (P-006) — a scope is not an assembly, so a scope-edit
// publication after the join would advance the composition counter with no matching assembly publication, and the
// world could never rejoin it.
//
// The tree, exactly as the reference composition draws it:
//
//   story-world                        (the world root, declared by the world itself)
//   ├── chapter-one                    [ChapterNarrative]
//   │   ├── village                    npc-mara, gate-east, crowd-prop
//   │   ├── grove                      encounter-oak
//   │   └── museum                     [CapabilityIsolation: *]  npc-display
//   └── chapter-two                    [ChapterNarrative]
//       └── harbor                     npc-sailor
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>The world definition's scope tree, as scope records the lane opens with.</summary>
    public static class NarrativeScopes
    {
        /// <summary>Number of scopes the declaration contains including the world root.</summary>
        public const int DeclaredScopeCount = 7;

        /// <summary>
        /// The six child scopes of the world root, in declaration order. `museum` carries the reference
        /// composition's complete capability boundary, so a compatible recipe inside it receives nothing from a
        /// provider above it (P-016).
        /// </summary>
        public static IReadOnlyList<ScopeRecord> DeclaredChildren()
        {
            return new List<ScopeRecord>
            {
                Scope(NarrativeKeys.ChapterOneScope, NarrativeKeys.RootScope, 1, false),
                Scope(NarrativeKeys.ChapterTwoScope, NarrativeKeys.RootScope, 1, false),
                Scope(NarrativeKeys.VillageScope, NarrativeKeys.ChapterOneScope, 2, false),
                Scope(NarrativeKeys.GroveScope, NarrativeKeys.ChapterOneScope, 2, false),
                Scope(NarrativeKeys.MuseumScope, NarrativeKeys.ChapterOneScope, 2, true),
                Scope(NarrativeKeys.HarborScope, NarrativeKeys.ChapterTwoScope, 2, false),
            };
        }

        private static ScopeRecord Scope(ScopeId scope, ScopeId parent, int depth, bool isolateAllCapabilities)
        {
            return new ScopeRecord(
                scope,
                parent,
                depth,
                new IsolationSet(false, null),
                new IsolationSet(isolateAllCapabilities, null),
                null,
                null);
        }
    }
}
