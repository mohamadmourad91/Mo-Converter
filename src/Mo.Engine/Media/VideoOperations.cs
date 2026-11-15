namespace Mo.Engine.Media;

/// <summary>Convert wheel: another container. Streams the target can hold are copied (seconds, no quality loss); the rest is re-encoded.</summary>
internal sealed class ConvertVideoOperation(string extension, string badge, string glyph, int order, params string[] aliases) : MediaOperation
{
    public override string Id => "video.to-" + extension;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => glyph;
    public override int Order => order;

    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Video) && !input.AllHaveExtension([extension, .. aliases]);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasVideo) throw new OperationException("error.media-no-video", Path.GetFileName(source));
        var target = context.Output.PathFor(source, extension);
        var head = new List<string> { "-i", source, "-map", "0:v:0" };
        if (info.HasAudio) head.AddRange(["-map", "0:a:0"]);
        head.AddRange(["-map_metadata", "0"]);

        var copyAudio = !info.HasAudio || CanCopy(extension, null, info.AudioCodec);
        var audio = !info.HasAudio ? [] : copyAudio ? ["-c:a", "copy"] : AudioCodec(extension);

        if (CanCopy(extension, info.VideoCodec, null))
        {
            await Run([.. head, "-c:v", "copy", .. audio, .. FastStart(extension), target], target, source, info.Duration, report, cancellation);
        }
        else if (extension == "webm")
        {
            List<string> vp9 = ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", "32", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"];
            await Run([.. head, .. vp9, .. audio, target], target, source, info.Duration, report, cancellation);
        }
        else
        {
            await EncodeH264(video => [.. head, "-vf", EvenSize(), .. video, .. audio, .. FastStart(extension), target],
                info, target, source, info.Duration, report, cancellation);
        }
        return [target];
    }
}

/// <summary>Convert wheel: an animated GIF (12 frames per second, at most 480 px wide) for clips up to a minute.</summary>
internal sealed class VideoToGifOperation : MediaOperation
{
    public const double MaxSeconds = 60;

    public override string Id => "video.to-gif";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "GIF";
    public override string Glyph => "\uE714";
    public override int Order => 50;

    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Video);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasVideo) throw new OperationException("error.media-no-video", Path.GetFileName(source));
        if (info.Duration > MaxSeconds) throw new OperationException("error.gif-too-long", Path.GetFileName(source), (int)MaxSeconds);
        var target = context.Output.PathFor(source, "gif");
        // One shared palette per clip keeps colours clean; Bayer dithering keeps the file small.
        const string filter = "fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4";
        await Run(["-i", source, "-vf", filter, "-loop", "0", target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Convert wheel: the sound track as MP3.</summary>
internal sealed class VideoToMp3Operation : MediaOperation
{
    public override string Id => "video.to-mp3";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "MP3";
    public override string Glyph => "\uE8D6";
    public override int Order => 60;

    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Video);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasAudio) throw new OperationException("error.media-no-audio", Path.GetFileName(source));
        var target = context.Output.PathFor(source, "mp3");
        await Run(["-i", source, "-map", "0:a:0", "-vn", .. AudioCodec("mp3"), target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Base for tools that keep the video's own container where possible.</summary>
internal abstract class VideoToolOperation : MediaOperation
{
    public override WheelKind Wheel => WheelKind.Tools;
    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Video);

    /// <summary>Container for re-encoded results: WEBM stays WEBM, MKV/MOV stay, everything else becomes MP4.</summary>
    protected static string EncodedExtension(string source) => FileTypes.ExtensionOf(source) is "webm" or "mkv" or "mov" or "mp4" ? FileTypes.ExtensionOf(source) : "mp4";

    /// <summary>Re-encodes video with a filter, in H.264 (or VP9 for WEBM), audio copied when the container allows it.</summary>
    protected static async Task Reencode(string source, MediaInfo info, string target, string videoFilter, List<string> audio,
        Action<double> report, CancellationToken cancellation, double bitrateFactor = 1, int vp9Crf = 32, double? duration = null,
        IReadOnlyList<string>? inputOptions = null)
    {
        var extension = FileTypes.ExtensionOf(target);
        var head = new List<string>(inputOptions ?? []) { "-i", source, "-map", "0:v:0" };
        if (info.HasAudio) head.AddRange(["-map", "0:a:0"]);
        var length = duration ?? info.Duration;
        if (extension == "webm")
        {
            List<string> vp9 = ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", vp9Crf.ToString(), "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"];
            await Run([.. head, "-vf", videoFilter, .. vp9, .. audio, target], target, source, length, report, cancellation);
        }
        else
        {
            await EncodeH264(video => [.. head, "-vf", videoFilter + "," + EvenSize(), .. video, .. audio, .. FastStart(extension), target],
                info, target, source, length, report, cancellation, bitrateFactor);
        }
    }

    protected static List<string> AudioFor(string target, MediaInfo info) =>
        !info.HasAudio ? [] : CanCopy(FileTypes.ExtensionOf(target), null, info.AudioCodec) ? ["-c:a", "copy"] : AudioCodec(FileTypes.ExtensionOf(target));
}

/// <summary>Tools wheel: smaller video. Light / balanced / strong lower the bit rate and, for balanced and strong, the size (1920 / 1280 px).</summary>
internal sealed class CompressVideoOperation : VideoToolOperation
{
    public override string Id => "video.compress";
    public override string Glyph => "\uE9A6";
    public override int Order => 10;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var level = context.Option("level", CompressionLevel.Balanced);
        var (factor, maxWidth, crf, audioKbps) = level switch
        {
            CompressionLevel.Light => (0.7, (int?)null, 36, 160),
            CompressionLevel.Strong => (0.3, (int?)1280, 44, 96),
            _ => (0.5, (int?)1920, 40, 128),
        };
        var target = context.Output.PathFor(source, EncodedExtension(source), "compressed");
        var filter = maxWidth is { } w ? $"scale='min(iw,{w})':-2" : "null";
        var audio = info.HasAudio ? AudioCodec(FileTypes.ExtensionOf(target), audioKbps) : [];
        await Reencode(source, info, target, filter, audio, report, cancellation, factor, crf);
        KeepSmaller(source, target);
        return [target];
    }
}

/// <summary>Tools wheel: turns the picture clockwise (option "degrees": 90, 180 or 270).</summary>
internal sealed class RotateVideoOperation : VideoToolOperation
{
    public override string Id => "video.rotate";
    public override string Glyph => "\uE7AD";
    public override int Order => 20;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var degrees = ((context.Option("degrees", 90) % 360) + 360) % 360;
        var filter = degrees switch { 180 => "hflip,vflip", 270 => "transpose=2", _ => "transpose=1" };
        var target = context.Output.PathFor(source, EncodedExtension(source), "rotated");
        await Reencode(source, info, target, filter, AudioFor(target, info), report, cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: plays faster (option "factor", default 2; 0.5 makes it slower), sound pitch kept.</summary>
internal sealed class SpeedVideoOperation : VideoToolOperation
{
    public override string Id => "video.speed";
    public override string Glyph => "\uEC4A";
    public override int Order => 30;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var factor = Math.Clamp(context.Option("factor", 2.0), 0.5, 4.0);
        var target = context.Output.PathFor(source, EncodedExtension(source), Seconds(factor) + "x");
        var audio = info.HasAudio ? (List<string>)["-af", $"atempo={Seconds(factor)}", .. AudioCodec(FileTypes.ExtensionOf(target))] : [];
        await Reencode(source, info, target, $"setpts=PTS/{Seconds(factor)}", audio, report, cancellation, duration: info.Duration / factor);
        return [target];
    }
}

/// <summary>
/// Tools wheel: keeps the part between "start" and "end" (seconds), chosen in the trim window. The cut is exact to the
/// frame because the picture is re-encoded; the sound is copied where the container allows it.
/// </summary>
internal sealed class TrimVideoOperation : VideoToolOperation, IOperation
{
    public override string Id => "video.trim";
    public override string Glyph => "\uE916";
    public override int Order => 15;
    public string? EditorId => "video.trim";

    public override bool Accepts(InputSet input) => input.IsSingle && base.Accepts(input);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var start = Math.Max(0, context.Option("start", -1.0));
        var end = context.Option("end", -1.0);
        if (info.Duration > 0) end = Math.Min(end, info.Duration);
        if (end <= start || context.Option("start", -1.0) < 0) throw new OperationException("error.edit-needs-editor");
        var target = context.Output.PathFor(source, EncodedExtension(source), "trimmed");
        await Reencode(source, info, target, "null", AudioFor(target, info), report, cancellation,
            duration: end - start, inputOptions: ["-ss", Seconds(start), "-to", Seconds(end)]);
        return [target];
    }
}
/// <summary>Tools wheel: removes the sound; the picture is copied untouched.</summary>
internal sealed class MuteVideoOperation : VideoToolOperation
{
    public override string Id => "video.mute";
    public override string Glyph => "\uE74F";
    public override int Order => 40;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "muted");
        await Run(["-i", source, "-map", "0:v", "-c", "copy", "-an", .. FastStart(extension), target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: even loudness (EBU R128, −16 LUFS); the picture is copied untouched.</summary>
internal sealed class NormalizeVideoOperation : VideoToolOperation
{
    public override string Id => "video.normalize";
    public override string Glyph => "\uE767";
    public override int Order => 50;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasAudio) throw new OperationException("error.media-no-audio", Path.GetFileName(source));
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "normalized");
        await Run(["-i", source, "-map", "0:v", "-map", "0:a:0", "-c:v", "copy", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11",
            .. AudioCodec(extension), .. FastStart(extension), target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: still pictures — one per second, at most 600 in total — as JPG in a folder.</summary>
internal sealed class VideoFramesOperation : VideoToolOperation
{
    public const int MaxFrames = 600;

    public override string Id => "video.frames";
    public override string Glyph => "\uE722";
    public override int Order => 60;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasVideo) throw new OperationException("error.media-no-video", Path.GetFileName(source));
        var fps = info.Duration > MaxFrames ? MaxFrames / info.Duration : 1;
        var folder = context.Output.FolderFor(source, "frames");
        var pattern = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + "-%04d.jpg");
        try
        {
            await Ffmpeg.RunAsync(["-i", source, "-vf", $"fps={Seconds(fps)}", "-q:v", "2", pattern], info.Duration, report, cancellation);
        }
        catch (Exception e) when (e is FfmpegException or OperationCanceledException)
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
            if (e is OperationCanceledException) throw;
            throw new OperationException("error.media-failed", Path.GetFileName(source));
        }
        return Directory.GetFiles(folder, "*.jpg").Order(StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Tools wheel: equal parts without re-encoding (option "seconds"; default: halves for clips up to ten minutes,
/// five-minute parts for longer ones). Cuts land on key frames, so lengths are approximate.
/// </summary>
internal sealed class SplitVideoOperation : VideoToolOperation
{
    public override string Id => "video.split";
    public override string Glyph => "\uE8C6";
    public override int Order => 70;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        // Each part should be at least a second long.
        if (info.Duration < 2) throw new OperationException("error.media-too-short", Path.GetFileName(source));
        var seconds = context.Option("seconds", info.Duration <= 600 ? Math.Ceiling(info.Duration / 2) : 300.0);
        var extension = FileTypes.ExtensionOf(source);
        var folder = context.Output.FolderFor(source, "parts");
        var pattern = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + "-part%02d." + extension);
        List<string> args = ["-i", source, "-map", "0:v", "-map", "0:a?", "-c", "copy", "-f", "segment",
            "-segment_time", Seconds(seconds), "-reset_timestamps", "1", pattern];
        try
        {
            await Ffmpeg.RunAsync(args, info.Duration, report, cancellation);
        }
        catch (Exception e) when (e is FfmpegException or OperationCanceledException)
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
            if (e is OperationCanceledException) throw;
            throw new OperationException("error.media-failed", Path.GetFileName(source));
        }
        return Directory.GetFiles(folder).Order(StringComparer.Ordinal).ToList();
    }
}

/// <summary>Tools wheel: removes title, dates, GPS position and chapters; streams are copied untouched.</summary>
internal sealed class StripVideoMetadataOperation : VideoToolOperation
{
    public override string Id => "video.strip-metadata";
    public override string Glyph => "\uE894";
    public override int Order => 80;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "clean");
        await Run(["-i", source, "-map", "0:v", "-map", "0:a?", "-map_metadata", "-1", "-map_chapters", "-1",
            "-c", "copy", "-fflags", "+bitexact", .. FastStart(extension), target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>
/// Tools wheel: two or more clips one after another as one MP4, in the order given. Every clip is fitted into the
/// first clip's frame size (black bars where the shape differs); clips without sound get silence.
/// </summary>
internal sealed class MergeVideosOperation : MediaOperation
{
    public override string Id => "video.merge";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8F1";
    public override int Order => 5;

    public override bool Accepts(InputSet input) => input.Count >= 2 && input.AllAre(FileCategory.Video);

    protected override Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation) =>
        throw new NotSupportedException();

    public override Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var paths = context.Input.Paths;
            var missing = paths.FirstOrDefault(p => !File.Exists(p));
            if (missing is not null) throw new OperationException("error.file-missing", Path.GetFileName(missing));
            var infos = paths.Select(Ffmpeg.Probe).ToList();
            var noVideo = paths.Where((p, i) => !infos[i].HasVideo).FirstOrDefault();
            if (noVideo is not null) throw new OperationException("error.media-no-video", Path.GetFileName(noVideo));

            var width = Math.Max(2, infos[0].Width / 2 * 2);
            var height = Math.Max(2, infos[0].Height / 2 * 2);
            var fps = infos[0].FrameRate is > 1 and < 121 ? infos[0].FrameRate : 30;
            var withAudio = infos.Any(i => i.HasAudio);

            var inputs = new List<string>();
            foreach (var path in paths) inputs.AddRange(["-i", path]);
            var graph = new System.Text.StringBuilder();
            var concat = new System.Text.StringBuilder();
            var silence = paths.Count;
            for (var i = 0; i < paths.Count; i++)
            {
                graph.Append($"[{i}:v:0]scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={Seconds(fps)},format=yuv420p[v{i}];");
                if (withAudio)
                {
                    if (infos[i].HasAudio)
                    {
                        graph.Append($"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];");
                    }
                    else
                    {
                        // A silent track as long as the clip, so picture and sound stay in step.
                        inputs.AddRange(["-f", "lavfi", "-t", Seconds(infos[i].Duration), "-i", "anullsrc=r=48000:cl=stereo"]);
                        graph.Append($"[{silence++}:a]aformat=channel_layouts=stereo[a{i}];");
                    }
                }
                concat.Append(withAudio ? $"[v{i}][a{i}]" : $"[v{i}]");
            }
            graph.Append(concat).Append($"concat=n={paths.Count}:v=1:a={(withAudio ? 1 : 0)}[v]{(withAudio ? "[a]" : "")}");

            var target = context.Output.PathFor(paths[0], "mp4", "merged");
            var total = infos.Sum(i => i.Duration);
            var merged = infos[0] with { Width = width, Height = height, FrameRate = fps, VideoBitRate = (long)infos.Average(i => i.VideoBitRate) };
            List<string> audio = withAudio ? ["-map", "[a]", .. AudioCodec("mp4")] : [];
            await EncodeH264(video => [.. inputs, "-filter_complex", graph.ToString(), "-map", "[v]", .. audio, .. video, .. FastStart("mp4"), target],
                merged, target, paths[0], total, f => context.Report(f), cancellation);
            context.Report(1);
            return OperationResult.Of(target);
        }, cancellation);
}
