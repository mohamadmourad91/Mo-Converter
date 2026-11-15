using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace Mo.Engine.Pdf;

/// <summary>Renders PDF pages to PNG with the PDF engine built into Windows (Windows.Data.Pdf).</summary>
public static class PdfRenderer
{
    /// <summary>Calls <paramref name="onPage"/> with (page index, page count, page size in points, PNG stream) for every page.</summary>
    public static async Task RenderAsync(string source, double dpi, Func<int, int, (double Width, double Height), Stream, Task> onPage, CancellationToken cancellation, bool jpeg = false)
    {
        WinPdf.PdfDocument document;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
            document = await WinPdf.PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Windows reports a password-protected file with ERROR_WRONG_PASSWORD.
            throw new OperationException(e.HResult == unchecked((int)0x8007052B) ? "error.pdf-protected" : "error.pdf-unreadable", Path.GetFileName(source));
        }

        var count = (int)document.PageCount;
        for (uint i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var page = document.GetPage(i);
            using var png = new InMemoryRandomAccessStream();
            // page.Size is in device-independent pixels (1/96 inch).
            var options = new WinPdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * dpi / 96)),
                // A page is white paper. The default is transparent, which turns black text invisible on a dark viewer.
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                // Straight to JPEG: decoding and re-encoding Windows' PNG with ImageMagick was three times slower.
                BitmapEncoderId = jpeg ? Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId : Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
            };
            await page.RenderToStreamAsync(png, options);
            png.Seek(0);
            using var stream = png.AsStreamForRead();
            await onPage((int)i, count, (page.Size.Width * 72 / 96, page.Size.Height * 72 / 96), stream);
        }
    }
}
