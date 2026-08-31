# 2026-09-01 sidecar 安装器验证

## 候选发布物

- 文件：`ChongZhenCodexInstaller.exe`
- 版本：`1.1.0`
- 平台：Windows x64，自包含 .NET 8 压缩单文件
- 大小：107,540,563 字节（约 102.6 MiB）
- SHA-256：`B3CF6FB23BFB7C0EBAE7001FFFA06D6FE6F47931765CA3DA7C946FD0F3A0BE5F`
- 内嵌载荷：35,775,430 字节，SHA-256 `B0846AEB6F66EC16C8DD308A05742CF309D576D1C7F98A99D439825259E8FA30`
- 相比 723,246,671 字节的 v1.0.0 本地候选缩小约 85.1%。

## 自动验证结果

- Node 桥接、BYOK、地区、renderer、ASAR 与 sidecar 测试：46/46 通过。
- .NET 安装器、密钥、迁移、加载器生命周期、刷新与观察器测试：55/55 通过。
- 原生加载器单元/集成测试通过；代理 DLL 的 17 个 Version API 导出全部通过。
- 动态加载审计确认代理不包含固定 ASAR 哈希或构建机路径，并且无效状态 fail-open 到官方 ASAR。
- 公开树隐私扫描通过。
- 发行载荷 315 个条目全部匹配白名单、长度与 SHA-256；现场 patcher 及其锁定依赖从最终解包布局成功加载。
- 内嵌载荷自审计、单 EXE 目录检查、构建机用户路径扫描通过。
- 载荷不包含任何 `*.asar`、预置 bridge token、Codex/ChatGPT 登录凭据、session、配置、存档、日志、用户路径、私有地址或固定模型。

## 官方文件不变式

- 新安装流程从自动发现的 Steam `resources/app.asar` 以只读方式生成 `%LOCALAPPDATA%\ChongZhenCodexBridge\sidecars\<源头部哈希>\app.asar`。
- 安装流程不写入 `ChongZhenSimulator.exe` 或官方 `resources/app.asar`；只允许在不存在或哈希属于本项目旧版本时处理游戏根目录的 `version.dll` 与 `version_original.dll`。
- 遇到未知同名 DLL 会在任何写入前拒绝。
- sidecar 元数据、源文件或输出损坏、过期、越界或版本不兼容时，加载器不重定向读取，Electron 继续打开官方 ASAR。
- v1.0.0 替换式安装状态会先按已记录哈希恢复官方文件，再迁移到 sidecar 模式。

## 隐私与账号行为

- bridge token 在目标电脑生成并限制为当前 Windows 用户访问，不存在构建时固定值。
- 安装器不会读取或打包本机 `.codex`、ChatGPT 数据、认证密钥或既有对话。
- 运行时调用目标电脑上已安装并登录的 Codex CLI，模型不写死，跟随该 CLI 当前配置。
- 游戏上下文持久化仅保存本机新建的游戏线程映射与消息哈希；切换 Codex 登录账号仍复用同一份游戏会话映射。

## 待完成的实机与发布检查

- 迁移当前机器 v1.0.0 状态并确认官方 EXE/ASAR 恢复到基线哈希。
- 用候选 EXE 安装两种模式，证明官方 EXE/ASAR 安装前后完整 SHA-256 相同。
- 从 Steam 正常点击“开始游戏”，确认 sidecar 重定向、非 CN 国际分流和真实 Codex 请求成功。
- 验证退出 60 秒后桥接自动停止、损坏/过期元数据回退、卸载与重装。
- 实机通过后合并 `main`、等待对应 GitHub Actions 成功，并上传完全相同字节的 Release EXE。
