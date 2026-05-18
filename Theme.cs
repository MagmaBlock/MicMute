namespace MicMute;

/// <summary>
/// Theme palette for window chrome (Settings, Help, Update, OSD, tooltips,
/// context menu). Two palettes — Catppuccin Mocha (Dark) and Latte (Light) —
/// selected once at startup via <see cref="Initialize"/> based on the user's
/// saved <c>ThemeMode</c> preference (resolved through <see cref="ResolveIsDark"/>).
/// The active palette is then exposed via the static colour properties
/// (BgColor, FgColor, etc.) that all chrome surfaces read from.
///
/// Tray icons (mic_on / mic_off) are NOT driven by this — they always render
/// against the user's actual taskbar so they stay legible regardless of which
/// window-chrome pin is active.
///
/// Why static state instead of a flowing instance: OsdForm's GDI brush cache,
/// any custom ToolStripRenderer's color table, and other <c>static readonly</c>
/// field initializers capture Theme.* at first class load. They are write-once
/// per process. <see cref="Initialize"/> MUST be called before any of those
/// classes is first touched (currently: before <c>new OsdForm()</c> and
/// <c>BuildTrayMenu()</c> in TrayApp's constructor body, immediately after
/// <c>_config.Load()</c>). Changing theme at runtime is intentionally not
/// supported — restart-to-apply keeps the GDI caches honest.
/// </summary>
internal static class Theme
{
    private static bool _isDark = true;
    private static bool _initialized;

    /// <summary>True if the active palette is the dark (Mocha) one.</summary>
    public static bool IsDark => _isDark;

    /// <summary>
    /// Selects the active palette. Call once at startup, before any class
    /// with a <c>static readonly</c> Theme.* capture is first touched.
    /// </summary>
    public static void Initialize(bool isDark)
    {
        // Idempotent guard: a second call can't take effect because static
        // GDI caches (OsdForm, future renderers) captured Theme.* at first
        // class load. Log loudly rather than silently returning so a future
        // maintainer who tries to add live-theme-swap gets a Trace entry
        // pointing at the constraint instead of debugging a mixed palette.
        // Rule 12 (CLAUDE.md): fail loud, proactively.
        if (_initialized)
        {
            System.Diagnostics.Trace.WriteLine(
                $"MicMute: Theme.Initialize called twice (was isDark={_isDark}, requested {isDark}) — ignored. " +
                "Theme is restart-to-apply by design (static GDI caches captured at first class load).");
            return;
        }
        _isDark = isDark;
        _initialized = true;
    }

    /// <summary>
    /// Resolves the user's saved <c>ThemeMode</c> value ("System", "Dark",
    /// "Light", or empty) into a concrete is-dark decision. "System" (or any
    /// unrecognized value, including empty) reads the Windows
    /// <c>SystemUsesLightTheme</c> registry value.
    /// </summary>
    public static bool ResolveIsDark(string configValue)
    {
        if (string.Equals(configValue, "Dark", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(configValue, "Light", System.StringComparison.OrdinalIgnoreCase))
            return false;
        // "System" / null / empty / typo → follow OS.
        return !IsSystemLightTheme();
    }

    /// <summary>
    /// Apply themed window chrome to a form's titlebar. Call from
    /// <c>OnHandleCreated</c> AFTER <c>base.OnHandleCreated</c> so the HWND
    /// exists. Behaviour:
    /// <list type="bullet">
    /// <item>Dark mode → immersive dark titlebar (Win10 1809+ / Win11).</item>
    /// <item>Light mode → Win11 brand-blue titlebar via DWMWA_CAPTION_COLOR
    /// (35) + white titlebar text via DWMWA_TEXT_COLOR (36). On Win10 these
    /// attributes return E_INVALIDARG and the form gets the OS default
    /// white titlebar — graceful no-op, no functional impact.</item>
    /// </list>
    /// COLORREF packing: DWM expects 0x00BBGGRR (BGR), not RGB. Without
    /// the channel swap the titlebar reads as some unintended hue (Mocha
    /// red instead of brand blue, for example).
    /// </summary>
    public static void ApplyWindowChrome(System.Windows.Forms.Form form)
    {
        if (form == null || !form.IsHandleCreated) return;
        var handle = form.Handle;
        // Dark-mode dance: try modern attribute 20 first, fall back to
        // legacy attribute 19 on Win10 1809–19H2. Always run this even in
        // light mode so the attribute is explicitly set to 0 (= light
        // titlebar) — defends against a future where the OS default flips.
        int darkFlag = _isDark ? 1 : 0;
        int hr = NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref darkFlag, sizeof(int));
        if (hr != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1,
                ref darkFlag, sizeof(int));
        }

        // Both modes get a brand-blue titlebar — Light uses the original
        // HeaderColor #2255AA against the white form, Dark uses a brighter
        // #3B5BA8 against the Mocha-dark form (perceptually-matched lift).
        // Without this, dark mode kept the OS immersive titlebar which is
        // near-identical to the form Bg (#1E1E2E) — almost no visual
        // separation between chrome and body.
        System.Drawing.Color bg = TitlebarBg;
        System.Drawing.Color fg = System.Drawing.Color.White;
        // BGR-pack (DWMWA_CAPTION_COLOR expects COLORREF — B in low byte).
        int bgRef = (bg.B << 16) | (bg.G << 8) | bg.R;
        int fgRef = (fg.B << 16) | (fg.G << 8) | fg.R;
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_CAPTION_COLOR,
            ref bgRef, sizeof(int));
        NativeMethods.DwmSetWindowAttribute(
            handle, NativeMethods.DWMWA_TEXT_COLOR,
            ref fgRef, sizeof(int));
    }

    /// <summary>
    /// Reads <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\SystemUsesLightTheme</c>.
    /// Returns false on any failure (locked key, missing value, registry exception).
    /// </summary>
    public static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object val = key?.GetValue("SystemUsesLightTheme");
            return val is int i && i == 1;
        }
        catch (System.Exception ex)
        {
            // A registry read failure silently sends the user to dark mode
            // regardless of their actual OS theme. Trace so the unexpected
            // case (locked HKCU, AppContainer sandbox, group policy) is at
            // least diagnosable instead of "why is my Settings dialog dark
            // when my taskbar is light".
            System.Diagnostics.Trace.WriteLine(
                $"MicMute: Theme.IsSystemLightTheme registry read failed " +
                $"(err={ex.GetType().Name}: {ex.Message}) — assuming dark theme");
            return false;
        }
    }

    // ── Active palette accessors ───────────────────────────────────────────
    // Each property routes to the matching slot on the active palette. Form
    // code reads these once during construction (e.g. BackColor = Theme.BgColor)
    // and never again — no per-paint indirection cost.

    public static System.Drawing.Color BgColor         => _isDark ? Dark.Bg         : Light.Bg;
    public static System.Drawing.Color FgColor         => _isDark ? Dark.Fg         : Light.Fg;
    public static System.Drawing.Color FgDisabledColor => _isDark ? Dark.FgDisabled : Light.FgDisabled;
    public static System.Drawing.Color DimColor        => _isDark ? Dark.Dim        : Light.Dim;
    public static System.Drawing.Color HighlightBg     => _isDark ? Dark.HighlightBg: Light.HighlightBg;
    public static System.Drawing.Color EditBgColor     => _isDark ? Dark.EditBg     : Light.EditBg;
    public static System.Drawing.Color DividerColor    => _isDark ? Dark.Divider    : Light.Divider;
    /// <summary>
    /// Border colour for input-shaped controls (ComboBox wrappers, custom
    /// frame Panels) that need to read as a distinct edge against the form
    /// Bg. Distinct from <see cref="DividerColor"/> — Divider is a soft
    /// section-header rule that should be subtle; InputBorder must have
    /// enough contrast against EditBg + Bg that the control's footprint
    /// is unambiguous. In dark mode both can share Divider; in light mode
    /// Divider is too pale (#CCCCCC on white = invisible thin line) so
    /// InputBorder uses ~ControlDark (#A0A0A0), matching what
    /// BorderStyle.FixedSingle paints around the dialog's TextBoxes.
    /// </summary>
    public static System.Drawing.Color InputBorder    => _isDark ? Dark.Divider    : Light.InputBorder;
    public static System.Drawing.Color AccentBlue      => _isDark ? Dark.AccentBlue : Light.AccentBlue;

    /// <summary>
    /// Titlebar background colour for the DWM caption (Win11 22000+). Both
    /// modes use a saturated brand blue so the chrome reads as a distinct
    /// edge against the form body. Dark mode picks a slightly brighter
    /// navy than Light so it carries the same visual weight against the
    /// darker form Bg (Light bg is white → #2255AA pops; Dark bg is dark
    /// purple → needs a lighter blue for the same perceived contrast lift).
    /// Text colour is always white — readable on both.
    /// </summary>
    public static System.Drawing.Color TitlebarBg      => _isDark ? Dark.TitlebarBg : Light.TitlebarBg;
    public static System.Drawing.Color AccentGreen     => _isDark ? Dark.AccentGreen: Light.AccentGreen;
    public static System.Drawing.Color AccentWarn      => _isDark ? Dark.AccentWarn : Light.AccentWarn;

    /// <summary>
    /// Soft focus tint for inline capture (hotkey-row "recording" state). Dark
    /// uses a warm amber overlay against the dark Bg; Light keeps the canonical
    /// FFF8DC cornsilk that worked in the v2.1.x pre-theme dialog.
    /// </summary>
    public static System.Drawing.Color FocusTint       => _isDark ? Dark.FocusTint  : Light.FocusTint;

    /// <summary>
    /// Soft error tint for the inline "bare modifiers need PTT mode" rejection.
    /// Dark uses a desaturated maroon against the dark Bg; Light keeps the
    /// canonical FFF0F0 that worked in the v2.1.x pre-theme dialog.
    /// </summary>
    public static System.Drawing.Color ErrorTint       => _isDark ? Dark.ErrorTint  : Light.ErrorTint;

    /// <summary>
    /// CheckBox glyph + label colour. Dark uses pure white because the body Fg
    /// renders thin against the dark BG at 9pt through FlatStyle.Flat's
    /// grayscale-AA path; Light uses the normal Fg.
    /// </summary>
    public static System.Drawing.Color CheckboxFgColor => _isDark ? System.Drawing.Color.White : Light.Fg;

    // ── Dark palette — Catppuccin Mocha ────────────────────────────────────
    private static class Dark
    {
        public static readonly System.Drawing.Color Bg          = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x2E);
        public static readonly System.Drawing.Color Fg          = System.Drawing.Color.FromArgb(0xCD, 0xD6, 0xF3);
        public static readonly System.Drawing.Color FgDisabled  = System.Drawing.Color.FromArgb(0x80, 0x80, 0x95);
        public static readonly System.Drawing.Color Dim         = System.Drawing.Color.FromArgb(0xA0, 0xA0, 0xC0);
        public static readonly System.Drawing.Color HighlightBg = System.Drawing.Color.FromArgb(0x35, 0x35, 0x50);
        public static readonly System.Drawing.Color EditBg      = System.Drawing.Color.FromArgb(0x2A, 0x2A, 0x3E);
        public static readonly System.Drawing.Color Divider     = System.Drawing.Color.FromArgb(0x40, 0x40, 0x50);
        public static readonly System.Drawing.Color AccentBlue  = System.Drawing.Color.FromArgb(0x89, 0xB4, 0xFA);
        public static readonly System.Drawing.Color AccentGreen = System.Drawing.Color.FromArgb(0xA6, 0xE3, 0xA1);
        public static readonly System.Drawing.Color AccentWarn  = System.Drawing.Color.FromArgb(0xFA, 0xB3, 0x87);
        public static readonly System.Drawing.Color FocusTint   = System.Drawing.Color.FromArgb(0x4D, 0x42, 0x2A);
        public static readonly System.Drawing.Color ErrorTint   = System.Drawing.Color.FromArgb(0x4A, 0x29, 0x2E);
        // Brighter navy than Light.TitlebarBg — needed because perceived
        // contrast lift against the dark form Bg (#1E1E2E) is weaker than
        // the same blue against pure white. #3B5BA8 gives a ~80-unit channel
        // jump over Bg vs Light's #2255AA also gives ~80 against white.
        public static readonly System.Drawing.Color TitlebarBg  = System.Drawing.Color.FromArgb(0x3B, 0x5B, 0xA8);
    }

    // ── Light palette — restored "original MicMute" feel ───────────────────
    // The first port used Catppuccin Latte (cool grey-blue tint) which was
    // visually wrong against MicMute's v2.1.x identity (clean white with dark
    // text + the brand-blue accent #2255AA from the section headers). Nate's
    // feedback was "doesn't look like our original version and kind of hurts
    // the eyeballs." This palette restores the original tokens
    // (TitleColor/HeaderColor/BodyColor/LabelColor/HintColor) one-for-one and
    // fills in the slots Latte added (HighlightBg/EditBg/Divider) with neutral
    // greys that sit calmly against pure white.
    private static class Light
    {
        public static readonly System.Drawing.Color Bg          = System.Drawing.Color.White;                       // original BackColor
        public static readonly System.Drawing.Color Fg          = System.Drawing.Color.FromArgb(0x1E, 0x1E, 0x1E); // original BodyColor
        public static readonly System.Drawing.Color FgDisabled  = System.Drawing.Color.FromArgb(0xB0, 0xB0, 0xB0);
        public static readonly System.Drawing.Color Dim         = System.Drawing.Color.FromArgb(0x88, 0x88, 0x88); // original HintColor
        public static readonly System.Drawing.Color HighlightBg = System.Drawing.Color.FromArgb(0xE6, 0xE6, 0xE6); // neutral hover grey
        public static readonly System.Drawing.Color EditBg      = System.Drawing.Color.White;                       // text inputs stay pure white
        public static readonly System.Drawing.Color Divider     = System.Drawing.Color.FromArgb(0xCC, 0xCC, 0xCC); // light grey separator (section rules — subtle by design)
        // Was #A0A0A0 (ControlDark match) but a 1px line of that grey
        // sandwiched between a white combo interior and a white form Bg
        // failed to register at 1px on Nate's monitor in light mode while
        // dark mode (40-unit channel diff) was clearly visible. Dropped to
        // #606060 — closer to ControlDarkDark — for a 160-unit channel jump
        // against white. Still neutral, no chromatic tint.
        public static readonly System.Drawing.Color InputBorder = System.Drawing.Color.FromArgb(0x60, 0x60, 0x60);
        public static readonly System.Drawing.Color AccentBlue  = System.Drawing.Color.FromArgb(0x22, 0x55, 0xAA); // original HeaderColor (brand blue)
        public static readonly System.Drawing.Color AccentGreen = System.Drawing.Color.FromArgb(0x4C, 0xAF, 0x50); // original SuccessGreen
        public static readonly System.Drawing.Color AccentWarn  = System.Drawing.Color.FromArgb(0xFF, 0x98, 0x00); // original WarnOrange
        public static readonly System.Drawing.Color FocusTint   = System.Drawing.Color.FromArgb(0xFF, 0xF8, 0xDC); // original FocusYellow (cornsilk)
        public static readonly System.Drawing.Color ErrorTint   = System.Drawing.Color.FromArgb(0xFF, 0xF0, 0xF0); // original ErrorTint (mistyrose)
        // Light titlebar uses the brand blue verbatim — user-confirmed
        // "perfect" against the white form Bg.
        public static readonly System.Drawing.Color TitlebarBg  = System.Drawing.Color.FromArgb(0x22, 0x55, 0xAA);
    }
}
