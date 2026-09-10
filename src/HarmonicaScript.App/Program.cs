using Avalonia;

namespace HarmonicaScript.App;

internal static class Program
{
    /// <summary>
    /// When true the app opens its main window, waits for one render pass, then exits 0.
    /// This is M0 spike (a) turned into a repeatable check: it proves the XAML compiler task
    /// ran, the Avalonia native backend resolved, and a window actually renders on this
    /// OS/architecture/runtime combination. CI runs it headlessly on every push.
    /// </summary>
    internal static bool SmokeMode { get; private set; }

    /// <summary>A MIDI path passed as <c>--open &lt;file&gt;</c>, so the app can be launched straight into a song.</summary>
    internal static string? OpenPath { get; private set; }

    // Avalonia requires a synchronous main before any Avalonia type is touched,
    // so that SkiaSharp and the platform backend initialise in the right order.
    [STAThread]
    internal static int Main(string[] args)
    {
        SmokeMode = Array.Exists(args, a => a is "--smoke");

        var openIndex = Array.IndexOf(args, "--open");
        if (openIndex >= 0 && openIndex + 1 < args.Length)
        {
            OpenPath = args[openIndex + 1];
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Also used by the XAML previewer, which calls it by convention.</summary>
    internal static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
