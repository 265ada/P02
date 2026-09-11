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
            // Clicking the icon again means "show me P02", not "tell me where
            // it is". Being informed that the thing you just asked for is
            // already somewhere else is a worse answer than simply doing it.
            Native.PostMessage(Native.HWND_BROADCAST, Native.WM_P02_SHOW, 0, 0);
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
