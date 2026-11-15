using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>
/// Transparent, topmost, never-activated window that sits under the cursor while a wheel key is held during a drag.
/// It is the OLE drop target: DragEnter tells whether files are being dragged, DragOver highlights a petal,
/// Drop picks one. It raises events and leaves all decisions to <see cref="WheelController"/>.
/// </summary>
internal partial class WheelOverlay : Window
{
    /// <summary>Window edge length in DIPs: the wheel plus a cancel margin around it.</summary>
    public const double WindowSize = 2 * (WheelView.OuterRadius + 80);

    private const int MorePageDwellMs = 650;

    private readonly DispatcherTimer _dwell = new() { Interval = TimeSpan.FromMilliseconds(MorePageDwellMs) };
    private IReadOnlyList<WheelPetal> _all = [];
    private int _page;
    private bool _open;
    private string _idleHubText = "";

    public WheelOverlay()
    {
        InitializeComponent();
        Title = Localizer.Current.T("app.name");
        Wheel.Clockwise = !Localizer.Current.IsRightToLeft;
        Wheel.RightToLeftText = Localizer.Current.IsRightToLeft;
        SourceInitialized += (_, _) => Native.MakeNonActivating(new WindowInteropHelper(this).Handle);
        _dwell.Tick += (_, _) => TurnPage();

        DragEnter += OnDragEnter;
        DragOver += OnDragOver;
        DragLeave += (_, e) => { SetHighlight(WheelHit.Outside); e.Handled = true; };
        Drop += OnDrop;
    }

    /// <summary>A drag carrying these file paths entered the overlay.</summary>
    public event Action<string[]>? FilesEntered;

    /// <summary>A drag without files entered the overlay.</summary>
    public event Action? NotAFileDrag;

    /// <summary>The user dropped: the chosen petal, or null for the hub / cancel zone.</summary>
    public event Action<WheelPetal?>? Dropped;

    public bool IsWheelOpen => _open;

    /// <summary>Puts the still-invisible overlay under the cursor so the drag enters it.</summary>
    public void Arm(Native.POINT cursor)
    {
        _open = false;
        HideWheel();
        if (!IsVisible) Show();
        ScreenPlacement.CenterOn(this, cursor, new Size(WindowSize, WindowSize), WheelView.OuterRadius + WheelView.PlatePadding + 8);
    }

    /// <summary>
    /// Shows the wheel once far off-screen, so the window handle, fonts, petal drawing and animation are
    /// ready before the first real drag (that first drag cost about 200 ms more than later ones).
    /// Call <see cref="Retract"/> afterwards, once the dispatcher has rendered.
    /// </summary>
    public void Prewarm(IReadOnlyList<WheelPetal> petals)
    {
        if (IsVisible || petals.Count == 0) return;
        Left = -32000;
        Top = -32000;
        Show();
        OpenWheel("", "", petals);
        UpdateLayout();
    }

    /// <summary>Shows petals with a short scale-and-fade animation.</summary>
    public void OpenWheel(string caption, string idleHubText, IReadOnlyList<WheelPetal> petals)
    {
        _all = petals;
        _page = 0;
        _idleHubText = idleHubText;
        Wheel.Caption = caption;
        ShowPage();
        // Short on purpose: the wheel is in the way of the drop until it is fully there.
        Animate(0.94, _open ? 70 : 80);
        _open = true;
    }

    /// <summary>Hides the overlay so normal drag and drop works again.</summary>
    public void Retract()
    {
        _dwell.Stop();
        _open = false;
        HideWheel();
        Wheel.HighlightIndex = WheelHit.Outside;
        if (IsVisible) Hide();
    }

    /// <summary>Removes running animations (they would otherwise keep holding their end value) and hides the wheel.</summary>
    private void HideWheel()
    {
        Wheel.BeginAnimation(OpacityProperty, null);
        Wheel.Opacity = 0;
    }

    private void ShowPage()
    {
        var slots = WheelPager.Page(_all, _page);
        var pages = WheelPager.PageCount(_all.Count);
        Wheel.Petals = slots.Select(s => s.IsMore
            ? new WheelPetal(null, Localizer.Current.T("wheel.more"), Localizer.Current.T("wheel.page", _page % pages + 1, pages), null, "\uE712", true)
            : s.Item!).ToList();
        SetHighlight(WheelHit.Outside);
    }

    private void TurnPage()
    {
        _page++;
        ShowPage();
        Animate(0.94, 70);
    }

    private void Animate(double fromScale, int milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var scale = new DoubleAnimation(fromScale, 1, duration) { EasingFunction = ease };
        WheelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        WheelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
    }

    private void SetHighlight(int index)
    {
        if (Wheel.HighlightIndex != index) Wheel.HighlightIndex = index;
        var petals = Wheel.Petals;
        Wheel.HubText = index switch
        {
            WheelHit.Hub => Localizer.Current.T("wheel.cancel"),
            >= 0 when index < petals.Count => petals[index].Title,
            _ => _idleHubText,
        };

        var onMore = index >= 0 && index < petals.Count && petals[index].IsMore;
        if (onMore && !_dwell.IsEnabled) _dwell.Start();
        else if (!onMore) _dwell.Stop();
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            if (!_open) FilesEntered?.Invoke(paths);
            OnDragOver(sender, e);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            NotAFileDrag?.Invoke();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_open) { e.Effects = DragDropEffects.None; return; }
        var index = Wheel.HitTestPoint(e.GetPosition(Wheel));
        SetHighlight(index);
        var onPetal = index >= 0 && index < Wheel.Petals.Count && !Wheel.Petals[index].IsMore;
        e.Effects = onPetal ? SafeEffect(e.AllowedEffects) : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var index = _open ? Wheel.HitTestPoint(e.GetPosition(Wheel)) : WheelHit.Outside;
        var petal = index >= 0 && index < Wheel.Petals.Count && !Wheel.Petals[index].IsMore ? Wheel.Petals[index] : null;
        e.Effects = petal is null ? DragDropEffects.None : SafeEffect(e.AllowedEffects);
        Dropped?.Invoke(petal);
    }

    /// <summary>
    /// Never report "Move" back to the drag source: Explorer would then delete the originals after a
    /// non-optimised move. Copy (or Link) tells the source to leave its files alone.
    /// </summary>
    private static DragDropEffects SafeEffect(DragDropEffects allowed) =>
        allowed.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy
        : allowed.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link
        : DragDropEffects.None;
}
