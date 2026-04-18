namespace MicMute;

/// <summary>
/// Discreet borderless OSD pinned above the system tray. Click-through,
/// no-activate, auto-dismiss, cached GDI resources.
///
/// v2 palette ported from MWBToggle — softened greys, regular (not semibold)
/// weight, muted accent dots, tighter padding, lower opacity. Same positioning
/// logic as before: Shell_TrayWnd anchoring when it lands inside the working
/// area, working-area corner fallback for top/left/right taskbars.
/// </summary>
internal sealed class OsdForm : Form
{
    private readonly System.Windows.Forms.Timer _dismissTimer;
    private bool _disposed;

    // Cached GDI resources — created once, reused across the app lifetime.
    private static readonly Font s_labelFont = new("Segoe UI", 9f);
    private static readonly SolidBrush s_bgBrush = new(Color.FromArgb(0x24, 0x24, 0x26));
    private static readonly SolidBrush s_textBrush = new(Color.FromArgb(0xC8, 0xC8, 0xCC));
    private static readonly SolidBrush s_mutedDotBrush = new(Color.FromArgb(0xCC, 0x5A, 0x5A));  // muted red
    private static readonly SolidBrush s_activeDotBrush = new(Color.FromArgb(0x4C, 0xB8, 0x74)); // muted green

    private static readonly string s_mutedLabel = "Mic Muted";
    private static readonly string s_activeLabel = "Mic Active";
    // Dot is drawn via FillEllipse for deterministic pixel placement —
    // DrawString(U+25CF) shifts vertically with font metrics and ends up
    // 1-2px below the letter baseline.
    private const int DotSize = 8;

    private bool _showMuted;
    private string _customText;

    public OsdForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x24, 0x24, 0x26);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);

        _dismissTimer = new System.Windows.Forms.Timer();
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            if (!_disposed) Hide();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW — no taskbar entry
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT — click-through
            cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Show the OSD with standard mute/active state display.</summary>
    public void ShowOsd(bool muted, int durationMs)
    {
        _showMuted = muted;
        _customText = null;
        ShowInternal(muted ? s_mutedLabel : s_activeLabel, durationMs);
    }

    /// <summary>Show the OSD with custom notification text.</summary>
    public void ShowNotification(string text, bool isMuted, int durationMs)
    {
        _showMuted = isMuted;
        _customText = text;
        ShowInternal(text, durationMs);
    }

    /// <summary>
    /// Show the OSD until explicitly hidden. Used for sticky PTT "mic listening"
    /// where the user needs a persistent indicator that the mic is hot.
    /// </summary>
    public void ShowPersistent(string text, bool isMuted)
    {
        _showMuted = isMuted;
        _customText = text;
        ShowInternal(text, -1);
    }

    /// <summary>Hide a persistent OSD. Safe to call when not shown.</summary>
    public void HidePersistent()
    {
        if (_disposed || IsDisposed) return;
        _dismissTimer.Stop();
        if (Visible) Hide();
    }

    private void ShowInternal(string displayText, int durationMs)
    {
        // Defensive: any failure in measurement/positioning should not kill
        // the OSD pipeline or propagate to the caller (a hotkey handler).
        try
        {
            if (_disposed || IsDisposed) return;
            if (string.IsNullOrWhiteSpace(displayText)) return;

            // Measure text — tighter padding (10 + 12 + text + 12) and a
            // shorter pill height than the original high-contrast variant.
            using (var g = CreateGraphics())
            {
                var labelSize = g.MeasureString(displayText, s_labelFont);
                int w = 10 + 12 + (int)Math.Ceiling(labelSize.Width) + 12;
                int h = 28;

                // Default anchor: bottom-right corner of the working area.
                // WorkingArea already excludes the taskbar regardless of its
                // edge (top/left/right/bottom), so this is safe for every
                // taskbar orientation.
                var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var workArea = screen.WorkingArea;
                int xPos = workArea.Right - w - 12;
                int yPos = workArea.Bottom - h - 8;

                // Try precise Shell_TrayWnd anchoring; accept only if it
                // stays inside the working area. Top/left/right taskbars
                // place the naive anchor off-screen — the bounds check
                // rejects those and the working-area fallback wins.
                nint trayHwnd = NativeMethods.FindWindow("Shell_TrayWnd", "");
                if (trayHwnd != 0 &&
                    NativeMethods.GetWindowRect(trayHwnd, out var rect))
                {
                    int anchoredX = rect.Right - w - 12;
                    int anchoredY = rect.Top - h - 8;
                    if (anchoredY >= workArea.Top &&
                        anchoredX >= workArea.Left &&
                        anchoredX + w <= workArea.Right)
                    {
                        xPos = anchoredX;
                        yPos = anchoredY;
                    }
                }

                SetBounds(xPos, yPos, w, h);
            }

            // Win11 rounded corners — returns HRESULT on older Windows
            // without throwing. Enhancement, not a requirement.
            int preference = NativeMethods.DWMWCP_ROUND;
            _ = NativeMethods.DwmSetWindowAttribute(Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));

            Opacity = 215.0 / 255.0;
            Invalidate();

            if (!Visible) Show();

            // durationMs <= 0 is the "persistent" sentinel — no auto-dismiss.
            // Caller owns Hide() via HidePersistent().
            _dismissTimer.Stop();
            if (durationMs > 0)
            {
                _dismissTimer.Interval = Math.Max(500, durationMs);
                _dismissTimer.Start();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("OsdForm.ShowInternal failed: " + ex.Message);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.FillRectangle(s_bgBrush, ClientRectangle);

        // Centre the dot vertically against the 28px pill. SmoothingMode
        // antialiases the ellipse so it doesn't look like a chunky square.
        var prev = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var dotBrush = _showMuted ? s_mutedDotBrush : s_activeDotBrush;
        int dotY = (ClientSize.Height - DotSize) / 2;
        g.FillEllipse(dotBrush, 11, dotY, DotSize, DotSize);
        g.SmoothingMode = prev;

        string label = _customText ?? (_showMuted ? s_mutedLabel : s_activeLabel);
        g.DrawString(label, s_labelFont, s_textBrush, 24, 5);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _dismissTimer.Stop();
            _dismissTimer.Dispose();
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
