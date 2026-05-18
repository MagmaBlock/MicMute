namespace MicMute;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

/// <summary>
/// Themed context-menu renderer for the tray right-click menu. Reads Theme.*
/// at first class load — Theme.Initialize MUST have fired in TrayApp's ctor
/// before this is constructed.
///
/// Cached GDI: paint fires on every mouse-move over a menu item, so per-paint
/// brush/pen allocation would burn GDI handles in 24/7 tray operation. The
/// brushes are static-readonly (process-lifetime); Win32 reclaims handles on
/// process exit so explicit disposal isn't needed for the cache itself.
/// </summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly SolidBrush BgBrush        = new(Theme.BgColor);
    private static readonly SolidBrush HighlightBrush = new(Theme.HighlightBg);
    private static readonly Pen        SeparatorPen   = new(Theme.DividerColor);
    // Checkmark glyph for any Checked submenu items (e.g. Mode → Toggle/PTT,
    // Mic Source → currently-selected device). 1.6f stroke with rounded caps
    // anti-aliases cleanly against the dark Bg; the default
    // ControlPaint.DrawMenuGlyph path uses SystemColors and is near-invisible
    // on the dark HighlightBg.
    private static readonly Pen        CheckPen       = new(Theme.FgColor, 1.6f)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
    };

    public MenuRenderer() : base(new ThemedColorTable()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);
        var brush = e.Item.Selected && e.Item.Enabled ? HighlightBrush : BgBrush;
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.FillRectangle(BgBrush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(SeparatorPen, rect);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Suppress the default white image-margin strip on the left.
        e.Graphics.FillRectangle(BgBrush, e.AffectedBounds);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        int y = bounds.Height / 2;
        e.Graphics.DrawLine(SeparatorPen, bounds.Left + 4, y, bounds.Right - 4, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        // Two-segment checkmark: short stroke down-right from upper-left,
        // long stroke up-right from there to the upper-right.
        int padX = r.Width / 4;
        int padY = r.Height / 4;
        var pLeft   = new Point(r.Left + padX,            r.Top + r.Height / 2);
        var pBottom = new Point(r.Left + r.Width / 2 - 1, r.Bottom - padY);
        var pRight  = new Point(r.Right - padX,           r.Top + padY);

        var prevSmooth = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLine(CheckPen, pLeft, pBottom);
        e.Graphics.DrawLine(CheckPen, pBottom, pRight);
        e.Graphics.SmoothingMode = prevSmooth;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Force our Fg/FgDisabled — the base renderer routes disabled items
        // through ControlPaint.DrawStringDisabled which IGNORES e.TextColor
        // and emboss-renders system grey (unreadable on dark Bg). Draw the
        // disabled path ourselves so the version header / mode label keeps
        // the themed colour.
        Color color = e.Item.Enabled ? Theme.FgColor : Theme.FgDisabledColor;
        e.TextColor = color;

        if (!e.Item.Enabled && !string.IsNullOrEmpty(e.Text))
        {
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
            return;
        }
        base.OnRenderItemText(e);
    }

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.DividerColor;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.HighlightBg;
        public override Color MenuStripGradientBegin => Theme.BgColor;
        public override Color MenuStripGradientEnd => Theme.BgColor;
        public override Color MenuItemSelectedGradientBegin => Theme.HighlightBg;
        public override Color MenuItemSelectedGradientEnd => Theme.HighlightBg;
        public override Color MenuItemPressedGradientBegin => Theme.HighlightBg;
        public override Color MenuItemPressedGradientEnd => Theme.HighlightBg;
        public override Color ImageMarginGradientBegin => Theme.BgColor;
        public override Color ImageMarginGradientMiddle => Theme.BgColor;
        public override Color ImageMarginGradientEnd => Theme.BgColor;
        public override Color ToolStripDropDownBackground => Theme.BgColor;
        public override Color SeparatorDark => Theme.DividerColor;
        public override Color SeparatorLight => Theme.DividerColor;
        public override Color CheckBackground => Theme.HighlightBg;
        public override Color CheckSelectedBackground => Theme.HighlightBg;
        public override Color CheckPressedBackground => Theme.HighlightBg;
    }
}
