using System.Diagnostics;

// Verbose per-event logging for development use. [Conditional("DEBUG")] makes the compiler
// strip every call site entirely in Release builds, so no #if clutter is needed at each
// call site - just swap Debug.Log(...) for DevLog.Log(...) where the log is testing chatter
// rather than a real diagnostic (keep using Logger.LogError for genuine error conditions).
internal static class DevLog
{
    [Conditional("DEBUG")]
    public static void Log(object message) => UnityEngine.Debug.Log(message);
}
