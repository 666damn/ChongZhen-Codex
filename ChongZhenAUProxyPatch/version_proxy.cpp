#include <windows.h>
#include "loader_core.h"

namespace {

bool InitializeSidecarLoader() {
    wchar_t executable_path[32768]{};
    const DWORD executable_length = GetModuleFileNameW(nullptr, executable_path, static_cast<DWORD>(std::size(executable_path)));
    if (executable_length == 0 || executable_length >= std::size(executable_path)) return false;
    wchar_t local_app_data[32768]{};
    const DWORD local_length = GetEnvironmentVariableW(L"LOCALAPPDATA", local_app_data, static_cast<DWORD>(std::size(local_app_data)));
    if (local_length == 0 || local_length >= std::size(local_app_data)) return false;
    if (wcscat_s(local_app_data, L"\\ChongZhenCodexBridge") != 0) return false;
    LoaderState state{};
    if (!TryLoadValidatedSidecar(executable_path, local_app_data, &state)) return false;
    if (!PatchExpectedAsarHashInMemory(state)) return false;
    if (InstallSidecarHooks(state)) return true;
    LoaderState rollback = state;
    std::swap(rollback.source_header_sha256, rollback.sidecar_header_sha256);
    PatchExpectedAsarHashInMemory(rollback);
    return false;
}

}  // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        if (!InitializeSidecarLoader()) {
            OutputDebugStringA("ChongZhen Codex loader: using the official ASAR.\n");
        }
    }
    return TRUE;
}
