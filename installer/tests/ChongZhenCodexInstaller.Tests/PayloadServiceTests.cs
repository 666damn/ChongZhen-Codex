using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class PayloadServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-payload-{Guid.NewGuid():N}");

    [Fact]
    public void ExtractsOnlyManifestedFilesAfterHashVerification()
    {
        using var zip = TestFiles.CreatePayloadZip(new Dictionary<string, byte[]>
        {
            ["global/app.asar"] = [1, 2, 3],
            ["global/version.dll"] = [4, 5],
        });

        var payload = new PayloadService().ExtractAndVerify(zip, root);

        Assert.Equal("test", payload.Manifest.Version);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(payload.GetPath("global/app.asar")));
    }

    [Fact]
    public void RejectsHashMismatchAndLeavesNoStagedPayload()
    {
        using var zip = TestFiles.CreatePayloadZip(new Dictionary<string, byte[]> { ["global/app.asar"] = [9] }, true);

        Assert.Throws<InvalidDataException>(() => new PayloadService().ExtractAndVerify(zip, root));
        Assert.False(Directory.Exists(root));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
