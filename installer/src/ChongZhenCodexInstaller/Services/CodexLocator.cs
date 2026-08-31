namespace ChongZhenCodexInstaller.Services;

public sealed class CodexLocator
{
    public string Find(IReadOnlyDictionary<string, string?> environment)
    {
        environment.TryGetValue("PATH", out var pathValue);
        foreach (var directory in (pathValue ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var filename in new[] { "codex.exe", "codex.cmd" })
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.Trim('"'), filename));
                if (File.Exists(candidate)) return candidate;
            }
        }

        environment.TryGetValue("LOCALAPPDATA", out var localAppData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(binRoot).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(directory, "codex.exe");
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }

        throw new FileNotFoundException("Codex CLI was not found on PATH or in the official local installation directory.");
    }
}
