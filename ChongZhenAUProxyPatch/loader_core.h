#pragma once

#include <windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>

struct LoaderState {
    std::wstring official_asar_path;
    std::wstring sidecar_path;
    std::string source_header_sha256;
    std::string sidecar_header_sha256;
    std::uint64_t source_length = 0;
    std::uint64_t sidecar_length = 0;
};

std::array<std::uint8_t, 32> HexToBytes(const std::string& value);
bool ExtractExpectedAsarHashFromIntegrityJson(
    const char* data,
    std::size_t size,
    std::string* output) noexcept;
bool ComputeAsarHeaderSha256(const wchar_t* path, std::string* output) noexcept;
bool TryLoadValidatedSidecar(
    const wchar_t* game_executable_path,
    const wchar_t* runtime_root,
    LoaderState* output) noexcept;
bool ShouldRedirectRead(
    const wchar_t* requested_path,
    DWORD desired_access,
    DWORD creation_disposition,
    const LoaderState& state) noexcept;
bool PatchExpectedAsarHashInMemory(const LoaderState& state) noexcept;
bool RestoreExpectedAsarHashInMemory(const LoaderState& state) noexcept;
bool InstallSidecarHooks(const LoaderState& state) noexcept;
bool ArmSidecarHooksAtProcessEntry(const LoaderState& state) noexcept;
bool ArmSidecarHooksAtAddressForTesting(const LoaderState& state, void* address) noexcept;
