# Sidecar ASAR Loader Design

## Goal

Build a smaller portable installer that never embeds game content and never
modifies a Steam-owned game file. The installer reads the discovered
`resources/app.asar` only as an input, generates a versioned patched sidecar in
`%LOCALAPPDATA%\ChongZhenCodexBridge`, and adds only loader files owned by this
project. Normal Steam launch and normal game operation must continue to work.

The Release must contain no game ASAR, Codex credentials, Codex sessions, saved
games, local model selection, private server configuration, logs, or build-machine
paths.

## User-Approved Boundary

Adding new loader files is allowed when it does not interfere with Steam launch or
game operation. Overwriting a Steam-owned file is not allowed. The game executable
and the official `resources/app.asar` must remain byte-for-byte unchanged.

## Current State

Version 1.0.0 embeds a complete 448 MiB patched ASAR and transactionally replaces
the game's `resources/app.asar`. Its proxy `version.dll` changes the ASAR integrity
hash in process memory but does not redirect file reads. This release is about 690
MiB and does not satisfy the new zero-modification requirement for official game
files.

The existing installer already discovers the game directory, validates hashes,
installs the bridge, starts a per-user watcher, records installation state, and
checks that `ChongZhenSimulator.exe` remains unchanged.

## Chosen Architecture

### Read-only source and versioned sidecar

The official file remains at:

`<game>\resources\app.asar`

The patch generator reads it without write sharing and produces:

`%LOCALAPPDATA%\ChongZhenCodexBridge\sidecars\<source-header-sha256>\app.asar`

The sidecar directory also contains a loader metadata file with the source ASAR
header hash, source length, sidecar header hash, sidecar length, and schema version.
Metadata is written atomically only after all patch verification succeeds.

No file is created beside the official ASAR. Electron continues to believe it is
opening the official path, so existing `app.asar.unpacked` path semantics remain
unchanged.

### Added loader

The game executable already imports the Windows Version API early enough for the
currently tested proxy to run before ASAR verification. The installer may add these
project-owned files to the game root only when their target names are absent or
contain a hash from the public list of legacy project builds:

- `version.dll`: loader and Version API proxy;
- `version_original.dll`: verified Windows System32 Version API implementation.

An unknown existing file is never overwritten. A known legacy project file may be
migrated after its hash is verified. Added files are recorded as project-owned and
are removed on uninstall only when their current hashes still match the installed
state.

The loader performs two operations before the Electron entry point runs:

1. It validates the metadata, current official ASAR header/length, and sidecar
   header/length. It then patches the executable's in-memory expected integrity
   hash from the validated source hash to the validated sidecar hash.
2. It hooks the process file-open path used for the exact canonical official
   `resources\app.asar` path and substitutes the sidecar file handle. All other
   paths and all writes pass through unchanged.

The redirect hook must compare canonical case-insensitive paths and redirect only
read-only opens. It must never redirect a write, delete, rename, directory scan, or
an unrelated file with the same basename.

If metadata, version, path, hash, hook installation, or sidecar verification fails,
the loader installs no redirect and makes no integrity change. The process then
loads the untouched official ASAR normally.

### Update behavior

The installed watcher checks the official ASAR header fingerprint while the game
is not running. When Steam installs a new version:

- the old sidecar no longer matches the source fingerprint and cannot be loaded;
- the original game remains launchable because loader validation fails closed to
  the official path;
- the watcher tries to generate and verify a new versioned sidecar;
- if the public patch anchors remain compatible, the new sidecar becomes active;
- if the anchors changed, the watcher records an actionable compatibility status
  and leaves the original game active until project patch rules are updated.

The loader never uses an old sidecar with a new official ASAR. Automatic support
for an arbitrary future game build is not claimed; safe original-game fallback is
required for every future build.

### Codex configuration

The installer generates a cryptographic random bridge token on the target machine.
The token is stored only in the installed runtime directory and is injected into
that installation's sidecar. No build-time or shared release token remains.

The public loopback endpoint `http://127.0.0.1:43129/v1` remains a program constant.
It is not a captured server setting. The bridge binds only to loopback and follows
the target computer's current Codex CLI login and model selection at runtime. No
concrete model identifier is stored in the Release.

## Install and Runtime Flow

1. Discover the Steam game and Codex CLI.
2. Verify that the game is not running.
3. Hash the game EXE and official ASAR header; open the ASAR read-only.
4. Generate or load the target-machine bridge token.
5. Generate a sidecar in a unique temporary directory using bundled Node and the
   pinned Electron ASAR package.
6. Verify ASAR structure, renderer patch, country routing patch, and the complete
   source-to-output delta.
7. Recheck the official EXE and ASAR fingerprints to detect concurrent updates.
8. Atomically publish the sidecar and metadata under the source hash directory.
9. Add the loader files only after absence/known-legacy validation.
10. Start the watcher and record only project-owned file hashes and paths.
11. Launch normally through Steam for acceptance testing.

Patch generation failure occurs before adding or changing any game-directory file.
Cancellation and failure delete temporary outputs.

## Release Payload and Size

The payload contains only the self-contained installer, Node runtime, bridge source,
patcher source, pinned `@electron/asar` runtime dependencies, loader DLL, and public
compatibility metadata. It explicitly excludes `app.asar`, `baseline.asar`, any
other game content, and any local runtime state.

Payload ZIP compression and .NET single-file compression reduce download size only.
They do not compress, rewrite, or replace a game file. The installer remains
self-contained for Windows x64.

The final EXE must be below 300 MiB and at least 50 percent smaller than v1.0.0.

## Privacy and Supply-Chain Rules

The build fails unless the embedded payload matches a strict allowlist. It rejects:

- game content or game saves;
- `.codex`, `auth.json`, session, thread, history, log, state, or cache files;
- OpenAI/ChatGPT/API keys and credential-shaped values;
- a build user profile, computer name, LAN address, or private server address;
- a captured model setting;
- a build-time bridge token;
- unpinned patcher dependencies or unexpected native binaries.

The final EXE self-audit and an independent binary privacy scan are release gates.

## Restore and Uninstall

The official EXE and ASAR require no restoration because they are never changed.
Uninstall removes project-owned loader files only when their hashes match recorded
installed hashes, removes sidecars and runtime state under the exact validated
LocalAppData root, and leaves unknown files untouched.

If a known legacy release previously replaced official files, migration first uses
the existing verified backup state to restore those originals. Only after their
hashes are confirmed does it install the sidecar loader architecture.

## Automated Tests

- release staging contains no ASAR and no build-time token;
- payload allowlist and final privacy audit reject all prohibited local data;
- patch generation reads a discovered source and writes only to a temporary or
  LocalAppData sidecar path;
- patch output and delta verification are mandatory before metadata publication;
- source changes during generation invalidate the result;
- loader canonical-path matching redirects only the exact read-only ASAR open;
- loader metadata validation rejects stale source, damaged sidecar, unknown schema,
  bad hash, bad length, and a path outside the sidecar root;
- every rejection path falls back to the official ASAR;
- unknown existing loader filenames are never overwritten;
- known legacy migration restores official hashes first;
- install, reinstall, and uninstall never change the game EXE or official ASAR;
- watcher detects a synthetic version change, rebuilds a compatible sidecar, and
  reports an incompatible version without damaging the source;
- bridge tests prove current target Codex login/model discovery is still dynamic.

## Real-Machine Acceptance

On the installed Windows x64 Steam game:

1. restore the current v1.0.0 replacement patch and record official EXE/ASAR hashes;
2. install the new sidecar build and prove those hashes remain identical;
3. launch from the normal Steam Play action and prove the game window responds;
4. prove the loader used the validated sidecar without changing the official ASAR;
5. prove all non-CN routing and a real Codex-backed game request succeed;
6. prove the watcher starts the bridge automatically and stops it after the idle
   window when the game exits;
7. replace the source fingerprint with an incompatible fixture in a controlled
   test and prove loader fallback selects the original ASAR;
8. uninstall and prove only project-owned additions and LocalAppData runtime are
   removed;
9. reinstall to prove repeatability;
10. repeat the final EXE payload/privacy/size audit.

## Completion Criteria

- no Steam-owned file is modified;
- normal Steam launch and game operation pass;
- current compatible game builds use a verified LocalAppData sidecar;
- stale or incompatible builds always fall back to the official game;
- Release contains no game ASAR or private local data;
- EXE meets the size threshold;
- all automated and real-machine acceptance checks pass;
- source and documentation are pushed to the public GitHub repository;
- the verified EXE is attached to a new non-draft GitHub Release.
