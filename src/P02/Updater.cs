using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace P02;

/// <summary>
/// Checks the GitHub Releases feed, downloads the new exe, and swaps it in via
/// a throwaway batch file (a running exe cannot overwrite itself).
/// </summary>
internal static class Updater
{
    public const string Owner = "265ada";
    public const string Repo = "P02";
    private const string AssetName = "P02.exe";

    private static string? Token =>
        Environment.GetEnvironmentVariable("P02_GITHUB_TOKEN") is { Length: > 0 } env
            ? env
            : File.Exists(Path.Combine(AppConfig.Dir, "token.txt"))
                ? File.ReadAllText(Path.Combine(AppConfig.Dir, "token.txt")).Trim()
                : null;

    private static Version Current
    {
        get
        {
            var s = Assembly.GetExecutingAssembly()
                            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                            .InformationalVersion.Split('+')[0];
            return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
        }
    }

    private static HttpClient MakeClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"P02/{Current}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (Token is { Length: > 0 } t)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", t);
        return http;
    }

    public static async Task CheckAsync(IWin32Window owner, bool silent)
    {
        try
        {
            using var http = MakeClient();
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
            {
                string why = (int)resp.StatusCode == 404
                    ? "No release published yet, or this is a private repo and no token is set."
                    : $"GitHub said {(int)resp.StatusCode} {resp.ReasonPhrase}.";
                Log.Write($"update check failed: {why}");
                if (!silent) MessageBox.Show(owner, why, "Check for updates",
                                             MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            {
                Log.Write($"unparseable tag '{tag}'");
                return;
            }

            if (latest <= Current)
            {
                if (!silent)
                    MessageBox.Show(owner, $"You're on the latest version ({Current}).",
                                    "Check for updates", MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                return;
            }

            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            if (notes.Length > 600) notes = notes[..600] + "…";

            if (MessageBox.Show(owner,
                    $"Version {latest} is available (you have {Current}).\n\n{notes}\n\n" +
                    "Download and restart now?",
                    "Update available", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            string? assetUrl = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                if (string.Equals(a.GetProperty("name").GetString(), AssetName,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    // The api url serves private assets too, given a token.
                    assetUrl = a.GetProperty("url").GetString();
                    break;
                }
            }
            if (assetUrl is null)
            {
                MessageBox.Show(owner, $"That release has no {AssetName} attached.",
                                "Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string tmp = Path.Combine(Path.GetTempPath(), $"P02-{latest}.exe");
            using (var req = new HttpRequestMessage(HttpMethod.Get, assetUrl))
            {
                req.Headers.Accept.Clear();
                req.Headers.Accept.ParseAdd("application/octet-stream");
                using var dl = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                dl.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await dl.Content.CopyToAsync(fs);
            }

            SwapAndRestart(tmp);
        }
        catch (Exception ex)
        {
            Log.Write($"update failed: {ex.Message}");
            if (!silent)
                MessageBox.Show(owner, ex.Message, "Update failed",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void SwapAndRestart(string newExe)
    {
        string current = Environment.ProcessPath
                         ?? Path.Combine(AppContext.BaseDirectory, "P02.exe");
        string bat = Path.Combine(Path.GetTempPath(), "p02-update.cmd");

        // Wait for this process to release the file, swap, relaunch, self-delete.
        File.WriteAllText(bat, $"""
            @echo off
            setlocal
            set "target={current}"
            set "source={newExe}"
            for /l %%i in (1,1,40) do (
              copy /y "%source%" "%target%" >nul 2>&1 && goto done
              ping -n 2 127.0.0.1 >nul
            )
            echo Could not replace "%target%".
            pause
            goto cleanup
            :done
            del /q "%source%" >nul 2>&1
            start "" "%target%"
            :cleanup
            del /q "%~f0" >nul 2>&1
            """);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Application.Exit();
    }
}
