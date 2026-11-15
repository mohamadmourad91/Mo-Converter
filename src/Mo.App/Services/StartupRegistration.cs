using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Mo.App.Services;

/// <summary>
/// "Start with Windows". Installed normally: the Run value HKCU\Software\Microsoft\Windows\CurrentVersion\Run "Mo Converter".
/// Inside the Microsoft Store package: the package's own startup task (<see cref="StartupTaskId"/> in the manifest),
/// because a Run value pointing into the package folder does not start a packaged app reliably.
/// </summary>
public static class StartupRegistration
{
    /// <summary>Must match the TaskId of the windows.startupTask extension in AppxManifest.xml.</summary>
    public const string StartupTaskId = "MoConverterStartup";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mo Converter";

    private static string Command => "\"" + (Environment.ProcessPath ?? "") + "\"";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    /// <summary>True when running from an MSIX package (the Store version).</summary>
    public static bool IsPackaged
    {
        get
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) != NoPackage;
        }
    }

    /// <summary>True when Windows will start Mo Converter at sign-in.</summary>
    public static bool IsEnabled()
    {
        try
        {
            if (IsPackaged) return Task.Run(async () => (await StartupTask.GetAsync(StartupTaskId)).State).GetAwaiter().GetResult()
                is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value &&
                   string.Equals(value.Trim(), Command, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Turns it on or off. A user can switch a packaged startup task off in Windows Settings; the app may not
    /// switch it back on, so that case throws <see cref="UnauthorizedAccessException"/> (shown as a save error).
    /// </summary>
    public static void Set(bool enabled)
    {
        if (IsPackaged)
        {
            SetPackaged(enabled);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else if (IsEnabled()) key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static void SetPackaged(bool enabled)
    {
        var state = Task.Run(async () =>
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (!enabled)
            {
                task.Disable();
                return task.State;
            }
            return await task.RequestEnableAsync();
        }).GetAwaiter().GetResult();

        if (enabled && state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
            throw new UnauthorizedAccessException("Windows does not allow Mo Converter to start with Windows (Settings > Apps > Startup).");
    }
}
