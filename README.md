# 历史模拟器：崇祯 Codex 版

这是 Windows 安装器的公开源码仓库。安装器让《历史模拟器：崇祯》调用目标电脑上已经安装并登录的 Codex CLI，并提供可选的全球分流模式。

## 使用方式

目标电脑需要 Windows x64、已安装并至少运行登录过一次 Codex CLI，以及 Steam 版游戏。双击本地构建的单文件安装器后：

- “仅 Codex 桥接”不会替换游戏 EXE、ASAR 或 DLL。
- “Codex 桥接 + 全球分流”只替换经过严格版本校验的 `resources/app.asar`，并安装带原 DLL 转发的 `version.dll`；游戏 EXE 始终不修改。
- 默认/空地区和 `CN` 保持游戏原国内线路；其他非空地区代码走国际线路。
- 安装后监视器随 Windows 登录启动。检测到游戏运行时启动桥接，游戏退出 60 秒后停止桥接。
- 桥接跟随目标电脑当前 Codex 登录账号与当前配置模型，不写死账号或模型。

安装器会优先读取 Steam 注册表、`libraryfolders.vdf` 和 App ID `4304230` 清单，找不到时再扫描固定磁盘。运行数据集中保存在 `%LOCALAPPDATA%\ChongZhenCodexBridge`。

## 隐私边界

公开仓库和本地发布载荷不包含完整游戏资源、存档、运行日志、现有 `.codex` 数据、Codex/ChatGPT 历史、Cookie、OAuth/access/refresh token、API key、邮箱、账号 ID、套餐信息或旧 session/thread ID。

桥接器仅通过官方 `codex app-server` 在内存中检查是否已登录并读取当前模型。为满足游戏上下文持久化，它只在本机新建并保存游戏产生的线程 ID 与消息哈希，不导入旧 Codex 对话；切换 Codex 账号仍使用同一份游戏会话映射。

## 构建与验证

Node.js 24 用于桥接和 ASAR 工具，.NET 8 Windows Forms 用于安装器与监视器。完整游戏载荷和最终 EXE 始终在 Git 外本地生成，不上传公开仓库。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Output dist
powershell -NoProfile -ExecutionPolicy Bypass -File tests\release-payload.test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\assert-release-clean.ps1 -Installer dist\ChongZhenCodexInstaller.exe
```
