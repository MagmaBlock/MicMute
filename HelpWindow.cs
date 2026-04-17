namespace MicMute;

using System.Text;

/// <summary>
/// Singleton resizable help window. Renders a structured, typographically
/// styled view of <see cref="s_helpText"/> via a <see cref="RichTextBox"/>.
/// </summary>
internal sealed class HelpWindow : Form
{
    private static HelpWindow s_instance = null!;
    private readonly RichTextBox _textBox;
    private readonly List<Font> _fonts = new();

    private static readonly string s_helpText = @"MICMUTE — Global Microphone Mute Toggle

MicMute lets you mute and unmute your microphone system-wide using a hotkey or the tray icon. It works at the Windows audio level, so it affects all apps at once — Zoom, Discord, Teams, etc.

Green tray icon = mic is active (unmuted)
Red tray icon = mic is muted

——— BASIC USAGE ———————————————————

• Left-click the tray icon to toggle mute.
• Press your hotkey (default: Win+Shift+Ctrl+A) to toggle from anywhere.
• Right-click the tray icon for the full menu (change mode, pick a mic, open settings, etc.).
• Change your hotkey anytime via Tray → ""Change Hotkey…"" in the menu.

——— MODES ————————————————————————

Toggle (default): Press the hotkey once to mute, press again to unmute.

Push-to-Talk: Hold the hotkey to unmute. Releasing it mutes you again. Useful for noisy environments where you only want to be heard while actively speaking.

Switch modes via the tray menu (Mode → Toggle / Push-to-Talk), or enable ""Middle-click tray icon to toggle"" in Settings to quickly swap between them.

——— DEAFEN MODE ———————————————————

Deafen mutes both your microphone AND your speakers at the same time. Useful for stepping away or silencing everything quickly. Assign a hotkey in Settings under the Hotkeys section. Press again to undeafen (restores both to their previous state).

——— SETTINGS —————————————————————

Sound feedback: Plays a short tone when you mute or unmute.

On-screen display (OSD): Shows a small dark floating bubble above the taskbar when you toggle mute. The Duration setting controls how long it stays visible (minimum 500 ms).

Mute Lock: Prevents other applications from silently unmuting or muting your mic.

Middle-click toggle: When enabled, middle-clicking the tray icon swaps between Toggle and Push-to-Talk mode.

Run at startup: Creates a Windows startup shortcut so MicMute launches automatically when you log in.

On startup: Controls what happens to your mic when MicMute starts:
  • Don't change — leaves your mic however it was.
  • Always muted — forces mic muted on launch.
  • Always unmuted — forces mic unmuted on launch.
  • Remember last — restores the mute state from your last session.

——— HOTKEYS —————————————————————

Your main mute/unmute hotkey is set via the tray menu (right-click → ""Change Hotkey…""). The Settings window has a separate field for the Deafen hotkey.

Both support Windows key combinations (like Win+Shift+D). Use AHK syntax:
  # = Win,  ^ = Ctrl,  ! = Alt,  + = Shift
  Example: #+d means Win+Shift+D

——— CUSTOM FILES ——————————————————

Muted icon / Active icon: Replace the default red/green tray icons with your own .ico files.

Mute sound / Unmute sound: Replace the default beep tones with your own .wav files for audio feedback.

Use Browse to pick a file, or Clear to revert to the defaults.

——— MIC SOURCE ———————————————————

Right-click the tray icon → ""Mic Source"" to choose which microphone MicMute controls. By default it uses your Windows system default. If you switch mics or plug in a new one, MicMute auto-detects the change and reconnects.";

    private HelpWindow()
    {
        Text = "MicMute v" + Config.Version + " \u2014 Help";
        TopMost = true;
        BackColor = Color.White;
        ClientSize = new Size(540, 560);
        MinimumSize = new Size(440, 360);
        StartPosition = FormStartPosition.CenterScreen;

        _textBox = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            WordWrap = true,
            TabStop = false,
            Location = new Point(18, 14),
            Size = new Size(ClientSize.Width - 36, ClientSize.Height - 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(_textBox);

        RenderHelp();

        // Kill the default "all text selected on show" behaviour.
        Shown += (_, _) =>
        {
            _textBox.SelectionStart = 0;
            _textBox.SelectionLength = 0;
            _textBox.DeselectAll();
            ActiveControl = null;
        };

        FormClosed += (_, _) => s_instance = null!;
    }

    private void RenderHelp()
    {
        // Track each font immediately — if any ctor throws (OOM / GDI
        // exhaustion), the already-constructed ones would otherwise leak
        // their native handles until the GC finalizer eventually runs.
        var titleFont = new Font("Segoe UI", 13.5f, FontStyle.Bold);
        _fonts.Add(titleFont);
        var headerFont = new Font("Segoe UI Semibold", 10.75f, FontStyle.Bold);
        _fonts.Add(headerFont);
        var bodyFont = new Font("Segoe UI", 9.75f);
        _fonts.Add(bodyFont);

        var titleColor = Color.FromArgb(0x11, 0x11, 0x11);
        var headerColor = Color.FromArgb(0x22, 0x55, 0xAA);
        var bodyColor = Color.FromArgb(0x1E, 0x1E, 0x1E);

        _textBox.Clear();

        var body = new StringBuilder();

        void FlushBody()
        {
            if (body.Length == 0) return;
            // Collapse leading blank lines so sections don't have stacked gaps.
            var text = body.ToString().TrimStart('\r', '\n');
            body.Clear();
            if (text.Length == 0) return;
            _textBox.SelectionFont = bodyFont;
            _textBox.SelectionColor = bodyColor;
            _textBox.AppendText(text);
        }

        var lines = s_helpText.Replace("\r\n", "\n").Split('\n');
        bool titleWritten = false;

        foreach (var raw in lines)
        {
            if (!titleWritten)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                _textBox.SelectionFont = titleFont;
                _textBox.SelectionColor = titleColor;
                _textBox.AppendText(raw.Trim() + "\n\n");
                titleWritten = true;
                continue;
            }

            if (raw.StartsWith("\u2014\u2014\u2014") || raw.StartsWith("---"))
            {
                FlushBody();
                var title = raw.Trim().Trim('\u2014', '-', ' ');
                if (title.Length == 0) continue;
                _textBox.AppendText("\n");
                _textBox.SelectionFont = headerFont;
                _textBox.SelectionColor = headerColor;
                _textBox.AppendText(title + "\n\n");
                continue;
            }

            body.AppendLine(raw);
        }
        FlushBody();

        _textBox.SelectionStart = 0;
        _textBox.SelectionLength = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var f in _fonts) f.Dispose();
            _fonts.Clear();
        }
        base.Dispose(disposing);
    }

    public static void ShowInstance()
    {
        if (s_instance != null && !s_instance.IsDisposed)
        {
            s_instance.Show();
            s_instance.BringToFront();
            return;
        }
        s_instance = new HelpWindow();
        s_instance.Show();
    }
}
