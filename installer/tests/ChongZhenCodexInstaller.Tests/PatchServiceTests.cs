using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class PatchServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-patch-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(FailurePoint.AfterBackup)]
    [InlineData(FailurePoint.AfterAsarReplace)]
    [InlineData(FailurePoint.AfterDllReplace)]
    public async Task RestoresExactOriginalBytesOnInjectedFailure(FailurePoint failurePoint)
    {
        var gameRoot = Path.Combine(root, "game");
        var payloadRoot = Path.Combine(root, "payload");
        var runtimeRoot = Path.Combine(root, "runtime");
        Directory.CreateDirectory(Path.Combine(gameRoot, "resources"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "global"));
        var exe = Path.Combine(gameRoot, "ChongZhenSimulator.exe");
        var asar = Path.Combine(gameRoot, "resources", "app.asar");
        var version = Path.Combine(gameRoot, "version.dll");
        var systemVersion = Path.Combine(root, "system-version.dll");
        await File.WriteAllBytesAsync(exe, [1, 1, 1]);
        TestFiles.WriteMinimalAsar(asar, "original");
        await File.WriteAllBytesAsync(version, [2, 2, 2]);
        await File.WriteAllBytesAsync(systemVersion, [3, 3, 3]);
        var originalAsar = await File.ReadAllBytesAsync(asar);
        var originalVersion = await File.ReadAllBytesAsync(version);

        var payloadAsar = Path.Combine(payloadRoot, "global", "app.asar");
        var payloadDll = Path.Combine(payloadRoot, "global", "version.dll");
        TestFiles.WriteMinimalAsar(payloadAsar, "patched");
        await File.WriteAllBytesAsync(payloadDll, [7, 7, 7]);
        var manifest = new PayloadManifest(
            "test",
            new Dictionary<string, PayloadFile>
            {
                ["global/app.asar"] = new(TestFiles.Sha256(payloadAsar), new FileInfo(payloadAsar).Length),
                ["global/version.dll"] = new(TestFiles.Sha256(payloadDll), new FileInfo(payloadDll).Length),
            },
            [TestFiles.Sha256(exe)],
            [Hashing.AsarHeaderSha256(asar)],
            [TestFiles.Sha256(version)]);
        var payload = new VerifiedPayload(manifest, payloadRoot);
        var service = new PatchService(new InstallStateStore(runtimeRoot), systemVersion);

        await Assert.ThrowsAsync<InjectedFailureException>(() => service.InstallGlobalAsync(
            GameInstall.FromRoot(gameRoot), payload, failurePoint, CancellationToken.None));

        Assert.Equal(originalAsar, await File.ReadAllBytesAsync(asar));
        Assert.Equal(originalVersion, await File.ReadAllBytesAsync(version));
        Assert.False(File.Exists(Path.Combine(gameRoot, "version_original.dll")));
        Assert.Equal(TestFiles.Sha256(exe), TestFiles.Sha256(Path.Combine(gameRoot, "ChongZhenSimulator.exe")));
    }

    [Fact]
    public async Task SuccessfulInstallAndUninstallRoundTripPreservesEveryOriginal()
    {
        var fixture = await CreateFixtureAsync();
        var originalAsar = await File.ReadAllBytesAsync(fixture.Game.AsarPath);
        var originalVersion = await File.ReadAllBytesAsync(fixture.Game.VersionPath);

        var result = await fixture.Service.InstallGlobalAsync(fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None);
        Assert.Equal([7, 7, 7], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.Equal([3, 3, 3], await File.ReadAllBytesAsync(fixture.Game.VersionOriginalPath));

        await fixture.Service.RestoreAsync(fixture.Game, result.State, CancellationToken.None);

        Assert.Equal(originalAsar, await File.ReadAllBytesAsync(fixture.Game.AsarPath));
        Assert.Equal(originalVersion, await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.False(File.Exists(fixture.StateStore.StatePath));
    }

    [Fact]
    public async Task NeverReplacesGameFilesWhenPayloadHashNoLongerMatches()
    {
        var fixture = await CreateFixtureAsync();
        var originalAsar = await File.ReadAllBytesAsync(fixture.Game.AsarPath);
        await File.WriteAllBytesAsync(fixture.Payload.GetPath("global/version.dll"), [0]);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None));

        Assert.Equal(originalAsar, await File.ReadAllBytesAsync(fixture.Game.AsarPath));
    }

    [Fact]
    public async Task RefusesUnknownExistingVersionDllBeforeAnyGameFileReplacement()
    {
        var fixture = await CreateFixtureAsync();
        var originalAsar = await File.ReadAllBytesAsync(fixture.Game.AsarPath);
        await File.WriteAllBytesAsync(fixture.Game.VersionPath, [99, 99]);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None));

        Assert.Equal(originalAsar, await File.ReadAllBytesAsync(fixture.Game.AsarPath));
        Assert.Equal([99, 99], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
    }

    private async Task<(GameInstall Game, VerifiedPayload Payload, PatchService Service, InstallStateStore StateStore)> CreateFixtureAsync()
    {
        var id = Guid.NewGuid().ToString("N");
        var gameRoot = Path.Combine(root, id, "game");
        var payloadRoot = Path.Combine(root, id, "payload");
        var runtimeRoot = Path.Combine(root, id, "runtime");
        Directory.CreateDirectory(Path.Combine(gameRoot, "resources"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "global"));
        var game = GameInstall.FromRoot(gameRoot);
        await File.WriteAllBytesAsync(game.ExecutablePath, [1, 1, 1]);
        TestFiles.WriteMinimalAsar(game.AsarPath, "original");
        await File.WriteAllBytesAsync(game.VersionPath, [2, 2, 2]);
        var systemVersion = Path.Combine(root, id, "system-version.dll");
        await File.WriteAllBytesAsync(systemVersion, [3, 3, 3]);
        var payloadAsar = Path.Combine(payloadRoot, "global", "app.asar");
        var payloadDll = Path.Combine(payloadRoot, "global", "version.dll");
        TestFiles.WriteMinimalAsar(payloadAsar, "patched");
        await File.WriteAllBytesAsync(payloadDll, [7, 7, 7]);
        var manifest = new PayloadManifest(
            "test",
            new Dictionary<string, PayloadFile>
            {
                ["global/app.asar"] = new(TestFiles.Sha256(payloadAsar), new FileInfo(payloadAsar).Length),
                ["global/version.dll"] = new(TestFiles.Sha256(payloadDll), new FileInfo(payloadDll).Length),
            },
            [TestFiles.Sha256(game.ExecutablePath)],
            [Hashing.AsarHeaderSha256(game.AsarPath)],
            [TestFiles.Sha256(game.VersionPath)]);
        var stateStore = new InstallStateStore(runtimeRoot);
        return (game, new VerifiedPayload(manifest, payloadRoot), new PatchService(stateStore, systemVersion), stateStore);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
