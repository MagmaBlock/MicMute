namespace MicMute.Tests;

[TestClass]
public class ConfigParseHotkeyTests
{
    // ── Modifier prefix decoding ─────────────────────────────────────────

    [TestMethod]
    public void EmptyOrNull_Rejected()
    {
        Assert.IsFalse(Config.ParseHotkey("", out _, out _));
        Assert.IsFalse(Config.ParseHotkey(null!, out _, out _));
    }

    [TestMethod]
    public void ModifierPlusLetter_Parses()
    {
        Assert.IsTrue(Config.ParseHotkey("#^+a", out uint mods, out uint vk));
        // Win | Ctrl | Shift bits should all be set; NOREPEAT also set.
        Assert.AreEqual((uint)'A', vk);
        Assert.IsTrue((mods & NativeMethods.MOD_WIN) != 0);
        Assert.IsTrue((mods & NativeMethods.MOD_CONTROL) != 0);
        Assert.IsTrue((mods & NativeMethods.MOD_SHIFT) != 0);
        Assert.IsTrue((mods & NativeMethods.MOD_NOREPEAT) != 0);
    }

    [TestMethod]
    public void AltOnlyModifier_Parses()
    {
        Assert.IsTrue(Config.ParseHotkey("!a", out uint mods, out uint vk));
        Assert.AreEqual((uint)'A', vk);
        Assert.IsTrue((mods & NativeMethods.MOD_ALT) != 0);
        Assert.IsTrue((mods & NativeMethods.MOD_WIN) == 0);
    }

    // ── Bare-key gate (the v2.1.6 tightening) ────────────────────────────

    [TestMethod]
    public void BareLetter_RejectedWhenAllowBareFalse()
    {
        // Without a modifier and without allowBare, plain letters must fail —
        // they would silently hijack a generic key globally otherwise.
        Assert.IsFalse(Config.ParseHotkey("a", out _, out _));
    }

    [TestMethod]
    public void BareLetter_AcceptedWhenAllowBareTrue()
    {
        Assert.IsTrue(Config.ParseHotkey("a", out _, out uint vk, allowBare: true));
        Assert.AreEqual((uint)'A', vk);
    }

    [TestMethod]
    public void BareFunctionKey_AcceptedEvenWhenAllowBareFalse()
    {
        // F1-F24 are allowed bare per the polling-path comment.
        Assert.IsTrue(Config.ParseHotkey("F1", out _, out uint vk));
        Assert.AreEqual(0x70u, vk); // VK_F1
    }

    [TestMethod]
    public void ModifierPrefixOnly_NoKey_Rejected()
    {
        // "#" or "^" alone — no key name follows, KeyNameToVk returns 0.
        Assert.IsFalse(Config.ParseHotkey("#", out _, out _));
        Assert.IsFalse(Config.ParseHotkey("^!+", out _, out _));
    }

    [TestMethod]
    public void UnknownKey_Rejected()
    {
        Assert.IsFalse(Config.ParseHotkey("#^totally-not-a-key", out _, out _));
    }
}
