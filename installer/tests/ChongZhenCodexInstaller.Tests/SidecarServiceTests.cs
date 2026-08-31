using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class SidecarServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-sidecar-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task PublishesVerifiedSidecarWithoutChangingOfficialGameFiles()
    {
        var fixture = CreateFixture(new FakePatcher());
        var exeBefore = TestFiles.Sha256(fixture.Game.ExecutablePath);
        var asarBefore = TestFiles.Sha256(fixture.Game.AsarPath);

        var result = await fixture.Service.EnsureCurrentAsync(
            fixture.Game, fixture.Payload, $"czb_{new string('a', 64)}", CancellationToken.None);

        Assert.Equal(exeBefore, TestFiles.Sha256(fixture.Game.ExecutablePath));
        Assert.Equal(asarBefore, TestFiles.Sha256(fixture.Game.AsarPath));
        Assert.True(File.Exists(result.SidecarPath));
        Assert.True(File.Exists(result.MetadataPath));
        Assert.StartsWith(Path.Combine(fixture.RuntimeRoot, "sidecars"), result.SidecarPath, StringComparison.OrdinalIgnoreCase);
        var decoded = SidecarMetadataCodec.Read(result.MetadataPath, fixture.RuntimeRoot);
        Assert.Equal(Path.GetFullPath(fixture.Game.AsarPath), decoded.OfficialAsarPath);
        Assert.Equal(Path.GetFullPath(result.SidecarPath), decoded.SidecarPath);
        Assert.Equal(Hashing.AsarHeaderSha256(fixture.Game.AsarPath), decoded.SourceHeaderSha256);
        Assert.Equal(Hashing.AsarHeaderSha256(result.SidecarPath), decoded.SidecarHeaderSha256);
    }

    [Fact]
    public async Task SourceMutationDuringBuildRejectsOutputAndLeavesNoPublishedMetadata()
    {
        FakePatcher? patcher = null;
        patcher = new FakePatcher(sourceMutator: source => TestFiles.WriteMinimalAsar(source, "updated-by-steam"));
        var fixture = CreateFixture(patcher);
        var exeBefore = TestFiles.Sha256(fixture.Game.ExecutablePath);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.EnsureCurrentAsync(
            fixture.Game, fixture.Payload, $"czb_{new string('b', 64)}", CancellationToken.None));

        Assert.Contains("changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(exeBefore, TestFiles.Sha256(fixture.Game.ExecutablePath));
        Assert.Empty(Directory.Exists(Path.Combine(fixture.RuntimeRoot, "sidecars"))
            ? Directory.EnumerateFiles(Path.Combine(fixture.RuntimeRoot, "sidecars"), "loader-metadata.bin", SearchOption.AllDirectories)
            : []);
    }

    private Fixture CreateFixture(ISidecarPatcher patcher)
    {
        var id = Guid.NewGuid().ToString("N");
        var gameRoot = Path.Combine(root, id, "game");
        var runtimeRoot = Path.Combine(root, id, "runtime");
        var payloadRoot = Path.Combine(root, id, "payload");
        Directory.CreateDirectory(Path.Combine(gameRoot, "resources"));
        Directory.CreateDirectory(payloadRoot);
        var game = GameInstall.FromRoot(gameRoot);
        File.WriteAllBytes(game.ExecutablePath, [1, 3, 3, 7]);
        TestFiles.WriteMinimalAsar(game.AsarPath, "official");
        var manifest = new PayloadManifest(
            "test",
            new Dictionary<string, PayloadFile>(),
            [Hashing.Sha256(game.ExecutablePath)],
            [Hashing.AsarHeaderSha256(game.AsarPath)],
            []);
        var payload = new VerifiedPayload(manifest, payloadRoot);
        return new(game, payload, runtimeRoot, new SidecarService(runtimeRoot, patcher));
    }

    private sealed class FakePatcher(Action<string>? sourceMutator = null) : ISidecarPatcher
    {
        public Task<SidecarBuildResult> BuildAsync(
            string source,
            string output,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestFiles.WriteMinimalAsar(output, "patched-sidecar");
            var result = new SidecarBuildResult(
                Hashing.AsarHeaderSha256(source),
                new FileInfo(source).Length,
                Hashing.AsarHeaderSha256(output),
                new FileInfo(output).Length);
            sourceMutator?.Invoke(source);
            return Task.FromResult(result);
        }
    }

    private sealed record Fixture(
        GameInstall Game,
        VerifiedPayload Payload,
        string RuntimeRoot,
        SidecarService Service);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
