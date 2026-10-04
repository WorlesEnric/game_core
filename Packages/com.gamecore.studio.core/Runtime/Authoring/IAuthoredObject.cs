// GameCore.Studio.Authoring - authoring identity of an object (docs/studio/03-authoring-contracts.md s1).
// Components and ScriptableObjects that carry an authoring id implement this interface. The gameplay packages define
// their own interface with the same name and shape in com.gamecore.gameplay.contracts (so gameplay runtime code has no
// Studio dependency); AuthoringIdentity matches either one by interface name, so both are recognised.
#nullable enable

namespace GameCore.Studio.Authoring
{
    /// <summary>An authored object: it carries a stable authoring id (a lowercase GUID text minted once).</summary>
    public interface IAuthoredObject
    {
        /// <summary>The authoring id; empty when not minted yet.</summary>
        string AuthoringId { get; }
    }
}
