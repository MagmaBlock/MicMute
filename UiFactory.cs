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

    public static Button MakeButton(string text, int width, int x, int y) => new()
    {
        Text = text,
        Width = width,
        Height = UiTokens.BtnHeight,
        AutoSize = false,
        UseVisualStyleBackColor = true,
        Location = new Point(x, y),
    };

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
    public static Button MakeIconButton(string glyph, int x, int y) => new()
    {
        Text = glyph,
        Width = UiTokens.BtnIconWidth,
        Height = UiTokens.BtnIconHeight,
        AutoSize = false,
        FlatStyle = FlatStyle.Flat,
        TabStop = false,
        Location = new Point(x, y),
    };

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

    public static TextBox MakeClickableDisplay(int width, int x, int y) => new()
    {
        ReadOnly = true,
        BackColor = Color.White,
        Cursor = Cursors.Hand,
        Width = width,
        Location = new Point(x, y),
    };
}
