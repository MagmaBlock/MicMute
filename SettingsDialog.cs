namespace MicMute;

/// <summary>
/// Settings GUI matching the AHK version's layout and functionality.
/// </summary>
internal sealed class SettingsDialog : Form
{
    private readonly Config _config;
    private readonly Action _onApply;
    private readonly Font _dialogFont;
    private readonly List<Font> _sectionFonts = new();

    // Behavior
    private readonly CheckBox _chkSoundFeedback;
    private readonly CheckBox _chkOsd;
    private readonly NumericUpDown _edtOsdDuration;
    private readonly CheckBox _chkMuteLock;
    private readonly CheckBox _chkMiddleClick;
    private readonly CheckBox _chkRunAtStartup;
    private readonly ComboBox _ddlStartMuted;

    // Hotkeys — captured values update via the compact-row helper.
    private string _capturedMainHK = "";
    private string _capturedDeafenHK = "";

    // When a hotkey row is in capture mode, these hold callbacks that
    // ProcessCmdKey below invokes instead of letting Esc/Enter trigger
    // CancelButton/AcceptButton. Null when no row is capturing.
    private Action _capturingCancel;
    private Action _capturingCommit;

    // Custom files
    private readonly TextBox _edtIconMuted;
    private readonly TextBox _lblIconMuted;
    private readonly TextBox _edtIconActive;
    private readonly TextBox _lblIconActive;
    private readonly TextBox _edtMuteSound;
    private readonly TextBox _lblMuteSound;
    private readonly TextBox _edtUnmuteSound;
    private readonly TextBox _lblUnmuteSound;
    private ToolTip _fileRowTooltip;

    public SettingsDialog(Config config, Action onApply)
    {
        _config = config;
        _onApply = onApply;

        Text = "MicMute v" + Config.Version + " \u2014 Settings";
        TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        _dialogFont = new Font("Segoe UI", 9f);
        Font = _dialogFont;
        AutoScaleMode = AutoScaleMode.Dpi;

        int y = 14;
        int leftMargin = 16;
        int indent = 28;

        // ── Hotkeys (compact 2-column grid) ──
        // Same 2-column rhythm as the Custom Files section below: Toggle Mute
        // left, Deafen Mute right. Unified interaction — both use the […]
        // button to open HotkeyDialog (modal capture). Deafen gets a × clear
        // button since its default is "unbound"; Toggle doesn't (a main
        // hotkey is always expected).
        AddSectionHeader("Hotkeys", leftMargin, ref y);

        const int hkSectionLeft = 16;
        const int hkSectionRight = 504;
        const int hkColGap = 12;
        int hkCellW = (hkSectionRight - hkSectionLeft - hkColGap) / 2;
        int hkCol1X = hkSectionLeft;
        int hkCol2X = hkSectionLeft + hkCellW + hkColGap;

        _capturedMainHK = config.Hotkey;
        _capturedDeafenHK = config.DeafenHotkey;

        AddCompactHotkeyRow(
            "Toggle Mute",
            () => _capturedMainHK,
            v => _capturedMainHK = v,
            bareKeysAllowed: () => _config.Mode == "push-to-talk",
            hkCol1X, y, hkCellW);

        AddCompactHotkeyRow(
            "Deafen Mute",
            () => _capturedDeafenHK,
            v => _capturedDeafenHK = v,
            bareKeysAllowed: () => false,
            hkCol2X, y, hkCellW);

        y += 28;

        // Per-cell hints stacked vertically beneath the two-column row.
        // Both span the full section width — cell-width clipping would
        // have forced too-terse phrasing.
        var toggleHint = new Label
        {
            Text = "Toggle Mute: mutes / unmutes your mic. In Push-to-Talk mode, hold to talk.",
            AutoSize = true,
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            Location = new Point(indent, y),
        };
        Controls.Add(toggleHint);
        y += toggleHint.Height + 2;

        var deafenHint = new Label
        {
            Text = "Deafen: mutes your mic AND your speakers at the same time.",
            AutoSize = true,
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            Location = new Point(indent, y),
        };
        Controls.Add(deafenHint);
        y += deafenHint.Height + 10;

        // ── Behavior ──
        AddSectionHeader("Behavior", leftMargin, ref y);

        _chkSoundFeedback = AddCheckBox("Sound feedback on mute/unmute", indent, ref y, config.SoundFeedback);
        // OSD row: checkbox (left) + "Duration (ms):" label + textbox (right,
        // anchored to the section edge like the startup row below). Single
        // baseline shared by all three controls.
        int osdRowY = y;
        const int osdSectionRight = 504;
        const int osdDurWidth = 55;

        _chkOsd = new CheckBox
        {
            Text = "On-screen display bubble on mute/unmute",
            AutoSize = true,
            Checked = config.OsdEnabled,
            Location = new Point(indent, osdRowY),
        };
        Controls.Add(_chkOsd);

        _edtOsdDuration = new NumericUpDown
        {
            Minimum = 500,
            Maximum = 10000,
            Increment = 100,
            Value = Math.Clamp(config.OsdDuration, 500, 10000),
            Width = osdDurWidth,
            Location = new Point(osdSectionRight - osdDurWidth, osdRowY - 1),
            TextAlign = HorizontalAlignment.Left,
        };
        Controls.Add(_edtOsdDuration);

        var durLabel = new Label
        {
            Text = "Duration (ms):",
            AutoSize = true,
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            Location = new Point(0, osdRowY + 3),
        };
        Controls.Add(durLabel);
        durLabel.Left = _edtOsdDuration.Left - durLabel.PreferredWidth - 6;

        y += 28;

        // Mute Lock row: checkbox label + inline grey hint spelling out the
        // 15-second caveat so the user isn't surprised when Discord/Zoom
        // briefly wins a mute fight.
        _chkMuteLock = new CheckBox
        {
            Text = "Mute Lock",
            AutoSize = true,
            Checked = config.MuteLock,
            Location = new Point(indent, y),
        };
        Controls.Add(_chkMuteLock);
        var muteLockHint = new Label
        {
            Text = "\u2014  reverts external mute changes every 15 seconds (not instant).",
            AutoSize = true,
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            Location = new Point(_chkMuteLock.Right + 4, y + 1),
        };
        Controls.Add(muteLockHint);
        y += _chkMuteLock.Height + 4;
        _chkMiddleClick = AddCheckBox("Middle-click tray icon to toggle Toggle/PTT mode", indent, ref y, config.MiddleClickToggle);
        string startupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MicMute.lnk");

        // Startup row: "Run at startup" checkbox (left) paired with the
        // "On startup:" dropdown (right, anchored to the section edge) on a
        // single baseline. Checkbox and combo share rowY; the label sits
        // +3px lower so text optically centers against both controls.
        int rowY = y;
        const int sectionRight = 504;
        const int ddlStartupWidth = 130;

        _chkRunAtStartup = new CheckBox
        {
            Text = "Run at startup",
            AutoSize = true,
            Checked = File.Exists(startupPath),
            Location = new Point(indent, rowY),
        };
        Controls.Add(_chkRunAtStartup);

        _ddlStartMuted = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = ddlStartupWidth,
            Location = new Point(sectionRight - ddlStartupWidth, rowY - 1),
        };
        _ddlStartMuted.Items.AddRange(new[] { "Don't change", "Always muted", "Always unmuted", "Remember last" });
        _ddlStartMuted.SelectedIndex = config.StartMuted switch
        {
            "yes" => 1,
            "unmuted" => 2,
            "last" => 3,
            _ => 0,
        };
        Controls.Add(_ddlStartMuted);

        var startLabel = new Label
        {
            Text = "On startup:",
            AutoSize = true,
            Location = new Point(0, rowY + 3),
        };
        Controls.Add(startLabel);
        startLabel.Left = _ddlStartMuted.Left - startLabel.PreferredWidth - 6;

        y += 28;

        // Inform the user that PTT mode overrides this preference. The
        // startup mute-preference code force-mutes under PTT regardless of
        // the dropdown, so showing "Don't change" while PTT always mutes
        // would be a small lie. Only render when currently in PTT mode.
        if (config.Mode == "push-to-talk")
        {
            // Right-align the hint under the "On startup:" dropdown so it
            // reads as a caption on that control, not a general-section note.
            var pttHintLabel = new Label
            {
                Text = "Push-to-Talk mode always starts muted.",
                AutoSize = true,
                ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
                Location = new Point(0, y),
            };
            Controls.Add(pttHintLabel);
            pttHintLabel.Left = sectionRight - pttHintLabel.PreferredWidth;
            y += 20;
        }
        else
        {
            y += 6;
        }

        // ── Custom Files (compact 2×2 grid) ──
        // Advanced / rarely-used customization section — shrunk to a tight
        // two-row grid so the dialog isn't dominated by four empty "(none)"
        // fields. Each cell: label + click-to-browse display + tiny × clear.
        AddSectionHeader("Custom Files", leftMargin, ref y);

        const int sectionInnerLeft = 16;
        const int sectionInnerRight = 504;
        const int colGap = 12;
        int cellW = (sectionInnerRight - sectionInnerLeft - colGap) / 2;
        int col1X = sectionInnerLeft;
        int col2X = sectionInnerLeft + cellW + colGap;

        (_edtIconMuted,   _lblIconMuted)   = AddCompactFileRow("Muted icon",   config.IconMuted,   "Icon files (*.ico)|*.ico",   col1X, y, cellW);
        (_edtIconActive,  _lblIconActive)  = AddCompactFileRow("Active icon",  config.IconActive,  "Icon files (*.ico)|*.ico",   col2X, y, cellW);
        y += 26;
        (_edtMuteSound,   _lblMuteSound)   = AddCompactFileRow("Mute sound",   config.MuteSound,   "Sound files (*.wav)|*.wav",  col1X, y, cellW);
        (_edtUnmuteSound, _lblUnmuteSound) = AddCompactFileRow("Unmute sound", config.UnmuteSound, "Sound files (*.wav)|*.wav",  col2X, y, cellW);
        y += 26;

        // ── Buttons ──
        y += 12;
        const int dialogWidth = 520;
        int rightEdge = dialogWidth - leftMargin;

        // Auxiliary links (left) — subtle, navigation-style
        var lnkGitHub = new LinkLabel
        {
            Text = "GitHub",
            AutoSize = true,
            Location = new Point(leftMargin, y + 6),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        lnkGitHub.LinkClicked += (_, _) =>
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/itsnateai/MicMute",
                UseShellExecute = true,
            });
        };
        Controls.Add(lnkGitHub);

        var lnkHelp = new LinkLabel
        {
            Text = "Help",
            AutoSize = true,
            Location = new Point(lnkGitHub.Right + 14, y + 6),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        lnkHelp.LinkClicked += (_, _) => HelpWindow.ShowInstance();
        Controls.Add(lnkHelp);

        var lnkUpdate = new LinkLabel
        {
            Text = "Check for updates",
            AutoSize = true,
            Location = new Point(lnkHelp.Right + 14, y + 6),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        lnkUpdate.LinkClicked += (_, _) =>
        {
            using var dlg = new UpdateDialog();
            dlg.ShowDialog(this);
        };
        Controls.Add(lnkUpdate);

        // Action buttons (right) — anchored to the right edge via the shared
        // UiFactory so they match Hotkey dialog + any future dialog pixel-perfect.
        // Widths auto-shrink if the left link-label group runs wide (long locales,
        // high DPI) so left and right groups never overlap.
        const int btnMinWidth = 64;
        const int groupGap = 16;
        int leftGroupRight = lnkUpdate.Right;
        int rightGroupAvailable = rightEdge - leftGroupRight - groupGap;
        int btnWidth = Math.Min(UiTokens.BtnActionWidth,
            Math.Max(btnMinWidth, (rightGroupAvailable - 2 * UiTokens.BtnGap) / 3));

        var btnCancel = UiFactory.MakeButton("Cancel", btnWidth, rightEdge - btnWidth, y);
        btnCancel.Click += (_, _) => Close();
        Controls.Add(btnCancel);
        CancelButton = btnCancel;

        var btnApply = UiFactory.MakeButton("Apply", btnWidth,
            btnCancel.Left - UiTokens.BtnGap - btnWidth, y);
        btnApply.Click += (_, _) => ApplySettings();
        Controls.Add(btnApply);

        var btnOK = UiFactory.MakeButton("Save", btnWidth,
            btnApply.Left - UiTokens.BtnGap - btnWidth, y);
        btnOK.Click += (_, _) => { ApplySettings(); Close(); };
        Controls.Add(btnOK);
        AcceptButton = btnOK;

        ClientSize = new Size(dialogWidth, y + 38);
    }

    private void ApplySettings()
    {
        _config.SoundFeedback = _chkSoundFeedback.Checked;
        _config.OsdEnabled = _chkOsd.Checked;

        // NumericUpDown clamps + validates inherently — no need for the
        // earlier TextBox parse-and-tint dance.
        _config.OsdDuration = (int)_edtOsdDuration.Value;
        _config.MuteLock = _chkMuteLock.Checked;
        _config.MiddleClickToggle = _chkMiddleClick.Checked;

        _config.StartMuted = _ddlStartMuted.SelectedIndex switch
        {
            1 => "yes",
            2 => "unmuted",
            3 => "last",
            _ => "no",
        };

        // Startup shortcut
        string startupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MicMute.lnk");
        if (_chkRunAtStartup.Checked && !File.Exists(startupPath))
            ShortcutHelper.CreateShortcut(startupPath, Environment.ProcessPath ?? "");
        else if (!_chkRunAtStartup.Checked && File.Exists(startupPath))
            try { File.Delete(startupPath); } catch (Exception ex) { Log.Warn("Delete startup shortcut failed: " + ex.Message); }

        // Hotkeys — captured inline in the Hotkeys section. Validate here
        // before committing so the user sees feedback before Apply closes.
        if (!ValidateHotkeysBeforeApply())
            return;
        _config.Hotkey = _capturedMainHK;
        _config.DeafenHotkey = _capturedDeafenHK;

        // Custom files
        _config.IconMuted = _edtIconMuted.Text.Trim();
        _config.IconActive = _edtIconActive.Text.Trim();
        _config.MuteSound = _edtMuteSound.Text.Trim();
        _config.UnmuteSound = _edtUnmuteSound.Text.Trim();

        bool saved = _config.Save();
        _onApply();
        if (!saved)
        {
            MessageBox.Show(this,
                "Settings were applied to the current session, but couldn't be written to MicMute.ini. " +
                "Your changes will be lost on next launch.\n\n" +
                "Check that MicMute has permission to write to its config folder.",
                "MicMute \u2014 Settings not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Run all hotkey validation checks before ApplySettings commits. Returns
    /// false if the user aborted (and Apply should back out).
    /// Checks: parse-ability, duplicate between Toggle+Deafen, RegisterHotKey
    /// conflict with other apps (best-effort), risky bindings in PTT mode.
    /// </summary>
    private bool ValidateHotkeysBeforeApply()
    {
        bool pttMode = _config.Mode == "push-to-talk";

        // Parse check — reject invalid combos up front rather than letting
        // RegisterMainHotkey/RegisterDeafenHotkey silently fail later.
        if (!string.IsNullOrEmpty(_capturedMainHK) &&
            !Config.ParseHotkey(_capturedMainHK, out uint mMods, out uint mVk, allowBare: pttMode))
        {
            MessageBox.Show(this,
                "Toggle Mute hotkey \"" + Config.HotkeyToReadable(_capturedMainHK) + "\" isn\u2019t a valid binding.\n\n" +
                (pttMode
                    ? "In Push-to-Talk mode, bare keys are allowed only if they\u2019re modifiers (LCtrl, RShift, etc.) or function keys."
                    : "Bare keys aren\u2019t allowed in Toggle mode \u2014 add at least one of Ctrl, Alt, Shift, or Win, or switch to Push-to-Talk."),
                "MicMute \u2014 Invalid hotkey",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (!string.IsNullOrEmpty(_capturedDeafenHK) &&
            !Config.ParseHotkey(_capturedDeafenHK, out uint dMods, out uint dVk, allowBare: false))
        {
            MessageBox.Show(this,
                "Deafen Mute hotkey \"" + Config.HotkeyToReadable(_capturedDeafenHK) + "\" isn\u2019t a valid binding.\n\n" +
                "Deafen uses Windows\u2019 global hotkey system, which needs at least one of Ctrl, Alt, Shift, or Win.",
                "MicMute \u2014 Invalid hotkey",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Duplicate check — same combo on both hotkeys silently fights at
        // registration time (Windows grants it to whichever binds first).
        if (!string.IsNullOrEmpty(_capturedMainHK) &&
            !string.IsNullOrEmpty(_capturedDeafenHK) &&
            string.Equals(_capturedMainHK, _capturedDeafenHK, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Toggle Mute and Deafen are both set to \"" + Config.HotkeyToReadable(_capturedMainHK) + "\".\n\n" +
                "Windows can\u2019t route one key press to two different actions \u2014 pick a different combo for one of them.",
                "MicMute \u2014 Duplicate hotkey",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Best-effort conflict probe — same pattern TrayApp uses. Caught LL-hook
        // users we can't see, so this is a courtesy warning, not a hard block.
        const int PROBE_ID_MAIN = 0x7A1D;
        const int PROBE_ID_DEAFEN = 0x7A1E;
        if (!string.IsNullOrEmpty(_capturedMainHK) &&
            Config.ParseHotkey(_capturedMainHK, out uint probeMMods, out uint probeMVk, allowBare: pttMode))
        {
            bool ok = NativeMethods.RegisterHotKey(Handle, PROBE_ID_MAIN, probeMMods, probeMVk);
            if (ok) NativeMethods.UnregisterHotKey(Handle, PROBE_ID_MAIN);
            if (!ok)
            {
                var res = MessageBox.Show(this,
                    "Toggle Mute hotkey \"" + Config.HotkeyToReadable(_capturedMainHK) + "\" looks like it\u2019s already claimed by another app.\n\n" +
                    "MicMute may lose the race, or both apps may fire at once. Use it anyway?",
                    "MicMute \u2014 Hotkey conflict",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (res != DialogResult.Yes) return false;
            }
        }
        if (!string.IsNullOrEmpty(_capturedDeafenHK) &&
            Config.ParseHotkey(_capturedDeafenHK, out uint probeDMods, out uint probeDVk, allowBare: false))
        {
            bool ok = NativeMethods.RegisterHotKey(Handle, PROBE_ID_DEAFEN, probeDMods, probeDVk);
            if (ok) NativeMethods.UnregisterHotKey(Handle, PROBE_ID_DEAFEN);
            if (!ok)
            {
                var res = MessageBox.Show(this,
                    "Deafen Mute hotkey \"" + Config.HotkeyToReadable(_capturedDeafenHK) + "\" looks like it\u2019s already claimed by another app.\n\n" +
                    "Use it anyway?",
                    "MicMute \u2014 Hotkey conflict",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (res != DialogResult.Yes) return false;
            }
        }

        // Risky-key check — only for the Toggle hotkey in PTT mode (bare letters,
        // Space, common Ctrl-shortcuts that fire during normal typing).
        if (pttMode && !string.IsNullOrEmpty(_capturedMainHK) &&
            Config.ParseHotkey(_capturedMainHK, out uint riskMods, out uint riskVk, allowBare: true) &&
            Config.IsRiskyHotkey(riskMods, riskVk))
        {
            var res = MessageBox.Show(this,
                "\"" + Config.HotkeyToReadable(_capturedMainHK) + "\" is a key you\u2019ll press during normal use.\n\n" +
                "In Push-to-Talk mode, your mic will open every time you press it \u2014 in every app, not just voice chat. Use it anyway?",
                "MicMute \u2014 Risky PTT key",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (res != DialogResult.Yes) return false;
        }

        return true;
    }

    private void AddSectionHeader(string text, int x, ref int y)
    {
        var boldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        _sectionFonts.Add(boldFont);
        var label = new Label
        {
            Text = text,
            Font = boldFont,
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
            AutoSize = true,
            Location = new Point(x, y),
        };
        Controls.Add(label);
        y += label.Height + 3;

        // Separator line — extends to near the right margin so the section
        // header visually spans the dialog's content width (was 410, leaving
        // ~70px of dead space on the right).
        var sep = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Height = 1,
            Width = 488,
            Location = new Point(x, y),
        };
        Controls.Add(sep);
        y += 8;
    }

    private CheckBox AddCheckBox(string text, int x, ref int y, bool isChecked)
    {
        var chk = new CheckBox
        {
            Text = text,
            AutoSize = true,
            Checked = isChecked,
            Location = new Point(x, y),
        };
        Controls.Add(chk);
        y += chk.Height + 4;
        return chk;
    }

    /// <summary>
    /// Compact cell for the Hotkeys 2-column grid. Cell layout:
    ///   [label 78px] [display fills] [⋯ 28px] [× 20px — only if allowClear]
    /// The [⋯] button opens HotkeyDialog (modal). The × button clears.
    /// State lives outside the helper — getCaptured/setCaptured wire into the
    /// dialog's _capturedMainHK / _capturedDeafenHK fields so ApplySettings
    /// still has one source of truth.
    /// </summary>
    /// <summary>
    /// Compact cell for the Hotkeys 2-column grid. Inline capture — clicking
    /// the display box enters "recording" mode (yellow background), pressing
    /// a key/combo captures in place, Escape cancels, clicking elsewhere or
    /// Tab commits. Matches the Custom Files section's `[label][box][×]`
    /// rhythm — no modal pop-up, no extra button.
    /// </summary>
    private void AddCompactHotkeyRow(
        string labelText,
        Func<string> getCaptured,
        Action<string> setCaptured,
        Func<bool> bareKeysAllowed,
        int x, int y, int cellWidth)
    {
        // Layout: [label 76][gap4][display flex][gap4][× 22] — identical to
        // Custom Files for visual consistency across sections.
        const int labelWidth = 76;
        const int btnWidth = UiTokens.BtnIconWidth;
        const int gap = UiTokens.RowGap;

        int labelX = x;
        int clearX = x + cellWidth - btnWidth;
        int dispX = labelX + labelWidth + gap;
        int dispW = clearX - dispX - gap;

        var lbl = UiFactory.MakeFieldLabel(labelText, labelX, y + 4, labelWidth);
        Controls.Add(lbl);

        var display = new TextBox
        {
            ReadOnly = true,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            Width = dispW,
            Location = new Point(dispX, y - 1),
        };
        Controls.Add(display);

        var btnClear = UiFactory.MakeIconButton("\u00D7", clearX, y - 1);
        Controls.Add(btnClear);

        _fileRowTooltip ??= new ToolTip();
        _fileRowTooltip.SetToolTip(btnClear, "Clear hotkey");
        _fileRowTooltip.SetToolTip(display, "Click and press a key combo to record");

        bool captureMode = false;
        string preCaptureValue = null;

        void Refresh()
        {
            string v = getCaptured();
            bool empty = string.IsNullOrEmpty(v);
            string readable = empty ? "(not set)" : Config.HotkeyToReadable(v);
            display.Text = readable;
            display.ForeColor = empty
                ? UiTokens.GreyTextColor
                : (captureMode ? Color.Black : Color.Black);
            display.BackColor = captureMode ? UiTokens.FocusYellow : Color.White;
            btnClear.Enabled = !empty;
        }
        Refresh();

        void EnterCapture()
        {
            if (captureMode) return;
            captureMode = true;
            preCaptureValue = getCaptured();
            // Register form-level Esc/Enter interception — otherwise Esc
            // fires CancelButton (closes Settings) and Enter fires OK.
            _capturingCancel = CancelCapture;
            _capturingCommit = () => { ActiveControl = null; };
            Refresh();
        }

        void ExitCapture()
        {
            if (!captureMode) return;
            captureMode = false;
            preCaptureValue = null;
            _capturingCancel = null;
            _capturingCommit = null;
            Refresh();
        }

        void CancelCapture()
        {
            if (!captureMode) return;
            if (preCaptureValue != null) setCaptured(preCaptureValue);
            captureMode = false;
            preCaptureValue = null;
            _capturingCancel = null;
            _capturingCommit = null;
            Refresh();
            ActiveControl = null; // blur off the field
        }

        display.Click += (_, _) => EnterCapture();
        display.Enter += (_, _) => EnterCapture();
        display.Leave += (_, _) => ExitCapture();

        display.KeyDown += (s, e) =>
        {
            e.SuppressKeyPress = true;
            if (!captureMode) return;

            // Escape cancels and restores the pre-capture value.
            if (e.KeyCode == Keys.Escape)
            {
                CancelCapture();
                return;
            }
            // Enter commits and blurs.
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
            {
                ActiveControl = null;
                return;
            }
            // Tab — let WinForms handle focus move; Leave will commit.
            if (e.KeyCode == Keys.Tab) return;

            bool bare = bareKeysAllowed();

            // Bare modifier-only press (RCtrl alone, LShift alone, etc.)
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                if (!bare) return;
                const int VK_RSHIFT = 0xA1, VK_RCONTROL = 0xA3, VK_RMENU = 0xA5;
                string side = null;
                if (e.KeyCode == Keys.ControlKey)
                    side = (NativeMethods.GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0 ? "RCtrl" : "LCtrl";
                else if (e.KeyCode == Keys.ShiftKey)
                    side = (NativeMethods.GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0 ? "RShift" : "LShift";
                else if (e.KeyCode == Keys.Menu)
                    side = (NativeMethods.GetAsyncKeyState(VK_RMENU) & 0x8000) != 0 ? "RAlt" : "LAlt";
                else if (e.KeyCode == Keys.LWin) side = "LWin";
                else if (e.KeyCode == Keys.RWin) side = "RWin";

                if (side != null)
                {
                    setCaptured(side);
                    Refresh();
                }
                return;
            }

            // Regular combo — build AHK prefix from modifiers + key name.
            string prefix = "";
            if (e.Modifiers.HasFlag(Keys.Control)) prefix += "^";
            if (e.Modifiers.HasFlag(Keys.Alt)) prefix += "!";
            if (e.Modifiers.HasFlag(Keys.Shift)) prefix += "+";
            if ((NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0)
                prefix = "#" + prefix;

            string keyName = Config.KeyCodeToName(e.KeyCode);
            if (string.IsNullOrEmpty(keyName)) return;

            setCaptured(prefix + keyName);
            Refresh();
        };

        btnClear.Click += (_, _) =>
        {
            setCaptured("");
            if (captureMode) preCaptureValue = ""; // don't re-populate on Escape
            Refresh();
        };
    }

    /// <summary>
    /// Compact cell for the Custom Files 2×2 grid. Each cell fits in ~227px
    /// horizontal: label (88px) + click-to-browse filename display + tiny ×
    /// clear button (only enabled when a file is set).
    ///
    /// The display textbox is its own "browse" button — ReadOnly, cursor hand,
    /// click opens OpenFileDialog. This halves the section's vertical footprint
    /// vs. the previous "label + textbox + Browse + Clear" four-control row.
    /// </summary>
    private (TextBox edit, TextBox display) AddCompactFileRow(
        string labelText, string currentPath, string filter,
        int x, int y, int cellWidth)
    {
        const int labelWidth = 88;
        const int clearWidth = 22;
        const int gap = 4;

        int dispX = x + labelWidth + gap;
        int clearX = x + cellWidth - clearWidth;
        int dispW = clearX - dispX - gap;
        bool hasFile = !string.IsNullOrEmpty(currentPath);

        var lbl = new Label
        {
            Text = labelText,
            Width = labelWidth,
            AutoSize = false,
            Location = new Point(x, y + 4),
            ForeColor = Color.FromArgb(0x44, 0x44, 0x44),
        };
        Controls.Add(lbl);

        var fileDisplay = new TextBox
        {
            Text = FileLabel(currentPath),
            ReadOnly = true,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            ForeColor = hasFile ? Color.Black : Color.FromArgb(0x99, 0x99, 0x99),
            Width = dispW,
            Location = new Point(dispX, y - 1),
        };
        Controls.Add(fileDisplay);

        // Hidden field: stores the full path (display shows only the filename).
        var edit = new TextBox { Text = currentPath, Visible = false, Width = 0, Location = new Point(0, 0) };
        Controls.Add(edit);

        // Clear button — only meaningful when a file is set. Single glyph to
        // keep the column tight; tooltip carries the meaning for screen readers.
        // Routed through UiFactory so it matches the Hotkeys section's × pixel-
        // for-pixel (both sit inline with a TextBox, both use BtnIconHeight=23).
        var btnClear = UiFactory.MakeIconButton("\u00D7", clearX, y - 1);
        btnClear.Enabled = hasFile;
        _fileRowTooltip ??= new ToolTip();
        _fileRowTooltip.SetToolTip(btnClear, "Reset to default");
        _fileRowTooltip.SetToolTip(fileDisplay, "Click to choose a custom file");
        Controls.Add(btnClear);

        void RunBrowse()
        {
            using var ofd = new OpenFileDialog { Filter = filter };
            if (ofd.ShowDialog(this) != DialogResult.OK) return;

            // Validate before accepting. Hard failures block; soft warnings
            // let the user override with OK.
            var (hardFail, message) = ValidateCustomFile(ofd.FileName, filter);
            if (message != null)
            {
                if (hardFail)
                {
                    MessageBox.Show(this, message,
                        "MicMute \u2014 Can\u2019t use this file",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var res = MessageBox.Show(this, message + "\n\nUse it anyway?",
                    "MicMute \u2014 Large file",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (res != DialogResult.OK) return;
            }

            edit.Text = ofd.FileName;
            fileDisplay.Text = FileLabel(ofd.FileName);
            fileDisplay.ForeColor = Color.Black;
            btnClear.Enabled = true;
        }

        fileDisplay.Click += (_, _) => RunBrowse();
        // Keyboard support — ReadOnly textboxes still fire KeyDown; Enter/Space
        // opens browse so the row is usable without a mouse.
        fileDisplay.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                e.SuppressKeyPress = true;
                RunBrowse();
            }
        };

        btnClear.Click += (_, _) =>
        {
            edit.Text = "";
            fileDisplay.Text = FileLabel("");
            fileDisplay.ForeColor = Color.FromArgb(0x99, 0x99, 0x99);
            btnClear.Enabled = false;
        };

        return (edit, fileDisplay);
    }

    private static string FileLabel(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "default";
        return Path.GetFileName(path);
    }

    /// <summary>
    /// Validate a user-picked custom file before we commit it to config.
    /// Returns (true, message) for hard failures that should block selection,
    /// (false, message) for soft warnings the user can override, or
    /// (false, null) if everything looks fine.
    ///
    /// Soft caps are deliberately generous — this is about catching obvious
    /// mistakes (wrong file type, 200 MB WAV) without nagging normal use.
    /// </summary>
    private static (bool hardFail, string message) ValidateCustomFile(string path, string filter)
    {
        const long IconSoftCapBytes  = 2L * 1024 * 1024;   // 2 MB
        const long WavSoftCapBytes   = 10L * 1024 * 1024;  // 10 MB

        bool isIcon = filter.Contains(".ico", StringComparison.OrdinalIgnoreCase);
        bool isWav  = filter.Contains(".wav", StringComparison.OrdinalIgnoreCase);

        FileInfo info;
        try { info = new FileInfo(path); }
        catch (Exception ex) { return (true, "Couldn\u2019t read this file: " + ex.Message); }

        if (!info.Exists)
            return (true, "That file no longer exists. Pick another.");

        if (isIcon)
        {
            // Hard check — try to load as a Windows icon. Handles bogus
            // extensions (file renamed .ico but actually .png) and truncated
            // .ico files with bad image-directory entries.
            try
            {
                using var test = new Icon(path);
                _ = test.Width;
            }
            catch (Exception ex)
            {
                return (true,
                    "This file isn\u2019t a valid Windows icon (.ico).\n\n" +
                    "MicMute needs a real multi-size Windows icon. Try converting " +
                    "your image with a tool like GIMP or an online .ico converter.\n\n" +
                    "Details: " + ex.Message);
            }

            if (info.Length > IconSoftCapBytes)
            {
                return (false,
                    $"This icon is {info.Length / 1024:N0} KB \u2014 larger than the 2 MB soft cap.\n\n" +
                    "Tray icons get rasterized down to the taskbar size anyway, so " +
                    "anything over a couple hundred KB is wasted memory.");
            }
            return (false, null);
        }

        if (isWav)
        {
            // Hard check — parse the RIFF/WAVE header + fmt chunk format code.
            // System.Media.SoundPlayer only handles PCM (1) or IEEE float (3);
            // anything else (MP3, ADPCM, µ-law) plays as silence with no error.
            try
            {
                using var fs = File.OpenRead(path);
                Span<byte> header = stackalloc byte[12];
                if (fs.Read(header) < 12 ||
                    header[0] != 'R' || header[1] != 'I' || header[2] != 'F' || header[3] != 'F' ||
                    header[8] != 'W' || header[9] != 'A' || header[10] != 'V' || header[11] != 'E')
                {
                    return (true,
                        "This file isn\u2019t a standard WAV (missing RIFF/WAVE header).\n\n" +
                        "MicMute uses Windows' built-in sound player, which only handles " +
                        "uncompressed PCM WAV files. Convert your audio to 16-bit PCM WAV " +
                        "(e.g. via Audacity \u2192 Export \u2192 WAV).");
                }

                // Walk chunks looking for 'fmt '. Bail if the file is malformed.
                Span<byte> chunk = stackalloc byte[8];
                Span<byte> fmtBuf = stackalloc byte[2];
                int guard = 0;
                while (fs.Read(chunk) == 8 && guard++ < 16)
                {
                    string id = System.Text.Encoding.ASCII.GetString(chunk.Slice(0, 4));
                    int chunkSize = BitConverter.ToInt32(chunk.Slice(4, 4));
                    if (id == "fmt ")
                    {
                        if (fs.Read(fmtBuf) != 2)
                            return (true, "WAV fmt chunk is truncated \u2014 file may be corrupt.");
                        int formatCode = BitConverter.ToUInt16(fmtBuf);
                        if (formatCode != 1 && formatCode != 3)
                        {
                            string fmtName = formatCode switch
                            {
                                2   => "Microsoft ADPCM",
                                6   => "A-law",
                                7   => "\u00B5-law",
                                17  => "IMA ADPCM",
                                85  => "MP3-in-WAV",
                                _   => $"format code {formatCode}",
                            };
                            return (true,
                                $"This WAV uses {fmtName} compression, which Windows\u2019 " +
                                "built-in sound player can\u2019t decode.\n\n" +
                                "Re-export as 16-bit PCM WAV (Audacity: Export \u2192 WAV \u2192 " +
                                "WAV signed 16-bit PCM).");
                        }
                        goto FormatOk;
                    }
                    if (chunkSize <= 0 || chunkSize > 500_000_000)
                        return (true, "WAV has an implausible chunk size \u2014 file may be corrupt.");
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }
                return (true, "Couldn\u2019t find the WAV fmt chunk \u2014 file may be corrupt.");

                FormatOk:;
            }
            catch (Exception ex)
            {
                return (true, "Couldn\u2019t read this WAV file: " + ex.Message);
            }

            if (info.Length > WavSoftCapBytes)
            {
                return (false,
                    $"This sound is {info.Length / (1024.0 * 1024.0):F1} MB \u2014 larger than the 10 MB soft cap.\n\n" +
                    "MicMute loads the whole file into memory each time it plays. For " +
                    "a mute/unmute toast, anything over ~500 KB is wildly oversized.");
            }
            return (false, null);
        }

        return (false, null);
    }

    /// <summary>
    /// Intercepts Esc/Enter at the form level when a hotkey row is in
    /// capture mode, so Esc cancels the capture (instead of closing Settings
    /// via CancelButton) and Enter commits + blurs (instead of firing OK).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_capturingCancel != null && keyData == Keys.Escape)
        {
            _capturingCancel();
            return true;
        }
        if (_capturingCommit != null &&
            (keyData == Keys.Enter || keyData == Keys.Return))
        {
            _capturingCommit();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dialogFont?.Dispose();
            foreach (var f in _sectionFonts)
                f.Dispose();
            _sectionFonts.Clear();
            _fileRowTooltip?.Dispose();
        }
        base.Dispose(disposing);
    }
}
