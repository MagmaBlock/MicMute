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

#if DEBUG
        // DPI render harness — bypasses the single-instance mutex so a render can
        // run while a normal MicMute is in the tray. DEBUG-only (out of Release).
        if (args.Contains("--diag-render-form"))
        {
            // Hard-exit: UpdateDialog's Shown handler starts an async GitHub check
            // whose continuation can outlive Run() and keep the process alive.
            Environment.Exit(DiagRender.Run(args));
        }
#endif

        bool isAfterUpdate = args.Contains("--after-update");
        // --after-theme-restart: dispatched by TrayApp.TryAutoRestartForTheme
        // when the user changes the Theme dropdown in Settings. Same mutex-
        // retry treatment as --after-update because the outgoing instance is
        // racing the incoming one for the single-instance lock.
        bool isAfterThemeRestart = args.Contains("--after-theme-restart");

        // Back-compat handoff from v2.1.5 (which used Global\MicMute_SingleInstance).
        // Only relevant during --after-update: the old instance is winding down and
        // we need to wait for it to release the legacy mutex before proceeding.
        // Normal cold-start no longer pays the 250 ms probe — v2.1.5 is 4 minors old.
        if (isAfterUpdate)
        {
            try
            {
                using var legacyMutex = new Mutex(false, @"Global\MicMute_SingleInstance");
                bool legacyHeld;
                try
                {
                    legacyHeld = legacyMutex.WaitOne(5000, false);
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
            acquired = mutex.WaitOne((isAfterUpdate || isAfterThemeRestart) ? 5000 : 0, false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died without releasing — safe to proceed, we now own it.
            Log.Warn("Mutex was abandoned — previous instance crashed without cleanup");
            acquired = true;
        }
        if (!acquired)
        {
            Log.Info($"MicMute launch: another instance already running (isAfterUpdate={isAfterUpdate}), exiting.");
            return;
        }

        Log.Info($"MicMute starting (afterUpdate={isAfterUpdate}, afterThemeRestart={isAfterThemeRestart})");

        try
        {
            RunApp(isAfterUpdate);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch (Exception ex) { Log.Warn("ReleaseMutex on exit: " + ex.Message); }
            Log.Info("MicMute exiting");
        }
    }

    private static void RunApp(bool isAfterUpdate)
    {
        UpdateDialog.CleanupUpdateArtifacts();
        ShortcutHelper.ValidateStartupShortcut();

        // Canonical .NET 7+ initializer — source-generated from MicMute.csproj's
        // <ApplicationHighDpiMode>, <ApplicationDefaultFont>, etc. Replaces the
        // prior triplet (SetHighDpiMode + EnableVisualStyles +
        // SetCompatibleTextRenderingDefault) so the csproj properties are the
        // single source of truth and don't drift from explicit code calls.
        ApplicationConfiguration.Initialize();

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
