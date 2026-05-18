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

    // Appearance — restart-to-apply theme pin (System / Dark / Light).
    private readonly ComboBox _ddlTheme;

    // Hotkeys — captured values update via the compact-row helper.
    private string _capturedMainHK = "";
    private string _capturedDeafenHK = "";
    // Cached "use it anyway" confirmations — set by ValidateHotkeysBeforeApply,
    // committed to _config by ApplySettings. Prevents re-warning the user on
    // repeated Apply/Save when the hotkey is unchanged.
    private string _pendingAckedMainHk;
    private string _pendingAckedDeafenHk;

    // When a hotkey row is in capture mode, these hold callbacks that
    // ProcessCmdKey below invokes instead of letting Esc/Enter trigger
    // CancelButton/AcceptButton. Null when no row is capturing.
    private Action _capturingCancel;
    private Action _capturingCommit;

    // Custom files — paths held as string fields (updated by closure callbacks
    // from AddCompactFileRow). Display TextBoxes named _lbl* show only the
    // filename. No hidden phantom TextBox controls parented to the form.
    //
    // Mute / Unmute custom sounds are intentionally NOT exposed in the GUI
    // (the Custom Files section is dominated by 4 cells worth of empty
    // "(none)" boxes that almost nobody uses). The Config.MuteSound /
    // UnmuteSound fields still persist via the INI for power users who want
    // to hand-edit MicMute.ini — TrayApp.PlayFeedback still honours them.
    // If we ever bring sound customisation back, restore AddCompactFileRow
    // calls for them in the Custom Files section.
    private string _pathIconMuted  = "";
    private string _pathIconActive = "";
    private readonly TextBox _lblIconMuted;
    private readonly TextBox _lblIconActive;
    private ToolTip _fileRowTooltip;

    // Tracks in-flight reject-animation timers (the 1800ms tints that fire
    // when the user tries to capture a bare modifier outside PTT mode).
    // Without tracking, if the dialog closes during the animation window
    // the WinForms.Timer's native HWND-bound handle is leaked. Dispose()
    // sweeps any survivors below.
    private readonly List<System.Windows.Forms.Timer> _activeRejectTimers = new();

    // RegisterHotKey IDs used by the conflict-probe in ValidateHotkeysBeforeApply.
    // These same IDs are unregistered defensively in Dispose in case the probe
    // throws between Register and Unregister. Keeping the constants at class
    // scope ensures both call-sites stay in lock-step — a method-local const
    // duplicated by bare literals in Dispose drifts the first time someone
    // bumps one site without touching the other.
    private const int PROBE_ID_MAIN = 0x7A1D;
    private const int PROBE_ID_DEAFEN = 0x7A1E;

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
        BackColor = Theme.BgColor;
        ForeColor = Theme.FgColor;
        // Pin design baseline to 96 DPI BEFORE Font assignment and AutoScaleMode
        // so every literal `new Size(...)` / `new Point(...)` is interpreted as
        // 96-DPI design pixels regardless of which monitor first realizes the
        // form. Without the pin, AutoScaleDimensions defaults to the first-
        // realized monitor's DPI; on a 125%/150% display the dialog then
        // double-scales and controls clip. Order matches the SyncthingPause v3.0.1
        // reference impl — pin first, Font + everything else after.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        _dialogFont = new Font(UiTokens.PrimaryFont, UiTokens.DialogFontSize);
        Font = _dialogFont;

        int y = 14;
        int leftMargin = 16;
        int indent = 28;

        // ── Hotkeys (compact 2-column grid) ──
        // Same 2-column rhythm as the Custom Files section below: Toggle Mute
        // left, Deafen Mute right. Unified interaction — both use the […]
        // button to enter inline capture mode in-place. Deafen gets a × clear
        // button since its default is "unbound"; Toggle doesn't (a main
        // hotkey is always expected).
        AddSectionHeader("Hotkeys", leftMargin, ref y);

        const int hkSectionLeft = 16;
        const int hkSectionRight = UiTokens.SettingsSectionRight;
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
        var toggleHint = UiFactory.MakeHintLabel(
            "Toggle Mute: mutes / unmutes your mic. In Push-to-Talk mode, hold to talk.",
            indent, y);
        Controls.Add(toggleHint);
        y += toggleHint.Height + 2;

        var deafenHint = UiFactory.MakeHintLabel(
            "Deafen: mutes your mic AND your speakers at the same time.",
            indent, y);
        Controls.Add(deafenHint);
        y += deafenHint.Height + 10;

        // ── Behavior ──
        AddSectionHeader("Behavior", leftMargin, ref y);

        _chkSoundFeedback = AddCheckBox("Sound feedback on mute/unmute", indent, ref y, config.SoundFeedback);
        // OSD row: checkbox (left) + "Duration (ms):" label + textbox (right,
        // anchored to the section edge like the startup row below). Single
        // baseline shared by all three controls.
        int osdRowY = y;
        const int osdSectionRight = UiTokens.SettingsSectionRight;
        const int osdDurWidth = 55;

        _chkOsd = new CheckBox
        {
            Text = "On-screen display bubble on mute/unmute",
            AutoSize = true,
            Checked = config.OsdEnabled,
            ForeColor = Theme.CheckboxFgColor,
            BackColor = Theme.BgColor,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(indent, osdRowY),
        };
        _chkOsd.FlatAppearance.BorderColor = Theme.DividerColor;
        _chkOsd.FlatAppearance.CheckedBackColor = Theme.HighlightBg;
        _chkOsd.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        Controls.Add(_chkOsd);

        _edtOsdDuration = new NumericUpDown
        {
            Minimum = 500,
            Maximum = 10000,
            Increment = 100,
            Value = Math.Clamp(config.OsdDuration, 500, 10000),
            Width = osdDurWidth,
            // NumericUpDown composes spinner buttons + text via nested HWNDs
            // whose scaling math diverges by a few px at every non-integer
            // ratio. MinimumSize floors the outer bounds so AutoScale can't
            // shrink the spinner band into the digit area.
            MinimumSize = new Size(osdDurWidth, 26),
            Location = new Point(osdSectionRight - osdDurWidth, osdRowY - 1),
            TextAlign = HorizontalAlignment.Left,
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_edtOsdDuration);
        // NumericUpDown's inner spinner band (Controls[0]) is an internal
        // UpDownButtons HWND that paints its own background via ControlPaint
        // and ignores its parent's BackColor. Without this the digit area is
        // dark but the up/down arrow strip beside it is system-grey (visible
        // split). Setting Controls[0].BackColor matches the band to the digit
        // area; the arrow glyphs stay system-rendered but read fine against
        // either themed band.
        if (_edtOsdDuration.Controls.Count > 0)
        {
            _edtOsdDuration.Controls[0].BackColor = Theme.EditBgColor;
            _edtOsdDuration.Controls[0].ForeColor = Theme.FgColor;
        }

        var durLabel = UiFactory.MakeHintLabel("Duration (ms):", 0, osdRowY + 3);
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
            ForeColor = Theme.CheckboxFgColor,
            BackColor = Theme.BgColor,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(indent, y),
        };
        _chkMuteLock.FlatAppearance.BorderColor = Theme.DividerColor;
        _chkMuteLock.FlatAppearance.CheckedBackColor = Theme.HighlightBg;
        _chkMuteLock.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        Controls.Add(_chkMuteLock);
        var muteLockHint = UiFactory.MakeHintLabel(
            "\u2014  reverts external mute changes every 15 seconds (not instant).",
            _chkMuteLock.Right + 4, y + 1);
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
        const int sectionRight = UiTokens.SettingsSectionRight;
        const int ddlStartupWidth = 130;

        _chkRunAtStartup = new CheckBox
        {
            Text = "Run at startup",
            AutoSize = true,
            Checked = File.Exists(startupPath),
            ForeColor = Theme.CheckboxFgColor,
            BackColor = Theme.BgColor,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(indent, rowY),
        };
        _chkRunAtStartup.FlatAppearance.BorderColor = Theme.DividerColor;
        _chkRunAtStartup.FlatAppearance.CheckedBackColor = Theme.HighlightBg;
        _chkRunAtStartup.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        Controls.Add(_chkRunAtStartup);

        // BorderStyle.FixedSingle paints the wrapper's 1px border in the
        // non-client area — the child ComboBox physically cannot reach NC
        // pixels (NC paint is OS-handled via WM_NCPAINT, not subject to
        // child overpaint). Three prior approaches (Panel BackColor padding
        // trick, BorderPanel.OnPaint rectangle, both with various slack
        // values) all failed in light mode because Flat-style ComboBox
        // overpaints its declared Bounds by 1-2px on the bottom row,
        // erasing any border drawn in client area. NC area is bulletproof.
        // Same BorderStyle the dialog's TextBoxes already use successfully
        // for the Hotkey + Custom Files rows. Border colour is the OS
        // SystemColors.WindowFrame (~#646464) — visible in both palettes
        // and consistent with the rest of the dialog.
        var startWrap = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.EditBgColor,
        };
        _ddlStartMuted = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = ddlStartupWidth,
            Location = new Point(0, 0),       // at panel client-origin
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
        };
        startWrap.Controls.Add(_ddlStartMuted);
        // Size wrapper = combo + 2 each axis (1px border NC area on each side).
        // PreferredHeight is the design-time height before AutoScale; close
        // enough since the wrapper itself scales with the combo.
        startWrap.Size = new Size(
            _ddlStartMuted.Width + 2,
            _ddlStartMuted.PreferredHeight + 2);
        startWrap.Location = new Point(sectionRight - startWrap.Width, rowY - 1);
        Controls.Add(startWrap);
        _ddlStartMuted.Items.AddRange(new[] { "Don't change", "Always muted", "Always unmuted", "Remember last" });
        _ddlStartMuted.SelectedIndex = config.StartMuted switch
        {
            "yes" => 1,
            "unmuted" => 2,
            "last" => 3,
            _ => 0,
        };
        // _ddlStartMuted is already parented to startWrap above — don't
        // re-add it to the form (would steal it from the wrapper and lose
        // the border).

        var startLabel = new Label
        {
            Text = "Mic mode On Startup:",
            AutoSize = true,
            Location = new Point(0, rowY + 3),
        };
        Controls.Add(startLabel);
        startLabel.Left = startWrap.Left - startLabel.PreferredWidth - 6;

        y += 28;

        // Inform the user that PTT mode overrides this preference. The
        // startup mute-preference code force-mutes under PTT regardless of
        // the dropdown, so showing "Don't change" while PTT always mutes
        // would be a small lie. Only render when currently in PTT mode.
        if (config.Mode == "push-to-talk")
        {
            // Right-align the hint under the "On startup:" dropdown so it
            // reads as a caption on that control, not a general-section note.
            var pttHintLabel = UiFactory.MakeHintLabel("Push-to-Talk mode always starts muted.", 0, y);
            Controls.Add(pttHintLabel);
            pttHintLabel.Left = sectionRight - pttHintLabel.PreferredWidth;
            y += 20;
        }
        else
        {
            y += 6;
        }

        // ── Appearance ──
        // Theme pin is restart-to-apply (the GDI brush caches in OsdForm
        // and MenuRenderer capture Theme.* at first class load). When the
        // user changes this combo and clicks Apply / Save, TrayApp.
        // OnSettingsApplied detects the is-dark flip and auto-restarts.
        AddSectionHeader("Appearance", leftMargin, ref y);

        const int themeRowSectionRight = UiTokens.SettingsSectionRight;
        const int themeDdlWidth = 130;
        int themeRowY = y;

        var themeLabel = new Label
        {
            Text = "Theme:",
            AutoSize = true,
            Location = new Point(indent, themeRowY + 3),
            ForeColor = Theme.FgColor,
        };
        Controls.Add(themeLabel);

        // FixedSingle wrapper — same NC-border approach as _ddlStartMuted.
        var themeWrap = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.EditBgColor,
        };
        _ddlTheme = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = themeDdlWidth,
            Location = new Point(0, 0),
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
        };
        _ddlTheme.Items.AddRange(new object[] { "System", "Dark", "Light" });
        // FindStringExact does case-INSENSITIVE matching; the legacy
        // ArrayList.IndexOf path was ordinal-equals, so a lowercase
        // "dark" in MicMute.ini (a user hand-edit, or a future config
        // migration that lowercases) would fall through to System and
        // silently revert the user's theme on next open.
        //
        // Null/empty guard: FindStringExact(null) throws ArgumentNullException
        // (where IndexOf(null) used to return -1 silently). Config.ThemeMode
        // is normally populated by Config.Load's safe-defaults, but a corrupt
        // INI or a partial-write recovery could plausibly leave it null — and
        // throwing here would crash the dialog ctor for what's a recoverable
        // config-corruption case.
        int themeIdx = string.IsNullOrEmpty(config.ThemeMode)
            ? -1
            : _ddlTheme.FindStringExact(config.ThemeMode);
        _ddlTheme.SelectedIndex = themeIdx >= 0 ? themeIdx : 0;
        themeWrap.Controls.Add(_ddlTheme);
        themeWrap.Size = new Size(
            _ddlTheme.Width + 2,
            _ddlTheme.PreferredHeight + 2);
        themeWrap.Location = new Point(themeRowSectionRight - themeWrap.Width, themeRowY - 1);
        Controls.Add(themeWrap);

        y += 34;

        // ── Custom Files (compact 2-cell row) ──
        // Custom mic icons only — Mute/Unmute sound rows removed in v2.1.x
        // to make room for the Appearance section above. Power users can
        // still set MuteSound / UnmuteSound by hand-editing MicMute.ini.
        AddSectionHeader("Custom Files", leftMargin, ref y);

        const int sectionInnerLeft = 16;
        const int sectionInnerRight = UiTokens.SettingsSectionRight;
        const int colGap = 12;
        int cellW = (sectionInnerRight - sectionInnerLeft - colGap) / 2;
        int col1X = sectionInnerLeft;
        int col2X = sectionInnerLeft + cellW + colGap;

        _pathIconMuted   = config.IconMuted;
        _pathIconActive  = config.IconActive;

        _lblIconMuted   = AddCompactFileRow("Muted icon",   () => _pathIconMuted,   v => _pathIconMuted   = v, "Icon files (*.ico)|*.ico",  col1X, y, cellW);
        _lblIconActive  = AddCompactFileRow("Active icon",  () => _pathIconActive,  v => _pathIconActive  = v, "Icon files (*.ico)|*.ico",  col2X, y, cellW);
        y += 26;

        // ── Buttons ──
        y += 12;
        const int dialogWidth = UiTokens.SettingsDialogWidth;
        int rightEdge = dialogWidth - leftMargin;

        // Auxiliary links (left) — subtle, navigation-style. Link / active /
        // visited all use Theme.AccentBlue so the colour stays consistent
        // across light + dark palettes and the post-click "visited purple"
        // default doesn't clash with our accent.
        LinkLabel MakeNavLink(string text, int x, LinkLabelLinkClickedEventHandler onClick)
        {
            var lnk = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y + 6),
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Theme.BgColor,
                LinkColor = Theme.AccentBlue,
                ActiveLinkColor = Theme.AccentBlue,
                VisitedLinkColor = Theme.AccentBlue,
                DisabledLinkColor = Theme.FgDisabledColor,
            };
            lnk.LinkClicked += onClick;
            return lnk;
        }

        var lnkGitHub = MakeNavLink("GitHub", leftMargin, (_, _) =>
        {
            // Process.Start with UseShellExecute=true throws when no default
            // browser is registered, the URL is blocked by Group Policy, or
            // (rarely) the ShellExecute handler returns an error HRESULT.
            // Without the try/catch the lambda escapes to WinForms'
            // ThreadException pump and surfaces as the standard unhandled-
            // exception dialog, which looks like a crash to the user. Catch
            // here and surface a graceful warning instead.
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/itsnateai/MicMute",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Open GitHub URL failed: " + ex.Message);
                MessageBox.Show(this,
                    "Couldn’t open the GitHub page in your browser.\n\n" +
                    "Details: " + ex.Message,
                    "MicMute — Open URL",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        });
        Controls.Add(lnkGitHub);

        var lnkHelp = MakeNavLink("Help", lnkGitHub.Right + 14, (_, _) => HelpWindow.ShowInstance());
        Controls.Add(lnkHelp);

        var lnkUpdate = MakeNavLink("Check for updates", lnkHelp.Right + 14, (_, _) =>
        {
            using var dlg = new UpdateDialog();
            dlg.ShowDialog(this);
        });
        Controls.Add(lnkUpdate);

        // Action buttons (right) — anchored to the right edge via the shared
        // UiFactory so they match Hotkey dialog + any future dialog pixel-perfect.
        //
        // Per-Monitor-V2 DPI gotcha: `AutoSize=true` LinkLabels measure their
        // PreferredWidth at the *current monitor DPI* (font sized in points →
        // GDI converts to native pixels), while every literal here (`rightEdge`,
        // `BtnActionWidth`, `BtnGap`) is design-space (96 DPI, per the pin at
        // the top of this ctor). The previous shrink-on-overflow math
        // (`rightEdge - lnkUpdate.Right - groupGap / 3`) mixed those two
        // coordinate systems and collapsed `btnWidth` to its 64px floor on
        // 125%/150% displays — "Cancel" rendered at the shrunk width was
        // clipping its trailing "l" against the FlatAppearance border inset.
        // Fix: always use the full BtnActionWidth. The left-link group is short
        // enough (GitHub / Help / Check for updates) to never collide at any
        // sensible DPI, and if a future label runs long it should overflow
        // its own footprint, not silently truncate the action buttons.
        int btnWidth = UiTokens.BtnActionWidth;

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

        // Defensive overlap guard — protects against accessibility text-size
        // override (Settings → Accessibility → Text size, independent of
        // display DPI) and any future locale where "Check for updates"
        // measures wider than its design-space footprint. AutoSize
        // LinkLabels measure at the live monitor DPI / font-scale, not the
        // 96-DPI design pin, so a >175% text scale or a long-form locale
        // string can push lnkUpdate.Right past btnOK.Left. The pre-v2.2.2
        // shrink-math caught this by clipping the buttons (visible failure);
        // the fixed-width refactor would otherwise paint labels UNDER the
        // buttons (invisible failure). Hide the rightmost link when it
        // would collide — the user can still reach update check via the
        // tray menu and self-update auto-prompts.
        // Cascade-hide rightmost-first: lnkUpdate is the longest label, so it
        // collides first; lnkHelp is checked second to cover the (rare)
        // extreme-text-scale case where hiding Update alone isn't enough.
        // lnkGitHub is the anchor at leftMargin and is never hidden — if it
        // were to collide, the dialog layout itself would be unusable and a
        // wider-dialog refactor would be the right answer, not a hide.
        if (lnkUpdate.Right > btnOK.Left - UiTokens.BtnGap)
            lnkUpdate.Visible = false;
        if (lnkHelp.Right > btnOK.Left - UiTokens.BtnGap)
            lnkHelp.Visible = false;

        // Footer clearance — buttons sit at row `y` with BtnHeight=28. The old
        // `y + 38` literal left only 10px of slack below the button which read
        // as "smooshed against the bottom edge" at Per-Monitor-V2 fractional
        // DPI ratios where the frame chrome eats a couple of those pixels.
        // BtnHeight + DialogMargin gives the same 16px breathing room used
        // everywhere else in the dialog and matches the top padding.
        ClientSize = new Size(dialogWidth, y + UiTokens.BtnHeight + UiTokens.DialogMargin);
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
            try { File.Delete(startupPath); }
            catch (Exception ex)
            {
                Log.Warn("Delete startup shortcut failed: " + ex.Message);
                MessageBox.Show(this,
                    "Couldn\u2019t remove the startup shortcut from your Startup folder.\n\n" +
                    "MicMute will still start with Windows until the shortcut is removed manually.\n\n" +
                    "Details: " + ex.Message,
                    "MicMute \u2014 Startup shortcut",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

        // Hotkeys — captured inline in the Hotkeys section. Validate here
        // before committing so the user sees feedback before Apply closes.
        if (!ValidateHotkeysBeforeApply())
            return;
        _config.Hotkey = _capturedMainHK;
        _config.DeafenHotkey = _capturedDeafenHK;
        if (_pendingAckedMainHk != null) _config.AckedMainHkConflict = _pendingAckedMainHk;
        if (_pendingAckedDeafenHk != null) _config.AckedDeafenHkConflict = _pendingAckedDeafenHk;

        // Custom files — paths are maintained as string fields by the
        // AddCompactFileRow closures (no hidden TextBox phantom controls).
        // SanitizePath is belt-and-braces here: ValidateCustomFile already rejects
        // UNC at file-pick, but Save-time sanitization keeps Config the single
        // source of truth so any future textbox-paste path stays defended.
        // MuteSound / UnmuteSound are NOT touched here — the GUI was removed
        // for them, so we leave whatever the user set via hand-edited INI.
        _config.IconMuted = Config.SanitizePath((_pathIconMuted ?? "").Trim());
        _config.IconActive = Config.SanitizePath((_pathIconActive ?? "").Trim());

        // Appearance — theme pin is restart-to-apply; TrayApp.OnSettingsApplied
        // detects the is-dark flip post-Save and spawns a replacement process.
        _config.ThemeMode = (_ddlTheme.SelectedItem as string) ?? "System";

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

        // Reset the pending-ack sentinels at the top of every Validate run.
        // Without this, a stale value can survive across Apply attempts when
        // the user reverts a captured combo back to its prior _config value.
        // Reproducer that motivated this fix:
        //   1. Config has Hotkey=X, AckedMainHkConflict=X (user acked X long ago)
        //   2. User opens Settings, changes Toggle to Y → probe Y succeeds →
        //      _pendingAckedMainHk = "" (clear-on-Apply sentinel)
        //   3. Some other validation later in the chain rejects (deafen warn,
        //      icon hard-fail, etc.) → ValidateHotkeysBeforeApply returns false
        //      WITHOUT clearing the pending sentinels (instance state survives).
        //   4. User reverts Y back to X, clicks Apply.
        //   5. mainUnchanged is now true → probe block skipped → _pending stays "".
        //   6. ApplySettings's `if (_pendingAckedMainHk != null) _config.Acked... = ""`
        //      wipes the X ack out of config. Next launch, X is no longer acked
        //      and the user gets a "claimed by another app" warning they
        //      already dismissed.
        // The fix is one reset per Validate entry — the rest of the deferred-
        // commit pattern (null = leave alone, "" = clear on Apply, value = set
        // on Apply) stays intact.
        _pendingAckedMainHk = null;
        _pendingAckedDeafenHk = null;

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
        // Compare by parsed (mods, vk) so that "^F1" and "ctrl+F1" are treated
        // as the same binding even if the string representations differ.
        if (!string.IsNullOrEmpty(_capturedMainHK) &&
            !string.IsNullOrEmpty(_capturedDeafenHK) &&
            Config.ParseHotkey(_capturedMainHK, out uint dupMM, out uint dupMV, allowBare: pttMode) &&
            Config.ParseHotkey(_capturedDeafenHK, out uint dupDM, out uint dupDV, allowBare: false) &&
            (dupMM & ~NativeMethods.MOD_NOREPEAT) == (dupDM & ~NativeMethods.MOD_NOREPEAT) && dupMV == dupDV)
        {
            MessageBox.Show(this,
                "Toggle Mute and Deafen are both set to \"" + Config.HotkeyToReadable(_capturedMainHK) + "\".\n\n" +
                "Windows can\u2019t route one key press to two different actions \u2014 pick a different combo for one of them.",
                "MicMute \u2014 Duplicate hotkey",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Best-effort conflict probe using RegisterHotKey on this dialog's HWND.
        // Limitation: apps that intercept keys via low-level keyboard hooks (e.g.
        // Discord PTT, PowerToys) won't be detected — the probe will succeed even
        // though those apps will still intercept the combo at runtime.
        // This is therefore a courtesy warning, not a hard block.
        // Self-conflict guard: skip the probe when the captured combo matches
        // the one TrayApp already owns on its own HWND. RegisterHotKey is
        // unique per (mod, vk) tuple per process — a second call from the
        // dialog's HWND would fail with ERROR_HOTKEY_ALREADY_REGISTERED and
        // produce a false "claimed by another app" warning where the "another
        // app" is MicMute itself.
        bool mainUnchanged = _capturedMainHK.Equals(_config.Hotkey, StringComparison.OrdinalIgnoreCase);
        bool deafenUnchanged = _capturedDeafenHK.Equals(_config.DeafenHotkey, StringComparison.OrdinalIgnoreCase);
        if (!mainUnchanged &&
            !string.IsNullOrEmpty(_capturedMainHK) &&
            Config.ParseHotkey(_capturedMainHK, out uint probeMMods, out uint probeMVk, allowBare: pttMode))
        {
            bool ok = false;
            try   { ok = NativeMethods.RegisterHotKey(Handle, PROBE_ID_MAIN, probeMMods, probeMVk); }
            finally { if (ok) NativeMethods.UnregisterHotKey(Handle, PROBE_ID_MAIN); }
            if (ok)
            {
                // Probe succeeded — clear any stale ack so a future real conflict re-warns.
                _pendingAckedMainHk = "";
            }
            else if (_capturedMainHK.Equals(_config.AckedMainHkConflict, StringComparison.OrdinalIgnoreCase))
            {
                // User already confirmed "use it anyway" for this exact combo; stay silent.
                _pendingAckedMainHk = _capturedMainHK;
            }
            else
            {
                var res = MessageBox.Show(this,
                    "Toggle Mute hotkey \"" + Config.HotkeyToReadable(_capturedMainHK) + "\" appears to be claimed by another app " +
                    "(detected via RegisterHotKey probe).\n\n" +
                    "Note: apps using low-level keyboard hooks (e.g. Discord PTT, PowerToys) won\u2019t be detected by this probe.\n\n" +
                    "MicMute may lose the race, or both apps may fire at once. Use it anyway?",
                    "MicMute \u2014 Hotkey conflict",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (res != DialogResult.Yes) return false;
                _pendingAckedMainHk = _capturedMainHK;
            }
        }
        if (!deafenUnchanged &&
            !string.IsNullOrEmpty(_capturedDeafenHK) &&
            Config.ParseHotkey(_capturedDeafenHK, out uint probeDMods, out uint probeDVk, allowBare: false))
        {
            bool ok = false;
            try   { ok = NativeMethods.RegisterHotKey(Handle, PROBE_ID_DEAFEN, probeDMods, probeDVk); }
            finally { if (ok) NativeMethods.UnregisterHotKey(Handle, PROBE_ID_DEAFEN); }
            if (ok)
            {
                _pendingAckedDeafenHk = "";
            }
            else if (_capturedDeafenHK.Equals(_config.AckedDeafenHkConflict, StringComparison.OrdinalIgnoreCase))
            {
                _pendingAckedDeafenHk = _capturedDeafenHK;
            }
            else
            {
                var res = MessageBox.Show(this,
                    "Deafen Mute hotkey \"" + Config.HotkeyToReadable(_capturedDeafenHK) + "\" appears to be claimed by another app.\n\n" +
                    "Note: low-level keyboard hook apps won\u2019t be detected by this probe.\n\n" +
                    "Use it anyway?",
                    "MicMute \u2014 Hotkey conflict",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (res != DialogResult.Yes) return false;
                _pendingAckedDeafenHk = _capturedDeafenHK;
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
        var boldFont = new Font(UiTokens.PrimaryFont, UiTokens.SectionHeaderSize, FontStyle.Bold);
        _sectionFonts.Add(boldFont);
        var label = new Label
        {
            Text = text,
            Font = boldFont,
            ForeColor = UiTokens.LabelColor,
            AutoSize = true,
            Location = new Point(x, y),
        };
        Controls.Add(label);
        y += label.Height + 3;

        // Separator line — flat 1-px Theme.DividerColor band instead of the
        // OS Fixed3D etch (Fixed3D ignores BackColor and renders as a 3D
        // groove that reads as an unintended bevel against the themed Bg).
        var sep = new Panel
        {
            BackColor = Theme.DividerColor,
            BorderStyle = BorderStyle.None,
            Height = 1,
            Width = UiTokens.SectionSeparatorWidth,
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
            // Dark mode: the body Fg (#CDD6F3) renders thin against the dark Bg
            // at 9.5pt through FlatStyle.Flat's grayscale-AA path — Theme.CheckboxFgColor
            // returns pure white in dark mode and the normal Fg in light mode.
            ForeColor = Theme.CheckboxFgColor,
            BackColor = Theme.BgColor,
            // FlatStyle.Flat switches CheckBox to a render path that respects
            // ForeColor for the tick glyph. Default Standard uses VisualStyles
            // which renders a light-themed glyph regardless of ForeColor and
            // draws the focus rect via ControlPaint.DrawFocusRectangle (XORs
            // against SystemColors.ControlText — invisible on dark Bg).
            FlatStyle = FlatStyle.Flat,
            Location = new Point(x, y),
        };
        chk.FlatAppearance.BorderColor = Theme.DividerColor;
        chk.FlatAppearance.CheckedBackColor = Theme.HighlightBg;
        chk.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        Controls.Add(chk);
        y += chk.Height + 4;
        return chk;
    }

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
            BackColor = Theme.EditBgColor,
            ForeColor = Theme.FgColor,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Width = dispW,
            Location = new Point(dispX, y - 1),
        };
        Controls.Add(display);

        var btnClear = UiFactory.MakeIconButton("\u00D7", clearX, y - 1);
        Controls.Add(btnClear);

        _fileRowTooltip ??= UiFactory.MakeThemedToolTip();
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
            display.ForeColor = empty ? UiTokens.GreyTextColor : Theme.FgColor;
            display.BackColor = captureMode ? UiTokens.FocusYellow : Theme.EditBgColor;
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
            // SuppressKeyPress only inside capture mode — outside capture,
            // Escape, Enter, and Tab must fall through to the form's default
            // handling (CancelButton, AcceptButton, and focus advancement).
            if (!captureMode) return;
            e.SuppressKeyPress = true;

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
            // Tab — suppress key press so the tab character isn't typed, but
            // don't suppress the key itself so WinForms advances focus normally;
            // Leave will commit the current capture value.
            if (e.KeyCode == Keys.Tab)
            {
                e.SuppressKeyPress = false;
                return;
            }

            bool bare = bareKeysAllowed();

            // Bare modifier-only press (RCtrl alone, LShift alone, etc.)
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            {
                if (!bare)
                {
                    // Briefly tint the display red to signal that bare modifiers
                    // aren't accepted outside Push-to-Talk mode.
                    display.BackColor = UiTokens.ErrorTint;
                    string prevText = display.Text;
                    display.Text = "Bare modifiers need Push-to-Talk mode";
                    var rejectTimer = new System.Windows.Forms.Timer { Interval = 1800 };
                    _activeRejectTimers.Add(rejectTimer);
                    rejectTimer.Tick += (_, _) =>
                    {
                        // Three races to defend against:
                        // (1) Dispose ran between WM_TIMER being posted and us
                        //     dequeuing it — list no longer contains rejectTimer,
                        //     and calling Stop on the disposed timer throws
                        //     ObjectDisposedException into the WinForms thread-
                        //     exception pump.
                        // (2) User pressed Escape during the 1800ms window —
                        //     CancelCapture cleared captureMode and re-painted
                        //     the field, so restoring FocusYellow here would
                        //     paint over the cancelled state and strand the
                        //     row in capture-mode yellow.
                        // (3) The display TextBox was disposed but the row's
                        //     closure on captureMode still resolves — the
                        //     IsDisposed check catches this.
                        if (!_activeRejectTimers.Remove(rejectTimer))
                            return; // Dispose-sweep already handled it.
                        rejectTimer.Stop();
                        rejectTimer.Dispose();
                        if (display.IsDisposed || !captureMode)
                            return;
                        display.Text = prevText;
                        display.BackColor = UiTokens.FocusYellow;
                    };
                    rejectTimer.Start();
                    return;
                }
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
    ///
    /// Path state is owned by the caller via getPath/setPath callbacks — no
    /// hidden phantom TextBox is parented to the form.
    /// </summary>
    private TextBox AddCompactFileRow(
        string labelText,
        Func<string> getPath, Action<string> setPath,
        string filter,
        int x, int y, int cellWidth)
    {
        const int labelWidth = 88;
        const int clearWidth = 22;
        const int gap = 4;

        int dispX = x + labelWidth + gap;
        int clearX = x + cellWidth - clearWidth;
        int dispW = clearX - dispX - gap;
        bool hasFile = !string.IsNullOrEmpty(getPath());

        var lbl = new Label
        {
            Text = labelText,
            Width = labelWidth,
            AutoSize = false,
            Location = new Point(x, y + 4),
            ForeColor = UiTokens.LabelColor,
        };
        Controls.Add(lbl);

        var fileDisplay = new TextBox
        {
            Text = FileLabel(getPath()),
            ReadOnly = true,
            BackColor = Theme.EditBgColor,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            ForeColor = hasFile ? Theme.FgColor : UiTokens.GreyTextColor,
            Width = dispW,
            Location = new Point(dispX, y - 1),
        };
        Controls.Add(fileDisplay);

        // Clear button — only meaningful when a file is set. Single glyph to
        // keep the column tight; tooltip carries the meaning for screen readers.
        // Routed through UiFactory so it matches the Hotkeys section's × pixel-
        // for-pixel (both sit inline with a TextBox, both use BtnIconHeight=23).
        var btnClear = UiFactory.MakeIconButton("\u00D7", clearX, y - 1);
        btnClear.Enabled = hasFile;
        _fileRowTooltip ??= UiFactory.MakeThemedToolTip();
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

            setPath(ofd.FileName);
            fileDisplay.Text = FileLabel(ofd.FileName);
            fileDisplay.ForeColor = Theme.FgColor;
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
            setPath("");
            fileDisplay.Text = FileLabel("");
            fileDisplay.ForeColor = UiTokens.GreyTextColor;
            btnClear.Enabled = false;
        };

        return fileDisplay;
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
    internal static (bool hardFail, string message) ValidateCustomFile(string path, string filter)
    {
        const long IconSoftCapBytes  = 2L * 1024 * 1024;   // 2 MB
        const long WavSoftCapBytes   = 10L * 1024 * 1024;  // 10 MB

        bool isIcon = filter.Contains(".ico", StringComparison.OrdinalIgnoreCase);
        bool isWav  = filter.Contains(".wav", StringComparison.OrdinalIgnoreCase);

        // Reject UNC / file:// before any I/O. Touching `\\server\share\foo.ico`
        // via FileInfo / new Icon(...) / SoundPlayer triggers SMB auth to the
        // remote host, leaking an NTLMv2 challenge. Same gate as Config.SanitizePath
        // \u2014 keep the rules colocated there.
        if (Config.SanitizePath(path).Length == 0)
            return (true,
                "Network paths (UNC, file://) are not allowed for security reasons. " +
                "Pick a local file.");

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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
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
            // Defensively unregister probe IDs in case ValidateHotkeysBeforeApply
            // threw between RegisterHotKey and UnregisterHotKey (A1-F04).
            // These are no-ops if the IDs aren't registered; calling them is safe.
            if (IsHandleCreated)
            {
                NativeMethods.UnregisterHotKey(Handle, PROBE_ID_MAIN);
                NativeMethods.UnregisterHotKey(Handle, PROBE_ID_DEAFEN);
            }
            _dialogFont?.Dispose();
            foreach (var f in _sectionFonts)
                f.Dispose();
            _sectionFonts.Clear();
            _fileRowTooltip?.Dispose();
            // Sweep any in-flight reject-animation timers — covers the
            // narrow race where the user dismisses Settings during the
            // 1800ms tint window. Iterate a copy because Stop()→Tick
            // could mutate the list during the sweep.
            foreach (var t in _activeRejectTimers.ToArray())
            {
                t.Stop();
                t.Dispose();
            }
            _activeRejectTimers.Clear();
        }
        base.Dispose(disposing);
    }
}
