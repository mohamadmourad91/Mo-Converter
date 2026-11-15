using System.Windows;
using Mo.App.Services;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>
/// First-run guide: which key opens which wheel and how to drop files. Shown once automatically;
/// "Welcome guide" in the tray menu opens it again.
/// </summary>
public partial class WelcomeWindow : Window
{
    private readonly AppSettings _settings;

    public WelcomeWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        var t = Localizer.Current;
        KeysText.Text = t.T("welcome.keys", t.T("key." + KeyWord(settings.ConvertKey)), t.T("key." + KeyWord(settings.ToolsKey)), t.T("key." + KeyWord(settings.ActionsKey)));
        DontShow.IsChecked = settings.WelcomeShown;
    }

    private static string KeyWord(ModifierKey key) => key switch
    {
        ModifierKey.Shift => "shift",
        ModifierKey.Ctrl => "ctrl",
        _ => "alt",
    };

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // Remember the choice only when the box is ticked; unticking the box again shows the guide next time.
        if (DontShow.IsChecked == true && !_settings.WelcomeShown)
        {
            _settings.WelcomeShown = true;
            try { _settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write(ex); }
        }
        else if (DontShow.IsChecked != true && _settings.WelcomeShown)
        {
            _settings.WelcomeShown = false;
            try { _settings.Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashLog.Write(ex); }
        }
        Close();
    }
}
