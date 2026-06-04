namespace MicMute;

using System.Drawing;

/// <summary>
/// Centralized UI design tokens for MicMute. Every dialog in the project
/// MUST reference these constants — no magic numbers in dialog code. If
/// drift happens (someone types a literal size in a new dialog), it's
/// immediately flaggable with `grep -n "= [0-9]" *.cs | grep -v UiTokens`.
/// If we want to change the look (e.g. BtnHeight 28 → 32), we edit one
/// constant and every control updates together.
/// </summary>
internal static class UiTokens
{
    // ── Buttons ─────────────────────────────────────────────────────────
    public const int BtnHeight              = 28;
    public const int BtnActionWidth         = 80;    // OK / Cancel / Apply
    public const int BtnWideWidth           = 120;   // "Type manually", "Change…"
    public const int BtnIconWidth           = 22;    // ⋯, ×, single glyph
    public const int BtnIconHeight          = 23;    // matches TextBox row height — icon buttons live inline with text fields, not in an action row
    public const int BtnGap                 = 6;

    // ── Dropdowns / numeric inputs ──────────────────────────────────────
    // Default ComboBox width for the right-anchored dropdowns on settings rows
    // (Mic-mode-on-startup, Theme). Both want the same width so the right edge
    // of the wrapping border panel lines up with the section-right anchor.
    public const int DropdownWidth          = 130;
    // OSD-duration NumericUpDown width — wide enough for a 5-digit value
    // (10000 ms cap) plus the spinner band.
    public const int OsdDurationWidth       = 55;

    // ── Animation timings ───────────────────────────────────────────────
    // Reject-animation tint duration: how long the hotkey-row display stays
    // red ("Bare modifiers need Push-to-Talk mode") before reverting to the
    // capture-mode yellow. Long enough to read, short enough not to feel like
    // a freeze.
    public const int RejectAnimDurationMs   = 1800;

    // ── Colors ──────────────────────────────────────────────────────────
    // Route through Theme so every dialog auto-themes with the user's
    // ThemeMode pin. Properties (not fields) so a read returns the active
    // palette — required because dialogs cache these into control
    // properties at construction time, AFTER Theme.Initialize fires in
    // TrayApp's ctor. If you add a hard-coded Color to a dialog, prefer
    // Theme.* directly so this list stays the single source of truth.
    public static Color TitleColor     => Theme.FgColor;
    public static Color HeaderColor    => Theme.AccentBlue;
    public static Color BodyColor      => Theme.FgColor;
    public static Color LabelColor     => Theme.FgColor;
    public static Color HintColor      => Theme.DimColor;
    public static Color GreyTextColor  => Theme.FgDisabledColor;
    public static Color FocusYellow    => Theme.FocusTint;
    public static Color ErrorTint      => Theme.ErrorTint;
    // Progress / status colors used by UpdateDialog. Green for success
    // accents (download progress bar fill), Warn for error status text.
    public static Color SuccessGreen   => Theme.AccentGreen;
    public static Color WarnOrange     => Theme.AccentWarn;

    // ── Fonts ───────────────────────────────────────────────────────────
    public const string PrimaryFont         = "Segoe UI";
    public const string SemiboldFont        = "Segoe UI Semibold";
    public const float  DialogFontSize      = 9.5f;
    public const float  SectionHeaderSize   = 9.5f;
    public const float  HelpTitleSize       = 13.5f;
    public const float  HelpHeaderSize      = 10.75f;
}
