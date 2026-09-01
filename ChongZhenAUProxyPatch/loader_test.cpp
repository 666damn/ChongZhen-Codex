#include <windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

#include "loader_core.h"

namespace fs = std::filesystem;

namespace {

void Require(bool condition, const char* message) {
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", message);
        std::exit(1);
    }
}

void WriteText(const fs::path& path, const char* text) {
    fs::create_directories(path.parent_path());
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output.write(text, static_cast<std::streamsize>(std::strlen(text)));
}

void WriteMinimalAsar(const fs::path& path, const char* tag) {
    const std::string json = std::string("{\"files\":{},\"tag\":\"") + tag + "\"}\0";
    const std::uint32_t json_size = static_cast<std::uint32_t>(json.size());
    const std::uint32_t header_size = 8 + json_size;
    std::vector<std::uint8_t> bytes(8 + header_size, 0);
    std::memcpy(bytes.data() + 4, &header_size, sizeof(header_size));
    std::memcpy(bytes.data() + 8 + 4, &json_size, sizeof(json_size));
    std::memcpy(bytes.data() + 8 + 8, json.data(), json.size());
    fs::create_directories(path.parent_path());
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    output.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
}

void WriteMetadata(const fs::path& path, const LoaderState& state, std::int32_t schema = 1) {
    fs::create_directories(path.parent_path());
    std::ofstream output(path, std::ios::binary | std::ios::trunc);
    const std::array<char, 8> magic{'C', 'Z', 'S', 'C', 'A', 'R', '1', '\0'};
    const std::int32_t flags = 0;
    const auto source_hash = HexToBytes(state.source_header_sha256);
    const auto sidecar_hash = HexToBytes(state.sidecar_header_sha256);
    const std::int32_t official_bytes = static_cast<std::int32_t>(state.official_asar_path.size() * sizeof(wchar_t));
    const std::int32_t sidecar_bytes = static_cast<std::int32_t>(state.sidecar_path.size() * sizeof(wchar_t));
    output.write(magic.data(), magic.size());
    output.write(reinterpret_cast<const char*>(&schema), sizeof(schema));
    output.write(reinterpret_cast<const char*>(&flags), sizeof(flags));
    output.write(reinterpret_cast<const char*>(&state.source_length), sizeof(state.source_length));
    output.write(reinterpret_cast<const char*>(&state.sidecar_length), sizeof(state.sidecar_length));
    output.write(reinterpret_cast<const char*>(source_hash.data()), source_hash.size());
    output.write(reinterpret_cast<const char*>(sidecar_hash.data()), sidecar_hash.size());
    output.write(reinterpret_cast<const char*>(&official_bytes), sizeof(official_bytes));
    output.write(reinterpret_cast<const char*>(state.official_asar_path.data()), official_bytes);
    output.write(reinterpret_cast<const char*>(&sidecar_bytes), sizeof(sidecar_bytes));
    output.write(reinterpret_cast<const char*>(state.sidecar_path.data()), sidecar_bytes);
}

std::string ReadViaCreateFile(const fs::path& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    Require(file != INVALID_HANDLE_VALUE, "CreateFileW failed");
    char buffer[256] = {};
    DWORD read = 0;
    Require(ReadFile(file, buffer, sizeof(buffer), &read, nullptr) == TRUE, "ReadFile failed");
    CloseHandle(file);
    return std::string(buffer, buffer + read);
}

}  // namespace

int wmain() {
    const std::string expected_hash(64, 'a');
    const std::string integrity_json =
        "[{\"file\":\"resources\\\\other.asar\",\"alg\":\"SHA256\",\"value\":\"" + std::string(64, 'b') +
        "\"},{ \"value\" : \"" + expected_hash + "\", \"alg\" : \"SHA256\", \"file\" : \"resources/app.asar\" }]";
    std::string extracted_hash;
    Require(ExtractExpectedAsarHashFromIntegrityJson(
        integrity_json.data(), integrity_json.size(), &extracted_hash), "integrity resource hash extraction failed");
    Require(extracted_hash == expected_hash, "wrong integrity resource hash extracted");
    const std::string wrong_algorithm =
        "[{\"file\":\"resources\\\\app.asar\",\"alg\":\"SHA1\",\"value\":\"" + expected_hash + "\"}]";
    Require(!ExtractExpectedAsarHashFromIntegrityJson(
        wrong_algorithm.data(), wrong_algorithm.size(), &extracted_hash), "non-SHA256 integrity entry was accepted");

    const auto root = fs::temp_directory_path() / (L"cz-loader-test-" + std::to_wstring(GetCurrentProcessId()));
    fs::remove_all(root);
    const auto game = root / L"game";
    const auto runtime = root / L"local" / L"ChongZhenCodexBridge";
    const auto official = game / L"resources" / L"app.asar";
    WriteMinimalAsar(official, "official");
    WriteText(game / L"resources" / L"app.asar.bak", "backup");
    WriteText(game / L"ChongZhenSimulator.exe", "fixture");

    LoaderState state{};
    state.official_asar_path = fs::weakly_canonical(official).wstring();
    Require(ComputeAsarHeaderSha256(official.c_str(), &state.source_header_sha256), "source ASAR hash failed");
    state.source_length = fs::file_size(official);
    const auto sidecar = runtime / L"sidecars" / std::wstring(state.source_header_sha256.begin(), state.source_header_sha256.end()) / L"app.asar";
    WriteMinimalAsar(sidecar, "sidecar");
    state.sidecar_path = fs::weakly_canonical(sidecar).wstring();
    Require(ComputeAsarHeaderSha256(sidecar.c_str(), &state.sidecar_header_sha256), "sidecar ASAR hash failed");
    state.sidecar_length = fs::file_size(sidecar);
    const auto metadata = sidecar.parent_path() / L"loader-metadata.bin";
    WriteMetadata(metadata, state);

    LoaderState loaded{};
    Require(TryLoadValidatedSidecar((game / L"ChongZhenSimulator.exe").c_str(), runtime.c_str(), &loaded), "valid metadata was rejected");
    Require(ShouldRedirectRead(official.c_str(), GENERIC_READ, OPEN_EXISTING, loaded), "exact read was not redirected");
    Require(!ShouldRedirectRead((game / L"resources" / L"app.asar.bak").c_str(), GENERIC_READ, OPEN_EXISTING, loaded), "suffix path was redirected");
    Require(!ShouldRedirectRead(official.c_str(), GENERIC_WRITE, OPEN_EXISTING, loaded), "write was redirected");
    Require(!ShouldRedirectRead(official.c_str(), GENERIC_READ, CREATE_ALWAYS, loaded), "create was redirected");

    WriteMetadata(metadata, state, 2);
    Require(!TryLoadValidatedSidecar((game / L"ChongZhenSimulator.exe").c_str(), runtime.c_str(), &loaded), "unknown schema was accepted");
    WriteMetadata(metadata, state);
    WriteMinimalAsar(official, "steam-update");
    Require(!TryLoadValidatedSidecar((game / L"ChongZhenSimulator.exe").c_str(), runtime.c_str(), &loaded), "stale source was accepted");
    WriteMinimalAsar(official, "official");

    void* entry_stub = VirtualAlloc(nullptr, 1, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    Require(entry_stub != nullptr, "entry stub allocation failed");
    *static_cast<unsigned char*>(entry_stub) = 0xc3;
    Require(ArmSidecarHooksAtAddressForTesting(state, entry_stub), "entry hook arming failed");
    reinterpret_cast<void (*)()>(entry_stub)();
    Require(ReadViaCreateFile(official).find("sidecar") != std::string::npos, "hook did not return sidecar bytes");
    Require(ReadViaCreateFile(game / L"resources" / L"app.asar.bak").empty() == false, "unrelated path test fixture missing");
    Require(VirtualFree(entry_stub, 0, MEM_RELEASE) == TRUE, "entry stub release failed");

    fs::remove_all(root);
    std::puts("PASS: loader validation, fail-open behavior, and exact read redirection succeeded.");
    return 0;
}
