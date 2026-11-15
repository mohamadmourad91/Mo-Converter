using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>What every tool window shares: Fluent theme, reading direction, pictures from bytes.</summary>
public static class ToolWindowSupport
{
    public static void Prepare(Window window, bool dark)
    {
        window.FlowDirection = Localizer.Current.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
    }

    /// <summary>A frozen bitmap (usable from any thread) from PNG/JPEG bytes.</summary>
    public static BitmapImage Picture(byte[] bytes)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bytes);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static BitmapImage Picture(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Picture(copy.ToArray());
    }
}

/// <summary>Binds PNG bytes to an Image.</summary>
public sealed class BytesToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] { Length: > 0 } bytes ? ToolWindowSupport.Picture(bytes) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
