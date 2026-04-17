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
    private readonly TextBox _edtOsdDuration;
    private readonly CheckBox _chkMuteLock;
    private readonly CheckBox _chkMiddleClick;
    private readonly CheckBox _chkRunAtStartup;
    private readonly CheckBox _chkLowLatencyPtt;
    private readonly ComboBox _ddlStartMuted;

    // Hotkeys
    private readonly TextBox _edtDeafenHK;
    private string _capturedDeafenHK = "";

    // Custom files
    private readonly TextBox _edtIconMuted;
    private readonly TextBox _lblIconMuted;
    private readonly TextBox _edtIconActive;
    private readonly TextBox _lblIconActive;
    private readonly TextBox _edtMuteSound;
    private readonly TextBox _lblMuteSound;
    private readonly TextBox _edtUnmuteSound;
    private readonly TextBox _lblUnmuteSound;

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

        // ── Hotkeys ──
        AddSectionHeader("Hotkeys", leftMargin, ref y);

        var deafenLabel = new Label { Text = "Deafen Mute hotkey:", AutoSize = true, Location = new Point(indent, y + 2) };
        Controls.Add(deafenLabel);
        _capturedDeafenHK = config.DeafenHotkey;
        _edtDeafenHK = new TextBox
        {
            Text = string.IsNullOrEmpty(config.DeafenHotkey) ? "(not set)" : Config.HotkeyToReadable(config.DeafenHotkey),
            Width = 160,
            ReadOnly = true,
            BackColor = Color.White,
            ForeColor = string.IsNullOrEmpty(config.DeafenHotkey) ? Color.FromArgb(0x88, 0x88, 0x88) : Color.Black,
            Location = new Point(deafenLabel.Right + 8, y - 1),
        };
        // Visual cue while recording a new combo.
        _edtDeafenHK.Enter += (_, _) => _edtDeafenHK.BackColor = Color.FromArgb(0xFF, 0xF8, 0xDC);
        _edtDeafenHK.Leave += (_, _) => _edtDeafenHK.BackColor = Color.White;
        _edtDeafenHK.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            string prefix = "";
            if (e.Modifiers.HasFlag(Keys.Control)) prefix += "^";
            if (e.Modifiers.HasFlag(Keys.Alt)) prefix += "!";
            if (e.Modifiers.HasFlag(Keys.Shift)) prefix += "+";
            if ((NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0)
                prefix = "#" + prefix;

            string keyName = HotkeyDialog.KeyCodeToName(e.KeyCode);
            if (string.IsNullOrEmpty(keyName)) return;

            _capturedDeafenHK = prefix + keyName;
            _edtDeafenHK.Text = Config.HotkeyToReadable(_capturedDeafenHK);
            _edtDeafenHK.ForeColor = Color.Black;
        };
        Controls.Add(_edtDeafenHK);

        var btnClearHK = new Button { Text = "Clear", Width = 45, Location = new Point(_edtDeafenHK.Right + 4, y - 1) };
        btnClearHK.Click += (_, _) =>
        {
            _capturedDeafenHK = "";
            _edtDeafenHK.Text = "(not set)";
            _edtDeafenHK.ForeColor = Color.FromArgb(0x88, 0x88, 0x88);
        };
        Controls.Add(btnClearHK);

        var hkHintLabel = new Label
        {
            Text = "Click the box and press a key combo to bind or change",
            AutoSize = true,
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            Location = new Point(indent, y + 22),
        };
        Controls.Add(hkHintLabel);
        y += 48;

        // ── Behavior ──
        AddSectionHeader("Behavior", leftMargin, ref y);

        _chkSoundFeedback = AddCheckBox("Sound feedback on mute/unmute", indent, ref y, config.SoundFeedback);
        _chkOsd = AddCheckBox("On-screen display bubble on mute/unmute", indent, ref y, config.OsdEnabled);

        // OSD duration
        var durLabel = new Label { Text = "Duration (ms):", AutoSize = true, ForeColor = Color.FromArgb(0x88, 0x88, 0x88), Location = new Point(48, y + 2) };
        Controls.Add(durLabel);
        _edtOsdDuration = new TextBox { Text = config.OsdDuration.ToString(), Width = 55, Location = new Point(durLabel.Right + 6, y - 1) };
        Controls.Add(_edtOsdDuration);
        y += 28;

        _chkMuteLock = AddCheckBox("Mute Lock (prevent external apps from changing mute state)", indent, ref y, config.MuteLock);
        _chkMiddleClick = AddCheckBox("Middle-click tray icon to toggle Toggle/PTT mode", indent, ref y, config.MiddleClickToggle);
        _chkLowLatencyPtt = AddCheckBox("Low-latency PTT (fullscreen-safe; allows bare keys)", indent, ref y, config.LowLatencyPtt);

        string startupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MicMute.lnk");
        _chkRunAtStartup = AddCheckBox("Run at startup", indent, ref y, File.Exists(startupPath));

        // On startup dropdown
        var startLabel = new Label { Text = "On startup:", AutoSize = true, Location = new Point(indent, y + 2) };
        Controls.Add(startLabel);
        _ddlStartMuted = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
            Location = new Point(startLabel.Right + 8, y - 1),
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
        y += 30;

        // ── Custom Files ──
        AddSectionHeader("Custom Files", leftMargin, ref y);

        (_edtIconMuted, _lblIconMuted) = AddFileRow("Muted icon:", config.IconMuted, "Icon files (*.ico)|*.ico", indent, ref y);
        (_edtIconActive, _lblIconActive) = AddFileRow("Active icon:", config.IconActive, "Icon files (*.ico)|*.ico", indent, ref y);
        (_edtMuteSound, _lblMuteSound) = AddFileRow("Mute sound:", config.MuteSound, "Sound files (*.wav)|*.wav", indent, ref y);
        (_edtUnmuteSound, _lblUnmuteSound) = AddFileRow("Unmute sound:", config.UnmuteSound, "Sound files (*.wav)|*.wav", indent, ref y);

        // ── Buttons ──
        y += 12;
        const int dialogWidth = 498;
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

        // Action buttons (right) — anchored to the right edge. Width is
        // shrunk if the LinkLabel row runs wide (long locales, scaled DPI)
        // so left and right groups never overlap.
        const int btnMinWidth = 64;
        const int btnDefaultWidth = 80;
        const int btnGap = 6;
        const int groupGap = 16;
        int leftGroupRight = lnkUpdate.Right;
        int rightGroupAvailable = rightEdge - leftGroupRight - groupGap;
        int btnWidth = Math.Min(btnDefaultWidth,
            Math.Max(btnMinWidth, (rightGroupAvailable - 2 * btnGap) / 3));

        var btnCancel = new Button { Text = "Cancel", Width = btnWidth, Location = new Point(rightEdge - btnWidth, y) };
        btnCancel.Click += (_, _) => Close();
        Controls.Add(btnCancel);
        CancelButton = btnCancel;

        var btnApply = new Button { Text = "Apply", Width = btnWidth, Location = new Point(btnCancel.Left - btnGap - btnWidth, y) };
        btnApply.Click += (_, _) => ApplySettings();
        Controls.Add(btnApply);

        var btnOK = new Button { Text = "OK", Width = btnWidth, Location = new Point(btnApply.Left - btnGap - btnWidth, y) };
        btnOK.Click += (_, _) => { ApplySettings(); Close(); };
        Controls.Add(btnOK);
        AcceptButton = btnOK;

        ClientSize = new Size(dialogWidth, y + 38);
    }

    private void ApplySettings()
    {
        _config.SoundFeedback = _chkSoundFeedback.Checked;
        _config.OsdEnabled = _chkOsd.Checked;
        if (int.TryParse(_edtOsdDuration.Text, out int dur))
            _config.OsdDuration = Math.Max(500, dur);
        _config.MuteLock = _chkMuteLock.Checked;
        _config.MiddleClickToggle = _chkMiddleClick.Checked;
        _config.LowLatencyPtt = _chkLowLatencyPtt.Checked;

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

        // Deafen hotkey
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
            Width = 466,
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

    private (TextBox edit, TextBox display) AddFileRow(string labelText, string currentPath, string filter, int x, ref int y)
    {
        // Layout: [label 80w] [filename textbox fills] [Browse 65w] [Clear 45w]
        // Right edge matches the section separator (x=16 + 466 = 482).
        const int labelWidth = 80;
        const int browseWidth = 65;
        const int clearWidth = 45;
        const int gap = 4;
        const int rightEdge = 482;

        int clearX = rightEdge - clearWidth;
        int browseX = clearX - gap - browseWidth;
        int textX = x + labelWidth;
        int textW = browseX - gap - textX;

        var lbl = new Label
        {
            Text = labelText,
            Width = labelWidth,
            AutoSize = false,
            Location = new Point(x, y + 4),
        };
        Controls.Add(lbl);

        // Read-only display box matches the Deafen hotkey row styling.
        var fileDisplay = new TextBox
        {
            Text = FileLabel(currentPath),
            ReadOnly = true,
            BackColor = Color.White,
            ForeColor = string.IsNullOrEmpty(currentPath) ? Color.FromArgb(0x88, 0x88, 0x88) : Color.Black,
            Width = textW,
            Location = new Point(textX, y - 1),
        };
        Controls.Add(fileDisplay);

        // Hidden field: stores the full path (display shows only the filename).
        var edit = new TextBox { Text = currentPath, Visible = false, Width = 0, Location = new Point(0, 0) };
        Controls.Add(edit);

        var btnBrowse = new Button { Text = "Browse\u2026", Width = browseWidth, Location = new Point(browseX, y - 1) };
        Controls.Add(btnBrowse);

        var btnClear = new Button { Text = "Clear", Width = clearWidth, Location = new Point(clearX, y - 1) };
        Controls.Add(btnClear);

        btnBrowse.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog { Filter = filter };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                edit.Text = ofd.FileName;
                fileDisplay.Text = FileLabel(ofd.FileName);
                fileDisplay.ForeColor = Color.Black;
            }
        };

        btnClear.Click += (_, _) =>
        {
            edit.Text = "";
            fileDisplay.Text = "(none)";
            fileDisplay.ForeColor = Color.FromArgb(0x88, 0x88, 0x88);
        };

        y += 30;
        return (edit, fileDisplay);
    }

    private static string FileLabel(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "(none)";
        return Path.GetFileName(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dialogFont?.Dispose();
            foreach (var f in _sectionFonts)
                f.Dispose();
            _sectionFonts.Clear();
        }
        base.Dispose(disposing);
    }
}
