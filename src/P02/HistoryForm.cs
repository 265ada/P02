namespace P02;

/// <summary>
/// Every release and what it was for.
///
/// The changelog only ever appeared while an update was waiting, and only
/// covered the hops between two versions. Once installed, a release had no way
/// of telling anybody what it had changed - which, after fifty of them, is most
/// of what there is to know about this.
/// </summary>
internal sealed class HistoryForm : Form
{
    private readonly ListBox _list = new();
    private readonly TextBox _notes = new();
    private List<Updater.Release> _releases = [];

    public HistoryForm(string current)
    {
        Text = "What has changed";
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(560, 360);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;

        _list.Dock = DockStyle.Left;
        _list.Width = 300;
        _list.BorderStyle = BorderStyle.None;
        _list.BackColor = Theme.Card;
        _list.ForeColor = Theme.Text;
        _list.Font = Theme.Ui;
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => ShowNotes();

        _notes.Dock = DockStyle.Fill;
        _notes.Multiline = true;
        _notes.ReadOnly = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        _notes.BorderStyle = BorderStyle.None;
        _notes.BackColor = Theme.Bg;
        _notes.ForeColor = Theme.Text;
        _notes.Font = new Font("Consolas", 9.5f);

        var split = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        split.Controls.Add(_notes);
        split.Controls.Add(_list);

        var close = new Button
        {
            Text = "Close",
            Dock = DockStyle.Bottom,
            Height = 32,
            DialogResult = DialogResult.OK,
        };
        Theme.Primary(close, Theme.Accent);

        Controls.Add(split);
        Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;

        _list.Items.Add("Fetching...");
        _ = Load(current);
    }

    private async Task Load(string current)
    {
        try
        {
            _releases = await Updater.HistoryAsync();
        }
        catch (Exception ex)
        {
            _notes.Text = $"Could not fetch the history: {ex.Message}";
            _list.Items.Clear();
            return;
        }

        _list.Items.Clear();
        if (_releases.Count == 0)
        {
            _notes.Text = "No releases came back.";
            return;
        }

        foreach (var r in _releases)
        {
            // The one you are running is worth marking, because "which of these
            // do I already have" is the first thing anybody asks of a list
            // like this.
            string here = r.Tag.TrimStart('v', 'V') == current ? "  <- you are here" : "";
            _list.Items.Add($"{r.Tag}   {r.When:d MMM}{here}");
        }

        _list.SelectedIndex = 0;
    }

    private void ShowNotes()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _releases.Count) return;

        var r = _releases[i];
        _notes.Text = ($"{r.Tag}   -   {r.When:d MMMM yyyy}" + Environment.NewLine
                       + Environment.NewLine + r.Summary + Environment.NewLine
                       + Environment.NewLine + new string('-', 60) + Environment.NewLine
                       + Environment.NewLine + r.Notes)
                      .Replace("\n", Environment.NewLine);
        _notes.Select(0, 0);
    }
}
