using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChongZhenCodexInstaller.Services;

public sealed class NodeSidecarPatcher(string nodeExecutable, string patcherScript) : ISidecarPatcher
{
    private static readonly Regex HashPattern = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new("^czb_[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<SidecarBuildResult> BuildAsync(
        string source,
        string output,
        string token,
        CancellationToken cancellationToken)
    {
        if (!TokenPattern.IsMatch(token)) throw new InvalidDataException("Bridge token is invalid.");
        if (!File.Exists(source)) throw new FileNotFoundException("Official ASAR is missing.", source);
        if (Path.IsPathRooted(nodeExecutable) && !File.Exists(nodeExecutable))
            throw new FileNotFoundException("Bundled Node executable is missing.", nodeExecutable);
        if (!File.Exists(patcherScript)) throw new FileNotFoundException("Sidecar patcher is missing.", patcherScript);

        var start = new ProcessStartInfo
        {
            FileName = nodeExecutable,
            WorkingDirectory = Path.GetDirectoryName(patcherScript)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(patcherScript);
        start.ArgumentList.Add("--input");
        start.ArgumentList.Add(Path.GetFullPath(source));
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(Path.GetFullPath(output));
        start.Environment["CHONGZHEN_BRIDGE_TOKEN"] = token;

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Unable to start the sidecar patcher.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (stdout.Length > 64 * 1024 || stderr.Length > 64 * 1024)
            throw new InvalidDataException("Sidecar patcher output exceeded the allowed size.");
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Sidecar patcher failed with exit code {process.ExitCode}: {stderr.Trim()}");
        var json = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? throw new InvalidDataException("Sidecar patcher returned no result.");
        var result = JsonSerializer.Deserialize<SidecarBuildResult>(json, JsonOptions)
            ?? throw new InvalidDataException("Sidecar patcher result is invalid.");
        if (!HashPattern.IsMatch(result.SourceHeaderSha256) || !HashPattern.IsMatch(result.SidecarHeaderSha256) ||
            result.SourceLength <= 0 || result.SidecarLength <= 0)
            throw new InvalidDataException("Sidecar patcher result failed validation.");
        return result;
    }
}
