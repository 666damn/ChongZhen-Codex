using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class InstallerCoordinatorTests
{
    [Fact]
    public async Task BridgeOnlyNeverCallsGlobalPatchService()
    {
        var backend = new FakeBackend();
        var store = new MemoryCoordinatorStateStore();
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.InstallAsync(InstallMode.BridgeOnly, CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain("InstallGlobal", backend.Calls);
        Assert.Equal(["Discover", "InstallBridge", "ConfigureByokInstall"], backend.Calls);
        Assert.Equal(InstallMode.BridgeOnly, store.State?.Mode);
    }

    [Fact]
    public async Task SwitchingFromGlobalRestoresOriginalGameBeforeBridgeOnlyInstall()
    {
        var backend = new FakeBackend();
        var store = new MemoryCoordinatorStateStore
        {
            State = new CoordinatorState(@"D:\Game", InstallMode.BridgeAndGlobal, "old", DateTimeOffset.UnixEpoch),
        };
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.InstallAsync(InstallMode.BridgeOnly, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["Discover", "InstallBridge", "RestoreGlobal", "ConfigureByokInstall"], backend.Calls);
    }

    [Fact]
    public async Task ReinstallingGlobalTransactionallyReplacesTheExistingProjectLoader()
    {
        var backend = new FakeBackend();
        var store = new MemoryCoordinatorStateStore
        {
            State = new CoordinatorState(@"D:\Game", InstallMode.BridgeAndGlobal, "old", DateTimeOffset.UnixEpoch),
        };
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.InstallAsync(InstallMode.BridgeAndGlobal, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["Discover", "InstallBridge", "InstallGlobal", "ConfigureByokInstall"], backend.Calls);
    }

    [Fact]
    public async Task BridgeUpdateFailureLeavesExistingGlobalLoaderUntouched()
    {
        var backend = new FakeBackend { RejectBridge = true };
        var store = new MemoryCoordinatorStateStore
        {
            State = new CoordinatorState(@"D:\Game", InstallMode.BridgeAndGlobal, "old", DateTimeOffset.UnixEpoch),
        };
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.InstallAsync(InstallMode.BridgeAndGlobal, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(InstallMode.BridgeAndGlobal, store.State?.Mode);
        Assert.Equal(["Discover", "InstallBridge"], backend.Calls);
    }

    [Fact]
    public async Task UnsupportedGlobalVersionLeavesCoordinatorStateUntouched()
    {
        var backend = new FakeBackend { RejectGlobal = true };
        var store = new MemoryCoordinatorStateStore();
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.InstallAsync(InstallMode.BridgeAndGlobal, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(store.State);
        Assert.Equal(["Discover", "InstallBridge", "InstallGlobal"], backend.Calls);
    }

    [Fact]
    public async Task UninstallRestoresGlobalThenRemovesByokAndBridgeRuntime()
    {
        var backend = new FakeBackend();
        var store = new MemoryCoordinatorStateStore
        {
            State = new CoordinatorState(@"D:\Game", InstallMode.BridgeAndGlobal, "old", DateTimeOffset.UnixEpoch),
        };
        var coordinator = new InstallerCoordinator(backend, store);

        var result = await coordinator.UninstallAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["Discover", "RestoreGlobal", "ConfigureByokRemove", "UninstallBridge"], backend.Calls);
        Assert.Null(store.State);
    }

    private sealed class FakeBackend : IInstallerBackend
    {
        public List<string> Calls { get; } = [];
        public bool RejectGlobal { get; set; }
        public bool RejectBridge { get; set; }

        public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
        {
            Calls.Add("Discover");
            return Task.FromResult(new DiscoveryResult(@"D:\Game", @"C:\Codex\codex.exe", false));
        }
        public Task InstallBridgeAsync(DiscoveryResult discovery, CancellationToken cancellationToken)
        {
            Calls.Add("InstallBridge");
            if (RejectBridge) throw new IOException("fixture");
            return Task.CompletedTask;
        }
        public Task InstallGlobalAsync(DiscoveryResult discovery, CancellationToken cancellationToken)
        {
            Calls.Add("InstallGlobal");
            if (RejectGlobal) throw new UnsupportedGameVersionException("fixture");
            return Task.CompletedTask;
        }
        public Task RestoreGlobalAsync(string gamePath, CancellationToken cancellationToken)
        {
            Calls.Add("RestoreGlobal");
            return Task.CompletedTask;
        }
        public Task ConfigureByokAsync(DiscoveryResult discovery, bool install, CancellationToken cancellationToken)
        {
            Calls.Add(install ? "ConfigureByokInstall" : "ConfigureByokRemove");
            return Task.CompletedTask;
        }
        public Task UninstallBridgeAsync(CancellationToken cancellationToken)
        {
            Calls.Add("UninstallBridge");
            return Task.CompletedTask;
        }
        public Task<InstallerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InstallerStatus(null, null, null, false, false, false, "fixture"));
    }

    private sealed class MemoryCoordinatorStateStore : ICoordinatorStateStore
    {
        public CoordinatorState? State { get; set; }
        public CoordinatorState? Load() => State;
        public void Save(CoordinatorState state) => State = state;
        public void Remove() => State = null;
    }
}
