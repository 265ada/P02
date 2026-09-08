using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
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

    private static string TokenPath => Path.Combine(AppConfig.Dir, "token.txt");

    /// <summary>
    /// Optional. The repository is public, so updates work with no token; this
    /// stays only so a private fork keeps working.
    /// </summary>
    private static string? Token
    {
        get
        {
            if (Environment.GetEnvironmentVariable("P02_GITHUB_TOKEN") is { Length: > 0 } env)
                return env;
            if (!File.Exists(TokenPath)) return null;
            string t = File.ReadAllText(TokenPath).Trim();
            return t.Length > 0 ? t : null;
        }
    }

    /// <summary>
    /// Why a check might have failed before it even reached GitHub. An empty
    /// token file looks identical to a missing one from the outside, and both
    /// look identical to "no releases yet" once GitHub answers 404.
    /// </summary>
    private static string TokenState()
    {
        if (Environment.GetEnvironmentVariable("P02_GITHUB_TOKEN") is { Length: > 0 })
            return "using the P02_GITHUB_TOKEN environment variable";
        if (!File.Exists(TokenPath))
            return $"no token file at {TokenPath}";
        return File.ReadAllText(TokenPath).Trim().Length == 0
            ? $"the token file at {TokenPath} is empty"
            : $"using the token in {TokenPath}";
    }

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

    /// <summary>
    /// The newest release marked critical, or null. Set by any check, silent or
    /// not, so a launch check is enough to raise the alarm.
    /// </summary>
    public static string? Critical { get; private set; }

    /// <summary>One line saying what the critical release fixes.</summary>
    public static string CriticalWhy { get; private set; } = "";

    private static string FirstLine(string body)
    {
        foreach (string line in body.Split([(char)10, (char)13],
                                           StringSplitOptions.RemoveEmptyEntries))
        {
            string t = line.Replace("[critical]", "", StringComparison.OrdinalIgnoreCase)
                           .Trim(' ', '#', '-', '*');
            if (t.Length > 0) return t;
        }

        return "it fixes something that can get you killed";
    }

    public static async Task CheckAsync(IWin32Window owner, bool silent,
                                        Action? beforeExit = null, bool ui = true)
    {
        try
        {
            using var http = MakeClient();
            // Every release, not just the newest: someone several versions
            // behind should see everything they missed, not only the last of
            // it.
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=50";
            using var resp = await http.GetAsync(url);

            if (!resp.IsSuccessStatusCode)
            {
                string why = (int)resp.StatusCode switch
                {
                    404 => $"No release found for {Owner}/{Repo}."
                           + Environment.NewLine + Environment.NewLine
                           + "The repository is public, so no token is needed. If this "
                           + "persists, the release may have been removed."
                           + Environment.NewLine + $"(token: {TokenState()})",
                    401 => $"GitHub rejected the token ({TokenState()}). The repository is "
                           + "public and needs no token at all - deleting token.txt will fix "
                           + "this.",
                    403 => "GitHub refused the request. Usually rate limiting; try again in a "
                           + "few minutes.",
                    _ => $"GitHub said {(int)resp.StatusCode} {resp.ReasonPhrase}.",
                };

                Log.Write($"update check failed: {why}");
                if (!silent) MessageBox.Show(owner, why, "Check for updates",
                                             MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            var newer = new List<(Version V, string Tag, string Body, JsonElement Rel)>();
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                if (rel.TryGetProperty("prerelease", out var p) && p.GetBoolean()) continue;

                string t = rel.GetProperty("tag_name").GetString() ?? "";
                if (!Version.TryParse(t.TrimStart('v', 'V'), out var v)) continue;
                if (v <= Current) continue;

                newer.Add((v, t, rel.TryGetProperty("body", out var b)
                                 ? b.GetString() ?? "" : "", rel));
            }

            if (newer.Count == 0)
            {
                if (!silent)
                    MessageBox.Show(owner, $"You're on the latest version ({Current}).",
                                    "Check for updates", MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                return;
            }

            newer.Sort((x, y) => y.V.CompareTo(x.V));
            var latest = newer[0].V;

            // A release says for itself whether it is one people must not stay
            // behind on. Anything that fixes a way for this to sit quiet while
            // somebody dies belongs here and nothing else does - the moment it
            // is used for a tidy-up nobody will believe the next one.
            foreach (var (v, t, body, _) in newer)
            {
                if (!body.Contains("[critical]", StringComparison.OrdinalIgnoreCase)) continue;
                Critical = t;
                CriticalWhy = FirstLine(body);
                Log.Write($"update: {t} is marked critical - {CriticalWhy}");
                break;
            }

            var root = newer[0].Rel;

            // Newest first, each under its own version, so a jump of several
            // releases reads as a list of what changed rather than one entry.
            var story = new StringBuilder();
            foreach (var (v, t, body, _) in newer)
            {
                story.AppendLine($"### {t}");

                // Each release body ends with a compare link for its own hop.
                // Leaving them in would give the dialog several links to choose
                // from and it would pick the wrong one - the last hop rather
                // than the whole span.
                var lines = body.Split(new[] { '\n', '\r' },
                                      StringSplitOptions.None);
                var kept = lines
                    .Where(l => !l.TrimStart().StartsWith("**Full Changelog**",
                                                          StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                string trimmed = string.Join(Environment.NewLine, kept).Trim();
                if (trimmed.Length > 0) story.AppendLine(trimmed);
                story.AppendLine();
            }

            // One link covering everything between the installed version and
            // the newest, rather than only the last hop.
            story.AppendLine($"**Full Changelog**: https://github.com/{Owner}/{Repo}/compare/"
                             + $"v{Current}...{newer[0].Tag}");

            string notes = story.ToString();
            Log.Write($"update: {newer.Count} newer release(s), {Current} -> {latest}");

            // The background watch only exists to notice a critical release;
            // a dialog over the game every few minutes would be worse than the
            // problem it is looking for.
            if (!ui) return;

            using (var dlg = new UpdateDialog(latest, Current, notes, newer.Count))
            {
                if (dlg.ShowDialog(owner) != DialogResult.Yes) return;
            }

            if (!TargetWritable(out string blocked))
            {
                Log.Write($"update blocked: {blocked}");
                MessageBox.Show(owner, blocked, "Update",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

            long expected = 0;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
                if (string.Equals(a.GetProperty("name").GetString(), AssetName,
                                  StringComparison.OrdinalIgnoreCase))
                    expected = a.GetProperty("size").GetInt64();

            // A fixed name per version meant a second attempt reused the same
            // path - and a 58 MB executable that was written moments ago is
            // very often still held open by the virus scanner inspecting it, so
            // the retry failed on the file the previous try had left behind.
            // A new name each time cannot collide with anything.
            string tmp = Path.Combine(
                Path.GetTempPath(),
                $"P02-{latest}-{DateTime.Now:HHmmss}-{Environment.ProcessId}.exe");

            TidyOldDownloads();

            Log.Write($"update: downloading {expected} bytes to {tmp}");
            using (var req = new HttpRequestMessage(HttpMethod.Get, assetUrl))
            {
                req.Headers.Accept.Clear();
                req.Headers.Accept.ParseAdd("application/octet-stream");
                using var dl = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                dl.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmp);
                await dl.Content.CopyToAsync(fs);
            }

            long got = new FileInfo(tmp).Length;
            if (expected > 0 && got != expected)
            {
                string msg = $"Download was {got} bytes but should be {expected}. "
                           + "Not swapping a half-downloaded file - try again.";
                Log.Write($"update: {msg}");
                try { File.Delete(tmp); } catch { /* best effort */ }
                MessageBox.Show(owner, msg, "Update",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Log.Write($"update: downloaded {got} bytes ok");

            SwapAndRestart(tmp, beforeExit);
        }
        catch (Exception ex)
        {
            // Some exceptions carry no message at all, which produced an empty
            // dialog and an empty log line - the least useful possible outcome.
            string what = string.IsNullOrWhiteSpace(ex.Message)
                ? $"{ex.GetType().Name} (no message)"
                : $"{ex.GetType().Name}: {ex.Message}";
            Log.Write($"update failed: {what}{Environment.NewLine}{ex}");
            if (!silent)
                MessageBox.Show(owner,
                    what + Environment.NewLine + Environment.NewLine
                    + $"Full details in {Log.Path_}",
                    "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Hands the swap to a detached batch file and quits.
    ///
    /// Two things were wrong before. Application.Exit() runs every form-closing
    /// handler, so tearing down the monitor and key threads happened inside the
    /// try block that reports "update failed" - when it threw, the message was
    /// blank, the app stayed alive, and the exe stayed locked, so the swapper
    /// could never replace it. Environment.Exit cannot be blocked that way.
    ///
    /// And the batch waited by retrying the copy blindly, then called `pause`.
    /// With no console attached pause reads end-of-file and returns at once, so
    /// a failed swap deleted its own script and left no trace. It now waits for
    /// this process to actually disappear and writes what happened to a log.
    /// </summary>
    /// <summary>
    /// Clears out downloads left behind by earlier updates.
    ///
    /// Each is the whole application, so leaving them accumulating in the temp
    /// folder costs sixty megabytes a release. Anything still held open is
    /// skipped rather than fought over.
    /// </summary>
    private static void TidyOldDownloads()
    {
        try
        {
            foreach (string old in Directory.EnumerateFiles(Path.GetTempPath(), "P02-*.exe"))
            {
                try
                {
                    if (DateTime.Now - File.GetCreationTime(old) > TimeSpan.FromMinutes(10))
                        File.Delete(old);
                }
                catch { /* in use, or not ours to delete */ }
            }
        }
        catch { /* the temp folder is not worth failing an update over */ }
    }

    private static void SwapAndRestart(string newExe, Action? beforeExit)
    {
        string current = Environment.ProcessPath
                         ?? Path.Combine(AppContext.BaseDirectory, "P02.exe");
        string bat = Path.Combine(Path.GetTempPath(), "p02-update.cmd");
        string swapLog = Path.Combine(AppConfig.Dir, "update.log");
        int pid = Environment.ProcessId;

        // Every external command is called by full path. Git for Windows puts
        // Unix tools on PATH, so a bare `find` in a batch file can resolve to
        // Unix find, which errored out and made the wait loop fall straight
        // through - the copy then raced a process that still held the exe.
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
        string ping = Path.Combine(sys, "PING.EXE");

        var script = string.Join(Environment.NewLine,
            "@echo off",
            "setlocal",
            $"set \"target={current}\"",
            $"set \"source={newExe}\"",
            $"set \"log={swapLog}\"",
            $"echo [%date% %time%] waiting for pid {pid} >> \"%log%\"",
            // One call, no polling and no text parsing to get wrong.
            $"\"{ps}\" -NoProfile -NonInteractive -Command " +
                $"\"Wait-Process -Id {pid} -Timeout 120 -ErrorAction SilentlyContinue\"",
            $"echo [%date% %time%] proceeding to copy >> \"%log%\"",
            "for /l %%i in (1,1,30) do (",
            "  copy /y \"%source%\" \"%target%\" >nul 2>&1 && goto done",
            $"  \"{ping}\" -n 2 127.0.0.1 >nul",
            ")",
            "echo [%date% %time%] FAILED to replace \"%target%\" >> \"%log%\"",
            "goto cleanup",
            ":done",
            "echo [%date% %time%] replaced ok, restarting >> \"%log%\"",
            "del \"%source%\" >nul 2>&1",
            "start \"\" \"%target%\"",
            ":cleanup",
            "del \"%~f0\" >nul 2>&1");

        Directory.CreateDirectory(AppConfig.Dir);
        File.WriteAllText(bat, script);
        Log.Write($"update: wrote swap script, launching; target={current}");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        // Outside every try block, and not Application.Exit: nothing a closing
        // handler does can keep this process holding the file.
        try { beforeExit?.Invoke(); } catch { /* nothing may block the exit */ }
        Log.Write("update: exiting for swap");
        Environment.Exit(0);
    }

    /// <summary>Fails early if the exe cannot be replaced where it sits, rather
    /// than after a 50 MB download.</summary>
    private static bool TargetWritable(out string why)
    {
        why = "";
        string current = Environment.ProcessPath
                         ?? Path.Combine(AppContext.BaseDirectory, "P02.exe");
        string? dir = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(dir)) { why = "cannot tell where P02.exe is"; return false; }

        try
        {
            string probe = Path.Combine(dir, $".p02-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            why = $"P02.exe lives in {dir}, which cannot be written to ({ex.GetType().Name}). "
                + "Move P02.exe somewhere like your Downloads folder and try again.";
            return false;
        }
    }
}
