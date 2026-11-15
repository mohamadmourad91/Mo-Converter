using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Mo.App.Services;
using Mo.App.Services.Demo;
using Mo.App.Services.Updates;
using Mo.App.Shell;
using Mo.App.ViewModels;
using Mo.App.Views;
using Mo.Engine;

namespace Mo.App;

/// <summary>
/// Tray application without a main window. Startup: single instance → settings → texts → theme →
/// operations → tray icon → drag watcher. Runs until Quit.
/// </summary>
public partial class App : Application
{
    private const string RestartArgument = "--restarted";

    private SingleInstance? _instance;
    private ThemeService? _theme;
    private TrayIcon? _tray;
    private DragWatcher? _watcher;
    private WheelController? _wheel;
    private SettingsWindow? _settingsWindow;
    private UpdateService? _updates;
    private UpdateWindow? _updateWindow;
    private readonly CancellationTokenSource _closing = new();

    public static AppSettings Settings { get; private set; } = new();
    public static OperationRegistry Operations { get; private set; } = new();
    public static ToastService Toasts { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _instance = SingleInstance.Acquire(waitForPrevious: e.Args.Contains(RestartArgument));
        if (!_instance.IsFirst)
        {
            // Already running: ask that copy to show its settings, then leave quietly.
            _instance.SignalFirst();
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        // A mandatory update downloaded in an earlier run is installed before anything else starts.
        _updates = new UpdateService(new VelopackUpdateChannel());
        if (_updates.TryApplyPendingAtStartup())
        {
            Shutdown();
            return;
        }

        Settings = AppSettings.Load();
        Localizer.Current = new Localizer(Settings.Language ?? Localizer.SystemLanguage(), typeof(App).Assembly);

        _theme = new ThemeService(this);
        _theme.Apply(Settings.Theme);
        _theme.Changed += () => _settingsWindow?.ApplyTheme(_theme.IsDark);

        Operations = OperationRegistry.CreateDefault();
        DemoOperations.Register(Operations);

        Toasts = new ToastService();
        OperationRunner? runner = null;
        var tools = new ToolWindows(() => runner!, Operations, Toasts, () => _theme?.IsDark ?? false);
        var shelf = new ShelfService(() => _theme?.IsDark ?? false, () => Settings);
        runner = new OperationRunner(() => Settings, Toasts, tools, shelf.AddResults);

        _tray = new TrayIcon(OpenSettings, tools.OpenQr, () => shelf.Show(), OpenWelcome, Quit, () => Settings);
        _tray.ApplyTheme(_theme.IsDark);
        _theme.Changed += () => _tray?.ApplyTheme(_theme.IsDark);
        _tray.Show();

        _watcher = new DragWatcher(() => Settings);
        _wheel = new WheelController(_watcher, Operations, runner);
        _watcher.Start();

        _instance.Listen(() => Dispatcher.BeginInvoke(OpenSettings));

        // Once the app is idle after the start: prepare what the first drag, drop and conversion need,
        // so they are as quick as every later one.
        Dispatcher.BeginInvoke(new Action(Prewarm), DispatcherPriority.ApplicationIdle);

        // The guide opens once on the first start, after the tray icon is up; the box in it stops that.
        if (!Settings.WelcomeShown) Dispatcher.BeginInvoke(new Action(OpenWelcome), DispatcherPriority.ApplicationIdle);

        _updates.Downloading += version => Toasts.Show(ToastKind.Info, Localizer.Current.T("update.downloading", version));
        _updates.Ready += ShowRequiredUpdate;
        _ = _updates.RunAsync(_closing.Token);
    }

    private void Prewarm()
    {
        try
        {
            _wheel?.Prewarm();
            Toasts.Prewarm();
        }
        catch (Exception ex)
        {
            // Only a head start: if it fails, the first drag simply does this work itself.
            CrashLog.Write(ex);
        }

        _ = Task.Run(() =>
        {
            try
            {
                ImageMagick.MagickNET.Initialize();
                _ = Mo.Engine.Office.LibreOffice.IsAvailable;
                if (Mo.Engine.Media.Ffmpeg.IsAvailable) _ = Mo.Engine.Media.Ffmpeg.Encoders;
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        });
    }

    /// <summary>The guide: from the tray menu, and once on the first start.</summary>
    private void OpenWelcome()
    {
        var window = new WelcomeWindow(Settings);
        ToolWindowSupport.Prepare(window, _theme?.IsDark ?? false);
        window.Show();
        window.Activate();
    }

    /// <summary>A newer version is downloaded: stop the wheels and ask for the restart that installs it.</summary>
    private void ShowRequiredUpdate(string version)
    {
        _watcher?.Stop();
        _settingsWindow?.Close();
        if (_updateWindow is not null) return;

        var window = new UpdateWindow(version, UpdateService.Countdown, () => _updates?.ApplyNow());
        window.ApplyTheme(_theme?.IsDark ?? false);
        _updateWindow = window;
        window.Show();
        window.Activate();
    }

    private void OpenSettings()
    {
        if (_updateWindow is not null)
        {
            // Nothing else is available until the update is installed.
            _updateWindow.Activate();
            return;
        }

        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow();
        // Edit a copy so Cancel leaves the live settings untouched.
        var copy = AppSettings.Load();
        window.DataContext = new SettingsViewModel(copy, window, StartupRegistration.IsEnabled(), StartupRegistration.Set, OnSettingsSaved);
        window.ApplyTheme(_theme?.IsDark ?? false);
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        window.Show();
        window.Activate();
    }

    private void OnSettingsSaved(AppSettings saved, bool restart)
    {
        Settings = saved;
        _theme?.Apply(saved.Theme);
        _tray?.UpdateTooltip(saved);
        if (restart) Restart();
    }

    /// <summary>Starts a fresh copy (which waits for our mutex) and exits, so a new language takes effect.</summary>
    private void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, RestartArgument) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                return;
            }
        }
        Quit();
    }

    private void Quit()
    {
        if (_updateWindow is not null) _updateWindow.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _closing.Cancel();
        _watcher?.Dispose();
        _wheel?.Dispose();
        _tray?.Dispose();
        _theme?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the tray app alive; the error is logged and briefly shown.
        CrashLog.Write(e.Exception);
        e.Handled = true;
        try
        {
            Toasts?.Show(ToastKind.Error, Localizer.Current.T("toast.unexpected", e.Exception.Message));
        }
        catch
        {
            // Showing the toast failed as well; the log entry is all we can do.
        }
    }
}
