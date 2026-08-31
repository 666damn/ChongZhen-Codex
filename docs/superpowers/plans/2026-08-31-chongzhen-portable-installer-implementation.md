# 《历史模拟器：崇祯》Codex 单文件安装器 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建、测试并发布一个不会携带本机用户数据的 Windows x64 单文件安装器，自动定位 Steam 游戏，安装本机 Codex 桥接，并可选安装全球国际服分流补丁。

**Architecture:** 使用 .NET 8 WinForms 自包含单文件程序负责 GUI、定位、安装状态机、恢复和后台监听；内嵌 Node.js 24.13.1 运行现有 JavaScript 桥接。全球模式直接安装这台电脑按当前方式生成并验证的 ASAR 与 proxy DLL，目标机只在原始游戏哈希完全匹配时接受载荷，不在目标机重新反编译或打包。

**Tech Stack:** C# / .NET 8 / WinForms、Node.js 24.13.1、Node 内置测试与 SQLite、`@electron/asar`、Codex app-server JSONL RPC、PowerShell、GitHub CLI

**Spec:** `docs/superpowers/specs/2026-08-31-chongzhen-portable-installer-design.md`

## Global Constraints

- 最终发布产物为 `dist/ChongZhenCodexInstaller.exe`，支持 Windows x64。
- 目标机必须已经安装并运行过 Codex CLI；不得内嵌或迁移 Codex 登录。
- 不得读取、复制、保存或发布现有 `.codex`、ChatGPT/Codex token、Cookie、邮箱、账号对象、历史聊天、用户日志或完整 BYOK 配置。
- 不得修改 `ChongZhenSimulator.exe`；每次全球补丁操作前后都必须验证其 SHA-256 不变。
- 公开 Git 仓库不得包含完整游戏 ASAR、EXE、DLL 原件、存档、开发机备份、运行数据库、日志、token 或绝对用户路径。
- 带完整游戏补丁载荷的最终 EXE 只在本机生成和交付，不上传到公开 GitHub 仓库或 Release。
- 游戏 Steam App ID 为 `4304230`；国家空值与 `CN` 保持国内服，所有其他非空国家代码走国际服。
- 桥接只绑定 `127.0.0.1:43129`，使用随机本地 bearer，不回退到 OpenAI Platform API Key。
- 模型在每次 Codex turn 前通过 `config/read` 获取，不写死、不持久化。
- 所有功能和修复先写失败测试，再实现最小代码，再运行测试。

---

## File Structure

```text
.github/workflows/ci.yml                         Windows 构建、测试和公开树检查
.gitattributes                                   文本换行与二进制规则
.gitignore                                       严格允许列表，排除所有本机/游戏产物
LICENSE                                          仅覆盖本仓库原创代码的 MIT 许可
README.md                                        中文项目说明、模式、限制和构建方法
bridge/                                          经过隐私收敛的现有桥接源码与 Node 测试
installer/ChongZhenCodexInstaller.sln            .NET 解决方案
installer/src/ChongZhenCodexInstaller/            WinForms 安装器与 watcher
installer/src/.../Domain/                         安装模式、状态和清单值对象
installer/src/.../Services/GameLocator.cs         Steam/磁盘游戏定位
installer/src/.../Services/CodexLocator.cs        本机 codex.exe 定位
installer/src/.../Services/PayloadService.cs      内嵌载荷解压与哈希验证
installer/src/.../Services/PatchService.cs        备份、原子替换、恢复
installer/src/.../Services/BridgeService.cs       运行时安装和 BYOK 配置调用
installer/src/.../Services/StartupService.cs      HKCU 自启动管理
installer/src/.../Services/WatcherService.cs      游戏进程监听和桥接生命周期
installer/src/.../UI/MainForm.cs                  安装/修复/恢复/卸载 GUI
installer/tests/ChongZhenCodexInstaller.Tests/    xUnit 单元与临时目录集成测试
scripts/assert-public-tree.ps1                    发布树与敏感数据门禁
scripts/bootstrap-dotnet.ps1                      项目内安装 .NET 8 SDK
scripts/fetch-node.ps1                            校验下载 Node 24.13.1 win-x64 ZIP
scripts/build-payload.ps1                         生成干净 payload 和 manifest
scripts/build-release.ps1                         单文件 EXE 构建
scripts/build-patched-asar.mjs                    在开发机按当前方式生成受支持 ASAR
scripts/stage-current-payload.ps1                 收集当前已验证 ASAR/DLL，不提交载荷
scripts/patch-region.mjs                          精确 ASAR renderer 转换
scripts/configure-game-byok.mjs                   无完整备份的 localStorage 合并/删除
ChongZhenAUProxyPatch/version_proxy.cpp            当前完整性兼容 DLL 源码
ChongZhenAUProxyPatch/build.ps1                    当前 proxy DLL 构建脚本
tests/                                            ASAR、地区、隐私和发布集成测试
```

### Task 1: 建立安全的公开 Git 仓库基线

**Files:**
- Create: `.gitignore`
- Create: `.gitattributes`
- Create: `LICENSE`
- Create: `README.md`
- Create: `scripts/assert-public-tree.ps1`
- Create: `tests/public-tree.test.ps1`
- Modify: `docs/superpowers/specs/2026-08-31-chongzhen-portable-installer-design.md`

**Interfaces:**
- Consumes: 远端空仓库 `https://github.com/666damn/ChongZhen-Codex`
- Produces: `scripts/assert-public-tree.ps1 -Root <path> [-Staged]`，失败返回非零退出码

- [ ] **Step 1: 写公开树门禁的失败测试**

```powershell
$fixture = Join-Path $TestDrive 'repo'
New-Item -ItemType Directory -Path $fixture | Out-Null
$field = 'access_' + 'token'
$value = 'gh' + 'o_' + ('A' * 32)
Set-Content (Join-Path $fixture 'leak.json') ("{`"$field`":`"$value`"}")
& $AssertScript -Root $fixture
if ($LASTEXITCODE -eq 0) { throw 'secret fixture must fail' }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `powershell -NoProfile -File tests/public-tree.test.ps1`

Expected: FAIL，因为 `scripts/assert-public-tree.ps1` 尚不存在。

- [ ] **Step 3: 实现严格允许列表和内容扫描**

门禁拒绝以下路径或扩展：`node_modules`、`build`、`work`、`diagnostics`、`backups`、`*.asar`、`*.jsc`、`*.exe`、`*.dll`、`*.db*`、`*.log`、`*.cer`、`*.png`、`*.webp`、`*.mp4`。内容扫描至少拒绝 `gho_`、`sk-`、`access_token`、`refresh_token`、非空 `api_key`、`C:\\Users\\admin` 和现有 bridge bearer。

`.gitignore` 使用允许列表只开放原创源码、测试、文档和构建脚本；`ChongZhenSimulator_*`、`build/`、`work/`、本机 runtime 和所有反编译完整资源必须忽略。

- [ ] **Step 4: 运行门禁测试并扫描拟提交文件**

Run: `powershell -NoProfile -File tests/public-tree.test.ps1`

Expected: PASS。

Run: `powershell -NoProfile -File scripts/assert-public-tree.ps1 -Root .`

Expected: PASS，输出扫描文件数且无秘密命中。

- [ ] **Step 5: 初始化仓库并首次安全提交**

```powershell
git init -b main
git remote add origin https://github.com/666damn/ChongZhen-Codex.git
git add .gitignore .gitattributes LICENSE README.md scripts/assert-public-tree.ps1 tests/public-tree.test.ps1 docs/superpowers/specs/2026-08-31-chongzhen-portable-installer-design.md docs/superpowers/plans/2026-08-31-chongzhen-portable-installer-implementation.md
powershell -NoProfile -File scripts/assert-public-tree.ps1 -Root . -Staged
git commit -m "chore: initialize sanitized public repository"
git push -u origin main
```

### Task 2: 收敛桥接隐私边界并只使用本机 Codex

**Files:**
- Modify: `bridge/package.json`
- Modify: `bridge/package-lock.json`
- Modify: `bridge/src/codex-discovery.js`
- Modify: `bridge/src/codex-client.js`
- Modify: `bridge/src/bridge-service.js`
- Modify: `bridge/src/main.js`
- Modify: `bridge/test/codex-client.test.js`
- Modify: `bridge/test/bridge-service.test.js`
- Create: `bridge/test/privacy.test.js`

**Interfaces:**
- Consumes: `codex app-server --listen stdio://`
- Produces: `discoverCodexExecutable(env): string`、`CodexClient.isLoggedIn(): Promise<boolean>`、`CodexClient.readCurrentModel(): Promise<string|null>`

- [ ] **Step 1: 写失败测试证明不优先使用包内 Codex 且账号对象不落盘**

```js
test('discovers PATH codex before local OpenAI versions', () => {
  assert.equal(discoverCodexExecutable(env), pathCodex);
});

test('login check returns only a boolean', async () => {
  rpc.reply('account/read', { account: { email: 'private@example.test', planType: 'pro' } });
  assert.equal(await client.isLoggedIn(), true);
  assert.equal(JSON.stringify(stateStore.dump()).includes('private@example.test'), false);
});
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd bridge; node --test test/codex-client.test.js test/privacy.test.js`

Expected: FAIL，现有 API 为 `readAccount()` 且发现顺序仍含包内 Codex。

- [ ] **Step 3: 实现本机 Codex 发现和最小登录判断**

删除运行时对 `@openai/codex` 的依赖。`discoverCodexExecutable` 按环境变量、PATH、LocalAppData 版本目录顺序搜索。`isLoggedIn` 调用 `account/read` 后立即返回 `Boolean(result.account)`，不把对象传给调用者或日志。

每次 `thread/start`、`thread/resume`、`turn/start` 前调用 `config/read`，只在请求参数中使用当前模型。

- [ ] **Step 4: 增加日志隐私断言并运行全部 bridge 测试**

Run: `cd bridge; npm test`

Expected: 全部 PASS，临时日志不包含测试邮箱、token、完整 prompt 或 response。

- [ ] **Step 5: 提交**

```powershell
git add bridge
git commit -m "feat: harden local Codex bridge privacy"
```

### Task 3: 改造无秘密 BYOK 配置器

**Files:**
- Modify: `scripts/configure-game-byok.mjs`
- Modify: `tests/game-byok-config.test.mjs`
- Create: `tests/game-byok-privacy.test.mjs`

**Interfaces:**
- Produces: `mergeLocalCodexStrategy(currentConfig, token): Config`
- Produces: `removeLocalCodexStrategy(currentConfig): Config`
- CLI: `node scripts/configure-game-byok.mjs --port <n> --token <value> --action install|remove`

- [ ] **Step 1: 写诱饵 API Key 不落盘的失败测试**

```js
const original = { providers: { qwen: { api_key: 'qwen-secret-fixture' } }, custom_llms: { qwen: {} } };
const merged = mergeLocalCodexStrategy(original, 'local-token');
assert.equal(merged.providers.qwen.api_key, 'qwen-secret-fixture');
assert.equal(findWrittenFiles(tempDir).some((p) => readFileSync(p, 'utf8').includes('qwen-secret-fixture')), false);
```

- [ ] **Step 2: 运行测试确认现有完整备份行为失败**

Run: `node --test tests/game-byok-config.test.mjs tests/game-byok-privacy.test.mjs`

Expected: FAIL，现有 CLI 要求 `--backup` 并写完整配置。

- [ ] **Step 3: 实现 install/remove 原地合并**

安装只写 `local_codex` provider/strategy；五个角色的 `model` 使用各自角色名，桥接再映射到当前 Codex 模型。删除只移除 `local_codex`，不序列化或备份其他 provider。

- [ ] **Step 4: 运行测试并扫描临时目录**

Run: `node --test tests/game-byok-config.test.mjs tests/game-byok-privacy.test.mjs`

Expected: PASS，诱饵 key 只存在测试进程内存和测试源文件，不出现在输出目录。

- [ ] **Step 5: 提交**

```powershell
git add scripts/configure-game-byok.mjs tests/game-byok-*.test.mjs
git commit -m "feat: configure game without persisting BYOK secrets"
```

### Task 4: 实现确定性的全球地区 ASAR 转换

**Files:**
- Create: `scripts/patch-region.mjs`
- Create: `scripts/build-patched-asar.mjs`
- Modify: `scripts/patch-renderer.mjs`
- Modify: `ChongZhenAUProxyPatch/version_proxy.cpp`
- Modify: `ChongZhenAUProxyPatch/build.ps1`
- Modify: `ChongZhenAUProxyPatch/test-proxy-patch.ps1`
- Create: `tests/region-patch.test.mjs`
- Modify: `tests/renderer-patch.test.mjs`
- Create: `tests/fixtures/region-function.js`

**Interfaces:**
- Produces: `patchRegion(source: string): string`
- Produces: `patchRenderer(source: string, releaseBridgeToken: string): string`
- CLI 只在开发机读取已经备份的受支持原 ASAR，输出 `build/current-payload/app.asar`；输入和输出都不进入 Git

- [ ] **Step 1: 写地区语义和精确锚点失败测试**

```js
const patched = patchRegion(originalFunction);
assert.equal(runRegion(patched, undefined), 1);
assert.equal(runRegion(patched, 'CN'), 1);
for (const code of ['HK', 'MO', 'AU', 'US', 'DE']) assert.equal(runRegion(patched, code), 2);
assert.throws(() => patchRegion(originalFunction + originalFunction), /exactly once/);
```

- [ ] **Step 2: 运行测试确认失败**

Run: `node --test tests/region-patch.test.mjs tests/renderer-patch.test.mjs`

Expected: FAIL，`patchRegion` 尚不存在。

- [ ] **Step 3: 实现仅改变最终 Locked 返回值的转换**

转换必须匹配整个 `getIpRegionByCountry` 函数锚点并恰好出现一次，把最后的 `return 0` 改为 `return 2`；空值、CN 和原全球列表分支不变。构建时生成一个全新的发布专用本地 bridge token，同时写入补丁 ASAR 和发布 payload 配置；不得复用当前机器已安装 token。该 token 仅识别回环端口请求，不是 Codex/ChatGPT/API 登录凭据。

- [ ] **Step 4: 在原始备份副本上生成补丁并验证，不安装到游戏**

Run: `node scripts/build-patched-asar.mjs --input "$env:LOCALAPPDATA\ChongZhenCodexBridge\backups\app.asar.pre-codex" --output "build\current-payload\app.asar" --token-file "build\current-payload\bridge-token.txt"`

Expected: 输出 ASAR，通过内容测试；源备份 SHA-256 前后相同；新 token 与当前安装配置 token 不同。

随后把输出 ASAR header hash 和长度写入 `version_proxy.cpp` 的受支持载荷常量，运行当前 `build.ps1` 和 `test-proxy-patch.ps1`。Expected: proxy 测试 PASS，磁盘 EXE 哈希不变。

- [ ] **Step 5: 提交源码和小型 fixture**

```powershell
git add scripts/patch-region.mjs scripts/build-patched-asar.mjs scripts/patch-renderer.mjs ChongZhenAUProxyPatch/version_proxy.cpp ChongZhenAUProxyPatch/build.ps1 ChongZhenAUProxyPatch/test-proxy-patch.ps1 tests/region-patch.test.mjs tests/renderer-patch.test.mjs tests/fixtures/region-function.js
git commit -m "feat: patch non-CN regions to global service"
```

### Task 5: 引导 .NET 8 工具链并建立安装器骨架

**Files:**
- Create: `scripts/bootstrap-dotnet.ps1`
- Create: `global.json`
- Create: `installer/ChongZhenCodexInstaller.sln`
- Create: `installer/src/ChongZhenCodexInstaller/ChongZhenCodexInstaller.csproj`
- Create: `installer/src/ChongZhenCodexInstaller/Program.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Domain/InstallMode.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Domain/InstallState.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/ChongZhenCodexInstaller.Tests.csproj`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: `InstallMode.BridgeOnly`、`InstallMode.BridgeAndGlobal`
- Produces: `InstallState(GamePath, Mode, Version, Files, InstalledAt)`

- [ ] **Step 1: 下载官方 dotnet-install 脚本到临时目录并安装项目内 SDK**

Run: `powershell -NoProfile -File scripts/bootstrap-dotnet.ps1`

Expected: `.tools/dotnet/dotnet.exe --info` 显示 .NET 8 SDK；`.tools` 被 Git 忽略。

- [ ] **Step 2: 创建解决方案、WinForms 和 xUnit 项目**

Run: `.\.tools\dotnet\dotnet new sln -n ChongZhenCodexInstaller -o installer`

Run: `.\.tools\dotnet\dotnet new winforms -f net8.0 -n ChongZhenCodexInstaller -o installer/src/ChongZhenCodexInstaller`

Run: `.\.tools\dotnet\dotnet new xunit -f net8.0 -n ChongZhenCodexInstaller.Tests -o installer/tests/ChongZhenCodexInstaller.Tests`

- [ ] **Step 3: 写命令行解析失败测试**

```csharp
[Theory]
[InlineData("--watch", AppCommand.Watch)]
[InlineData("--status", AppCommand.Status)]
public void ParsesCommand(string arg, AppCommand expected) =>
    Assert.Equal(expected, CommandLine.Parse([arg]).Command);
```

- [ ] **Step 4: 实现最小 Program/Domain 并运行测试**

Run: `.\.tools\dotnet\dotnet test installer/ChongZhenCodexInstaller.sln`

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add global.json scripts/bootstrap-dotnet.ps1 installer
git commit -m "feat: scaffold Windows installer"
```

### Task 6: 实现 Steam 游戏与 Codex 自动定位

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Services/GameLocator.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/CodexLocator.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/VdfReader.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/GameLocatorTests.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/CodexLocatorTests.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/Fixtures/libraryfolders.vdf`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/Fixtures/appmanifest_4304230.acf`

**Interfaces:**
- Produces: `Task<IReadOnlyList<GameCandidate>> GameLocator.FindAsync(CancellationToken)`
- Produces: `string CodexLocator.Find(IReadOnlyDictionary<string,string?> env)`

- [ ] **Step 1: 写缓存、VDF、多库和全盘兜底测试**

```csharp
[Fact]
public async Task PrefersValidatedCachedPath() { /* fake filesystem, assert no drive scan */ }
[Fact]
public async Task ResolvesApp4304230AcrossSteamLibraries() { /* fixture VDF + manifest */ }
[Fact]
public async Task FallsBackToFixedDriveScan() { /* manifest missing, exe candidate valid */ }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "GameLocator|CodexLocator"`

Expected: FAIL，服务类型不存在。

- [ ] **Step 3: 实现定位器及验证规则**

候选必须包含 EXE、ASAR 和 `version.dll`。扫描捕获 UnauthorizedAccess/IO 异常，跳过系统目录。多个有效候选按缓存、Steam manifest、最后写入时间排序并返回 UI 选择。

- [ ] **Step 4: 运行定位测试**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "GameLocator|CodexLocator"`

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add installer/src installer/tests
git commit -m "feat: locate Steam game and local Codex"
```

### Task 7: 实现载荷、备份、原子安装和恢复状态机

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Services/PayloadService.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/InstallStateStore.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/PatchService.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/FileTransaction.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/PayloadServiceTests.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/PatchServiceTests.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/FileTransactionTests.cs`

**Interfaces:**
- Produces: `PayloadManifest LoadAndVerify(Stream payload)`
- Produces: `Task<PatchResult> InstallGlobalAsync(GameInstall, Payload, CancellationToken)`
- Produces: `Task RestoreAsync(GameInstall, InstallState, CancellationToken)`

- [ ] **Step 1: 写哈希不匹配、失败注入与幂等恢复测试**

```csharp
[Fact] public async Task NeverReplacesWhenPayloadHashFails() { }
[Theory]
[InlineData(FailurePoint.AfterBackup)]
[InlineData(FailurePoint.AfterAsarReplace)]
[InlineData(FailurePoint.AfterDllReplace)]
public async Task RestoresExactOriginalBytesOnFailure(FailurePoint point) { }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "Payload|Patch|FileTransaction"`

Expected: FAIL。

- [ ] **Step 3: 实现同卷临时文件与事务回滚**

先复制并 SHA-256 验证备份，再在目标卷写 `.codex-installing` 临时文件。只接受清单中的原始/补丁哈希；使用 `File.Move(temp, target, true)` 完成同卷替换。任何异常按逆序恢复，未知当前哈希不覆盖。

- [ ] **Step 4: 运行失败注入测试**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "Payload|Patch|FileTransaction"`

Expected: PASS，所有恢复字节与 fixture 原始哈希一致。

- [ ] **Step 5: 提交**

```powershell
git add installer/src installer/tests
git commit -m "feat: add transactional patch installation"
```

### Task 8: 安装桥接、注册自启动并监听游戏生命周期

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Services/BridgeInstallService.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/StartupService.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/WatcherService.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/ProcessService.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/BridgeInstallServiceTests.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/WatcherServiceTests.cs`

**Interfaces:**
- Produces: `Task InstallRuntimeAsync(InstallOptions, CancellationToken)`
- Produces: `void StartupService.Enable(string installedExe)` / `Disable()`
- Produces: `Task WatcherService.RunAsync(CancellationToken)`

- [ ] **Step 1: 写游戏出现后启动、退出六十秒关闭和崩溃重启测试**

```csharp
[Fact] public async Task StartsBridgeWhenGameAppearsWithinTwoSeconds() { }
[Fact] public async Task StopsBridgeSixtySecondsAfterGameExit() { }
[Fact] public async Task RestartsBridgeWhenItCrashesWhileGameRuns() { }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "BridgeInstall|Watcher"`

Expected: FAIL。

- [ ] **Step 3: 实现 LocalAppData 安装与 HKCU Run**

使用 Task 4 构建时新生成的发布专用回环桥接识别码，使预构建 ASAR 与本机桥接一致；它不是账号/API 登录凭据且不得复用开发电脑现有值。配置文件 ACL 仅当前用户。运行时只包含干净 bridge 源码、Node 和工具依赖。`--watch` 每两秒检测游戏，桥接关闭宽限为六十秒。

- [ ] **Step 4: 运行测试并检查注册表 fixture**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter "BridgeInstall|Watcher"`

Expected: PASS；测试使用抽象注册表，不写真实 HKCU。

- [ ] **Step 5: 提交**

```powershell
git add installer/src installer/tests
git commit -m "feat: auto-start bridge with the game"
```

### Task 9: 完成 WinForms 管理界面和操作编排

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/UI/MainForm.cs`
- Create: `installer/src/ChongZhenCodexInstaller/UI/MainForm.Designer.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/InstallerCoordinator.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Program.cs`
- Create: `installer/tests/ChongZhenCodexInstaller.Tests/InstallerCoordinatorTests.cs`

**Interfaces:**
- Produces: `Task<OperationResult> InstallAsync(InstallMode mode, CancellationToken)`
- Produces: `RepairAsync`、`RestoreAsync`、`UninstallAsync`、`GetStatusAsync`

- [ ] **Step 1: 写模式切换和未知版本保护测试**

```csharp
[Fact] public async Task BridgeOnlyNeverCallsPatchService() { }
[Fact] public async Task SwitchingFromGlobalRestoresBeforeBridgeOnly() { }
[Fact] public async Task UnsupportedGameVersionLeavesFilesUntouched() { }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `.\.tools\dotnet\dotnet test installer/...Tests.csproj --filter InstallerCoordinator`

Expected: FAIL。

- [ ] **Step 3: 实现协调器和中文 UI**

主界面展示游戏路径、Codex 路径/登录状态、当前模式、文件状态和四个操作按钮。所有长操作可取消并显示阶段进度；游戏运行时禁用补丁/恢复按钮。关闭窗口不影响已安装 watcher。

- [ ] **Step 4: 运行协调器和全部 .NET 测试**

Run: `.\.tools\dotnet\dotnet test installer/ChongZhenCodexInstaller.sln`

Expected: PASS。

- [ ] **Step 5: 提交**

```powershell
git add installer
git commit -m "feat: add installer management UI"
```

### Task 10: 构建干净载荷与单文件 EXE

**Files:**
- Create: `scripts/fetch-node.ps1`
- Create: `scripts/build-payload.ps1`
- Create: `scripts/build-release.ps1`
- Create: `scripts/stage-current-payload.ps1`
- Create: `installer/src/ChongZhenCodexInstaller/Resources/payload.manifest.json`
- Create: `tests/release-payload.test.ps1`
- Modify: `.gitignore`

**Interfaces:**
- `scripts/build-release.ps1 -Configuration Release -Output dist`
- Produces: `dist/ChongZhenCodexInstaller.exe`

- [ ] **Step 1: 写发布载荷失败测试**

测试解压临时 payload，断言包含 Node 24.13.1、bridge、当前验证 ASAR、proxy DLL、原 DLL 转发副本和全新发布 bridge token；断言不包含 `.codex`、`state.db`、日志、开发机路径、邮箱、Codex/ChatGPT token、现有 session/thread ID 或非空第三方 `api_key`。

- [ ] **Step 2: 运行测试确认失败**

Run: `powershell -NoProfile -File tests/release-payload.test.ps1`

Expected: FAIL，构建脚本和 payload 尚不存在。

- [ ] **Step 3: 实现可再现载荷构建**

`fetch-node.ps1` 从 Node 官方分发下载 `node-v24.13.1-win-x64.zip` 及 `SHASUMS256.txt`，验证 SHA-256 后缓存到 `.tools/cache`。`stage-current-payload.ps1` 只接受 Task 4 生成且通过测试的 ASAR/DLL，并确认原始输入哈希等于当前受支持版本。`build-payload.ps1` 从明确允许列表复制 bridge、Node 和当前验证载荷，生成相对路径/长度/SHA-256 清单。载荷目录和最终 EXE 始终被 Git 忽略。

`build-release.ps1` 使用：

```powershell
.\.tools\dotnet\dotnet publish installer/src/ChongZhenCodexInstaller/ChongZhenCodexInstaller.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false -o dist
```

- [ ] **Step 4: 构建并运行发布载荷测试**

Run: `powershell -NoProfile -File scripts/build-release.ps1 -Configuration Release -Output dist`

Run: `powershell -NoProfile -File tests/release-payload.test.ps1`

Expected: PASS；`dist` 仅一个 EXE。

- [ ] **Step 5: 提交构建系统，不提交 dist**

```powershell
git add scripts installer tests .gitignore
git commit -m "build: produce sanitized single-file installer"
```

### Task 11: 端到端验收、CI 和本地交付

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `tests/e2e-installer.ps1`
- Create: `docs/verification/2026-08-31-release-verification.md`
- Modify: `README.md`

**Interfaces:**
- CI 在 `windows-latest` 执行不依赖游戏载荷的 Node 测试、.NET 测试和公开树门禁
- 本地交付：`dist/ChongZhenCodexInstaller.exe` 和 SHA-256；不上传公开 GitHub

- [ ] **Step 1: 在临时 Steam 布局运行无破坏 E2E**

Run: `powershell -NoProfile -File tests/e2e-installer.ps1 -Installer dist/ChongZhenCodexInstaller.exe`

Expected: PASS，覆盖自动定位、两模式、失败回滚、修复、恢复、卸载和无系统 Node 环境。

- [ ] **Step 2: 运行全部验证命令**

```powershell
node --test bridge/test/*.test.js tests/*.test.mjs
.\.tools\dotnet\dotnet test installer/ChongZhenCodexInstaller.sln -c Release
powershell -NoProfile -File tests/public-tree.test.ps1
powershell -NoProfile -File tests/release-payload.test.ps1
powershell -NoProfile -File scripts/assert-public-tree.ps1 -Root . -Staged
```

Expected: 全部 PASS。

- [ ] **Step 3: 在当前机器执行只读基线和实际安装验收**

先记录游戏 EXE、原始备份和当前文件 SHA-256。仅在用户退出游戏后运行安装器，验证自动定位 D 盘、Steam 启动、两秒内 bridge 启动、模型跟随、地区表和六十秒关闭。最后恢复当前有效模式，并再次确认 EXE SHA-256 与基线完全相同。

- [ ] **Step 4: 写验证报告并配置 CI**

报告记录命令、退出码、测试数量、EXE SHA-256、隐私扫描结论和未覆盖限制，不记录账号、模型配置正文、prompt 或 token。

- [ ] **Step 5: 最终公开树扫描、提交并推送**

```powershell
git add .github README.md docs/verification tests/e2e-installer.ps1
powershell -NoProfile -File scripts/assert-public-tree.ps1 -Root . -Staged
git commit -m "ci: verify portable installer release"
git push origin main
```

- [ ] **Step 6: 生成本地交付哈希并确认未进入 Git**

```powershell
$hash = (Get-FileHash dist/ChongZhenCodexInstaller.exe -Algorithm SHA256).Hash
Set-Content dist/ChongZhenCodexInstaller.exe.sha256 "$hash  ChongZhenCodexInstaller.exe"
if (git status --short -- dist build) { throw 'Private payload or dist must not be tracked' }
```

- [ ] **Step 7: 验证公开仓库与本地交付**

Run: `gh repo view 666damn/ChongZhen-Codex --json visibility,url,defaultBranchRef`

Expected: `visibility` 为 `PUBLIC`，默认分支为 `main`。

Run: `Get-FileHash dist/ChongZhenCodexInstaller.exe -Algorithm SHA256; git ls-files dist build`

Expected: 哈希与 `.sha256` 一致，`git ls-files` 对私有载荷和 dist 无输出。
