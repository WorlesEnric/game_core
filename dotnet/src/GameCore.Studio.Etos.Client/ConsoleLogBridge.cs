#nullable enable
// Standalone host for the shared core logging/redaction source. Unity builds use UnityEngine.Debug.
namespace UnityEngine
{
    internal static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine(message);
        public static void LogWarning(object message) => System.Console.Error.WriteLine(message);
        public static void LogError(object message) => System.Console.Error.WriteLine(message);
    }
}
