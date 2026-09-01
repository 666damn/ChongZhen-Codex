using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace ChongZhenCodexInstaller.Services;

public sealed class BridgeSecretStore
{
    private static readonly Regex TokenPattern = new(
        "^czb_[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string runtimeRoot;
    private readonly string tokenPath;

    public BridgeSecretStore(string runtimeRoot)
    {
        this.runtimeRoot = Path.GetFullPath(runtimeRoot);
        tokenPath = Path.Combine(this.runtimeRoot, "bridge-token.txt");
    }

    public async Task<string> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(tokenPath)) return await ReadValidatedAsync(cancellationToken);

        Directory.CreateDirectory(runtimeRoot);
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = "czb_" + Convert.ToHexString(bytes).ToLowerInvariant();
        var temporary = tokenPath + $".writing-{Guid.NewGuid():N}";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var encoded = Encoding.UTF8.GetBytes(token);
                await output.WriteAsync(encoded, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(true);
            }
            RestrictToCurrentUser(temporary);
            try { File.Move(temporary, tokenPath, false); }
            catch (IOException) when (File.Exists(tokenPath))
            {
                File.Delete(temporary);
                return await ReadValidatedAsync(cancellationToken);
            }
            return token;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<string> ReadValidatedAsync(CancellationToken cancellationToken)
    {
        var token = (await File.ReadAllTextAsync(tokenPath, cancellationToken)).Trim();
        if (!TokenPattern.IsMatch(token))
            throw new InvalidDataException("Installed bridge secret is invalid.");
        return token;
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
