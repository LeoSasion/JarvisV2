#pragma once

#include "jarvis_explorer_bridge_core.h"

#include <atomic>
#include <cstdint>

struct jarvis_bridge_callback_token final {
    std::uint64_t session_nonce;
    std::uint32_t generation;
    std::uint32_t acquired;
};

struct jarvis_bridge_core_instance final {
    std::atomic<std::uint32_t> state{JARVIS_BRIDGE_CORE_STATE_COLD};
    std::atomic<std::uint32_t> active_callback_count{0U};
    std::atomic<std::uint32_t> pass_through{1U};
    std::atomic<std::uint32_t> external_entry_published{0U};
    std::atomic<std::uint32_t> initialize_attempt_count{0U};
    std::atomic<std::uint32_t> rejected_callback_count{0U};
    std::atomic<std::uint32_t> generation{0U};
    std::atomic<std::uint32_t> live_explorer_touched{0U};
    std::uint32_t explorer_process_id{0U};
    std::uint32_t shell_thread_id{0U};
    std::uint64_t session_nonce{0U};
    std::uint8_t settings_sha256[32]{};
};

void jarvis_bridge_core_reset_for_test(
    jarvis_bridge_core_instance* instance) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_prepare(
    jarvis_bridge_core_instance* instance,
    const jarvis_bridge_core_init_request* request,
    jarvis_bridge_core_response* response) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_publish_transport(
    jarvis_bridge_core_instance* instance,
    std::uint32_t explorer_process_id,
    std::uint32_t shell_thread_id,
    std::uint64_t session_nonce,
    std::uint32_t live_explorer_touched,
    jarvis_bridge_core_response* response) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_try_enter_callback(
    jarvis_bridge_core_instance* instance,
    std::uint32_t observed_process_id,
    std::uint32_t observed_thread_id,
    jarvis_bridge_callback_token* token,
    jarvis_bridge_core_response* response) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_leave_callback(
    jarvis_bridge_core_instance* instance,
    jarvis_bridge_callback_token* token,
    jarvis_bridge_core_response* response) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_begin_quiesce(
    jarvis_bridge_core_instance* instance,
    jarvis_bridge_core_response* response) noexcept;

[[nodiscard]] jarvis_bridge_core_result jarvis_bridge_core_query(
    const jarvis_bridge_core_instance* instance,
    jarvis_bridge_core_response* response) noexcept;
