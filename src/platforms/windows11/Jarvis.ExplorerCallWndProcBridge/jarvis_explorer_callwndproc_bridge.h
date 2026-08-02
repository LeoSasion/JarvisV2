#pragma once

#include <cstdint>

inline constexpr std::uint32_t
    JARVIS_CALLWNDPROC_BRIDGE_ABI_VERSION = 1U;

using jarvis_callwndproc_result = std::uint32_t;
inline constexpr jarvis_callwndproc_result
    JARVIS_CALLWNDPROC_RESULT_PROCESSED = 0U;
inline constexpr jarvis_callwndproc_result
    JARVIS_CALLWNDPROC_RESULT_CHAINED_ONLY = 1U;
inline constexpr jarvis_callwndproc_result
    JARVIS_CALLWNDPROC_RESULT_ENTER_REJECTED = 2U;
inline constexpr jarvis_callwndproc_result
    JARVIS_CALLWNDPROC_RESULT_LEAVE_FAILED = 3U;
inline constexpr jarvis_callwndproc_result
    JARVIS_CALLWNDPROC_RESULT_CHAIN_UNAVAILABLE = 4U;

struct jarvis_callwndproc_receipt final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_callwndproc_result result;
    std::int32_t n_code;
    std::uint32_t observed_process_id;
    std::uint32_t observed_thread_id;
    std::uint32_t callback_entered;
    std::uint32_t callback_left;
    std::uint32_t chain_called;
    std::uint32_t body_called;
    std::uint32_t negative_code_bypassed;
    std::uint32_t entry_rejected;
    std::uint32_t bridge_state;
    std::uint32_t pass_through;
    std::uint32_t active_callback_count;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::int64_t chain_result;
};

static_assert(sizeof(jarvis_callwndproc_receipt) == 80U);
