namespace MicMute;

/// <summary>
/// DPI-correct-by-construction layout kit for MicMute dialogs — the replacement for
/// the absolute-pixel <c>int y</c> + <c>new Point(x, y)</c> model that clipped at
/// 125%/150% (fonts grew, fixed positions didn't). Nothing here has a literal pixel
/// position: sections stack in a 1-column <see cref="TableLayoutPanel"/>, each section
/// is a bold header + 1px divider + a single-column body whose rows are self-contained
/// sub-containers, and everything AutoSizes to its (DPI-scaled) font. At any scale the
/// layout is *relational*, so 100% and 150% are proportionally identical with zero
/// pixel literals to mis-scale.
///
/// This is EQSwitch's CardLayout technique (proven, real-150%-verified) adapted to
/// MicMute's FLAT section aesthetic — no accent-bar cards, transparent bodies, the same
/// bold-header-plus-divider look the dialog already shipped. MicMute is PerMonitorV2, so
/// the framework scales control Bounds via PerformAutoScale; the only literals are field
/// WIDTHS, which scale too and never clip text vertically.
///
/// Fonts are shared statics (process-lifetime, like HelpWindow's) so no dialog has to
/// track them for disposal.
/// </summary>
internal static class UiLayout
{
    // Shared, process-lifetime fonts. Family/size only — theme-independent. No disposal.
    public static readonly Font BodyFont    = new(UiTokens.PrimaryFont, UiTokens.DialogFontSize);
    public static readonly Font HeaderFont  = new(UiTokens.PrimaryFont, UiTokens.SectionHeaderSize, FontStyle.Bold);

    /// <summary>Build a full-width row container (1-col, AutoSize) parented nowhere yet.</summary>
    private static TableLayoutPanel OneCol() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 1,
        RowCount = 0,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f) },
    };

    private static void AddRow(TableLayoutPanel stack, Control c, bool fixedHeight = false, bool fill = true)
    {
        int row = stack.RowCount;
        stack.RowCount = row + 1;
        stack.RowStyles.Add(new RowStyle(fixedHeight ? SizeType.Absolute : SizeType.AutoSize,
                                         fixedHeight ? c.Height : 0));
        // Containers fill the column width (and AutoSize their height); leaf controls
        // (header, checkbox, hint) hug the left so AutoSize governs their box, not Dock
        // (a Dock=Fill Label fights AutoSize and can collapse its height).
        if (fill) c.Dock = DockStyle.Fill;
        else c.Anchor = AnchorStyles.Left;
        stack.Controls.Add(c, 0, row);
    }

    /// <summary>
    /// The vertical stack of sections that fills a dialog. The dialog AutoSizes to this,
    /// so the window is exactly content-tall at every DPI (no fixed height, no dead band).
    /// </summary>
    internal sealed class Stack
    {
        private readonly TableLayoutPanel _root;

        /// <summary>The content panel. A dialog sizes its ClientSize to <c>Root.PreferredSize</c>
        /// (in OnShown, post-autoscale) so the window is exactly content-sized at every DPI.</summary>
        public TableLayoutPanel Root => _root;

        public Stack(Control parent)
        {
            // Dock=Top so the stack takes the dialog's (fixed) client width and AutoSizes
            // its height; the dialog reads that laid-out height and sets its own ClientSize
            // in OnShown. (Laid-out Height is ground truth — PreferredSize on a Percent-
            // column TableLayoutPanel underestimates and clips the lower rows.)
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = new Padding(16, 12, 16, 12),
                ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f) },
            };
            parent.Controls.Add(_root);
        }

        public Section NewSection(string title)
        {
            var s = new Section(title);
            AddRow(_root, s.Container);
            return s;
        }

        /// <summary>Add a full-width control as its own stack row (e.g. the footer button bar).</summary>
        public T Add<T>(T control) where T : Control
        {
            AddRow(_root, control);
            return control;
        }
    }

    /// <summary>
    /// One flat section: a bold header label, a 1px divider, and a 1-column body whose
    /// rows are self-contained containers. Build through <see cref="Stack.NewSection"/>.
    /// </summary>
    internal sealed class Section
    {
        public TableLayoutPanel Container { get; }
        private readonly TableLayoutPanel _body;

        internal Section(string title)
        {
            Container = OneCol();
            Container.Margin = new Padding(0, 0, 0, 10);   // gap below each section

            var header = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = UiTokens.LabelColor,
                Font = HeaderFont,
                Margin = new Padding(0, 0, 0, 2),
            };
            AddRow(Container, header, fill: false);

            var divider = new Panel
            {
                Height = 1,
                BackColor = Theme.DividerColor,
                Margin = new Padding(0, 0, 0, 6),
            };
            AddRow(Container, divider, fixedHeight: true);

            _body = OneCol();
            _body.Padding = new Padding(12, 0, 0, 0);   // indent body content under the header
            AddRow(Container, _body);
        }

        // ── Row builders (each appends a full-width row to the section body) ──────────

        /// <summary>label : field, where the field stretches to the section's right edge.</summary>
        public T LabelField<T>(string label, T field) where T : Control
        {
            var row = TwoCol(SizeType.AutoSize, SizeType.Percent);
            row.Controls.Add(RowLabel(label), 0, 0);
            field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            field.Margin = new Padding(0, 1, 0, 1);
            row.Controls.Add(field, 1, 0);
            AddRow(_body, row);
            return field;
        }

        /// <summary>
        /// A row with a left group that hugs the left edge and a right control that hugs
        /// the section's right edge — the "[checkbox] ......... [label][control]" pattern
        /// (OSD duration, Run-at-startup + Mic-mode, etc.). The spacer between is a Percent
        /// column so the right control always sits at the section edge at any DPI.
        /// </summary>
        public void EdgeRow(Control left, Control right)
        {
            var row = TwoCol(SizeType.Percent, SizeType.AutoSize);
            left.Anchor = AnchorStyles.Left;
            left.Margin = new Padding(0, 3, 0, 3);
            right.Anchor = AnchorStyles.Right;
            right.Margin = new Padding(0, 1, 0, 1);
            row.Controls.Add(left, 0, 0);
            row.Controls.Add(right, 1, 0);
            AddRow(_body, row);
        }

        /// <summary>Two equal-width cells side by side (the Hotkeys / Custom Files grids).</summary>
        public void Grid2(Control leftCell, Control rightCell)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 1, 0, 1),
                Padding = Padding.Empty,
                ColumnStyles = { new ColumnStyle(SizeType.Percent, 50f), new ColumnStyle(SizeType.Percent, 50f) },
                RowStyles = { new RowStyle(SizeType.AutoSize) },
            };
            leftCell.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            leftCell.Margin = new Padding(0, 0, 6, 0);
            rightCell.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            rightCell.Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(leftCell, 0, 0);
            row.Controls.Add(rightCell, 1, 0);
            AddRow(_body, row);
        }

        /// <summary>A checkbox spanning the section width.</summary>
        public CheckBox Check(CheckBox box)
        {
            box.AutoSize = true;
            box.Margin = new Padding(0, 3, 0, 3);
            AddRow(_body, box, fill: false);
            return box;
        }

        /// <summary>A dim hint/description spanning the section width. Use "\n" for hard line breaks.</summary>
        public Label Hint(string text, int indentPx = 0)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = UiTokens.HintColor,
                Margin = new Padding(indentPx, 0, 0, 6),
            };
            AddRow(_body, lbl, fill: false);
            return lbl;
        }

        /// <summary>Any full-width control as its own row.</summary>
        public T Full<T>(T control) where T : Control
        {
            control.Margin = new Padding(0, 1, 0, 1);
            AddRow(_body, control);
            return control;
        }

        private static TableLayoutPanel TwoCol(SizeType c0, SizeType c1) => new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 1, 0, 1),
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(c0, c0 == SizeType.Percent ? 100f : 0f),
                new ColumnStyle(c1, c1 == SizeType.Percent ? 100f : 0f),
            },
            RowStyles = { new RowStyle(SizeType.AutoSize) },
        };

        private static Label RowLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = UiTokens.LabelColor,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 10, 1),   // top:4 vertically centers against ~font-height fields
        };
    }

    /// <summary>
    /// A compact "[label] [display……] [×]" cell — the hotkey and custom-file rows. A 3-col
    /// sub-grid (label AutoSize, display fills, clear-button AutoSize) so the display box
    /// stretches and the × stays glued to the cell's right edge at any DPI.
    /// </summary>
    public static TableLayoutPanel CompactCell(string label, Control display, Control clear)
    {
        var cell = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnStyles =
            {
                new ColumnStyle(SizeType.AutoSize),
                new ColumnStyle(SizeType.Percent, 100f),
                new ColumnStyle(SizeType.AutoSize),
            },
            RowStyles = { new RowStyle(SizeType.AutoSize) },
        };
        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = UiTokens.LabelColor,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 6, 1),
        };
        display.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        display.Margin = new Padding(0, 1, 4, 1);
        clear.Anchor = AnchorStyles.Left;
        clear.Margin = new Padding(0, 1, 0, 1);
        cell.Controls.Add(lbl, 0, 0);
        cell.Controls.Add(display, 1, 0);
        cell.Controls.Add(clear, 2, 0);
        return cell;
    }

    /// <summary>
    /// A trailing "[label] [field]" group for the right side of an <see cref="Section.EdgeRow"/>
    /// — e.g. "Duration (ms): [nud]" or "Mic mode On Startup: [combo]". The label sits
    /// immediately left of the field and the whole group hugs the section's right edge.
    /// </summary>
    public static FlowLayoutPanel LabelBefore(string text, Control field, bool dim = false)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        var lbl = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = dim ? UiTokens.HintColor : UiTokens.LabelColor,
            Margin = new Padding(0, 4, 6, 0),
        };
        field.Margin = new Padding(0, 1, 0, 1);
        flow.Controls.Add(lbl);
        flow.Controls.Add(field);
        return flow;
    }

    /// <summary>
    /// Explicit, deterministic DPI scaling for a dialog built with this kit — run ONCE in
    /// OnLoad (after the handle exists, before the first paint). The dialogs use
    /// <c>AutoScaleMode.None</c> because <c>AutoScaleMode.Dpi</c> (under PerMonitorV2) scales
    /// point-fonts but NOT the pixel Margins/Paddings or fixed control Widths inside layout
    /// containers — and inconsistently between forms — leaving 150% proportionally tighter
    /// than 100% (numeric/combo text clipped, spacing compressed, some frames unscaled).
    ///
    /// Here every pixel literal is scaled by the device factor so 150% is EXACTLY 100% x 1.5
    /// (nothing reflows — it just scales, the Spotify/Word bar). AutoSize controls (labels,
    /// checkboxes) already grow via their point-fonts and are left alone. No-op at 100%
    /// (LogicalToDeviceUnits is the identity there), so it cannot regress the 100% layout.
    /// </summary>
    public static void ApplyDpi(Control root)
    {
        if (root.DeviceDpi == 96) return;   // identity at 100% — leave the layout untouched
        if (root is TableLayoutPanel or FlowLayoutPanel or Panel)
            root.Padding = ScalePad(root.Padding, root);
        if (root is TableLayoutPanel rootTlp)
            ScaleRowStyles(rootTlp, root);
        ScaleChildren(root, root);
    }

    private static void ScaleChildren(Control parent, Control dpi)
    {
        foreach (Control c in parent.Controls)
        {
            // Margins drive inter-row spacing — AutoScale leaves them at design px, so scale
            // every one (this is what keeps 150% spacing proportional to 100%).
            c.Margin = ScalePad(c.Margin, dpi);
            switch (c)
            {
                case NumericUpDown nud:
                    nud.Width = dpi.LogicalToDeviceUnits(nud.Width);
                    nud.MinimumSize = new Size(dpi.LogicalToDeviceUnits(nud.MinimumSize.Width),
                                               dpi.LogicalToDeviceUnits(nud.MinimumSize.Height));
                    break;
                case ComboBox cb:
                    cb.Width = dpi.LogicalToDeviceUnits(cb.Width);
                    break;
                case Button b when !b.AutoSize:
                    b.Size = new Size(dpi.LogicalToDeviceUnits(b.Width), dpi.LogicalToDeviceUnits(b.Height));
                    break;
                case TableLayoutPanel tlp:
                    tlp.Padding = ScalePad(tlp.Padding, dpi);
                    ScaleRowStyles(tlp, dpi);   // scale Absolute rows (e.g. the 1px section divider)
                    break;
                case FlowLayoutPanel:
                    c.Padding = ScalePad(c.Padding, dpi);
                    break;
                case Panel pnl:
                    pnl.Padding = ScalePad(pnl.Padding, dpi);
                    // A fixed (non-Auto, non-docked) Panel is a sized element (e.g. the
                    // progress-bar track) — scale its box. AutoSize/docked panels follow
                    // their content or parent and are left alone.
                    if (!pnl.AutoSize && pnl.Dock == DockStyle.None)
                        pnl.Size = new Size(dpi.LogicalToDeviceUnits(pnl.Width), dpi.LogicalToDeviceUnits(pnl.Height));
                    break;
            }
            // Recurse into containers, but not into a leaf field's own internals (a
            // NumericUpDown's edit/buttons, a ComboBox's edit, a TextBox).
            if (c is not (NumericUpDown or ComboBox or Button or TextBox))
                ScaleChildren(c, dpi);
        }
    }

    private static Padding ScalePad(Padding p, Control d) => new(
        d.LogicalToDeviceUnits(p.Left), d.LogicalToDeviceUnits(p.Top),
        d.LogicalToDeviceUnits(p.Right), d.LogicalToDeviceUnits(p.Bottom));

    // Scale Absolute row heights (the only fixed rows the kit creates are the 1px section
    // dividers) — ApplyDpi otherwise touches Margins/Paddings/control Sizes but not the TLP
    // RowStyles, which would leave dividers 1 device-px (too thin) at 150%. Percent/AutoSize
    // rows and all columns are relational and need no scaling.
    private static void ScaleRowStyles(TableLayoutPanel tlp, Control d)
    {
        foreach (RowStyle rs in tlp.RowStyles)
            if (rs.SizeType == SizeType.Absolute)
                rs.Height = d.LogicalToDeviceUnits((int)rs.Height);
    }
}

/// <summary>
/// Height-free, DPI-correct, UNPARENTED field factories for the layout-container rebuild —
/// MicMute's themed control palette without any <c>Size(w, h)</c> or <c>Location</c>. Heights
/// come from the font (single-line inputs auto-height at any DPI); only WIDTHS are literal,
/// and width never clips text vertically. Place each returned control with a
/// <see cref="UiLayout.Section"/> row method or a <see cref="UiLayout.CompactCell"/>.
/// </summary>
internal static class Fields
{
    /// <summary>A flat action button (Save / Apply / Cancel) at the shared action width.</summary>
    public static Button Action(string text) => Button(text, UiTokens.BtnActionWidth);

    /// <summary>A flat button of a given design width (scales under PerMonitorV2).</summary>
    public static Button Button(string text, int width)
    {
        var btn = new Button
        {
            Text = text,
            Width = width,
            Height = UiTokens.BtnHeight,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.FgColor,
            BackColor = Theme.BgColor,
            Margin = new Padding(0),
        };
        btn.FlatAppearance.BorderColor = Theme.DividerColor;
        btn.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        btn.FlatAppearance.MouseDownBackColor = Theme.EditBgColor;
        return btn;
    }

    /// <summary>
    /// The single-glyph × clear button used inline with a display box. Height matches the
    /// TextBox row (BtnIconHeight) so it doesn't stick out. Keeps UiFactory's disabled-glyph
    /// repaint so the × stays readable in dark mode whether enabled or not.
    /// </summary>
    public static Button Icon(string glyph)
    {
        var btn = new Button
        {
            Text = glyph,
            Width = UiTokens.BtnIconWidth,
            Height = UiTokens.BtnIconHeight,
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            TabStop = false,
            Margin = new Padding(0),
        };
        btn.FlatAppearance.BorderColor = Theme.IconButtonBorder;
        btn.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        btn.FlatAppearance.MouseDownBackColor = Theme.BgColor;
        btn.Paint += (s, e) =>
        {
            if (btn.Enabled) return;
            using var bgBrush = new SolidBrush(btn.BackColor);
            e.Graphics.FillRectangle(bgBrush, new Rectangle(1, 1, btn.Width - 2, btn.Height - 2));
            TextRenderer.DrawText(
                e.Graphics, btn.Text, btn.Font, btn.ClientRectangle, Theme.FgDisabledColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        return btn;
    }

    /// <summary>A read-only, hand-cursor display box (hotkey capture display / file picker).</summary>
    public static TextBox Display() => new()
    {
        ReadOnly = true,
        BackColor = Theme.EditBgColor,
        ForeColor = Theme.FgColor,
        BorderStyle = BorderStyle.FixedSingle,
        Cursor = Cursors.Hand,
    };

    /// <summary>A flat-themed checkbox whose label never clips (AutoSize).</summary>
    public static CheckBox Check(string text)
    {
        var chk = new CheckBox
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.CheckboxFgColor,
            BackColor = Theme.BgColor,
            FlatStyle = FlatStyle.Flat,
        };
        chk.FlatAppearance.BorderColor = Theme.DividerColor;
        chk.FlatAppearance.CheckedBackColor = Theme.HighlightBg;
        chk.FlatAppearance.MouseOverBackColor = Theme.HighlightBg;
        return chk;
    }

    /// <summary>A themed NumericUpDown with a MinimumSize height floor (spinner band can't squeeze the digits).</summary>
    public static NumericUpDown Numeric(int min, int max, int val, int increment, int width)
    {
        var nud = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Value = Math.Clamp(val, min, max),
            Width = width,
            MinimumSize = new Size(width, 26),
            TextAlign = HorizontalAlignment.Left,
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            BorderStyle = BorderStyle.FixedSingle,
        };
        // The inner UpDownButtons HWND paints its own background and ignores the parent
        // BackColor — match it to the digit area so the spinner band isn't system-grey.
        if (nud.Controls.Count > 0)
        {
            nud.Controls[0].BackColor = Theme.EditBgColor;
            nud.Controls[0].ForeColor = Theme.FgColor;
        }
        return nud;
    }

    /// <summary>
    /// A DropDownList ComboBox wrapped in a FixedSingle-bordered Panel — the wrapper paints
    /// the 1px border in the non-client area, which the flat ComboBox physically can't
    /// overpaint (the standing MicMute fix for the disappearing combo border in light mode).
    /// Returns the wrapper (place THAT in the layout); the combo is exposed via <paramref name="combo"/>.
    /// </summary>
    public static Panel Combo(out ComboBox combo, int width, params string[] items)
    {
        combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width,
            ForeColor = Theme.FgColor,
            BackColor = Theme.EditBgColor,
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
        };
        combo.Items.AddRange(items);
        var wrap = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.EditBgColor,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        wrap.Controls.Add(combo);
        return wrap;
    }

    /// <summary>A subtle navigation-style LinkLabel (GitHub / Help / Check for updates).</summary>
    public static LinkLabel Nav(string text)
    {
        return new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Theme.BgColor,
            LinkColor = Theme.AccentBlue,
            ActiveLinkColor = Theme.AccentBlue,
            VisitedLinkColor = Theme.AccentBlue,
            DisabledLinkColor = Theme.FgDisabledColor,
            Margin = new Padding(0, 4, 14, 0),
        };
    }
}

/// <summary>
/// Button-row bars. <see cref="Split"/> puts a left group hugging the left edge and a right
/// group hugging the right edge with a flexible spacer between (the footer: links left,
/// Save/Apply/Cancel right) — relational, so the right group stays glued to the edge at any DPI.
/// </summary>
internal static class Bars
{
    public static TableLayoutPanel Split(Control[] left, Control[] right)
    {
        var bar = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 6, 0, 0),
            Padding = Padding.Empty,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100f), new ColumnStyle(SizeType.AutoSize) },
            RowStyles = { new RowStyle(SizeType.AutoSize) },
        };
        bar.Controls.Add(Group(left, AnchorStyles.Left), 0, 0);
        bar.Controls.Add(Group(right, AnchorStyles.Right), 1, 0);
        return bar;
    }

    private static FlowLayoutPanel Group(Control[] items, AnchorStyles anchor)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Anchor = anchor,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        for (int i = 0; i < items.Length; i++)
        {
            items[i].Margin = new Padding(i == 0 ? 0 : UiTokens.BtnGap, 0, 0, 0);
            flow.Controls.Add(items[i]);
        }
        return flow;
    }
}
