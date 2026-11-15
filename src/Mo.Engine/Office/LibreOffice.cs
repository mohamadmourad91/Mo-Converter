using System.Diagnostics;
using Microsoft.Win32;

namespace Mo.Engine.Office;

/// <summary>
/// Converts office documents with an installed LibreOffice (headless). LibreOffice is not shipped with Mo Converter;
/// when it is missing, the operations that need it are simply not offered. Every run uses its own temporary
/// profile, so an open LibreOffice window is never disturbed.
/// </summary>
public static class LibreOffice
{
    private static readonly Lazy<string?> Executable = new(Locate);

    /// <summary>Overrides detection (tests); null restores it.</summary>
    internal static Func<bool>? AvailabilityOverride { get; set; }

    public static bool IsAvailable => AvailabilityOverride?.Invoke() ?? Executable.Value is not null;

    private static string? Locate()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("MO_SOFFICE") is { Length: > 0 } custom) candidates.Add(custom);
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath");
                if (key?.GetValue(null) is string path) candidates.Add(Path.Combine(path, "soffice.exe"));
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }

        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            candidates.Add(Path.Combine(Environment.GetFolderPath(programs), "LibreOffice", "program", "soffice.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Converts <paramref name="source"/> to <paramref name="format"/> (a LibreOffice filter such as "pdf", "docx" or
    /// "txt:Text (encoded):UTF8") and moves the result to <paramref name="target"/>.
    /// </summary>
    public static async Task ConvertAsync(string source, string format, string target, CancellationToken cancellation)
    {
        var soffice = Executable.Value ?? throw new OperationException("error.office-missing");
        var work = Directory.CreateTempSubdirectory("mo-office-").FullName;
        try
        {
            var outDir = Path.Combine(work, "out");
            Directory.CreateDirectory(outDir);
            // A plain ASCII copy name avoids problems with unusual characters in LibreOffice's command line.
            var input = Path.Combine(work, "input" + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, input);

            var start = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[]
            {
                "-env:UserInstallation=" + new Uri(Path.Combine(work, "profile")).AbsoluteUri,
                "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
                "--convert-to", format, "--outdir", outDir, input,
            })
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw new OperationException("error.office-missing");
            using (cancellation.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
            {
                var finished = await Task.WhenAny(process.WaitForExitAsync(CancellationToken.None), Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None));
                cancellation.ThrowIfCancellationRequested();
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    throw new OperationException("error.office-failed", Path.GetFileName(source));
                }
            }

            var produced = Directory.GetFiles(outDir).FirstOrDefault();
            if (produced is null || new FileInfo(produced).Length == 0) throw new OperationException("error.office-failed", Path.GetFileName(source));
            File.Move(produced, target);
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
