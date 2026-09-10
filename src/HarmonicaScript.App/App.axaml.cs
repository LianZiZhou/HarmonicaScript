using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace HarmonicaScript.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // Blocking on first run only, and never in smoke mode (CI has nobody to accept it).
            if (!Program.SmokeMode && !Views.DisclaimerWindow.AlreadyAccepted)
            {
                window.Opened += async (_, _) => await new Views.DisclaimerWindow().ShowDialog(window);
            }

            if (Program.SmokeMode)
            {
                ScheduleSmokeExit(window, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Waits until the window has actually laid out and produced a frame, then shuts down
    /// cleanly with exit code 0. A crash, a missing native backend or an uncompiled XAML
    /// resource all fail before this fires, which is exactly what the spike must detect.
    /// </summary>
    private static void ScheduleSmokeExit(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        window.Opened += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                Console.WriteLine(
                    $"SMOKE OK: window rendered {window.Bounds.Width:0}x{window.Bounds.Height:0} "
                    + $"on {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} "
                    + $"/ {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} "
                    + $"/ runtime {Environment.Version} "
                    + $"/ Avalonia {typeof(Application).Assembly.GetName().Version}");
                desktop.Shutdown(0);
            },
            DispatcherPriority.ApplicationIdle);
    }
}
