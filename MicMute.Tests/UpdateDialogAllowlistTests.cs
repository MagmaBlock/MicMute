namespace MicMute.Tests;

[TestClass]
public class UpdateDialogAllowlistTests
{
    // Empty / malformed input

    [TestMethod]
    public void NullOrEmpty_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(""));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(null!));
    }

    [TestMethod]
    public void NotAUri_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin("not a url"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin("/relative/path"));
    }

    // Scheme

    [TestMethod]
    public void HttpScheme_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "http://github.com/MagmaBlock/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void NonHttpsScheme_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "ftp://github.com/MagmaBlock/MicMute/"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "file:///C:/MagmaBlock/MicMute/"));
    }

    // github.com 鈥?owner-scoped

    [TestMethod]
    public void GitHubCom_CorrectOwnerRepo_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/MagmaBlock/MicMute/releases/download/v2.1.11/MicMute.exe"));
    }

    [TestMethod]
    public void GitHubCom_WrongOwner_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/evil/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void GitHubCom_WrongRepo_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/itsnateai/SomethingElse/releases/download/v1/file.exe"));
    }

    // api.github.com 鈥?repo-scoped

    [TestMethod]
    public void ApiGitHub_CorrectRepoPath_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://api.github.com/repos/MagmaBlock/MicMute/releases/latest"));
    }

    [TestMethod]
    public void ApiGitHub_WrongRepoPath_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://api.github.com/repos/evil/MicMute/releases/latest"));
    }

    // GitHub release-asset CDNs (both legacy + new)

    [TestMethod]
    public void ObjectsCdn_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://objects.githubusercontent.com/github-production-release-asset-anything"));
    }

    [TestMethod]
    public void ReleaseAssetsCdn_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://release-assets.githubusercontent.com/github-production-release-asset-anything"));
    }

    // Host-confusion attacks the validator must defeat

    [TestMethod]
    public void HostConfusion_SubdomainOfAttacker_Rejected()
    {
        // A naive `StartsWith("github.com")` check would let this through.
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com.evil.example/MagmaBlock/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void HostConfusion_AttackerWithGitHubInPath_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://evil.example/github.com/MagmaBlock/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void HostConfusion_LookalikeCdn_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://objects.githubusercontent.com.evil.example/anything"));
    }

    [TestMethod]
    public void DifferentGitHubProperty_Rejected()
    {
        // github.io and other GitHub-owned-but-not-allowlisted hosts must fail.
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://itsnateai.github.io/MicMute/index.html"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://raw.githubusercontent.com/MagmaBlock/MicMute/main/README.md"));
    }

    // Case-insensitivity (host + path)

    [TestMethod]
    public void HostCaseInsensitive_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://GITHUB.COM/MagmaBlock/MicMute/releases/latest"));
    }

    [TestMethod]
    public void RepoPathCaseInsensitive_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/MagmaBlock/MicMute/releases/latest"));
    }

    // Defensive pins 鈥?the audit ran these as black-box probes against Uri.Host
    // semantics and confirmed they're rejected. Pin the behavior in case .NET's
    // Uri parser changes how it handles authority/path edge cases.

    [TestMethod]
    public void HostConfusion_UserinfoAuthority_Rejected()
    {
        // Uri.Host returns "evil.example", not "github.com", because RFC 3986
        // authority parsing puts everything before @ in UserInfo.
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com@evil.example/MagmaBlock/MicMute/releases/download/v1/MicMute.exe"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com:443@evil.example/MagmaBlock/MicMute/releases/download/v1/MicMute.exe"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://api.github.com@evil.example/repos/MagmaBlock/MicMute/releases/latest"));
    }

    [TestMethod]
    public void HostConfusion_PathTraversalToOtherRepo_Rejected()
    {
        // Uri normalizes /a/b/../c 鈫?/a/c, so the path no longer starts with
        // /MagmaBlock/MicMute/ and the prefix check rejects.
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/MagmaBlock/MicMute/../EvilRepo/releases/download/v1/x.exe"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://api.github.com/repos/MagmaBlock/MicMute/../EvilRepo/releases/latest"));
    }
}
