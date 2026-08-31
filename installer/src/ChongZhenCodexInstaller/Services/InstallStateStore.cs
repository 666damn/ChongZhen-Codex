using System.Text.Json;
using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public sealed class InstallStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string RootPath { get; }
    public string StatePath => Path.Combine(RootPath, "install-state.json");

    public InstallStateStore(string rootPath) => RootPath = Path.GetFullPath(rootPath);

    public string CreateBackupDirectory()
    {
        var directory = Path.Combine(RootPath, "backups", $"global-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Save(PatchInstallState state)
        => SaveSerialized(state);

    public void Save(LoaderInstallState state)
        => SaveSerialized(state);

    private void SaveSerialized<T>(T state)
    {
        Directory.CreateDirectory(RootPath);
        var temporary = StatePath + $".writing-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporary, StatePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public PatchInstallState? Load() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<PatchInstallState>(File.ReadAllText(StatePath), JsonOptions)
        : null;

    public PatchInstallState? LoadLegacy()
    {
        var state = Load();
        return state?.InstalledSha256.ContainsKey("resources/app.asar") == true ? state : null;
    }

    public LoaderInstallState? LoadLoader()
    {
        var state = Load();
        if (state is null || state.InstalledSha256.ContainsKey("resources/app.asar")) return null;
        return new(
            state.GamePath,
            state.Version,
            state.Originals,
            state.InstalledSha256,
            state.InstalledAt);
    }

    public void Remove() => File.Delete(StatePath);
}
