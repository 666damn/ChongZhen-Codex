#include "loader_core.h"

#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <cctype>
#include <cwchar>
#include <filesystem>
#include <fstream>
#include <limits>
#include <vector>

namespace fs = std::filesystem;

namespace {

constexpr std::array<char, 8> kMetadataMagic{'C', 'Z', 'S', 'C', 'A', 'R', '1', '\0'};
constexpr std::int32_t kMetadataSchema = 1;
constexpr std::uint32_t kMaximumAsarHeader = 32U * 1024U * 1024U;

LoaderState g_loader_state;
decltype(&CreateFileW) g_original_create_file_w = nullptr;
void* g_entry_point = nullptr;
std::uint8_t g_original_entry_byte = 0;
PVOID g_entry_handler = nullptr;
volatile LONG g_entry_hook_armed = 0;
std::string g_expected_asar_hash;

std::string BytesToHex(const std::uint8_t* bytes, std::size_t size) {
    static constexpr char digits[] = "0123456789abcdef";
    std::string output(size * 2, '\0');
    for (std::size_t index = 0; index < size; ++index) {
        output[index * 2] = digits[bytes[index] >> 4];
        output[index * 2 + 1] = digits[bytes[index] & 0x0f];
    }
    return output;
}

bool IsLowerHexHash(const std::string& value) {
    return value.size() == 64 && std::all_of(value.begin(), value.end(), [](unsigned char value) {
        return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');
    });
}

bool Canonicalize(const wchar_t* path, std::wstring* output) {
    if (!path || !*path || !output) return false;
    const DWORD required = GetFullPathNameW(path, 0, nullptr, nullptr);
    if (required == 0 || required > 32767) return false;
    std::wstring buffer(required, L'\0');
    const DWORD written = GetFullPathNameW(path, required, buffer.data(), nullptr);
    if (written == 0 || written >= required) return false;
    buffer.resize(written);
    while (buffer.size() > 3 && (buffer.back() == L'\\' || buffer.back() == L'/')) buffer.pop_back();
    *output = std::move(buffer);
    return true;
}

bool EqualPath(const std::wstring& left, const std::wstring& right) {
    return _wcsicmp(left.c_str(), right.c_str()) == 0;
}

bool IsUnderRoot(const std::wstring& path, const std::wstring& root) {
    if (path.size() <= root.size()) return false;
    if (_wcsnicmp(path.c_str(), root.c_str(), root.size()) != 0) return false;
    return path[root.size()] == L'\\' || path[root.size()] == L'/';
}

template <typename T>
bool ReadValue(std::ifstream& input, T* value) {
    input.read(reinterpret_cast<char*>(value), sizeof(T));
    return input.good();
}

bool ReadWidePath(std::ifstream& input, std::wstring* output) {
    std::int32_t byte_count = 0;
    if (!ReadValue(input, &byte_count) || byte_count <= 0 || byte_count > 65534 || byte_count % 2 != 0) return false;
    std::wstring value(static_cast<std::size_t>(byte_count) / sizeof(wchar_t), L'\0');
    input.read(reinterpret_cast<char*>(value.data()), byte_count);
    if (!input.good() || value.find(L'\0') != std::wstring::npos) return false;
    return Canonicalize(value.c_str(), output);
}

bool ReadMetadata(const fs::path& path, LoaderState* output) {
    std::ifstream input(path, std::ios::binary);
    if (!input || !output) return false;
    std::array<char, 8> magic{};
    input.read(magic.data(), magic.size());
    std::int32_t schema = 0;
    std::int32_t flags = 0;
    if (!input.good() || magic != kMetadataMagic || !ReadValue(input, &schema) || !ReadValue(input, &flags) ||
        schema != kMetadataSchema || flags != 0 ||
        !ReadValue(input, &output->source_length) || !ReadValue(input, &output->sidecar_length) ||
        output->source_length == 0 || output->sidecar_length == 0) return false;
    std::array<std::uint8_t, 32> source_hash{};
    std::array<std::uint8_t, 32> sidecar_hash{};
    input.read(reinterpret_cast<char*>(source_hash.data()), source_hash.size());
    input.read(reinterpret_cast<char*>(sidecar_hash.data()), sidecar_hash.size());
    if (!input.good() || !ReadWidePath(input, &output->official_asar_path) || !ReadWidePath(input, &output->sidecar_path)) return false;
    if (input.peek() != std::ifstream::traits_type::eof()) return false;
    output->source_header_sha256 = BytesToHex(source_hash.data(), source_hash.size());
    output->sidecar_header_sha256 = BytesToHex(sidecar_hash.data(), sidecar_hash.size());
    return true;
}

bool FileLength(const wchar_t* path, std::uint64_t* output) {
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if (!GetFileAttributesExW(path, GetFileExInfoStandard, &attributes) ||
        (attributes.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0) return false;
    *output = (static_cast<std::uint64_t>(attributes.nFileSizeHigh) << 32U) | attributes.nFileSizeLow;
    return true;
}

bool HashBuffer(const std::uint8_t* data, std::size_t size, std::array<std::uint8_t, 32>* digest) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD object_length = 0;
    DWORD result_length = 0;
    std::vector<std::uint8_t> object;
    bool success = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) goto cleanup;
    if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&object_length),
            sizeof(object_length), &result_length, 0) < 0 || object_length == 0) goto cleanup;
    object.resize(object_length);
    if (BCryptCreateHash(algorithm, &hash, object.data(), object_length, nullptr, 0, 0) < 0) goto cleanup;
    if (size > (std::numeric_limits<ULONG>::max)() ||
        BCryptHashData(hash, const_cast<PUCHAR>(data), static_cast<ULONG>(size), 0) < 0) goto cleanup;
    if (BCryptFinishHash(hash, digest->data(), static_cast<ULONG>(digest->size()), 0) < 0) goto cleanup;
    success = true;
cleanup:
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    return success;
}

bool ReadExpectedAsarHashFromResource(std::string* output) {
    HMODULE image = GetModuleHandleW(nullptr);
    if (!image || !output) return false;
    HRSRC resource = FindResourceW(image, L"ELECTRONASAR", L"INTEGRITY");
    if (!resource) return false;
    const DWORD size = SizeofResource(image, resource);
    HGLOBAL loaded = LoadResource(image, resource);
    const auto* data = static_cast<const char*>(LockResource(loaded));
    return data && size > 0 && ExtractExpectedAsarHashFromIntegrityJson(data, size, output);
}

bool PatchIntegrityResourceHash(const std::string& from, const std::string& to) {
    if (!IsLowerHexHash(from) || !IsLowerHexHash(to)) return false;
    HMODULE image = GetModuleHandleW(nullptr);
    if (!image) return false;
    HRSRC resource = FindResourceW(image, L"ELECTRONASAR", L"INTEGRITY");
    if (!resource) return false;
    const DWORD size = SizeofResource(image, resource);
    HGLOBAL loaded = LoadResource(image, resource);
    auto* data = static_cast<std::uint8_t*>(LockResource(loaded));
    if (!data || size < from.size()) return false;
    std::uint8_t* match = nullptr;
    unsigned count = 0;
    for (std::size_t offset = 0; offset + from.size() <= size; ++offset) {
        if (std::memcmp(data + offset, from.data(), from.size()) == 0) {
            match = data + offset;
            ++count;
        }
    }
    if (count != 1 || !match) return false;
    DWORD old_protection = 0;
    if (!VirtualProtect(match, from.size(), PAGE_READWRITE, &old_protection)) return false;
    std::memcpy(match, to.data(), to.size());
    DWORD ignored = 0;
    const BOOL restored = VirtualProtect(match, from.size(), old_protection, &ignored);
    FlushInstructionCache(GetCurrentProcess(), match, from.size());
    return restored == TRUE;
}

bool PatchImport(const char* function_name, void* replacement, void** original) {
    auto* image = reinterpret_cast<std::uint8_t*>(GetModuleHandleW(nullptr));
    if (!image) return false;
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(image);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!directory.VirtualAddress) return false;
    auto* descriptor = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(image + directory.VirtualAddress);
    for (; descriptor->Name; ++descriptor) {
        const char* module_name = reinterpret_cast<const char*>(image + descriptor->Name);
        if (_stricmp(module_name, "KERNEL32.dll") != 0 && _stricmp(module_name, "KERNELBASE.dll") != 0) continue;
        auto* names = reinterpret_cast<IMAGE_THUNK_DATA64*>(image + descriptor->OriginalFirstThunk);
        auto* addresses = reinterpret_cast<IMAGE_THUNK_DATA64*>(image + descriptor->FirstThunk);
        if (!descriptor->OriginalFirstThunk) names = addresses;
        for (; names->u1.AddressOfData; ++names, ++addresses) {
            if (IMAGE_SNAP_BY_ORDINAL64(names->u1.Ordinal)) continue;
            auto* import = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(image + names->u1.AddressOfData);
            if (std::strcmp(reinterpret_cast<const char*>(import->Name), function_name) != 0) continue;
            DWORD old_protection = 0;
            if (!VirtualProtect(&addresses->u1.Function, sizeof(addresses->u1.Function), PAGE_READWRITE, &old_protection)) return false;
            *original = reinterpret_cast<void*>(addresses->u1.Function);
            addresses->u1.Function = reinterpret_cast<ULONGLONG>(replacement);
            DWORD ignored = 0;
            const BOOL restored = VirtualProtect(&addresses->u1.Function, sizeof(addresses->u1.Function), old_protection, &ignored);
            if (!restored) {
                addresses->u1.Function = reinterpret_cast<ULONGLONG>(*original);
                VirtualProtect(&addresses->u1.Function, sizeof(addresses->u1.Function), old_protection, &ignored);
            }
            FlushInstructionCache(GetCurrentProcess(), &addresses->u1.Function, sizeof(addresses->u1.Function));
            return restored == TRUE;
        }
    }
    return false;
}

HANDLE WINAPI RedirectedCreateFileW(
    LPCWSTR file_name,
    DWORD desired_access,
    DWORD share_mode,
    LPSECURITY_ATTRIBUTES security_attributes,
    DWORD creation_disposition,
    DWORD flags_and_attributes,
    HANDLE template_file) {
    const wchar_t* selected = file_name;
    if (ShouldRedirectRead(file_name, desired_access, creation_disposition, g_loader_state)) {
        selected = g_loader_state.sidecar_path.c_str();
    }
    return g_original_create_file_w(selected, desired_access, share_mode, security_attributes,
        creation_disposition, flags_and_attributes, template_file);
}

LONG CALLBACK EntryPointHookHandler(EXCEPTION_POINTERS* exception) {
    if (!exception || !exception->ExceptionRecord || !exception->ContextRecord ||
        exception->ExceptionRecord->ExceptionCode != EXCEPTION_BREAKPOINT ||
        exception->ExceptionRecord->ExceptionAddress != g_entry_point ||
        InterlockedCompareExchange(&g_entry_hook_armed, 0, 1) != 1) {
        return EXCEPTION_CONTINUE_SEARCH;
    }

    DWORD old_protection = 0;
    if (!VirtualProtect(g_entry_point, 1, PAGE_EXECUTE_READWRITE, &old_protection)) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    *static_cast<volatile std::uint8_t*>(g_entry_point) = g_original_entry_byte;
    DWORD ignored = 0;
    const BOOL restored = VirtualProtect(g_entry_point, 1, old_protection, &ignored);
    FlushInstructionCache(GetCurrentProcess(), g_entry_point, 1);
    if (!restored) return EXCEPTION_CONTINUE_SEARCH;

    if (!InstallSidecarHooks(g_loader_state)) {
        RestoreExpectedAsarHashInMemory(g_loader_state);
    }
#if defined(_M_X64)
    exception->ContextRecord->Rip = reinterpret_cast<DWORD64>(g_entry_point);
#else
#error The sidecar loader only supports x64 builds.
#endif
    return EXCEPTION_CONTINUE_EXECUTION;
}

bool ArmSidecarHooksAtAddress(const LoaderState& state, void* address) {
    if (!address || InterlockedCompareExchange(&g_entry_hook_armed, 1, 0) != 0) return false;
    g_loader_state = state;
    g_entry_point = address;
    g_original_entry_byte = *static_cast<const std::uint8_t*>(address);
    g_entry_handler = AddVectoredExceptionHandler(1, &EntryPointHookHandler);
    if (!g_entry_handler) {
        InterlockedExchange(&g_entry_hook_armed, 0);
        return false;
    }
    DWORD old_protection = 0;
    if (!VirtualProtect(address, 1, PAGE_EXECUTE_READWRITE, &old_protection)) {
        RemoveVectoredExceptionHandler(g_entry_handler);
        g_entry_handler = nullptr;
        InterlockedExchange(&g_entry_hook_armed, 0);
        return false;
    }
    *static_cast<volatile std::uint8_t*>(address) = 0xcc;
    DWORD ignored = 0;
    const BOOL restored = VirtualProtect(address, 1, old_protection, &ignored);
    FlushInstructionCache(GetCurrentProcess(), address, 1);
    if (!restored) {
        *static_cast<volatile std::uint8_t*>(address) = g_original_entry_byte;
        VirtualProtect(address, 1, old_protection, &ignored);
        RemoveVectoredExceptionHandler(g_entry_handler);
        g_entry_handler = nullptr;
        InterlockedExchange(&g_entry_hook_armed, 0);
        return false;
    }
    return true;
}

}  // namespace

std::array<std::uint8_t, 32> HexToBytes(const std::string& value) {
    std::array<std::uint8_t, 32> result{};
    if (!IsLowerHexHash(value)) return result;
    auto digit = [](char value) -> std::uint8_t {
        return value <= '9' ? static_cast<std::uint8_t>(value - '0') : static_cast<std::uint8_t>(value - 'a' + 10);
    };
    for (std::size_t index = 0; index < result.size(); ++index) {
        result[index] = static_cast<std::uint8_t>((digit(value[index * 2]) << 4U) | digit(value[index * 2 + 1]));
    }
    return result;
}

bool ExtractExpectedAsarHashFromIntegrityJson(
    const char* data,
    std::size_t size,
    std::string* output) noexcept {
    try {
        if (!data || size == 0 || !output) return false;
        const std::string json(data, size);
        auto read_string = [&json](std::size_t begin, std::size_t end, const char* key, std::string* value) {
            const std::string tag = std::string("\"") + key + "\"";
            std::size_t position = begin;
            while ((position = json.find(tag, position)) != std::string::npos && position < end) {
                position += tag.size();
                while (position < end && std::isspace(static_cast<unsigned char>(json[position]))) ++position;
                if (position >= end || json[position] != ':') continue;
                ++position;
                while (position < end && std::isspace(static_cast<unsigned char>(json[position]))) ++position;
                if (position >= end || json[position] != '"') continue;
                ++position;
                std::string decoded;
                while (position < end) {
                    const char current = json[position++];
                    if (current == '"') {
                        *value = std::move(decoded);
                        return true;
                    }
                    if (current == '\\') {
                        if (position >= end) return false;
                        const char escaped = json[position++];
                        if (escaped != '\\' && escaped != '"' && escaped != '/') return false;
                        decoded.push_back(escaped);
                    } else {
                        decoded.push_back(current);
                    }
                }
                return false;
            }
            return false;
        };
        std::size_t search = 0;
        std::string match;
        unsigned count = 0;
        while ((search = json.find('{', search)) != std::string::npos) {
            const std::size_t object_end = json.find('}', search + 1);
            if (object_end == std::string::npos) return false;
            std::string file;
            std::string algorithm;
            std::string candidate;
            if (read_string(search + 1, object_end, "file", &file) &&
                read_string(search + 1, object_end, "alg", &algorithm) &&
                read_string(search + 1, object_end, "value", &candidate)) {
                std::replace(file.begin(), file.end(), '/', '\\');
                if (_stricmp(file.c_str(), "resources\\app.asar") == 0 &&
                    algorithm == "SHA256" && IsLowerHexHash(candidate)) {
                    match = std::move(candidate);
                    ++count;
                }
            }
            search = object_end + 1;
        }
        if (count != 1) return false;
        *output = std::move(match);
        return true;
    } catch (...) {
        return false;
    }
}

bool ComputeAsarHeaderSha256(const wchar_t* path, std::string* output) noexcept {
    try {
        std::ifstream input(fs::path(path), std::ios::binary);
        if (!input || !output) return false;
        std::array<std::uint8_t, 8> prefix{};
        input.read(reinterpret_cast<char*>(prefix.data()), prefix.size());
        if (!input.good()) return false;
        std::uint32_t header_size = 0;
        std::memcpy(&header_size, prefix.data() + 4, sizeof(header_size));
        input.seekg(0, std::ios::end);
        const auto file_size = input.tellg();
        if (header_size <= 8 || header_size > kMaximumAsarHeader || file_size < static_cast<std::streamoff>(8 + header_size)) return false;
        input.seekg(8, std::ios::beg);
        std::vector<std::uint8_t> header(header_size);
        input.read(reinterpret_cast<char*>(header.data()), header.size());
        if (!input.good()) return false;
        std::uint32_t json_size = 0;
        std::memcpy(&json_size, header.data() + 4, sizeof(json_size));
        if (json_size == 0 || json_size > header_size - 8) return false;
        std::array<std::uint8_t, 32> digest{};
        if (!HashBuffer(header.data() + 8, json_size, &digest)) return false;
        *output = BytesToHex(digest.data(), digest.size());
        return true;
    } catch (...) {
        return false;
    }
}

bool TryLoadValidatedSidecar(
    const wchar_t* game_executable_path,
    const wchar_t* runtime_root,
    LoaderState* output) noexcept {
    try {
        if (!game_executable_path || !runtime_root || !output) return false;
        std::wstring game_executable;
        std::wstring runtime;
        if (!Canonicalize(game_executable_path, &game_executable) || !Canonicalize(runtime_root, &runtime)) return false;
        const fs::path official_path = fs::path(game_executable).parent_path() / L"resources" / L"app.asar";
        std::wstring official;
        if (!Canonicalize(official_path.c_str(), &official)) return false;
        std::string source_hash;
        std::uint64_t source_length = 0;
        if (!ComputeAsarHeaderSha256(official.c_str(), &source_hash) || !FileLength(official.c_str(), &source_length)) return false;
        const fs::path expected_root = fs::path(runtime) / L"sidecars" / std::wstring(source_hash.begin(), source_hash.end());
        LoaderState candidate{};
        if (!ReadMetadata(expected_root / L"loader-metadata.bin", &candidate)) return false;
        std::wstring expected_sidecar;
        std::wstring sidecars_root;
        if (!Canonicalize((expected_root / L"app.asar").c_str(), &expected_sidecar) ||
            !Canonicalize((fs::path(runtime) / L"sidecars").c_str(), &sidecars_root)) return false;
        if (!EqualPath(candidate.official_asar_path, official) || !EqualPath(candidate.sidecar_path, expected_sidecar) ||
            !IsUnderRoot(candidate.sidecar_path, sidecars_root) || candidate.source_length != source_length ||
            candidate.source_header_sha256 != source_hash) return false;
        std::uint64_t sidecar_length = 0;
        std::string sidecar_hash;
        if (!FileLength(candidate.sidecar_path.c_str(), &sidecar_length) || sidecar_length != candidate.sidecar_length ||
            !ComputeAsarHeaderSha256(candidate.sidecar_path.c_str(), &sidecar_hash) || sidecar_hash != candidate.sidecar_header_sha256) return false;
        *output = std::move(candidate);
        return true;
    } catch (...) {
        return false;
    }
}

bool ShouldRedirectRead(
    const wchar_t* requested_path,
    DWORD desired_access,
    DWORD creation_disposition,
    const LoaderState& state) noexcept {
    try {
        constexpr DWORD write_access = GENERIC_WRITE | DELETE | WRITE_DAC | WRITE_OWNER;
        if ((desired_access & write_access) != 0 || creation_disposition != OPEN_EXISTING) return false;
        std::wstring requested;
        if (!Canonicalize(requested_path, &requested)) return false;
        return EqualPath(requested, state.official_asar_path);
    } catch (...) {
        return false;
    }
}

bool PatchExpectedAsarHashInMemory(const LoaderState& state) noexcept {
    try {
        std::string expected;
        if (!ReadExpectedAsarHashFromResource(&expected)) return false;
        g_expected_asar_hash = expected;
        return PatchIntegrityResourceHash(expected, state.sidecar_header_sha256);
    } catch (...) {
        return false;
    }
}

bool RestoreExpectedAsarHashInMemory(const LoaderState& state) noexcept {
    try {
        return IsLowerHexHash(g_expected_asar_hash) &&
            PatchIntegrityResourceHash(state.sidecar_header_sha256, g_expected_asar_hash);
    } catch (...) {
        return false;
    }
}

bool InstallSidecarHooks(const LoaderState& state) noexcept {
    try {
        if (g_original_create_file_w) return false;
        g_loader_state = state;
        void* original = nullptr;
        if (!PatchImport("CreateFileW", reinterpret_cast<void*>(&RedirectedCreateFileW), &original)) return false;
        g_original_create_file_w = reinterpret_cast<decltype(&CreateFileW)>(original);
        return g_original_create_file_w != nullptr;
    } catch (...) {
        return false;
    }
}

bool ArmSidecarHooksAtProcessEntry(const LoaderState& state) noexcept {
    try {
        auto* image = reinterpret_cast<std::uint8_t*>(GetModuleHandleW(nullptr));
        if (!image) return false;
        auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(image);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
        auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(image + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.AddressOfEntryPoint == 0) return false;
        return ArmSidecarHooksAtAddress(state, image + nt->OptionalHeader.AddressOfEntryPoint);
    } catch (...) {
        return false;
    }
}

bool ArmSidecarHooksAtAddressForTesting(const LoaderState& state, void* address) noexcept {
    try {
        return ArmSidecarHooksAtAddress(state, address);
    } catch (...) {
        return false;
    }
}
