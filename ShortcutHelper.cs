using System.Runtime.InteropServices;

namespace MicMute;

/// <summary>
/// Creates Windows .lnk shortcuts using WScript.Shell COM interop.
/// </summary>
internal static class ShortcutHelper
{
    /// <summary>
    /// If a startup shortcut exists but points to a stale exe path, update it.
    /// Called early in app startup so winget/moved installs auto-heal.
    /// </summary>
    public static void ValidateStartupShortcut()
    {
        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MicMute.lnk");

        if (!File.Exists(shortcutPath)) return; // No shortcut = nothing to validate

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                try
                {
                    var targetPath = (string)shortcut.TargetPath;
                    var currentPath = Environment.ProcessPath ?? "";
                    if (!targetPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Stale — update it
                        CreateShortcut(shortcutPath, currentPath);
                    }
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        catch { /* Silently ignore — startup validation is best-effort */ }
    }

    public static void CreateShortcut(string shortcutPath, string targetPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            object shell = Activator.CreateInstance(shellType)!;
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell,
                    new object[] { shortcutPath })!;

                try
                {
                    var scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath",
                        System.Reflection.BindingFlags.SetProperty, null, shortcut,
                        new object[] { targetPath });
                    scType.InvokeMember("WorkingDirectory",
                        System.Reflection.BindingFlags.SetProperty, null, shortcut,
                        new object[] { Path.GetDirectoryName(targetPath) ?? "" });
                    scType.InvokeMember("Description",
                        System.Reflection.BindingFlags.SetProperty, null, shortcut,
                        new object[] { "MicMute \u2014 Global mic mute toggle" });
                    scType.InvokeMember("IconLocation",
                        System.Reflection.BindingFlags.SetProperty, null, shortcut,
                        new object[] { targetPath + ",0" });
                    scType.InvokeMember("Save",
                        System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
                }
                finally
                {
                    Marshal.ReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
        catch
        {
            // Shortcut creation is best-effort
        }
    }
}
