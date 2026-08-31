using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class InstallerBackendTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-backend-{Guid.NewGuid():N}");

    [Fact]
    public async Task StatusUsesOperationalFilesAndNeverOpensTheLargePayload()
    {
        var runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(Path.Combine(runtime, "app", "src"));
        await File.WriteAllBytesAsync(Path.Combine(runtime, "app", "node.exe"), [1]);
        await File.WriteAllTextAsync(Path.Combine(runtime, "app", "src", "main.js"), "// fixture");
        await File.WriteAllTextAsync(Path.Combine(runtime, "config.json"), "{}");
        var states = new CoordinatorStateStore(runtime);
        states.Save(new CoordinatorState(@"D:\Game", InstallMode.BridgeAndGlobal, "test", DateTimeOffset.UnixEpoch));
        var patchState = new PatchInstallState(@"D:\Game", "test",
            new Dictionary<string, OriginalFileState>(), new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        new InstallStateStore(runtime).Save(patchState);
        var payload = new RejectingPayloadProvider();
        var backend = new InstallerBackend(
            runtime,
            new FakeDiscoveryService(new DiscoveryResult(@"D:\Game", @"C:\Codex\codex.exe", false)),
            payload,
            Path.Combine(root, "installer.exe"));

        var status = await backend.GetStatusAsync(CancellationToken.None);

        Assert.True(status.BridgeInstalled);
        Assert.True(status.GlobalInstalled);
        Assert.Equal(InstallMode.BridgeAndGlobal, status.Mode);
        Assert.Equal(0, payload.OpenCount);
    }

    private sealed class FakeDiscoveryService(DiscoveryResult result) : IInstallationDiscoveryService
    {
        public Task<DiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class RejectingPayloadProvider : IPayloadProvider
    {
        public int OpenCount { get; private set; }
        public Stream OpenRead() { OpenCount++; throw new InvalidOperationException("Payload should not be opened for status."); }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
