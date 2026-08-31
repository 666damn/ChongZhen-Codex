using ChongZhenCodexInstaller.Domain;
using ChongZhenCodexInstaller.Services;

namespace ChongZhenCodexInstaller.UI;

public partial class MainForm : Form
{
    private readonly IInstallerCoordinator coordinator;
    private CancellationTokenSource? operationCancellation;

    public MainForm(IInstallerCoordinator coordinator)
    {
        this.coordinator = coordinator;
        InitializeComponent();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshStatusAsync();
    }

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        var mode = globalModeRadio.Checked ? InstallMode.BridgeAndGlobal : InstallMode.BridgeOnly;
        await RunOperationAsync(token => coordinator.InstallAsync(mode, token));
    }

    private async void RepairButton_Click(object? sender, EventArgs e) =>
        await RunOperationAsync(coordinator.RepairAsync);

    private async void RestoreButton_Click(object? sender, EventArgs e) =>
        await RunOperationAsync(coordinator.RestoreAsync);

    private async void UninstallButton_Click(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(this, "将恢复已备份的游戏文件，并删除本机桥接运行时。是否继续？",
            "确认卸载", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) await RunOperationAsync(coordinator.UninstallAsync);
    }

    private async void RefreshButton_Click(object? sender, EventArgs e) => await RefreshStatusAsync();
    private void CancelButton_Click(object? sender, EventArgs e) => operationCancellation?.Cancel();

    private async Task RunOperationAsync(Func<CancellationToken, Task<OperationResult>> operation)
    {
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            resultLabel.Text = "正在执行，请稍候……";
            var result = await operation(operationCancellation.Token);
            resultLabel.Text = result.Message;
            resultLabel.ForeColor = result.Success ? Color.FromArgb(24, 120, 72) : Color.FromArgb(180, 50, 50);
            if (result.Status is not null) ApplyStatus(result.Status);
            else await RefreshStatusAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshStatusAsync()
    {
        SetBusy(true);
        try
        {
            ApplyStatus(await coordinator.GetStatusAsync(CancellationToken.None));
            resultLabel.Text = "状态已刷新。";
            resultLabel.ForeColor = SystemColors.ControlText;
        }
        catch (Exception error)
        {
            resultLabel.Text = error.Message;
            resultLabel.ForeColor = Color.FromArgb(180, 50, 50);
        }
        finally { SetBusy(false); }
    }

    private void ApplyStatus(InstallerStatus status)
    {
        var model = MainFormViewModel.FromStatus(status);
        gamePathValue.Text = model.GamePath;
        codexPathValue.Text = model.CodexPath;
        modeValue.Text = model.Mode;
        bridgeValue.Text = model.Bridge;
        globalValue.Text = model.Global;
        gameProcessValue.Text = model.GameProcess;
        detailValue.Text = model.Detail;
        installButton.Enabled = model.CanModify;
        repairButton.Enabled = model.CanModify && status.Mode is not null;
        restoreButton.Enabled = model.CanModify && status.GlobalInstalled;
        uninstallButton.Enabled = model.CanModify && status.BridgeInstalled;
    }

    private void SetBusy(bool busy)
    {
        progressBar.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        progressBar.MarqueeAnimationSpeed = busy ? 25 : 0;
        cancelButton.Enabled = busy;
        refreshButton.Enabled = !busy;
        if (busy)
        {
            installButton.Enabled = false;
            repairButton.Enabled = false;
            restoreButton.Enabled = false;
            uninstallButton.Enabled = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) operationCancellation?.Dispose();
        base.Dispose(disposing);
    }
}
