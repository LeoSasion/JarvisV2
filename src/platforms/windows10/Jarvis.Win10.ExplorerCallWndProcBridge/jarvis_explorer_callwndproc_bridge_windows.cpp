#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "jarvis_explorer_callwndproc_bridge_internal.h"

#include <cstdint>

namespace {

std::int64_t ChainToNextHook(
    void*,
    const std::int32_t n_code,
    const std::uint64_t w_param,
    const std::int64_t l_param) noexcept {
    return static_cast<std::int64_t>(CallNextHookEx(
        nullptr,
        n_code,
        static_cast<WPARAM>(w_param),
        static_cast<LPARAM>(l_param)));
}

}  // namespace

extern "C" __declspec(dllexport) LRESULT CALLBACK
JarvisBridge_CallWndProc(
    const int n_code,
    const WPARAM w_param,
    const LPARAM l_param) noexcept {
    return static_cast<LRESULT>(jarvis_callwndproc_dispatch(
        jarvis_bridge_core_global_instance(),
        GetCurrentProcessId(),
        GetCurrentThreadId(),
        static_cast<std::int32_t>(n_code),
        static_cast<std::uint64_t>(w_param),
        static_cast<std::int64_t>(l_param),
        nullptr,
        nullptr,
        &ChainToNextHook,
        nullptr,
        nullptr));
}

#if defined(JARVIS_ZIG_ZERO_ENTRY_LINK_STUB)
// Zig's Windows linker requires a DLL entry symbol even when the final PE is
// intentionally stamped with AddressOfEntryPoint = 0. The package builder
// verifies this exact no-op symbol, clears the PE field, then re-inspects it;
// Windows never calls this body in the shipped module.
extern "C" BOOL WINAPI _DllMainCRTStartup(
    HINSTANCE,
    DWORD,
    LPVOID) noexcept {
    return TRUE;
}
#endif
