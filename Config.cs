using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MicMute;

/// <summary>
/// Reads and writes MicMute.ini configuration using the Windows INI API.
/// </summary>
internal sealed class Config
{
    public static readonly string Version = typeof(Config).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    // Settings with defaults
    public string Hotkey = "#^+a";
    public bool SoundFeedback = true;
    public string Mode = "toggle"; // "toggle" or "push-to-talk"
    public string DeviceId = "";
    public string IconMuted = "";
    public string IconActive = "";
    public string MuteSound = "";
    public string UnmuteSound = "";
    public bool MuteLock;
    public bool OsdEnabled;
    public int OsdDuration = 1500;
    public string DeafenHotkey = "";
    public bool MiddleClickToggle = true;
    public string StartMuted = "no"; // "no", "yes", "unmuted", "last"
    public bool LastMuteState;
    // Opt-in: poll-based PTT via GetAsyncKeyState instead of RegisterHotKey.
    // Works over fullscreen-exclusive games and supports bare-modifier keys
    // (LCtrl / RCtrl / RShift / etc.). No keyboard hook is installed — the
    // key state is read passively the same way games themselves read it,
    // so there's no anti-cheat signature.
    public bool LowLatencyPtt;

    private readonly string _iniPath;

    // Pre-compiled regex for hotkey parsing
    // s_modifierPrefix captures recognized modifier symbols for parsing
    // s_stripModifiers strips ALL AHK prefix symbols including ~*$ (passthrough/wildcard/hook)
    private static readonly Regex s_modifierPrefix = new(@"^[<>#^!+~*$]+", RegexOptions.Compiled);
    private static readonly Regex s_stripModifiers = new(@"^[<>#^!+~*$]+", RegexOptions.Compiled);

    // Signal from Load() → Save() that a legacy-format key was rewritten
    // mid-load and should be persisted. One Save() handles all migrations
    // rather than one WriteIni per key.
    private bool _migrationPending;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(
        string lpAppName, string lpKeyName, string lpDefault,
        StringBuilder lpReturnedString, uint nSize, string lpFileName);

    public Config()
    {
        _iniPath = ResolveIniPath();
    }

    private static string ResolveIniPath()
    {
        const string iniName = "MicMute.ini";

        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "")
            ?? AppDomain.CurrentDomain.BaseDirectory;
        if (string.IsNullOrEmpty(exeDir))
            exeDir = AppDomain.CurrentDomain.BaseDirectory;

        // 1. Existing ini next to exe — backwards compat for portable users
        string portablePath = Path.Combine(exeDir, iniName);
        if (File.Exists(portablePath))
            return portablePath;

        // 2. Existing ini in %APPDATA%\MicMute\
        string appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicMute");
        string appDataPath = Path.Combine(appDataDir, iniName);
        if (File.Exists(appDataPath))
            return appDataPath;

        // 3. Winget install — create in %APPDATA%\MicMute\
        if (UpdateDialog.IsWingetManaged())
        {
            Directory.CreateDirectory(appDataDir);
            return appDataPath;
        }

        // 4. Traditional portable — create next to exe
        return portablePath;
    }

    public void Load()
    {
        if (!File.Exists(_iniPath))
            return;

        FixEncoding();

        Hotkey = MigrateLegacyHotkey(ReadIni("Hotkey", Hotkey));
        SoundFeedback = ReadIni("SoundFeedback", "1") == "1";
        Mode = ReadIni("Mode", Mode).Trim();
        if (Mode != "toggle" && Mode != "push-to-talk")
            Mode = "toggle";
        DeviceId = ReadIni("DeviceId", "").Trim();
        IconMuted = SanitizePath(ReadIni("IconMuted", "").Trim());
        IconActive = SanitizePath(ReadIni("IconActive", "").Trim());
        MuteSound = SanitizePath(ReadIni("MuteSound", "").Trim());
        UnmuteSound = SanitizePath(ReadIni("UnmuteSound", "").Trim());
        MuteLock = ReadIni("MuteLock", "0") == "1";
        OsdEnabled = ReadIni("OSD_Enabled", "0") == "1";
        if (int.TryParse(ReadIni("OSD_Duration", "1500"), out int dur))
            OsdDuration = Math.Max(500, dur);
        DeafenHotkey = MigrateLegacyHotkey(ReadIni("DeafenHotkey", "").Trim());
        MiddleClickToggle = ReadIni("MiddleClickToggle", "1") == "1";
        StartMuted = ReadIni("StartMuted", "no").Trim().ToLowerInvariant();
        if (StartMuted != "no" && StartMuted != "yes" && StartMuted != "unmuted" && StartMuted != "last")
            StartMuted = "no";
        LastMuteState = ReadIni("LastMuteState", "0") == "1";
        LowLatencyPtt = ReadIni("LowLatencyPtt", "0") == "1";

        // Persist any v2.1.5 → v2.1.6 bare-hotkey migrations so the next
        // launch reads the already-valid value (and so the user's "change
        // hotkey" flow doesn't revert to the original legacy string).
        if (_migrationPending)
        {
            _migrationPending = false;
            Save();
        }
    }

    /// <summary>
    /// v2.1.6 tightened ParseHotkey to reject modifier-less bindings (which
    /// would hijack that key globally). This breaks v2.1.5 users whose INI
    /// had bare hotkeys like "Pause" or "PrintScreen". On first load of a
    /// legacy INI, rewrite these with a safe "Ctrl+Shift+" prefix so their
    /// hotkey keeps working instead of silently falling back to tray-only.
    /// </summary>
    private string MigrateLegacyHotkey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (ParseHotkey(value, out _, out _))
            return value; // already valid under v2.1.6 rules

        // Try prepending Ctrl+Shift. If that parses, the original was just
        // missing modifiers — salvageable. If it still fails, the config is
        // genuinely unparseable and RegisterMainHotkey will surface an error.
        string migrated = "^+" + value;
        if (!ParseHotkey(migrated, out _, out _))
            return value;

        _migrationPending = true;
        return migrated;
    }

    /// <summary>
    /// Serialize the full config as a canonical INI and replace the file
    /// atomically (write to .tmp, File.Move swap). Returns true on success.
    /// v2.1.6 and earlier used 14 sequential WritePrivateProfileString
    /// calls per Save(), which could corrupt the INI if the system crashed
    /// mid-sequence — leaving users with truncated config on reboot.
    /// </summary>
    public bool Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine("Hotkey=" + Hotkey);
        sb.AppendLine("SoundFeedback=" + (SoundFeedback ? "1" : "0"));
        sb.AppendLine("Mode=" + Mode);
        sb.AppendLine("DeviceId=" + DeviceId);
        sb.AppendLine("IconMuted=" + IconMuted);
        sb.AppendLine("IconActive=" + IconActive);
        sb.AppendLine("MuteSound=" + MuteSound);
        sb.AppendLine("UnmuteSound=" + UnmuteSound);
        sb.AppendLine("MuteLock=" + (MuteLock ? "1" : "0"));
        sb.AppendLine("OSD_Enabled=" + (OsdEnabled ? "1" : "0"));
        sb.AppendLine("OSD_Duration=" + OsdDuration.ToString());
        sb.AppendLine("DeafenHotkey=" + DeafenHotkey);
        sb.AppendLine("MiddleClickToggle=" + (MiddleClickToggle ? "1" : "0"));
        sb.AppendLine("StartMuted=" + StartMuted);
        sb.AppendLine("LastMuteState=" + (LastMuteState ? "1" : "0"));
        sb.AppendLine("LowLatencyPtt=" + (LowLatencyPtt ? "1" : "0"));

        return WriteAtomic(sb.ToString());
    }

    public bool SaveLastMuteState(bool muted)
    {
        if (StartMuted != "last")
            return true;
        LastMuteState = muted;
        return Save();
    }

    private bool WriteAtomic(string content)
    {
        string tmp = _iniPath + ".tmp";
        try
        {
            string dir = Path.GetDirectoryName(_iniPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, _iniPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Config save failed ({_iniPath}): {ex.GetType().Name}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch { /* cleanup best-effort */ }
            return false;
        }
    }

    /// <summary>
    /// Converts AHK hotkey string to human-readable form.
    /// e.g. "#+a" → "Win + Shift + A"
    /// </summary>
    public static string HotkeyToReadable(string hk)
    {
        if (string.IsNullOrEmpty(hk))
            return "(none)";

        var prefixMatch = s_modifierPrefix.Match(hk);
        string prefix = prefixMatch.Success ? prefixMatch.Value : "";
        string key = s_modifierPrefix.Replace(hk, "");

        string side = prefix.Contains('<') ? "L" : prefix.Contains('>') ? "R" : "";
        var sb = new StringBuilder();
        if (prefix.Contains('#')) sb.Append(side).Append("Win+");
        if (prefix.Contains('^')) sb.Append(side).Append("Ctrl+");
        if (prefix.Contains('!')) sb.Append(side).Append("Alt+");
        if (prefix.Contains('+')) sb.Append(side).Append("Shift+");
        sb.Append(PrettifyKeyName(key));
        return sb.ToString();
    }

    private static string PrettifyKeyName(string key) => key.ToUpperInvariant() switch
    {
        "LCTRL" or "LCONTROL" => "LCtrl",
        "RCTRL" or "RCONTROL" => "RCtrl",
        "LSHIFT" => "LShift",
        "RSHIFT" => "RShift",
        "LALT" or "LMENU" => "LAlt",
        "RALT" or "RMENU" => "RAlt",
        "LWIN" => "LWin",
        "RWIN" => "RWin",
        _ => key.ToUpperInvariant(),
    };

    /// <summary>
    /// Extracts the key name from an AHK hotkey string (strips modifier symbols).
    /// </summary>
    public static string ExtractKeyName(string hk)
    {
        return s_stripModifiers.Replace(hk, "");
    }

    /// <summary>
    /// Parses AHK modifier symbols into Win32 modifier flags + virtual key code.
    /// Returns false if the key cannot be mapped. Bare keys (no modifier) are
    /// rejected by default because they would hijack every press globally; set
    /// <paramref name="allowBare"/> to true only on paths that use polling
    /// (Low-latency PTT) instead of <c>RegisterHotKey</c>.
    /// </summary>
    public static bool ParseHotkey(string hk, out uint modifiers, out uint vk, bool allowBare = false)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrEmpty(hk))
            return false;

        var prefixMatch = s_modifierPrefix.Match(hk);
        string prefix = prefixMatch.Success ? prefixMatch.Value : "";
        string keyName = s_modifierPrefix.Replace(hk, "");

        if (prefix.Contains('#')) modifiers |= NativeMethods.MOD_WIN;
        if (prefix.Contains('^')) modifiers |= NativeMethods.MOD_CONTROL;
        if (prefix.Contains('!')) modifiers |= NativeMethods.MOD_ALT;
        if (prefix.Contains('+')) modifiers |= NativeMethods.MOD_SHIFT;

        uint realMods = modifiers; // before MOD_NOREPEAT is added
        modifiers |= NativeMethods.MOD_NOREPEAT;

        vk = KeyNameToVk(keyName);
        if (vk == 0)
            return false;

        // Bare keys are only acceptable on the polling path (allowBare).
        // RegisterHotKey either rejects bare modifiers outright or — worse —
        // would hijack a generic key globally. Function keys are an exception:
        // they're rarely typed during normal use and RegisterHotKey handles
        // them. Everything else requires a modifier unless allowBare is set.
        bool isFunctionKey = vk >= 0x70 && vk <= 0x87; // VK_F1..VK_F24
        if (realMods == 0 && !isFunctionKey && !allowBare)
            return false;

        return true;
    }

    /// <summary>
    /// True when a hotkey would reasonably fire during ordinary app use
    /// (bare letter/digit/Space/Enter/Tab/Esc/Backspace, Shift+letter such
    /// as every capital letter typed, or ANY Ctrl-containing combo with a
    /// plain letter — Ctrl+A/C/V/X/Z/S/F are every-app shortcuts and so are
    /// Ctrl+Shift+letter and Ctrl+Alt+letter). L/R modifiers, function keys,
    /// and Win-combos are NOT risky (they're the happy path).
    /// </summary>
    public static bool IsRiskyHotkey(uint modifiers, uint vk)
    {
        uint realMods = modifiers & ~NativeMethods.MOD_NOREPEAT;
        bool isLetter = vk >= 'A' && vk <= 'Z';
        bool isDigit = vk >= '0' && vk <= '9';

        // Bare keys — no modifier held.
        if (realMods == 0)
        {
            if (IsBareModifierVk(vk)) return false;
            if (vk >= 0x70 && vk <= 0x87) return false; // F1-F24
            if (isLetter || isDigit) return true;
            if (vk is 0x20 or 0x0D or 0x09 or 0x1B or 0x08) return true; // Space/Enter/Tab/Esc/Backspace
            return false;
        }

        // Shift+letter — every capital letter you type.
        if (realMods == NativeMethods.MOD_SHIFT && isLetter)
            return true;

        // Any Ctrl-containing combo on a plain letter. Covers Ctrl+A, Ctrl+C,
        // Ctrl+Shift+A (VS Code), Ctrl+Alt+letter (some intl keyboards / AltGr).
        // Win-combos are intentionally NOT flagged — the user chose them.
        if ((realMods & NativeMethods.MOD_CONTROL) != 0 &&
            (realMods & NativeMethods.MOD_WIN) == 0 &&
            isLetter)
            return true;

        return false;
    }

    /// <summary>
    /// Whether the given VK is a distinct left/right modifier key. These
    /// are permitted as bare hotkeys because (a) they're unlikely to
    /// conflict with typing (nobody types "right ctrl" into a document)
    /// and (b) the low-latency PTT path specifically supports them so
    /// users can match their Discord binding.
    /// </summary>
    internal static bool IsBareModifierVk(uint vk) =>
        vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private static uint KeyNameToVk(string keyName)
    {
        if (string.IsNullOrEmpty(keyName))
            return 0;

        // Single character: A-Z, 0-9
        if (keyName.Length == 1)
        {
            char c = char.ToUpperInvariant(keyName[0]);
            if (c is >= 'A' and <= 'Z')
                return (uint)c;
            if (c is >= '0' and <= '9')
                return (uint)c;
        }

        // Function keys F1-F24
        if (keyName.StartsWith('F') && int.TryParse(keyName.AsSpan(1), out int fNum) && fNum is >= 1 and <= 24)
            return (uint)(0x6F + fNum); // VK_F1=0x70

        // Named keys
        return keyName.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "BACKSPACE" or "BS" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "CAPSLOCK" => 0x14,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            "PRINTSCREEN" => 0x2C,
            "PAUSE" => 0x13,
            // Side-specific modifiers — usable as bare hotkeys in low-latency
            // PTT mode (Discord-style "right ctrl alone" bindings).
            "LCTRL" or "LCONTROL" => 0xA2,
            "RCTRL" or "RCONTROL" => 0xA3,
            "LSHIFT" => 0xA0,
            "RSHIFT" => 0xA1,
            "LALT" or "LMENU" => 0xA4,
            "RALT" or "RMENU" => 0xA5,
            "LWIN" => 0x5B,
            "RWIN" => 0x5C,
            "NUMPAD0" => 0x60,
            "NUMPAD1" => 0x61,
            "NUMPAD2" => 0x62,
            "NUMPAD3" => 0x63,
            "NUMPAD4" => 0x64,
            "NUMPAD5" => 0x65,
            "NUMPAD6" => 0x66,
            "NUMPAD7" => 0x67,
            "NUMPAD8" => 0x68,
            "NUMPAD9" => 0x69,
            "NUMPADADD" or "NUMPAD+" => 0x6B,
            "NUMPADSUB" or "NUMPAD-" => 0x6D,
            "NUMPADMULT" or "NUMPAD*" => 0x6A,
            "NUMPADDIV" or "NUMPAD/" => 0x6F,
            "NUMPADDOT" or "NUMPAD." => 0x6E,
            "NUMPADENTER" => 0x0D, // same VK, distinguishable by extended flag
            // VK_OEM_* punctuation — HotkeyDialog can capture these as bare keys
            // (US layout — other layouts map the same VK to different glyphs)
            ";" or ":" => 0xBA,      // VK_OEM_1
            "=" or "+" => 0xBB,      // VK_OEM_PLUS
            "," or "<" => 0xBC,      // VK_OEM_COMMA
            "-" or "_" => 0xBD,      // VK_OEM_MINUS
            "." or ">" => 0xBE,      // VK_OEM_PERIOD
            "/" or "?" => 0xBF,      // VK_OEM_2
            "`" or "~" => 0xC0,      // VK_OEM_3
            "[" or "{" => 0xDB,      // VK_OEM_4
            "\\" or "|" => 0xDC,     // VK_OEM_5
            "]" or "}" => 0xDD,      // VK_OEM_6
            "'" or "\"" => 0xDE,     // VK_OEM_7
            _ => 0,
        };
    }

    /// <summary>
    /// Reject path forms that reach network shares or devices — these can
    /// leak NTLM credentials over SMB (`\\server\share`, `\\?\UNC\...`)
    /// or hit NT object namespace (`\\.\pipe\...`). Also reject the
    /// `file://` URI form since SoundPlayer/Icon happily dereferences it
    /// back into the same UNC paths we're trying to block.
    /// </summary>
    private static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        string trimmed = path.TrimStart();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            return "";                                      // `\\server\share` UNC, `\\?\UNC\...`, `\\.\device`
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            return "";                                      // forward-slash UNC variant
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return "";                                      // `file://server/share/...` → SMB auth
        return path;
    }

    private string ReadIni(string key, string defaultValue)
    {
        var sb = new StringBuilder(512);
        GetPrivateProfileString("General", key, defaultValue, sb, 512, _iniPath);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Fix UTF-16 LE without BOM encoding issue (mirrors AHK FixIniEncoding).
    /// Rewrites atomically via .tmp swap so a crash mid-rewrite can't corrupt
    /// the INI beyond recovery.
    /// </summary>
    private void FixEncoding()
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(_iniPath);
            if (bytes.Length < 4)
                return;
            // Check for UTF-16 LE BOM or plain ANSI
            if ((bytes[0] == 0xFF && bytes[1] == 0xFE) || bytes[1] != 0x00)
                return;
            // UTF-16 LE without BOM — re-encode to UTF-8
            string content = Encoding.Unicode.GetString(bytes);
            WriteAtomic(content);
        }
        catch
        {
            // Ignore encoding fix failures
        }
    }
}
