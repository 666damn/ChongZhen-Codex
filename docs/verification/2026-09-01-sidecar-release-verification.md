# 2026-09-01 Sidecar 安装器验收记录

## 最终候选

- 文件：`ChongZhenCodexInstaller.exe`
- 版本：`1.1.0`
- 平台：Windows x64，自包含 .NET 8 压缩单文件
- 大小：107,545,171 字节（约 102.6 MiB）
- SHA-256：`B864E586E64BB564800B1E6BDDC9CA30BF44CC61A9D7BD84C26CFD95AB940512`
- 内嵌载荷：35,778,938 字节，SHA-256 `20E54CF30749AF1C58DE18F5D29D1E01D0E65ACD028647FC052577779E0B4AED`
- 相比 723,246,671 字节的 v1.0.0 本地候选缩小约 85.1%。

## 自动验证

- Node 桥接、BYOK、地区、renderer、ASAR 与 sidecar：46/46 通过。
- .NET 安装、密钥、迁移、重装事务、观察器与进程清理：60/60 通过。
- 原生加载器测试通过；代理 DLL 的 17 个 Version API 导出全部通过。
- 原生回归覆盖 Windows 导入表完成解析后再安装 `CreateFileW` 重定向，避免加载器覆盖早期 IAT 钩子。
- Electron `INTEGRITY/ELECTRONASAR` 资源中的期望散列在运行时解析，不假设其等于官方 ASAR 当前头散列。
- 公开树隐私扫描、315 个载荷文件白名单审计、内嵌载荷自审计和构建机用户路径扫描全部通过。
- EXE 和载荷均不包含 `*.asar`、预置 bridge token、Codex/ChatGPT 登录数据、session、API key、用户配置、存档、日志、私有地址或固定模型。

## 官方文件与更新行为

- 安装器从自动发现的 Steam 游戏目录只读 `resources/app.asar`，现场生成 `%LOCALAPPDATA%\ChongZhenCodexBridge\sidecars\<源头散列>\app.asar`。
- 安装、重装和实机启动前后，`ChongZhenSimulator.exe` 与官方 `resources/app.asar` 的完整 SHA-256 均保持不变。
- 最终实机基线：EXE `68F05EDBF6E1E48F1CDCDA30E53BD6E37419A3584B684632DBFE223B1B39D785`；ASAR `FD24B1C407C2BF18E2B570CC443C4CF50CE98B81C002A5192BE948692014B4A6`。
- 游戏根目录只管理项目自己的 `version.dll` 与系统副本 `version_original.dll`；未知同名 DLL 在任何写入前拒绝。
- 同模式重装不再先卸载旧代理；先停止并等待精确运行时路径下的观察器/Node 进程，再事务性覆盖项目文件。
- 游戏更新导致源头散列变化时，观察器只在游戏停止后重建 sidecar；未知版本保持 fail-open，不改官方文件。

## 实机验收

- v1 替换式状态已迁移到 sidecar 模式；官方 EXE/ASAR 恢复并保持上述散列。
- 最终候选覆盖重装成功：旧观察器退出后被新观察器替换，代理与载荷散列一致，未出现半安装状态。
- 直接启动最终已安装游戏：主窗口出现，无 `Integrity check failed for asar archive`。
- 观察器随游戏自动启动桥接；一次真实本机 Codex chat completion 返回 HTTP 200 且内容非空。
- 游戏退出后，桥接进程和 `bridge-ready.json` 在 60 秒宽限内清理，观察器继续常驻。
- Steam 客户端当前未登录（Steam ID 为 0），因此从 Steam“开始游戏”和 Steam 鉴权票据仍是发布前唯一外部验收项；这不是加载器或 Codex 桥接失败。

## 隐私与账号行为

- bridge token 仅在目标电脑生成并限制为当前 Windows 用户访问。
- 构建和打包不读取本机 `.codex`、ChatGPT 数据、认证密钥或既有对话。
- 运行时调用目标电脑已安装并登录的 Codex CLI；不写死模型，跟随该 CLI 当前配置与登录账号。
- 游戏上下文仅保存本地新建的游戏线程映射与消息散列；切换 Codex 登录账号仍复用同一份游戏会话映射。

## 发布门槛

- 登录 Steam 后从库中点击“开始游戏”，确认窗口、国际分流与一次真实游戏内 Codex 请求。
- 该项通过后合并 `main`，等待 GitHub Actions 成功，并上传与上述 SHA-256 完全一致的 Release EXE。
