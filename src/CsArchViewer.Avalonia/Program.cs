using Avalonia;
using Microsoft.Build.Locator;
using System;

namespace CsArchViewer.Avalonia;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        MSBuildLocator.RegisterDefaults();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
