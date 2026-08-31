#include <windows.h>
#include <cstdint>
#include <cstring>

namespace {

constexpr char kOriginalEmbeddedHash[] =
    "dbaaf692e2391d3b13c635f4e63d4159a8b52bce41ed2beb5cc78749949742c1";
constexpr char kPatchedAsarHeaderHash[] =
    "083b4f00ede7f8bab0f1558c3ab2fec05221a63e915ea9414e51aeb63a863abe";
constexpr std::uint64_t kPatchedAsarSize = 469836514ULL;
constexpr char kCodexAsarHeaderHash[] =
    "ee85b1ae1fecc337e575015993e76e603f8ae291043ca9e9e3ab3a73c4a06ef4";
constexpr std::uint64_t kCodexAsarSize = 469836834ULL;

const char* GetInstalledPatchedAsarHash() {
    wchar_t executablePath[MAX_PATH] = {};
    const DWORD length = GetModuleFileNameW(nullptr, executablePath, MAX_PATH);
    if (length == 0 || length >= MAX_PATH) return nullptr;

    wchar_t* slash = wcsrchr(executablePath, L'\\');
    if (!slash) return nullptr;
    *(slash + 1) = L'\0';

    constexpr wchar_t relativePath[] = L"resources\\app.asar";
    if (wcslen(executablePath) + wcslen(relativePath) >= MAX_PATH) return nullptr;
    wcscat_s(executablePath, relativePath);

    WIN32_FILE_ATTRIBUTE_DATA attributes = {};
    if (!GetFileAttributesExW(executablePath, GetFileExInfoStandard, &attributes)) return nullptr;

    const std::uint64_t size =
        (static_cast<std::uint64_t>(attributes.nFileSizeHigh) << 32) |
        attributes.nFileSizeLow;
    if (size == kPatchedAsarSize) return kPatchedAsarHeaderHash;
    if (size == kCodexAsarSize) return kCodexAsarHeaderHash;
    return nullptr;
}

bool PatchIntegrityHashInMemory() {
    const char* installedHash = GetInstalledPatchedAsarHash();
    if (!installedHash) return true;

    auto* imageBase = reinterpret_cast<std::uint8_t*>(GetModuleHandleW(nullptr));
    if (!imageBase) return false;

    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(imageBase);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(imageBase + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;

    IMAGE_SECTION_HEADER* resourceSection = nullptr;
    auto* sections = IMAGE_FIRST_SECTION(nt);
    for (WORD index = 0; index < nt->FileHeader.NumberOfSections; ++index) {
        char name[IMAGE_SIZEOF_SHORT_NAME + 1] = {};
        std::memcpy(name, sections[index].Name, IMAGE_SIZEOF_SHORT_NAME);
        if (std::strcmp(name, ".rsrc") == 0) {
            resourceSection = &sections[index];
            break;
        }
    }
    if (!resourceSection) return false;

    auto* start = imageBase + resourceSection->VirtualAddress;
    const std::size_t size = resourceSection->Misc.VirtualSize;
    constexpr std::size_t hashLength = sizeof(kOriginalEmbeddedHash) - 1;

    std::uint8_t* match = nullptr;
    unsigned int matchCount = 0;
    for (std::size_t offset = 0; offset + hashLength <= size; ++offset) {
        if (std::memcmp(start + offset, kOriginalEmbeddedHash, hashLength) == 0) {
            match = start + offset;
            ++matchCount;
        }
    }
    if (matchCount != 1 || !match) return false;

    DWORD oldProtection = 0;
    if (!VirtualProtect(match, hashLength, PAGE_READWRITE, &oldProtection)) return false;
    std::memcpy(match, installedHash, hashLength);
    DWORD ignored = 0;
    const BOOL restored = VirtualProtect(match, hashLength, oldProtection, &ignored);
    FlushInstructionCache(GetCurrentProcess(), match, hashLength);
    return restored == TRUE;
}

}  // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        if (!PatchIntegrityHashInMemory()) {
            OutputDebugStringA("ChongZhen AU proxy: integrity hash patch failed.\n");
        }
    }
    return TRUE;
}
