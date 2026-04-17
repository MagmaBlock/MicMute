namespace MicMute;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Install FIRST so any crash before mutex acquisition (rare: locked-down
        // Global\ namespace ACLs, OOM at startup, COM subsystem failure) still
        // leaves a log record instead of dying silently.
        InstallGlobalExceptionHandlers();

        bool isAfterUpdate = args.Contains("--after-update");

        // Back-compat handoff from v2.1.5 (which used Global\MicMute_SingleInstance).
        // If a v2.1.5 instance is still winding down during upgrade, wait briefly
        // for it to exit so we don't dual-run with two tray icons + fighting
        // hotkey registrations. A v2.1.6+ instance never acquires Global\, so on
        // steady-state this is either a fast miss (acquire + release) or a
        // meaningful wait during the self-update handoff. Can be removed once
        // all users have upgraded past 2.1.5.
        try
        {
            using var legacyMutex = new Mutex(false, @"Global\MicMute_SingleInstance");
            bool legacyHeld;
            try
            {
                legacyHeld = legacyMutex.WaitOne(isAfterUpdate ? 5000 : 250, false);
            }
            catch (AbandonedMutexException)
            {
                // v2.1.5 crashed without cleanup — we now own the mutex
                // and must release it below so v2.1.5-era shimmers don't
                // see it as still-abandoned on a subsequent wait.
                legacyHeld = true;
            }
            if (legacyHeld)
                legacyMutex.ReleaseMutex();
        }
        catch (Exception ex)
        {
            Log.Warn("Legacy mutex check failed (non-fatal): " + ex.Message);
        }

        // Single-instance: acquire ownership explicitly via WaitOne so a duplicate
        // launch exits silently instead of racing on the hotkey + INI file.
        //
        // Local\ namespace = per-session. Each Windows user (fast-user-switching,
        // terminal server) gets their own tray app. Global\ would block all but
        // the first user on a multi-user machine.
        using var mutex = new Mutex(false, @"Local\MicMute_SingleInstance");
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(isAfterUpdate ? 5000 : 0, false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died without releasing — safe to proceed, we now own it.
            Log.Warn("Mutex was abandoned — previous instance crashed without cleanup");
            acquired = true;
        }
        if (!acquired)
            return;

        Log.Info($"MicMute starting (afterUpdate={isAfterUpdate})");

        try
        {
            RunApp(isAfterUpdate);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { }
            Log.Info("MicMute exiting");
        }
    }

    private static void RunApp(bool isAfterUpdate)
    {
        UpdateDialog.CleanupUpdateArtifacts();
        ShortcutHelper.ValidateStartupShortcut();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (isAfterUpdate)
            UpdateDialog.ShowUpdateToast();

        Application.Run(new TrayApp());
    }

    private static void InstallGlobalExceptionHandlers()
    {
        // UI-thread exceptions. Catch-and-continue so one bad WndProc/tick
        // doesn't kill the tray — the log is our breadcrumb for later.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            Log.Fatal("Unhandled UI-thread exception", e.Exception);

        // Non-UI-thread exceptions (background tasks, finalizers, COM callbacks).
        // .NET terminates the process after this fires — we only get to log.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal("Unhandled domain exception (terminating=" + e.IsTerminating + ")",
                e.ExceptionObject as Exception);

        // Unobserved Task exceptions — fire-and-forget `async void` or lost Tasks.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }
}
