using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class CodexLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"cz-codex-locator-{Guid.NewGuid():N}");

    [Fact]
    public void PrefersTargetsPathAndNeverNeedsCodexConfiguration()
    {
        var pathDirectory = Path.Combine(root, "path");
        var local = Path.Combine(root, "local");
        Directory.CreateDirectory(pathDirectory);
        Directory.CreateDirectory(Path.Combine(local, "OpenAI", "Codex", "bin", "999"));
        var pathCodex = Touch(Path.Combine(pathDirectory, "codex.exe"));
        Touch(Path.Combine(local, "OpenAI", "Codex", "bin", "999", "codex.exe"));

        var result = new CodexLocator().Find(new Dictionary<string, string?>
        {
            ["PATH"] = pathDirectory,
            ["LOCALAPPDATA"] = local,
        });

        Assert.Equal(pathCodex, result);
    }

    [Fact]
    public void UsesNewestOfficialLocalInstallationWhenPathIsMissing()
    {
        var local = Path.Combine(root, "local");
        var oldPath = Touch(Path.Combine(local, "OpenAI", "Codex", "bin", "001", "codex.exe"));
        var newPath = Touch(Path.Combine(local, "OpenAI", "Codex", "bin", "999", "codex.exe"));

        var result = new CodexLocator().Find(new Dictionary<string, string?>
        {
            ["PATH"] = string.Empty,
            ["LOCALAPPDATA"] = local,
        });

        Assert.NotEqual(oldPath, result);
        Assert.Equal(newPath, result);
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        return Path.GetFullPath(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
