namespace P02;

/// <summary>
/// A small always-on-top readout: both globes, whether it is armed, and a light
/// that blinks when a key goes out.
///
/// The full window is far too big to leave over a game, and minimising it means
/// flying blind. This is the part worth watching while playing.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly LevelBar _life = new();
    private readonly LevelBar _mana = new();
    private readonly Label _lifePct = new();
    private readonly Label _manaPct = new();
    private readonly Label _armed = new();
    private readonly Label _fire = new();
    private readonly Label _detail = new();
    private readonly System.Windows.Forms.Timer _fade = new();

    /// <summary>Raised when a drag finishes, so the position can be saved.</summary>
    public event Action? Moved;

    private readonly Label _manaCaption;
    private Point _grabbedAt;
    private Point _wasAt;
    private bool _dragging;
    private bool _manaShown = true;

    public OverlayForm()
    {
        Text = "P02";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(292, 84);

        // Only the readouts should be visible over the game. Everything painted
        // in this exact colour is punched through, so the window has no
        // background and no border at all - drag it by the text.
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        AddRow("Life", _life, _lifePct, 4);
        _manaCaption = AddRow("Mana", _mana, _manaPct, 26);

        _armed.SetBounds(10, 52, 78, 22);
        _armed.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _armed.BackColor = Color.Transparent;
        Controls.Add(_armed);

        _detail.SetBounds(92, 52, 128, 22);
        _detail.Font = new Font("Segoe UI", 9);
        _detail.ForeColor = Color.FromArgb(190, 190, 195);
        _detail.BackColor = Color.Transparent;
        Controls.Add(_detail);

        _fire.SetBounds(224, 52, 60, 22);
        _fire.TextAlign = ContentAlignment.MiddleRight;
        _fire.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _fire.ForeColor = Color.FromArgb(90, 90, 95);
        _fire.BackColor = Color.Transparent;
        _fire.Text = "";
        Controls.Add(_fire);

        _fade.Interval = 260;
        _fade.Tick += (_, _) =>
        {
            _fade.Stop();
            _fire.Text = "";
        };

        // Draggable by any part of it - there is no title bar to grab, and the
        // background is punched through, so only the readouts can be clicked.
        // Screen coordinates rather than control-relative ones: the previous
        // attempt added each control's own position to the movement, so the
        // window bolted across the desktop instead of following the pointer.
        // The row controls wire themselves as they are built; these are the
        // ones added afterwards.
        MakeDraggable(this);
        MakeDraggable(_armed);
        MakeDraggable(_detail);
        MakeDraggable(_fire);

        SetArmed(false);
    }

    private void MakeDraggable(Control c)
    {
        c.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _grabbedAt = Cursor.Position;
            _wasAt = Location;
            Cursor = Cursors.SizeAll;
        };

        c.MouseMove += (_, _) =>
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            Location = new Point(_wasAt.X + now.X - _grabbedAt.X,
                                 _wasAt.Y + now.Y - _grabbedAt.Y);
        };

        c.MouseUp += (_, _) =>
        {
            _dragging = false;
            Cursor = Cursors.Default;
            Moved?.Invoke();
        };
    }

    private Label AddRow(string name, LevelBar bar, Label pct, int y)
    {
        var caption = new Label
        {
            Text = name,
            Bounds = new Rectangle(10, y, 34, 18),
            ForeColor = Color.FromArgb(210, 210, 215),
            BackColor = Color.Transparent,
        };
        Controls.Add(caption);
        bar.SetBounds(48, y + 1, 186, 15);
        Controls.Add(bar);
        bar.BackColor = Color.Transparent;
        pct.SetBounds(238, y, 46, 18);
        pct.ForeColor = Color.FromArgb(220, 220, 225);
        pct.BackColor = Color.Transparent;
        Controls.Add(pct);
        MakeDraggable(caption);
        MakeDraggable(bar);
        MakeDraggable(pct);
        return caption;
    }

    /// <summary>
    /// A globe that is not watched has nothing to say, so it takes no room.
    /// </summary>
    private void ShowMana(bool on)
    {
        if (on == _manaShown) return;
        _manaShown = on;

        _manaCaption.Visible = on;
        _mana.Visible = on;
        _manaPct.Visible = on;

        int top = on ? 52 : 30;
        _armed.Top = top;
        _detail.Top = top;
        _fire.Top = top;
        ClientSize = new Size(ClientSize.Width, top + 32);
    }

    public void Show(GlobeReading life, GlobeReading mana, double lifeTrigger, double manaTrigger)
    {
        ShowMana(!(mana.Note == "off" && !mana.Ok));

        Apply(_life, _lifePct, life, lifeTrigger);
        if (_manaShown) Apply(_mana, _manaPct, mana, manaTrigger);

        // The actual numbers, not just a percentage: seeing 1,465/1,465 next to
        // the armed state is what tells you it is reading the right thing.
        string detail = life.TextRaw.Length > 0
            ? life.TextRaw.Replace("memory, life ", "").Replace("numbers, ", "")
            : life.Ok ? "globe pixels" : life.Note;
        if (detail.Length > 22) detail = detail[..22];
        _detail.Text = detail;
        _detail.ForeColor = life.FromText
            ? Color.FromArgb(190, 190, 195)
            : Color.FromArgb(230, 180, 90);
    }

    private static void Apply(LevelBar bar, Label pct, GlobeReading r, double trigger)
    {
        if (!r.Ok)
        {
            bar.Value = 0;
            bar.Below = false;
            pct.Text = r.Note.Length > 0 ? r.Note : "--";
            pct.ForeColor = Color.FromArgb(140, 140, 145);
            return;
        }
        bar.Value = r.Fraction;
        bar.Below = r.Fraction < trigger;
        pct.Text = $"{r.Fraction * 100:0} %";

        // Amber when the globe pixels are deciding: those follow energy shield
        // as well as life, since the shield is drawn over the same globe.
        pct.ForeColor = r.FromText
            ? Color.FromArgb(220, 220, 225)
            : Color.FromArgb(230, 180, 90);
    }

    public void SetArmed(bool armed)
    {
        _armed.Text = armed ? "ARMED" : "disarmed";
        _armed.ForeColor = armed ? Color.FromArgb(255, 90, 90) : Color.FromArgb(140, 140, 145);
    }

    /// <summary>Blinks when a key is actually sent.</summary>
    public void Fired()
    {
        _fire.Text = "FIRING";
        _fire.ForeColor = Color.FromArgb(120, 230, 130);
        _fade.Stop();
        _fade.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Never read as a globe by our own capture, and never a surprise in a
        // screenshot either - it is small and the user put it there.
        Native.ExcludeFromCapture(Handle, false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fade.Dispose();
        base.Dispose(disposing);
    }
}
