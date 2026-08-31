# 2026-08-31 本地发布验收

## 发布物

- 文件：`ChongZhenCodexInstaller.exe`
- 平台：Windows x64，自包含 .NET 8 单文件
- SHA-256：`0A3618B6B1E12371AE7296B34CDFDD07357975518EEF91CD075D17ADF74A3A25`
- 发布目录仅包含一个 EXE；EXE 与完整游戏载荷不进入公开 Git。

## 自动验证结果

- Bridge Node 测试：32/32 通过。
- ASAR、地区、renderer 与 BYOK 源测试：12/12 通过。
- .NET 安装器测试：41/41 通过。
- 发布载荷：20/20 白名单条目通过长度和 SHA-256 校验。
- 最终 EXE：嵌入载荷自解包校验、WinForms UI 启动和构建机用户路径扫描通过。
- 当前 Steam 游戏 EXE 保持受支持的原始 SHA-256：`68F05EDBF6E1E48F1CDCDA30E53BD6E37419A3584B684632DBFE223B1B39D785`。
- ASAR 差异验证确认只有目标 renderer bundle 改变；代理 DLL 的 17 个 Version API 导出和目标 ASAR 完整性哈希均通过。

## 隐私结论

载荷由明确文件白名单构建，未读取用户 `.codex`、旧桥接运行目录、浏览器或游戏存档目录。公开树、解压载荷和最终二进制检查均未发现构建机用户路径、现有 Codex/ChatGPT 会话、OAuth/access/refresh token、API key、邮箱、账号 ID、套餐资料、日志或存档。

安装后仅会在目标电脑本地生成桥接自身的配置、游戏新线程映射和消息哈希。官方 Codex CLI 的登录凭据仍由 Codex 自己管理，桥接只在内存中判断登录状态并读取当前配置模型。

## 覆盖边界

公开 CI 不含商业游戏资源，因此只运行桥接、补丁源、安装器和公开树测试。完整 ASAR、代理和单文件 EXE 验收仅在本地私有载荷上执行；最终 EXE 不上传公开仓库。
