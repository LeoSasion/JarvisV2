#pragma once

#include <cstdint>

// Host-side lifecycle contract for one pre-admitted Explorer UI thread. This
// component owns neither target discovery nor DLL loading. The Win32 adapter
// is compiled for review but is not linked into a runnable controller.

inline constexpr std::uint32_t
    JARVIS_EXACT_THREAD_TRANSPORT_ABI_VERSION = 1U;
inline constexpr std::uint32_t JARVIS_EXACT_THREAD_SCOPE = 1U;

using jarvis_exact_thread_transport_state = std::uint32_t;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_COLD = 0U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_READY = 1U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_INSTALLING = 2U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_ACTIVE = 3U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_DRAINING = 4U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_QUIESCED = 5U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_BLOCKED = 6U;
inline constexpr jarvis_exact_thread_transport_state
    JARVIS_EXACT_THREAD_STATE_FAULTED = 7U;

using jarvis_exact_thread_transport_result = std::uint32_t;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_SUCCESS = 0U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT = 1U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_SIZE_MISMATCH = 2U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_ABI_MISMATCH = 3U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED = 4U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH = 5U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_BRIDGE_NOT_READY = 6U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT = 7U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_TARGET_VALIDATION_FAILED = 8U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED = 9U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_UNHOOK_FAILED = 10U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING = 11U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_QUIESCED = 12U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_BLOCKED = 13U;
inline constexpr jarvis_exact_thread_transport_result
    JARVIS_EXACT_THREAD_RESULT_FAULTED = 14U;

struct jarvis_exact_thread_transport_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint32_t explorer_process_id;
    std::uint32_t shell_thread_id;
    std::uint64_t shell_window_handle;
    std::uint64_t module_handle;
    std::uint64_t hook_procedure;
    std::uint64_t session_nonce;
    std::uint32_t host_admission_passed;
    std::uint32_t kill_switch_armed;
    std::uint32_t one_shot_permit_valid;
    std::uint32_t transport_scope;
    std::uint32_t architecture_match;
    std::uint32_t reserved0;
    std::uint32_t reserved1;
    std::uint32_t reserved2;
};

struct jarvis_exact_thread_transport_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_exact_thread_transport_state state;
    jarvis_exact_thread_transport_result result;
    std::uint32_t explorer_process_id;
    std::uint32_t shell_thread_id;
    std::uint32_t prepare_attempt_count;
    std::uint32_t install_attempt_count;
    std::uint32_t unhook_attempt_count;
    std::uint32_t target_validation_count;
    std::uint32_t hook_entry_published;
    std::uint32_t hook_removed;
    std::uint32_t pass_through;
    std::uint32_t module_pin_required;
    std::uint32_t unload_permitted;
    std::uint32_t live_explorer_touched;
    std::uint32_t mutation_performed;
    std::uint32_t activation_permitted;
    std::uint32_t last_platform_error;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_exact_thread_transport_request) == 80U);
static_assert(sizeof(jarvis_exact_thread_transport_response) == 80U);
