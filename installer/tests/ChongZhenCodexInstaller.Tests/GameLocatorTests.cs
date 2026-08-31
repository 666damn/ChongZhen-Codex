using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class GameLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-locator-{Guid.NewGuid():N}");

    [Fact]
    public async Task PrefersValidatedCachedPath()
    {
        var cached = CreateValidGame(Path.Combine(root, "cached"));
        var locator = new GameLocator(new GameLocatorOptions(cached, [], [Path.Combine(root, "scan")]));

        var result = await locator.FindAsync(CancellationToken.None);

        Assert.Equal(cached, result[0].Path);
        Assert.Equal(GameCandidateSource.Cache, result[0].Source);
    }

    [Fact]
    public async Task ResolvesApp4304230AcrossSteamLibraries()
    {
        var steamRoot = Path.Combine(root, "steam");
        var library = Path.Combine(root, "library");
        Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var libraries = File.ReadAllText(Path.Combine(fixtureRoot, "libraryfolders.vdf"))
            .Replace("{LIBRARY_PATH}", library.Replace("\\", "\\\\"), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), libraries);
        File.Copy(
            Path.Combine(fixtureRoot, "appmanifest_4304230.acf"),
            Path.Combine(library, "steamapps", "appmanifest_4304230.acf"));
        var game = CreateValidGame(Path.Combine(library, "steamapps", "common", "历史模拟器：崇祯"));

        var result = await new GameLocator(new GameLocatorOptions(null, [steamRoot], [])).FindAsync(CancellationToken.None);

        Assert.Contains(result, candidate => candidate.Path == game && candidate.Source == GameCandidateSource.SteamManifest);
    }

    [Fact]
    public async Task FallsBackToBoundedDirectoryScan()
    {
        var scanRoot = Path.Combine(root, "drive");
        var game = CreateValidGame(Path.Combine(scanRoot, "Games", "Steam", "common", "ChongZhen"));

        var result = await new GameLocator(new GameLocatorOptions(null, [], [scanRoot], MaximumScanDepth: 5))
            .FindAsync(CancellationToken.None);

        Assert.Contains(result, candidate => candidate.Path == game && candidate.Source == GameCandidateSource.DriveScan);
    }

    [Fact]
    public async Task AcceptsFreshSteamInstallBeforeProxyDllExists()
    {
        var path = Path.Combine(root, "fresh");
        Directory.CreateDirectory(Path.Combine(path, "resources"));
        File.WriteAllBytes(Path.Combine(path, "ChongZhenSimulator.exe"), [1]);
        File.WriteAllBytes(Path.Combine(path, "resources", "app.asar"), [2]);

        var result = await new GameLocator(new GameLocatorOptions(path, [], [])).FindAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(Path.GetFullPath(path), result[0].Path);
    }

    private static string CreateValidGame(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "resources"));
        File.WriteAllBytes(Path.Combine(path, "ChongZhenSimulator.exe"), [1]);
        File.WriteAllBytes(Path.Combine(path, "resources", "app.asar"), [2]);
        File.WriteAllBytes(Path.Combine(path, "version.dll"), [3]);
        return Path.GetFullPath(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
