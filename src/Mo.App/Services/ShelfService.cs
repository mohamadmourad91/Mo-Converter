using System.Windows;
using System.Windows.Threading;
using Mo.App.ViewModels;
using Mo.App.Views;
using Mo.Engine;

namespace Mo.App.Services;

/// <summary>Owns the one shelf window. Created on first use and reused; closing only hides it.</summary>
public sealed class ShelfService(Func<bool> dark, Func<AppSettings> settings)
{
    private ShelfWindow? _window;

    public ShelfViewModel Model { get; } = new();

    /// <summary>Called by finished operations when "results go to the shelf" is on.</summary>
    public void AddResults(IReadOnlyList<string> paths)
    {
        if (!settings().ResultsToShelf || paths.Count == 0) return;
        Model.Add(paths);
        Show(activate: false);
    }

    public void Show(bool activate = true)
    {
        if (_window is null)
        {
            _window = new ShelfWindow(Model);
            ToolWindowSupport.Prepare(_window, dark());
            _window.Closed += (_, _) => _window = null;
        }
        if (!_window.IsVisible) _window.Show();
        if (activate) _window.Activate();
    }
}
