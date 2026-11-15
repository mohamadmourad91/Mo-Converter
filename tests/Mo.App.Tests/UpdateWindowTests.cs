using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mo.App.Views;
using Mo.Engine;

namespace Mo.App.Tests;

/// <summary>Builds the "update required" window off-screen; it is never shown on the desktop.</summary>
public class UpdateWindowTests
{
    /// <summary>
    /// One test on one thread: the window uses StaticResource, which needs an Application, and only one
    /// Application (bound to its thread) can exist per process.
    /// </summary>
    [Fact]
    public void Update_window_renders_in_every_direction()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(Load("Themes/Shared.xaml"));
                Render("ar", dark: false);
                Render("de", dark: true);
                CannotBeClosedUntilAllowed();
                app.Resources.MergedDictionaries.Insert(0, Load("Themes/Light.xaml"));
                ToolWindowsBuildAndDraw();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static void Render(string language, bool dark)
    {
        var previous = Localizer.Current;
        Localizer.Current = new Localizer(language, typeof(App).Assembly);
        try
        {
            var window = new UpdateWindow("1.4.0", TimeSpan.FromSeconds(60), () => { }) { AllowClose = true };
            window.Resources.MergedDictionaries.Add(Load(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml"));
            window.ApplyTheme(dark);

            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(460, double.PositiveInfinity));
            content.Arrange(new Rect(new Size(460, content.DesiredSize.Height)));
            content.UpdateLayout();

            Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
            Assert.Contains("1.4.0", window.MessageText.Text);
            Assert.Contains("60", window.CountdownText.Text);

            var bitmap = new RenderTargetBitmap(460, (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(dark ? Brushes.Black : Brushes.White, null, new Rect(0, 0, bitmap.Width, bitmap.Height));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, bitmap.Width, bitmap.Height));
            }
            bitmap.Render(visual);

            // Set MO_RENDER_DIR to look at the result.
            if (Environment.GetEnvironmentVariable("MO_RENDER_DIR") is { Length: > 0 } dir)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(dir, $"update-window-{language}.png"));
                encoder.Save(file);
            }
            window.Close();
        }
        finally
        {
            Localizer.Current = previous;
        }
    }

    /// <summary>The close button, Alt+F4 and WM_CLOSE all go through Closing; only quitting the app may close it.</summary>
    private static void CannotBeClosedUntilAllowed()
    {
        var applied = 0;
        var window = new UpdateWindow("1.4.0", TimeSpan.FromSeconds(60), () => applied++)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        window.Show();

        window.Close();
        Assert.True(window.IsVisible, "the mandatory update window closed");
        Assert.Equal(0, applied);

        window.AllowClose = true;
        window.Close();
        Assert.False(window.IsVisible);
    }

    /// <summary>
    /// The image editor and the QR window are built (on the same thread as the one Application), the editor draws
    /// its preview and crop frame from a real picture, and the QR window makes a code while typing.
    /// </summary>
    private static void ToolWindowsBuildAndDraw()
    {
        Localizer.Current = new Localizer("en", typeof(App).Assembly);
        var picture = Path.Combine(Path.GetTempPath(), $"mo-editor-{Guid.NewGuid():N}.png");
        try
        {
            using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 2400, 1200)) image.Write(picture);
            var editor = new ImageEditorWindow(picture, ViewModels.EditorMode.Crop);
            Assert.Equal((2400, 1200), (editor.Model.Width, editor.Model.Height));
            Assert.NotNull(editor.Picture.Source);
            Assert.Equal(1600, editor.Stage.Width); // the preview is reduced, the edit is not
            editor.Model.Drawn(new Rect(0, 0, 800, 400), new Size(1600, 800));
            Assert.Equal(new Mo.Engine.Images.CropRect(0, 0, 1200, 600), editor.Model.Crop);
            editor.RenderPreview();
            Assert.Equal(Visibility.Visible, editor.CropFrame.Visibility);
            Assert.Equal(800, editor.CropFrame.Width);
            editor.Close();

            var qr = new QrWindow();
            qr.Model.Text = "Mo Converter";
            Assert.True(qr.Model.HasCode);
            qr.Close();
        }
        finally
        {
            File.Delete(picture);
        }
    }

    private static ResourceDictionary Load(string path)
    {
        // No Application runs in tests, so the dictionaries are loaded from the app assembly directly.
        return (ResourceDictionary)Application.LoadComponent(new Uri($"/Mo Converter;component/{path}", UriKind.Relative));
    }
}
