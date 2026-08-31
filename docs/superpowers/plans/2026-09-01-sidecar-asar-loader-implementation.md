# Sidecar ASAR Loader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce and publish a smaller installer that reads the discovered Steam ASAR, builds a LocalAppData sidecar, adds a fail-open loader, and never modifies a Steam-owned file.

**Architecture:** The installer packages Node, pinned Electron ASAR tooling, bridge source, and a loader DLL but no game content. A C# sidecar service invokes a Node patcher into a temporary directory, verifies the source and output, atomically publishes versioned metadata, and installs only absent or known-project loader files. The loader validates source/sidecar fingerprints and redirects only read-only opens of the exact official ASAR path; stale or incompatible state falls back to the official file.

**Tech Stack:** .NET 8 WinForms/C#, Node.js 24.13.1 ESM, `@electron/asar` 4.3.x, C++17/Win32 x64, PowerShell release tooling, xUnit, Node test runner, GitHub Actions and Releases.

**Spec:** `docs/superpowers/specs/2026-09-01-sidecar-asar-loader-design.md`

## Global Constraints

- Never modify `ChongZhenSimulator.exe` or the official `resources/app.asar`.
- Add loader files only when absent or when the existing hash is a known project-owned legacy hash; never overwrite an unknown file.
- Store generated sidecars only under `%LOCALAPPDATA%\ChongZhenCodexBridge\sidecars\<source-header-sha256>`.
- Release payload must not contain any ASAR, build-time token, Codex credential/session/configuration, save, log, user path, private address, or captured model.
- Generate a cryptographic random bridge token on the target computer and keep it ACL-restricted to the current user.
- Bind the bridge only to loopback and follow the target Codex CLI's current login and model at runtime.
- Any stale, damaged, unsupported, or incompatible sidecar state must fall back to the official game.
- Final Windows x64 EXE must be below 300 MiB and at least 50 percent smaller than v1.0.0.
- Real-machine acceptance must use normal Steam Play and prove official EXE/ASAR hashes are unchanged.

---

### Task 1: Target-machine secret and payload contract

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Services/BridgeSecretStore.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/BridgeInstallService.cs`
- Modify: `scripts/stage-current-payload.ps1`
- Modify: `tests/release-payload.test.ps1`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/BridgeSecretStoreTests.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/BridgeInstallServiceTests.cs`

**Interfaces:**
- Produces: `BridgeSecretStore.GetOrCreateAsync(CancellationToken) -> Task<string>` returning `czb_` plus 64 lowercase hex characters.
- Produces: payload without `bridge/bridge-id.txt` or `global/app.asar`.
- Consumes: existing current-user ACL logic and `VerifiedPayload`.

- [ ] **Step 1: Write failing secret and payload tests**

Add tests proving two reads return the same target-generated token, its file is under the runtime root, malformed existing data is rejected, and `BridgeInstallService` no longer reads a payload token. Change the PowerShell payload expectation to reject every `*.asar` and `bridge-id.txt` entry.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release --filter "FullyQualifiedName~BridgeSecretStoreTests|FullyQualifiedName~BridgeInstallServiceTests"
```

Expected: compilation/test failure because `BridgeSecretStore` does not exist and the runtime still requires `bridge/bridge-id.txt`.

- [ ] **Step 3: Implement the target secret and remove release token staging**

Implement `BridgeSecretStore` with `RandomNumberGenerator.Fill`, atomic write, strict token validation, and a current-user-only ACL. Inject it into `BridgeInstallService`; write `config.json` from `GetOrCreateAsync`. Remove `build/release-token.txt`, `bridge/bridge-id.txt`, `build/app.asar`, and `build/baseline.asar` from staging requirements.

- [ ] **Step 4: Run focused and full installer tests**

Run the focused command, then:

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release
```

Expected: all tests pass with zero failures.

- [ ] **Step 5: Commit**

```powershell
git add installer scripts/stage-current-payload.ps1 tests/release-payload.test.ps1
git commit -m "feat: generate bridge secret on target"
```

### Task 2: Portable install-time ASAR patcher

**Files:**
- Create: `patcher/package.json`
- Create: `patcher/package-lock.json`
- Create: `patcher/src/build-sidecar.mjs`
- Create: `patcher/src/verify-sidecar.mjs`
- Modify: `scripts/patch-region.mjs`
- Modify: `scripts/patch-renderer.mjs`
- Modify: `scripts/verify-asar.mjs`
- Modify: `scripts/verify-asar-delta.mjs`
- Test: `tests/sidecar-builder.test.mjs`
- Test: `tests/fixtures/sidecar-renderer.js`

**Interfaces:**
- Produces CLI: `node patcher/src/build-sidecar.mjs --input <official.asar> --output <temporary.asar> --token <czb_...>`.
- Produces JSON on stdout: `{ sourceHeaderSha256, sourceLength, sidecarHeaderSha256, sidecarLength }`.
- Guarantees source is opened read-only, output is outside the source directory, patches occur exactly once, and delta verification passes.

- [ ] **Step 1: Write a synthetic ASAR test that proves the input is unchanged**

Create a synthetic Electron ASAR containing the exact renderer anchors and an unrelated binary file. Assert source SHA-256 before/after is identical, output contains both patches, output has no unexpected delta, and an incompatible source fails without an output file.

- [ ] **Step 2: Run and verify RED**

```powershell
node --test --test-concurrency=1 tests/sidecar-builder.test.mjs
```

Expected: module-not-found for `patcher/src/build-sidecar.mjs`.

- [ ] **Step 3: Implement the patcher with pinned runtime dependencies**

Move the reusable build orchestration into `patcher/src/build-sidecar.mjs`, import patch transforms from `scripts`, use `@electron/asar` 4.3.x, and emit only verified JSON. Keep tests and CLI free of real game bytes.

- [ ] **Step 4: Run patcher and existing patch tests**

```powershell
node --test --test-concurrency=1 tests/sidecar-builder.test.mjs tests/region-patch.test.mjs tests/renderer-patch.test.mjs tests/asar-content.test.mjs tests/asar-delta.test.mjs
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add patcher scripts tests
git commit -m "feat: build verified ASAR sidecars at install time"
```

### Task 3: Atomic sidecar generation and metadata publication

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Domain/SidecarMetadata.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/ISidecarPatcher.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/NodeSidecarPatcher.cs`
- Create: `installer/src/ChongZhenCodexInstaller/Services/SidecarService.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/BridgeInstallService.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/SidecarServiceTests.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/NodeSidecarPatcherTests.cs`

**Interfaces:**
- `ISidecarPatcher.BuildAsync(string source, string output, string token, CancellationToken) -> Task<SidecarBuildResult>`.
- `SidecarService.EnsureCurrentAsync(GameInstall game, VerifiedPayload payload, string token, CancellationToken) -> Task<SidecarMetadata>`.
- `SidecarMetadata` contains schema version, canonical official path, source header hash/length, canonical sidecar path, sidecar header hash/length.

- [ ] **Step 1: Write failing service tests**

Test successful atomic publish, source mutation between build and publish, patcher failure, bad output hash, cancellation, path escape, and compatible sidecar reuse. Every failure asserts official EXE/ASAR hashes and contents are unchanged and no final metadata exists.

- [ ] **Step 2: Run and verify RED**

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release --filter "FullyQualifiedName~SidecarServiceTests|FullyQualifiedName~NodeSidecarPatcherTests"
```

Expected: compilation failure because the interfaces do not exist.

- [ ] **Step 3: Implement sidecar service and process runner**

Pass Node arguments through `ProcessStartInfo.ArgumentList` with `UseShellExecute=false`; capture bounded stdout/stderr; enforce exit code, JSON schema, hash, length, allowed root, and source recheck. Publish `app.asar` then a fixed-schema `loader-metadata.bin` with flush and atomic rename; always delete temporary directories.

- [ ] **Step 4: Run focused and full installer tests**

Run the focused command and then the full installer solution. Expected: zero failures.

- [ ] **Step 5: Commit**

```powershell
git add installer
git commit -m "feat: publish versioned ASAR sidecars atomically"
```

### Task 4: Fail-open Win32 sidecar loader

**Files:**
- Create: `ChongZhenAUProxyPatch/loader_core.h`
- Create: `ChongZhenAUProxyPatch/loader_core.cpp`
- Create: `ChongZhenAUProxyPatch/loader_metadata.h`
- Create: `ChongZhenAUProxyPatch/loader_test.cpp`
- Create: `ChongZhenAUProxyPatch/loader_integration_host.cpp`
- Modify: `ChongZhenAUProxyPatch/version_proxy.cpp`
- Modify: `ChongZhenAUProxyPatch/build.ps1`
- Modify: `ChongZhenAUProxyPatch/test-proxy-patch.ps1`
- Modify: `ChongZhenAUProxyPatch/test-proxy-exports.ps1`

**Interfaces:**
- `bool TryLoadValidatedSidecar(LoaderState*) noexcept` validates fixed-schema metadata and ASAR header hashes.
- `bool ShouldRedirectRead(const wchar_t* requested, DWORD access, DWORD disposition, const LoaderState&) noexcept` returns true only for the exact canonical official ASAR and read-only existing-file opens.
- Proxy exports the same 17 Version APIs and falls back to original behavior on every validation/hook error.

- [ ] **Step 1: Write native unit and integration tests first**

Cover case-insensitive canonical path equality, prefix/suffix attacks, relative paths, writes, create/truncate dispositions, stale source, damaged sidecar, path escape, unknown schema, and valid read redirection. The integration host imports the proxy, opens an official fixture path, and proves the returned bytes come from the sidecar only in the valid case.

- [ ] **Step 2: Build tests and verify RED**

```powershell
& .\ChongZhenAUProxyPatch\build.ps1 -TestOnly
```

Expected: compilation failure because loader core and `-TestOnly` do not exist.

- [ ] **Step 3: Implement fixed-schema metadata validation and IAT hooks**

Use fixed-size UTF-16 paths and lowercase hexadecimal SHA-256 fields; resolve LocalAppData with `SHGetKnownFolderPath`; restrict sidecar paths to the exact runtime root; hash only the ASAR header using the same byte range as `scripts/asar-header-hash.mjs`; verify lengths; patch the executable resource hash exactly once; hook `CreateFileW` and required attribute calls for the main process. Avoid shell execution and arbitrary DLL loading.

- [ ] **Step 4: Run native tests and export audit**

```powershell
& .\ChongZhenAUProxyPatch\build.ps1 -TestOnly
& .\ChongZhenAUProxyPatch\test-proxy-exports.ps1 -ProxyPath .\ChongZhenAUProxyPatch\build\version.dll
& .\ChongZhenAUProxyPatch\test-proxy-patch.ps1 -ProxyPath .\ChongZhenAUProxyPatch\build\version.dll
```

Expected: unit, integration, 17-export, and fail-open tests pass.

- [ ] **Step 5: Commit**

```powershell
git add ChongZhenAUProxyPatch
git commit -m "feat: redirect verified ASAR reads to sidecars"
```

### Task 5: Non-overwriting loader lifecycle and legacy migration

**Files:**
- Create: `installer/src/ChongZhenCodexInstaller/Services/LoaderInstallService.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Domain/PayloadManifest.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/PatchService.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/InstallStateStore.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/InstallerBackend.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/InstallerCoordinator.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/LoaderInstallServiceTests.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/PatchServiceTests.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/InstallerCoordinatorTests.cs`

**Interfaces:**
- `LoaderInstallService.InstallAsync(GameInstall, VerifiedPayload, CancellationToken) -> Task<LoaderInstallState>`.
- `LoaderInstallService.UninstallAsync(GameInstall, LoaderInstallState, CancellationToken)` removes only matching project-owned additions.
- `PatchService` becomes orchestration for legacy restore, sidecar generation, and loader add/remove; it never replaces official EXE/ASAR.

- [ ] **Step 1: Write failing lifecycle tests**

Test absent targets, known legacy hashes, unknown existing files, rollback after first added file, uninstall with matching files, uninstall after user modification, and migration from v1.0.0 backup state. Assert official EXE/ASAR hashes before and after every case.

- [ ] **Step 2: Run and verify RED**

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release --filter "FullyQualifiedName~LoaderInstallServiceTests|FullyQualifiedName~PatchServiceTests|FullyQualifiedName~InstallerCoordinatorTests"
```

Expected: failures because global install still replaces `global/app.asar`.

- [ ] **Step 3: Implement non-overwriting lifecycle**

Remove `global/app.asar` from the payload manifest and patch transaction. Restore a known v1.0.0 replacement first, verify official hashes, generate the sidecar, then add loader files. Record `Existed=false` additions and installed hashes; refuse unknown collisions; uninstall only matching additions.

- [ ] **Step 4: Run all installer tests**

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add installer
git commit -m "feat: install sidecar loader without overwriting game files"
```

### Task 6: Watcher refresh and safe game-update fallback

**Files:**
- Modify: `installer/src/ChongZhenCodexInstaller/Services/WatcherService.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/ProcessService.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/InstallerRuntime.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Program.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/CommandLine.cs`
- Modify: `installer/src/ChongZhenCodexInstaller/Domain/AppCommand.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/WatcherServiceTests.cs`
- Test: `installer/tests/ChongZhenCodexInstaller.Tests/SmokeTests.cs`

**Interfaces:**
- Add `AppCommand.RefreshSidecar` mapped from `--refresh-sidecar`.
- Extend `IProcessService.RefreshSidecarAsync(CancellationToken)`.
- Watcher refreshes only while the game is stopped, serializes refresh attempts, and keeps bridge start/stop timing unchanged.

- [ ] **Step 1: Write failing update tests**

Test initial refresh, unchanged fingerprint skip, changed compatible source refresh, incompatible source status without retry storm, no refresh while game runs, and existing 2-second bridge start/60-second stop behavior.

- [ ] **Step 2: Run and verify RED**

```powershell
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release --filter "FullyQualifiedName~WatcherServiceTests|FullyQualifiedName~SmokeTests"
```

Expected: missing refresh command/interface failures.

- [ ] **Step 3: Implement refresh command and watcher scheduling**

The installed EXE loads coordinator state, verifies global mode, and calls the same `SidecarService`; it writes a compatibility status under LocalAppData. Watcher invokes it without a shell only when no game process exists and suppresses repeated attempts for the same incompatible source fingerprint.

- [ ] **Step 4: Run focused and full tests**

Run the focused command and full solution. Expected: zero failures and unchanged timing assertions.

- [ ] **Step 5: Commit**

```powershell
git add installer
git commit -m "feat: refresh sidecars safely after game updates"
```

### Task 7: Compressed, ASAR-free release and privacy gates

**Files:**
- Modify: `scripts/build-payload.ps1`
- Modify: `scripts/build-release.ps1`
- Modify: `scripts/stage-current-payload.ps1`
- Modify: `scripts/assert-release-clean.ps1`
- Modify: `tests/release-payload.test.ps1`
- Modify: `installer/src/ChongZhenCodexInstaller/Services/ReleasePayloadAudit.cs`
- Modify: `.github/workflows/ci.yml`
- Test: `tests/public-tree.test.ps1`

**Interfaces:**
- Release staging takes no game-file input.
- ZIP uses `CompressionLevel.Optimal`; publish sets `EnableCompressionInSingleFile=true` while remaining self-contained win-x64.
- `--audit-payload` rejects any ASAR, release token, unallowlisted file, local identity/path/network/model/credential pattern, or unexpected native binary.

- [ ] **Step 1: Strengthen tests before changing the build**

Require the payload to contain patcher sources/dependencies and loader files, reject `*.asar` regardless of path, reject `release-token.txt`/`bridge-id.txt`, assert no build root strings, and assert final EXE size below 300 MiB.

- [ ] **Step 2: Run release tests and verify RED**

```powershell
& .\scripts\build-release.ps1
& .\tests\release-payload.test.ps1
```

Expected: the strengthened test fails because v1 staging still contains `global/app.asar` and a release token.

- [ ] **Step 3: Implement ASAR-free compressed packaging**

Stage clean pinned patcher dependencies, use optimal ZIP compression, enable single-file compression, and make allowlists exact. Ensure the build no longer reads `build/app.asar`, `build/baseline.asar`, game directories, Codex directories, or user runtime state.

- [ ] **Step 4: Build and audit the final candidate**

```powershell
& .\scripts\build-release.ps1
& .\tests\release-payload.test.ps1
& .\scripts\assert-release-clean.ps1 -Installer .\dist\ChongZhenCodexInstaller.exe
```

Expected: one EXE, all audits pass, size below 300 MiB.

- [ ] **Step 5: Commit**

```powershell
git add scripts tests installer/src/ChongZhenCodexInstaller/Services/ReleasePayloadAudit.cs .github/workflows/ci.yml
git commit -m "build: publish compressed ASAR-free installer"
```

### Task 8: Full regression and documentation

**Files:**
- Modify: `README.md`
- Create: `docs/verification/2026-09-01-sidecar-release-verification.md`
- Modify: `docs/superpowers/plans/2026-09-01-sidecar-asar-loader-implementation.md`

**Interfaces:**
- Documentation states zero official-file modification, update fallback behavior, target-generated secret, supported modes, prerequisites, hashes, size, and exact verification evidence.

- [ ] **Step 1: Run every automated suite**

```powershell
npm ci --prefix bridge
npm ci --prefix patcher
node --test --test-concurrency=1 bridge/test/*.test.js tests/game-byok-config.test.mjs tests/game-byok-privacy.test.mjs tests/region-patch.test.mjs tests/renderer-patch.test.mjs tests/sidecar-builder.test.mjs tests/asar-content.test.mjs tests/asar-delta.test.mjs
& .\.tools\dotnet\dotnet.exe test installer\ChongZhenCodexInstaller.sln -c Release
& .\tests\public-tree.test.ps1
& .\tests\release-payload.test.ps1
& .\scripts\assert-release-clean.ps1 -Installer .\dist\ChongZhenCodexInstaller.exe
```

Expected: all suites and privacy/release gates pass.

- [ ] **Step 2: Update README and verification record**

Record exact test counts, final size/SHA-256, payload entry count, absence of ASAR/private data, and the pending real-machine checks. Do not include usernames, private paths, tokens, Codex thread/session IDs, saves, or model names.

- [ ] **Step 3: Commit**

```powershell
git add README.md docs
git commit -m "docs: document sidecar installer verification"
```

### Task 9: Real-machine acceptance, main merge, and GitHub Release

**Files:**
- Modify: `docs/verification/2026-09-01-sidecar-release-verification.md`
- Produce locally: `dist/ChongZhenCodexInstaller.exe`
- Copy locally: `C:\Users\admin\Desktop\历史模拟器：崇祯Codex版.exe`

**Interfaces:**
- GitHub `main` receives the reviewed commits.
- A new non-draft Release receives the verified Windows x64 installer.

- [ ] **Step 1: Capture baseline and migrate v1 safely**

Stop the game, bridge, and installed watcher as needed. Use recorded v1 state to restore official files. Record EXE/ASAR hashes and prove they match the validated baseline before installing the sidecar candidate.

- [ ] **Step 2: Install candidate and verify zero official-file changes**

Install each supported mode. Re-hash official EXE/ASAR after install and require exact equality. Inspect the loader state and prove only known project additions and LocalAppData sidecars were created.

- [ ] **Step 3: Launch through Steam and exercise a real request**

Use the Steam Play action, confirm the window is responsive, confirm sidecar redirection is active, confirm non-CN routing, and submit a real in-game request through the currently logged-in Codex CLI. Record HTTP success without recording prompt content, token, account, session ID, or model.

- [ ] **Step 4: Verify automatic shutdown, fallback, uninstall, and reinstall**

Close the game and require bridge shutdown after the configured idle window. Exercise controlled stale/incompatible metadata and prove official ASAR fallback. Uninstall and prove official hashes remain unchanged and additions are removed; reinstall and launch again.

- [ ] **Step 5: Final release audit and commit evidence**

Run all release audits again against the exact candidate, update the verification document with sanitized evidence, commit, and push the feature branch.

- [ ] **Step 6: Merge to main and verify CI**

Merge the verified branch into `main`, push, and wait for the GitHub Actions run for the exact main SHA to complete successfully.

- [ ] **Step 7: Publish Release EXE and verify remote metadata**

Create a new non-draft version tag, upload the exact candidate, and verify GitHub asset name, state, size, digest, and download URL against the local SHA-256. Copy the same bytes to the single desktop EXE path.

- [ ] **Step 8: Mark the goal complete only after the requirement audit passes**

Audit every spec completion criterion against current files, test output, real-machine behavior, main SHA, CI run, and Release asset metadata. Keep the goal active if any evidence is missing.
