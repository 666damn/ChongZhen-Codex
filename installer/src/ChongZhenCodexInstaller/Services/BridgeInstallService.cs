using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace ChongZhenCodexInstaller.Services;

public sealed record BridgeRuntimePaths(
    string RuntimeRoot,
    string NodePath,
    string BridgeMainPath,
    string ConfigPath,
    string InstalledExecutablePath);

public sealed class BridgeInstallService(string runtimeRoot, StartupService startupService)
{
    private readonly string runtimeRoot = Path.GetFullPath(runtimeRoot);

    public async Task<BridgeRuntimePaths> InstallRuntimeAsync(
        VerifiedPayload payload,
        string installerSource,
        CancellationToken cancellationToken)
    {
        var bridgeFiles = payload.Manifest.Files.Keys
            .Where(path => path.StartsWith("bridge/", StringComparison.Ordinal))
            .ToArray();
        if (bridgeFiles.Length == 0 || !bridgeFiles.Contains("bridge/node.exe") ||
            !bridgeFiles.Contains("bridge/src/main.js") || !bridgeFiles.Contains("bridge/bridge-id.txt"))
            throw new InvalidDataException("Bridge runtime payload is incomplete.");

        var bridgeId = (await File.ReadAllTextAsync(payload.GetPath("bridge/bridge-id.txt"), cancellationToken)).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(bridgeId, "^czb_[a-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidDataException("Bridge release identifier is invalid.");

        Directory.CreateDirectory(runtimeRoot);
        var appRoot = Path.Combine(runtimeRoot, "app");
        var stagingRoot = Path.Combine(runtimeRoot, $".app-installing-{Guid.NewGuid():N}");
        var previousRoot = Path.Combine(runtimeRoot, $".app-previous-{Guid.NewGuid():N}");
        var previousMoved = false;
        var appActivated = false;
        Directory.CreateDirectory(stagingRoot);
        try
        {
            foreach (var relative in bridgeFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = payload.GetPath(relative);
                var destination = Path.Combine(stagingRoot, relative["bridge/".Length..].Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(source, destination, cancellationToken);
                var expected = payload.Manifest.Files[relative];
                if (new FileInfo(destination).Length != expected.Length ||
                    !string.Equals(Hashing.Sha256(destination), expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Installed bridge file verification failed: {relative}");
            }

            if (Directory.Exists(appRoot))
            {
                Directory.Move(appRoot, previousRoot);
                previousMoved = true;
            }
            try { Directory.Move(stagingRoot, appRoot); }
            catch
            {
                if (Directory.Exists(previousRoot)) Directory.Move(previousRoot, appRoot);
                throw;
            }
            appActivated = true;

            var installedExecutable = Path.Combine(runtimeRoot, "ChongZhenCodexInstaller.exe");
            await AtomicCopyAsync(installerSource, installedExecutable, cancellationToken);
            var configPath = Path.Combine(runtimeRoot, "config.json");
            await AtomicWriteAsync(configPath, JsonSerializer.Serialize(new { port = 43129, token = bridgeId }), cancellationToken);
            RestrictToCurrentUser(configPath);
            startupService.Enable(installedExecutable);

            if (Directory.Exists(previousRoot)) Directory.Delete(previousRoot, true);
            return new(
                runtimeRoot,
                Path.Combine(appRoot, "node.exe"),
                Path.Combine(appRoot, "src", "main.js"),
                configPath,
                installedExecutable);
        }
        catch
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
            if (appActivated && Directory.Exists(appRoot)) Directory.Delete(appRoot, true);
            if (previousMoved && Directory.Exists(previousRoot)) Directory.Move(previousRoot, appRoot);
            throw;
        }
    }

    private static async Task AtomicCopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + $".writing-{Guid.NewGuid():N}";
        try
        {
            await CopyFileAsync(source, temporary, cancellationToken);
            if (!string.Equals(Hashing.Sha256(source), Hashing.Sha256(temporary), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Installed file verification failed: {destination}");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task AtomicWriteAsync(string destination, string contents, CancellationToken cancellationToken)
    {
        var temporary = destination + $".writing-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(true);
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
