namespace MicMute.Tests;

[TestClass]
public class SettingsDialogValidationTests
{
    // These tests pin the CALL SITES (not just the helper). If a future change
    // removes Config.SanitizePath(...) from ValidateCustomFile, the function
    // would fall through to FileInfo / new Icon(...) on the UNC path, return
    // a different error message (or hang on SMB), and these tests would fail.
    //
    // Without this layer, the existing ConfigSanitizePathTests would still pass
    // on a regression that silently re-introduced the NTLM-leak gap from the
    // 2026-04-25 audit (P1+P2). The exact regression class flagged by the
    // verifier as "tests pin the helper but not the wiring."

    [TestMethod]
    public void ValidateCustomFile_UncBackslash_HardFailsWithSecurityMessage()
    {
        var (hardFail, message) = SettingsDialog.ValidateCustomFile(
            @"\\evil-server\share\icon.ico", ".ico");
        Assert.IsTrue(hardFail, "UNC backslash path must hard-fail");
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Contains("Network paths"),
            $"Expected 'Network paths' in error message; got: {message}");
    }

    [TestMethod]
    public void ValidateCustomFile_UncForwardSlash_HardFailsWithSecurityMessage()
    {
        var (hardFail, message) = SettingsDialog.ValidateCustomFile(
            "//evil-server/share/icon.ico", ".ico");
        Assert.IsTrue(hardFail, "UNC forward-slash variant must hard-fail");
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Contains("Network paths"),
            $"Expected 'Network paths' in error message; got: {message}");
    }

    [TestMethod]
    public void ValidateCustomFile_FileScheme_HardFailsWithSecurityMessage()
    {
        var (hardFail, message) = SettingsDialog.ValidateCustomFile(
            "file://evil-server/share/icon.ico", ".ico");
        Assert.IsTrue(hardFail, "file:// scheme must hard-fail");
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Contains("Network paths"),
            $"Expected 'Network paths' in error message; got: {message}");
    }

    [TestMethod]
    public void ValidateCustomFile_UncWav_HardFailsWithSecurityMessage()
    {
        // Same gate must apply to .wav files (PlaySound on UNC also leaks).
        var (hardFail, message) = SettingsDialog.ValidateCustomFile(
            @"\\evil-server\share\beep.wav", ".wav");
        Assert.IsTrue(hardFail, "UNC .wav path must hard-fail");
        Assert.IsNotNull(message);
        Assert.IsTrue(message.Contains("Network paths"),
            $"Expected 'Network paths' in error message; got: {message}");
    }

    [TestMethod]
    public void ValidateCustomFile_LocalNonexistentPath_FailsWithDifferentMessage()
    {
        // Pin that LOCAL paths get past SanitizePath into the existing validation —
        // if SanitizePath were broadened to reject all paths, this test would fail
        // with the security message instead of the existence message.
        var (hardFail, message) = SettingsDialog.ValidateCustomFile(
            @"C:\definitely-does-not-exist-" + System.Guid.NewGuid().ToString("N") + ".ico",
            ".ico");
        Assert.IsTrue(hardFail, "Missing local file must hard-fail");
        Assert.IsNotNull(message);
        Assert.IsFalse(message.Contains("Network paths"),
            $"Local missing path should NOT trigger network-paths message; got: {message}");
    }
}
