using System.IO;
using System.Windows;
using Mo.App.ViewModels;
using Mo.Engine;
using Mo.Engine.Images;
using Mo.Engine.Pdf;

namespace Mo.App.Tests;

public class ImageEditorViewModelTests
{
    public ImageEditorViewModelTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    [Fact]
    public void A_rectangle_on_the_preview_becomes_original_pixels()
    {
        var model = new ImageEditorViewModel(1600, 800);
        Assert.Equal(new CropRect(200, 100, 400, 200), model.FromView(new Rect(100, 50, 200, 100), new Size(800, 400)));
        // Dragged from bottom right to top left, and past the edge.
        Assert.Equal(new CropRect(0, 0, 400, 200), model.FromView(new Rect(new Point(200, 100), new Point(-50, -50)), new Size(800, 400)));
        // A click without dragging is not an area.
        Assert.Null(model.FromView(new Rect(10, 10, 1, 1), new Size(800, 400)));
    }

    [Fact]
    public void Drawing_crops_or_hides_depending_on_the_mode()
    {
        var model = new ImageEditorViewModel(1000, 500, EditorMode.Crop);
        model.Drawn(new Rect(0, 0, 500, 250), new Size(1000, 500));
        Assert.Equal(new CropRect(0, 0, 500, 250), model.Crop);
        Assert.Empty(model.Redactions);

        model.Mode = EditorMode.Hide;
        model.Drawn(new Rect(10, 10, 100, 100), new Size(1000, 500));
        model.Drawn(new Rect(200, 10, 100, 100), new Size(1000, 500));
        Assert.Equal(2, model.Redactions.Count);
        model.UndoCommand.Execute(null);
        Assert.Single(model.Redactions);
        Assert.True(model.IsHideMode);
    }

    [Fact]
    public void The_edit_describes_every_change_and_skips_a_full_size_crop()
    {
        var model = new ImageEditorViewModel(400, 300) { Crop = new CropRect(0, 0, 400, 300) };
        Assert.False(model.HasChanges);

        model.RotateLeftCommand.Execute(null);
        model.FlipHorizontal = true;
        model.Brightness = 150; // clamped
        model.BlackBars = true;
        model.Redactions.Add(new CropRect(1, 2, 3, 4));
        var edit = model.ToEdit();
        Assert.Null(edit.Crop);
        Assert.Equal(270, edit.Rotation);
        Assert.True(edit.FlipHorizontal);
        Assert.Equal(100, edit.Brightness);
        Assert.Equal("black", edit.RedactionStyle);
        Assert.Equal([new CropRect(1, 2, 3, 4)], edit.Redactions);
        Assert.True(model.HasChanges);

        model.ResetCommand.Execute(null);
        Assert.False(model.HasChanges);
    }

    [Fact]
    public void Preview_redraws_when_something_changes()
    {
        var model = new ImageEditorViewModel(100, 100);
        var redraws = 0;
        model.EditChanged += () => redraws++;
        model.Contrast = 10;
        model.Redactions.Add(new CropRect(0, 0, 10, 10));
        model.Rotation = 90;
        Assert.Equal(3, redraws);
    }
}

public class VideoTrimViewModelTests
{
    public VideoTrimViewModelTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    [Fact]
    public void Start_and_end_stay_in_order_and_inside_the_video()
    {
        var model = new VideoTrimViewModel(60);
        Assert.False(model.HasChanges);
        model.End = 30;
        model.Start = 45;   // cannot pass the end
        Assert.Equal(30 - VideoTrimViewModel.MinimumLength, model.Start, 2);
        model.Start = -5;
        Assert.Equal(0, model.Start);
        model.End = 99;
        Assert.Equal(60, model.End);
        model.Start = 12.346; // kept to hundredths
        Assert.Equal(12.35, model.Start, 2);
        Assert.True(model.HasChanges);
        Assert.Equal(new Dictionary<string, object?> { ["start"] = 12.35, ["end"] = 60.0 }, model.Options());
        Assert.Equal("Length 0:47.6", model.LengthText);
    }

    [Theory]
    [InlineData(5.25, "0:05.2")]
    [InlineData(65.9, "1:05.9")]
    [InlineData(3725.5, "1:02:05.5")]
    public void Times_are_written_like_a_player(double seconds, string text) => Assert.Equal(text, VideoTrimViewModel.Format(seconds));
}

public class PdfPagesViewModelTests
{
    public PdfPagesViewModelTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    [Fact]
    public void Pages_can_be_moved_turned_copied_and_removed()
    {
        var model = new PdfPagesViewModel(3);
        Assert.False(model.HasChanges);
        Assert.Equal("3 pages", model.CountText);

        model.Move(2, 0);                                   // 3 1 2
        model.RotateCommand.Execute(model.Pages[1]);        // page 1 turned
        model.DuplicateCommand.Execute(model.Pages[0]);     // 3 3 1 2
        model.RemoveCommand.Execute(model.Pages[3]);        // 3 3 1
        model.LaterCommand.Execute(model.Pages[2]);         // already last: nothing

        Assert.Equal([new PdfPageChoice(2), new PdfPageChoice(2), new PdfPageChoice(0, 90)], model.Choices());
        Assert.Equal(["Page 3", "Page 3", "Page 1"], model.Pages.Select(p => p.Label));
        Assert.Equal("3 pages", model.CountText);
        Assert.True(model.HasChanges);
    }

    [Fact]
    public void The_last_page_cannot_be_removed()
    {
        var model = new PdfPagesViewModel(1);
        model.RemoveCommand.Execute(model.Pages[0]);
        Assert.Single(model.Pages);
    }
}

public class QrViewModelTests
{
    public QrViewModelTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    [Fact]
    public void Typing_makes_a_readable_code()
    {
        var model = new QrViewModel();
        Assert.False(model.HasCode);
        model.Text = "https://github.com/mohamadmourad91/Mo-Converter";
        Assert.True(model.HasCode);

        var path = Path.Combine(Path.GetTempPath(), $"mo-qr-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, model.Png!);
            Assert.Equal([model.Text], QrCodes.Read(path));
        }
        finally { File.Delete(path); }

        model.Text = new string('x', QrViewModel.MaxLength + 1);
        Assert.False(model.HasCode);
        Assert.Equal("This text is too long for a QR code.", model.Error);
        model.Text = "";
        Assert.Null(model.Error);
    }
}
