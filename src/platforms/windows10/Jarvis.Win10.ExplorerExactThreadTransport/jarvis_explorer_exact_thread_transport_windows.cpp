#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include "jarvis_explorer_exact_thread_transport_internal.h"

#include <cstdint>

namespace {

std::uint32_t ValidateExactTarget(
    void*,
    const std::uint32_t explorer_process_id,
    const std::uint32_t shell_thread_id,
    const std::uint64_t shell_window_handle,
    std::uint32_t* const platform_error) noexcept {
    if (platform_error == nullptr || explorer_process_id == 0U ||
        shell_thread_id == 0U || shell_window_handle == 0U) {
        return 0U;
    }

    DWORD observed_process_id = 0U;
    const auto observed_thread_id = GetWindowThreadProcessId(
        reinterpret_cast<HWND>(
            static_cast<std::uintptr_t>(shell_window_handle)),
        &observed_process_id);
    if (observed_thread_id == 0U) {
        *platform_error = GetLastError();
        return 0U;
    }

    *platform_error = ERROR_SUCCESS;
    return observed_process_id == explorer_process_id &&
                   observed_thread_id == shell_thread_id
        ? 1U
        : 0U;
}

std::uint32_t InstallExactThreadHook(
    void*,
    const std::uint32_t shell_thread_id,
    const std::uint64_t module_handle,
    const std::uint64_t hook_procedure,
    std::uint64_t* const hook_handle,
    std::uint32_t* const platform_error) noexcept {
    if (hook_handle == nullptr || platform_error == nullptr ||
        shell_thread_id == 0U || module_handle == 0U ||
        hook_procedure == 0U) {
        return 0U;
    }

    *hook_handle = 0U;
    const auto hook = SetWindowsHookExW(
        WH_CALLWNDPROC,
        reinterpret_cast<HOOKPROC>(
            static_cast<std::uintptr_t>(hook_procedure)),
        reinterpret_cast<HINSTANCE>(
            static_cast<std::uintptr_t>(module_handle)),
        shell_thread_id);
    if (hook == nullptr) {
        *platform_error = GetLastError();
        return 0U;
    }

    *platform_error = ERROR_SUCCESS;
    *hook_handle = static_cast<std::uint64_t>(
        reinterpret_cast<std::uintptr_t>(hook));
    return 1U;
}

std::uint32_t RemoveExactThreadHook(
    void*,
    const std::uint64_t hook_handle,
    std::uint32_t* const platform_error) noexcept {
    if (platform_error == nullptr || hook_handle == 0U) {
        return 0U;
    }

    const auto hook = reinterpret_cast<HHOOK>(
        static_cast<std::uintptr_t>(hook_handle));
    if (UnhookWindowsHookEx(hook) == FALSE) {
        *platform_error = GetLastError();
        return 0U;
    }

    *platform_error = ERROR_SUCCESS;
    return 1U;
}

}  // namespace

jarvis_exact_thread_platform_api
jarvis_exact_thread_windows_platform_api() noexcept {
    return jarvis_exact_thread_platform_api{
        .size = sizeof(jarvis_exact_thread_platform_api),
        .execution_kind = JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE,
        .context = nullptr,
        .validate_exact_target = &ValidateExactTarget,
        .install_exact_thread_hook = &InstallExactThreadHook,
        .remove_exact_thread_hook = &RemoveExactThreadHook,
    };
}
