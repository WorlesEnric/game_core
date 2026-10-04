// GameCore.Studio.Model - selection snapshot captured when a prompt is sent (docs/studio/03-authoring-contracts.md s2).
// The frame image is referenced by content stamp and stored under Studio/Artifacts; it is never inline.
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameCore.Studio.Model
{
    /// <summary>What the user had selected, and how they were looking at it, when a prompt was sent (03 s2). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SelectionSnapshot
    {
        [JsonConstructor]
        public SelectionSnapshot(
            string id,
            SelectionMode mode,
            IReadOnlyList<AuthoringRef> targets,
            long indexRevision,
            IReadOnlyList<PartRef>? parts = null,
            RegionRect? regionRect = null,
            FrameContext? frame = null,
            string? worldSession = null)
        {
            Id = ModelLists.NotEmpty(id, nameof(id));
            Mode = mode;
            Targets = ModelLists.Required(targets, nameof(targets));
            IndexRevision = indexRevision;
            Parts = ModelLists.Optional(parts, nameof(parts));
            RegionRect = regionRect;
            Frame = frame;
            WorldSession = worldSession;
        }

        [JsonProperty("id", Required = Required.Always)]
        [SchemaHint(Pattern = StudioPatterns.SelectionId)]
        public string Id { get; }

        [JsonProperty("mode", Required = Required.Always)]
        public SelectionMode Mode { get; }

        /// <summary>Logical objects; subparts are resolved to their logical owner unless the user picked "this part".</summary>
        [JsonProperty("targets", Required = Required.Always)]
        public IReadOnlyList<AuthoringRef> Targets { get; }

        [JsonProperty("parts", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<PartRef>? Parts { get; }

        /// <summary>Box-selection rectangle.</summary>
        [JsonProperty("regionRect", NullValueHandling = NullValueHandling.Ignore)]
        public RegionRect? RegionRect { get; }

        [JsonProperty("frame", NullValueHandling = NullValueHandling.Ignore)]
        public FrameContext? Frame { get; }

        /// <summary>WorldId text of the live session; Play only, omitted in Edit.</summary>
        [JsonProperty("worldSession", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinLength = 1)]
        public string? WorldSession { get; }

        /// <summary>Semantic index revision the snapshot was taken against.</summary>
        [JsonProperty("indexRevision", Required = Required.Always)]
        public long IndexRevision { get; }
    }

    /// <summary>A picked subpart of a logical object (e.g. <c>Mesh:Lantern_Glass</c>). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PartRef
    {
        [JsonConstructor]
        public PartRef(AuthoringRef owner, string part)
        {
            Owner = ModelLists.NotNull(owner, nameof(owner));
            Part = ModelLists.NotEmpty(part, nameof(part));
        }

        [JsonProperty("owner", Required = Required.Always)]
        public AuthoringRef Owner { get; }

        [JsonProperty("part", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Part { get; }
    }

    /// <summary>Box-selection rectangle in screen pixels, [x0, y0, x1, y1]. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class RegionRect
    {
        [JsonConstructor]
        public RegionRect(IReadOnlyList<double> screen)
        {
            Screen = ModelLists.Required(screen, nameof(screen));
        }

        [JsonProperty("screen", Required = Required.Always)]
        [SchemaHint(MinItems = 4, MaxItems = 4)]
        public IReadOnlyList<double> Screen { get; }
    }

    /// <summary>The camera and viewport a selection was made in. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FrameContext
    {
        [JsonConstructor]
        public FrameContext(CameraPose camera, IReadOnlyList<int> viewport, string? image = null)
        {
            Camera = ModelLists.NotNull(camera, nameof(camera));
            Viewport = ModelLists.Required(viewport, nameof(viewport));
            Image = image;
        }

        [JsonProperty("camera", Required = Required.Always)]
        public CameraPose Camera { get; }

        /// <summary>Viewport size in pixels, [width, height].</summary>
        [JsonProperty("viewport", Required = Required.Always)]
        [SchemaHint(MinItems = 2, MaxItems = 2)]
        public IReadOnlyList<int> Viewport { get; }

        /// <summary>Content stamp of the captured frame image (stored in Studio/Artifacts).</summary>
        [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(Pattern = StudioPatterns.Stamp)]
        public string? Image { get; }
    }

    /// <summary>Camera pose at capture time. Rotation is a quaternion [x, y, z, w] or Euler degrees [x, y, z]. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CameraPose
    {
        [JsonConstructor]
        public CameraPose(IReadOnlyList<double> position, IReadOnlyList<double> rotation, double fov, double aspect)
        {
            Position = ModelLists.Required(position, nameof(position));
            Rotation = ModelLists.Required(rotation, nameof(rotation));
            Fov = fov;
            Aspect = aspect;
        }

        [JsonProperty("position", Required = Required.Always)]
        [SchemaHint(MinItems = 3, MaxItems = 3)]
        public IReadOnlyList<double> Position { get; }

        [JsonProperty("rotation", Required = Required.Always)]
        [SchemaHint(MinItems = 3, MaxItems = 4)]
        public IReadOnlyList<double> Rotation { get; }

        /// <summary>Vertical field of view in degrees.</summary>
        [JsonProperty("fov", Required = Required.Always)]
        public double Fov { get; }

        [JsonProperty("aspect", Required = Required.Always)]
        public double Aspect { get; }
    }
}
