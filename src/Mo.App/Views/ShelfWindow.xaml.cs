using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>Floating shelf: drop files on it to keep them, drag them out to use them elsewhere.</summary>
public partial class ShelfWindow : Window
{
    private Point _pressed;
    private ShelfItem? _pressedItem;

    public ShelfWindow(ShelfViewModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = model;
        Loaded += (_, _) => model.Prune();
    }

    public ShelfViewModel Model { get; }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Model.Add(paths);
        e.Handled = true;
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = e.GetPosition(List);
        // A press on a remove button must not start a drag.
        _pressedItem = e.OriginalSource is DependencyObject { } source && !IsInsideButton(source)
            ? (ListBoxItemOf(source)?.DataContext as ShelfItem) : null;
    }

    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (_pressedItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var moved = e.GetPosition(List) - _pressed;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _pressedItem;
        _pressedItem = null;
        var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
        // Copy only: dragging out must never move the file away from the shelf's source.
        DragDrop.DoDragDrop(List, data, DragDropEffects.Copy);
    }

    private static ListBoxItem? ListBoxItemOf(DependencyObject element)
    {
        for (var current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is ListBoxItem item) return item;
        return null;
    }

    private static bool IsInsideButton(DependencyObject element)
    {
        for (var current = element; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is Button) return true;
            if (current is ListBoxItem) return false;
        }
        return false;
    }
}

/// <summary>Inverts a bool for bindings (used to hide the list while the shelf is empty).</summary>
public sealed class InvertBoolConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? false : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
