using System.Runtime.InteropServices;

namespace Mo.App.Shell;

/// <summary>The few Win32 calls the shell needs. All coordinates are physical pixels (the app is per-monitor v2 aware).</summary>
internal static class Native
{
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B;
    public const int SM_SWAPBUTTON = 23, SM_CXDRAG = 68, SM_CYDRAG = 69;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint GA_ROOT = 2;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// High bit set = key or button is down right now. Unlike keyboard hooks this needs no focus and cannot slow
    /// down the system's input queue, which is why the drag watcher polls instead of installing a low-level hook.
    /// Note: for mouse buttons it reports the PHYSICAL button, so swapped buttons must be handled by the caller.
    /// </summary>
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    public static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    /// <summary>Is the logical primary (usually left) button down? Respects "swap mouse buttons".</summary>
    public static bool PrimaryButtonDown() => IsDown(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON);

    /// <summary>
    /// Makes a window a non-activating tool window: clicking or showing it never takes focus from the app
    /// the user is working in, and it does not appear in the taskbar or Alt+Tab.
    /// </summary>
    public static void MakeNonActivating(IntPtr hwnd)
    {
        var style = IntPtr.Size == 8 ? (long)GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        style |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, (int)style);
    }

    /// <summary>True when the top-level window under the point belongs to this process.</summary>
    public static bool IsOwnWindowAt(POINT point)
    {
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(GetAncestor(hwnd, GA_ROOT), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>Monitor rectangle, work area and DPI scale (1.0 = 96 dpi) of the monitor nearest a point.</summary>
    public static (RECT Bounds, RECT Work, double Scale) MonitorAt(POINT point) => Describe(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST));

    public static (RECT Bounds, RECT Work, double Scale) MonitorOfWindow(IntPtr hwnd) => Describe(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    private static (RECT, RECT, double) Describe(IntPtr monitor)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        // 0 = MDT_EFFECTIVE_DPI, the scale the user picked in Display settings.
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
        return (info.rcMonitor, info.rcWork, scale);
    }
}
