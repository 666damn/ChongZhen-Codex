using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.UI;

namespace ChongZhenCodexInstaller.Tests;

public sealed class MainFormViewModelTests
{
    [Fact]
    public void MapsOperationalStatusWithoutAccountOrCredentialFields()
    {
        var model = MainFormViewModel.FromStatus(new InstallerStatus(
            @"D:\Game", @"C:\Codex\codex.exe", InstallMode.BridgeOnly,
            false, true, false, "ready"));

        Assert.Equal(@"D:\Game", model.GamePath);
        Assert.Equal(@"C:\Codex\codex.exe", model.CodexPath);
        Assert.Equal("仅 Codex 桥接", model.Mode);
        Assert.True(model.CanModify);
        Assert.DoesNotContain("token", string.Join('|', model.GamePath, model.CodexPath, model.Mode, model.Detail), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisablesFileOperationsWhileGameIsRunning()
    {
        var model = MainFormViewModel.FromStatus(new InstallerStatus(
            null, null, null, true, false, false, "running"));

        Assert.False(model.CanModify);
        Assert.Equal("运行中（请先退出游戏）", model.GameProcess);
    }
}
