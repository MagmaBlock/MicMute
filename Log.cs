namespace MicMute;

/// <summary>
/// Minimal rolling file logger. Lives at %LOCALAPPDATA%\MicMute\micmute.log.
/// Bounded at ~1 MB with a single .1 rotation so it can't grow unbounded.
/// Open-append-close per write to survive unexpected exits with partial data flushed.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object _gate = new();
    private static readonly string _logPath = GetLogPath();

    private static string GetLogPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MicMute");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "micmute.log");
        }
        catch
        {
            // If AppData is unavailable we'll fall back to swallowing — logging must
            // never throw on the caller's thread.
            return "";
        }
    }

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Warn(string msg) => Write("WARN", msg, null);
    public static void Error(string msg, Exception ex = null) => Write("ERROR", msg, ex);
    public static void Fatal(string msg, Exception ex = null) => Write("FATAL", msg, ex);

    private static void Write(string level, string msg, Exception ex)
    {
        if (string.IsNullOrEmpty(_logPath)) return;
        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                var line = ex == null
                    ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}"
                    : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg} -- {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                File.AppendAllText(_logPath, line);
            }
        }
        catch
        {
            // Logging failures are always swallowed — we never want to mask the real problem.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (!info.Exists || info.Length < MaxBytes) return;
            var rolled = _logPath + ".1";
            if (File.Exists(rolled)) File.Delete(rolled);
            File.Move(_logPath, rolled);
        }
        catch { }
    }
}
