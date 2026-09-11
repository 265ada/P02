using System.Reflection;

namespace P02;

/// <summary>
/// The orb, for every window that wants one.
///
/// ApplicationIcon puts it on the executable, which is what a taskbar and a
/// file listing use - but a Form draws whatever it was handed, and handed
/// nothing it draws the default. So the same file is carried inside the
/// assembly and loaded once.
/// </summary>
internal static class AppIcon
{
    private static Icon? _icon;
    private static bool _tried;

    public static Icon? Load()
    {
        if (_tried) return _icon;
        _tried = true;

        try
        {
            var self = Assembly.GetExecutingAssembly();
            string? name = Array.Find(self.GetManifestResourceNames(),
                                      n => n.EndsWith("app.ico", StringComparison.Ordinal));
            if (name is null) return null;

            using var stream = self.GetManifestResourceStream(name);
            if (stream is null) return null;

            _icon = new Icon(stream);
        }
        catch (Exception ex)
        {
            Log.Write($"icon: could not load the app icon - {ex.Message}");
        }

        return _icon;
    }
}
