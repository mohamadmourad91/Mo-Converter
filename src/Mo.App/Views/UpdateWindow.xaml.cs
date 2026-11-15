using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Mo.App.Services;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>
/// "Update required": cannot be closed, offers only "Update now" and installs on its own when the countdown ends.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly Action _apply;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _secondsLeft;
    private bool _applying;

    public UpdateWindow(string version, TimeSpan countdown, Action apply)
    {
        InitializeComponent();
        var t = Localizer.Current;
        FlowDirection = t.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        MessageText.Text = t.T("update.message", version);

        _apply = apply;
        _secondsLeft = (int)Math.Ceiling(countdown.TotalSeconds);
        ShowCountdown();
        _timer.Tick += (_, _) =>
        {
            _secondsLeft--;
            ShowCountdown();
            if (_secondsLeft <= 0) Apply();
        };
        Loaded += (_, _) => _timer.Start();
    }

    public void ApplyTheme(bool dark) => ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

    private void ShowCountdown() =>
        CountdownText.Text = Localizer.Current.T("update.countdown", Math.Max(_secondsLeft, 0));

    private void OnUpdateClick(object sender, RoutedEventArgs e) => Apply();

    private void Apply()
    {
        if (_applying) return;
        _applying = true;
        _timer.Stop();
        UpdateButton.IsEnabled = false;
        CountdownText.Text = Localizer.Current.T("update.installing");
        // Let the window repaint before the updater takes over and the process exits.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            try
            {
                _apply();
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                CountdownText.Text = Localizer.Current.T("update.failed", ex.Message);
                UpdateButton.IsEnabled = true;
                _applying = false;
            }
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The update is mandatory: the window only goes away when the app restarts (or the user quits from the tray).
        if (!AllowClose) e.Cancel = true;
        base.OnClosing(e);
    }

    /// <summary>Set by the app when it quits, so shutdown is not blocked.</summary>
    public bool AllowClose { get; set; }
}
