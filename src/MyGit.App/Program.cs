using Avalonia;
using Velopack;

namespace MyGit.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: the installer and updater start the app with special arguments (install, update, uninstall)
        // and expect it to handle them and exit. A normal start just continues.
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the XAML previewer in Visual Studio and Rider.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .WithInterFont()
                  .LogToTrace();
}
