namespace ChongZhenCodexInstaller.UI;

partial class MainForm
{
    private Label gamePathValue = null!;
    private Label codexPathValue = null!;
    private Label modeValue = null!;
    private Label bridgeValue = null!;
    private Label globalValue = null!;
    private Label gameProcessValue = null!;
    private Label detailValue = null!;
    private RadioButton bridgeModeRadio = null!;
    private RadioButton globalModeRadio = null!;
    private Button installButton = null!;
    private Button repairButton = null!;
    private Button restoreButton = null!;
    private Button uninstallButton = null!;
    private Button refreshButton = null!;
    private Button cancelButton = null!;
    private ProgressBar progressBar = null!;
    private Label resultLabel = null!;

    private void InitializeComponent()
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(7F, 17F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 248, 250);
        ClientSize = new Size(780, 600);
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "历史模拟器：崇祯 Codex 版";

        var title = new Label
        {
            AutoSize = true, Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            Location = new Point(28, 22), Text = "历史模拟器：崇祯 Codex 版",
        };
        var privacy = new Label
        {
            AutoSize = false, Location = new Point(31, 65), Size = new Size(715, 42),
            ForeColor = Color.FromArgb(75, 82, 92),
            Text = "使用目标电脑当前 Codex 登录与当前模型；不导入或保存 Codex/ChatGPT 密钥、账号资料、旧 session 或历史对话。",
        };
        var statusBox = new GroupBox { Location = new Point(28, 112), Size = new Size(724, 240), Text = "检测状态" };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 10), ColumnCount = 2, RowCount = 7,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gamePathValue = AddStatusRow(table, 0, "游戏路径", "检测中……");
        codexPathValue = AddStatusRow(table, 1, "Codex CLI", "检测中……");
        modeValue = AddStatusRow(table, 2, "当前模式", "检测中……");
        bridgeValue = AddStatusRow(table, 3, "桥接运行时", "检测中……");
        globalValue = AddStatusRow(table, 4, "全球分流", "检测中……");
        gameProcessValue = AddStatusRow(table, 5, "游戏进程", "检测中……");
        detailValue = AddStatusRow(table, 6, "说明", "检测中……");
        statusBox.Controls.Add(table);

        var modeBox = new GroupBox { Location = new Point(28, 366), Size = new Size(724, 78), Text = "安装模式" };
        bridgeModeRadio = new RadioButton { AutoSize = false, Checked = true, Location = new Point(24, 27), Size = new Size(310, 34), Text = "仅 Codex 桥接（不替换 ASAR/DLL）" };
        globalModeRadio = new RadioButton { AutoSize = false, Location = new Point(350, 27), Size = new Size(330, 34), Text = "Codex 桥接 + 全球分流" };
        modeBox.Controls.AddRange([bridgeModeRadio, globalModeRadio]);

        installButton = MakeButton("安装 / 切换", 28, InstallButton_Click);
        repairButton = MakeButton("修复", 154, RepairButton_Click);
        restoreButton = MakeButton("恢复原版", 280, RestoreButton_Click);
        uninstallButton = MakeButton("卸载", 406, UninstallButton_Click);
        refreshButton = MakeButton("重新检测", 532, RefreshButton_Click);
        cancelButton = MakeButton("取消", 658, CancelButton_Click);
        cancelButton.Width = 94;
        cancelButton.Enabled = false;

        progressBar = new ProgressBar { Location = new Point(28, 510), Size = new Size(724, 8) };
        resultLabel = new Label { AutoEllipsis = true, Location = new Point(28, 533), Size = new Size(724, 38), Text = "准备就绪。" };

        Controls.AddRange([
            title, privacy, statusBox, modeBox, installButton, repairButton, restoreButton,
            uninstallButton, refreshButton, cancelButton, progressBar, resultLabel,
        ]);
        ResumeLayout(false);
        PerformLayout();
    }

    private static Label AddStatusRow(TableLayoutPanel table, int row, string name, string value)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 14.285F));
        table.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(75, 82, 92), Text = name }, 0, row);
        var label = new Label { AutoEllipsis = true, Dock = DockStyle.Fill, Text = value };
        table.Controls.Add(label, 1, row);
        return label;
    }

    private static Button MakeButton(string text, int left, EventHandler handler)
    {
        var button = new Button { Location = new Point(left, 462), Size = new Size(112, 34), Text = text, UseVisualStyleBackColor = true };
        button.Click += handler;
        return button;
    }
}
