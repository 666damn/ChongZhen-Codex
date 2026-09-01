namespace ChongZhenCodexInstaller.Services;

public sealed class WatcherService(
    IProcessService processes,
    TimeSpan idleGrace,
    TimeSpan? pollInterval = null,
    TimeSpan? refreshInterval = null)
{
    private readonly TimeSpan pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    private readonly TimeSpan refreshInterval = refreshInterval ?? TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim tickGate = new(1, 1);
    private BridgeProcessHandle? bridge;
    private DateTimeOffset? lastGameSeen;
    private DateTimeOffset? lastRefresh;

    public async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await tickGate.WaitAsync(cancellationToken);
        try
        {
            var gameRunning = processes.IsGameRunning();
            var bridgeRunning = bridge is not null && processes.IsBridgeRunning(bridge);
            if (gameRunning)
            {
                lastGameSeen = now;
                lastRefresh = null;
                if (!bridgeRunning) bridge = processes.StartBridge();
                return;
            }

            if (bridgeRunning && lastGameSeen is not null && now - lastGameSeen >= idleGrace)
            {
                await processes.StopBridgeAsync(bridge!, cancellationToken);
                bridge = null;
                lastGameSeen = null;
            }

            if (lastRefresh is null || now - lastRefresh >= refreshInterval)
            {
                lastRefresh = now;
                try { await processes.RefreshSidecarAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
        }
        finally { tickGate.Release(); }
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
        lastRefresh = null;
    }
}
