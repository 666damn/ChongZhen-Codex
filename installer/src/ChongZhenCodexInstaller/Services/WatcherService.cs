namespace ChongZhenCodexInstaller.Services;

public sealed class WatcherService(
    IProcessService processes,
    TimeSpan idleGrace,
    TimeSpan? pollInterval = null)
{
    private readonly TimeSpan pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    private BridgeProcessHandle? bridge;
    private DateTimeOffset? lastGameSeen;

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var gameRunning = processes.IsGameRunning();
        var bridgeRunning = bridge is not null && processes.IsBridgeRunning(bridge);
        if (gameRunning)
        {
            lastGameSeen = now;
            if (!bridgeRunning) bridge = processes.StartBridge();
            return;
        }

        if (bridgeRunning && lastGameSeen is not null && now - lastGameSeen >= idleGrace)
        {
            await processes.StopBridgeAsync(bridge!, cancellationToken);
            bridge = null;
            lastGameSeen = null;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await TickAsync(DateTimeOffset.UtcNow, cancellationToken);
        using var timer = new PeriodicTimer(pollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await TickAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (bridge is not null && processes.IsBridgeRunning(bridge))
            await processes.StopBridgeAsync(bridge, cancellationToken);
        bridge = null;
        lastGameSeen = null;
    }
}
