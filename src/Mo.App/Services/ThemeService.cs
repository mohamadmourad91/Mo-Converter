using System.Windows;
using Microsoft.Win32;
using Mo.Engine;

namespace Mo.App.Services;

/// <summary>
/// Swaps the app's light/dark colour dictionary (Themes\Light.xaml or Themes\Dark.xaml) and follows the
/// Windows app theme when the setting is "System".
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly Application _app;
    private ResourceDictionary? _current;
    private ThemeChoice _choice;

    public ThemeService(Application app)
    {
        _app = app;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public bool IsDark { get; private set; }

    /// <summary>Raised after the colours changed (for windows that also set a Fluent ThemeMode).</summary>
    public event Action? Changed;

    public void Apply(ThemeChoice choice)
    {
        _choice = choice;
        var dark = choice switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => WindowsUsesDarkApps(),
        };
        if (_current is not null && dark == IsDark) return;

        IsDark = dark;
        var next = new ResourceDictionary { Source = new Uri($"/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        var dictionaries = _app.Resources.MergedDictionaries;
        if (_current is not null) dictionaries.Remove(_current);
        dictionaries.Insert(0, next);
        _current = next;
        Changed?.Invoke();
    }

    /// <summary>Reads "Choose your app mode" from the registry; defaults to light when unknown.</summary>
    public static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_choice != ThemeChoice.System || e.Category != UserPreferenceCategory.General) return;
        _app.Dispatcher.BeginInvoke(() => Apply(ThemeChoice.System));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
