// ==WindhawkMod==
// @id              jarvis-taskbar-icon-size
// @name            JARVIS2 Native Taskbar Icon Size
// @description     Narrow, fail-closed experiment that changes the native Windows 11 taskbar icon-size calculation
// @version         0.2.0
// @author          JARVIS2 contributors; based on work by m417z
// @license         GPL-3.0
// @include         %SystemRoot%\explorer.exe
// @architecture    amd64
// @compilerOptions -DWINVER=0x0A00 -D_WIN32_WINNT=0x0A00 -Wl,--no-insert-timestamp -lversion -lbcrypt -ladvapi32 -lshell32 -lole32
// ==/WindhawkMod==

// Copyright (C) 2026 JARVIS2 contributors.
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, version 3 of the License.
//
// This narrow implementation is derived from the GPL-3.0 Taskbar height and
// icon size Windhawk mod by m417z, version 1.3.7, pinned at commit
// 5d70208acc5a1f46d1c28439cb21c13f1079ec1d.
// Canonical LF source SHA-256:
// F8FC11864877B1AD8DD975D4514E28608AA60E5A4924EFBAB363ACD54FEBBB57
// Windows CRLF source SHA-256:
// FF080F8962E12D777C92A704C1BC462302D4514D8A54E79D912B34257B7DE692
//
// JARVIS2 intentionally keeps only the modern
// TaskbarConfiguration::GetIconHeightInViewPixels() path. It does not change
// taskbar height or button width, hook the tray/search modules, scan opcodes,
// write object offsets, patch constants, create windows, or restart Explorer.

// ==WindhawkModReadme==
/*
# JARVIS2 Native Taskbar Icon Size

This is an intentionally narrow native-shell experiment. It intercepts one
private `Taskbar.View.dll` calculation and changes the icon size returned for
normal taskbar icons. It does not draw a replacement taskbar or overlay.

Safety is the default:

- `Enabled` defaults to `false`.
- `IconSize` defaults to Windows' stock value, `24`.
- Values from `20-32` are accepted; missing or invalid values fall back to the
  stock-safe value `24`. The small-icon path is left untouched.
- The exact Windows build, Explorer image, loaded Taskbar.View path, product
  versions, file sizes, SHA-256 hashes, mapped PE identities, and CodeView PDB
  GUID/age values must match the audited profile.
- Initialization takes `Local\JARVIS2.StateGate.v1` only with a zero-timeout
  wait. If the supervisor owns the gate, the module fails closed immediately
  instead of delaying Explorer startup.
- `%LOCALAPPDATA%\JARVIS2\disabled.flag` blocks initialization before symbol
  resolution. A background directory watcher latches an already loaded module
  into pass-through mode; the hook itself performs no file-system I/O.
- `%LOCALAPPDATA%\JARVIS2\Recovery\m2-recovery-terminal.json` must also retain
  a fresh heartbeat. The watcher polls it once per second and latches
  pass-through if its last-write time becomes older than six seconds. The
  Recovery child directory is outside the non-recursive state-root file-name
  notifications, so normal heartbeats cannot quiesce their own module.
- Every load also requires and atomically consumes
  `%LOCALAPPDATA%\JARVIS2\active-module.txt`. Its entire contents must be the
  exact ASCII module ID (no BOM and no trailing newline):

  ```text
  jarvis-taskbar-icon-size
  ```

  The permit is a one-shot safety interlock, not an authentication secret. It
  is accepted only for five minutes after its last-write time; future-dated or
  expired permits are rejected without being consumed.
- A quiesced module never reactivates from a settings change. Reloading it must
  repeat every gate.

Changing the emergency flag while the module is already running signals the
directory watcher, which atomically latches the hook into pass-through mode. It
cannot physically unload a DLL. Disable the module in Windhawk and use the
separately confirmed recovery procedure if a complete unload or Explorer
restart is required.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- Enabled: false
  $name: Enable the experimental native icon-size hook
  $description: >-
    Off by default. Enabling still requires every in-process compatibility gate,
    a clear global emergency switch, and a valid one-shot module permit.
- IconSize: 24
  $name: Normal taskbar icon size
  $description: >-
    Stock is 24. Values from 20-32 are accepted; zero, missing, or invalid values
    fall back to 24. Small taskbar icons, taskbar height, button width, tray,
    search, badges, and overlays are not modified by this milestone.
*/
// ==/WindhawkModSettings==

#include <windhawk_utils.h>

#include <bcrypt.h>
#include <initguid.h>
#include <knownfolders.h>
#include <shlobj.h>
#include <winternl.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace {

constexpr DWORD kValidatedBuild = 26200;
constexpr DWORD kValidatedUbr = 8875;
constexpr wchar_t kValidatedInstallationType[] = L"Client";
constexpr wchar_t kModuleId[] = L"jarvis-taskbar-icon-size";
constexpr wchar_t kStateGateName[] = L"Local\\JARVIS2.StateGate.v1";

constexpr wchar_t kStateDirectorySuffix[] = L"\\JARVIS2";
constexpr wchar_t kKillSwitchSuffix[] = L"\\disabled.flag";
constexpr wchar_t kActivationPermitSuffix[] = L"\\active-module.txt";
constexpr wchar_t kRecoveryLeaseSuffix[] =
    L"\\Recovery\\m2-recovery-terminal.json";
constexpr char kActivationPermitPayload[] = "jarvis-taskbar-icon-size";
constexpr ULONGLONG kFileTimeTicksPerSecond = 10'000'000;
constexpr ULONGLONG kActivationPermitMaxAgeTicks =
    5 * 60 * kFileTimeTicksPerSecond;
constexpr ULONGLONG kRecoveryLeaseMaxAgeTicks =
    6 * kFileTimeTicksPerSecond;
constexpr ULONGLONG kRecoveryLeaseFutureSkewTicks =
    2 * kFileTimeTicksPerSecond;
constexpr DWORD kRecoveryLeasePollIntervalMs = 1000;

constexpr wchar_t kTaskbarViewRelativePath[] =
    L"SystemApps\\MicrosoftWindows.Client.Core_cw5n1h2txyewy"
    L"\\Taskbar.View.dll";

constexpr wchar_t kExplorerSha256[] =
    L"80B21E6F70524EFD84037A4EDA479DDC4BC55C0D6C1A33439B85A554E740F30C";
constexpr wchar_t kTaskbarViewSha256[] =
    L"00D1BD68240ED0CDB19A98E551BC5BFBA383843CC2564FF40523CB2DCFCD09F5";
constexpr std::uint64_t kExplorerSize = 3385624;
constexpr std::uint64_t kTaskbarViewSize = 10020864;

// Audited from the on-disk PE headers on 2026-07-22. These identities are
// checked directly against the images mapped in this Explorer process before
// any symbol hook is registered.
constexpr DWORD kExplorerImageTimeDateStamp = 0x0BEBF481;
constexpr DWORD kExplorerImageSize = 0x00337000;
constexpr DWORD kTaskbarViewImageTimeDateStamp = 0x6A29C5C5;
constexpr DWORD kTaskbarViewImageSize = 0x00996000;
constexpr GUID kExplorerPdbGuid{
    0xEE2147B7,
    0x59D5,
    0xA1FC,
    {0x28, 0x83, 0x04, 0x09, 0x18, 0x69, 0x50, 0x2A}};
constexpr GUID kTaskbarViewPdbGuid{
    0x4A99B7C5,
    0xBAD9,
    0x4999,
    {0x95, 0x75, 0x72, 0xCC, 0x66, 0xE1, 0x19, 0x78}};
constexpr DWORD kPdbAge = 1;

struct FileVersion {
    WORD major;
    WORD minor;
    WORD build;
    WORD revision;
};

constexpr FileVersion kExplorerVersion{10, 0, 26100, 8875};
constexpr FileVersion kTaskbarViewVersion{2605, 22000, 400, 0};

enum class ModuleState : LONG {
    kBlocked,
    kVerified,
    kActive,
    kQuiesced,
    kUnloading,
};

std::atomic<ModuleState> g_state{ModuleState::kBlocked};
std::atomic<int> g_iconSize{24};
std::atomic<bool> g_runtimeBlocked{true};

std::array<wchar_t, 32768> g_stateDirectoryPath{};
std::array<wchar_t, 32768> g_killSwitchPath{};
std::array<wchar_t, 32768> g_activationPermitPath{};
std::array<wchar_t, 32768> g_recoveryLeasePath{};

HANDLE g_watcherStopEvent = nullptr;
HANDLE g_watcherChangeNotification = INVALID_HANDLE_VALUE;
HANDLE g_watcherThread = nullptr;
HMODULE g_taskbarViewModuleReference = nullptr;

using GetIconHeightInViewPixels_t = double(WINAPI*)(void* pThis);
GetIconHeightInViewPixels_t GetIconHeightInViewPixels_Original;

class UniqueHandle {
   public:
    UniqueHandle() = default;

    explicit UniqueHandle(HANDLE handle) : handle_(handle) {}

    ~UniqueHandle() {
        reset();
    }

    UniqueHandle(const UniqueHandle&) = delete;
    UniqueHandle& operator=(const UniqueHandle&) = delete;

    HANDLE get() const {
        return handle_;
    }

    explicit operator bool() const {
        return handle_ && handle_ != INVALID_HANDLE_VALUE;
    }

    void reset(HANDLE handle = INVALID_HANDLE_VALUE) {
        if (*this) {
            CloseHandle(handle_);
        }
        handle_ = handle;
    }

   private:
    HANDLE handle_ = INVALID_HANDLE_VALUE;
};

class StateGateLease {
   public:
    StateGateLease() = default;

    ~StateGateLease() {
        release();
    }

    StateGateLease(const StateGateLease&) = delete;
    StateGateLease& operator=(const StateGateLease&) = delete;

    bool tryAcquire() {
        if (handle_) {
            return false;
        }

        HANDLE candidate = CreateSemaphoreW(nullptr, 1, 1, kStateGateName);
        if (!candidate) {
            Wh_Log(L"Can't open the JARVIS2 state gate (error=%lu)",
                   GetLastError());
            return false;
        }

        // The supervisor can hold this gate while waiting for a replacement
        // Explorer process. Blocking here would deadlock Explorer startup, so
        // a busy gate is always an immediate fail-closed result.
        DWORD waitResult = WaitForSingleObject(candidate, 0);
        if (waitResult != WAIT_OBJECT_0) {
            DWORD error =
                waitResult == WAIT_FAILED ? GetLastError() : ERROR_BUSY;
            Wh_Log(L"Can't acquire the JARVIS2 state gate immediately "
                   L"(wait=%lu error=%lu)",
                   waitResult, error);
            CloseHandle(candidate);
            return false;
        }

        handle_ = candidate;
        return true;
    }

    bool release() {
        if (!handle_) {
            return true;
        }

        HANDLE handle = handle_;
        handle_ = nullptr;
        BOOL released = ReleaseSemaphore(handle, 1, nullptr);
        DWORD error = released ? ERROR_SUCCESS : GetLastError();
        CloseHandle(handle);
        if (!released) {
            Wh_Log(L"Can't release the JARVIS2 state gate (error=%lu)",
                   error);
            return false;
        }

        return true;
    }

   private:
    HANDLE handle_ = nullptr;
};

class UniqueBcryptAlgorithm {
   public:
    UniqueBcryptAlgorithm() = default;

    ~UniqueBcryptAlgorithm() {
        if (handle_) {
            BCryptCloseAlgorithmProvider(handle_, 0);
        }
    }

    UniqueBcryptAlgorithm(const UniqueBcryptAlgorithm&) = delete;
    UniqueBcryptAlgorithm& operator=(const UniqueBcryptAlgorithm&) = delete;

    BCRYPT_ALG_HANDLE* put() {
        return &handle_;
    }

    BCRYPT_ALG_HANDLE get() const {
        return handle_;
    }

   private:
    BCRYPT_ALG_HANDLE handle_ = nullptr;
};

class UniqueBcryptHash {
   public:
    UniqueBcryptHash() = default;

    ~UniqueBcryptHash() {
        if (handle_) {
            BCryptDestroyHash(handle_);
        }
    }

    UniqueBcryptHash(const UniqueBcryptHash&) = delete;
    UniqueBcryptHash& operator=(const UniqueBcryptHash&) = delete;

    BCRYPT_HASH_HANDLE* put() {
        return &handle_;
    }

    BCRYPT_HASH_HANDLE get() const {
        return handle_;
    }

   private:
    BCRYPT_HASH_HANDLE handle_ = nullptr;
};

class HeapBuffer {
   public:
    explicit HeapBuffer(SIZE_T size)
        : data_(HeapAlloc(GetProcessHeap(), 0, size)) {}

    ~HeapBuffer() {
        if (data_) {
            HeapFree(GetProcessHeap(), 0, data_);
        }
    }

    HeapBuffer(const HeapBuffer&) = delete;
    HeapBuffer& operator=(const HeapBuffer&) = delete;

    void* get() const {
        return data_;
    }

    explicit operator bool() const {
        return data_ != nullptr;
    }

   private:
    void* data_ = nullptr;
};

class UniqueModuleReference {
   public:
    UniqueModuleReference() = default;

    explicit UniqueModuleReference(HMODULE module) : module_(module) {}

    ~UniqueModuleReference() {
        reset();
    }

    UniqueModuleReference(const UniqueModuleReference&) = delete;
    UniqueModuleReference& operator=(const UniqueModuleReference&) = delete;

    HMODULE get() const {
        return module_;
    }

    HMODULE release() {
        HMODULE module = module_;
        module_ = nullptr;
        return module;
    }

    void reset(HMODULE module = nullptr) {
        if (module_) {
            FreeLibrary(module_);
        }
        module_ = module;
    }

   private:
    HMODULE module_ = nullptr;
};

int HexNibble(wchar_t value) {
    if (value >= L'0' && value <= L'9') {
        return value - L'0';
    }
    if (value >= L'A' && value <= L'F') {
        return value - L'A' + 10;
    }
    if (value >= L'a' && value <= L'f') {
        return value - L'a' + 10;
    }
    return -1;
}

bool DigestMatchesHex(const std::array<UCHAR, 32>& digest,
                      PCWSTR expectedHex) {
    if (!expectedHex || wcslen(expectedHex) != digest.size() * 2) {
        return false;
    }

    for (size_t i = 0; i < digest.size(); i++) {
        int high = HexNibble(expectedHex[i * 2]);
        int low = HexNibble(expectedHex[i * 2 + 1]);
        if (high < 0 || low < 0 ||
            digest[i] != static_cast<UCHAR>((high << 4) | low)) {
            return false;
        }
    }

    return true;
}

bool FileSha256Matches(PCWSTR path, PCWSTR expectedHex) {
    UniqueBcryptAlgorithm algorithm;
    bool matches = false;

    do {
        if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(
                algorithm.put(), BCRYPT_SHA256_ALGORITHM, nullptr, 0))) {
            break;
        }

        ULONG objectLength = 0;
        ULONG bytesWritten = 0;
        if (!BCRYPT_SUCCESS(BCryptGetProperty(
                algorithm.get(), BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength),
                &bytesWritten, 0)) ||
            bytesWritten != sizeof(objectLength) || objectLength == 0) {
            break;
        }

        HeapBuffer hashObject(objectLength);
        if (!hashObject) {
            break;
        }

        // The BCrypt hash object borrows hashObject's storage. Declare the
        // handle second so it is destroyed before that storage on every exit.
        UniqueBcryptHash hash;
        if (!BCRYPT_SUCCESS(BCryptCreateHash(
                algorithm.get(), hash.put(),
                static_cast<PUCHAR>(hashObject.get()), objectLength, nullptr, 0,
                0))) {
            break;
        }

        UniqueHandle file(CreateFileW(path, GENERIC_READ,
                                      FILE_SHARE_READ | FILE_SHARE_WRITE |
                                          FILE_SHARE_DELETE,
                                      nullptr, OPEN_EXISTING,
                                      FILE_FLAG_SEQUENTIAL_SCAN, nullptr));
        if (!file) {
            break;
        }

        constexpr DWORD kReadBufferSize = 128 * 1024;
        HeapBuffer buffer(kReadBufferSize);
        if (!buffer) {
            break;
        }

        for (;;) {
            DWORD bytesRead = 0;
            if (!ReadFile(file.get(), buffer.get(), kReadBufferSize, &bytesRead,
                          nullptr)) {
                break;
            }

            if (bytesRead == 0) {
                std::array<UCHAR, 32> digest{};
                if (BCRYPT_SUCCESS(BCryptFinishHash(
                        hash.get(), digest.data(),
                        static_cast<ULONG>(digest.size()), 0))) {
                    matches = DigestMatchesHex(digest, expectedHex);
                }
                break;
            }

            if (!BCRYPT_SUCCESS(
                    BCryptHashData(hash.get(), static_cast<PUCHAR>(buffer.get()),
                                   bytesRead, 0))) {
                break;
            }
        }
    } while (false);

    return matches;
}

bool GetModulePath(HMODULE module, std::wstring* path) {
    std::array<wchar_t, 32768> buffer{};
    DWORD length =
        GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0 || length >= buffer.size()) {
        return false;
    }

    path->assign(buffer.data(), length);
    return true;
}

bool HasExpectedProductVersion(PCWSTR path, const FileVersion& expected) {
    DWORD unused = 0;
    DWORD dataSize = GetFileVersionInfoSizeW(path, &unused);
    if (!dataSize) {
        return false;
    }

    std::vector<BYTE> data(dataSize);
    if (!GetFileVersionInfoW(path, 0, dataSize, data.data())) {
        return false;
    }

    VS_FIXEDFILEINFO* fixedInfo = nullptr;
    UINT fixedInfoSize = 0;
    if (!VerQueryValueW(data.data(), L"\\",
                        reinterpret_cast<void**>(&fixedInfo),
                        &fixedInfoSize) ||
        !fixedInfo || fixedInfoSize < sizeof(*fixedInfo)) {
        return false;
    }

    FileVersion actual{
        HIWORD(fixedInfo->dwProductVersionMS),
        LOWORD(fixedInfo->dwProductVersionMS),
        HIWORD(fixedInfo->dwProductVersionLS),
        LOWORD(fixedInfo->dwProductVersionLS),
    };

    return actual.major == expected.major && actual.minor == expected.minor &&
           actual.build == expected.build &&
           actual.revision == expected.revision;
}

bool HasExpectedSize(PCWSTR path, std::uint64_t expected) {
    UniqueHandle file(CreateFileW(path, FILE_READ_ATTRIBUTES,
                                  FILE_SHARE_READ | FILE_SHARE_WRITE |
                                      FILE_SHARE_DELETE,
                                  nullptr, OPEN_EXISTING, 0, nullptr));
    if (!file) {
        return false;
    }

    LARGE_INTEGER size{};
    return GetFileSizeEx(file.get(), &size) &&
           static_cast<std::uint64_t>(size.QuadPart) == expected;
}

bool VerifyFile(PCWSTR label,
                PCWSTR path,
                const FileVersion& expectedVersion,
                std::uint64_t expectedSize,
                PCWSTR expectedSha256) {
    // Keep the path's current file object stable across the separate Windows
    // version-resource, size, and digest APIs. Sharing read only denies a
    // servicing replacement or write until this fingerprint is complete.
    UniqueHandle stableFile(CreateFileW(path, GENERIC_READ, FILE_SHARE_READ,
                                        nullptr, OPEN_EXISTING,
                                        FILE_ATTRIBUTE_NORMAL, nullptr));
    if (!stableFile) {
        Wh_Log(L"%s fingerprint file couldn't be locked for reading", label);
        return false;
    }

    bool versionMatches = HasExpectedProductVersion(path, expectedVersion);
    bool sizeMatches = HasExpectedSize(path, expectedSize);
    bool hashMatches = FileSha256Matches(path, expectedSha256);

    Wh_Log(L"%s fingerprint: version=%d size=%d sha256=%d", label,
           versionMatches, sizeMatches, hashMatches);
    return versionMatches && sizeMatches && hashMatches;
}

template <size_t N>
bool BuildFixedPath(PCWSTR base,
                    PCWSTR suffix,
                    std::array<wchar_t, N>* destination) {
    if (!base || !suffix || !destination) {
        return false;
    }

    size_t baseLength = wcslen(base);
    size_t suffixLength = wcslen(suffix);
    if (baseLength == 0 || baseLength + suffixLength + 1 > N) {
        return false;
    }

    memcpy(destination->data(), base, baseLength * sizeof(wchar_t));
    memcpy(destination->data() + baseLength, suffix,
           (suffixLength + 1) * sizeof(wchar_t));
    return true;
}

bool InitializeStatePaths() {
    PWSTR localAppData = nullptr;
    HRESULT result = SHGetKnownFolderPath(
        FOLDERID_LocalAppData, KF_FLAG_DONT_VERIFY, nullptr, &localAppData);
    if (FAILED(result) || !localAppData) {
        if (localAppData) {
            CoTaskMemFree(localAppData);
        }
        Wh_Log(L"Can't resolve FOLDERID_LocalAppData; failing closed");
        return false;
    }

    bool pathsBuilt =
        BuildFixedPath(localAppData, kStateDirectorySuffix,
                       &g_stateDirectoryPath) &&
        BuildFixedPath(g_stateDirectoryPath.data(), kKillSwitchSuffix,
                       &g_killSwitchPath) &&
        BuildFixedPath(g_stateDirectoryPath.data(), kActivationPermitSuffix,
                       &g_activationPermitPath) &&
        BuildFixedPath(g_stateDirectoryPath.data(), kRecoveryLeaseSuffix,
                       &g_recoveryLeasePath);
    CoTaskMemFree(localAppData);

    if (!pathsBuilt) {
        Wh_Log(L"Known-folder state path is too long; failing closed");
        return false;
    }

    return true;
}

bool IsPathDefinitelyAbsent(PCWSTR path) {
    DWORD attributes = GetFileAttributesW(path);
    if (attributes != INVALID_FILE_ATTRIBUTES) {
        return false;
    }

    DWORD error = GetLastError();
    return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND;
}

bool IsEmergencyKillSwitchArmed() {
    if (!g_killSwitchPath[0]) {
        return true;
    }

    DWORD attributes = GetFileAttributesW(g_killSwitchPath.data());
    if (attributes != INVALID_FILE_ATTRIBUTES) {
        return true;
    }

    DWORD error = GetLastError();
    return error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND;
}

ULONGLONG FileTimeTicks(const FILETIME& fileTime) {
    return (static_cast<ULONGLONG>(fileTime.dwHighDateTime) << 32) |
           fileTime.dwLowDateTime;
}

bool IsActivationPermitFresh(const FILETIME& lastWriteTime) {
    FILETIME currentTime{};
    GetSystemTimeAsFileTime(&currentTime);

    ULONGLONG currentTicks = FileTimeTicks(currentTime);
    ULONGLONG lastWriteTicks = FileTimeTicks(lastWriteTime);
    if (currentTicks < lastWriteTicks) {
        Wh_Log(L"Activation permit is future-dated; leaving it unconsumed");
        return false;
    }

    if (currentTicks - lastWriteTicks > kActivationPermitMaxAgeTicks) {
        Wh_Log(L"Activation permit is older than five minutes; leaving it "
               L"unconsumed");
        return false;
    }

    return true;
}

bool ValidateAndConsumeActivationPermit() {
    if (!g_activationPermitPath[0]) {
        return false;
    }

    UniqueHandle permit(CreateFileW(
        g_activationPermitPath.data(), GENERIC_READ | DELETE, 0, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT,
        nullptr));
    if (!permit) {
        Wh_Log(L"Activation permit is absent or can't be opened exclusively; "
               L"initialization blocked (error=%lu)",
               GetLastError());
        return false;
    }

    BY_HANDLE_FILE_INFORMATION fileInformation{};
    if (!GetFileInformationByHandle(permit.get(), &fileInformation) ||
        (fileInformation.dwFileAttributes &
         (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT))) {
        Wh_Log(L"Activation permit isn't a regular non-reparse file");
        return false;
    }

    // Freshness is part of the capability contract. An expired, future-dated,
    // or wrong-module permit is left in place so the supervisor and user can
    // diagnose the rejected preparation explicitly.
    if (!IsActivationPermitFresh(fileInformation.ftLastWriteTime)) {
        return false;
    }

    bool payloadMatches = false;
    LARGE_INTEGER fileSize{};
    constexpr DWORD expectedPayloadSize =
        static_cast<DWORD>(sizeof(kActivationPermitPayload) - 1);
    if (GetFileSizeEx(permit.get(), &fileSize) &&
        fileSize.QuadPart == expectedPayloadSize) {
        std::array<char, expectedPayloadSize> payload{};
        DWORD bytesRead = 0;
        payloadMatches =
            ReadFile(permit.get(), payload.data(), expectedPayloadSize,
                     &bytesRead, nullptr) &&
            bytesRead == expectedPayloadSize &&
            memcmp(payload.data(), kActivationPermitPayload,
                   expectedPayloadSize) == 0;
    }

    if (!payloadMatches) {
        Wh_Log(L"Activation permit payload doesn't name this module; leaving "
               L"it unconsumed");
        return false;
    }

    // Mark the exclusively opened, fully validated regular file for deletion
    // before accepting it. If consumption fails, no hook may be registered.
    FILE_DISPOSITION_INFO disposition{
        .DeleteFile = TRUE,
    };
    if (!SetFileInformationByHandle(permit.get(), FileDispositionInfo,
                                    &disposition, sizeof(disposition))) {
        Wh_Log(L"Activation permit couldn't be consumed; initialization "
               L"blocked (error=%lu)",
               GetLastError());
        return false;
    }

    permit.reset();
    if (!IsPathDefinitelyAbsent(g_activationPermitPath.data())) {
        Wh_Log(L"Activation permit remained or was recreated; initialization "
               L"blocked");
        return false;
    }

    Wh_Log(L"One-shot activation permit consumed for %s", kModuleId);
    return true;
}

void LatchRuntimeBlocked(PCWSTR reason) {
    bool wasBlocked =
        g_runtimeBlocked.exchange(true, std::memory_order_acq_rel);

    ModuleState state = g_state.load(std::memory_order_acquire);
    while (state == ModuleState::kVerified || state == ModuleState::kActive) {
        if (g_state.compare_exchange_weak(state, ModuleState::kQuiesced,
                                          std::memory_order_acq_rel)) {
            break;
        }
    }

    if (!wasBlocked) {
        Wh_Log(L"Module latched into pass-through mode: %s", reason);
    }
}

bool IsRecoveryLeaseHeartbeatFresh() {
    if (!g_recoveryLeasePath[0]) {
        return false;
    }

    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if (!GetFileAttributesExW(g_recoveryLeasePath.data(),
                              GetFileExInfoStandard, &attributes) ||
        (attributes.dwFileAttributes &
         (FILE_ATTRIBUTE_DIRECTORY | FILE_ATTRIBUTE_REPARSE_POINT)) != 0) {
        return false;
    }

    FILETIME nowFileTime{};
    GetSystemTimeAsFileTime(&nowFileTime);
    ULARGE_INTEGER now{};
    now.LowPart = nowFileTime.dwLowDateTime;
    now.HighPart = nowFileTime.dwHighDateTime;
    ULARGE_INTEGER heartbeat{};
    heartbeat.LowPart = attributes.ftLastWriteTime.dwLowDateTime;
    heartbeat.HighPart = attributes.ftLastWriteTime.dwHighDateTime;

    if (heartbeat.QuadPart > now.QuadPart) {
        return heartbeat.QuadPart - now.QuadPart <=
               kRecoveryLeaseFutureSkewTicks;
    }

    return now.QuadPart - heartbeat.QuadPart <=
           kRecoveryLeaseMaxAgeTicks;
}

DWORD WINAPI KillSwitchWatcherThread(void*) {
    HANDLE waitHandles[] = {
        g_watcherStopEvent,
        g_watcherChangeNotification,
    };

    for (;;) {
        DWORD waitResult = WaitForMultipleObjects(
            ARRAYSIZE(waitHandles), waitHandles, FALSE,
            kRecoveryLeasePollIntervalMs);
        if (waitResult == WAIT_OBJECT_0) {
            return 0;
        }

        if (waitResult == WAIT_OBJECT_0 + 1) {
            // The state root is dedicated to safety controls. Any direct
            // file-name mutation after activation is conservatively treated as
            // an emergency request. active-module.txt is consumed before this
            // watcher starts. Recovery heartbeats live in a pre-created child
            // directory and therefore don't trigger this non-recursive watch.
            LatchRuntimeBlocked(L"JARVIS2 state directory changed");
            return 0;
        }

        if (waitResult == WAIT_TIMEOUT) {
            if (IsRecoveryLeaseHeartbeatFresh()) {
                continue;
            }

            LatchRuntimeBlocked(L"recovery-terminal heartbeat expired");
            return WAIT_TIMEOUT;
        }

        DWORD error = GetLastError();
        LatchRuntimeBlocked(L"kill-switch watcher failed");
        return error;
    }
}

bool StartKillSwitchWatcher() {
    if (!g_stateDirectoryPath[0] || g_watcherThread ||
        g_watcherStopEvent ||
        g_watcherChangeNotification != INVALID_HANDLE_VALUE) {
        return false;
    }

    if (!IsRecoveryLeaseHeartbeatFresh()) {
        Wh_Log(L"Recovery-terminal heartbeat is missing or stale");
        return false;
    }

    g_watcherStopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_watcherStopEvent) {
        return false;
    }

    g_watcherChangeNotification = FindFirstChangeNotificationW(
        g_stateDirectoryPath.data(), FALSE, FILE_NOTIFY_CHANGE_FILE_NAME);
    if (g_watcherChangeNotification == INVALID_HANDLE_VALUE) {
        CloseHandle(g_watcherStopEvent);
        g_watcherStopEvent = nullptr;
        return false;
    }

    g_watcherThread =
        CreateThread(nullptr, 0, KillSwitchWatcherThread, nullptr, 0, nullptr);
    if (!g_watcherThread) {
        FindCloseChangeNotification(g_watcherChangeNotification);
        g_watcherChangeNotification = INVALID_HANDLE_VALUE;
        CloseHandle(g_watcherStopEvent);
        g_watcherStopEvent = nullptr;
        return false;
    }

    return true;
}

bool StopKillSwitchWatcher() {
    if (g_watcherThread) {
        SetEvent(g_watcherStopEvent);
        constexpr DWORD kWatcherStopTimeoutMs = 5000;
        DWORD waitResult =
            WaitForSingleObject(g_watcherThread, kWatcherStopTimeoutMs);
        if (waitResult != WAIT_OBJECT_0) {
            Wh_Log(L"Kill-switch watcher didn't stop cleanly (wait=%lu)",
                   waitResult);
            return false;
        }

        CloseHandle(g_watcherThread);
        g_watcherThread = nullptr;
    }

    if (g_watcherChangeNotification != INVALID_HANDLE_VALUE) {
        FindCloseChangeNotification(g_watcherChangeNotification);
        g_watcherChangeNotification = INVALID_HANDLE_VALUE;
    }

    if (g_watcherStopEvent) {
        CloseHandle(g_watcherStopEvent);
        g_watcherStopEvent = nullptr;
    }

    return true;
}

bool IsExactWindowsProfile() {
    using RtlGetVersion_t = LONG(WINAPI*)(PRTL_OSVERSIONINFOW);

    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    auto rtlGetVersion = ntdll
                             ? reinterpret_cast<RtlGetVersion_t>(
                                   GetProcAddress(ntdll, "RtlGetVersion"))
                             : nullptr;
    if (!rtlGetVersion) {
        return false;
    }

    RTL_OSVERSIONINFOW version{
        .dwOSVersionInfoSize = sizeof(version),
    };
    if (rtlGetVersion(&version) < 0 || version.dwMajorVersion != 10 ||
        version.dwBuildNumber != kValidatedBuild) {
        return false;
    }

    DWORD ubr = 0;
    DWORD ubrSize = sizeof(ubr);
    if (RegGetValueW(
            HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"UBR",
            RRF_RT_REG_DWORD, nullptr, &ubr, &ubrSize) != ERROR_SUCCESS ||
        ubr != kValidatedUbr) {
        return false;
    }

    std::array<wchar_t, 32> installationType{};
    DWORD installationTypeSize =
        static_cast<DWORD>(installationType.size() * sizeof(wchar_t));
    if (RegGetValueW(HKEY_LOCAL_MACHINE,
                     L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",
                     L"InstallationType", RRF_RT_REG_SZ, nullptr,
                     installationType.data(), &installationTypeSize) !=
            ERROR_SUCCESS ||
        _wcsicmp(installationType.data(), kValidatedInstallationType) != 0) {
        return false;
    }

    return true;
}

struct CodeViewRsdsHeader {
    DWORD signature;
    GUID guid;
    DWORD age;
};

static_assert(sizeof(CodeViewRsdsHeader) == 24);
constexpr DWORD kCodeViewRsdsSignature = 0x53445352;  // "RSDS"

bool IsMappedImageRangeValid(DWORD rva, DWORD size, DWORD imageSize) {
    return rva != 0 && rva < imageSize && size <= imageSize - rva;
}

bool IsReadableMappedImageRange(const BYTE* base,
                                DWORD rva,
                                SIZE_T size,
                                DWORD maximumImageSize) {
    if (!base || size == 0 || rva > maximumImageSize ||
        size > static_cast<SIZE_T>(maximumImageSize - rva)) {
        return false;
    }

    std::uintptr_t allocationBase =
        reinterpret_cast<std::uintptr_t>(base);
    std::uintptr_t current = allocationBase + rva;
    std::uintptr_t end = current + size;
    if (current < allocationBase || end < current) {
        return false;
    }

    while (current < end) {
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(reinterpret_cast<const void*>(current), &memory,
                         sizeof(memory)) != sizeof(memory) ||
            memory.AllocationBase != base || memory.State != MEM_COMMIT ||
            (memory.Protect & PAGE_GUARD) ||
            (memory.Protect & 0xFF) == PAGE_NOACCESS) {
            return false;
        }

        std::uintptr_t regionStart =
            reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
        std::uintptr_t regionEnd = regionStart + memory.RegionSize;
        if (regionEnd <= current || regionEnd < regionStart) {
            return false;
        }
        current = (std::min)(regionEnd, end);
    }

    return true;
}

bool HasExpectedCodeViewIdentity(const BYTE* base,
                                 const IMAGE_NT_HEADERS64* ntHeaders,
                                 const GUID& expectedPdbGuid,
                                 DWORD expectedPdbAge) {
    if (ntHeaders->OptionalHeader.NumberOfRvaAndSizes <=
        IMAGE_DIRECTORY_ENTRY_DEBUG) {
        return false;
    }

    const IMAGE_DATA_DIRECTORY& debugDataDirectory =
        ntHeaders->OptionalHeader
            .DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    DWORD imageSize = ntHeaders->OptionalHeader.SizeOfImage;
    if (debugDataDirectory.Size < sizeof(IMAGE_DEBUG_DIRECTORY) ||
        debugDataDirectory.Size % sizeof(IMAGE_DEBUG_DIRECTORY) != 0 ||
        !IsMappedImageRangeValid(debugDataDirectory.VirtualAddress,
                                 debugDataDirectory.Size, imageSize) ||
        !IsReadableMappedImageRange(base,
                                    debugDataDirectory.VirtualAddress,
                                    debugDataDirectory.Size, imageSize)) {
        return false;
    }

    DWORD debugDirectoryCount =
        debugDataDirectory.Size / sizeof(IMAGE_DEBUG_DIRECTORY);
    for (DWORD i = 0; i < debugDirectoryCount; i++) {
        IMAGE_DEBUG_DIRECTORY debugDirectory{};
        DWORD directoryRva =
            debugDataDirectory.VirtualAddress +
            i * sizeof(IMAGE_DEBUG_DIRECTORY);
        memcpy(&debugDirectory, base + directoryRva,
               sizeof(debugDirectory));
        if (debugDirectory.Type != IMAGE_DEBUG_TYPE_CODEVIEW ||
            debugDirectory.SizeOfData < sizeof(CodeViewRsdsHeader) ||
            !IsMappedImageRangeValid(debugDirectory.AddressOfRawData,
                                     debugDirectory.SizeOfData, imageSize) ||
            !IsReadableMappedImageRange(base,
                                        debugDirectory.AddressOfRawData,
                                        sizeof(CodeViewRsdsHeader),
                                        imageSize)) {
            continue;
        }

        // Use memcpy so an unexpectedly unaligned CodeView record still fails
        // safely without relying on compiler alignment assumptions.
        CodeViewRsdsHeader codeView{};
        memcpy(&codeView, base + debugDirectory.AddressOfRawData,
               sizeof(codeView));
        if (codeView.signature == kCodeViewRsdsSignature &&
            codeView.age == expectedPdbAge &&
            memcmp(&codeView.guid, &expectedPdbGuid, sizeof(GUID)) == 0) {
            return true;
        }
    }

    return false;
}

bool HasExpectedLoadedImageIdentity(PCWSTR label,
                                    HMODULE module,
                                    DWORD expectedTimeDateStamp,
                                    DWORD expectedSizeOfImage,
                                    const GUID& expectedPdbGuid,
                                    DWORD expectedPdbAge) {
    if (!module) {
        return false;
    }

    const auto* base = reinterpret_cast<const BYTE*>(module);
    if (!IsReadableMappedImageRange(base, 0, sizeof(IMAGE_DOS_HEADER),
                                    expectedSizeOfImage)) {
        Wh_Log(L"%s loaded-image DOS header isn't readable", label);
        return false;
    }

    IMAGE_DOS_HEADER dosHeader{};
    memcpy(&dosHeader, base, sizeof(dosHeader));
    if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE ||
        dosHeader.e_lfanew < static_cast<LONG>(sizeof(IMAGE_DOS_HEADER)) ||
        dosHeader.e_lfanew > 1024 * 1024 ||
        !IsReadableMappedImageRange(
            base, static_cast<DWORD>(dosHeader.e_lfanew),
            sizeof(IMAGE_NT_HEADERS64), expectedSizeOfImage)) {
        Wh_Log(L"%s loaded-image DOS header mismatch", label);
        return false;
    }

    IMAGE_NT_HEADERS64 ntHeaders{};
    memcpy(&ntHeaders, base + dosHeader.e_lfanew, sizeof(ntHeaders));
    if (ntHeaders.Signature != IMAGE_NT_SIGNATURE ||
        ntHeaders.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        ntHeaders.FileHeader.SizeOfOptionalHeader <
            sizeof(IMAGE_OPTIONAL_HEADER64) ||
        ntHeaders.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        ntHeaders.OptionalHeader.NumberOfRvaAndSizes <=
            IMAGE_DIRECTORY_ENTRY_DEBUG) {
        Wh_Log(L"%s loaded-image NT header mismatch", label);
        return false;
    }

    bool peIdentityMatches =
        ntHeaders.FileHeader.TimeDateStamp == expectedTimeDateStamp &&
        ntHeaders.OptionalHeader.SizeOfImage == expectedSizeOfImage;
    bool codeViewMatches =
        peIdentityMatches &&
        HasExpectedCodeViewIdentity(base, &ntHeaders, expectedPdbGuid,
                                    expectedPdbAge);

    Wh_Log(L"%s loaded-image identity: timestamp=%08X size=%08X "
           L"pe=%d codeview=%d",
           label, ntHeaders.FileHeader.TimeDateStamp,
           ntHeaders.OptionalHeader.SizeOfImage, peIdentityMatches,
           codeViewMatches);
    return peIdentityMatches && codeViewMatches;
}

bool IsCurrentProcessTheVerifiedDesktopShell() {
    HWND shellWindow = GetShellWindow();
    HWND taskbarWindow = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!shellWindow || !taskbarWindow) {
        return false;
    }

    DWORD shellProcessId = 0;
    DWORD taskbarProcessId = 0;
    GetWindowThreadProcessId(shellWindow, &shellProcessId);
    GetWindowThreadProcessId(taskbarWindow, &taskbarProcessId);
    DWORD currentProcessId = GetCurrentProcessId();
    if (!shellProcessId || shellProcessId != taskbarProcessId ||
        shellProcessId != currentProcessId) {
        return false;
    }

    DWORD shellProcessIdAgain = 0;
    DWORD taskbarProcessIdAgain = 0;
    return GetShellWindow() == shellWindow &&
           FindWindowW(L"Shell_TrayWnd", nullptr) == taskbarWindow &&
           GetWindowThreadProcessId(shellWindow, &shellProcessIdAgain) &&
           GetWindowThreadProcessId(taskbarWindow, &taskbarProcessIdAgain) &&
           shellProcessIdAgain == currentProcessId &&
           taskbarProcessIdAgain == currentProcessId;
}

bool IsLoadedModuleAtExpectedPath(PCWSTR moduleName,
                                  const std::wstring& expectedPath) {
    HMODULE module = GetModuleHandleW(moduleName);
    if (!module) {
        return false;
    }

    std::wstring actualPath;
    return GetModulePath(module, &actualPath) &&
           _wcsicmp(actualPath.c_str(), expectedPath.c_str()) == 0;
}

bool VerifyHost(HMODULE* taskbarViewModule) {
    if (!IsExactWindowsProfile()) {
        Wh_Log(L"Windows build profile mismatch; initialization blocked");
        return false;
    }

    std::array<wchar_t, 32768> windowsDirectory{};
    UINT windowsDirectoryLength = GetWindowsDirectoryW(
        windowsDirectory.data(), static_cast<UINT>(windowsDirectory.size()));
    if (windowsDirectoryLength == 0 ||
        windowsDirectoryLength >= windowsDirectory.size()) {
        return false;
    }

    std::wstring expectedExplorerPath(windowsDirectory.data(),
                                      windowsDirectoryLength);
    expectedExplorerPath += L"\\explorer.exe";
    std::wstring expectedTaskbarViewPath(windowsDirectory.data(),
                                         windowsDirectoryLength);
    expectedTaskbarViewPath += L"\\";
    expectedTaskbarViewPath += kTaskbarViewRelativePath;
    std::wstring expectedSystemTrayPath(windowsDirectory.data(),
                                        windowsDirectoryLength);
    expectedSystemTrayPath +=
        L"\\SystemApps\\MicrosoftWindows.Client.Core_cw5n1h2txyewy"
        L"\\SystemTray.dll";
    std::wstring expectedSearchUxPath(windowsDirectory.data(),
                                      windowsDirectoryLength);
    expectedSearchUxPath +=
        L"\\SystemApps\\MicrosoftWindows.Client.CBS_cw5n1h2txyewy"
        L"\\SearchUx.UI.dll";

    if (!IsCurrentProcessTheVerifiedDesktopShell() ||
        !IsLoadedModuleAtExpectedPath(L"SystemTray.dll",
                                      expectedSystemTrayPath) ||
        !IsLoadedModuleAtExpectedPath(L"SearchUx.UI.dll",
                                      expectedSearchUxPath)) {
        Wh_Log(L"This process isn't the audited desktop shell instance");
        return false;
    }

    std::wstring actualExplorerPath;
    if (!GetModulePath(nullptr, &actualExplorerPath) ||
        _wcsicmp(actualExplorerPath.c_str(), expectedExplorerPath.c_str()) !=
            0) {
        Wh_Log(L"Explorer image path mismatch; initialization blocked");
        return false;
    }

    HMODULE loadedTaskbarView = nullptr;
    if (!GetModuleHandleExW(0, L"Taskbar.View.dll", &loadedTaskbarView)) {
        Wh_Log(L"Taskbar.View.dll isn't loaded; refusing delayed hooks");
        return false;
    }
    UniqueModuleReference taskbarViewReference(loadedTaskbarView);

    std::wstring actualTaskbarViewPath;
    if (!GetModulePath(loadedTaskbarView, &actualTaskbarViewPath) ||
        _wcsicmp(actualTaskbarViewPath.c_str(),
                 expectedTaskbarViewPath.c_str()) != 0) {
        Wh_Log(L"Loaded Taskbar.View.dll path mismatch; initialization blocked");
        return false;
    }

    if (GetModuleHandleW(L"ExplorerExtensions.dll")) {
        Wh_Log(L"Legacy ExplorerExtensions.dll is loaded; profile rejected");
        return false;
    }

    if (!HasExpectedLoadedImageIdentity(
            L"explorer.exe", GetModuleHandleW(nullptr),
            kExplorerImageTimeDateStamp, kExplorerImageSize,
            kExplorerPdbGuid, kPdbAge) ||
        !HasExpectedLoadedImageIdentity(
            L"Taskbar.View.dll", loadedTaskbarView,
            kTaskbarViewImageTimeDateStamp, kTaskbarViewImageSize,
            kTaskbarViewPdbGuid, kPdbAge)) {
        Wh_Log(L"Loaded host image identity mismatch; initialization blocked");
        return false;
    }

    if (!VerifyFile(L"explorer.exe", actualExplorerPath.c_str(),
                    kExplorerVersion, kExplorerSize, kExplorerSha256) ||
        !VerifyFile(L"Taskbar.View.dll", actualTaskbarViewPath.c_str(),
                    kTaskbarViewVersion, kTaskbarViewSize,
                    kTaskbarViewSha256)) {
        Wh_Log(L"Host binary fingerprint mismatch; initialization blocked");
        return false;
    }

    *taskbarViewModule = taskbarViewReference.release();
    return true;
}

struct ModuleSettings {
    bool enabled;
    int iconSize;
};

ModuleSettings ReadSettings() {
    int enabledValue = Wh_GetIntSetting(L"Enabled");
    int requestedIconSize = Wh_GetIntSetting(L"IconSize");

    bool enabled = enabledValue == 1;
    if (enabledValue != 0 && enabledValue != 1) {
        Wh_Log(L"Enabled setting %d is invalid; treating it as false",
               enabledValue);
    }

    int iconSize = requestedIconSize;
    if (iconSize < 20 || iconSize > 32) {
        iconSize = 24;
        Wh_Log(L"IconSize %d is invalid; using stock-safe value 24",
               requestedIconSize);
    }

    return ModuleSettings{
        .enabled = enabled,
        .iconSize = iconSize,
    };
}

double WINAPI GetIconHeightInViewPixels_Hook(void* pThis) {
    double original = GetIconHeightInViewPixels_Original(pThis);

    if (g_runtimeBlocked.load(std::memory_order_acquire) ||
        g_state.load(std::memory_order_acquire) != ModuleState::kActive) {
        return original;
    }

    // Leave the audited Windows small-icon result alone. Any other unexpected
    // future range latches the whole module into pass-through mode.
    if (std::isfinite(original) && original >= 15.5 && original <= 16.5) {
        return original;
    }
    if (!std::isfinite(original) || original < 20.0 || original > 40.0) {
        LatchRuntimeBlocked(L"unexpected native icon-size result");
        return original;
    }

    return static_cast<double>(g_iconSize.load(std::memory_order_acquire));
}

bool HookTaskbarIconSize(HMODULE taskbarViewModule) {
    WindhawkUtils::SYMBOL_HOOK hooks[] = {
        {
            {LR"(public: double __cdecl winrt::Taskbar::implementation::TaskbarConfiguration::GetIconHeightInViewPixels(void))"},
            &GetIconHeightInViewPixels_Original,
            GetIconHeightInViewPixels_Hook,
        },
    };

    return HookSymbols(taskbarViewModule, hooks, ARRAYSIZE(hooks));
}

}  // namespace

BOOL Wh_ModInit() {
    Wh_Log(L">");

    UniqueModuleReference taskbarViewReference;
    try {
        g_state.store(ModuleState::kBlocked, std::memory_order_release);
        g_runtimeBlocked.store(true, std::memory_order_release);

        if (!InitializeStatePaths()) {
            return FALSE;
        }

        if (IsEmergencyKillSwitchArmed()) {
            Wh_Log(L"Emergency kill switch is armed; initialization blocked");
            return FALSE;
        }

        ModuleSettings settings = ReadSettings();
        if (!settings.enabled) {
            Wh_Log(L"Enabled is false; no symbols or APIs will be hooked");
            return FALSE;
        }

        HMODULE taskbarViewModule = nullptr;
        if (!VerifyHost(&taskbarViewModule)) {
            return FALSE;
        }
        taskbarViewReference.reset(taskbarViewModule);

        StateGateLease stateGate;
        if (!stateGate.tryAcquire()) {
            Wh_Log(L"JARVIS2 state gate blocked initialization");
            return FALSE;
        }

        // Re-check under the same named gate used by the supervisor. The
        // earlier check is only a fast path; this check establishes the safety
        // snapshot used for permit consumption.
        if (IsEmergencyKillSwitchArmed()) {
            Wh_Log(L"Emergency kill switch is armed under the state gate");
            return FALSE;
        }

        // Enabled=false and compatibility failures returned above without
        // touching the permit. Validate and consume the supervisor's exact
        // active-module.txt capability while holding the shared state gate.
        if (!ValidateAndConsumeActivationPermit()) {
            LatchRuntimeBlocked(L"activation permit unavailable");
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            return FALSE;
        }

        // active-module.txt lives in the watched root, so start the watcher
        // only after our own deletion has completed. The state gate remains
        // held throughout this transition, and the kill switch is re-checked
        // before the gate is released.
        g_runtimeBlocked.store(false, std::memory_order_release);
        g_state.store(ModuleState::kVerified, std::memory_order_release);
        if (!StartKillSwitchWatcher()) {
            LatchRuntimeBlocked(L"kill-switch watcher couldn't start");
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            return FALSE;
        }

        if (g_runtimeBlocked.load(std::memory_order_acquire) ||
            IsEmergencyKillSwitchArmed()) {
            LatchRuntimeBlocked(L"emergency kill switch");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            Wh_Log(L"Emergency kill switch changed after permit consumption");
            return FALSE;
        }

        if (!stateGate.release()) {
            LatchRuntimeBlocked(L"state gate couldn't be released cleanly");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            return FALSE;
        }

        // The supervisor can change safety state as soon as the gate is
        // released. Re-check before the sole HookSymbols call; the watcher is
        // already live and its atomic latch also keeps the hook pass-through.
        if (g_runtimeBlocked.load(std::memory_order_acquire) ||
            IsEmergencyKillSwitchArmed()) {
            LatchRuntimeBlocked(L"emergency kill switch");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            Wh_Log(L"Emergency kill switch changed after state-gate release");
            return FALSE;
        }

        g_iconSize.store(settings.iconSize, std::memory_order_release);
        if (!HookTaskbarIconSize(taskbarViewModule)) {
            LatchRuntimeBlocked(L"required Taskbar.View symbol wasn't resolved");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            Wh_Log(L"Required Taskbar.View symbol wasn't resolved");
            return FALSE;
        }

        if (g_runtimeBlocked.load(std::memory_order_acquire) ||
            IsEmergencyKillSwitchArmed()) {
            LatchRuntimeBlocked(L"emergency kill switch");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            Wh_Log(L"Emergency kill switch changed during symbol resolution");
            return FALSE;
        }

        ModuleState expectedState = ModuleState::kVerified;
        if (!g_state.compare_exchange_strong(expectedState,
                                             ModuleState::kActive,
                                             std::memory_order_acq_rel)) {
            LatchRuntimeBlocked(L"initialization state changed");
            StopKillSwitchWatcher();
            g_state.store(ModuleState::kBlocked, std::memory_order_release);
            return FALSE;
        }

        g_taskbarViewModuleReference = taskbarViewReference.release();
        Wh_Log(L"Native icon-size hook verified; size=%d",
               g_iconSize.load(std::memory_order_acquire));
        return TRUE;
    } catch (...) {
        LatchRuntimeBlocked(L"initialization exception");
        StopKillSwitchWatcher();
        g_state.store(ModuleState::kBlocked, std::memory_order_release);
        Wh_Log(L"Unexpected initialization exception; failing closed");
        return FALSE;
    }
}

void Wh_ModBeforeUninit() {
    Wh_Log(L">");
    LatchRuntimeBlocked(L"module unloading");
    g_state.store(ModuleState::kUnloading, std::memory_order_release);
    StopKillSwitchWatcher();
}

void Wh_ModUninit() {
    Wh_Log(L">");

    StopKillSwitchWatcher();
    if (g_taskbarViewModuleReference) {
        FreeLibrary(g_taskbarViewModuleReference);
        g_taskbarViewModuleReference = nullptr;
    }
}

void Wh_ModSettingsChanged() {
    Wh_Log(L">");

    try {
        ModuleSettings settings = ReadSettings();
        if (!settings.enabled) {
            LatchRuntimeBlocked(L"Enabled setting turned off");
            return;
        }

        if (g_runtimeBlocked.load(std::memory_order_acquire) ||
            IsEmergencyKillSwitchArmed()) {
            LatchRuntimeBlocked(L"emergency kill switch");
            return;
        }

        if (g_state.load(std::memory_order_acquire) != ModuleState::kActive) {
            LatchRuntimeBlocked(L"settings changed without an active hook");
            Wh_Log(L"A full module reload and a new one-shot permit are "
                   L"required");
            return;
        }

        // Publish only after every safety check. This callback never registers
        // hooks and never clears a latched pass-through state.
        g_iconSize.store(settings.iconSize, std::memory_order_release);
        Wh_Log(L"Icon size updated to %d; existing visuals refresh only when "
               L"Windows next recalculates them",
               g_iconSize.load(std::memory_order_acquire));
    } catch (...) {
        LatchRuntimeBlocked(L"settings exception");
    }
}
