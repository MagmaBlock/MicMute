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

    // ── Mouse buttons (PTT polling path only) ─────────────────────────────

    [TestMethod]
    public void MouseButtons_ParseWithAllowBare_TrueMapsVk()
    {
        // PTT mode (allowBare: true) — the polling path binds these.
        Assert.IsTrue(Config.ParseHotkey("XButton1", out _, out uint vk1, allowBare: true));
        Assert.AreEqual(0x05u, vk1); // VK_XBUTTON1
        Assert.IsTrue(Config.ParseHotkey("XButton2", out _, out uint vk2, allowBare: true));
        Assert.AreEqual(0x06u, vk2); // VK_XBUTTON2
        Assert.IsTrue(Config.ParseHotkey("MButton", out _, out uint vk3, allowBare: true));
        Assert.AreEqual(0x04u, vk3); // VK_MBUTTON
    }

    [TestMethod]
    public void MouseButtons_RejectedWhenAllowBareFalse()
    {
        // RegisterHotKey accepts mouse VKs but never posts WM_HOTKEY — callers
        // headed there must see a loud parse failure, not a fake success.
        Assert.IsFalse(Config.ParseHotkey("XButton1", out _, out _));
        Assert.IsFalse(Config.ParseHotkey("XButton2", out _, out _));
        Assert.IsFalse(Config.ParseHotkey("MButton", out _, out _, allowBare: false));
    }

    [TestMethod]
    public void MouseButtons_ModifierCombo_AlsoRejectedForRegisterHotKeyPath()
    {
        // "^XButton1" has real modifiers so the bare-key gate alone wouldn't
        // catch it — the mouse branch must reject it on every non-polling path.
        Assert.IsFalse(Config.ParseHotkey("^XButton1", out _, out _));
        Assert.IsFalse(Config.ParseHotkey("#!XButton2", out _, out _));
    }

    [TestMethod]
    public void MouseButtons_CaseInsensitive()
    {
        Assert.IsTrue(Config.ParseHotkey("xbutton1", out _, out uint vk, allowBare: true));
        Assert.AreEqual(0x05u, vk);
    }

    [TestMethod]
    public void IsMouseVk_MatchesExactlyThreeButtonVks()
    {
        Assert.IsTrue(Config.IsMouseVk(0x04));
        Assert.IsTrue(Config.IsMouseVk(0x05));
        Assert.IsTrue(Config.IsMouseVk(0x06));
        Assert.IsFalse(Config.IsMouseVk(0x03)); // VK_CANCEL
        Assert.IsFalse(Config.IsMouseVk(0x07)); // undefined
        Assert.IsFalse(Config.IsMouseVk('A'));
        Assert.IsFalse(Config.IsMouseVk(0xA3)); // VK_RCONTROL — modifier, not mouse
    }

    [TestMethod]
    public void HotkeyToReadable_MouseButtons()
    {
        Assert.AreEqual("XButton1", Config.HotkeyToReadable("XButton1"));
        Assert.AreEqual("XButton2", Config.HotkeyToReadable("XButton2"));
        Assert.AreEqual("MButton", Config.HotkeyToReadable("MButton"));
        // With modifiers, the prefix renders ahead of the prettified name.
        Assert.AreEqual("Ctrl+Shift+XButton1", Config.HotkeyToReadable("^+XButton1"));
    }

    [TestMethod]
    public void MigrateLegacyHotkey_LeavesMouseKeysUnchanged()
    {
        // Migration runs in Load() before Mode is read, with the v2.1.6
        // bare-key rules. A PTT user's bare "XButton1" doesn't parse under
        // those rules — the guard must exempt mouse keys or migration would
        // rewrite it into "^+XButton1", which parses under no path.
        var cfg = new Config();
        Assert.AreEqual("XButton1", cfg.MigrateLegacyHotkey("XButton1"));
        Assert.AreEqual("XButton2", cfg.MigrateLegacyHotkey("XButton2"));
        Assert.AreEqual("MButton", cfg.MigrateLegacyHotkey("MButton"));
        // Modifier-prefixed mouse keys are also left alone.
        Assert.AreEqual("^+XButton1", cfg.MigrateLegacyHotkey("^+XButton1"));
    }

    [TestMethod]
    public void MigrateLegacyHotkey_StillMigratesLegacyBareKeys()
    {
        // The guard must not break the original v2.1.5 salvage: a bare
        // keyboard key still gets the Ctrl+Shift prefix.
        var cfg = new Config();
        Assert.AreEqual("^+PAUSE", cfg.MigrateLegacyHotkey("PAUSE"));
        Assert.AreEqual("^+PRINTSCREEN", cfg.MigrateLegacyHotkey("PRINTSCREEN"));
    }
}
