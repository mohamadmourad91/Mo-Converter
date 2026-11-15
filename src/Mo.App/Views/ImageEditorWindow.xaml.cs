using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ImageMagick;
using Mo.App.ViewModels;
using Mo.Engine;
using Mo.Engine.Images;

namespace Mo.App.Views;

/// <summary>
/// Image editor: crop, hide areas, turn, mirror and colours. The preview is a reduced copy rendered with the same
/// code that saves the result, so what you see is what you get.
/// </summary>
public partial class ImageEditorWindow : Window
{
    private const uint PreviewSide = 1600;

    private readonly MagickImage _preview;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private readonly List<Rectangle> _areaMarks = [];
    private Point? _dragStart;

    public ImageEditorWindow(string path, EditorMode mode)
    {
        InitializeComponent();
        Title = Localizer.Current.T("editor.title", System.IO.Path.GetFileName(path));

        using var original = new MagickImage(path);
        original.AutoOrient();
        Model = new ImageEditorViewModel((int)original.Width, (int)original.Height, mode);
        _preview = (MagickImage)original.Clone();
        if (_preview.Width > PreviewSide || _preview.Height > PreviewSide) _preview.Resize(new MagickGeometry(PreviewSide, PreviewSide));

        Stage.Width = Overlay.Width = Picture.Width = _preview.Width;
        Stage.Height = Overlay.Height = Picture.Height = _preview.Height;
        DataContext = Model;

        _refresh.Tick += (_, _) => { _refresh.Stop(); RenderPreview(); };
        Model.EditChanged += () => { _refresh.Stop(); _refresh.Start(); };
        Model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ImageEditorViewModel.Mode)) ShowHint(); };
        ShowHint();
        RenderPreview();
        Closed += (_, _) => _preview.Dispose();
    }

    public ImageEditorViewModel Model { get; }

    /// <summary>Raised with the changes when the user saves; the window closes itself.</summary>
    public event Action<ImageEdit>? Saved;

    private Size StageSize => new(_preview.Width, _preview.Height);

    private void ShowHint() =>
        ModeHint.Text = Localizer.Current.T(Model.Mode == EditorMode.Crop ? "editor.hint.crop" : "editor.hint.hide");

    /// <summary>Colours and hidden areas are drawn into the preview; crop is an overlay; turn and mirror are transforms.</summary>
    internal void RenderPreview()
    {
        var edit = Model.ToEdit();
        var factor = _preview.Width / (double)Model.Width;
        using (var copy = (MagickImage)_preview.Clone())
        {
            ImageEdit.Apply(copy, edit with
            {
                Crop = null,
                Rotation = 0,
                FlipHorizontal = false,
                FlipVertical = false,
                Redactions = edit.Redactions.Select(r => new CropRect((int)(r.X * factor), (int)(r.Y * factor),
                    Math.Max(1, (int)(r.Width * factor)), Math.Max(1, (int)(r.Height * factor)))).ToList(),
            });
            // Redrawn on every slider move and only decoded again here: the fastest zlib level is enough.
            copy.Settings.SetDefine(MagickFormat.Png, "compression-level", "1");
            Picture.Source = ToolWindowSupport.Picture(copy.ToByteArray(MagickFormat.Png));
        }

        Turn.Angle = Model.Rotation;
        Mirror.ScaleX = Model.FlipHorizontal ? -1 : 1;
        Mirror.ScaleY = Model.FlipVertical ? -1 : 1;

        // Crop frame and the darkened part that will be cut away.
        if (Model.Crop is { } crop)
        {
            var frame = Model.ToView(crop, StageSize);
            Canvas.SetLeft(CropFrame, frame.X);
            Canvas.SetTop(CropFrame, frame.Y);
            CropFrame.Width = frame.Width;
            CropFrame.Height = frame.Height;
            CropFrame.Visibility = Visibility.Visible;
            Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(StageSize)), new RectangleGeometry(frame));
        }
        else
        {
            CropFrame.Visibility = Visibility.Collapsed;
            Dim.Data = null;
        }

        // Thin outlines around hidden areas, so they can be found again.
        foreach (var mark in _areaMarks) Overlay.Children.Remove(mark);
        _areaMarks.Clear();
        foreach (var area in Model.Redactions)
        {
            var rect = Model.ToView(area, StageSize);
            var mark = new Rectangle { Width = rect.Width, Height = rect.Height, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [2, 2], IsHitTestVisible = false };
            Canvas.SetLeft(mark, rect.X);
            Canvas.SetTop(mark, rect.Y);
            Overlay.Children.Add(mark);
            _areaMarks.Add(mark);
        }
    }

    private void OnDrawStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Overlay);
        Overlay.CaptureMouse();
        Rubber.Visibility = Visibility.Visible;
        UpdateRubber(_dragStart.Value);
    }

    private void OnDrawMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not null) UpdateRubber(e.GetPosition(Overlay));
    }

    private void OnDrawEnd(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start) return;
        _dragStart = null;
        Overlay.ReleaseMouseCapture();
        Rubber.Visibility = Visibility.Collapsed;
        Model.Drawn(new Rect(start, e.GetPosition(Overlay)), StageSize);
    }

    private void UpdateRubber(Point current)
    {
        var rect = new Rect(_dragStart!.Value, current);
        Canvas.SetLeft(Rubber, rect.X);
        Canvas.SetTop(Rubber, rect.Y);
        Rubber.Width = rect.Width;
        Rubber.Height = rect.Height;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var edit = Model.ToEdit();
        Close();
        Saved?.Invoke(edit);
    }
}
