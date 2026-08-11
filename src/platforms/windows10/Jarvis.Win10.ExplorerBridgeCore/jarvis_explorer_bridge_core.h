#pragma once

#include <cstdint>

// Windows 10-owned fork of the reviewed standalone Explorer bridge ABI. The
// core owns lifecycle policy only; target discovery, exact-host admission and
// Hook installation remain in the separately reviewed Win10 collector stack.

inline constexpr std::uint32_t JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION = 3U;
inline constexpr std::uint32_t JARVIS_EXPLORER_BRIDGE_TRANSPORT_SCOPE_EXACT_THREAD =
    1U;

using jarvis_bridge_core_state = std::uint32_t;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_COLD = 0U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_PREPARING = 1U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_READY = 2U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_ACTIVE = 3U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_DRAINING = 4U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_QUIESCED = 5U;
inline constexpr jarvis_bridge_core_state JARVIS_BRIDGE_CORE_STATE_BLOCKED = 6U;

using jarvis_bridge_core_result = std::uint32_t;
inline constexpr jarvis_bridge_core_result JARVIS_BRIDGE_CORE_RESULT_SUCCESS = 0U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_CORE_ONLY_NO_TRANSPORT = 1U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT = 2U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_ABI_MISMATCH = 3U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_SIZE_MISMATCH = 4U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID = 5U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED = 6U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_ALREADY_INITIALIZED = 7U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_STATE_CONFLICT = 8U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_TRANSPORT_IDENTITY_MISMATCH = 9U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED = 10U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING = 11U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_QUIESCED = 12U;
inline constexpr jarvis_bridge_core_result
    JARVIS_BRIDGE_CORE_RESULT_BLOCKED = 13U;

struct jarvis_bridge_core_init_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint32_t explorer_process_id;
    std::uint32_t shell_thread_id;
    std::uint64_t session_nonce;
    std::uint32_t host_admission_passed;
    std::uint32_t kill_switch_armed;
    std::uint32_t one_shot_permit_valid;
    std::uint32_t transport_scope;
    std::uint8_t settings_sha256[32];
    std::uint32_t reserved0;
    std::uint32_t reserved1;
};

struct jarvis_bridge_core_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_bridge_core_state state;
    jarvis_bridge_core_result result;
    std::uint32_t active_callback_count;
    std::uint32_t pass_through;
    std::uint32_t external_entry_published;
    std::uint32_t module_pin_required;
    std::uint32_t unload_permitted;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t initialize_attempt_count;
    std::uint32_t rejected_callback_count;
    std::uint32_t accepted_callback_count;
    std::uint32_t generation;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_bridge_core_init_request) == 80U);
static_assert(sizeof(jarvis_bridge_core_response) == 68U);

#if defined(_WIN32) && !defined(JARVIS_BRIDGE_CORE_STATIC)
#define JARVIS_BRIDGE_CORE_API extern "C" __declspec(dllexport)
#define JARVIS_BRIDGE_CORE_CALL __cdecl
#else
#define JARVIS_BRIDGE_CORE_API extern "C"
#define JARVIS_BRIDGE_CORE_CALL
#endif

JARVIS_BRIDGE_CORE_API jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL
JarvisBridge_QueryContract(jarvis_bridge_core_response* response) noexcept;

JARVIS_BRIDGE_CORE_API jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL
JarvisBridge_Initialize(
    const jarvis_bridge_core_init_request* request,
    jarvis_bridge_core_response* response) noexcept;

JARVIS_BRIDGE_CORE_API jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL
JarvisBridge_Quiesce(jarvis_bridge_core_response* response) noexcept;

JARVIS_BRIDGE_CORE_API jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL
JarvisBridge_QueryState(jarvis_bridge_core_response* response) noexcept;

// The exact-target collector uses this opaque pointer only with the reviewed
// bridge-core functions linked into the collector. The callback DLL and
// collector therefore operate on the same shared-section instance without
// exporting its internal layout as part of the public ABI.
JARVIS_BRIDGE_CORE_API void* JARVIS_BRIDGE_CORE_CALL
JarvisBridge_AcquireSharedInstance() noexcept;
