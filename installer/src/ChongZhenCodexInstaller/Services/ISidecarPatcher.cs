namespace ChongZhenCodexInstaller.Services;

public sealed record SidecarBuildResult(
    string SourceHeaderSha256,
    long SourceLength,
    string SidecarHeaderSha256,
    long SidecarLength);

public interface ISidecarPatcher
{
    Task<SidecarBuildResult> BuildAsync(
        string source,
        string output,
        string token,
        CancellationToken cancellationToken);
}
