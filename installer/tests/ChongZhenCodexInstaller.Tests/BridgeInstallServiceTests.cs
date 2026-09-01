using System.Text.Json;
using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class BridgeInstallServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-bridge-install-{Guid.NewGuid():N}");

    [Fact]
    public async Task InstallsCleanRuntimeAndOnlyOperationalConfiguration()
    {
        var payloadRoot = Path.Combine(root, "payload");
        var runtimeRoot = Path.Combine(root, "runtime");
        var installerSource = Path.Combine(root, "source-installer.exe");
        Directory.CreateDirectory(Path.Combine(payloadRoot, "bridge", "src"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "bridge", "tools"));
        await File.WriteAllBytesAsync(Path.Combine(payloadRoot, "bridge", "node.exe"), [1]);
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "src", "main.js"), "// clean fixture");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "tools", "configure-game-byok.mjs"), "// clean fixture");
        await File.WriteAllBytesAsync(installerSource, [9]);
        var manifest = new PayloadManifest(
            "test",
            Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(payloadRoot, file).Replace('\\', '/'),
                file => new PayloadFile(TestFiles.Sha256(file), new FileInfo(file).Length)),
            [], [], []);
        var startup = new StartupService(new FakeStartupRegistry());
        var service = new BridgeInstallService(runtimeRoot, startup);

        var result = await service.InstallRuntimeAsync(new VerifiedPayload(manifest, payloadRoot), installerSource, CancellationToken.None);

        Assert.True(File.Exists(result.NodePath));
        Assert.True(File.Exists(result.BridgeMainPath));
        Assert.True(File.Exists(result.ConfigureGameByokPath));
        Assert.Equal([9], await File.ReadAllBytesAsync(result.InstalledExecutablePath));
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync(result.ConfigPath));
        Assert.Equal(43129, config.RootElement.GetProperty("port").GetInt32());
        var token = config.RootElement.GetProperty("token").GetString();
        Assert.Matches("^czb_[a-f0-9]{64}$", token!);
        Assert.Equal(token, (await File.ReadAllTextAsync(Path.Combine(runtimeRoot, "bridge-token.txt"))).Trim());
        var serialized = config.RootElement.GetRawText();
        Assert.DoesNotContain("account", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistersOnlyTheInstalledWatcherCommandInHkcuRunAbstraction()
    {
        var registry = new FakeStartupRegistry();
        var service = new StartupService(registry);

        service.Enable(@"C:\Runtime\ChongZhenCodexInstaller.exe");
        Assert.Equal("\"C:\\Runtime\\ChongZhenCodexInstaller.exe\" --watch", registry.Value);
        service.Disable();
        Assert.Null(registry.Value);
    }

    [Fact]
    public async Task RestoresPreviousRuntimeWhenInstallationFailsAfterDirectorySwap()
    {
        var payloadRoot = Path.Combine(root, "rollback-payload");
        var runtimeRoot = Path.Combine(root, "rollback-runtime");
        Directory.CreateDirectory(Path.Combine(payloadRoot, "bridge", "src"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "bridge", "tools"));
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "app"));
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "app", "node.exe"), "old-runtime");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "node.exe"), "new-runtime");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "src", "main.js"), "// fixture");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "tools", "configure-game-byok.mjs"), "// fixture");
        var manifest = new PayloadManifest(
            "test",
            Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(payloadRoot, file).Replace('\\', '/'),
                file => new PayloadFile(TestFiles.Sha256(file), new FileInfo(file).Length)),
            [], [], []);
        var service = new BridgeInstallService(runtimeRoot, new StartupService(new FakeStartupRegistry()));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.InstallRuntimeAsync(
            new VerifiedPayload(manifest, payloadRoot), Path.Combine(root, "missing-installer.exe"), CancellationToken.None));

        Assert.Equal("old-runtime", await File.ReadAllTextAsync(Path.Combine(runtimeRoot, "app", "node.exe")));
    }

    [Fact]
    public async Task RejectsRuntimePayloadWithoutMemoryOnlyByokConfigurator()
    {
        var payloadRoot = Path.Combine(root, "incomplete-payload");
        Directory.CreateDirectory(Path.Combine(payloadRoot, "bridge", "src"));
        await File.WriteAllBytesAsync(Path.Combine(payloadRoot, "bridge", "node.exe"), [1]);
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "bridge", "src", "main.js"), "// fixture");
        var manifest = new PayloadManifest(
            "test",
            Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories).ToDictionary(
                file => Path.GetRelativePath(payloadRoot, file).Replace('\\', '/'),
                file => new PayloadFile(TestFiles.Sha256(file), new FileInfo(file).Length)),
            [], [], []);
        var service = new BridgeInstallService(Path.Combine(root, "incomplete-runtime"), new StartupService(new FakeStartupRegistry()));
        var installerSource = Path.Combine(root, "incomplete-source-installer.exe");
        await File.WriteAllBytesAsync(installerSource, [9]);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallRuntimeAsync(
            new VerifiedPayload(manifest, payloadRoot), installerSource, CancellationToken.None));
    }

    private sealed class FakeStartupRegistry : IStartupRegistry
    {
        public string? Value { get; private set; }
        public void Set(string name, string value) => Value = value;
        public void Delete(string name) => Value = null;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
