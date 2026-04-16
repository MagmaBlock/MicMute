namespace MicMute;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool isAfterUpdate = args.Contains("--after-update");

        // Single-instance: acquire ownership explicitly via WaitOne so a duplicate
        // launch exits silently instead of racing on the hotkey + INI file.
        // Post-update: wait up to 5 s for the old exe to release the mutex during
        // the self-replace handoff; normal launches return immediately.
        using var mutex = new Mutex(false, @"Global\MicMute_SingleInstance");
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(isAfterUpdate ? 5000 : 0, false);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died without releasing — safe to proceed, we now own it.
            acquired = true;
        }
        if (!acquired)
            return;

        try
        {
            RunApp(isAfterUpdate);
        }
        finally
        {
            try { mutex.ReleaseMutex(); } catch { }
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
}
