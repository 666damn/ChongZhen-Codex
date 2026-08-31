using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.Tests;

public sealed class InstallerRuntimeTests
{
    [Fact]
    public void RuntimeRootUsesOnlyTargetLocalAppData()
    {
        var result = InstallerRuntime.ResolveRuntimeRoot(new Dictionary<string, string?>
        {
            ["LOCALAPPDATA"] = @"X:\TargetUser\Local",
        });

        Assert.Equal(Path.GetFullPath(@"X:\TargetUser\Local\ChongZhenCodexBridge"), result);
    }
}
