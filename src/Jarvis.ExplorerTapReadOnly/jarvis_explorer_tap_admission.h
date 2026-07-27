#pragma once

#include "jarvis_explorer_tap_readonly.h"

#include <cstdint>

using jarvis_tap_admission_state = std::uint32_t;
inline constexpr jarvis_tap_admission_state
    JARVIS_TAP_ADMISSION_STATE_COLD = 0U;
inline constexpr jarvis_tap_admission_state
    JARVIS_TAP_ADMISSION_STATE_ADMITTED = 1U;
inline constexpr jarvis_tap_admission_state
    JARVIS_TAP_ADMISSION_STATE_BLOCKED = 2U;

using jarvis_tap_admission_result = std::uint32_t;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_MODEL_ONLY = 0U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_INVALID_ARGUMENT = 2U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_SIZE_MISMATCH = 3U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_ABI_MISMATCH = 4U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_BIND_INVALID = 5U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID = 6U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_CAPABILITY_NOT_CURRENT = 7U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_EXISTING_CONSUMER = 8U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_ENDPOINT_COUNT_INVALID = 9U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_TAP_EXPORT_SET_INVALID = 10U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_IMPORT_POLICY_FAILED = 11U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_RECOVERY_NOT_READY = 12U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_PLAN_UNAVAILABLE = 13U;
inline constexpr jarvis_tap_admission_result
    JARVIS_TAP_ADMISSION_RESULT_REPLAY = 14U;

struct jarvis_tap_admission_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_transport_bind_request bind;
    jarvis_transport_hash256 controller_sha256;
    jarvis_transport_hash256 tap_dll_sha256;
    jarvis_transport_hash256 xaml_diagnostics_sha256;
    jarvis_transport_hash256 endpoint_name_sha256;
    std::uint64_t evaluated_at_monotonic_ms;
    std::uint32_t observed_consumer_count;
    std::uint32_t endpoint_candidate_count;
    std::uint32_t tap_export_count;
    std::uint32_t import_policy_passed;
    std::uint32_t binary_identity_passed;
    std::uint32_t recovery_ready;
    std::uint32_t one_shot_plan_available;
    std::uint32_t reserved;
};

struct jarvis_tap_admission_instance final {
    jarvis_tap_admission_state state;
    std::uint32_t attempt_count;
    std::uint32_t plan_consumed;
    std::uint32_t reserved;
    jarvis_transport_bind_request bind;
    jarvis_transport_hash256 controller_sha256;
    jarvis_transport_hash256 tap_dll_sha256;
    jarvis_transport_hash256 xaml_diagnostics_sha256;
    jarvis_transport_hash256 endpoint_name_sha256;
    std::uint64_t evaluated_at_monotonic_ms;
};

struct jarvis_tap_admission_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_admission_state state;
    jarvis_tap_admission_result result;
    std::uint32_t attempt_count;
    std::uint32_t plan_consumed;
    std::uint32_t observed_consumer_count;
    std::uint32_t endpoint_candidate_count;
    std::uint32_t binary_identity_accepted;
    std::uint32_t recovery_ready;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_tap_admission_request) == 792U);
static_assert(sizeof(jarvis_tap_admission_instance) == 768U);
static_assert(sizeof(jarvis_tap_admission_response) == 60U);

void jarvis_tap_admission_reset(
    jarvis_tap_admission_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_admission_response
jarvis_tap_admission_query_contract() noexcept;

[[nodiscard]] jarvis_tap_admission_response
jarvis_tap_admission_evaluate(
    jarvis_tap_admission_instance* instance,
    const jarvis_tap_admission_request* request) noexcept;

[[nodiscard]] jarvis_tap_admission_response
jarvis_tap_admission_query(
    const jarvis_tap_admission_instance* instance) noexcept;
