#pragma once

#include "jarvis_explorer_tap_admission.h"

#include <cstdint>

using jarvis_tap_fingerprint_state = std::uint32_t;
inline constexpr jarvis_tap_fingerprint_state
    JARVIS_TAP_FINGERPRINT_STATE_COLD = 0U;
inline constexpr jarvis_tap_fingerprint_state
    JARVIS_TAP_FINGERPRINT_STATE_BOUND = 1U;
inline constexpr jarvis_tap_fingerprint_state
    JARVIS_TAP_FINGERPRINT_STATE_COLLECTING = 2U;
inline constexpr jarvis_tap_fingerprint_state
    JARVIS_TAP_FINGERPRINT_STATE_COMPLETE = 3U;
inline constexpr jarvis_tap_fingerprint_state
    JARVIS_TAP_FINGERPRINT_STATE_BLOCKED = 4U;

using jarvis_tap_fingerprint_result = std::uint32_t;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_MODEL_ONLY = 0U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE = 2U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT = 3U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_ADMISSION_INVALID = 4U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_BIND_INVALID = 5U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_STATE_INVALID = 6U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_SIZE_MISMATCH = 7U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_ABI_MISMATCH = 8U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_SEQUENCE_INVALID = 9U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_IDENTITY_DRIFT = 10U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_SLOT_INVALID = 11U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_SELECTOR_MISMATCH = 12U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID = 13U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED = 14U;
inline constexpr jarvis_tap_fingerprint_result
    JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL = 15U;

using jarvis_tap_property_value_kind = std::uint32_t;
inline constexpr jarvis_tap_property_value_kind
    JARVIS_TAP_PROPERTY_VALUE_NULL = 0U;
inline constexpr jarvis_tap_property_value_kind
    JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR = 1U;
inline constexpr std::uint32_t
    JARVIS_TAP_OPACITY_MILLIONTHS_MAX = 1000000U;

struct jarvis_tap_fingerprint_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_transport_target_identity target;
    std::uint32_t surface_slot;
    std::uint32_t property_slot;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 selector_sha256;
    jarvis_tap_property_value_kind value_kind;
    std::uint32_t argb;
    std::uint32_t opacity_millionths;
    std::uint32_t reserved;
};

struct jarvis_tap_fingerprint_instance final {
    jarvis_tap_fingerprint_state state;
    std::uint32_t observed_property_count;
    std::uint64_t next_sequence;
    jarvis_transport_target_identity target;
    std::uint64_t surface_instance_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 expected_selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 observed_fingerprint_sha256[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint32_t observed_mask;
    std::uint32_t reserved;
};

struct jarvis_tap_fingerprint_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_fingerprint_state state;
    jarvis_tap_fingerprint_result result;
    std::uint64_t next_sequence;
    std::uint32_t observed_property_count;
    std::uint32_t complete;
    jarvis_transport_hash256 last_fingerprint_sha256;
    std::uint32_t fingerprint_model_supported;
    std::uint32_t property_read_supported;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

static_assert(sizeof(jarvis_tap_fingerprint_request) == 176U);
static_assert(sizeof(jarvis_tap_fingerprint_instance) == 528U);
static_assert(sizeof(jarvis_tap_fingerprint_response) == 96U);

void jarvis_tap_fingerprint_reset(
    jarvis_tap_fingerprint_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_fingerprint_response
jarvis_tap_fingerprint_query_contract() noexcept;

[[nodiscard]] jarvis_tap_fingerprint_result
jarvis_tap_fingerprint_compute_canonical(
    const jarvis_tap_fingerprint_request* request,
    jarvis_transport_hash256* output) noexcept;

[[nodiscard]] jarvis_tap_fingerprint_response
jarvis_tap_fingerprint_bind(
    jarvis_tap_fingerprint_instance* instance,
    const jarvis_tap_admission_instance* admission,
    const jarvis_transport_bind_request* bind) noexcept;

[[nodiscard]] jarvis_tap_fingerprint_response
jarvis_tap_fingerprint_observe(
    jarvis_tap_fingerprint_instance* instance,
    const jarvis_tap_fingerprint_request* request) noexcept;

[[nodiscard]] jarvis_tap_fingerprint_response
jarvis_tap_fingerprint_query(
    const jarvis_tap_fingerprint_instance* instance) noexcept;
