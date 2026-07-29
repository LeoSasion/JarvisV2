#pragma once

#include <cstdint>

// This is an offline contract model, not a loader or Explorer module.
// All fields use fixed-width types so a later native implementation can be
// reviewed against one explicit, versioned boundary.

inline constexpr std::uint32_t JARVIS_EXPLORER_BRIDGE_ABI_VERSION = 1U;

using jarvis_bridge_state = std::uint32_t;
inline constexpr jarvis_bridge_state JARVIS_BRIDGE_STATE_COLD = 0U;
inline constexpr jarvis_bridge_state JARVIS_BRIDGE_STATE_BLOCKED = 1U;
inline constexpr jarvis_bridge_state JARVIS_BRIDGE_STATE_QUIESCED = 2U;

using jarvis_bridge_result = std::uint32_t;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED = 0U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_ABI_MISMATCH = 1U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_REQUEST_SIZE_MISMATCH = 2U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_IDENTITY_INVALID = 3U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_ALREADY_INITIALIZED = 4U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_QUIESCED = 5U;
inline constexpr jarvis_bridge_result JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT = 6U;

struct jarvis_bridge_init_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint32_t explorer_process_id;
    std::uint32_t shell_thread_id;
    std::uint64_t session_nonce;
};

struct jarvis_bridge_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_bridge_state state;
    jarvis_bridge_result result;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
};

struct jarvis_bridge_model_instance final {
    jarvis_bridge_state state;
    std::uint32_t initialize_attempt_count;
};

static_assert(sizeof(jarvis_bridge_init_request) == 24U);
static_assert(sizeof(jarvis_bridge_response) == 32U);
static_assert(sizeof(jarvis_bridge_model_instance) == 8U);

void jarvis_bridge_model_reset(jarvis_bridge_model_instance* instance) noexcept;

[[nodiscard]] jarvis_bridge_response
jarvis_bridge_model_query_contract() noexcept;

[[nodiscard]] jarvis_bridge_response jarvis_bridge_model_initialize(
    jarvis_bridge_model_instance* instance,
    const jarvis_bridge_init_request* request) noexcept;

[[nodiscard]] jarvis_bridge_response jarvis_bridge_model_quiesce(
    jarvis_bridge_model_instance* instance) noexcept;

[[nodiscard]] jarvis_bridge_response jarvis_bridge_model_query(
    const jarvis_bridge_model_instance* instance) noexcept;
