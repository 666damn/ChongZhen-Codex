using System.Text.Json;

namespace ChongZhenCodexInstaller.Services;

public static class BridgeReadyStatus
{
    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void DeleteIfOwned(string path, int processId)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("pid", out var pid) ||
                !pid.TryGetInt32(out var actualProcessId) || actualProcessId != processId) return;
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
        }
    }
}
