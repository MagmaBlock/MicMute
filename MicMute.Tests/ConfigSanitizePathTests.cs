namespace MicMute.Tests;

[TestClass]
public class ConfigSanitizePathTests
{
    // SanitizePath is the single source of truth for path acceptability —
    // called by Config.Load AND by SettingsDialog.ValidateCustomFile/ApplySettings.
    // Reject anything that would cause Windows to authenticate to a remote SMB
    // host (UNC, file://) and leak an NTLMv2 challenge during icon/sound load.

    [TestMethod]
    public void Empty_ReturnsEmpty()
    {
        Assert.AreEqual("", Config.SanitizePath(""));
        Assert.AreEqual("", Config.SanitizePath(null!));
        Assert.AreEqual("", Config.SanitizePath("   "));
    }

    [TestMethod]
    public void LocalAbsolutePath_PassesThrough()
    {
        Assert.AreEqual(@"C:\Icons\mic.ico", Config.SanitizePath(@"C:\Icons\mic.ico"));
        Assert.AreEqual(@"D:\Sounds\beep.wav", Config.SanitizePath(@"D:\Sounds\beep.wav"));
    }

    [TestMethod]
    public void RelativePath_PassesThrough()
    {
        // Sanitization is not path resolution — relative paths are caller's problem.
        Assert.AreEqual(@"icons\mic.ico", Config.SanitizePath(@"icons\mic.ico"));
    }

    [TestMethod]
    public void UncBackslash_Rejected()
    {
        Assert.AreEqual("", Config.SanitizePath(@"\\server\share\icon.ico"));
        Assert.AreEqual("", Config.SanitizePath(@"\\evil.example\share\icon.ico"));
        Assert.AreEqual("", Config.SanitizePath(@"\\?\UNC\server\share\icon.ico"));
        Assert.AreEqual("", Config.SanitizePath(@"\\.\pipe\anything"));
    }

    [TestMethod]
    public void UncForwardSlash_Rejected()
    {
        Assert.AreEqual("", Config.SanitizePath("//server/share/icon.ico"));
        Assert.AreEqual("", Config.SanitizePath("//evil.example/share/icon.ico"));
    }

    [TestMethod]
    public void FileScheme_Rejected()
    {
        // file:// can be aliased back to UNC by SoundPlayer / Icon constructor.
        Assert.AreEqual("", Config.SanitizePath("file://server/share/icon.ico"));
        Assert.AreEqual("", Config.SanitizePath("FILE:///C:/icon.ico"));
        Assert.AreEqual("", Config.SanitizePath("file:icon.ico"));
    }

    [TestMethod]
    public void LeadingWhitespace_DoesNotBypass()
    {
        // Trim before the prefix check — a leading space would otherwise leak.
        Assert.AreEqual("", Config.SanitizePath(@"  \\server\share\x.ico"));
        Assert.AreEqual("", Config.SanitizePath("\t//server/share/x.ico"));
        Assert.AreEqual("", Config.SanitizePath(" file://server/x.ico"));
    }
}
