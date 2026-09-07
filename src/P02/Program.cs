namespace P02;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance, or two copies fight over the same hotkey and both fire.
        using var single = new Mutex(true, @"Local\P02-singleton", out bool first);
        if (!first)
        {
            MessageBox.Show("P02 is already running — look in the system tray.",
                            "P02", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);

        Application.Run(new MainForm(AppConfig.Load()));
    }

    private static void Crash(Exception? ex)
    {
        Log.Write($"UNHANDLED: {ex}");
        MessageBox.Show($"{ex?.Message}\n\nDetails in:\n{Log.Path_}",
                        "P02 crashed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
