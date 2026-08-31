using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class WatcherServiceTests
{
    [Fact]
    public async Task StartsBridgeOnceWhenGameAppears()
    {
        var processes = new FakeProcessService { GameRunning = true };
        var watcher = new WatcherService(processes, TimeSpan.FromSeconds(60));

        await watcher.TickAsync(DateTimeOffset.UnixEpoch, CancellationToken.None);
        await watcher.TickAsync(DateTimeOffset.UnixEpoch.AddSeconds(2), CancellationToken.None);

        Assert.Equal(1, processes.StartCount);
        Assert.Equal(0, processes.StopCount);
    }

    [Fact]
    public async Task StopsBridgeOnlyAfterSixtySecondExitGracePeriod()
    {
        var processes = new FakeProcessService { GameRunning = true };
        var watcher = new WatcherService(processes, TimeSpan.FromSeconds(60));
        await watcher.TickAsync(DateTimeOffset.UnixEpoch, CancellationToken.None);
        processes.GameRunning = false;

        await watcher.TickAsync(DateTimeOffset.UnixEpoch.AddSeconds(59), CancellationToken.None);
        Assert.Equal(0, processes.StopCount);
        await watcher.TickAsync(DateTimeOffset.UnixEpoch.AddSeconds(60), CancellationToken.None);

        Assert.Equal(1, processes.StopCount);
    }

    [Fact]
    public async Task RestartsCrashedBridgeWhileGameStillRuns()
    {
        var processes = new FakeProcessService { GameRunning = true };
        var watcher = new WatcherService(processes, TimeSpan.FromSeconds(60));
        await watcher.TickAsync(DateTimeOffset.UnixEpoch, CancellationToken.None);
        processes.BridgeRunning = false;

        await watcher.TickAsync(DateTimeOffset.UnixEpoch.AddSeconds(2), CancellationToken.None);

        Assert.Equal(2, processes.StartCount);
    }

    private sealed class FakeProcessService : IProcessService
    {
        public bool GameRunning { get; set; }
        public bool BridgeRunning { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public bool IsGameRunning() => GameRunning;
        public bool IsBridgeRunning(BridgeProcessHandle handle) => BridgeRunning;
        public BridgeProcessHandle StartBridge()
        {
            StartCount++;
            BridgeRunning = true;
            return new BridgeProcessHandle(StartCount);
        }
        public Task StopBridgeAsync(BridgeProcessHandle handle, CancellationToken cancellationToken)
        {
            StopCount++;
            BridgeRunning = false;
            return Task.CompletedTask;
        }
    }
}
