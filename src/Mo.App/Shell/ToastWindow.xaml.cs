using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>Non-activating status card at the top centre of the active monitor. Click to dismiss.</summary>
internal partial class ToastWindow : Window
{
    private const double TopMargin = 6;

    public ToastWindow(ToastViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Title = Localizer.Current.T("app.name");
        FlowDirection = Localizer.Current.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        SourceInitialized += (_, _) => Native.MakeNonActivating(new WindowInteropHelper(this).Handle);
        // Keep it centred when the text changes its size.
        SizeChanged += (_, _) => { if (IsVisible) ScreenPlacement.TopCenterOfActiveMonitor(this, TopMargin); };
        MouseLeftButtonUp += (_, _) => FadeOut();
    }

    /// <summary>
    /// Creates the window handle, styles and layout once without showing anything (opacity 0, hidden again
    /// before the dispatcher runs), so the first real card after a start appears as fast as later ones.
    /// </summary>
    internal void Prewarm()
    {
        if (IsVisible) return;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        UpdateLayout();
        Hide();
    }

    public void ShowCard()
    {
        var wasVisible = IsVisible;
        if (!wasVisible) Show();
        UpdateLayout();
        ScreenPlacement.TopCenterOfActiveMonitor(this, TopMargin);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(wasVisible ? 60 : 160)));
    }

    public void FadeOut()
    {
        if (!IsVisible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (Opacity < 0.01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Cursor = Cursors.Hand;
    }
}
