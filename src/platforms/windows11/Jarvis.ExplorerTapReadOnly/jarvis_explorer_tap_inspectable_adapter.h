#pragma once

#include "jarvis_explorer_tap_fingerprint.h"

#include <cstdint>

#ifndef JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ
#define JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ 0
#endif

#if JARVIS_ENABLE_LIVE_XAML_PROPERTY_READ != 0
#error Phase 14 must be compiled with live IInspectable property reads disabled.
#endif

using jarvis_tap_adapter_state = std::uint32_t;
inline constexpr jarvis_tap_adapter_state
    JARVIS_TAP_ADAPTER_STATE_COLD = 0U;
inline constexpr jarvis_tap_adapter_state
    JARVIS_TAP_ADAPTER_STATE_READY = 1U;
inline constexpr jarvis_tap_adapter_state
    JARVIS_TAP_ADAPTER_STATE_COLLECTING = 2U;
inline constexpr jarvis_tap_adapter_state
    JARVIS_TAP_ADAPTER_STATE_COMPLETE = 3U;
inline constexpr jarvis_tap_adapter_state
    JARVIS_TAP_ADAPTER_STATE_BLOCKED = 4U;

using jarvis_tap_adapter_result = std::uint32_t;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_MODEL_ONLY = 0U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_COMPLETE = 2U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT = 3U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID = 4U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_SIZE_MISMATCH = 5U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_ABI_MISMATCH = 6U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_VALUE_ORIGIN_UNSUPPORTED = 7U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_RUNTIME_KIND_UNSUPPORTED = 8U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNSUPPORTED = 9U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNVERIFIED = 10U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL = 11U;
inline constexpr jarvis_tap_adapter_result
    JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED = 12U;

using jarvis_tap_property_value_origin = std::uint32_t;
inline constexpr jarvis_tap_property_value_origin
    JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL = 1U;

using jarvis_tap_runtime_value_kind = std::uint32_t;
inline constexpr jarvis_tap_runtime_value_kind
    JARVIS_TAP_RUNTIME_VALUE_NULL = 0U;
inline constexpr jarvis_tap_runtime_value_kind
    JARVIS_TAP_RUNTIME_VALUE_OBJECT = 1U;

using jarvis_tap_runtime_class = std::uint32_t;
inline constexpr jarvis_tap_runtime_class
    JARVIS_TAP_RUNTIME_CLASS_NONE = 0U;
inline constexpr jarvis_tap_runtime_class
    JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH = 1U;

struct jarvis_tap_canonical_property_value final {
    jarvis_tap_property_value_kind value_kind;
    std::uint32_t argb;
    std::uint32_t opacity_millionths;
    std::uint32_t reserved;
};

struct jarvis_tap_runtime_property_snapshot final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_transport_target_identity target;
    std::uint32_t surface_slot;
    std::uint32_t property_slot;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 selector_sha256;
    jarvis_tap_property_value_origin value_origin;
    jarvis_tap_runtime_value_kind runtime_value_kind;
    jarvis_tap_runtime_class runtime_class;
    std::uint32_t exact_runtime_class_name_matched;
    std::uint32_t argb;
    std::uint32_t opacity_millionths;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

struct jarvis_tap_inspectable_adapter_instance final {
    jarvis_tap_adapter_state state;
    std::uint32_t canonical_value_count;
    jarvis_tap_canonical_property_value canonical_values[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    jarvis_tap_fingerprint_instance fingerprint;
};

struct jarvis_tap_inspectable_adapter_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_adapter_state state;
    jarvis_tap_adapter_result result;
    std::uint32_t canonical_value_count;
    std::uint32_t forwarded_to_fingerprint;
    jarvis_tap_canonical_property_value canonical_value;
    jarvis_tap_fingerprint_response fingerprint;
    std::uint32_t adapter_model_supported;
    std::uint32_t property_read_supported;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

static_assert(sizeof(jarvis_tap_canonical_property_value) == 16U);
static_assert(sizeof(jarvis_tap_runtime_property_snapshot) == 192U);
static_assert(sizeof(jarvis_tap_inspectable_adapter_instance) == 680U);
static_assert(sizeof(jarvis_tap_inspectable_adapter_response) == 168U);

void jarvis_tap_inspectable_adapter_reset(
    jarvis_tap_inspectable_adapter_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_query_contract() noexcept;

[[nodiscard]] jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_bind(
    jarvis_tap_inspectable_adapter_instance* instance,
    const jarvis_tap_admission_instance* admission,
    const jarvis_transport_bind_request* bind) noexcept;

[[nodiscard]] jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_observe(
    jarvis_tap_inspectable_adapter_instance* instance,
    const jarvis_tap_runtime_property_snapshot* snapshot) noexcept;

[[nodiscard]] jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_query(
    const jarvis_tap_inspectable_adapter_instance* instance) noexcept;
