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
    private readonly System.Windows.Forms.Timer _fade = new();

    private Point _dragFrom;
    private bool _dragging;

    public OverlayForm()
    {
        Text = "P02";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(210, 92);
        BackColor = Color.FromArgb(28, 28, 30);

        AddRow("Life", _life, _lifePct, 10);
        AddRow("Mana", _mana, _manaPct, 32);

        _armed.SetBounds(10, 60, 110, 22);
        _armed.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        Controls.Add(_armed);

        _fire.SetBounds(126, 60, 74, 22);
        _fire.TextAlign = ContentAlignment.MiddleRight;
        _fire.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _fire.ForeColor = Color.FromArgb(90, 90, 95);
        _fire.Text = "";
        Controls.Add(_fire);

        _fade.Interval = 260;
        _fade.Tick += (_, _) =>
        {
            _fade.Stop();
            _fire.Text = "";
        };

        // Draggable by any part of it: there is no title bar worth grabbing.
        foreach (Control c in new Control[] { this, _armed, _fire, _lifePct, _manaPct })
        {
            c.MouseDown += (_, e) => { _dragging = true; _dragFrom = e.Location; };
            c.MouseMove += (s, e) =>
            {
                if (!_dragging) return;
                var origin = s == this ? Point.Empty : ((Control)s!).Location;
                Location = new Point(Location.X + origin.X + e.X - _dragFrom.X,
                                     Location.Y + origin.Y + e.Y - _dragFrom.Y);
            };
            c.MouseUp += (_, _) => _dragging = false;
        }

        SetArmed(false);
    }

    private void AddRow(string name, LevelBar bar, Label pct, int y)
    {
        Controls.Add(new Label
        {
            Text = name,
            Bounds = new Rectangle(10, y, 34, 18),
            ForeColor = Color.FromArgb(190, 190, 195),
        });
        bar.SetBounds(48, y + 1, 108, 15);
        Controls.Add(bar);
        pct.SetBounds(160, y, 44, 18);
        pct.ForeColor = Color.FromArgb(220, 220, 225);
        Controls.Add(pct);
    }

    public void Show(GlobeReading life, GlobeReading mana, double lifeTrigger, double manaTrigger)
    {
        Apply(_life, _lifePct, life, lifeTrigger);
        Apply(_mana, _manaPct, mana, manaTrigger);
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
