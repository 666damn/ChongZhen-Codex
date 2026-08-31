using ChongZhenCodexInstaller.Domain;

namespace ChongZhenCodexInstaller.UI;

public sealed record MainFormViewModel(
    string GamePath,
    string CodexPath,
    string Mode,
    string Bridge,
    string Global,
    string GameProcess,
    string Detail,
    bool CanModify)
{
    public static MainFormViewModel FromStatus(InstallerStatus status) => new(
        status.GamePath ?? "未找到",
        status.CodexPath ?? "未找到",
        status.Mode switch
        {
            InstallMode.BridgeOnly => "仅 Codex 桥接",
            InstallMode.BridgeAndGlobal => "Codex 桥接 + 全球分流",
            _ => "未安装",
        },
        status.BridgeInstalled ? "已安装" : "未安装",
        status.GlobalInstalled ? "已安装" : "未安装",
        status.GameRunning ? "运行中（请先退出游戏）" : "未运行",
        status.Detail,
        !status.GameRunning);
}
