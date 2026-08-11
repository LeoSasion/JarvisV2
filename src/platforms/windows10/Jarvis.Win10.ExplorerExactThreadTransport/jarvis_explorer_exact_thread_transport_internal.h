#pragma once

#include "jarvis_explorer_exact_thread_transport.h"
#include "../Jarvis.Win10.ExplorerBridgeCore/jarvis_explorer_bridge_core_internal.h"

#include <atomic>
#include <cstdint>

inline constexpr std::uint32_t JARVIS_TRANSPORT_EXECUTION_SYNTHETIC = 0U;
inline constexpr std::uint32_t JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE = 1U;

using jarvis_validate_exact_target_fn = std::uint32_t (*)(
    void* context,
    std::uint32_t explorer_process_id,
    std::uint32_t shell_thread_id,
    std::uint64_t shell_window_handle,
    std::uint32_t* platform_error) noexcept;

using jarvis_install_exact_thread_hook_fn = std::uint32_t (*)(
    void* context,
    std::uint32_t shell_thread_id,
    std::uint64_t module_handle,
    std::uint64_t hook_procedure,
    std::uint64_t* hook_handle,
    std::uint32_t* platform_error) noexcept;

using jarvis_remove_exact_thread_hook_fn = std::uint32_t (*)(
    void* context,
    std::uint64_t hook_handle,
    std::uint32_t* platform_error) noexcept;

struct jarvis_exact_thread_platform_api final {
    std::uint32_t size;
    std::uint32_t execution_kind;
    void* context;
    jarvis_validate_exact_target_fn validate_exact_target;
    jarvis_install_exact_thread_hook_fn install_exact_thread_hook;
    jarvis_remove_exact_thread_hook_fn remove_exact_thread_hook;
};

struct jarvis_exact_thread_transport_instance final {
    std::atomic<std::uint32_t> state{JARVIS_EXACT_THREAD_STATE_COLD};
    std::atomic<std::uint32_t> prepare_attempt_count{0U};
    std::atomic<std::uint32_t> install_attempt_count{0U};
    std::atomic<std::uint32_t> unhook_attempt_count{0U};
    std::atomic<std::uint32_t> target_validation_count{0U};
    std::atomic<std::uint32_t> hook_entry_published{0U};
    std::atomic<std::uint32_t> hook_removed{0U};
    std::atomic<std::uint32_t> live_explorer_touched{0U};
    std::atomic<std::uint32_t> last_platform_error{0U};
    std::atomic<std::uint32_t> cancel_requested{0U};
    std::atomic<std::uint32_t> install_in_flight{0U};
    std::atomic<std::uint32_t> unhook_started{0U};
    std::atomic<std::uint64_t> hook_handle{0U};
    jarvis_exact_thread_platform_api platform{};
    jarvis_bridge_core_instance* bridge{nullptr};
    std::uint32_t explorer_process_id{0U};
    std::uint32_t shell_thread_id{0U};
    std::uint64_t shell_window_handle{0U};
    std::uint64_t module_handle{0U};
    std::uint64_t hook_procedure{0U};
    std::uint64_t session_nonce{0U};
};

void jarvis_exact_thread_transport_reset_for_test(
    jarvis_exact_thread_transport_instance* instance) noexcept;

[[nodiscard]] jarvis_exact_thread_transport_result
jarvis_exact_thread_transport_prepare(
    jarvis_exact_thread_transport_instance* instance,
    const jarvis_exact_thread_transport_request* request,
    const jarvis_exact_thread_platform_api* platform,
    jarvis_bridge_core_instance* bridge,
    jarvis_exact_thread_transport_response* response) noexcept;

[[nodiscard]] jarvis_exact_thread_transport_result
jarvis_exact_thread_transport_install(
    jarvis_exact_thread_transport_instance* instance,
    jarvis_exact_thread_transport_response* response) noexcept;

[[nodiscard]] jarvis_exact_thread_transport_result
jarvis_exact_thread_transport_quiesce(
    jarvis_exact_thread_transport_instance* instance,
    jarvis_exact_thread_transport_response* response) noexcept;

[[nodiscard]] jarvis_exact_thread_transport_result
jarvis_exact_thread_transport_poll(
    jarvis_exact_thread_transport_instance* instance,
    jarvis_exact_thread_transport_response* response) noexcept;

[[nodiscard]] jarvis_exact_thread_platform_api
jarvis_exact_thread_windows_platform_api() noexcept;
