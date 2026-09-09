namespace P02;

/// <summary>
/// What is happening while an update installs.
///
/// It is sixty megabytes over somebody's connection, and the app is paused
/// behind it with nothing to look at. A window that says nothing for half a
/// minute is indistinguishable from one that has hung - especially the
/// unattended kind, which nobody asked for and which arrives mid-game.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _bar = new();
    private readonly Label _what = new();
    private readonly Label _detail = new();

    public UpdateProgressForm(string version)
    {
        Text = $"Updating to {version}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 116);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        _what.SetBounds(16, 14, 388, 22);
        _what.Font = Theme.Big;
        _what.Text = $"Updating to {version}";
        Controls.Add(_what);

        _bar.SetBounds(16, 46, 388, 18);
        _bar.Style = ProgressBarStyle.Continuous;
        _bar.Maximum = 1000;
        Controls.Add(_bar);

        _detail.SetBounds(16, 72, 388, 34);
        _detail.ForeColor = Theme.Dim;
        _detail.Text = "Starting...";
        Controls.Add(_detail);
    }

    /// <summary>Taking focus mid-download would be its own annoyance.</summary>
    protected override bool ShowWithoutActivation => true;

    public void Step(string what, long done, long total)
    {
        void Apply()
        {
            _detail.Text = what;

            if (total > 0)
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = (int)Math.Clamp(done * 1000 / total, 0, 1000);
                _detail.Text = $"{what}  -  {done / 1_048_576.0:0.0} of "
                               + $"{total / 1_048_576.0:0.0} MB";
            }
            else
            {
                // No length to count against; say so by moving rather than by
                // sitting at zero, which reads as stuck.
                _bar.Style = ProgressBarStyle.Marquee;
            }
        }

        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }
}
