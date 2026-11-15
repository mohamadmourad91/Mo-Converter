namespace Mo.Engine.Media;

/// <summary>Convert wheel: another audio format. Cover pictures are left out (several formats cannot hold them).</summary>
internal sealed class ConvertAudioOperation(string extension, string badge, int order) : MediaOperation
{
    public override string Id => "audio.to-" + extension;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => "\uE8D6";
    public override int Order => order;

    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Audio) && !input.AllHaveExtension(extension);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        if (!info.HasAudio) throw new OperationException("error.media-no-audio", Path.GetFileName(source));
        var target = context.Output.PathFor(source, extension);
        await Run(["-i", source, "-map", "0:a:0", "-map_metadata", "0", .. AudioCodec(extension), .. FastStart(extension), target],
            target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Base for audio tools: the result keeps the file's own format.</summary>
internal abstract class AudioToolOperation : MediaOperation
{
    /// <summary>Formats FFmpeg can write back (raw .aac uses the ADTS container).</summary>
    protected static readonly string[] Writable = ["mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma"];

    public override WheelKind Wheel => WheelKind.Tools;
    public override bool Accepts(InputSet input) => input.AllAre(FileCategory.Audio) && input.Extensions.All(Writable.Contains);

    /// <summary>Applies an audio filter and re-encodes in the same format.</summary>
    protected static async Task<IReadOnlyList<string>> Filter(OperationContext context, string source, MediaInfo info, string suffix,
        List<string> filterArgs, Action<double> report, CancellationToken cancellation, int? kbps = null)
    {
        if (!info.HasAudio) throw new OperationException("error.media-no-audio", Path.GetFileName(source));
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, suffix);
        await Run(["-i", source, "-map", "0:a:0", "-map_metadata", "0", .. filterArgs, .. AudioCodec(extension, kbps), .. FastStart(extension), target],
            target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: lower bit rate in the same format (lossy formats; FLAC gets the strongest lossless setting). Never bigger than before.</summary>
internal sealed class CompressAudioOperation : AudioToolOperation
{
    public override string Id => "audio.compress";
    public override string Glyph => "\uE9A6";
    public override int Order => 10;

    public override bool Accepts(InputSet input) =>
        input.AllAre(FileCategory.Audio) && input.Extensions.All(e => e is "mp3" or "m4a" or "aac" or "ogg" or "opus" or "flac");

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var level = context.Option("level", CompressionLevel.Balanced);
        var extension = FileTypes.ExtensionOf(source);
        List<string> args = extension == "flac" ? ["-compression_level", "12"] : [];
        int? kbps = extension switch
        {
            "flac" => null,
            "opus" => level switch { CompressionLevel.Light => 96, CompressionLevel.Strong => 48, _ => 64 },
            _ => level switch { CompressionLevel.Light => 160, CompressionLevel.Strong => 96, _ => 128 },
        };
        var outputs = await Filter(context, source, info, "compressed", args, report, cancellation, kbps);
        KeepSmaller(source, outputs[0]);
        return outputs;
    }
}

/// <summary>Tools wheel: even loudness (EBU R128, −16 LUFS, peaks under −1.5 dB).</summary>
internal sealed class NormalizeAudioOperation : AudioToolOperation
{
    public override string Id => "audio.normalize";
    public override string Glyph => "\uE767";
    public override int Order => 20;

    protected override Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation) =>
        Filter(context, source, info, "normalized", ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11"], report, cancellation);
}

/// <summary>Tools wheel: removes silence at the start and pauses longer than 0.7 s (quieter than −50 dB).</summary>
internal sealed class TrimSilenceOperation : AudioToolOperation
{
    public override string Id => "audio.trim-silence";
    public override string Glyph => "\uE916";
    public override int Order => 30;

    protected override Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation) =>
        Filter(context, source, info, "trimmed",
            ["-af", "silenceremove=start_periods=1:start_threshold=-50dB:stop_periods=-1:stop_duration=0.7:stop_threshold=-50dB"],
            report, cancellation);
}

/// <summary>Tools wheel: stereo becomes mono, mono becomes stereo.</summary>
internal sealed class SwitchChannelsOperation : AudioToolOperation
{
    public override string Id => "audio.channels";
    public override string Glyph => "\uE7F6";
    public override int Order => 40;

    protected override Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation) =>
        info.AudioChannels > 1
            ? Filter(context, source, info, "mono", ["-ac", "1"], report, cancellation)
            : Filter(context, source, info, "stereo", ["-ac", "2"], report, cancellation);
}

/// <summary>Tools wheel: removes title, artist, album, cover picture and other tags; the sound is copied untouched.</summary>
internal sealed class StripAudioMetadataOperation : AudioToolOperation
{
    public override string Id => "audio.strip-metadata";
    public override string Glyph => "\uE894";
    public override int Order => 60;

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "clean");
        List<string> id3 = extension == "mp3" ? ["-write_id3v1", "0", "-id3v2_version", "0"] : [];
        await Run(["-i", source, "-map", "0:a", "-map_metadata", "-1", "-map_chapters", "-1", "-c", "copy", "-fflags", "+bitexact",
            .. id3, .. FastStart(extension), target], target, source, info.Duration, report, cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: two or more recordings one after another, in the first file's format.</summary>
internal sealed class MergeAudioOperation : MediaOperation
{
    public override string Id => "audio.merge";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8F1";
    public override int Order => 5;

    public override bool Accepts(InputSet input) => input.Count >= 2 && input.AllAre(FileCategory.Audio);

    protected override Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, MediaInfo info, Action<double> report, CancellationToken cancellation) =>
        throw new NotSupportedException();

    public override Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var paths = context.Input.Paths;
            var missing = paths.FirstOrDefault(p => !File.Exists(p));
            if (missing is not null) throw new OperationException("error.file-missing", Path.GetFileName(missing));
            var infos = paths.Select(Ffmpeg.Probe).ToList();
            var silent = paths.Where((p, i) => !infos[i].HasAudio).FirstOrDefault();
            if (silent is not null) throw new OperationException("error.media-no-audio", Path.GetFileName(silent));

            var extension = FileTypes.ExtensionOf(paths[0]);
            if (extension is "wma") extension = "mp3";
            var inputs = paths.SelectMany(p => new[] { "-i", p }).ToList();
            var graph = string.Concat(paths.Select((_, i) => $"[{i}:a:0]aresample=48000,aformat=channel_layouts=stereo[a{i}];"))
                + string.Concat(paths.Select((_, i) => $"[a{i}]")) + $"concat=n={paths.Count}:v=0:a=1[a]";

            var target = context.Output.PathFor(paths[0], extension, "merged");
            await Run([.. inputs, "-filter_complex", graph, "-map", "[a]", .. AudioCodec(extension), .. FastStart(extension), target],
                target, paths[0], infos.Sum(i => i.Duration), f => context.Report(f), cancellation);
            context.Report(1);
            return OperationResult.Of(target);
        }, cancellation);
}

/// <summary>Registers every video and audio operation.</summary>
public static class MediaModule
{
    public static void Register(OperationRegistry registry)
    {
        registry
            .Add(new ConvertVideoOperation("mp4", "MP4", "\uE714", 10, "m4v"))
            .Add(new ConvertVideoOperation("mov", "MOV", "\uE714", 20))
            .Add(new ConvertVideoOperation("mkv", "MKV", "\uE714", 30))
            .Add(new ConvertVideoOperation("webm", "WEBM", "\uE714", 40))
            .Add(new VideoToGifOperation())
            .Add(new VideoToMp3Operation())
            .Add(new MergeVideosOperation())
            .Add(new CompressVideoOperation())
            .Add(new RotateVideoOperation())
            .Add(new TrimVideoOperation())
            .Add(new SpeedVideoOperation())
            .Add(new MuteVideoOperation())
            .Add(new NormalizeVideoOperation())
            .Add(new VideoFramesOperation())
            .Add(new SplitVideoOperation())
            .Add(new StripVideoMetadataOperation())
            .Add(new ConvertAudioOperation("mp3", "MP3", 10))
            .Add(new ConvertAudioOperation("m4a", "M4A", 20))
            .Add(new ConvertAudioOperation("wav", "WAV", 30))
            .Add(new ConvertAudioOperation("flac", "FLAC", 40))
            .Add(new ConvertAudioOperation("ogg", "OGG", 50))
            .Add(new ConvertAudioOperation("opus", "OPUS", 60))
            .Add(new MergeAudioOperation())
            .Add(new CompressAudioOperation())
            .Add(new NormalizeAudioOperation())
            .Add(new TrimSilenceOperation())
            .Add(new SwitchChannelsOperation())
            .Add(new StripAudioMetadataOperation());
    }
}
