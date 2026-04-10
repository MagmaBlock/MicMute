using System.Diagnostics;

namespace MicMute;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Single-instance enforcement: kill previous instances
        string processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "MicMute");
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p)
            {
                if (p.Id != Environment.ProcessId)
                {
                    try { p.Kill(); } catch { /* already exiting */ }
                }
            }
        }

        bool isAfterUpdate = args.Contains("--after-update");
        UpdateDialog.CleanupUpdateArtifacts();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (isAfterUpdate)
            UpdateDialog.ShowUpdateToast();

        Application.Run(new TrayApp());
    }
}
