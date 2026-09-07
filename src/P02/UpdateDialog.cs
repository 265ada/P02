using System.Diagnostics;
using System.Text.RegularExpressions;

namespace P02;

/// <summary>
/// The update prompt. A plain MessageBox cannot show a link, so release notes
/// arrived as raw markdown with a URL nobody could click.
/// </summary>
public sealed partial class UpdateDialog : Form
{
    [GeneratedRegex(@"https://github\.com/\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>Strips the markdown the API returns so the notes read as text.</summary>
    private static string Tidy(string notes)
    {
        string t = UrlPattern().Replace(notes, "");
        t = Regex.Replace(t, @"\*\*(.+?)\*\*", "$1");   // bold
        t = Regex.Replace(t, @"^#+\s*", "", RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*\*\s+", "  - ", RegexOptions.Multiline);
        t = Regex.Replace(t, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        t = Regex.Replace(t, @"(?m)^\s*Full Changelog\s*:?\s*$", "");
        return t.Trim();
    }

    public UpdateDialog(Version latest, Version current, string notes)
    {
        Text = "Update available";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 300);

        var head = new Label
        {
            Text = $"Version {latest} is available.  You have {current}.",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Bounds = new Rectangle(16, 16, 428, 24),
        };
        Controls.Add(head);

        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Bounds = new Rectangle(16, 46, 428, 170),
            Text = Tidy(notes),
        };
        body.Select(0, 0);
        Controls.Add(body);

        var match = UrlPattern().Match(notes);
        if (match.Success)
        {
            string url = match.Value.TrimEnd('.', ')', ',');
            var link = new LinkLabel
            {
                Text = "View the full changelog on GitHub",
                Bounds = new Rectangle(16, 224, 300, 20),
                AutoSize = false,
            };
            link.LinkClicked += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Write($"could not open changelog: {ex.Message}");
                }
            };
            Controls.Add(link);
        }

        var yes = new Button
        {
            Text = "Update and restart",
            Bounds = new Rectangle(228, 254, 140, 30),
            DialogResult = DialogResult.Yes,
        };
        var no = new Button
        {
            Text = "Not now",
            Bounds = new Rectangle(374, 254, 70, 30),
            DialogResult = DialogResult.No,
        };
        Controls.Add(yes);
        Controls.Add(no);
        AcceptButton = yes;
        CancelButton = no;
    }
}
