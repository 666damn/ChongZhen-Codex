using System.Text.Json;
using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.Services;

public interface ICoordinatorStateStore
{
    CoordinatorState? Load();
    void Save(CoordinatorState state);
    void Remove();
}

public sealed class CoordinatorStateStore(string runtimeRoot) : ICoordinatorStateStore
{
    private readonly string path = Path.Combine(Path.GetFullPath(runtimeRoot), "coordinator-state.json");
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public CoordinatorState? Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<CoordinatorState>(File.ReadAllText(path), Options)
        : null;

    public void Save(CoordinatorState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".writing-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Remove() => File.Delete(path);
}
