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
            "http://github.com/itsnateai/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void NonHttpsScheme_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "ftp://github.com/itsnateai/MicMute/"));
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "file:///C:/itsnateai/MicMute/"));
    }

    // github.com — owner-scoped

    [TestMethod]
    public void GitHubCom_CorrectOwnerRepo_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/itsnateai/MicMute/releases/download/v2.1.11/MicMute.exe"));
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

    // api.github.com — repo-scoped

    [TestMethod]
    public void ApiGitHub_CorrectRepoPath_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://api.github.com/repos/itsnateai/MicMute/releases/latest"));
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
            "https://github.com.evil.example/itsnateai/MicMute/releases/download/v1/MicMute.exe"));
    }

    [TestMethod]
    public void HostConfusion_AttackerWithGitHubInPath_Rejected()
    {
        Assert.IsFalse(UpdateDialog.IsAllowedReleaseOrigin(
            "https://evil.example/github.com/itsnateai/MicMute/releases/download/v1/MicMute.exe"));
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
            "https://raw.githubusercontent.com/itsnateai/MicMute/main/README.md"));
    }

    // Case-insensitivity (host + path)

    [TestMethod]
    public void HostCaseInsensitive_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://GITHUB.COM/itsnateai/MicMute/releases/latest"));
    }

    [TestMethod]
    public void RepoPathCaseInsensitive_Allowed()
    {
        Assert.IsTrue(UpdateDialog.IsAllowedReleaseOrigin(
            "https://github.com/ITSNATEAI/MICMUTE/releases/latest"));
    }
}
