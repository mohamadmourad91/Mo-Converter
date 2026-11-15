using System.Windows;
using System.Windows.Interop;

namespace Mo.App.Shell;

/// <summary>
/// Positions WPF windows in physical pixels. WPF's Left/Top are in DIPs of whatever monitor the window
/// currently sits on, which is ambiguous on mixed-DPI setups; SetWindowPos with pixels is not.
/// </summary>
internal static class ScreenPlacement
{
    /// <summary>
    /// Centres a window of <paramref name="dipSize"/> on <paramref name="point"/>, shifted only as far as needed
    /// to keep a circle of <paramref name="keepVisibleRadius"/> DIPs around its centre fully on that monitor.
    /// </summary>
    public static void CenterOn(Window window, Native.POINT point, Size dipSize, double keepVisibleRadius)
    {
        var (bounds, _, scale) = Native.MonitorAt(point);
        var radius = (int)Math.Ceiling(keepVisibleRadius * scale);
        var cx = Clamp(point.X, bounds.Left + radius, bounds.Right - radius);
        var cy = Clamp(point.Y, bounds.Top + radius, bounds.Bottom - radius);
        var width = (int)Math.Round(dipSize.Width * scale);
        var height = (int)Math.Round(dipSize.Height * scale);
        Move(window, cx - width / 2, cy - height / 2, width, height);
    }

    /// <summary>Places a window horizontally centred near the top of the work area of the monitor holding the active window.</summary>
    public static void TopCenterOfActiveMonitor(Window window, double topMarginDip)
    {
        var foreground = Native.GetForegroundWindow();
        var (_, work, scale) = foreground != IntPtr.Zero
            ? Native.MonitorOfWindow(foreground)
            : Native.MonitorAt(Cursor());
        var width = (int)Math.Round(window.ActualWidth * scale);
        var height = (int)Math.Round(window.ActualHeight * scale);
        var x = work.Left + (work.Width - width) / 2;
        var y = work.Top + (int)Math.Round(topMarginDip * scale);
        Move(window, x, y, width, height);
    }

    public static Native.POINT Cursor()
    {
        Native.GetCursorPos(out var p);
        return p;
    }

    private static void Move(Window window, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        const uint flags = Native.SWP_NOACTIVATE;
        // The first call may move the window onto a monitor with another DPI; Windows then sends
        // WM_DPICHANGED synchronously and WPF resizes the window to its own suggestion. The second
        // call puts back the exact rectangle we computed for the target monitor.
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, width, height, flags);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, width, height, flags);
    }

    private static int Clamp(int value, int min, int max) => min > max ? (min + max) / 2 : Math.Clamp(value, min, max);
}
