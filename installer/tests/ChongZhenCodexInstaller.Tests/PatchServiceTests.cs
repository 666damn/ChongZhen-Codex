using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class PatchServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-patch-{Guid.NewGuid():N}");

    [Fact]
    public async Task GeneratesSidecarAndInstallsLoaderWithoutReplacingOfficialFiles()
    {
        var fixture = CreateFixture();
        var exeBefore = TestFiles.Sha256(fixture.Game.ExecutablePath);
        var asarBefore = TestFiles.Sha256(fixture.Game.AsarPath);

        var result = await fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None);

        Assert.Equal(exeBefore, TestFiles.Sha256(fixture.Game.ExecutablePath));
        Assert.Equal(asarBefore, TestFiles.Sha256(fixture.Game.AsarPath));
        Assert.True(File.Exists(fixture.Game.VersionPath));
        Assert.True(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.DoesNotContain("resources/app.asar", result.State.InstalledSha256.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(fixture.RuntimeRoot, "sidecars"), "loader-metadata.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task MigratesV1ReplacementStateBeforeBuildingSidecar()
    {
        var fixture = CreateFixture();
        var originalAsar = await File.ReadAllBytesAsync(fixture.Game.AsarPath);
        var originalAsarHash = TestFiles.Sha256(fixture.Game.AsarPath);
        var backupRoot = fixture.StateStore.CreateBackupDirectory();
        var asarBackup = Path.Combine(backupRoot, "app.asar");
        await File.WriteAllBytesAsync(asarBackup, originalAsar);
        TestFiles.WriteMinimalAsar(fixture.Game.AsarPath, "v1-replaced-game-asar");
        await File.WriteAllBytesAsync(fixture.Game.VersionPath, [5, 5, 5]);
        await File.WriteAllBytesAsync(fixture.Game.VersionOriginalPath, [3, 3, 3]);
        var legacy = new PatchInstallState(
            fixture.Game.RootPath,
            "1.0.0",
            new Dictionary<string, OriginalFileState>(StringComparer.OrdinalIgnoreCase)
            {
                ["resources/app.asar"] = new(true, originalAsarHash, asarBackup),
                ["version.dll"] = new(false, null, null),
                ["version_original.dll"] = new(false, null, null),
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["resources/app.asar"] = TestFiles.Sha256(fixture.Game.AsarPath),
                ["version.dll"] = TestFiles.Sha256(fixture.Game.VersionPath),
                ["version_original.dll"] = TestFiles.Sha256(fixture.Game.VersionOriginalPath),
            },
            DateTimeOffset.UnixEpoch);
        fixture.StateStore.Save(legacy);

        var result = await fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None);

        Assert.Equal(originalAsarHash, TestFiles.Sha256(fixture.Game.AsarPath));
        Assert.Equal([7, 7, 7], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.DoesNotContain("resources/app.asar", result.State.Originals.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.NotNull(fixture.StateStore.LoadLoader());
        Assert.Null(fixture.StateStore.LoadLegacy());
    }

    [Fact]
    public async Task RestoreRemovesLoaderAndLeavesOfficialFilesByteIdentical()
    {
        var fixture = CreateFixture();
        var exeBefore = TestFiles.Sha256(fixture.Game.ExecutablePath);
        var asarBefore = TestFiles.Sha256(fixture.Game.AsarPath);
        var result = await fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None);

        await fixture.Service.RestoreAsync(fixture.Game, result.State, CancellationToken.None);

        Assert.Equal(exeBefore, TestFiles.Sha256(fixture.Game.ExecutablePath));
        Assert.Equal(asarBefore, TestFiles.Sha256(fixture.Game.AsarPath));
        Assert.False(File.Exists(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.False(File.Exists(fixture.StateStore.StatePath));
    }

    [Fact]
    public async Task FailedSidecarReinstallKeepsTheExistingLoaderAndState()
    {
        var fixture = CreateFixture();
        await fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.None, CancellationToken.None);
        var versionBefore = TestFiles.Sha256(fixture.Game.VersionPath);
        var originalBefore = TestFiles.Sha256(fixture.Game.VersionOriginalPath);
        var stateBefore = await File.ReadAllBytesAsync(fixture.StateStore.StatePath);

        await Assert.ThrowsAsync<InjectedFailureException>(() => fixture.Service.InstallGlobalAsync(
            fixture.Game, fixture.Payload, FailurePoint.AfterDllReplace, CancellationToken.None));

        Assert.Equal(versionBefore, TestFiles.Sha256(fixture.Game.VersionPath));
        Assert.Equal(originalBefore, TestFiles.Sha256(fixture.Game.VersionOriginalPath));
        Assert.Equal(stateBefore, await File.ReadAllBytesAsync(fixture.StateStore.StatePath));
    }

    private Fixture CreateFixture()
    {
        var id = Guid.NewGuid().ToString("N");
        var game = GameInstall.FromRoot(Path.Combine(root, id, "game"));
        var payloadRoot = Path.Combine(root, id, "payload");
        var runtimeRoot = Path.Combine(root, id, "runtime");
        Directory.CreateDirectory(Path.GetDirectoryName(game.AsarPath)!);
        Directory.CreateDirectory(Path.Combine(payloadRoot, "global"));
        File.WriteAllBytes(game.ExecutablePath, [1, 1, 1]);
        TestFiles.WriteMinimalAsar(game.AsarPath, "official");
        var proxy = Path.Combine(payloadRoot, "global", "version.dll");
        var systemVersion = Path.Combine(root, id, "system-version.dll");
        File.WriteAllBytes(proxy, [7, 7, 7]);
        File.WriteAllBytes(systemVersion, [3, 3, 3]);
        var manifest = new PayloadManifest(
            "2.0.0",
            new Dictionary<string, PayloadFile>
            {
                ["global/version.dll"] = new(TestFiles.Sha256(proxy), new FileInfo(proxy).Length),
            },
            [TestFiles.Sha256(game.ExecutablePath)],
            [Hashing.AsarHeaderSha256(game.AsarPath)],
            []);
        var payload = new VerifiedPayload(manifest, payloadRoot);
        var stateStore = new InstallStateStore(runtimeRoot);
        var loader = new LoaderInstallService(stateStore, systemVersion);
        var sidecar = new SidecarService(runtimeRoot, new FakePatcher());
        var service = new PatchService(
            stateStore, sidecar, new BridgeSecretStore(runtimeRoot), loader);
        return new(game, payload, runtimeRoot, stateStore, service);
    }

    private sealed class FakePatcher : ISidecarPatcher
    {
        public Task<SidecarBuildResult> BuildAsync(
            string source,
            string output,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestFiles.WriteMinimalAsar(output, "sidecar");
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
        InstallStateStore StateStore,
        PatchService Service);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
