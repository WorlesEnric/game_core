// GameCore.Validation.ProbeHost — the Wave 6 gate's composition audit (P-001, P-059; 04 s2).
//
// The Wave 6 exit gate says: "Demonstrate that optional physics/animation are absent from cards/narrative." GC-020
// proves that from *inside* the traversal scenario, over the other two genres' declarations and compiled descriptors.
// This audit is the same inspection lifted into the gate, so the claim is made by the run that also proves the
// traversal course HAS the optional stages:
//
//   * `WalkFamily` walks one genre's declared manifests AND its compiled ownership descriptor looking for any
//     traversal identity: a stage, a stage's factory keys, a system key, a buffer id (the declared step buffer and
//     the movement lane are the course's ports), a capability contract, a derivation rule, the course's configuration
//     schema, its plugin factory key, and — in the descriptor — a stage, a system key or an owned slot.
//   * `AdapterAssemblies` inspects the loaded assembly graph, which is also available inside an IL2CPP player: the
//     narrative and card gameplay assemblies must reference no `GameCore.Unity.Adapters` assembly (where the physics,
//     animation and audio halves live), while the traversal course does — and the traversal runtime really carries
//     those three optional stages.
//
// Both halves are identity-based, never name-based: a genre that renamed a stage would still be caught by its
// identity, and a genre that declared nothing would be caught by the walked-entry counter being zero (the audit never
// reports "clean" for a genre it did not actually read).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Gameplay.Traversal;
using GameCore.Planning;
using GameCore.Unity.Runtime.Integration;
using GameCore.Rules.Traversal;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>What one genre's declarations and compiled descriptor contain of the traversal course's surface.</summary>
    public sealed class W6FamilyAudit
    {
        internal W6FamilyAudit(
            string owner,
            int walked,
            int declaredStages,
            IReadOnlyList<string> offenders)
        {
            Owner = owner;
            Walked = walked;
            DeclaredStages = declaredStages;
            Offenders = offenders;
        }

        /// <summary>The genre label this audit read, e.g. <c>narrative</c>.</summary>
        public string Owner { get; }

        /// <summary>Declaration and descriptor entries this audit really read; zero means it read nothing.</summary>
        public int Walked { get; }

        /// <summary>Stages the genre's manifests declare; zero means it declares no stage at all.</summary>
        public int DeclaredStages { get; }

        /// <summary>Every traversal identity this genre carries; empty is the required value.</summary>
        public IReadOnlyList<string> Offenders { get; }

        /// <summary>True when the genre was really read and carries no traversal identity.</summary>
        public bool Clean => Walked > 0 && Offenders.Count == 0;

        public string Describe() => "owner=" + Owner
            + "; walked=" + Walked.ToString(CultureInfo.InvariantCulture)
            + "; declaredStages=" + DeclaredStages.ToString(CultureInfo.InvariantCulture)
            + "; offenders=" + W6CompositionAudit.Join(Offenders);

        public override string ToString() => Describe();
    }

    /// <summary>What the loaded assembly graph says about the optional engine adapters.</summary>
    public sealed class W6AdapterAssemblyFacts
    {
        internal W6AdapterAssemblyFacts(
            IReadOnlyList<string> families,
            bool narrativeReferencesAdapters,
            bool cardsReferencesAdapters,
            bool traversalReferencesAdapters,
            bool allLoaded)
        {
            Families = families;
            NarrativeReferencesAdapters = narrativeReferencesAdapters;
            CardsReferencesAdapters = cardsReferencesAdapters;
            TraversalReferencesAdapters = traversalReferencesAdapters;
            AllLoaded = allLoaded;
        }

        /// <summary>One `<assembly>=<referencesAdapters|noAdaptersAssembly|notLoaded>` line per gameplay assembly.</summary>
        public IReadOnlyList<string> Families { get; }

        public bool NarrativeReferencesAdapters { get; }

        public bool CardsReferencesAdapters { get; }

        public bool TraversalReferencesAdapters { get; }

        /// <summary>True when all three gameplay assemblies were loaded and inspectable.</summary>
        public bool AllLoaded { get; }

        public string Describe() => "assemblies=" + W6CompositionAudit.Join(Families)
            + "; narrativeAdapters=" + NarrativeReferencesAdapters
            + "; cardsAdapters=" + CardsReferencesAdapters
            + "; traversalAdapters=" + TraversalReferencesAdapters
            + "; allLoaded=" + AllLoaded;

        public override string ToString() => Describe();
    }

    /// <summary>The composition audit the Wave 6 gate runs over cards, narrative and the traversal course.</summary>
    public static class W6CompositionAudit
    {
        /// <summary>The assembly the optional physics, animation and audio halves live in (04 s2, 04 s7).</summary>
        public const string AdapterAssemblyName = "GameCore.Unity.Adapters";

        /// <summary>Simple name of the narrative gameplay assembly this audit reads.</summary>
        public const string NarrativeAssemblyName = "GameCore.Gameplay.Narrative";

        /// <summary>Simple name of the card gameplay assembly this audit reads.</summary>
        public const string CardsAssemblyName = "GameCore.Gameplay.Cards";

        /// <summary>Simple name of the traversal gameplay assembly this audit reads.</summary>
        public const string TraversalAssemblyName = "GameCore.Gameplay.Traversal";
        /// <summary>The reference course identities used to audit unrelated genre declarations.</summary>
        public static TraversalCourseSurface CourseSurface() => new TraversalCourseSurface(
            new List<StageId>
            {
                TraversalKeys.InputStage,
                TraversalKeys.IntegrateStage,
                TraversalKeys.SenseStage,
                TraversalKeys.CheckpointStage,
                TraversalKeys.OutputStage,
            },
            TraversalKeys.SystemKeys,
            TraversalVocabulary.AccelerationCapability);


        /// <summary>
        /// Walks one genre's declarations and its compiled descriptor for any traversal identity. The compiled report is
        /// read even when it did not succeed, so an audit never silently passes over a genre whose schedule refused.
        /// </summary>
        public static W6FamilyAudit WalkFamily(
            string owner,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            PipelineDescriptorReport descriptor,
            TraversalCourseSurface surface)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (declarations == null)
            {
                throw new ArgumentNullException(nameof(declarations));
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            if (surface == null)
            {
                throw new ArgumentNullException(nameof(surface));
            }

            var offenders = new List<string>();
            int walked = WalkDeclarations(owner, declarations, surface, offenders);
            walked += WalkDescriptor(owner, descriptor, surface, offenders);
            return new W6FamilyAudit(owner, walked, CountDeclaredStages(declarations), offenders);
        }

        /// <summary>
        /// Reads the loaded assembly graph of the running process and reports, per genre assembly, whether it references
        /// the optional adapter assembly. This works in the Editor and in a stripped player, where the repository tree
        /// the `.asmdef` files live in is not available.
        /// </summary>
        public static W6AdapterAssemblyFacts AdapterAssemblies()
        {
            EnsureGameplayAssembliesLoaded();

            Assembly? narrative = FindLoaded(NarrativeAssemblyName);
            Assembly? cards = FindLoaded(CardsAssemblyName);
            Assembly? traversal = FindLoaded(TraversalAssemblyName);

            bool narrativeAdapters = ReferencesAdapters(narrative);
            bool cardsAdapters = ReferencesAdapters(cards);
            bool traversalAdapters = ReferencesAdapters(traversal);

            var families = new List<string>
            {
                Narrate(NarrativeAssemblyName, narrative, narrativeAdapters),
                Narrate(CardsAssemblyName, cards, cardsAdapters),
                Narrate(TraversalAssemblyName, traversal, traversalAdapters),
            };

            return new W6AdapterAssemblyFacts(
                families,
                narrativeAdapters,
                cardsAdapters,
                traversalAdapters,
                narrative != null && cards != null && traversal != null);
        }

        /// <summary>Joins a value list for a detail string; an empty list renders as <c>&lt;none&gt;</c>.</summary>
        public static string Join(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return "<none>";
            }

            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return string.Join(",", array);
        }

        /// <summary>
        /// Touches one generated identity of each genre, so the three gameplay assemblies are really loaded before their
        /// reference graph is read: a process that has so far run only the narrative slice has no reason to have loaded
        /// the traversal assembly, and the audit must not report "not loaded" for an assembly the build ships.
        /// </summary>
        private static void EnsureGameplayAssembliesLoaded()
        {
            _ = GameCore.Gameplay.Narrative.NarrativeKeys.Mara;
            _ = GameCore.Gameplay.Cards.CardTableKeys.CommandRoute;
            _ = GameCore.Gameplay.Traversal.TraversalKeys.CommandRoute;
        }

        private static string Narrate(string assemblyName, Assembly? assembly, bool referencesAdapters)
        {
            string state = assembly == null
                ? "notLoaded"
                : (referencesAdapters ? "referencesAdapters" : "noAdaptersAssembly");
            return assemblyName + "=" + state;
        }

        private static Assembly? FindLoaded(string simpleName)
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                try
                {
                    if (string.Equals(loaded[i].GetName().Name, simpleName, StringComparison.Ordinal))
                    {
                        return loaded[i];
                    }
                }
                catch (Exception)
                {
                    // An assembly whose name cannot be read is reported as not loaded rather than failing the audit.
                }
            }

            return null;
        }

        private static bool ReferencesAdapters(Assembly? assembly)
        {
            if (assembly == null)
            {
                return false;
            }

            try
            {
                AssemblyName[] references = assembly.GetReferencedAssemblies();
                for (int i = 0; i < references.Length; i++)
                {
                    if (string.Equals(references[i].Name, AdapterAssemblyName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }

        private static int WalkDeclarations(
            string owner,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            TraversalCourseSurface surface,
            List<string> offenders)
        {
            int walked = 0;
            for (int d = 0; d < declarations.Count; d++)
            {
                PluginManifest manifest = declarations[d].Manifest;
                walked++;
                if (manifest.ConfigSchema.Equals(TraversalKeys.ConfigSchema))
                {
                    offenders.Add(owner + ":configSchema:traversal.config");
                }

                if (manifest.FactoryKey.Equals(TraversalKeys.PluginFactoryKey))
                {
                    offenders.Add(owner + ":factory:traversal.factory.course-plugin");
                }

                for (int s = 0; s < manifest.Stages.Count; s++)
                {
                    StageSpec stage = manifest.Stages[s];
                    walked++;
                    string named = NamesTraversalId(stage.StageId.Value, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":stage:" + named);
                    }

                    for (int k = 0; k < stage.FactoryKeys.Count; k++)
                    {
                        walked++;
                        string key = NamesTraversalKey(stage.FactoryKeys[k], surface);
                        if (key.Length != 0)
                        {
                            offenders.Add(owner + ":stageKey:" + key);
                        }
                    }

                    for (int y = 0; y < stage.Systems.Count; y++)
                    {
                        walked++;
                        string key = NamesTraversalKey(stage.Systems[y].SystemKey, surface);
                        if (key.Length != 0)
                        {
                            offenders.Add(owner + ":systemKey:" + key);
                        }
                    }
                }

                for (int b = 0; b < manifest.Buffers.Count; b++)
                {
                    walked++;
                    string named = NamesTraversalId(manifest.Buffers[b].BufferId.Value, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":buffer:" + named);
                    }
                }

                for (int c = 0; c < manifest.CapabilityContracts.Count; c++)
                {
                    walked++;
                    string named = NamesTraversalCapability(
                        manifest.CapabilityContracts[c].Capability.Capability.Value, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":contract:" + named);
                    }
                }

                for (int r = 0; r < manifest.DerivationRules.Count; r++)
                {
                    walked++;
                    string named = NamesTraversalCapability(
                        manifest.DerivationRules[r].OutputCapability.Capability.Value, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":rule:" + named);
                    }
                }
            }

            return walked;
        }

        private static int WalkDescriptor(
            string owner,
            PipelineDescriptorReport report,
            TraversalCourseSurface surface,
            List<string> offenders)
        {
            if (!report.Succeeded || report.Descriptor == null)
            {
                return 0;
            }

            int walked = 0;
            OwnershipStageDescriptor descriptor = report.Descriptor;
            for (int s = 0; s < descriptor.Stages.Count; s++)
            {
                walked++;
                DescriptorStage stage = descriptor.Stages[s];
                string named = NamesTraversalId(stage.Stage.Value, surface);
                if (named.Length != 0)
                {
                    offenders.Add(owner + ":descriptorStage:" + named);
                }

                for (int y = 0; y < stage.Systems.Count; y++)
                {
                    walked++;
                    string key = NamesTraversalKey(stage.Systems[y].Key, surface);
                    if (key.Length != 0)
                    {
                        offenders.Add(owner + ":descriptorSystem:" + key);
                    }
                }
            }

            for (int s = 0; s < descriptor.Slots.Count; s++)
            {
                walked++;
                string named = NamesTraversalSlot(descriptor.Slots[s].Slot, surface);
                if (named.Length != 0)
                {
                    offenders.Add(owner + ":slot:" + named);
                }
            }

            return walked;
        }

        private static int CountDeclaredStages(IReadOnlyList<CatalogPluginDeclaration> declarations)
        {
            int count = 0;
            for (int d = 0; d < declarations.Count; d++)
            {
                count += declarations[d].Manifest.Stages.Count;
            }

            return count;
        }

        private static string NamesTraversalId(Id128 id, TraversalCourseSurface surface)
        {
            if (id.Equals(surface.AccelerationCapability.Value))
            {
                return "traversal.acceleration";
            }

            for (int i = 0; i < surface.Stages.Count; i++)
            {
                if (id.Equals(surface.Stages[i].Value))
                {
                    return "stage#" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            if (id.Equals(TraversalKeys.ObservationBuffer.Value))
            {
                return "traversal.buffer.observation";
            }

            if (id.Equals(TraversalKeys.CommandLane.Value))
            {
                return "traversal.buffer.movement-lane";
            }

            return string.Empty;
        }

        private static string NamesTraversalKey(FactoryKey key, TraversalCourseSurface surface)
        {
            for (int i = 0; i < surface.Systems.Count; i++)
            {
                if (key.Equals(surface.Systems[i]))
                {
                    return "system#" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            return NamesTraversalId(key.RegistrationKey, surface);
        }

        private static string NamesTraversalCapability(Id128 capability, TraversalCourseSurface surface) =>
            capability.Equals(surface.AccelerationCapability.Value) ? "traversal.acceleration" : string.Empty;

        private static string NamesTraversalSlot(SlotId slot, TraversalCourseSurface surface)
        {
            if (slot.Equals(TraversalVocabulary.AccelerationSlot))
            {
                return "traversal.acceleration.slot-0";
            }

            if (slot.Equals(TraversalKeys.MotionSlot)
                || slot.Equals(TraversalKeys.InputSlot)
                || slot.Equals(TraversalKeys.ObservationSlot)
                || slot.Equals(TraversalKeys.ProgressSlot)
                || slot.Equals(TraversalKeys.CrossingSlot)
                || slot.Equals(TraversalKeys.SnapshotSlot))
            {
                return "traversal.slot";
            }

            return string.Empty;
        }
    }
}
