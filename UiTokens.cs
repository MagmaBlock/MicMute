namespace MicMute;

using System.Drawing;

/// <summary>
/// Centralized UI design tokens for MicMute. Every dialog in the project
/// MUST reference these constants — no magic numbers in dialog code. If
/// drift happens (someone types a literal size in a new dialog), it's
/// immediately flaggable with `grep -n "= [0-9]" *.cs | grep -v UiTokens`.
/// If we want to change the look (e.g. BtnHeight 28 → 32), we edit one
/// constant and every control updates together.
///
// nosemgrep: ai.generic.detect-generic-ai-anthprop.detect-generic-ai-anthprop -- detector fires on "_.claude/" path references; these are cross-project doc links, not security signals
/// See `_.claude/_templates/snippets/csharp/winforms-controls-canonical.md`
// nosemgrep: ai.generic.detect-generic-ai-anthprop.detect-generic-ai-anthprop -- same as above
/// for the cross-project pattern + `_.claude/_templates/lessons-learned/
/// principles/content-aware-dialog-sizing.md` for the width rule.
/// </summary>
internal static class UiTokens
{
    // ── Dialog ──────────────────────────────────────────────────────────
    public const int DialogMargin           = 16;
    public const int SettingsDialogWidth    = 520;
    public const int SettingsSectionRight   = 504;   // SettingsDialogWidth - DialogMargin
    public const int SectionSeparatorWidth  = 488;   // SettingsSectionRight - DialogMargin

    // ── Row / cell spacing ──────────────────────────────────────────────
    public const int RowGap                 = 4;
    public const int ColumnGap              = 12;
    public const int Indent                 = 28;

    // ── Buttons ─────────────────────────────────────────────────────────
    public const int BtnHeight              = 28;
    public const int BtnActionWidth         = 80;    // OK / Cancel / Apply
    public const int BtnWideWidth           = 120;   // "Type manually", "Change…"
    public const int BtnIconWidth           = 22;    // ⋯, ×, single glyph
    public const int BtnIconHeight          = 23;    // matches TextBox row height — icon buttons live inline with text fields, not in an action row
    public const int BtnGap                 = 6;

    // ── Compact grid cells (hotkeys + custom files) ─────────────────────
    public const int CellLabelWidth         = 76;
    public const int CellFileLabelWidth     = 88;    // "Unmute sound" needs a touch more

    // ── Colors ──────────────────────────────────────────────────────────
    public static readonly Color TitleColor     = Color.FromArgb(0x11, 0x11, 0x11);
    public static readonly Color HeaderColor    = Color.FromArgb(0x22, 0x55, 0xAA);
    public static readonly Color BodyColor      = Color.FromArgb(0x1E, 0x1E, 0x1E);
    public static readonly Color LabelColor     = Color.FromArgb(0x44, 0x44, 0x44);
    public static readonly Color HintColor      = Color.FromArgb(0x88, 0x88, 0x88);
    public static readonly Color GreyTextColor  = Color.FromArgb(0x99, 0x99, 0x99);
    public static readonly Color FocusYellow    = Color.FromArgb(0xFF, 0xF8, 0xDC);
    public static readonly Color ErrorTint      = Color.FromArgb(0xFF, 0xF0, 0xF0);
    // Progress / status colors used by UpdateDialog (A5-F05).
    public static readonly Color SuccessGreen   = Color.FromArgb(76, 175, 80);
    public static readonly Color WarnOrange     = Color.FromArgb(255, 152, 0);

    // ── Fonts ───────────────────────────────────────────────────────────
    public const string PrimaryFont         = "Segoe UI";
    public const string SemiboldFont        = "Segoe UI Semibold";
    public const float  DialogFontSize      = 9.5f;
    public const float  ModalFontSize       = 10f;
    public const float  SectionHeaderSize   = 9.5f;
    public const float  HelpTitleSize       = 13.5f;
    public const float  HelpHeaderSize      = 10.75f;
}
