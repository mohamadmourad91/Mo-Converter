using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mo.App.ViewModels;
using Mo.Engine;
using Mo.Engine.Pdf;

namespace Mo.App.Views;

/// <summary>Page arranger: drag tiles to reorder, buttons turn, copy, move or remove a page.</summary>
public partial class PdfPagesWindow : Window
{
    /// <summary>Thumbnails are small; 36 dpi keeps hundreds of pages quick.</summary>
    private const double ThumbnailDpi = 36;

    private readonly string _path;
    private Point _pressed;
    private PageTile? _pressedTile;

    public PdfPagesWindow(string path)
    {
        InitializeComponent();
        _path = path;
        Title = Localizer.Current.T("pages.title", System.IO.Path.GetFileName(path));
        CountText.Text = Localizer.Current.T("pages.loading");
        // "Earlier" points towards the start of the line: left in English and German, right in Arabic.
        var rightToLeft = Localizer.Current.IsRightToLeft;
        Resources["Mo.EarlierGlyph"] = rightToLeft ? "\uE76C" : "\uE76B";
        Resources["Mo.LaterGlyph"] = rightToLeft ? "\uE76B" : "\uE76C";
    }

    public PdfPagesViewModel? Model { get; private set; }

    public event Action<IReadOnlyList<PdfPageChoice>>? Saved;

    /// <summary>Renders the thumbnails; the tiles appear as soon as the page count is known.</summary>
    public async Task LoadAsync(CancellationToken cancellation = default)
    {
        await PdfRenderer.RenderAsync(_path, ThumbnailDpi, (index, count, _, png) =>
        {
            if (Model is null)
            {
                Model = new PdfPagesViewModel(count);
                DataContext = Model;
                // The loading text replaced the binding; bind the live page count again.
                CountText.SetBinding(TextBlock.TextProperty, nameof(PdfPagesViewModel.CountText));
                Model.Pages.CollectionChanged += (_, _) => SaveButton.IsEnabled = Model.HasChanges;
                Model.PropertyChanged += (_, _) => SaveButton.IsEnabled = Model.HasChanges;
            }
            var picture = ToolWindowSupport.Picture(png);
            foreach (var tile in Model.Pages.Where(t => t.SourceIndex == index))
            {
                tile.Thumbnail = picture;
                tile.PropertyChanged += (_, _) => SaveButton.IsEnabled = Model.HasChanges;
            }
            return Task.CompletedTask;
        }, cancellation);
    }

    private void OnPressTile(object sender, MouseButtonEventArgs e)
    {
        _pressed = e.GetPosition(PageList);
        _pressedTile = (e.OriginalSource as DependencyObject) is { } source && FindTile(source) is { } tile && !IsInsideButton(source) ? tile : null;
    }

    private void OnDragTile(object sender, MouseEventArgs e)
    {
        if (_pressedTile is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(PageList) - _pressed;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var tile = _pressedTile;
        _pressedTile = null;
        DragDrop.DoDragDrop(PageList, tile, DragDropEffects.Move);
    }

    private void OnDropTile(object sender, DragEventArgs e)
    {
        if (Model is null || e.Data.GetData(typeof(PageTile)) is not PageTile dragged) return;
        var target = (e.OriginalSource as DependencyObject) is { } source ? FindTile(source) : null;
        var to = target is null ? Model.Pages.Count - 1 : Model.Pages.IndexOf(target);
        Model.Move(Model.Pages.IndexOf(dragged), to);
    }

    private static PageTile? FindTile(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { DataContext: PageTile tile }) return tile;
        return null;
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button) return true;
            if (current is ListBoxItem) return false;
        }
        return false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Model is null) return;
        var choices = Model.Choices();
        Close();
        Saved?.Invoke(choices);
    }
}
