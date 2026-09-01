using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class LoaderInstallServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-loader-install-{Guid.NewGuid():N}");

    [Fact]
    public async Task InstallsIntoAbsentTargetsWithoutChangingOfficialFiles()
    {
        var fixture = CreateFixture();
        var official = CaptureOfficial(fixture.Game);

        var state = await fixture.Service.InstallAsync(fixture.Game, fixture.Payload, CancellationToken.None);

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.Equal([7, 7, 7], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.Equal([3, 3, 3], await File.ReadAllBytesAsync(fixture.Game.VersionOriginalPath));
        Assert.False(state.Originals["version.dll"].Existed);
        Assert.False(state.Originals["version_original.dll"].Existed);
        Assert.DoesNotContain("resources/app.asar", state.InstalledSha256.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplacesOnlyManifestDeclaredLegacyProjectDllsAndDoesNotRestoreThemOnUninstall()
    {
        var fixture = CreateFixture();
        await File.WriteAllBytesAsync(fixture.Game.VersionPath, [2, 2, 2]);
        var official = CaptureOfficial(fixture.Game);

        var state = await fixture.Service.InstallAsync(fixture.Game, fixture.Payload, CancellationToken.None);
        await fixture.Service.UninstallAsync(fixture.Game, state, CancellationToken.None);

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.False(File.Exists(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
    }

    [Fact]
    public async Task RefusesUnknownCollisionBeforeWritingAnything()
    {
        var fixture = CreateFixture();
        await File.WriteAllBytesAsync(fixture.Game.VersionPath, [99, 99]);
        var official = CaptureOfficial(fixture.Game);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.InstallAsync(fixture.Game, fixture.Payload, CancellationToken.None));

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.Equal([99, 99], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
    }

    [Fact]
    public async Task RollsBackTheFirstAddedDllWhenTheSecondWriteFails()
    {
        var fixture = CreateFixture();
        var official = CaptureOfficial(fixture.Game);

        await Assert.ThrowsAsync<InjectedFailureException>(() => fixture.Service.InstallAsync(
            fixture.Game, fixture.Payload, FailurePoint.AfterDllReplace, CancellationToken.None));

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.False(File.Exists(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.False(File.Exists(fixture.StateStore.StatePath));
    }

    [Fact]
    public async Task UninstallRemovesOnlyMatchingProjectAdditions()
    {
        var fixture = CreateFixture();
        var official = CaptureOfficial(fixture.Game);
        var state = await fixture.Service.InstallAsync(fixture.Game, fixture.Payload, CancellationToken.None);

        await fixture.Service.UninstallAsync(fixture.Game, state, CancellationToken.None);

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.False(File.Exists(fixture.Game.VersionPath));
        Assert.False(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.False(File.Exists(fixture.StateStore.StatePath));
    }

    [Fact]
    public async Task UninstallRefusesAUserModifiedAdditionAndPreservesEveryFile()
    {
        var fixture = CreateFixture();
        var state = await fixture.Service.InstallAsync(fixture.Game, fixture.Payload, CancellationToken.None);
        await File.WriteAllBytesAsync(fixture.Game.VersionPath, [42]);
        var official = CaptureOfficial(fixture.Game);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UninstallAsync(fixture.Game, state, CancellationToken.None));

        AssertOfficialUnchanged(fixture.Game, official);
        Assert.Equal([42], await File.ReadAllBytesAsync(fixture.Game.VersionPath));
        Assert.True(File.Exists(fixture.Game.VersionOriginalPath));
        Assert.True(File.Exists(fixture.StateStore.StatePath));
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
            "test",
            new Dictionary<string, PayloadFile>
            {
                ["global/version.dll"] = new(TestFiles.Sha256(proxy), new FileInfo(proxy).Length),
            },
            [TestFiles.Sha256(game.ExecutablePath)],
            [Hashing.AsarHeaderSha256(game.AsarPath)],
            [HashBytes([2, 2, 2])]);
        var store = new InstallStateStore(runtimeRoot);
        return new(game, new VerifiedPayload(manifest, payloadRoot), store,
            new LoaderInstallService(store, systemVersion));
    }

    private static (string Exe, string Asar) CaptureOfficial(GameInstall game) =>
        (TestFiles.Sha256(game.ExecutablePath), TestFiles.Sha256(game.AsarPath));

    private static void AssertOfficialUnchanged(GameInstall game, (string Exe, string Asar) expected)
    {
        Assert.Equal(expected.Exe, TestFiles.Sha256(game.ExecutablePath));
        Assert.Equal(expected.Asar, TestFiles.Sha256(game.AsarPath));
    }

    private static string HashBytes(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cz-hash-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(path, bytes);
            return TestFiles.Sha256(path);
        }
        finally { File.Delete(path); }
    }

    private sealed record Fixture(
        GameInstall Game,
        VerifiedPayload Payload,
        InstallStateStore StateStore,
        LoaderInstallService Service);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
