namespace MicMute;

/// <summary>
/// Dialog for changing the global mute hotkey at runtime.
/// Captures actual key presses and converts to internal format.
/// </summary>
internal sealed class HotkeyDialog : Form
{
    private readonly TextBox _displayBox;
    private readonly Font _dialogFont;
    private string _capturedHotkey = "";

    public string ResultHotkey { get; private set; } = "";

    public HotkeyDialog(string currentHotkey)
    {
        Text = "MicMute \u2014 Change Hotkey";
        TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        _dialogFont = new Font("Segoe UI", 10f);
        Font = _dialogFont;
        ClientSize = new Size(320, 180);

        var label = new Label
        {
            Text = "Press a key combination:",
            AutoSize = true,
            Location = new Point(16, 16),
        };
        Controls.Add(label);

        _displayBox = new TextBox
        {
            Location = new Point(16, 44),
            Width = 250,
            ReadOnly = true,
            BackColor = Color.White,
            Text = "(press a key combo)",
        };
        _displayBox.KeyDown += OnHotkeyKeyDown;
        _displayBox.KeyUp += (_, _) => { }; // swallow
        Controls.Add(_displayBox);

        var currentLabel = new Label
        {
            Text = "Current: " + Config.HotkeyToReadable(currentHotkey),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(16, 76),
        };
        Controls.Add(currentLabel);

        var hintLabel = new Label
        {
            Text = "Or type directly: # = Win, ^ = Ctrl, ! = Alt, + = Shift",
            ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
            AutoSize = true,
            Location = new Point(16, 98),
        };
        Controls.Add(hintLabel);

        var btnRaw = new Button
        {
            Text = "Type manually",
            Width = 100,
            Location = new Point(16, 124),
        };
        btnRaw.Click += (_, _) =>
        {
            _displayBox.ReadOnly = false;
            _displayBox.Text = "";
            _displayBox.KeyDown -= OnHotkeyKeyDown;
            _displayBox.Focus();
        };
        Controls.Add(btnRaw);

        var btnOK = new Button
        {
            Text = "OK",
            Width = 80,
            Location = new Point(130, 124),
            DialogResult = DialogResult.OK,
        };
        btnOK.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_capturedHotkey))
                ResultHotkey = _capturedHotkey;
            else if (!_displayBox.ReadOnly)
            {
                string raw = _displayBox.Text.Trim();
                if (!string.IsNullOrEmpty(raw))
                    ResultHotkey = raw;
            }
        };
        Controls.Add(btnOK);
        AcceptButton = btnOK;

        var btnCancel = new Button
        {
            Text = "Cancel",
            Width = 80,
            Location = new Point(218, 124),
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(btnCancel);
        CancelButton = btnCancel;
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;

        // Ignore modifier-only presses
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        string prefix = "";
        if (e.Modifiers.HasFlag(Keys.Control)) prefix += "^";
        if (e.Modifiers.HasFlag(Keys.Alt)) prefix += "!";
        if (e.Modifiers.HasFlag(Keys.Shift)) prefix += "+";
        // Win key detection via GetAsyncKeyState
        if ((NativeMethods.GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
            (NativeMethods.GetAsyncKeyState(0x5C) & 0x8000) != 0)
            prefix = "#" + prefix;

        string keyName = KeyCodeToName(e.KeyCode);
        if (string.IsNullOrEmpty(keyName))
            return;

        _capturedHotkey = prefix + keyName;
        _displayBox.Text = Config.HotkeyToReadable(_capturedHotkey);
    }

    internal static string KeyCodeToName(Keys key)
    {
        // Letters
        if (key is >= Keys.A and <= Keys.Z)
            return ((char)key).ToString().ToLowerInvariant();
        // Numbers
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((char)('0' + (key - Keys.D0))).ToString();
        // Function keys
        if (key is >= Keys.F1 and <= Keys.F24)
            return "F" + (key - Keys.F1 + 1);
        // Numpad
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return "Numpad" + (key - Keys.NumPad0);

        return key switch
        {
            Keys.Space => "Space",
            Keys.Enter or Keys.Return => "Enter",
            Keys.Tab => "Tab",
            Keys.Escape => "Escape",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.CapsLock => "CapsLock",
            Keys.NumLock => "NumLock",
            Keys.Scroll => "ScrollLock",
            Keys.PrintScreen => "PrintScreen",
            Keys.Pause => "Pause",
            Keys.Add => "NumpadAdd",
            Keys.Subtract => "NumpadSub",
            Keys.Multiply => "NumpadMult",
            Keys.Divide => "NumpadDiv",
            Keys.Decimal => "NumpadDot",
            Keys.OemPeriod => ".",
            Keys.Oemcomma => ",",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemBackslash or Keys.OemPipe => @"\",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.Oemtilde => "`",
            Keys.OemQuestion => "/",
            _ => "",
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _dialogFont?.Dispose();
        base.Dispose(disposing);
    }
}
