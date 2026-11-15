using Velopack;

namespace Mo.App;

/// <summary>
/// Entry point. Velopack must run first: during install, update and uninstall it starts the app
/// with special arguments, does its work and exits before any window or tray icon exists.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
