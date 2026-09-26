// GameCore.Gameplay.Traversal — the course's mount payloads (GC-020).
//
// Normative sources: 07 s4.1's composition tree (`CourseWorld [TraversalRuntime, CheckpointRuntime, InputAdapter,
// CourseSensorAdapter]`, `Valley [Tailwind]`, `Showcase [CapabilityIsolation: traversal.Acceleration]`,
// `Ridge [Headwind]`), 07 s4.3's before/after operations (mount tailwind, unmount it, reparent the runner subtree
// into the ridge, toggle both propagation modes) and 02 s5's mode gate.
//
// The mount payloads are the shape the control lane's applier validates (GC-004): an O-02 scope creation, an O-03
// installation mount, an O-08 mode edit, an O-02 subtree reparent and an O-07 unmount. A mount's declared
// configuration hash is the canonical hash of the effective configuration (schema defaults over the local patch),
// which is what `CompositionEditApplier.ValidateConfigDocument` recomputes (P-020).
#nullable enable
using System;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>The mount payloads of the traversal course, in the shape the control lane's applier validates.</summary>
    public static class TraversalPayloads
    {
        /// <summary>
        /// O-02: create one scope under an existing parent. The parent must already exist in the state the edit is
        /// planned against, so a nested tree is created top-down (P-010). <paramref name="isolateAcceleration"/>
        /// installs the capability boundary of 07 s4.1's `Showcase` scope.
        /// </summary>
        public static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent, bool isolateAcceleration)
        {
            IsolationSet capabilityIsolation = isolateAcceleration
                ? new IsolationSet(false, new[] { TraversalVocabulary.AccelerationCapability.Value })
                : new IsolationSet(false, null);

            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                capabilityIsolation,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// O-03: mount one plugin instance at one scope. The declared configuration hash is the canonical hash of the
        /// effective configuration, exactly as the applier recomputes it (P-020).
        /// </summary>
        public static CompositionEditPayload Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ConfigDocument effective = EffectiveConfiguration(manifest, instance);
            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                ConfigDocument.Empty,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// O-08: set the world propagation mode. The scope must be default or the world root, because the mode is one
        /// world-level setting (P-013, P-014).
        /// </summary>
        public static CompositionEditPayload ModeSet(PropagationMode mode)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ModeSet,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                mode);
        }

        /// <summary>
        /// O-02: move a scope subtree under a new parent (07 s4.3's "Reparent runner subtree into `Ridge`").
        /// Membership, contributions and bindings publish together (P-025).
        /// </summary>
        public static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId newParent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeReparent,
                scope,
                newParent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>O-07: unmount one installation (07 s4.3's "Unmount `Tailwind`").</summary>
        public static CompositionEditPayload Unmount(PluginInstanceId instance)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.InstallUnmount,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>The effective configuration a mount publishes: schema defaults, then the local declaration.</summary>
        private static ConfigDocument EffectiveConfiguration(PluginManifest manifest, PluginInstanceId instance)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(
                    ConfigLayerOrigin.SchemaDefaults,
                    manifest.ConfigSchema.Id.Value,
                    ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, ConfigDocument.Empty),
            });

            return composed.Value;
        }
    }
}
