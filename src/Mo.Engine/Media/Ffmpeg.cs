using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mo.Engine.Media;

/// <summary>What ffprobe reports about a file.</summary>
public sealed record MediaInfo(
    double Duration,
    bool HasVideo,
    bool HasAudio,
    int Width,
    int Height,
    double FrameRate,
    int AudioChannels,
    string? VideoCodec,
    string? AudioCodec,
    long BitRate,
    long VideoBitRate);

/// <summary>
/// Runs the FFmpeg command-line tools that ship next to Mo Converter (LGPL build, separate processes).
/// Looked up in: MO_FFMPEG_DIR, "ffmpeg" next to the program, tools\ffmpeg\bin of the source tree, then PATH.
/// </summary>
public static class Ffmpeg
{
    private static readonly Lazy<(string Ffmpeg, string Ffprobe)?> Tools = new(Locate);
    private static readonly Lazy<HashSet<string>> EncoderSet = new(ReadEncoders);

    public static bool IsAvailable => Tools.Value is not null;

    private static (string, string) Require() =>
        Tools.Value ?? throw new OperationException("error.ffmpeg-missing");

    private static (string Ffmpeg, string Ffprobe)? Locate()
    {
        var folders = new List<string>();
        if (Environment.GetEnvironmentVariable("MO_FFMPEG_DIR") is { Length: > 0 } custom) folders.Add(custom);
        folders.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg"));
        folders.Add(AppContext.BaseDirectory);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            folders.Add(Path.Combine(dir.FullName, "tools", "ffmpeg", "bin"));
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var folder in folders)
        {
            var ffmpeg = Path.Combine(folder, "ffmpeg.exe");
            var ffprobe = Path.Combine(folder, "ffprobe.exe");
            if (File.Exists(ffmpeg) && File.Exists(ffprobe)) return (ffmpeg, ffprobe);
        }
        return null;
    }

    /// <summary>Names of the encoders this FFmpeg build has (h264_mf, libopus, ...).</summary>
    public static IReadOnlySet<string> Encoders => EncoderSet.Value;

    private static HashSet<string> ReadEncoders()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Tools.Value is not { } tools) return set;
        var (code, output, _) = RunToEnd(tools.Ffmpeg, ["-hide_banner", "-encoders"], TimeSpan.FromSeconds(20));
        if (code != 0) return set;
        foreach (var line in output.Split('\n'))
        {
            // " V....D h264_mf   H264 via MediaFoundation (codec h264)"
            var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6 && parts[0][0] is 'V' or 'A' or 'S') set.Add(parts[1]);
        }
        return set;
    }

    /// <summary>Reads duration, streams and sizes. Unreadable files become a localized error.</summary>
    public static MediaInfo Probe(string path)
    {
        var (_, ffprobe) = Require();
        var (code, output, _) = RunToEnd(ffprobe,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", Path.GetFullPath(path)], TimeSpan.FromSeconds(60));
        if (code != 0 || output.Length == 0) throw new OperationException("error.media-unreadable", Path.GetFileName(path));

        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : [];
            // Cover art in audio files is a video stream marked as an attached picture; it is not video.
            var video = streams.FirstOrDefault(x => Text(x, "codec_type") == "video" && !IsAttachedPicture(x));
            var audio = streams.FirstOrDefault(x => Text(x, "codec_type") == "audio");
            var format = root.TryGetProperty("format", out var f) ? f : default;

            var duration = Number(format, "duration");
            if (duration <= 0 && video.ValueKind != JsonValueKind.Undefined) duration = Number(video, "duration");
            if (duration <= 0 && audio.ValueKind != JsonValueKind.Undefined) duration = Number(audio, "duration");

            return new MediaInfo(
                Duration: duration,
                HasVideo: video.ValueKind != JsonValueKind.Undefined,
                HasAudio: audio.ValueKind != JsonValueKind.Undefined,
                Width: (int)Number(video, "width"),
                Height: (int)Number(video, "height"),
                FrameRate: Rate(Text(video, "avg_frame_rate")) is > 0 and var r ? r : Rate(Text(video, "r_frame_rate")),
                AudioChannels: (int)Number(audio, "channels"),
                VideoCodec: Text(video, "codec_name"),
                AudioCodec: Text(audio, "codec_name"),
                BitRate: (long)Number(format, "bit_rate"),
                VideoBitRate: (long)Number(video, "bit_rate"));
        }
        catch (JsonException)
        {
            throw new OperationException("error.media-unreadable", Path.GetFileName(path));
        }
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() == 1;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
        return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static double Rate(string? fraction)
    {
        if (fraction is null) return 0;
        var parts = fraction.Split('/');
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b > 0
            ? a / b : 0;
    }

    /// <summary>
    /// Runs ffmpeg with the given arguments (never overwriting: -n) and reports progress 0..1 against
    /// <paramref name="duration"/>. A failing run throws <see cref="FfmpegException"/> with FFmpeg's message;
    /// cancelling stops FFmpeg at once.
    /// </summary>
    public static async Task RunAsync(IReadOnlyList<string> arguments, double duration, Action<double>? progress, CancellationToken cancellation)
    {
        var (ffmpeg, _) = Require();
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-hide_banner", "-nostdin", "-n", "-loglevel", "error", "-nostats", "-progress", "pipe:1" })
            start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new OperationException("error.ffmpeg-missing");
        var errors = new StringBuilder();
        var errorTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                lock (errors) if (errors.Length < 8000) errors.AppendLine(line);
        });

        using var registration = cancellation.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });

        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            // "out_time_us=12345678" (microseconds of output written so far)
            if (progress is not null && duration > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(12), out var micros) && micros >= 0)
                progress(Math.Clamp(micros / 1_000_000.0 / duration, 0, 1));
        }

        await process.WaitForExitAsync(CancellationToken.None);
        await errorTask;
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new FfmpegException(errors.ToString().Trim());
    }

    private static (int Code, string Output, string Error) RunToEnd(string exe, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "", "timeout");
        }
        return (process.ExitCode, output.Result, error.Result);
    }
}

/// <summary>FFmpeg exited with an error; <see cref="Exception.Message"/> holds what it printed.</summary>
public sealed class FfmpegException(string message) : Exception(message);
