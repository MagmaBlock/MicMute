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

    // A3-F02: track primary-log failure so we can funnel to emergency log once broken.
    private static bool _logBroken;
    private static readonly string _emergencyLogPath =
        Path.Combine(Path.GetTempPath(), "micmute-emergency.log");

    private static string GetLogPath()
    {
        // A3-F03: three-tier fallback — %LOCALAPPDATA%\MicMute → %TEMP%\MicMute → exe dir.
        // Returns "" only if all three fail, which silences logging gracefully.
        string lastError = null;

        // Tier 1: preferred location
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MicMute");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "micmute.log");
        }
        catch (Exception ex) { lastError = ex.Message; }

        // Tier 2: %TEMP%\MicMute
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "MicMute");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "micmute.log");
            // One-shot marker so the user (or support) knows the primary path failed.
            try
            {
                File.AppendAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] Log path fell back to {path} due to: {lastError}{Environment.NewLine}");
            }
            catch { }
            return path;
        }
        catch (Exception ex) { lastError = ex.Message; }

        // Tier 3: exe directory
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
            if (!string.IsNullOrEmpty(dir))
            {
                var path = Path.Combine(dir, "micmute.log");
                try
                {
                    File.AppendAllText(path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] Log path fell back to {path} due to: {lastError}{Environment.NewLine}");
                }
                catch { }
                return path;
            }
        }
        catch { }

        // All tiers failed — caller will bail early on empty string.
        return "";
    }

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Warn(string msg) => Write("WARN", msg, null);
    public static void Error(string msg, Exception ex = null) => Write("ERROR", msg, ex);
    public static void Fatal(string msg, Exception ex = null) => Write("FATAL", msg, ex);

    private static void Write(string level, string msg, Exception ex)
    {
        if (string.IsNullOrEmpty(_logPath)) return;

        var line = ex == null
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg} -- {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";

        try
        {
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(_logPath, line);
            }
        }
        catch (Exception writeEx)
        {
            // A3-F02: on first failure flip the broken flag and funnel to emergency log.
            // Subsequent failures also route there. Never show a MessageBox from here —
            // Write can be called from any thread.
            if (!_logBroken)
            {
                _logBroken = true;
                try
                {
                    File.AppendAllText(_emergencyLogPath,
                        $"{DateTime.Now:O} Primary log failed: {writeEx.Message}{Environment.NewLine}{line}");
                }
                catch { }
            }
            else
            {
                try { File.AppendAllText(_emergencyLogPath, line); }
                catch { }
            }
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
