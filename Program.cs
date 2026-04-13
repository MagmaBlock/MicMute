namespace MicMute;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single-instance: hold mutex for lifetime (serializes startup)
        using var mutex = new Mutex(true, @"Global\MicMute_SingleInstance", out _);

        bool isAfterUpdate = args.Contains("--after-update");
        UpdateDialog.CleanupUpdateArtifacts();
        ShortcutHelper.ValidateStartupShortcut();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (isAfterUpdate)
            UpdateDialog.ShowUpdateToast();

        Application.Run(new TrayApp());
    }
}
