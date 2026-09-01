using System.Text.RegularExpressions;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class BridgeSecretStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-secret-{Guid.NewGuid():N}");

    [Fact]
    public async Task GeneratesOnceOnTheTargetAndReusesTheInstalledSecret()
    {
        var store = new BridgeSecretStore(root);

        var first = await store.GetOrCreateAsync(CancellationToken.None);
        var second = await new BridgeSecretStore(root).GetOrCreateAsync(CancellationToken.None);

        Assert.Matches(new Regex("^czb_[a-f0-9]{64}$", RegexOptions.CultureInvariant), first);
        Assert.Equal(first, second);
        Assert.Equal(first, (await File.ReadAllTextAsync(Path.Combine(root, "bridge-token.txt"))).Trim());
    }

    [Fact]
    public async Task RejectsMalformedInstalledSecretInsteadOfSilentlyReplacingIt()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "bridge-token.txt"), "not-a-valid-secret");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BridgeSecretStore(root).GetOrCreateAsync(CancellationToken.None));

        Assert.Contains("invalid", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("not-a-valid-secret", await File.ReadAllTextAsync(Path.Combine(root, "bridge-token.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
