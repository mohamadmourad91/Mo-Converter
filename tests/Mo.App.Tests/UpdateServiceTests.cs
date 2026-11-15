using System.Net.Http;
using Mo.App.Services.Updates;

namespace Mo.App.Tests;

public class UpdateServiceTests
{
    private sealed class FakeChannel : IUpdateChannel
    {
        public bool IsSupported { get; set; } = true;
        public string? PendingVersion { get; set; }
        public string? Available { get; set; }
        public Exception? CheckError { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int Applies { get; private set; }

        public Task<string?> CheckAsync(CancellationToken cancellation)
        {
            Checks++;
            if (CheckError is not null) throw CheckError;
            return Task.FromResult(Available);
        }

        public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellation)
        {
            Downloads++;
            return Task.CompletedTask;
        }

        public void ApplyAndRestart() => Applies++;
    }

    private static UpdateService Create(FakeChannel channel, List<TimeSpan>? waits = null) =>
        new(channel, (wait, _) => { waits?.Add(wait); return Task.CompletedTask; }, _ => { });

    [Fact]
    public async Task Newer_version_is_downloaded_and_reported_ready()
    {
        var channel = new FakeChannel { Available = "1.2.0" };
        var service = Create(channel);
        string? ready = null, downloading = null;
        service.Downloading += v => downloading = v;
        service.Ready += v => ready = v;

        var next = await service.CheckOnceAsync(CancellationToken.None);

        Assert.Equal("1.2.0", downloading);
        Assert.Equal("1.2.0", ready);
        Assert.Equal("1.2.0", service.ReadyVersion);
        Assert.Equal(1, channel.Downloads);
        Assert.Equal(UpdateService.Regular, next);
    }

    [Fact]
    public async Task Up_to_date_downloads_nothing()
    {
        var channel = new FakeChannel();
        var service = Create(channel);
        var fired = false;
        service.Ready += _ => fired = true;

        var next = await service.CheckOnceAsync(CancellationToken.None);

        Assert.False(fired);
        Assert.Equal(0, channel.Downloads);
        Assert.Equal(UpdateService.Regular, next);
    }

    [Fact]
    public async Task Failure_keeps_the_app_running_and_retries_sooner()
    {
        var channel = new FakeChannel { CheckError = new HttpRequestException("offline") };
        var service = Create(channel);

        var next = await service.CheckOnceAsync(CancellationToken.None);

        Assert.Null(service.ReadyVersion);
        Assert.Equal(UpdateService.AfterFailure, next);
    }

    [Fact]
    public async Task Loop_retries_after_failure_and_stops_once_ready()
    {
        var channel = new FakeChannel { CheckError = new HttpRequestException("offline") };
        var waits = new List<TimeSpan>();
        var service = new UpdateService(channel, (wait, _) =>
        {
            waits.Add(wait);
            if (waits.Count == 2)
            {
                // Network is back on the second attempt.
                channel.CheckError = null;
                channel.Available = "2.0.0";
            }
            return Task.CompletedTask;
        }, _ => { });

        await service.RunAsync(CancellationToken.None);

        Assert.Equal([UpdateService.FirstCheck, UpdateService.AfterFailure], waits);
        Assert.Equal("2.0.0", service.ReadyVersion);
        Assert.Equal(2, channel.Checks);
    }

    [Fact]
    public async Task Unsupported_channel_never_checks()
    {
        var channel = new FakeChannel { IsSupported = false, Available = "9.9.9" };
        var service = Create(channel);

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(0, channel.Checks);
        Assert.False(service.TryApplyPendingAtStartup());
    }

    [Fact]
    public void Update_left_from_earlier_run_is_installed_at_startup()
    {
        var channel = new FakeChannel { PendingVersion = "1.3.0" };
        var service = Create(channel);

        Assert.True(service.TryApplyPendingAtStartup());
        Assert.Equal(1, channel.Applies);
    }

    [Fact]
    public void Nothing_pending_starts_normally()
    {
        var channel = new FakeChannel();
        var service = Create(channel);

        Assert.False(service.TryApplyPendingAtStartup());
        Assert.Equal(0, channel.Applies);
    }
}
