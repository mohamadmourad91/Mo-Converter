using Mo.Engine.Media;

namespace Mo.Engine.Tests.Media;

/// <summary>Sample media made with FFmpeg itself (run build/get-ffmpeg.ps1 once if FFmpeg is missing).</summary>
public sealed class MediaFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-media-").FullName;

    /// <summary>3 s, 320×240, 25 fps, H.264 + AAC stereo, key frame every second, title "Secret title".</summary>
    public string Clip { get; }
    /// <summary>2 s, 320×240, H.264, no sound.</summary>
    public string Silent { get; }
    /// <summary>2 s, VP9 + Opus.</summary>
    public string Webm { get; }
    /// <summary>65 s, tiny, for the GIF length limit.</summary>
    public string Long { get; }
    /// <summary>3 s stereo MP3 with title "Secret song".</summary>
    public string Song { get; }
    /// <summary>4 s WAV: silence, tone (1–2 s), silence, tone (3.5–4 s).</summary>
    public string Pauses { get; }
    /// <summary>2 s mono WAV.</summary>
    public string Mono { get; }
    public string Broken { get; }

    public MediaFixture()
    {
        Assert.True(Ffmpeg.IsAvailable, "FFmpeg not found. Run build/get-ffmpeg.ps1 first.");
        const string picture = "testsrc2=size=320x240:rate=25";
        const string tone = "sine=frequency=440:sample_rate=48000";

        Clip = Make("clip.mp4", ["-f", "lavfi", "-i", picture, "-f", "lavfi", "-i", tone, "-ac", "2", "-t", "3",
            "-c:v", "libopenh264", "-g", "25", "-c:a", "aac", "-metadata", "title=Secret title"]);
        Silent = Make("silent.mp4", ["-f", "lavfi", "-i", picture, "-t", "2", "-c:v", "libopenh264", "-g", "25"]);
        Webm = Make("clip.webm", ["-f", "lavfi", "-i", picture, "-f", "lavfi", "-i", tone, "-t", "2",
            "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-c:a", "libopus"]);
        Long = Make("long.mp4", ["-f", "lavfi", "-i", "testsrc2=size=64x48:rate=5", "-t", "65", "-c:v", "mpeg4"]);
        Song = Make("song.mp3", ["-f", "lavfi", "-i", tone, "-ac", "2", "-t", "3", "-c:a", "libmp3lame", "-b:a", "192k",
            "-metadata", "title=Secret song", "-metadata", "artist=Secret artist"]);
        Pauses = Make("pauses.wav", ["-f", "lavfi", "-i",
            "aevalsrc='(between(t,1,2)+between(t,3.5,4))*0.5*sin(2*PI*440*t)|(between(t,1,2)+between(t,3.5,4))*0.5*sin(2*PI*440*t)':s=48000:d=4"]);
        Mono = Make("mono.wav", ["-f", "lavfi", "-i", tone, "-ac", "1", "-t", "2"]);
        Broken = Path.Combine(Folder, "broken.mp4");
        File.WriteAllBytes(Broken, [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    }

    private string Make(string name, List<string> arguments)
    {
        var path = Path.Combine(Folder, name);
        Ffmpeg.RunAsync([.. arguments, path], 0, null, CancellationToken.None).GetAwaiter().GetResult();
        return path;
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose() => Directory.Delete(Folder, true);
}

public sealed class MediaOperationTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, params string[] files) =>
        (await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet(files), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None)).Outputs;

    private async Task<MediaInfo> RunOne(string id, string file) => Ffmpeg.Probe(Assert.Single(await Run(id, file)));

    // ---------- wheels ----------

    [Fact]
    public void A_video_gets_conversions_and_tools()
    {
        var input = new InputSet(["holiday.mp4"]);
        Assert.Equal(["video.to-mov", "video.to-mkv", "video.to-webm", "video.to-gif", "video.to-mp3"],
            Registry.Match(WheelKind.Convert, input).Select(o => o.Id));
        Assert.Equal(["video.compress", "video.trim", "video.rotate", "video.speed", "video.mute", "video.normalize", "video.frames", "video.split", "video.strip-metadata"],
            Registry.Match(WheelKind.Tools, input).Select(o => o.Id));
        Assert.Contains(Registry.Match(WheelKind.Tools, new InputSet(["a.mp4", "b.mov"])), o => o.Id == "video.merge");
    }

    [Fact]
    public void An_audio_file_gets_conversions_and_tools()
    {
        var input = new InputSet(["song.mp3"]);
        Assert.Equal(["audio.to-m4a", "audio.to-wav", "audio.to-flac", "audio.to-ogg", "audio.to-opus"],
            Registry.Match(WheelKind.Convert, input).Select(o => o.Id));
        Assert.Equal(["audio.compress", "audio.normalize", "audio.trim-silence", "audio.channels", "audio.strip-metadata"],
            Registry.Match(WheelKind.Tools, input).Select(o => o.Id));
        Assert.DoesNotContain(Registry.Match(WheelKind.Tools, new InputSet(["a.wav"])), o => o.Id == "audio.compress");
        Assert.Contains(Registry.Match(WheelKind.Tools, new InputSet(["a.mp3", "b.wav"])), o => o.Id == "audio.merge");
    }

    // ---------- video conversions ----------

    [Fact]
    public async Task Mp4_to_mkv_copies_the_streams_unchanged()
    {
        var info = await RunOne("video.to-mkv", fixture.Clip);
        Assert.Equal(("h264", "aac"), (info.VideoCodec, info.AudioCodec));
        Assert.InRange(info.Duration, 2.8, 3.2);
    }

    [Fact]
    public async Task Webm_to_mp4_is_re_encoded_as_h264_with_aac()
    {
        var info = await RunOne("video.to-mp4", fixture.Webm);
        Assert.Contains(info.VideoCodec, new[] { "h264", "mpeg4" });
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal((320, 240), (info.Width, info.Height));
        Assert.InRange(info.Duration, 1.8, 2.3);
    }

    [Fact]
    public async Task Mp4_to_webm_uses_vp9_and_opus()
    {
        var info = await RunOne("video.to-webm", fixture.Clip);
        Assert.Equal(("vp9", "opus"), (info.VideoCodec, info.AudioCodec));
    }

    [Fact]
    public async Task Mov_keeps_picture_and_sound()
    {
        var info = await RunOne("video.to-mov", fixture.Clip);
        Assert.True(info.HasVideo && info.HasAudio);
    }

    [Fact]
    public async Task Gif_is_animated_and_at_most_480_wide()
    {
        var info = await RunOne("video.to-gif", fixture.Clip);
        Assert.Equal("gif", info.VideoCodec);
        Assert.True(info.Width <= 480);
    }

    [Fact]
    public async Task Long_clips_are_refused_as_gif_with_the_limit()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("video.to-gif", fixture.Long));
        Assert.Equal("error.gif-too-long", error.Key);
        Assert.Equal(60, error.Args[1]);
    }

    [Fact]
    public async Task Sound_track_becomes_mp3()
    {
        var info = await RunOne("video.to-mp3", fixture.Clip);
        Assert.Equal("mp3", info.AudioCodec);
        Assert.False(info.HasVideo);
    }

    [Fact]
    public async Task A_silent_video_has_no_sound_to_extract()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("video.to-mp3", fixture.Silent));
        Assert.Equal("error.media-no-audio", error.Key);
    }

    // ---------- video tools ----------

    [Fact]
    public async Task Compress_never_returns_a_bigger_file()
    {
        var output = Assert.Single(await Run("video.compress", fixture.Clip));
        Assert.True(new FileInfo(output).Length <= new FileInfo(fixture.Clip).Length);
        Assert.True(Ffmpeg.Probe(output).HasVideo);
    }

    [Fact]
    public async Task Rotate_turns_landscape_into_portrait()
    {
        var info = await RunOne("video.rotate", fixture.Clip);
        Assert.Equal((240, 320), (info.Width, info.Height));
        Assert.True(info.HasAudio);
    }

    [Fact]
    public async Task Double_speed_halves_the_length_and_keeps_the_sound()
    {
        var info = await RunOne("video.speed", fixture.Clip);
        Assert.InRange(info.Duration, 1.3, 1.8);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public async Task Mute_removes_only_the_sound()
    {
        var info = await RunOne("video.mute", fixture.Clip);
        Assert.False(info.HasAudio);
        Assert.Equal("h264", info.VideoCodec);
    }

    [Fact]
    public async Task Normalize_keeps_picture_and_sound()
    {
        var info = await RunOne("video.normalize", fixture.Clip);
        Assert.True(info.HasVideo && info.HasAudio);
        Assert.Equal("h264", info.VideoCodec); // the picture is copied, not re-encoded
    }

    [Fact]
    public async Task Frames_are_one_picture_per_second()
    {
        var frames = await Run("video.frames", fixture.Clip);
        Assert.InRange(frames.Count, 3, 4);
        Assert.All(frames, f => Assert.EndsWith(".jpg", f));
        Assert.EndsWith("clip-0001.jpg", frames[0]);
    }

    [Fact]
    public async Task Split_makes_two_halves_without_re_encoding()
    {
        var parts = await Run("video.split", fixture.Clip);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal("h264", Ffmpeg.Probe(p).VideoCodec));
        Assert.InRange(parts.Sum(p => Ffmpeg.Probe(p).Duration), 2.7, 3.3);
    }

    [Fact]
    public async Task Metadata_is_removed_from_video()
    {
        var output = Assert.Single(await Run("video.strip-metadata", fixture.Clip));
        Assert.DoesNotContain("Secret title", File.ReadAllText(output, System.Text.Encoding.Latin1));
        Assert.Contains("Secret title", File.ReadAllText(fixture.Clip, System.Text.Encoding.Latin1));
    }

    [Fact]
    public async Task Merge_joins_clips_of_any_kind_with_sound_where_missing()
    {
        var output = Assert.Single(await Run("video.merge", fixture.Clip, fixture.Silent, fixture.Webm));
        var info = Ffmpeg.Probe(output);
        Assert.InRange(info.Duration, 6.5, 7.5);
        Assert.True(info.HasAudio);
        Assert.Equal((320, 240), (info.Width, info.Height));
        Assert.Equal("clip-merged.mp4", Path.GetFileName(output));
    }

    // ---------- audio ----------

    [Theory]
    [InlineData("audio.to-m4a", "aac")]
    [InlineData("audio.to-wav", "pcm_s16le")]
    [InlineData("audio.to-flac", "flac")]
    [InlineData("audio.to-ogg", "vorbis")]
    [InlineData("audio.to-opus", "opus")]
    public async Task Audio_converts_to_every_format(string id, string codec)
    {
        var info = await RunOne(id, fixture.Song);
        Assert.Equal(codec, info.AudioCodec);
        Assert.InRange(info.Duration, 2.8, 3.3);
    }

    [Fact]
    public async Task Wav_converts_to_mp3()
    {
        var info = await RunOne("audio.to-mp3", fixture.Mono);
        Assert.Equal("mp3", info.AudioCodec);
    }

    [Fact]
    public async Task Audio_compress_is_not_bigger()
    {
        var output = Assert.Single(await Run("audio.compress", fixture.Song));
        Assert.True(new FileInfo(output).Length <= new FileInfo(fixture.Song).Length);
    }

    [Fact]
    public async Task Silence_is_removed()
    {
        var info = await RunOne("audio.trim-silence", fixture.Pauses);
        // 4 s with 1 s + 1.5 s of silence: about 1.5 s of sound plus the 0.7 s kept of each pause at most.
        Assert.InRange(info.Duration, 1.4, 3.0);
    }

    [Fact]
    public async Task Stereo_becomes_mono_and_mono_becomes_stereo()
    {
        Assert.Equal(1, (await RunOne("audio.channels", fixture.Song)).AudioChannels);
        Assert.Equal(2, (await RunOne("audio.channels", fixture.Mono)).AudioChannels);
    }

    [Fact]
    public async Task Tags_are_removed_from_audio()
    {
        var output = Assert.Single(await Run("audio.strip-metadata", fixture.Song));
        var bytes = File.ReadAllText(output, System.Text.Encoding.Latin1);
        Assert.DoesNotContain("Secret song", bytes);
        Assert.DoesNotContain("Secret artist", bytes);
        Assert.Equal("mp3", Ffmpeg.Probe(output).AudioCodec);
    }

    [Fact]
    public async Task Recordings_are_joined_in_the_first_format()
    {
        var output = Assert.Single(await Run("audio.merge", fixture.Song, fixture.Mono));
        var info = Ffmpeg.Probe(output);
        Assert.Equal("song-merged.mp3", Path.GetFileName(output));
        Assert.InRange(info.Duration, 4.7, 5.3);
    }

    // ---------- errors and cancelling ----------

    [Fact]
    public async Task Broken_files_get_a_clear_message()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("video.to-mkv", fixture.Broken));
        Assert.Equal("error.media-unreadable", error.Key);
    }

    [Fact]
    public async Task Cancelling_stops_ffmpeg_and_leaves_no_file()
    {
        var folder = fixture.NewOutputFolder();
        using var cancel = new CancellationTokenSource();
        var reported = new TaskCompletionSource();
        var progress = new SyncProgress(p => { if (p.Fraction > 0.01) reported.TrySetResult(); });
        var run = Registry.Find("video.to-webm")!.RunAsync(
            new OperationContext(new InputSet([fixture.Long]), new OutputPlanner(folder), progress), cancel.Token);

        await Task.WhenAny(reported.Task, Task.Delay(10_000));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(Directory.GetFiles(folder));
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
