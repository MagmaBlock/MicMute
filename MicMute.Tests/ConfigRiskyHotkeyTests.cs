namespace MicMute.Tests;

[TestClass]
public class ConfigRiskyHotkeyTests
{
    private const uint MOD_WIN = NativeMethods.MOD_WIN;
    private const uint MOD_CONTROL = NativeMethods.MOD_CONTROL;
    private const uint MOD_ALT = NativeMethods.MOD_ALT;
    private const uint MOD_SHIFT = NativeMethods.MOD_SHIFT;

    // Bare keys — the typing-path danger zone

    [TestMethod]
    public void BareLetter_Risky()
    {
        Assert.IsTrue(Config.IsRiskyHotkey(0, 'A'));
        Assert.IsTrue(Config.IsRiskyHotkey(0, 'Z'));
    }

    [TestMethod]
    public void BareDigit_Risky()
    {
        Assert.IsTrue(Config.IsRiskyHotkey(0, '0'));
        Assert.IsTrue(Config.IsRiskyHotkey(0, '9'));
    }

    [TestMethod]
    public void BareSpaceEnterTabEscBackspace_Risky()
    {
        Assert.IsTrue(Config.IsRiskyHotkey(0, 0x20)); // Space
        Assert.IsTrue(Config.IsRiskyHotkey(0, 0x0D)); // Enter
        Assert.IsTrue(Config.IsRiskyHotkey(0, 0x09)); // Tab
        Assert.IsTrue(Config.IsRiskyHotkey(0, 0x1B)); // Esc
        Assert.IsTrue(Config.IsRiskyHotkey(0, 0x08)); // Backspace
    }

    [TestMethod]
    public void BareFunctionKey_NotRisky()
    {
        for (uint vk = 0x70; vk <= 0x87; vk++)
            Assert.IsFalse(Config.IsRiskyHotkey(0, vk), $"VK 0x{vk:X} (F-key) should not be risky");
    }

    [TestMethod]
    public void BareModifierVk_NotRisky()
    {
        // L/R modifier VKs explicitly permitted bare on the polling path.
        Assert.IsFalse(Config.IsRiskyHotkey(0, 0xA0)); // VK_LSHIFT
        Assert.IsFalse(Config.IsRiskyHotkey(0, 0xA2)); // VK_LCONTROL
        Assert.IsFalse(Config.IsRiskyHotkey(0, 0x5B)); // VK_LWIN
    }

    // Modifier combos

    [TestMethod]
    public void ShiftLetter_Risky()
    {
        // Shift+letter = every capital letter typed — high false-fire risk.
        Assert.IsTrue(Config.IsRiskyHotkey(MOD_SHIFT, 'A'));
    }

    [TestMethod]
    public void CtrlLetter_Risky()
    {
        // Ctrl+letter = Ctrl+C/V/X/Z/A/S/F — universal app shortcuts.
        Assert.IsTrue(Config.IsRiskyHotkey(MOD_CONTROL, 'C'));
        Assert.IsTrue(Config.IsRiskyHotkey(MOD_CONTROL | MOD_SHIFT, 'A'));
        Assert.IsTrue(Config.IsRiskyHotkey(MOD_CONTROL | MOD_ALT, 'A'));
    }

    [TestMethod]
    public void WinPlusAnything_NotRisky()
    {
        // Win-combos are intentionally exempt — user explicitly chose them.
        Assert.IsFalse(Config.IsRiskyHotkey(MOD_WIN, 'A'));
        Assert.IsFalse(Config.IsRiskyHotkey(MOD_WIN | MOD_CONTROL, 'C'));
        Assert.IsFalse(Config.IsRiskyHotkey(MOD_WIN | MOD_SHIFT, 'A'));
    }

    [TestMethod]
    public void AltLetter_NotRisky()
    {
        // Alt-only + letter is not flagged (Alt+F4 etc. are app-controlled).
        Assert.IsFalse(Config.IsRiskyHotkey(MOD_ALT, 'A'));
    }

    [TestMethod]
    public void NoRepeatBitDoesNotChangeOutcome()
    {
        // The NOREPEAT bit may or may not be set — IsRiskyHotkey must mask it.
        Assert.AreEqual(
            Config.IsRiskyHotkey(MOD_CONTROL, 'C'),
            Config.IsRiskyHotkey(MOD_CONTROL | NativeMethods.MOD_NOREPEAT, 'C'));
    }

    // IsBareModifierVk direct coverage

    [TestMethod]
    public void IsBareModifierVk_KnownModifiers()
    {
        foreach (uint vk in new uint[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
            Assert.IsTrue(Config.IsBareModifierVk(vk), $"VK 0x{vk:X} should be a bare modifier");
    }

    [TestMethod]
    public void IsBareModifierVk_NonModifiers()
    {
        Assert.IsFalse(Config.IsBareModifierVk('A'));
        Assert.IsFalse(Config.IsBareModifierVk(0x70)); // F1
        Assert.IsFalse(Config.IsBareModifierVk(0x20)); // Space
    }
}
