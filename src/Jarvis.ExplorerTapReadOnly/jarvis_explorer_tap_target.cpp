#include "jarvis_explorer_tap_readonly.h"

#include <windows.h>

#include <cstdint>

namespace {

[[nodiscard]] std::uint64_t FileTimeToUInt64(
    const FILETIME& value) noexcept {
    return static_cast<std::uint64_t>(value.dwLowDateTime) |
           (static_cast<std::uint64_t>(value.dwHighDateTime) << 32U);
}

}  // namespace

jarvis_tap_target_result jarvis_tap_verify_exact_target(
    const jarvis_transport_bind_request* const request,
    const std::uint32_t require_current_thread) noexcept {
    if (request == nullptr) {
        return JARVIS_TAP_TARGET_RESULT_INVALID_ARGUMENT;
    }

    const auto& target = request->target;
    if (GetCurrentProcessId() != target.explorer_process_id ||
        target.explorer_process_id ==
            target.desktop_shell_process_id) {
        return JARVIS_TAP_TARGET_RESULT_PROCESS_MISMATCH;
    }

    const HWND shell_window = GetShellWindow();
    DWORD shell_process_id = 0U;
    if (shell_window == nullptr ||
        GetWindowThreadProcessId(
            shell_window,
            &shell_process_id) == 0U ||
        shell_process_id != target.desktop_shell_process_id) {
        return JARVIS_TAP_TARGET_RESULT_DESKTOP_SHELL_MISMATCH;
    }

    const HWND window = reinterpret_cast<HWND>(
        static_cast<std::uintptr_t>(target.window_handle));
    if (!IsWindow(window) || !IsWindowVisible(window)) {
        return JARVIS_TAP_TARGET_RESULT_WINDOW_INVALID;
    }

    DWORD window_process_id = 0U;
    const DWORD window_thread_id =
        GetWindowThreadProcessId(window, &window_process_id);
    if (window_thread_id == 0U ||
        window_process_id != target.explorer_process_id ||
        window_thread_id != target.window_thread_id) {
        return JARVIS_TAP_TARGET_RESULT_WINDOW_IDENTITY_MISMATCH;
    }
    if (require_current_thread == 1U &&
        GetCurrentThreadId() != target.window_thread_id) {
        return JARVIS_TAP_TARGET_RESULT_CURRENT_THREAD_MISMATCH;
    }

    wchar_t class_name[32] = {};
    if (GetClassNameW(
            window,
            class_name,
            static_cast<int>(
                sizeof(class_name) / sizeof(class_name[0]))) <= 0 ||
        lstrcmpW(class_name, L"CabinetWClass") != 0) {
        return JARVIS_TAP_TARGET_RESULT_WINDOW_IDENTITY_MISMATCH;
    }

    if (GetWindowTextLengthW(window) != 3) {
        return JARVIS_TAP_TARGET_RESULT_WINDOW_IDENTITY_MISMATCH;
    }
    wchar_t window_title[4] = {};
    if (GetWindowTextW(
            window,
            window_title,
            static_cast<int>(
                sizeof(window_title) / sizeof(window_title[0]))) != 3 ||
        lstrcmpW(window_title, L"C:\\") != 0) {
        return JARVIS_TAP_TARGET_RESULT_WINDOW_IDENTITY_MISMATCH;
    }

    FILETIME creation_time{};
    FILETIME exit_time{};
    FILETIME kernel_time{};
    FILETIME user_time{};
    if (!GetProcessTimes(
            GetCurrentProcess(),
            &creation_time,
            &exit_time,
            &kernel_time,
            &user_time) ||
        FileTimeToUInt64(creation_time) !=
            target.process_start_time_utc_ticks) {
        return JARVIS_TAP_TARGET_RESULT_PROCESS_START_MISMATCH;
    }

    return JARVIS_TAP_TARGET_RESULT_ACCEPTED;
}
