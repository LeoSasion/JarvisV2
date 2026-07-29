#pragma once

#include "jarvis_explorer_tap_inspectable_adapter.h"

#include <cstdint>

#ifndef JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE
#define JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE 0
#endif

#if JARVIS_ENABLE_LIVE_XAML_PROPERTY_WRITE != 0
#error Phase 15 must be compiled with live XAML property writes disabled.
#endif

using jarvis_tap_style_transaction_state = std::uint32_t;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_COLD = 0U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED = 1U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLYING = 2U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLIED = 3U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED = 4U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORING = 5U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED = 6U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED = 7U;
inline constexpr jarvis_tap_style_transaction_state
    JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED = 8U;

using jarvis_tap_style_transaction_result = std::uint32_t;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY = 0U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_APPLIED = 2U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_REQUIRED = 3U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED = 4U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT = 5U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID = 6U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_SIZE_MISMATCH = 7U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_ABI_MISMATCH = 8U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_ADMISSION_INVALID = 9U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_SNAPSHOT_INCOMPLETE = 10U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_BIND_INVALID = 11U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_CAPABILITY_NOT_CURRENT = 12U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_DEADLINE_INVALID = 13U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_VALUE_INVALID = 14U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_HASH_MISMATCH = 15U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_NO_CHANGE = 16U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_SEQUENCE_INVALID = 17U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_IDENTITY_DRIFT = 18U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_SLOT_INVALID = 19U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_INSTANCE_INVALID = 20U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_SELECTOR_MISMATCH = 21U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED = 22U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED = 23U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED = 24U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH = 25U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_ORDER_INVALID = 26U;
inline constexpr jarvis_tap_style_transaction_result
    JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT = 27U;

struct jarvis_tap_style_plan_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_transport_bind_request bind;
    jarvis_tap_canonical_property_value styled_values[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint64_t prepared_at_monotonic_ms;
    std::uint64_t reserved;
};

struct jarvis_tap_style_step_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_transport_target_identity target;
    std::uint32_t surface_slot;
    std::uint32_t property_slot;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 selector_sha256;
    jarvis_tap_canonical_property_value observed_value;
};

struct jarvis_tap_style_transaction_instance final {
    jarvis_tap_style_transaction_state state;
    std::uint32_t next_apply_index;
    std::uint32_t dirty_mask;
    std::uint32_t verified_apply_count;
    std::uint32_t verified_restore_count;
    std::uint32_t simulated_write_attempt_count;
    std::uint32_t verification_count;
    std::uint32_t reserved;
    jarvis_transport_bind_request bind;
    std::uint64_t surface_instance_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_tap_canonical_property_value original_values[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    jarvis_tap_canonical_property_value styled_values[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint64_t preview_deadline_monotonic_ms;
    std::uint64_t next_sequence;
};

struct jarvis_tap_style_transaction_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_style_transaction_state state;
    jarvis_tap_style_transaction_result result;
    std::uint64_t next_sequence;
    std::uint32_t verified_apply_count;
    std::uint32_t verified_restore_count;
    std::uint32_t simulated_write_attempt_count;
    std::uint32_t dirty_property_count;
    std::uint32_t restore_required;
    std::uint32_t deadline_reached;
    std::uint32_t transaction_model_supported;
    std::uint32_t property_write_supported;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

static_assert(sizeof(jarvis_tap_style_plan_request) == 784U);
static_assert(sizeof(jarvis_tap_style_step_request) == 176U);
static_assert(sizeof(jarvis_tap_style_transaction_instance) == 1072U);
static_assert(sizeof(jarvis_tap_style_transaction_response) == 80U);

void jarvis_tap_style_transaction_reset(
    jarvis_tap_style_transaction_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_query_contract() noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_prepare(
    jarvis_tap_style_transaction_instance* instance,
    const jarvis_tap_admission_instance* admission,
    const jarvis_tap_inspectable_adapter_instance* adapter,
    const jarvis_tap_style_plan_request* request) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_record_apply(
    jarvis_tap_style_transaction_instance* instance,
    const jarvis_tap_style_step_request* request,
    std::uint32_t platform_write_attempted,
    std::uint32_t platform_write_succeeded,
    std::uint32_t verification_read_succeeded) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_require_restore(
    jarvis_tap_style_transaction_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_tick(
    jarvis_tap_style_transaction_instance* instance,
    std::uint64_t now_monotonic_ms) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_record_restore(
    jarvis_tap_style_transaction_instance* instance,
    const jarvis_tap_style_step_request* request,
    std::uint32_t platform_write_attempted,
    std::uint32_t platform_write_succeeded,
    std::uint32_t verification_read_succeeded) noexcept;

[[nodiscard]] jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_query(
    const jarvis_tap_style_transaction_instance* instance) noexcept;
