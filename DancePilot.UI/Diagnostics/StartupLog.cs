namespace DancePilot.UI.Diagnostics;

internal static class StartupLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DancePilot",
        "startup.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Startup logging must never prevent the app from opening.
        }
    }

    public static void Write(Exception exception, string context)
    {
        Write($"{context}: {exception}");
    }
}
