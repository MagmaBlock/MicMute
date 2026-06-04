#if DEBUG
using System.Drawing.Imaging;

namespace MicMute;

/// <summary>
/// DEBUG-only offscreen render harness for DPI verification — Phase 0 of the
/// scale-independence rebuild (see docs/superpowers/plans/2026-06-04-dpi-scaling-rebuild.md).
///
/// Renders each user-facing surface to a PNG at a CHOSEN monitor's DPI, plus a
/// manifest of DeviceDpi + ClientSize, so a 100% capture (a 96-DPI monitor) and a
/// 150% capture (a 144-DPI monitor — Tiny11, or a 150% host display) can be diffed.
/// The render is ground truth — it beats any static "this will/won't clip" reasoning.
///
/// Under PerMonitorV2 a window's DeviceDpi reflects whichever monitor it sits on, so
/// the harness probes every screen, logs its DPI, and renders on the one matching
/// --dpi (default 96). Pinning to a known monitor avoids the popup-reposition trap
/// where a ContextMenuStrip shown off-screen hops to a differently-scaled display.
///
/// Never compiled into Release: the whole file is under #if DEBUG and the only
/// production touch-point is a guarded dispatch in Program.Main.
///
/// Usage (run the Debug build):
///   MicMute.exe --diag-render-form &lt;Name|all&gt; --out &lt;dir&gt; [--dpi 96|144] [--theme dark|light]
/// Surfaces: Settings, SettingsPtt, Update, Help, Osd, Menu
///
/// Output is GUI-subsystem-friendly: no attached console, so all progress goes to
/// &lt;out&gt;/diag-log.txt and the geometry table to &lt;out&gt;/manifest.csv.
/// </summary>
internal static class DiagRender
{
    private static readonly System.Text.StringBuilder s_log = new();

    public static int Run(string[] args)
    {
        string target = ArgVal(args, "--diag-render-form") ?? "all";
        string outDir = ArgVal(args, "--out") ?? Path.Combine(Path.GetTempPath(), "micmute-diag");
        string theme = ArgVal(args, "--theme");                       // null = config default (System)
        int wantDpi = int.TryParse(ArgVal(args, "--dpi"), out int d) ? d : 96;

        // DPI mode + visual styles + ambient font come from the csproj-driven
        // initializer, exactly as the real app does in Program.RunApp. MUST run
        // before any Form is realized or the process DPI awareness isn't set.
        ApplicationConfiguration.Initialize();

        bool isDark = theme?.ToLowerInvariant() switch
        {
            "dark"  => true,
            "light" => false,
            _       => Theme.ResolveIsDark(new Config().ThemeMode),   // default → System
        };
        Theme.Initialize(isDark);

        Directory.CreateDirectory(outDir);
        Note($"theme={(isDark ? "dark" : "light")} wantDpi={wantDpi} out={outDir}");

        var screen = PickScreen(wantDpi);

        var manifest = new System.Text.StringBuilder();
        manifest.AppendLine("surface,deviceDpi,clientW,clientH,winW,winH");
        foreach (var name in ResolveTargets(target))
        {
            try
            {
                var (dpi, client, win) = RenderOne(name, outDir, screen);
                manifest.AppendLine($"{name},{dpi},{client.Width},{client.Height},{win.Width},{win.Height}");
                string flag = dpi == wantDpi ? "" : $"  *** DPI {dpi} != requested {wantDpi} ***";
                Note($"{name}: dpi={dpi} client={client.Width}x{client.Height} win={win.Width}x{win.Height}{flag}");
            }
            catch (Exception ex)
            {
                Note($"{name}: FAILED {ex.GetType().Name}: {ex.Message}");
            }
        }

        File.WriteAllText(Path.Combine(outDir, "manifest.csv"), manifest.ToString());
        File.WriteAllText(Path.Combine(outDir, "diag-log.txt"), s_log.ToString());
        return 0;
    }

    /// <summary>
    /// Probe every monitor's effective DPI (a 1×1 invisible form realizes its handle
    /// on that monitor, so DeviceDpi reports the monitor's scale) and return the first
    /// screen matching <paramref name="wantDpi"/>. Falls back to primary with a loud log.
    /// </summary>
    private static Screen PickScreen(int wantDpi)
    {
        Screen chosen = null;
        foreach (var s in Screen.AllScreens)
        {
            int dpi = ProbeDpi(s);
            bool pick = dpi == wantDpi && chosen == null;
            if (pick) chosen = s;
            Note($"screen {s.DeviceName} bounds={s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y} " +
                 $"primary={s.Primary} dpi={dpi}{(pick ? "  <= chosen" : "")}");
        }
        if (chosen == null)
        {
            chosen = Screen.PrimaryScreen;
            Note($"NO monitor at {wantDpi} DPI — falling back to primary ({ProbeDpi(chosen)} DPI). " +
                 "Captures will NOT be at the requested scale.");
        }
        return chosen;
    }

    private static int ProbeDpi(Screen s)
    {
        using var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            Size = new Size(1, 1),
            Opacity = 0,
            Location = new Point(s.WorkingArea.X + 20, s.WorkingArea.Y + 20),
        };
        f.Show();
        Pump();
        int dpi = f.DeviceDpi;
        f.Hide();
        return dpi;
    }

    private static IEnumerable<string> ResolveTargets(string target) =>
        target.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Settings", "SettingsPtt", "Update", "Help", "Osd", "Menu" }
            : new[] { target };

    private static (int dpi, Size client, Size win) RenderOne(string name, string outDir, Screen screen) => name.ToLowerInvariant() switch
    {
        "menu" => RenderMenu(outDir, screen),
        "osd"  => RenderOsd(outDir),
        _      => RenderForm(name, Build(name), outDir, screen),
    };

    private static Form Build(string name) => name switch
    {
        "Settings"    => new SettingsDialog(DefaultConfig(ptt: false), () => { }),
        "SettingsPtt" => new SettingsDialog(DefaultConfig(ptt: true), () => { }),
        "Update"      => new UpdateDialog(),
        // HelpWindow has a private ctor (singleton). Construct directly via the
        // nonPublic Activator overload so the harness needs no production-side
        // factory — zero edits to HelpWindow.cs.
        "Help"        => (Form)Activator.CreateInstance(typeof(HelpWindow), nonPublic: true),
        _ => throw new ArgumentException($"unknown surface '{name}'"),
    };

    private static Config DefaultConfig(bool ptt)
    {
        // Field defaults only — no Load(), so the render is deterministic and
        // independent of any MicMute.ini sitting next to the dev exe.
        var c = new Config();
        if (ptt) c.Mode = "push-to-talk";
        return c;
    }

    private static (int, Size, Size) RenderForm(string name, Form form, string outDir, Screen screen)
    {
        using (form)
        {
            // Realize the handle ON the chosen monitor (Location set before Show) so
            // DeviceDpi reflects that monitor's scale. Opacity=0 avoids a visible flash;
            // DrawToBitmap re-renders the control tree regardless of on-screen opacity.
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Opacity = 0;
            form.Location = new Point(screen.WorkingArea.X + 20, screen.WorkingArea.Y + 20);
            form.Show();
            Pump();
            var result = Capture(form, Path.Combine(outDir, $"{name}.png"));
            form.Hide();
            return result;
        }
    }

    private static (int, Size, Size) RenderOsd(string outDir)
    {
        // OsdForm self-positions on Screen.PrimaryScreen (by design), so its capture
        // DPI is the primary monitor's — render it where it lives.
        using var osd = new OsdForm();
        osd.ShowOsd(muted: true, durationMs: 600_000);   // 10 min so auto-dismiss can't fire mid-capture
        Pump();
        var result = Capture(osd, Path.Combine(outDir, "Osd.png"));
        osd.HidePersistent();
        return result;
    }

    private static (int, Size, Size) RenderMenu(string outDir, Screen screen)
    {
        // Representative tray menu exercising every MenuRenderer owner-draw path:
        // the blue title wash (Tag sentinel), separators, a checked submenu item,
        // and plain items. Item heights come from the framework (font-derived), so
        // this verifies the renderer's own geometry literals at DPI. Shown at the
        // chosen monitor's work-area origin so it doesn't hop to another display.
        using var menu = new ContextMenuStrip { Renderer = new MenuRenderer() };
        menu.Items.Add(new ToolStripMenuItem("MicMute") { Tag = MenuRenderer.TitleItemTag, Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Toggle Mute"));
        var mode = new ToolStripMenuItem("Mode");
        mode.DropDownItems.Add(new ToolStripMenuItem("Toggle") { Checked = true });
        mode.DropDownItems.Add(new ToolStripMenuItem("Push-to-Talk"));
        menu.Items.Add(mode);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…"));
        menu.Items.Add(new ToolStripMenuItem("Exit"));

        menu.Show(new Point(screen.WorkingArea.X + 20, screen.WorkingArea.Y + 20));
        Pump();
        var result = Capture(menu, Path.Combine(outDir, "Menu.png"));
        menu.Close();
        return result;
    }

    private static (int, Size, Size) Capture(Control c, string pngPath)
    {
        var client = c.ClientSize;
        using var bmp = new Bitmap(Math.Max(1, client.Width), Math.Max(1, client.Height));
        c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        bmp.Save(pngPath, ImageFormat.Png);
        return (c.DeviceDpi, client, c.Size);
    }

    private static void Pump()
    {
        // Let handle creation, PerformAutoScale, layout, and the first paint settle.
        for (int i = 0; i < 8; i++) Application.DoEvents();
    }

    private static string ArgVal(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
    }

    private static void Note(string line) => s_log.AppendLine("[diag] " + line);
}
#endif
