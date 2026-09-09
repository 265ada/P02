using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace P02;

/// <summary>
/// The small readout that sits over the game.
///
/// Composed by hand into a per-pixel alpha window rather than painted into a
/// normal one. A colour-keyed window can only be fully opaque or fully gone,
/// which costs the soft edges on everything - and the key colour itself leaks
/// into anything antialiased against it, which is what turned the whole readout
/// magenta. Here every pixel carries its own alpha, so text can be haloed
/// rather than outlined, bars can sit on a bed that is dark without being
/// black, and there is no colour that must never be drawn.
/// </summary>
public sealed class OverlayForm : Form
{
    private const int Pad = 11;
    private const int RowH = 40;
    private const int BarH = 11;
    private const int NameW = 34;
    private const int ValueW = 54;

    private readonly System.Windows.Forms.Timer _fade = new();

    private string _lifeKey = "";
    private string _manaKey = "";

    private GlobeReading _life = new("Life", 0, false);
    private GlobeReading _mana = new("Mana", 0, false, "off");
    private double _lifeTrigger = 0.5;
    private double _manaTrigger = 0.3;
    private bool _armed;
    private bool _firing;
    private int _fired;
    private bool _inCombat;
    private string _detail = "";
    private string _alert = "";

    private Point _grabbedAt;
    private Point _wasAt;
    private bool _dragging;

    /// <summary>Raised when a drag finishes, so the position can be saved.</summary>
    public event Action? Moved;

    /// <summary>Raised when the menu changes something worth remembering.</summary>
    public event Action<bool, bool>? OptionsChanged;

    /// <summary>Raised when the menu asks for the default position back.</summary>
    public event Action? ResetAsked;

    /// <summary>Raised when this spot should be remembered for where the character is.</summary>
    public event Action? RememberAsked;

    /// <summary>Raised when one of the three saved places is chosen.</summary>
    public event Action<int>? SlotChosen;

    /// <summary>Raised when following the panels is switched on or off.</summary>
    public event Action<bool>? SlotAutoChanged;

    /// <summary>Whether the position is chosen by where the character is.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SlotAuto { get; set; }

    /// <summary>Which of the three is in use, for the tick in the menu.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Slot { get; set; }

    /// <summary>
    /// Set while the readout is anchored to something in the game, so a drag
    /// cannot quietly fight the thing that keeps putting it back.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Locked { get; set; }

    /// <summary>Whether the mouse passes straight through to the game.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (value) _ctrlWatch.Start(); else _ctrlWatch.Stop();
            ApplyClickThrough();
        }
    }

    private void ApplyClickThrough()
    {
        if (!IsHandleCreated) return;

        bool wanted = _clickThrough && !Native.CtrlHeld;
        if (wanted == _throughNow) return;

        _throughNow = wanted;
        Native.ClickThrough(Handle, wanted);
    }

    private bool _clickThrough;
    private bool _throughNow;

    /// <summary>
    /// Watches for Ctrl while clicks are passing through.
    ///
    /// A window that ignores the mouse cannot be told to stop ignoring it, so
    /// the escape has to come from outside the mouse. Holding Ctrl makes it
    /// solid again for as long as it is held, which is enough to right-click
    /// the menu and turn the whole thing off.
    /// </summary>
    private readonly System.Windows.Forms.Timer _ctrlWatch = new();

    public OverlayForm()
    {
        Text = "P02";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(292, 100);

        _ctrlWatch.Interval = 120;
        _ctrlWatch.Tick += (_, _) => ApplyClickThrough();

        _fade.Interval = 260;
        _fade.Tick += (_, _) => { _fade.Stop(); _firing = false; Render(); };

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || Locked) return;
            _dragging = true;
            _grabbedAt = Cursor.Position;
            _wasAt = Location;
            Cursor = Cursors.SizeAll;
        };
        MouseMove += (_, _) =>
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            Location = new Point(_wasAt.X + now.X - _grabbedAt.X,
                                 _wasAt.Y + now.Y - _grabbedAt.Y);
            Render();
        };
        // Ctrl and right-click, deliberately. The readout sits over a game
        // where every ordinary click belongs to the game, and a menu that opens
        // on a plain right-click would open by accident all evening.
        MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && ModifierKeys == Keys.Control)
            {
                ShowMenu(e.Location);
                return;
            }

            if (!_dragging) return;
            _dragging = false;
            Cursor = Cursors.Default;
            Moved?.Invoke();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW
                         | Native.WS_EX_NOACTIVATE;
            return p;
        }
    }

    /// <summary>Taking focus off the game to show a readout would be its own bug.</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>The keys each pool would press, shown beside its trigger.</summary>
    public void SetKeys(string life, string mana)
    {
        _lifeKey = life;
        _manaKey = mana;
    }

    public void Show(GlobeReading life, GlobeReading mana,
                     double lifeTrigger, double manaTrigger)
    {
        _life = life;
        _mana = mana;
        _lifeTrigger = lifeTrigger;
        _manaTrigger = manaTrigger;

        _detail = life.Note.Length > 0 ? life.Note : Source(life.TextRaw);

        FitHeight();
        Render();
    }

    /// <summary>
    /// A line under everything else, for something that cannot wait until the
    /// player next looks at the window - which, mid-map, is never.
    /// </summary>
    public void SetAlert(string text)
    {
        if (text == _alert) return;
        _alert = text;
        FitHeight();
        Render();
    }

    public void SetArmed(bool armed)
    {
        _armed = armed;
        Render();
    }

    public void SetFightCount(int fired, bool inCombat)
    {
        if (fired == _fired && inCombat == _inCombat) return;
        _fired = fired;
        _inCombat = inCombat;
        Render();
    }

    /// <summary>Blinks when a key is actually sent.</summary>
    public void Fired()
    {
        _firing = true;
        Render();
        _fade.Stop();
        _fade.Start();
    }

    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _lockItem;
    private ToolStripMenuItem? _throughItem;
    private ToolStripMenuItem[]? _slotItems;
    private ToolStripMenuItem? _autoItem;

    /// <summary>
    /// The options menu, built once and kept.
    ///
    /// It used to be built on each open and disposed from its own Closed event
    /// - which runs before the click that closed it has finished being
    /// handled, so choosing anything from it crashed on the disposed menu. A
    /// menu is cheap to keep and nothing is saved by throwing it away.
    /// </summary>
    private void ShowMenu(Point at)
    {
        if (_menu is null)
        {
            _lockItem = new ToolStripMenuItem("Lock position")
            {
                CheckOnClick = true,
                ToolTipText = "Stops it being dragged by accident.",
            };
            _lockItem.Click += (_, _) =>
            {
                Locked = _lockItem.Checked;
                OptionsChanged?.Invoke(Locked, ClickThrough);
            };

            _throughItem = new ToolStripMenuItem("Click through")
            {
                CheckOnClick = true,
                ToolTipText = "Clicks land in the game instead of on the readout. "
                              + "Hold Ctrl to make it solid again for as long as you hold "
                              + "it, which is how you get back to this menu.",
            };
            _throughItem.Click += (_, _) =>
            {
                ClickThrough = _throughItem.Checked;
                OptionsChanged?.Invoke(Locked, ClickThrough);
            };

            var remember = new ToolStripMenuItem("Remember this spot")
            {
                ToolTipText = "Saves where the readout is now, for wherever your "
                              + "character is standing now. Do it once with your panels "
                              + "closed, once with the inventory open, once with the "
                              + "character sheet open - it works out which is which.",
            };
            remember.Click += (_, _) => RememberAsked?.Invoke();

            var reset = new ToolStripMenuItem("Move back to the corner");
            reset.Click += (_, _) => ResetAsked?.Invoke();

            // Three places, because the game slides the character sideways
            // when a panel opens and one remembered spot cannot serve three
            // layouts. Choosing one moves there; dragging saves where you put
            // it, into whichever is chosen.
            _slotItems =
            [
                new ToolStripMenuItem("Left"),
                new ToolStripMenuItem("Middle"),
                new ToolStripMenuItem("Right"),
            ];

            for (int i = 0; i < _slotItems.Length; i++)
            {
                int which = i;
                _slotItems[i].ToolTipText =
                    "Go to this position. Drag the readout afterwards and it is "
                    + "remembered here.";
                _slotItems[i].Click += (_, _) => SlotChosen?.Invoke(which);
            }

            _autoItem = new ToolStripMenuItem("Follow the panels")
            {
                CheckOnClick = true,
                ToolTipText = "Opening your inventory slides the character one way and "
                              + "the character sheet the other. This picks Left, Middle "
                              + "or Right to match, from where he actually is.",
            };
            _autoItem.Click += (_, _) => SlotAutoChanged?.Invoke(_autoItem.Checked);

            var places = new ToolStripMenuItem("Position");
            places.DropDownItems.Add(_autoItem);
            places.DropDownItems.Add(new ToolStripSeparator());
            places.DropDownItems.AddRange(_slotItems);

            _menu = new ContextMenuStrip { ShowImageMargin = false };
            _menu.Items.Add(remember);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(places);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_lockItem);
            _menu.Items.Add(_throughItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(reset);
        }

        _lockItem!.Checked = Locked;
        _throughItem!.Checked = ClickThrough;
        for (int i = 0; i < _slotItems!.Length; i++) _slotItems[i].Checked = i == Slot;
        _autoItem!.Checked = SlotAuto;
        _menu.Show(this, at);
    }

    /// <summary>
    /// Which source decided, without repeating its numbers.
    ///
    /// The numbers are already drawn under the bar. Printing them again beside
    /// "disarmed" said the same thing twice in a readout whose whole point is
    /// being small.
    /// </summary>
    private static string Source(string raw) =>
        raw.Length == 0 ? "globe pixels"
        : raw.StartsWith("memory", StringComparison.Ordinal) ? "memory"
        : raw.StartsWith("numbers", StringComparison.Ordinal) ? "numbers"
        : raw;

    /// <summary>The bare current/maximum out of whatever the source reported.</summary>
    private static string Exact(string raw)
    {
        int slash = raw.IndexOf('/');
        if (slash < 0) return "";

        int from = slash;
        while (from > 0 && (char.IsDigit(raw[from - 1]) || raw[from - 1] == ',')) from--;

        int to = slash + 1;
        while (to < raw.Length && (char.IsDigit(raw[to]) || raw[to] == ',')) to++;

        return to - from > 3 ? raw[from..to] : "";
    }

    /// <summary>A globe that is not watched has nothing to say, so it takes no room.</summary>
    private bool ManaShown => !(_mana.Note == "off" && !_mana.Ok);

    private void FitHeight()
    {
        int h = Pad + RowH + (ManaShown ? RowH : 0) + 26
                + (_alert.Length > 0 ? 22 : 0) + Pad;

        // Wide enough for whatever it has to say. An alert is a sentence, not a
        // label, and it was being cut off mid-word by a window sized for the
        // bars above it - "move him, then t".
        int w = 292;
        if (_alert.Length > 0)
        {
            using var g = CreateGraphics();
            w = Math.Max(w, (int)Math.Ceiling(g.MeasureString(_alert, Theme.Small).Width)
                            + Pad * 2 + 14);
        }

        if (ClientSize.Height != h || ClientSize.Width != w)
            ClientSize = new Size(w, h);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle, false);
        _throughNow = false;
        ApplyClickThrough();
        if (_clickThrough) _ctrlWatch.Start();
        Render();
    }

    // A layered window never receives WM_PAINT for its contents - everything
    // arrives through UpdateLayeredWindow instead.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e) { }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Render();
    }

    /// <summary>
    /// Draws the whole readout and hands it to the window as one image.
    ///
    /// It draws straight into a DIB the window can be handed directly, in
    /// premultiplied form. UpdateLayeredWindow reads the colour channels as
    /// already multiplied by the alpha, so an ordinary ARGB bitmap - where they
    /// are not - comes out washed and colour-shifted, which is what left the
    /// whole readout pink even after the key colour was gone.
    /// </summary>
    private void Render()
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (!IsHandleCreated || w <= 0 || h <= 0) return;

        var info = new Native.BITMAPINFO();
        info.bmiHeader.biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>();
        info.bmiHeader.biWidth = w;
        info.bmiHeader.biHeight = -h;   // negative: top-down, like everything else here
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = 0;   // BI_RGB

        nint screen = Native.GetDC(0);
        nint mem = Native.CreateCompatibleDC(screen);
        nint dib = Native.CreateDIBSection(screen, ref info, 0, out nint bits, 0, 0);
        nint old = 0;
        try
        {
            if (dib == 0) return;
            old = Native.SelectObject(mem, dib);

            using (var frame = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits))
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Subpixel rendering has no ground to antialias against here and
                // leaves coloured fringes on the glyphs. Grey keeps the alpha honest.
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                Compose(g);
            }

            var size = new Native.SIZE { Cx = w, Cy = h };
            var src = new Native.POINT { X = 0, Y = 0 };
            var dst = new Native.POINT { X = Left, Y = Top };
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src,
                                       0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            if (old != 0) Native.SelectObject(mem, old);
            if (dib != 0) Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(0, screen);
        }
    }

    private void Compose(Graphics g)
    {
        // Barely there, but not nothing: a fully transparent pixel passes the
        // mouse through to the game, and the whole thing has to stay draggable.
        using (var ghost = new SolidBrush(Color.FromArgb(8, 0, 0, 0)))
        using (var path = Rounded(new Rectangle(0, 0, ClientSize.Width - 1,
                                                ClientSize.Height - 1), 10))
            g.FillPath(ghost, path);

        int y = Pad;
        Row(g, "Life", _life, _lifeTrigger, _lifeKey, y);
        y += RowH;

        if (ManaShown)
        {
            Row(g, "Mana", _mana, _manaTrigger, _manaKey, y);
            y += RowH;
        }

        Status(g, y + 4);

        if (_alert.Length > 0)
        {
            // Loud on purpose. Everything else here is meant to be glanceable
            // and ignorable; this one is meant to interrupt.
            var box = new Rectangle(Pad - 4, y + 26, ClientSize.Width - 2 * Pad + 8, 19);
            using (var back = new SolidBrush(Color.FromArgb(210, 150, 30, 26)))
            using (var path = Rounded(box, 5))
                g.FillPath(back, path);

            Glyph(g, _alert, Color.White, Theme.Small, box.X + 7, box.Y + 2);
        }
    }

    private void Row(Graphics g, string name, GlobeReading r, double trigger,
                     string key, int y)
    {
        Glyph(g, name, Theme.Dim, Theme.Small, Pad, y + 2);

        int barX = Pad + NameW;
        var bar = new Rectangle(barX, y + 4, ClientSize.Width - barX - ValueW - Pad, BarH);
        int radius = BarH / 2;

        // A bed under the bar, so an empty one reads as an empty bar rather
        // than as nothing at all.
        using (var bed = new SolidBrush(Color.FromArgb(150, 10, 11, 14)))
        using (var path = Rounded(bar, radius))
            g.FillPath(bed, path);

        bool held = r.Note.Length > 0;
        if (r.Ok && !held)
        {
            var inner = new Rectangle(bar.X + 1, bar.Y + 1, bar.Width - 2, bar.Height - 2);
            int w = (int)Math.Round(inner.Width * Math.Clamp(r.Fraction, 0, 1));
            if (w > 2)
            {
                var fill = r.Fraction < trigger ? Theme.Bad : Theme.Good;
                var lit = new Rectangle(inner.X, inner.Y, w, inner.Height);
                using var brush = new LinearGradientBrush(
                    new Rectangle(lit.X, lit.Y - 1, lit.Width, lit.Height + 2),
                    ControlPaint.Light(fill, 0.35f), fill, LinearGradientMode.Vertical);
                using var path = Rounded(lit, Math.Min(radius, w / 2));
                g.FillPath(brush, path);
            }

            // Where the trigger sits, so a glance says how much room is left
            // rather than only where the level is.
            int tx = inner.X + (int)(inner.Width * Math.Clamp(trigger, 0, 1));
            using var tick = new Pen(Color.FromArgb(200, 255, 255, 255));
            g.DrawLine(tick, tx, bar.Y - 1, tx, bar.Bottom + 1);
        }

        using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255)))
        using (var path = Rounded(bar, radius))
            g.DrawPath(edge, path);

        string right = !r.Ok ? (r.Note.Length > 0 ? r.Note : "--")
                     : held ? "held"
                     : $"{r.Fraction * 100:0}%";
        var colour = !r.Ok ? Theme.Dim
                   : held ? Theme.Warn
                   : r.Fraction < trigger ? Theme.Bad : Theme.Text;
        Glyph(g, right, colour, Theme.UiBold, bar.Right + 8, y + 1);

        // What the percentage is a percentage of, and what would happen. A bare
        // "60%" says none of that, and every argument about whether it was
        // reading correctly came down to not being able to see the numbers it
        // was reading.
        string exact = Exact(r.TextRaw);
        if (exact.Length > 0)
            Glyph(g, exact, Theme.Dim, Theme.Small, Pad + NameW, y + BarH + 5);

        if (key.Length > 0)
            Glyph(g, $"{trigger:P0} to {key}", Theme.Dim, Theme.Small,
                  bar.Right + 8, y + BarH + 5);
    }

    private void Status(Graphics g, int y)
    {
        Glyph(g, _armed ? "ARMED" : "disarmed",
              _armed ? Theme.Armed : Theme.Dim, Theme.UiBold, Pad, y);

        string detail = _detail.Length > 22 ? _detail[..22] : _detail;
        var detailColour = _life.Note.Length > 0 || !_life.FromText ? Theme.Warn : Theme.Dim;
        Glyph(g, detail, detailColour, Theme.Small, Pad + 62, y + 1);

        if (_dragging)
        {
            Glyph(g, $"{Left}, {Top}", Theme.Accent, Theme.Small,
                  Pad, y + 1);
            return;
        }

        if (_firing)
        {
            // A dot rather than a word: it is lit for a quarter of a second and
            // only has to be noticed, not read.
            using var glow = new SolidBrush(Color.FromArgb(70, Theme.Good));
            g.FillEllipse(glow, ClientSize.Width - Pad - 44, y + 1, 15, 15);
            using var dot = new SolidBrush(Theme.Good);
            g.FillEllipse(dot, ClientSize.Width - Pad - 41, y + 4, 9, 9);
        }

        if (_inCombat || _fired > 0)
        {
            // The one number you look for mid-fight, so it is sized to be read
            // at a glance from the corner of an eye rather than squinted at.
            string n = _fired.ToString();
            int w = (int)Math.Ceiling(g.MeasureString(n, Theme.Big).Width);
            Glyph(g, n, _inCombat ? Theme.Warn : Theme.Dim, Theme.Big,
                  ClientSize.Width - Pad - w, y - 4);
        }
    }

    /// <summary>
    /// Text with a soft dark halo behind it. The ground behind a word changes
    /// constantly over a game and no single colour stays readable against all
    /// of it; with real alpha the halo can be a spread shadow rather than four
    /// hard copies of the glyph.
    /// </summary>
    private static void Glyph(Graphics g, string s, Color colour, Font font, int x, int y)
    {
        if (s.Length == 0) return;

        using (var halo = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            for (int r = 2; r >= 1; r--)
                foreach (var (dx, dy) in new[] { (-r, 0), (r, 0), (0, -r), (0, r),
                                                 (-r, -r), (r, -r), (-r, r), (r, r) })
                    g.DrawString(s, font, halo, x + dx, y + dy);

        using var brush = new SolidBrush(colour);
        g.DrawString(s, font, brush, x, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        if (r.Width <= d || r.Height <= d)
        {
            path.AddEllipse(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fade.Dispose();
            _ctrlWatch.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
