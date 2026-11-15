using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>The icon next to the clock and its menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly ContextMenu _menu;
    private ResourceDictionary? _menuTheme;

    public TrayIcon(Action openSettings, Action openQr, Action openShelf, Action openWelcome, Action quit, Func<AppSettings> settings)
    {
        var t = Localizer.Current;
        var menu = new ContextMenu
        {
            FlowDirection = t.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        menu.Items.Add(Item(t.T("tray.new-shelf"), "\uE7B8", openShelf));
        menu.Items.Add(Item(t.T("tray.qr"), "\uED14", openQr));
        menu.Items.Add(Item(t.T("tray.welcome"), "\uE897", openWelcome));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(t.T("tray.settings"), "\uE713", openSettings));
        menu.Items.Add(Item(t.T("tray.quit"), "\uE711", quit));

        _menu = menu;
        var s = settings();
        _icon = new TaskbarIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            ToolTipText = t.T("tray.tooltip", KeyName(s.ConvertKey), KeyName(s.ToolsKey), KeyName(s.ActionsKey)),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            NoLeftClickDelay = true,
            LeftClickCommand = new ViewModels.RelayCommand(openSettings),
        };
    }

    /// <summary>Creates the icon. The control is not in any visual tree, so it must be created explicitly.</summary>
    public void Show() => _icon.ForceCreate(enablesEfficiencyMode: false);

    /// <summary>
    /// Gives the menu the Windows 11 (Fluent) look in light or dark. The menu is a popup, not a window,
    /// so it cannot use Window.ThemeMode; the Fluent dictionary is merged into its resources instead.
    /// </summary>
    public void ApplyTheme(bool dark)
    {
        try
        {
            var next = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(dark ? "Dark" : "Light")}.xaml"),
            };
            if (_menuTheme is not null) _menu.Resources.MergedDictionaries.Remove(_menuTheme);
            _menu.Resources.MergedDictionaries.Add(next);
            _menuTheme = next;
        }
        catch (Exception ex)
        {
            // Purely cosmetic: the classic menu style still works.
            Services.CrashLog.Write(ex);
        }
    }

    public void UpdateTooltip(AppSettings s) =>
        _icon.ToolTipText = Localizer.Current.T("tray.tooltip", KeyName(s.ConvertKey), KeyName(s.ToolsKey), KeyName(s.ActionsKey));

    public static string KeyName(ModifierKey key) => Localizer.Current.T(key switch
    {
        ModifierKey.Shift => "key.shift",
        ModifierKey.Ctrl => "key.ctrl",
        _ => "key.alt",
    });

    private static MenuItem Item(string text, string glyph, Action? click)
    {
        var item = new MenuItem
        {
            Header = text,
            IsEnabled = click is not null,
            Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        if (click is not null) item.Click += (_, _) => click();
        return item;
    }

    public void Dispose() => _icon.Dispose();
}
