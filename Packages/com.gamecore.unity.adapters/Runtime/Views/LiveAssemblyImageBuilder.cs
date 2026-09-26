// GameCore.Unity.Adapters.Views — building the engine-observation image from the real published assembly (GC-019).
//
// Normative sources: 00 P-030 (the published view is the complete observation image of one assembly; readers capture
// one reference so they can never see a mixture), P-045 (an observer reads immutable images at publication only and
// cannot obtain writable references), P-008 (canonical target order) and 04 s7's GameObject/Transform row.
//
// This is the bridge that makes presentation read *committed* output instead of live storage: it takes the world's
// switched `PublishedWorldView` reference — one read — plus the committed target index, and projects the binding rows
// into the pure `CommittedAssemblyImage` DTO. Nothing in this file reads a component or writes anything, so the image
// it produces cannot become a write path back into gameplay (P-034).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Unity.Adapters.Views
{
    /// <summary>
    /// The committed target-to-scope/recipe lookup of a live world. Membership comes from the committed target index
    /// (`LiveTargetIndex`), which the assembly publication maintains, so the scope reported for a target is composition
    /// data and never a Transform parent (P-010).
    /// </summary>
    public sealed class LiveTargetScopeIndex : ITargetScopeIndex
    {
        private readonly LiveTargetIndex targets;

        public LiveTargetScopeIndex(LiveTargetIndex targets)
        {
            this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        public LiveTargetIndex Targets => targets;

        public bool TryGetScope(TargetId target, out ScopeId scope)
        {
            scope = default(ScopeId);
            if (!targets.TryGet(target, out LiveTarget live))
            {
                return false;
            }

            scope = live.Scope;
            return true;
        }

        public bool TryGetRecipe(TargetId target, out DefinitionRef recipe)
        {
            recipe = default(DefinitionRef);
            if (!targets.TryGet(target, out LiveTarget live))
            {
                return false;
            }

            recipe = live.Recipe;
            return true;
        }
    }

    /// <summary>
    /// Builds one `CommittedAssemblyImage` from the world's currently published assembly. The caller supplies the
    /// already-captured `PublishedWorldView`, so the image is one epoch/step by construction (P-030).
    /// </summary>
    public static class LiveAssemblyImageBuilder
    {
        /// <summary>
        /// Projects the binding rows of one published view into presentation fields. Only the effective, already
        /// composed row value is presented: the composed policy output is what the publication committed (P-019).
        /// </summary>
        public static CommittedAssemblyImage Build(PublishedWorldView view, ITargetScopeIndex index)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (index == null)
            {
                throw new ArgumentNullException(nameof(index));
            }

            IReadOnlyList<TargetId> targets = view.Targets;
            var entries = new List<CommittedTargetEntry>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                TargetId target = targets[i];
                ScopeId scope = default(ScopeId);
                _ = index.TryGetScope(target, out scope);
                DefinitionRef recipe = default(DefinitionRef);
                _ = index.TryGetRecipe(target, out recipe);

                IReadOnlyList<TargetBindingRow> rows = view.Bindings.BindingsOf(target);
                var fields = new List<PresentationField>(rows.Count);
                for (int r = 0; r < rows.Count; r++)
                {
                    fields.Add(new PresentationField(rows[r].Capability, rows[r].OutputSlot, rows[r].Value));
                }

                entries.Add(new CommittedTargetEntry(target, scope, recipe, fields));
            }

            return new CommittedAssemblyImage(view.Token, entries);
        }

        /// <summary>Convenience overload reading the switched view from the publisher's slot (one reference read).</summary>
        public static CommittedAssemblyImage Build(PublishedAssemblySlot slot, ITargetScopeIndex index)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            return Build(slot.Read(), index);
        }
    }
}
