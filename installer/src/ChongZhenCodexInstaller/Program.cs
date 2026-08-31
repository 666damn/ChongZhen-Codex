using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;
using ChongZhenCodexInstaller.UI;

namespace ChongZhenCodexInstaller;

static class Program
{
    [STAThread]
    static async Task Main(string[] args)
    {
        var options = CommandLine.Parse(args);
        if (options.Command == AppCommand.Watch)
        {
            await RunWatcherAsync();
            return;
        }
        if (options.Command == AppCommand.AuditPayload)
        {
            try { ReleasePayloadAudit.Run(); }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (options.Command == AppCommand.RefreshSidecar)
        {
            try { await InstallerRuntime.RefreshSidecarAsync(CancellationToken.None); }
            catch { Environment.ExitCode = 1; }
            return;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            var coordinator = InstallerRuntime.CreateCoordinator();
            if (options.Command == AppCommand.UserInterface)
            {
                Application.Run(new MainForm(coordinator));
                return;
            }

            var result = options.Command switch
            {
                AppCommand.Status => new OperationResult(true, FormatStatus(await coordinator.GetStatusAsync(CancellationToken.None))),
                AppCommand.InstallBridge => await coordinator.InstallAsync(InstallMode.BridgeOnly, CancellationToken.None),
                AppCommand.InstallGlobal => await coordinator.InstallAsync(InstallMode.BridgeAndGlobal, CancellationToken.None),
                AppCommand.Uninstall => await coordinator.UninstallAsync(CancellationToken.None),
                _ => new OperationResult(false, "未知命令。"),
            };
            MessageBox.Show(result.Message, result.Success ? "操作完成" : "操作失败", MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static async Task RunWatcherAsync()
    {
        using var mutex = new Mutex(true, @"Local\ChongZhenCodexBridgeWatcher", out var created);
        if (!created) return;
        try
        {
            var runtime = InstallerRuntime.CreateWatcher();
            using (runtime.Processes)
                await runtime.Watcher.RunAsync(CancellationToken.None);
        }
        catch
        {
            // Startup watcher is intentionally silent; the management UI reports runtime status.
        }
    }

    private static string FormatStatus(InstallerStatus status) =>
        $"游戏：{status.GamePath ?? "未找到"}\nCodex：{status.CodexPath ?? "未找到"}\n模式：{status.Mode?.ToString() ?? "未安装"}\n{status.Detail}";
}
