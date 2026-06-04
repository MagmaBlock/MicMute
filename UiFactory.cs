namespace MicMute;

using System.Drawing;
using System.Windows.Forms;

/// <summary>
/// Shared, non-positional UI helper. The button/label/field factories that used to live
/// here were retired when the dialogs moved to the relational <see cref="UiLayout"/> /
/// <see cref="Fields"/> container kit (no more <c>x, y</c> arguments); only the owner-draw
/// themed ToolTip — which has no layout position — remains.
/// </summary>
internal static class UiFactory
{
    /// <summary>
    /// Themed ToolTip. Win32 ToolTip ignores BackColor/ForeColor on the modern themed
    /// (visual-styles) paint path, so we go OwnerDraw and paint Bg/Fg/border ourselves.
    /// Without this the tip would always render with the OS-default light yellow chrome.
    ///
    /// Pre-allocates a Segoe UI 9pt font on the ToolTip's Tag so Popup's MeasureText and
    /// Draw's DrawString agree (font measurement diverges from rendered metrics if you
    /// pass two different Font instances).
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
        // Stash the font on Tag so Draw + Popup share one Font instance (cheaper than a
        // static — the ToolTip owns disposal).
        var font = new Font(UiTokens.PrimaryFont, 9f);
        tip.Tag = font;
        tip.Disposed += (_, _) => font.Dispose();

        const int padX = 8;
        const int padY = 5;

        tip.Popup += (s, e) =>
        {
            // PopupEventArgs doesn't carry the tip text directly — pull it from the
            // ToolTip itself for the measurement.
            string text = tip.GetToolTip(e.AssociatedControl) ?? "";
            var sz = TextRenderer.MeasureText(text, font);
            e.ToolTipSize = new Size(sz.Width + 2 * padX, sz.Height + 2 * padY);
        };
        tip.Draw += (s, e) =>
        {
            // Both GDI objects MUST be in `using` — Draw fires on every tooltip repaint,
            // and ToolTip.Show on a moving mouse can repaint many times per second. The
            // first port leaked one SolidBrush handle per paint (verifier catch,
            // 2026-05-17); a long Settings session would exhaust the per-process GDI cap.
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
}
