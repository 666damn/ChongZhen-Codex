using System.Text.Json;
using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class SidecarRefreshServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-sidecar-refresh-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReusesUnchangedSidecarAndRebuildsAfterCompatibleSourceUpdate()
    {
        var fixture = CreateFixture(includeUpdatedHeader: true);

        Assert.Equal(SidecarRefreshOutcome.Refreshed, await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: false, CancellationToken.None));
        Assert.Equal(SidecarRefreshOutcome.Current, await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: false, CancellationToken.None));
        Assert.Equal(1, fixture.Patcher.BuildCount);

        TestFiles.WriteMinimalAsar(fixture.Game.AsarPath, "compatible-update");
        Assert.Equal(SidecarRefreshOutcome.Refreshed, await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: false, CancellationToken.None));
        Assert.Equal(2, fixture.Patcher.BuildCount);
    }

    [Fact]
    public async Task RecordsAnIncompatibleFingerprintOnceWithoutRetryingIt()
    {
        var fixture = CreateFixture(includeUpdatedHeader: false);
        TestFiles.WriteMinimalAsar(fixture.Game.AsarPath, "unsupported-update");

        Assert.Equal(SidecarRefreshOutcome.Incompatible, await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: false, CancellationToken.None));
        Assert.Equal(SidecarRefreshOutcome.IncompatibleUnchanged, await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: false, CancellationToken.None));

        Assert.Equal(0, fixture.Patcher.BuildCount);
        using var status = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixture.RuntimeRoot, "sidecar-status.json")));
        Assert.Equal("incompatible", status.RootElement.GetProperty("state").GetString());
        Assert.Matches("^[a-f0-9]{64}$", status.RootElement.GetProperty("fingerprint").GetString()!);
        Assert.Equal(2, status.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task DoesNotInspectOrRefreshGameFilesWhileGameIsRunning()
    {
        var fixture = CreateFixture(includeUpdatedHeader: true);
        File.Delete(fixture.Game.AsarPath);

        var outcome = await fixture.Refresh.RefreshAsync(
            fixture.Game, fixture.Payload, fixture.Token, gameRunning: true, CancellationToken.None);

        Assert.Equal(SidecarRefreshOutcome.GameRunning, outcome);
        Assert.Equal(0, fixture.Patcher.BuildCount);
        Assert.False(File.Exists(Path.Combine(fixture.RuntimeRoot, "sidecar-status.json")));
    }

    private Fixture CreateFixture(bool includeUpdatedHeader)
    {
        var id = Guid.NewGuid().ToString("N");
        var game = GameInstall.FromRoot(Path.Combine(root, id, "game"));
        var runtimeRoot = Path.Combine(root, id, "runtime");
        var payloadRoot = Path.Combine(root, id, "payload");
        Directory.CreateDirectory(Path.GetDirectoryName(game.AsarPath)!);
        Directory.CreateDirectory(payloadRoot);
        File.WriteAllBytes(game.ExecutablePath, [1, 2, 3]);
        TestFiles.WriteMinimalAsar(game.AsarPath, "official");
        var supportedHeaders = new List<string> { Hashing.AsarHeaderSha256(game.AsarPath) };
        if (includeUpdatedHeader)
        {
            var updated = Path.Combine(root, id, "updated.asar");
            TestFiles.WriteMinimalAsar(updated, "compatible-update");
            supportedHeaders.Add(Hashing.AsarHeaderSha256(updated));
        }
        var payload = new VerifiedPayload(new PayloadManifest(
            "test",
            new Dictionary<string, PayloadFile>(),
            [Hashing.Sha256(game.ExecutablePath)],
            supportedHeaders,
            []), payloadRoot);
        var patcher = new CountingPatcher();
        var sidecar = new SidecarService(runtimeRoot, patcher);
        return new(
            game,
            payload,
            runtimeRoot,
            $"czb_{new string('a', 64)}",
            patcher,
            new SidecarRefreshService(runtimeRoot, sidecar));
    }

    private sealed class CountingPatcher : ISidecarPatcher
    {
        public int BuildCount { get; private set; }

        public Task<SidecarBuildResult> BuildAsync(
            string source,
            string output,
            string token,
            CancellationToken cancellationToken)
        {
            BuildCount++;
            TestFiles.WriteMinimalAsar(output, $"sidecar-{BuildCount}");
            return Task.FromResult(new SidecarBuildResult(
                Hashing.AsarHeaderSha256(source),
                new FileInfo(source).Length,
                Hashing.AsarHeaderSha256(output),
                new FileInfo(output).Length));
        }
    }

    private sealed record Fixture(
        GameInstall Game,
        VerifiedPayload Payload,
        string RuntimeRoot,
        string Token,
        CountingPatcher Patcher,
        SidecarRefreshService Refresh);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
