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
        t = Regex.Replace(t, @"(?m)^\s*Full Changelog\s*:?\s*$", "");

        // A multiline TextBox only breaks on CRLF. The API returns bare
        // newlines, so without this the whole changelog renders as one line.
        t = Regex.Replace(t, @"\r\n|\r|\n", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        t = t.Replace("\n", Environment.NewLine);
        return t.Trim();
    }

    public UpdateDialog(Version latest, Version current, string notes, int releases)
    {
        Text = "Update available";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 470);

        var head = new Label
        {
            // How far behind matters as much as what the newest one is: three
            // releases of changes is a different prospect from one.
            Text = releases > 1
                ? $"{releases} releases since yours: {current} to {latest}."
                : $"Version {latest} is available.  You have {current}.",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Bounds = new Rectangle(16, 16, 548, 24),
        };
        Controls.Add(head);

        var body = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Bounds = new Rectangle(16, 46, 548, 336),
            Text = Tidy(notes),
        };

        // A release whose notes are only a compare link used to leave this
        // panel completely blank, which reads as a broken dialog.
        if (body.Text.Length == 0)
        {
            body.Text = "No release notes were published for this version."
                      + Environment.NewLine + Environment.NewLine
                      + "The changelog link below shows what changed.";
            body.ForeColor = SystemColors.GrayText;
        }

        body.Select(0, 0);
        Controls.Add(body);

        var match = UrlPattern().Match(notes);
        if (match.Success)
        {
            string url = match.Value.TrimEnd('.', ')', ',');
            var link = new LinkLabel
            {
                Text = "View the full changelog on GitHub",
                Bounds = new Rectangle(16, 392, 340, 20),
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
            Text = releases > 1 ? "Update to newest" : "Update and restart",
            Bounds = new Rectangle(346, 422, 140, 30),
            DialogResult = DialogResult.Yes,
        };
        var no = new Button
        {
            Text = "Not now",
            Bounds = new Rectangle(494, 422, 70, 30),
            DialogResult = DialogResult.No,
        };
        Controls.Add(yes);
        Controls.Add(no);
        AcceptButton = yes;
        CancelButton = no;
    }
}
