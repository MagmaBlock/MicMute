namespace MicMute;

using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Shared factory for MicMute UI controls. All dialogs MUST use these
/// builders — no bare `new Button { ... }` scattered across dialog code.
/// Sizes, colors, and paddings come from <see cref="UiTokens"/>.
///
/// The factory guarantees that every button has the same Height,
/// AutoSize=false, and UseVisualStyleBackColor=true. That's the defense
/// against the "different button sizes on different tabs" drift pattern.
/// </summary>
internal static class UiFactory
{
    // ── Buttons ─────────────────────────────────────────────────────────

    public static Button MakeButton(string text, int width, int x, int y)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = UiTokens.BtnHeight,
            AutoSize = false,
            // FlatStyle.Flat + explicit Theme.* colors instead of
            // UseVisualStyleBackColor — the visual-styles path renders the
            // OS-themed (light) button chrome regardless of our BackColor,
            // which clashes with dark dialogs. Flat respects every color
            // assignment and lets HighlightBg / EditBg drive hover/press.
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            Location = new Point(x, y),
        };
        btn.FlatAppearance.BorderColor = Theme.DividerColor;
        btn.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        btn.FlatAppearance.MouseDownBackColor = Theme.EditBgColor;
        return btn;
    }

    public static Button MakeActionButton(string text, int x, int y)
        => MakeButton(text, UiTokens.BtnActionWidth, x, y);

    public static Button MakeWideButton(string text, int x, int y)
        => MakeButton(text, UiTokens.BtnWideWidth, x, y);

    /// <summary>
    /// Single-glyph flat button used inline with TextBox fields (× clear).
    /// Height matches TextBox row height — NOT action-button height — so it
    /// doesn't stick out below the field. FlatStyle=Flat + TabStop=false
    /// since these are accessory buttons; primary keyboard path is
    /// Enter/Space on the adjacent display field.
    /// </summary>
    public static Button MakeIconButton(string glyph, int x, int y)
    {
        var btn = new Button
        {
            Text = glyph,
            Width = UiTokens.BtnIconWidth,
            Height = UiTokens.BtnIconHeight,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            TabStop = false,
            Location = new Point(x, y),
        };
        btn.FlatAppearance.BorderColor = Theme.DividerColor;
        btn.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        btn.FlatAppearance.MouseDownBackColor = Theme.EditBgColor;
        return btn;
    }

    // ── Labels ──────────────────────────────────────────────────────────

    public static Label MakeHintLabel(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTokens.HintColor,
        Location = new Point(x, y),
    };

    public static Label MakeFieldLabel(string text, int x, int y, int width) => new()
    {
        Text = text,
        Width = width,
        AutoSize = false,
        ForeColor = UiTokens.LabelColor,
        Location = new Point(x, y),
    };

    // ── Primary factory helpers for read-only "clickable" textboxes used
    //    in compact hotkey and file-picker rows. ReadOnly + cursor=Hand so
    //    the user knows it's interactive; click/Enter/Space trigger the
    //    caller-provided action.

    /// <summary>
    /// Themed ToolTip. Win32 ToolTip ignores BackColor/ForeColor on the
    /// modern themed (visual-styles) paint path, so we go OwnerDraw and
    /// paint Bg/Fg/border ourselves. Without this the tip would always
    /// render with the OS-default light yellow chrome regardless of pin.
    ///
    /// Pre-allocates a Segoe UI 9pt font on the ToolTip's Tag so Popup's
    /// MeasureText and Draw's DrawString agree (font measurement diverges
    /// from rendered metrics if you pass two different Font instances).
    /// </summary>
    public static ToolTip MakeThemedToolTip()
    {
        var tip = new ToolTip
        {
            OwnerDraw = true,
            UseAnimation = false,
            UseFading = false,
            BackColor = Theme.BgColor,
            ForeColor = Theme.FgColor,
        };
        // Stash the font on Tag so Draw + Popup share one Font instance
        // (cheaper than a static — the ToolTip owns disposal).
        var font = new Font(UiTokens.PrimaryFont, 9f);
        tip.Tag = font;
        tip.Disposed += (_, _) => font.Dispose();

        const int padX = 8;
        const int padY = 5;

        tip.Popup += (s, e) =>
        {
            // PopupEventArgs doesn't carry the tip text directly — it gives
            // us the associated control and expects the size back. Pull the
            // text from the ToolTip itself for the measurement.
            string text = tip.GetToolTip(e.AssociatedControl) ?? "";
            var sz = TextRenderer.MeasureText(text, font);
            e.ToolTipSize = new Size(sz.Width + 2 * padX, sz.Height + 2 * padY);
        };
        tip.Draw += (s, e) =>
        {
            // Both GDI objects MUST be in `using` — Draw fires on every
            // tooltip repaint, and ToolTip.Show on a moving mouse can
            // repaint many times per second. The first port leaked one
            // SolidBrush handle per paint (verifier catch, 2026-05-17);
            // hovering tooltips for a long Settings session would
            // accumulate enough handles to silently exhaust the per-
            // process GDI cap and make tooltips stop painting entirely.
            using var bg = new SolidBrush(Theme.BgColor);
            using var border = new Pen(Theme.DividerColor);
            e.Graphics.FillRectangle(bg, e.Bounds);
            e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(
                e.Graphics, e.ToolTipText, font,
                new Rectangle(padX, padY,
                    e.Bounds.Width - 2 * padX, e.Bounds.Height - 2 * padY),
                Theme.FgColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix);
        };
        return tip;
    }

    public static TextBox MakeClickableDisplay(int width, int x, int y) => new()
    {
        ReadOnly = true,
        BackColor = Theme.EditBgColor,
        ForeColor = Theme.FgColor,
        BorderStyle = BorderStyle.FixedSingle,
        Cursor = Cursors.Hand,
        Width = width,
        Location = new Point(x, y),
    };
}
