using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mo.Engine;
using Mo.Engine.Images;
using Mo.Engine.Pdf;

namespace Mo.App.ViewModels;

public enum EditorMode { Crop, Hide }

/// <summary>
/// State of the image editor. All areas are kept in pixels of the upright original, whatever the size on screen,
/// so the saved result matches what the user marked.
/// </summary>
public sealed class ImageEditorViewModel : ObservableObject
{
    private EditorMode _mode;
    private CropRect? _crop;
    private int _rotation;
    private bool _flipHorizontal, _flipVertical, _blackBars;
    private int _brightness, _contrast, _saturation;

    public ImageEditorViewModel(int width, int height, EditorMode mode = EditorMode.Crop)
    {
        Width = width;
        Height = height;
        _mode = mode;
        Redactions.CollectionChanged += (_, _) => Changed();
        RotateLeftCommand = new RelayCommand(() => Rotation = (Rotation + 270) % 360);
        RotateRightCommand = new RelayCommand(() => Rotation = (Rotation + 90) % 360);
        UndoCommand = new RelayCommand(() => Redactions.RemoveAt(Redactions.Count - 1), () => Redactions.Count > 0);
        ResetCommand = new RelayCommand(Reset);
    }

    /// <summary>Size of the upright original in pixels.</summary>
    public int Width { get; }
    public int Height { get; }

    public EditorMode Mode { get => _mode; set { if (Set(ref _mode, value)) { OnPropertyChanged(nameof(IsCropMode)); OnPropertyChanged(nameof(IsHideMode)); } } }
    public bool IsCropMode { get => _mode == EditorMode.Crop; set { if (value) Mode = EditorMode.Crop; } }
    public bool IsHideMode { get => _mode == EditorMode.Hide; set { if (value) Mode = EditorMode.Hide; } }

    public CropRect? Crop { get => _crop; set { if (Set(ref _crop, value)) Changed(); } }
    public ObservableCollection<CropRect> Redactions { get; } = [];

    public int Rotation { get => _rotation; set { if (Set(ref _rotation, ((value % 360) + 360) % 360)) Changed(); } }
    public bool FlipHorizontal { get => _flipHorizontal; set { if (Set(ref _flipHorizontal, value)) Changed(); } }
    public bool FlipVertical { get => _flipVertical; set { if (Set(ref _flipVertical, value)) Changed(); } }
    public bool BlackBars { get => _blackBars; set { if (Set(ref _blackBars, value)) Changed(); } }
    public int Brightness { get => _brightness; set { if (Set(ref _brightness, Math.Clamp(value, -100, 100))) Changed(); } }
    public int Contrast { get => _contrast; set { if (Set(ref _contrast, Math.Clamp(value, -100, 100))) Changed(); } }
    public int Saturation { get => _saturation; set { if (Set(ref _saturation, Math.Clamp(value, -100, 100))) Changed(); } }

    public ICommand RotateLeftCommand { get; }
    public ICommand RotateRightCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand ResetCommand { get; }

    public bool HasChanges => !ToEdit().IsEmpty;

    /// <summary>Raised whenever anything that affects the picture changes (the window redraws its preview).</summary>
    public event Action? EditChanged;

    private void Changed()
    {
        OnPropertyChanged(nameof(HasChanges));
        EditChanged?.Invoke();
    }

    /// <summary>
    /// Turns a rectangle drawn on the preview (whose size is <paramref name="view"/>) into original pixels.
    /// Tiny rectangles (a click without dragging) are ignored and return null.
    /// </summary>
    public CropRect? FromView(Rect drawn, Size view)
    {
        if (view.Width <= 0 || view.Height <= 0) return null;
        var sx = Width / view.Width;
        var sy = Height / view.Height;
        var left = Math.Clamp(Math.Min(drawn.Left, drawn.Right), 0, view.Width);
        var top = Math.Clamp(Math.Min(drawn.Top, drawn.Bottom), 0, view.Height);
        var right = Math.Clamp(Math.Max(drawn.Left, drawn.Right), 0, view.Width);
        var bottom = Math.Clamp(Math.Max(drawn.Top, drawn.Bottom), 0, view.Height);
        var rect = new CropRect((int)Math.Round(left * sx), (int)Math.Round(top * sy),
            (int)Math.Round((right - left) * sx), (int)Math.Round((bottom - top) * sy));
        return rect.Width < 4 || rect.Height < 4 ? null : rect;
    }

    /// <summary>An original-pixel rectangle in preview coordinates (for drawing the overlays).</summary>
    public Rect ToView(CropRect area, Size view) =>
        new(area.X * view.Width / Width, area.Y * view.Height / Height, area.Width * view.Width / Width, area.Height * view.Height / Height);

    /// <summary>A drag on the picture: in crop mode it chooses the part to keep, in hide mode it adds an area.</summary>
    public void Drawn(Rect drawn, Size view)
    {
        var area = FromView(drawn, view);
        if (area is null) return;
        if (Mode == EditorMode.Crop) Crop = area;
        else Redactions.Add(area);
    }

    public ImageEdit ToEdit() => new()
    {
        Crop = _crop is { } c && (c.Width < Width || c.Height < Height) ? c : null,
        Redactions = [.. Redactions],
        RedactionStyle = _blackBars ? "black" : "pixelate",
        Rotation = _rotation,
        FlipHorizontal = _flipHorizontal,
        FlipVertical = _flipVertical,
        Brightness = _brightness,
        Contrast = _contrast,
        Saturation = _saturation,
    };

    private void Reset()
    {
        Crop = null;
        Redactions.Clear();
        Rotation = 0;
        FlipHorizontal = FlipVertical = false;
        Brightness = Contrast = Saturation = 0;
    }
}

/// <summary>The part of a video to keep, in seconds.</summary>
public sealed class VideoTrimViewModel : ObservableObject
{
    /// <summary>Shortest part that can be kept.</summary>
    public const double MinimumLength = 0.2;

    private double _start, _end;

    public VideoTrimViewModel(double duration)
    {
        Duration = Math.Max(duration, MinimumLength);
        _end = Duration;
    }

    public double Duration { get; }

    public double Start
    {
        get => _start;
        set
        {
            var clamped = Math.Clamp(value, 0, _end - MinimumLength);
            if (Set(ref _start, Math.Round(clamped, 2))) Notify();
        }
    }

    public double End
    {
        get => _end;
        set
        {
            var clamped = Math.Clamp(value, _start + MinimumLength, Duration);
            if (Set(ref _end, Math.Round(clamped, 2))) Notify();
        }
    }

    public string StartText => Format(_start);
    public string EndText => Format(_end);
    public string LengthText => Localizer.Current.T("trim.length", Format(_end - _start));

    /// <summary>True when something is cut off (saving the whole video would be pointless).</summary>
    public bool HasChanges => _start > 0.01 || _end < Duration - 0.01;

    private void Notify()
    {
        OnPropertyChanged(nameof(StartText));
        OnPropertyChanged(nameof(EndText));
        OnPropertyChanged(nameof(LengthText));
        OnPropertyChanged(nameof(HasChanges));
    }

    /// <summary>"1:05.3" or "1:02:05.3".</summary>
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}" : $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    public IReadOnlyDictionary<string, object?> Options() => new Dictionary<string, object?> { ["start"] = _start, ["end"] = _end };
}

/// <summary>One page tile in the page arranger.</summary>
public sealed class PageTile(int sourceIndex) : ObservableObject
{
    private int _rotation;
    private ImageSource? _thumbnail;
    private string _label = "";

    public int SourceIndex { get; } = sourceIndex;
    public int Rotation { get => _rotation; set => Set(ref _rotation, ((value % 360) + 360) % 360); }
    public ImageSource? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }
    public string Label { get => _label; set => Set(ref _label, value); }
}

/// <summary>Order, turns, copies and removals of the pages of one PDF.</summary>
public sealed class PdfPagesViewModel : ObservableObject
{
    public PdfPagesViewModel(int pageCount)
    {
        for (var i = 0; i < pageCount; i++) Pages.Add(new PageTile(i));
        Pages.CollectionChanged += (_, _) => Relabel();
        Relabel();
        RotateCommand = new ParameterCommand<PageTile>(t => t.Rotation += 90);
        DuplicateCommand = new ParameterCommand<PageTile>(Duplicate);
        RemoveCommand = new ParameterCommand<PageTile>(t => { if (Pages.Count > 1) Pages.Remove(t); });
        EarlierCommand = new ParameterCommand<PageTile>(t => Move(Pages.IndexOf(t), Pages.IndexOf(t) - 1));
        LaterCommand = new ParameterCommand<PageTile>(t => Move(Pages.IndexOf(t), Pages.IndexOf(t) + 1));
    }

    public ObservableCollection<PageTile> Pages { get; } = [];

    public ICommand RotateCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EarlierCommand { get; }
    public ICommand LaterCommand { get; }

    public string CountText => Localizer.Current.N("pages.count", Pages.Count);

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Pages.Count || to < 0 || to >= Pages.Count || from == to) return;
        Pages.Move(from, to);
    }

    private void Duplicate(PageTile tile)
    {
        var copy = new PageTile(tile.SourceIndex) { Rotation = tile.Rotation, Thumbnail = tile.Thumbnail };
        Pages.Insert(Pages.IndexOf(tile) + 1, copy);
    }

    /// <summary>"Page 3" names the original page, so moved and copied pages stay recognisable.</summary>
    private void Relabel()
    {
        foreach (var tile in Pages) tile.Label = Localizer.Current.T("pages.page", tile.SourceIndex + 1);
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>The pages in their new order, ready for the "pdf.organize" operation.</summary>
    public IReadOnlyList<PdfPageChoice> Choices() => Pages.Select(p => new PdfPageChoice(p.SourceIndex, p.Rotation)).ToList();

    public bool HasChanges => Pages.Count != Pages.Select(p => p.SourceIndex).Distinct().Count()
                              || Pages.Where((p, i) => p.SourceIndex != i || p.Rotation != 0).Any();
}

/// <summary>Text to QR picture, refreshed while typing.</summary>
public sealed class QrViewModel : ObservableObject
{
    /// <summary>A QR code holds at most about 2 300 characters at the error correction used.</summary>
    public const int MaxLength = 2000;

    private string _text = "";
    private byte[]? _png;
    private string? _error;

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            Refresh();
        }
    }

    /// <summary>PNG of the current code, or null when the text is empty or too long.</summary>
    public byte[]? Png { get => _png; private set { if (Set(ref _png, value)) OnPropertyChanged(nameof(HasCode)); } }
    public bool HasCode => _png is not null;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    private void Refresh()
    {
        if (string.IsNullOrWhiteSpace(_text)) { Png = null; Error = null; return; }
        if (_text.Length > MaxLength) { Png = null; Error = Localizer.Current.T("qr.too-long"); return; }
        try
        {
            Png = QrCodes.Png(_text, 12);
            Error = null;
        }
        catch (Exception e) when (e is QRCoder.Exceptions.DataTooLongException or ArgumentException)
        {
            Png = null;
            Error = Localizer.Current.T("qr.too-long");
        }
    }
}

/// <summary>Command with a typed parameter (list item buttons).</summary>
public sealed class ParameterCommand<T>(Action<T> execute) : ICommand where T : class
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => parameter is T;
    public void Execute(object? parameter) { if (parameter is T value) execute(value); }
}
