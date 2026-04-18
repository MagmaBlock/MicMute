namespace MicMute;

/// <summary>
/// Main application form — runs as a hidden window with a system tray icon.
/// Handles global hotkeys, mute state management, periodic sync, and all UI.
/// </summary>
internal sealed class TrayApp : Form
{
    private const int HOTKEY_ID_MAIN = 1;
    private const int HOTKEY_ID_DEAFEN = 2;

    private readonly Config _config;
    private readonly AudioManager _audio;
    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _trayMenu;
    private readonly System.Windows.Forms.Timer _syncTimer;
    private readonly OsdForm _osdForm;

    // Cached icons — loaded once at startup, reloaded on settings change
    private Icon _iconActive;
    private Icon _iconMuted;
    // State
    private bool _muted;
    private bool _deafened;
    private bool _speakerWasMuted;
    private bool _lockDebounce;
    private bool _flashing;
    private int _flashCount;
    private readonly System.Windows.Forms.Timer _flashTimer;

    // Explorer restart detection
    private readonly uint _wmTaskbarCreated;

    // Cached tooltip strings — only rebuilt on state change
    private string _cachedTooltipMuted = "";
    private string _cachedTooltipActive = "";
    private bool _tooltipDirty = true;

    // Track registered hotkeys for cleanup
    private bool _mainHotkeyRegistered;
    private bool _deafenHotkeyRegistered;

    // Singleton dialogs
    private SettingsDialog _settingsDialog;

    // Device menu lazy loading
    private ToolStripMenuItem _deviceMenuItem;
    private bool _deviceMenuPopulated;

    // Reusable bold font for menu title (disposed in cleanup)
    private Font _menuTitleFont;

    // One-shot set — we log a custom-sound failure once per file path so a
    // broken MuteSound doesn't spam the log on every toggle.
    private readonly HashSet<string> _playSoundFailed = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;
    private bool _shuttingDown;

    public TrayApp()
    {
        // Hidden window — no taskbar presence
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0;
        Size = Size.Empty;

        _config = new Config();
        _config.Load();

        _audio = new AudioManager();

        // Load icons
        LoadIcons();

        // Initialize audio endpoint
        bool hasAudio = _audio.Initialize(_config.DeviceId);

        // Read initial mute state
        if (hasAudio)
        {
            bool? currentMute = _audio.GetMute();
            _muted = currentMute ?? false;
            ApplyStartupMutePreference();
        }

        // Tray icon
        _trayMenu = new ContextMenuStrip();
        _trayIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _trayIcon.MouseClick += OnTrayMouseClick;

        // Build menu
        BuildTrayMenu();
        InvalidateTooltipCache();
        SyncTrayIcon();

        // OSD form (must be created before hotkey registration — errors show via OSD)
        _osdForm = new OsdForm();

        // Flash timer (reusable, not started)
        _flashTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _flashTimer.Tick += OnFlashTick;

        // Explorer restart recovery
        _wmTaskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        // Register hotkeys
        RegisterMainHotkey();
        RegisterDeafenHotkey();

        // Periodic sync timer — 15s to detect mic plug/unplug and external mute changes.
        // Hotkey actions sync immediately, so this doesn't affect responsiveness.
        _syncTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        _syncTimer.Tick += OnSyncTick;
        _syncTimer.Start();

        // Show notification if no mic found
        if (!hasAudio)
        {
            ShowTimedTooltip("No microphone detected.\nPlug one in \u2014 MicMute will auto-detect it.", 5000);
        }
    }

    // ── Icon Loading ──────────────────────────────────────────────────────

    private void LoadIcons()
    {
        _iconActive = LoadIcon(_config.IconActive, "mic_on.ico");
        _iconMuted = LoadIcon(_config.IconMuted, "mic_off.ico");
    }

    private static Icon LoadIcon(string customPath, string embeddedName)
    {
        // All icons come back pre-rasterized to the current tray small-icon
        // size, so Shell_NotifyIcon doesn't have to rescale on every update.
        // Without this, a 256x256 .ico forces the shell to resample down to
        // 16/24/32 px on every NIM_MODIFY — noticeable as per-click lag.
        using Icon raw = LoadIconRaw(customPath, embeddedName);
        try
        {
            return RasterizeToTraySize(raw);
        }
        catch (Exception ex)
        {
            Log.Warn($"LoadIcon rasterize failed: {ex.Message}");
            return (Icon)raw.Clone();
        }
    }

    private static Icon LoadIconRaw(string customPath, string embeddedName)
    {
        // Priority: custom path > file on disk next to exe > embedded resource
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
        {
            try { return new Icon(customPath); }
            catch (Exception ex) { Log.Warn($"LoadIcon custom '{customPath}' failed: {ex.Message}"); }
        }

        string dir = Path.GetDirectoryName(Environment.ProcessPath ?? "")
            ?? AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrEmpty(dir))
        {
            string diskPath = Path.Combine(dir, embeddedName);
            if (File.Exists(diskPath))
            {
                try { return new Icon(diskPath); }
                catch (Exception ex) { Log.Warn($"LoadIcon disk '{diskPath}' failed: {ex.Message}"); }
            }
        }

        // Embedded resource
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream(embeddedName);
        if (stream != null)
            return new Icon(stream);

        // Ultimate fallback — clone the system icon so it's safe to dispose
        return (Icon)SystemIcons.Application.Clone();
    }

    private static Icon RasterizeToTraySize(Icon src)
    {
        Size target = SystemInformation.SmallIconSize;
        using var bmp = new Bitmap(target.Width, target.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawIcon(src, new Rectangle(0, 0, target.Width, target.Height));
        }
        // Bitmap.GetHicon() returns a handle we must destroy ourselves; Icon.FromHandle
        // doesn't take ownership. Clone to produce an owned Icon, then free the temp.
        nint hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private void DisposeIcons()
    {
        // Safe to dispose — we always own these (cloned system icons or loaded from file/resource)
        _iconActive?.Dispose();
        _iconMuted?.Dispose();
    }

    // ── Tray Icon / Tooltip ──────────────────────────────────────────────

    private void InvalidateTooltipCache()
    {
        string modeName = _config.Mode == "push-to-talk" ? " [PTT]" : "";
        string suffix = _deafened ? " [DEAFENED]" : "";
        string combined = modeName + suffix;
        _cachedTooltipMuted = "MicMute v" + Config.Version + " \u2014 Mic: MUTED" + combined;
        _cachedTooltipActive = "MicMute v" + Config.Version + " \u2014 Mic: Active" + combined;
        _tooltipDirty = false;
    }

    private void SyncTrayIcon()
    {
        if (_flashing)
            return;
        SetTrayIcon();
    }

    private void SetTrayIcon()
    {
        if (_tooltipDirty)
            InvalidateTooltipCache();

        var icon = _muted ? _iconMuted : _iconActive;
        var tooltip = _muted ? _cachedTooltipMuted : _cachedTooltipActive;

        if (_trayIcon.Icon != icon)
            _trayIcon.Icon = icon;
        // NotifyIcon tooltip max 63 chars
        if (tooltip.Length > 63)
            tooltip = tooltip[..63];
        if (_trayIcon.Text != tooltip)
            _trayIcon.Text = tooltip;
    }

    // ── Core Mute Operations ─────────────────────────────────────────────

    private void ToggleMute()
    {
        if (!_audio.HasEndpoint)
        {
            ShowTimedTooltip("No microphone available.\nTry Tray \u2192 Reinitialise Mic.", 5000);
            return;
        }

        bool newState = !_muted;
        if (!_audio.SetMute(newState))
        {
            ShowTimedTooltip("SetMute failed. Device may have changed \u2014 try Reinitialise Mic.", 5000);
            return;
        }

        _muted = newState;
        _config.SaveLastMuteState(_muted);
        SyncTrayIcon();
        ShowOsd();
        PlayFeedback();
    }

    /// <summary>
    /// Apply a mute state. Returns true on success (or no-op), false if the
    /// Core Audio call failed. Callers MUST check the return value on paths
    /// where a silent failure would lie to the user — deafen entry and PTT
    /// press/release being the two catastrophic ones: the tray would show
    /// "muted" while the mic is actually hot, or vice-versa.
    /// </summary>
    private bool SetMuteState(bool muted, bool quiet = false)
    {
        if (!_audio.HasEndpoint)
            return false;
        if (_muted == muted)
            return true;

        if (!_audio.SetMute(muted))
            return false;

        _muted = muted;
        _config.SaveLastMuteState(_muted);
        SyncTrayIcon();

        if (!quiet)
        {
            ShowOsd();
            PlayFeedback();
        }
        return true;
    }

    // ── Sound Feedback ───────────────────────────────────────────────────

    private void PlayFeedback()
    {
        if (!_config.SoundFeedback)
            return;

        string soundFile = _muted ? _config.MuteSound : _config.UnmuteSound;
        if (!string.IsNullOrEmpty(soundFile) && File.Exists(soundFile))
        {
            // Use SND_ASYNC to avoid blocking the UI thread
            if (!NativeMethods.PlaySound(soundFile, 0,
                NativeMethods.SND_FILENAME | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT))
            {
                // Surface the first failure per file so a corrupt/unsupported WAV
                // doesn't silently fall back to beep forever. HashSet.Add returns
                // true only on the first occurrence — subsequent calls are quiet.
                if (_playSoundFailed.Add(soundFile))
                {
                    Log.Warn($"PlaySound failed for '{soundFile}'; falling back to beep");
                    ShowTimedTooltip(
                        "Custom sound couldn't play:\n" + Path.GetFileName(soundFile) +
                        "\nFalling back to the built-in beep.", 4000);
                }
                PlayToneSequence(_muted);
            }
        }
        else
        {
            PlayToneSequence(_muted);
        }
    }

    private static void PlayToneSequence(bool muted)
    {
        NativeMethods.Beep(muted ? 587u : 880u, 80);
    }

    private static void PlayModeChirp(string mode)
    {
        NativeMethods.Beep(mode == "push-to-talk" ? 1568u : 1175u, 50);
    }

    // ── OSD ──────────────────────────────────────────────────────────────

    private void ShowOsd()
    {
        if (!_config.OsdEnabled)
            return;
        _osdForm.ShowOsd(_muted, _config.OsdDuration);
    }

    // ── Icon Flash ───────────────────────────────────────────────────────

    private void FlashIcon()
    {
        if (_flashing)
            _flashTimer.Stop();
        _flashing = true;
        _flashCount = 0;
        _flashTimer.Start();
    }

    private void OnFlashTick(object sender, EventArgs e)
    {
        try
        {
            bool showOpposite = (_flashCount % 2) == 0;
            if (showOpposite)
            {
                // Show opposite icon
                _trayIcon.Icon = _muted ? _iconActive : _iconMuted;
            }
            else
            {
                SetTrayIcon();
            }

            _flashCount++;
            if (_flashCount >= 2)
            {
                _flashTimer.Stop();
                _flashing = false;
                SetTrayIcon();
            }
        }
        catch (Exception ex)
        {
            Log.Error("OnFlashTick", ex);
            _flashTimer.Stop();
            _flashing = false;
        }
    }

    // ── Periodic Sync ────────────────────────────────────────────────────

    private void OnSyncTick(object sender, EventArgs e)
    {
        try
        {
            if (!_audio.HasEndpoint)
            {
                // Try to find a newly plugged-in mic
                if (_audio.Initialize(_config.DeviceId))
                {
                    ShowTimedTooltip("Microphone detected \u2014 auto-connected.", 3000);
                    bool? m = _audio.GetMute();
                    _muted = m ?? false;
                    SyncTrayIcon();
                    // Re-arm the hotkey. Without this the polling path
                    // (which bails when HasEndpoint went false) never restarts,
                    // silently leaving the user with no hotkey until they
                    // mode-switch or relaunch.
                    RegisterMainHotkey();
                }
                return;
            }

            // Check if device is still valid
            bool? currentMute = _audio.GetMute();
            if (currentMute == null)
            {
                // Device went away
                _audio.Release();
                _muted = false;

                // If the user was deafened, mic loss shouldn't leave them
                // stuck with speakers muted and no UI affordance to recover.
                // Restore speakers to their pre-deafen state and clear the
                // deafen flag so the tooltip doesn't contradict itself.
                // Only clear _deafened if the speaker restore actually
                // succeeded — otherwise next mic-plug won't know we were
                // deafened and the user gets stuck with muted speakers AND
                // no "deafened" state to toggle off.
                if (_deafened)
                {
                    bool restored = true;
                    try { AudioManager.SetSpeakerMute(_speakerWasMuted); }
                    catch (Exception ex)
                    {
                        Log.Warn("SetSpeakerMute restore on device-loss failed: " + ex.Message);
                        restored = false;
                    }
                    if (restored)
                    {
                        _deafened = false;
                        _tooltipDirty = true;
                    }
                }

                SyncTrayIcon();
                ShowTimedTooltip("Microphone disconnected.\nWill auto-reconnect when available.", 5000);
                return;
            }

            bool externalMuted = currentMute.Value;
            if (externalMuted != _muted)
            {
                if (_config.MuteLock)
                {
                    // Fight back: re-apply our state
                    if (_lockDebounce)
                    {
                        _lockDebounce = false;
                        return;
                    }
                    if (_audio.SetMute(_muted))
                    {
                        _lockDebounce = true;
                    }
                    else
                    {
                        // Don't set the debounce on failure — we want the next
                        // tick to retry the fight-back instead of silently
                        // conceding the external state for a full 15s cycle.
                        Log.Warn("MuteLock fight-back SetMute failed; will retry on next sync tick");
                    }
                }
                else
                {
                    // Accept external change
                    _muted = externalMuted;
                    SyncTrayIcon();
                }
            }
            else
            {
                _lockDebounce = false;
            }
        }
        catch (Exception ex)
        {
            // Swallow to keep the tray alive — the global handler is our safety net,
            // but a transient audio-service hiccup shouldn't kill the app.
            Log.Error("OnSyncTick", ex);
        }
    }

    // ── Hotkey Registration ──────────────────────────────────────────────

    private void RegisterMainHotkey()
    {
        UnregisterMainHotkey();

        // PTT mode takes the polling path unconditionally — fullscreen-safe,
        // accepts bare modifier keys, no anti-cheat signature. Toggle mode
        // uses RegisterHotKey (event-driven, zero idle cost).
        bool pttMode = _config.Mode == "push-to-talk";
        if (!Config.ParseHotkey(_config.Hotkey, out uint mods, out uint vk, allowBare: pttMode))
        {
            ShowTimedTooltip("Invalid hotkey: " + _config.Hotkey + "\nFalling back to tray-only mode.", 5000);
            return;
        }

        if (pttMode)
        {
            StartUniversalPttPoll(vk, mods & ~NativeMethods.MOD_NOREPEAT);
            return;
        }

        if (NativeMethods.RegisterHotKey(Handle, HOTKEY_ID_MAIN, mods, vk))
            _mainHotkeyRegistered = true;
        else
            ShowTimedTooltip("Could not register hotkey: " + Config.HotkeyToReadable(_config.Hotkey), 5000);
    }

    private void UnregisterMainHotkey()
    {
        if (_mainHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_MAIN);
            _mainHotkeyRegistered = false;
        }

        // StopUniversalPttPoll handles the safety re-mute if the user was
        // holding PTT during a rebind / mode switch — don't duplicate here.
        StopUniversalPttPoll();
    }

    private void RegisterDeafenHotkey()
    {
        UnregisterDeafenHotkey();

        if (string.IsNullOrEmpty(_config.DeafenHotkey))
            return;

        if (!Config.ParseHotkey(_config.DeafenHotkey, out uint mods, out uint vk))
        {
            ShowTimedTooltip("Invalid deafen hotkey: " + _config.DeafenHotkey, 5000);
            return;
        }

        if (NativeMethods.RegisterHotKey(Handle, HOTKEY_ID_DEAFEN, mods, vk))
            _deafenHotkeyRegistered = true;
        else
            ShowTimedTooltip("Could not register deafen hotkey.", 5000);
    }

    private void UnregisterDeafenHotkey()
    {
        if (_deafenHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(Handle, HOTKEY_ID_DEAFEN);
            _deafenHotkeyRegistered = false;
        }
    }

    // ── WndProc ──────────────────────────────────────────────────────────

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id == HOTKEY_ID_MAIN)
            {
                // WM_HOTKEY only fires in Toggle mode now — PTT always runs
                // through the polling path. The mode guard defends against a
                // mode-switch race where a WM_HOTKEY is already queued when
                // we flip to PTT.
                if (_config.Mode != "push-to-talk")
                    ToggleMute();
            }
            else if (id == HOTKEY_ID_DEAFEN)
            {
                ToggleDeafen();
            }
        }
        else if (_wmTaskbarCreated != 0 && m.Msg == (int)_wmTaskbarCreated)
        {
            // Explorer restarted — re-show tray icon
            _trayIcon.Visible = true;
            SyncTrayIcon();
        }

        base.WndProc(ref m);
    }

    // ── Push-to-Talk (polling path) ──────────────────────────────────────
    // PTT mode always uses continuous GetAsyncKeyState polling — no
    // RegisterHotKey, no keyboard hook. 30 Hz is imperceptible for press
    // timing (human reaction ~200 ms) and CPU cost is a couple of syscalls
    // per tick. No hook = no anti-cheat signature = no risk of collateral
    // bans. Works over fullscreen-exclusive games and accepts bare modifier
    // keys (RCtrl alone, etc.) the way Discord does PTT.

    private System.Windows.Forms.Timer _pttUniversalTimer;
    private bool _pttUniversalDown;
    private uint _pttUniversalVk;
    private uint _pttUniversalMods;

    // Sticky PTT override: user left-clicked the tray while in PTT mode.
    // Polling is paused, mic is unmuted, a persistent OSD stays up until the
    // user left-clicks again (or changes mode, or exits). Lets the user hold
    // the mic open during a long conversation without rubber-banding on the
    // hotkey. See ToggleStickyPttOverride.
    private bool _pttStickyUnmuted;

    private void StartUniversalPttPoll(uint vk, uint mods)
    {
        // Idempotent — always drop any prior poll first. Covers back-to-back
        // RegisterMainHotkey calls from Settings-apply + mic-unplug races that
        // could otherwise leave a stale timer ticking against the old VK.
        StopUniversalPttPoll();

        // No mic → nothing to poll for. Sync-timer recovery path will
        // re-call RegisterMainHotkey once an endpoint reappears.
        if (!_audio.HasEndpoint) return;

        _pttUniversalVk = vk;
        _pttUniversalMods = mods;
        // Seed the edge-tracker from the key's CURRENT state. If the user is
        // already holding the key when we start polling (e.g. hit Apply while
        // holding PTT), the first tick would otherwise see an up→down edge
        // from nothing and fire a phantom unmute. Starting "down" means we
        // wait for a genuine release before the next press-edge triggers.
        _pttUniversalDown = (NativeMethods.GetAsyncKeyState((int)vk) & 0x8000) != 0;
        if (_pttUniversalTimer == null)
        {
            _pttUniversalTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _pttUniversalTimer.Tick += OnUniversalPttPoll;
        }
        _pttUniversalTimer.Start();
    }

    private void StopUniversalPttPoll()
    {
        if (_pttUniversalTimer == null) return;
        bool wasDown = _pttUniversalDown;
        _pttUniversalTimer.Stop();
        _pttUniversalDown = false;
        // Safety: if we stop while the user is still holding the key, the
        // natural release edge will never arrive — re-mute defensively.
        if (!_shuttingDown && wasDown && _audio.HasEndpoint && !_muted)
        {
            if (!SetMuteState(true, true))
                Log.Warn("Universal PTT safety re-mute failed during stop");
        }
    }

    private void OnUniversalPttPoll(object sender, EventArgs e)
    {
        try
        {
            // Mic unplugged mid-hold or during idle? Stop cleanly. Sync timer
            // will re-arm the hotkey path when a mic comes back.
            if (!_audio.HasEndpoint)
            {
                StopUniversalPttPoll();
                return;
            }

            bool keyDown = (NativeMethods.GetAsyncKeyState((int)_pttUniversalVk) & 0x8000) != 0;

            if (!_pttUniversalDown && keyDown && ModifiersMatch(_pttUniversalMods, _pttUniversalVk))
            {
                _pttUniversalDown = true;
                if (!SetMuteState(false, true))
                {
                    Log.Error("Universal PTT unmute failed");
                    ShowTimedTooltip(
                        "Couldn't unmute microphone.\nTry Tray \u2192 Reinit Mic.", 4000);
                }
            }
            else if (_pttUniversalDown && !keyDown)
            {
                _pttUniversalDown = false;
                if (!SetMuteState(true, true))
                {
                    Log.Error("Universal PTT re-mute failed — mic may still be hot");
                    ShowTimedTooltip(
                        "Couldn't re-mute microphone after PTT.\n" +
                        "Mic may still be open \u2014 verify in Windows.", 4000);
                }
            }
        }
        catch (Exception ex)
        {
            // Stop on exception — otherwise a steady-state fault spams the log
            // every 30 ms on steady-state faults.
            Log.Error("OnUniversalPttPoll", ex);
            _pttUniversalTimer?.Stop();

            // If the fault fired AFTER we unmuted for a press, the release
            // edge will never arrive — defensively re-mute so the mic doesn't
            // stay hot. This is the "silent hot mic" class of bug MicMute
            // exists to prevent.
            if (_pttUniversalDown && _audio.HasEndpoint && !_muted)
            {
                try
                {
                    if (SetMuteState(true, true))
                    {
                        _pttUniversalDown = false;
                    }
                    else
                    {
                        Log.Warn("Universal PTT exception-path re-mute failed — mic may be hot");
                        ShowTimedTooltip(
                            "Couldn't re-mute microphone after PTT error.\n" +
                            "Mic may be open \u2014 verify in Windows.", 4000);
                    }
                }
                catch (Exception inner)
                {
                    Log.Error("Exception-path re-mute threw", inner);
                }
            }
        }
    }

    private static bool ModifiersMatch(uint required, uint targetVk)
    {
        const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        bool targetCtrl  = targetVk is 0xA2 or 0xA3;
        bool targetAlt   = targetVk is 0xA4 or 0xA5;
        bool targetShift = targetVk is 0xA0 or 0xA1;
        bool targetWin   = targetVk is 0x5B or 0x5C;

        if (!CheckMod(required, NativeMethods.MOD_CONTROL, targetCtrl,  VK_CONTROL)) return false;
        if (!CheckMod(required, NativeMethods.MOD_ALT,     targetAlt,   VK_MENU))    return false;
        if (!CheckMod(required, NativeMethods.MOD_SHIFT,   targetShift, VK_SHIFT))   return false;
        if (!CheckModWin(required, targetWin, VK_LWIN, VK_RWIN)) return false;
        return true;
    }

    private static bool CheckMod(uint required, uint flag, bool targetIsModifier, int genericVk)
    {
        // If the target key IS this modifier, it's inherently down during a
        // press — don't fail the match on its own side-channel state.
        if (targetIsModifier) return true;
        bool wanted = (required & flag) != 0;
        bool down = (NativeMethods.GetAsyncKeyState(genericVk) & 0x8000) != 0;
        return wanted == down;
    }

    private static bool CheckModWin(uint required, bool targetIsWin, int vkLWin, int vkRWin)
    {
        if (targetIsWin) return true;
        bool wanted = (required & NativeMethods.MOD_WIN) != 0;
        bool down = ((NativeMethods.GetAsyncKeyState(vkLWin) & 0x8000) != 0)
                 || ((NativeMethods.GetAsyncKeyState(vkRWin) & 0x8000) != 0);
        return wanted == down;
    }

    // ── Deafen Mode ──────────────────────────────────────────────────────

    private void ToggleDeafen()
    {
        if (!_audio.HasEndpoint)
            return;

        if (!_deafened)
        {
            // Enter deafen — CRITICAL: if the mic-mute step fails, ABORT.
            // We must never show "DEAFENED — mic + speakers muted" while the
            // mic is actually live. Speaker-mute is skipped and _deafened stays
            // false so the tooltip tells the truth and the user can retry.
            try { _speakerWasMuted = AudioManager.GetSpeakerMute(); }
            catch (Exception ex) { Log.Warn("GetSpeakerMute failed, assuming unmuted: " + ex.Message); _speakerWasMuted = false; }

            if (!_muted && !SetMuteState(true))
            {
                Log.Error("Deafen aborted: mic mute failed — NOT muting speakers, NOT setting deafen flag");
                ShowTimedTooltip(
                    "Couldn't mute microphone \u2014 deafen aborted.\nTry Tray \u2192 Reinit Mic.", 5000);
                return;
            }

            try { AudioManager.SetSpeakerMute(true); }
            catch (Exception ex) { Log.Error("SetSpeakerMute(true) failed during deafen-enter", ex); }

            _deafened = true;
            _tooltipDirty = true;
            SetTrayIcon();
            ShowTimedTooltip("DEAFENED \u2014 mic + speakers muted", 3000);
        }
        else
        {
            // Exit deafen — best-effort. If mic-unmute fails the user still
            // wants speakers restored, and they'll see a warning about the mic.
            bool micUnmuted = SetMuteState(false);
            try { AudioManager.SetSpeakerMute(_speakerWasMuted); }
            catch (Exception ex) { Log.Error("SetSpeakerMute restore failed during deafen-exit", ex); }

            _deafened = false;
            _tooltipDirty = true;
            SetTrayIcon();
            if (micUnmuted)
                ShowTimedTooltip("Undeafened \u2014 audio restored", 3000);
            else
                ShowTimedTooltip(
                    "Undeafened, but microphone unmute failed.\nTry Tray \u2192 Reinit Mic.", 5000);
        }
    }

    // ── Mode Switching ───────────────────────────────────────────────────

    private void SetMode(string newMode)
    {
        // Any mode change invalidates sticky PTT — the override only makes
        // sense inside PTT mode. Clear it before the mode flips so the
        // persistent OSD doesn't linger under toggle-mode state.
        ClearStickyPttOverride();

        _config.Mode = newMode;
        if (newMode == "push-to-talk")
        {
            // Best-effort prep-mute on mode switch — a silent failure here
            // would be caught by the next sync tick anyway, and the user
            // hasn't pressed the PTT key yet, so no "lying UI" risk.
            if (!SetMuteState(true, true))
                Log.Warn("Prep-mute for PTT mode switch failed; sync timer will reconcile");
        }
        RegisterMainHotkey();
        BuildTrayMenu();
        _tooltipDirty = true;
        SetTrayIcon();
        if (!_config.Save())
            ShowTimedTooltip("Mode changed, but settings couldn't be saved.\nCheck permissions on MicMute.ini.", 4000);

        if (_config.SoundFeedback)
            PlayModeChirp(newMode);

        string modeName = newMode == "push-to-talk" ? "Push-to-Talk" : "Toggle";
        ShowTimedTooltip("Mode: " + modeName, 3000);
    }

    // ── Tray Menu ────────────────────────────────────────────────────────

    private void BuildTrayMenu()
    {
        // Dispose old menu items before clearing to prevent GDI/memory leaks
        DisposeMenuItems();

        string hotkeyReadable = Config.HotkeyToReadable(_config.Hotkey);

        // Reusable bold font for title item
        _menuTitleFont?.Dispose();
        _menuTitleFont = new Font(_trayMenu.Font, FontStyle.Bold);

        // Title — program name + version. Still clickable so it matches the
        // tray left-click (toggle in Toggle mode, sticky-PTT in PTT mode),
        // but the label no longer implies "toggle" since that wording was
        // misleading in PTT mode.
        var titleItem = new ToolStripMenuItem("MicMute v" + Config.Version)
        {
            Font = _menuTitleFont,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        titleItem.Click += (_, _) => HandleUserToggleClick();
        _trayMenu.Items.Add(titleItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        // Hotkey — the combo is the label. Click opens Settings (inline
        // capture lives in the Hotkeys section there). Bold font + bracketing
        // separators mark it as a reference line, not an ordinary action.
        string hotkeyDisplay = string.IsNullOrEmpty(_config.Hotkey)
            ? "(no hotkey bound \u2014 click to set)"
            : hotkeyReadable;
        var hotkeyItem = new ToolStripMenuItem(hotkeyDisplay) { Font = _menuTitleFont };
        hotkeyItem.Click += (_, _) => ShowSettingsDialog();
        _trayMenu.Items.Add(hotkeyItem);
        _trayMenu.Items.Add(new ToolStripSeparator());

        // Mode submenu
        string modeLabel = "Mode: " + (_config.Mode == "push-to-talk" ? "Push-to-Talk" : "Toggle");
        var modeItem = new ToolStripMenuItem(modeLabel);
        var toggleModeItem = new ToolStripMenuItem("Toggle");
        toggleModeItem.Checked = _config.Mode == "toggle";
        toggleModeItem.Click += (_, _) => SetMode("toggle");
        var pttModeItem = new ToolStripMenuItem("Push-to-Talk");
        pttModeItem.Checked = _config.Mode == "push-to-talk";
        pttModeItem.Click += (_, _) => SetMode("push-to-talk");
        modeItem.DropDownItems.Add(toggleModeItem);
        modeItem.DropDownItems.Add(pttModeItem);
        _trayMenu.Items.Add(modeItem);

        // Device submenu (lazy-loaded)
        _deviceMenuItem = new ToolStripMenuItem("Mic Source");
        var loadingItem = new ToolStripMenuItem("Loading\u2026") { Enabled = false };
        _deviceMenuItem.DropDownItems.Add(loadingItem);
        _deviceMenuItem.DropDownOpening += OnDeviceMenuOpening;
        _deviceMenuPopulated = false;
        _trayMenu.Items.Add(_deviceMenuItem);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // Reinit
        var reinitItem = new ToolStripMenuItem("Reinit Mic");
        reinitItem.Click += (_, _) => ReinitMic();
        _trayMenu.Items.Add(reinitItem);

        // Sound settings
        var soundItem = new ToolStripMenuItem("Sound Settings");
        soundItem.Click += (_, _) =>
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true,
            });
        };
        _trayMenu.Items.Add(soundItem);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // Settings — bracketed by separators so it sits in its own section
        // right above Exit, away from the action items.
        var settingsItem = new ToolStripMenuItem("Settings\u2026");
        settingsItem.Click += (_, _) => ShowSettingsDialog();
        _trayMenu.Items.Add(settingsItem);

        _trayMenu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        _trayMenu.Items.Add(exitItem);
    }

    /// <summary>
    /// Dispose all current menu items before rebuilding the menu.
    /// ToolStripItemCollection.Clear() does NOT dispose items.
    /// </summary>
    private void DisposeMenuItems()
    {
        for (int i = _trayMenu.Items.Count - 1; i >= 0; i--)
        {
            var item = _trayMenu.Items[i];
            // Dispose sub-items recursively for ToolStripMenuItems with dropdowns
            if (item is ToolStripMenuItem menuItem)
            {
                for (int j = menuItem.DropDownItems.Count - 1; j >= 0; j--)
                    menuItem.DropDownItems[j].Dispose();
            }
            item.Dispose();
        }
        _trayMenu.Items.Clear();
    }

    private void OnDeviceMenuOpening(object sender, EventArgs e)
    {
        if (_deviceMenuPopulated)
            return;
        PopulateDeviceMenu();
    }

    private void PopulateDeviceMenu()
    {
        // Dispose old placeholder items
        for (int i = _deviceMenuItem.DropDownItems.Count - 1; i >= 0; i--)
            _deviceMenuItem.DropDownItems[i].Dispose();
        _deviceMenuItem.DropDownItems.Clear();

        var defaultItem = new ToolStripMenuItem("System Default");
        defaultItem.Checked = string.IsNullOrEmpty(_config.DeviceId);
        defaultItem.Click += (_, _) => SelectDevice("");
        _deviceMenuItem.DropDownItems.Add(defaultItem);
        _deviceMenuItem.DropDownItems.Add(new ToolStripSeparator());

        var devices = AudioManager.EnumerateCaptureDevices();
        foreach (var dev in devices)
        {
            string label = dev.Name.Length > 40 ? dev.Name[..37] + "\u2026" : dev.Name;
            var item = new ToolStripMenuItem(label);
            item.Checked = _config.DeviceId == dev.Id;
            string devId = dev.Id; // capture for closure
            item.Click += (_, _) => SelectDevice(devId);
            _deviceMenuItem.DropDownItems.Add(item);
        }

        _deviceMenuPopulated = true;
    }

    private void SelectDevice(string deviceId)
    {
        _config.DeviceId = deviceId;
        _audio.Release();
        if (_audio.Initialize(deviceId))
        {
            bool? m = _audio.GetMute();
            _muted = m ?? false;
        }
        else
        {
            _muted = false;
        }
        SyncTrayIcon();
        BuildTrayMenu();
        if (!_config.Save())
            ShowTimedTooltip("Mic source changed, but settings couldn't be saved.\nCheck permissions on MicMute.ini.", 4000);

        ShowTimedTooltip(string.IsNullOrEmpty(deviceId)
            ? "Using system default microphone."
            : "Switched microphone.", 3000);
    }

    // ── Reinitialize Mic ─────────────────────────────────────────────────

    private void ReinitMic()
    {
        _audio.Release();
        if (_audio.Initialize(_config.DeviceId))
        {
            bool? m = _audio.GetMute();
            _muted = m ?? false;
            SyncTrayIcon();
            ShowTimedTooltip("Microphone reinitialised.", 3000);
        }
        else
        {
            ShowTimedTooltip("No microphone found.", 5000);
        }
    }

    // ── Startup Mute Preference ──────────────────────────────────────────

    private void ApplyStartupMutePreference()
    {
        // Attempt the startup state change, then re-read from the device so
        // the UI doesn't claim a mute state the hardware didn't actually
        // accept (rare, but possible on a device that's initialized but
        // flaky). Sync timer will eventually correct on its own; this keeps
        // the tray/OSD consistent from the first frame.
        bool targetKnown = true;
        bool target = _muted;

        switch (_config.StartMuted)
        {
            case "yes" when !_muted:
                target = true;
                break;
            case "unmuted" when _muted:
                target = false;
                break;
            case "last" when _config.LastMuteState != _muted:
                target = _config.LastMuteState;
                break;
            default:
                // PTT mode implies mic-off-until-held. Without an explicit
                // StartMuted preference, a hot mic at launch means the first
                // PTT press is an invisible no-op (SetMuteState short-circuits
                // on already-unmuted). Force-mute so PTT behaves from frame 1.
                if (_config.Mode == "push-to-talk" && !_muted)
                    target = true;
                else
                    targetKnown = false;
                break;
        }

        if (!targetKnown)
            return;

        if (_audio.SetMute(target))
            _muted = target;
        else
            _muted = _audio.GetMute() ?? _muted;
    }

    // ── Dialogs ──────────────────────────────────────────────────────────

    // Tray-menu "change hotkey" click routes straight into the Settings
    // dialog's Hotkeys section — inline capture lives there. No separate
    // HotkeyDialog window as of v2.1.9.

    private void ShowSettingsDialog()
    {
        if (_settingsDialog != null && !_settingsDialog.IsDisposed)
        {
            _settingsDialog.Show();
            _settingsDialog.BringToFront();
            return;
        }

        _settingsDialog = new SettingsDialog(_config, OnSettingsApplied);
        _settingsDialog.FormClosed += (_, _) => _settingsDialog = null;
        _settingsDialog.Show();
    }

    private void OnSettingsApplied()
    {
        // Any Apply path that touches hotkey config invalidates sticky PTT —
        // RegisterMainHotkey below will restart polling from a clean state.
        ClearStickyPttOverride();

        // Load new icons first, then swap and dispose old ones
        var oldActive = _iconActive;
        var oldMuted = _iconMuted;

        LoadIcons();

        // Update tray icon to reference the new icons before disposing old ones
        SetTrayIcon();

        // Now safe to dispose old icons
        oldActive?.Dispose();
        oldMuted?.Dispose();

        // Defensive re-mute if the user was holding PTT during Apply — the
        // new hotkey registration has no release event waiting for us, so
        // guarantee the mic isn't hot across the rebind.
        if (_config.Mode == "push-to-talk" && _audio.HasEndpoint && !_muted)
        {
            if (!SetMuteState(true, true))
                Log.Warn("Settings-apply safety re-mute failed — mic may stay hot across rebind");
        }
        RegisterMainHotkey();
        RegisterDeafenHotkey();

        // Refresh tray
        _tooltipDirty = true;
        BuildTrayMenu();
        SetTrayIcon();
    }

    // ── Tray Click Handling ──────────────────────────────────────────────

    private void OnTrayMouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            HandleUserToggleClick();
        }
        else if (e.Button == MouseButtons.Middle && _config.MiddleClickToggle)
        {
            SetMode(_config.Mode == "toggle" ? "push-to-talk" : "toggle");
        }
    }

    /// <summary>
    /// Unified entry point for left-click on the tray icon AND the
    /// "Toggle Mute" menu title item. In toggle mode it's a plain flip;
    /// in PTT mode it toggles the sticky override so the user can hold
    /// the mic open without holding the hotkey.
    /// </summary>
    private void HandleUserToggleClick()
    {
        if (_config.Mode == "push-to-talk")
            ToggleStickyPttOverride();
        else
            ToggleMute();
    }

    /// <summary>
    /// Sticky PTT: in PTT mode, left-clicking the tray unmutes the mic and
    /// pauses the polling hotkey until the next left-click. A persistent OSD
    /// stays up the whole time so the user can't forget the mic is live.
    /// Exiting the mode (or the app) clears the override and re-mutes.
    /// </summary>
    private void ToggleStickyPttOverride()
    {
        if (!_audio.HasEndpoint)
        {
            ShowTimedTooltip("No microphone available.\nTry Tray \u2192 Reinitialise Mic.", 5000);
            return;
        }

        if (_pttStickyUnmuted)
        {
            // Exit sticky — re-mute and re-arm polling so PTT works again.
            _pttStickyUnmuted = false;
            _osdForm.HidePersistent();
            if (!SetMuteState(true, true))
                Log.Warn("Sticky-PTT exit re-mute failed — mic may still be hot");
            RegisterMainHotkey();
            ShowTimedTooltip("Push-to-Talk \u2014 hold your hotkey to talk.", 2000);
        }
        else
        {
            // Enter sticky — stop polling first so a PTT release edge while
            // the user is walking their hand to the mouse doesn't race us
            // back to muted. The stop path's own safety re-mute only fires
            // if the PTT key is currently down, which it won't be here.
            StopUniversalPttPoll();
            if (!SetMuteState(false, true))
            {
                // Unmute failed — don't enter sticky state, and restart
                // polling so the user isn't stranded without a hotkey.
                Log.Error("Sticky-PTT unmute failed — aborting, re-arming hotkey");
                RegisterMainHotkey();
                ShowTimedTooltip("Couldn't unmute microphone.\nTry Tray \u2192 Reinitialise Mic.", 4000);
                return;
            }
            _pttStickyUnmuted = true;
            _osdForm.ShowPersistent("PTT \u2014 mic listening", isMuted: false);
        }
    }

    /// <summary>
    /// Clear any active sticky-PTT state. Called from SetMode (mode change),
    /// ExitApplication, and OnSettingsApplied when a setting change would
    /// invalidate the sticky pretense (hotkey rebind, LL-PTT toggle). Does
    /// NOT re-mute — caller owns the follow-on mic state.
    /// </summary>
    private void ClearStickyPttOverride()
    {
        if (!_pttStickyUnmuted) return;
        _pttStickyUnmuted = false;
        _osdForm.HidePersistent();
    }

    // ── Timed Notification ────────────────────────────────────────────────

    private void ShowTimedTooltip(string text, int durationMs)
    {
        // Show notification with actual text in the OSD. Duration is
        // whatever the caller asked for — important messages (hotkey
        // conflict, mic disconnected, startup errors) need longer than
        // the 3s cap that used to be here.
        _osdForm.ShowNotification(text, _muted, durationMs);
    }

    // ── Exit & Cleanup ───────────────────────────────────────────────────

    private void ExitApplication()
    {
        // Claim ownership of exit-time mute state before Dispose chain runs.
        // UnregisterMainHotkey skips its PTT re-mute safety net when this is set.
        _shuttingDown = true;

        // Hide the sticky-PTT persistent OSD so it doesn't linger visually
        // past Application.Exit while Dispose tears the form down.
        ClearStickyPttOverride();

        // Unmute on exit (F-10) + restore speakers if deafened (F-20)
        if (_deafened)
        {
            try { AudioManager.SetSpeakerMute(_speakerWasMuted); }
            catch (Exception ex) { Log.Warn("SetSpeakerMute restore on exit failed: " + ex.Message); }
        }

        if (_audio.HasEndpoint && _muted)
            _audio.SetMute(false);

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Belt-and-suspenders: if something disposes us without going
            // through ExitApplication, the shutdown guard still applies.
            _shuttingDown = true;

            // Unregister hotkeys
            UnregisterMainHotkey();
            UnregisterDeafenHotkey();

            // Stop and dispose timers
            _syncTimer.Stop();
            _syncTimer.Dispose();

            _flashTimer.Stop();
            _flashTimer.Dispose();

            if (_pttUniversalTimer != null)
            {
                _pttUniversalTimer.Stop();
                _pttUniversalTimer.Dispose();
                _pttUniversalTimer = null;
            }

            // Dispose OSD
            _osdForm.Dispose();

            // Dispose tray icon (set invisible first)
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            // Dispose menu items then menu
            DisposeMenuItems();
            _trayMenu.Dispose();
            _menuTitleFont?.Dispose();

            // Dispose icons
            DisposeIcons();

            // Dispose audio
            _audio.Dispose();

            // Dispose dialogs
            _settingsDialog?.Dispose();

            _disposed = true;
        }

        base.Dispose(disposing);
    }
}
