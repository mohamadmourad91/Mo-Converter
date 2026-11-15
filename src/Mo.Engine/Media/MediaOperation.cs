using System.Globalization;

namespace Mo.Engine.Media;

/// <summary>Base for video and audio operations: probes each file, runs FFmpeg, reports progress, cleans up on failure.</summary>
internal abstract class MediaOperation : IOperation
{
    public abstract string Id { get; }
    public abstract WheelKind Wheel { get; }
    public string TitleKey => "op." + Id;
    public virtual string? Badge => null;
    public abstract string Glyph { get; }
    public abstract int Order { get; }

    public abstract bool Accepts(InputSet input);

    /// <summary>Handles one file; <paramref name="report"/> takes this file's progress 0..1. Returns the paths written.</summary>
    protected abstract Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation);

    public virtual Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var outputs = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = paths[i];
                if (!File.Exists(source)) throw new OperationException("error.file-missing", Path.GetFileName(source));
                var index = i;
                context.Report((double)i / paths.Count, Path.GetFileName(source));
                var info = Ffmpeg.Probe(source);
                outputs.AddRange(await ProcessAsync(context, source, info, f => context.Report((index + f) / paths.Count), cancellation));
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);

    // ---------- helpers shared by all media operations ----------

    /// <summary>Runs FFmpeg for one output file; deletes it when FFmpeg fails or is cancelled.</summary>
    protected static async Task Run(List<string> arguments, string target, string source, double duration, Action<double> report, CancellationToken cancellation)
    {
        try
        {
            await Ffmpeg.RunAsync(arguments, duration, report, cancellation);
        }
        catch (FfmpegException)
        {
            TryDelete(target);
            throw new OperationException("error.media-failed", Path.GetFileName(source));
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    /// <summary>
    /// Encodes H.264 with the first encoder that works on this PC: Windows' own encoder (Media Foundation),
    /// then OpenH264, then plain MPEG-4 as a last resort. <paramref name="build"/> returns the full argument list
    /// for an encoder's video arguments.
    /// </summary>
    protected static async Task EncodeH264(Func<List<string>, List<string>> build, MediaInfo info, string target, string source,
        double duration, Action<double> report, CancellationToken cancellation, double bitrateFactor = 1)
    {
        var kbps = VideoKbps(info, bitrateFactor);
        var candidates = new List<List<string>>();
        if (Ffmpeg.Encoders.Contains("h264_mf")) candidates.Add(["-c:v", "h264_mf", "-b:v", kbps + "k", "-hw_encoding", "0"]);
        if (Ffmpeg.Encoders.Contains("libopenh264")) candidates.Add(["-c:v", "libopenh264", "-b:v", kbps + "k", "-pix_fmt", "yuv420p"]);
        candidates.Add(["-c:v", "mpeg4", "-b:v", kbps + "k", "-pix_fmt", "yuv420p"]);

        OperationException? last = null;
        foreach (var video in candidates)
        {
            try
            {
                await Run(build(video), target, source, duration, report, cancellation);
                return;
            }
            catch (OperationException e) when (e.Key == "error.media-failed")
            {
                last = e; // try the next encoder
            }
        }
        throw last!;
    }

    /// <summary>Target video bit rate: the source's own rate when known, otherwise about 0.1 bit per pixel and frame.</summary>
    protected static int VideoKbps(MediaInfo info, double factor)
    {
        var source = info.VideoBitRate > 0 ? info.VideoBitRate
            : info.BitRate > 0 ? Math.Max(info.BitRate - 160_000, info.BitRate / 2)
            : (long)(Math.Max(info.Width * info.Height, 320 * 240) * Math.Clamp(info.FrameRate, 10, 60) * 0.1);
        return (int)Math.Clamp(source * factor / 1000, 250, 25_000);
    }

    /// <summary>H.264 needs even width and height; optionally caps the width.</summary>
    protected static string EvenSize(int? maxWidth = null) => maxWidth is { } w
        ? $"scale='min(iw,{w})':-2,scale=trunc(iw/2)*2:trunc(ih/2)*2"
        : "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    /// <summary>Audio encoder arguments for a file extension.</summary>
    protected static List<string> AudioCodec(string extension, int? kbps = null) => extension switch
    {
        "mp3" => kbps is { } k ? ["-c:a", "libmp3lame", "-b:a", k + "k"] : ["-c:a", "libmp3lame", "-q:a", "2"],
        "m4a" or "aac" or "mp4" or "mov" or "m4v" or "mkv" => ["-c:a", "aac", "-b:a", (kbps ?? 192) + "k"],
        "wav" => ["-c:a", "pcm_s16le"],
        "flac" => ["-c:a", "flac", "-compression_level", "8"],
        "ogg" => ["-c:a", "libvorbis", "-b:a", (kbps ?? 192) + "k"],
        "opus" or "webm" => ["-c:a", "libopus", "-b:a", (kbps ?? 128) + "k"],
        "wma" or "wmv" => ["-c:a", "wmav2", "-b:a", (kbps ?? 192) + "k"],
        "avi" => ["-c:a", "libmp3lame", "-q:a", "2"],
        _ => ["-c:a", "aac", "-b:a", (kbps ?? 192) + "k"],
    };

    /// <summary>Can this container hold these streams without re-encoding?</summary>
    protected static bool CanCopy(string container, string? video, string? audio)
    {
        static bool In(string? codec, params string[] allowed) => codec is null || allowed.Contains(codec);
        return container switch
        {
            "mp4" or "mov" or "m4v" => In(video, "h264", "hevc", "mpeg4", "av1") && In(audio, "aac", "mp3", "alac", "ac3", "eac3"),
            "webm" => In(video, "vp8", "vp9", "av1") && In(audio, "opus", "vorbis"),
            "mkv" => true,
            _ => false,
        };
    }

    protected static List<string> FastStart(string extension) =>
        extension is "mp4" or "mov" or "m4v" or "m4a" ? ["-movflags", "+faststart"] : [];

    protected static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    protected static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>When the result is not smaller than the original, the original is kept instead (same format only).</summary>
    protected static void KeepSmaller(string source, string target)
    {
        if (!File.Exists(target) || new FileInfo(target).Length < new FileInfo(source).Length) return;
        if (!string.Equals(Path.GetExtension(source), Path.GetExtension(target), StringComparison.OrdinalIgnoreCase)) return;
        File.Delete(target);
        File.Copy(source, target);
    }
}
