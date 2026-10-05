#nullable enable
using Startup = UnityEditor.InitializeOnLoadAttribute;
[Startup]
public sealed class StartupHook { }
public sealed class ImportHook : UnityEditor.AssetPostprocessor { }
public sealed class ModificationHook : UnityEditor.AssetModificationProcessor { }
public sealed class CallbackHooks
{
    [UnityEditor.InitializeOnLoadMethod]
    private static void Init() { }
    [UnityEditor.Callbacks.DidReloadScripts]
    private static void Reload() { }
    [UnityEditor.MenuItem("Bad/Hook")]
    private static void Menu() { }
}
