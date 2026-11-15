using System.Windows;
using Microsoft.Win32;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>Settings window. Logic lives in <see cref="SettingsViewModel"/>; this class only provides dialogs.</summary>
public partial class SettingsWindow : Window, ISettingsDialogs
{
    public SettingsWindow()
    {
        InitializeComponent();
        FlowDirection = Localizer.Current.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    /// <summary>Uses the built-in Windows 11 (Fluent) control styles in the given colour mode.</summary>
    public void ApplyTheme(bool dark) => ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

    public string? PickFolder(string? current)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Localizer.Current.T("settings.output.pick-title"),
            Multiselect = false,
        };
        if (current is not null && Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    public bool AskRestart()
    {
        var t = Localizer.Current;
        var options = t.IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
        return MessageBox.Show(this, t.T("settings.restart.message"), t.T("app.name"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, options) == MessageBoxResult.Yes;
    }

    void ISettingsDialogs.Close() => Close();
}
