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

        // Single-instance: acquire ownership explicitly via WaitOne so a duplicate
        // launch exits silently instead of racing on the hotkey + INI file.
        // Post-update: wait up to 5 s for the old exe to release the mutex during
        // the self-replace handoff; normal launches return immediately.
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
