using Microsoft.Win32;

namespace ChongZhenCodexInstaller.Services;

public enum GameCandidateSource
{
    Cache,
    SteamManifest,
    DriveScan,
}

public sealed record GameCandidate(string Path, GameCandidateSource Source, DateTime LastWriteTimeUtc);

public sealed record GameLocatorOptions(
    string? CachedPath,
    IReadOnlyList<string> SteamRoots,
    IReadOnlyList<string> ScanRoots,
    int MaximumScanDepth = 7)
{
    public static GameLocatorOptions FromSystem(string? cachedPath = null)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var probe in new[]
        {
            (RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, @"Software\Valve\Steam", "InstallPath"),
        })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(probe.Item1, RegistryView.Default);
                if (baseKey.OpenSubKey(probe.Item2)?.GetValue(probe.Item3) is string value && Directory.Exists(value))
                    steamRoots.Add(Path.GetFullPath(value));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        var scanRoots = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)
            .ToArray();
        return new(cachedPath, steamRoots.ToArray(), scanRoots);
    }
}

public sealed class GameLocator
{
    public const string AppId = "4304230";
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "Recovery",
    };
    private readonly GameLocatorOptions options;

    public GameLocator(GameLocatorOptions options) => this.options = options;

    public Task<IReadOnlyList<GameCandidate>> FindAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<GameCandidate>>(() => Find(cancellationToken), cancellationToken);

    private IReadOnlyList<GameCandidate> Find(CancellationToken cancellationToken)
    {
        if (TryCandidate(options.CachedPath, GameCandidateSource.Cache, out var cached)) return [cached];

        var steamCandidates = new Dictionary<string, GameCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in options.SteamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var library in ReadSteamLibraries(root))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
                try
                {
                    if (!File.Exists(manifest)) continue;
                    var installDirectory = VdfReader.ReadValues(File.ReadAllText(manifest), "installdir").FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(installDirectory)) continue;
                    var path = Path.Combine(library, "steamapps", "common", installDirectory);
                    if (TryCandidate(path, GameCandidateSource.SteamManifest, out var candidate))
                        steamCandidates[candidate.Path] = candidate;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        if (steamCandidates.Count > 0) return Sort(steamCandidates.Values);

        var scanCandidates = new Dictionary<string, GameCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in options.ScanRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            Scan(root, scanCandidates, cancellationToken);
        return Sort(scanCandidates.Values);
    }

    private static IReadOnlyList<GameCandidate> Sort(IEnumerable<GameCandidate> candidates) => candidates
        .OrderBy(candidate => candidate.Source)
        .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
        .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IEnumerable<string> ReadSteamLibraries(string root)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(root)) libraries.Add(Path.GetFullPath(root));
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (var value in VdfReader.ReadValues(File.ReadAllText(vdf), "path"))
                    if (Directory.Exists(value)) libraries.Add(Path.GetFullPath(value));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        return libraries;
    }

    private void Scan(string root, IDictionary<string, GameCandidate> found, CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Path, int Depth)>();
        if (Directory.Exists(root)) queue.Enqueue((Path.GetFullPath(root), 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < 100_000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (TryCandidate(current.Path, GameCandidateSource.DriveScan, out var candidate))
            {
                found[candidate.Path] = candidate;
                continue;
            }
            if (current.Depth >= options.MaximumScanDepth) continue;
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current.Path))
                {
                    if (!SkippedDirectoryNames.Contains(Path.GetFileName(child))) queue.Enqueue((child, current.Depth + 1));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }
    }

    private static bool TryCandidate(string? path, GameCandidateSource source, out GameCandidate candidate)
    {
        candidate = null!;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var executable = Path.Combine(fullPath, "ChongZhenSimulator.exe");
            if (!File.Exists(executable) ||
                !File.Exists(Path.Combine(fullPath, "resources", "app.asar"))) return false;
            candidate = new(fullPath, source, File.GetLastWriteTimeUtc(executable));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
