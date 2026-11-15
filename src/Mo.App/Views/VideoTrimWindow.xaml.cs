using System.Windows;
using System.Windows.Threading;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Views;

/// <summary>Choose the first and last moment to keep; "Play selection" shows exactly that part.</summary>
public partial class VideoTrimWindow : Window
{
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _playing;

    public VideoTrimWindow(string path, double duration)
    {
        InitializeComponent();
        Title = Localizer.Current.T("trim.title", System.IO.Path.GetFileName(path));
        Model = new VideoTrimViewModel(duration);
        DataContext = Model;

        Player.Source = new Uri(path);
        Player.MediaOpened += (_, _) => Seek(Model.Start);
        Player.Play();
        Player.Pause();

        // Moving a slider shows that frame.
        StartSlider.ValueChanged += (_, _) => { StopPlaying(); Seek(Model.Start); };
        EndSlider.ValueChanged += (_, _) => { StopPlaying(); Seek(Model.End); };
        _watch.Tick += (_, _) => { if (Player.Position.TotalSeconds >= Model.End) StopPlaying(); };
        Closed += (_, _) => { _watch.Stop(); Player.Close(); };
    }

    public VideoTrimViewModel Model { get; }

    public event Action<IReadOnlyDictionary<string, object?>>? Saved;

    private void Seek(double seconds)
    {
        Player.Position = TimeSpan.FromSeconds(seconds);
        Player.Pause();
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playing) { StopPlaying(); return; }
        Player.Position = TimeSpan.FromSeconds(Model.Start);
        Player.Play();
        _playing = true;
        _watch.Start();
        PlayGlyph.Text = "";
        PlayText.Text = Localizer.Current.T("trim.pause");
    }

    private void StopPlaying()
    {
        if (!_playing) return;
        _playing = false;
        _watch.Stop();
        Player.Pause();
        PlayGlyph.Text = "";
        PlayText.Text = Localizer.Current.T("trim.play");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var options = Model.Options();
        Close();
        Saved?.Invoke(options);
    }
}
