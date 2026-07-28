#pragma once

#include "jarvis_explorer_tap_inspectable_adapter.h"

#include <cstdint>

struct IUnknown;

#ifndef JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE
#define JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE 0
#endif

#if JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE != 0 && \
    JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE != 1
#error JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE must be zero or one.
#endif

inline constexpr std::uint32_t
    JARVIS_TAP_XAML_READ_MAX_PROPERTY_SOURCE_COUNT = 128U;
inline constexpr std::uint32_t
    JARVIS_TAP_XAML_READ_MAX_PROPERTY_VALUE_COUNT = 512U;
inline constexpr std::uint64_t
    JARVIS_TAP_XAML_METADATA_IS_VALUE_HANDLE = 0x1ULL;
inline constexpr std::uint64_t
    JARVIS_TAP_XAML_METADATA_IS_VALUE_NULL = 0x20ULL;
inline constexpr std::uint64_t
    JARVIS_TAP_XAML_METADATA_IS_VALUE_HANDLE_AND_EVALUATED_VALUE = 0x40ULL;
inline constexpr std::uint64_t
    JARVIS_TAP_XAML_METADATA_KNOWN_MASK = 0x7FULL;

using jarvis_tap_xaml_read_state = std::uint32_t;
inline constexpr jarvis_tap_xaml_read_state
    JARVIS_TAP_XAML_READ_STATE_DISABLED = 0U;
inline constexpr jarvis_tap_xaml_read_state
    JARVIS_TAP_XAML_READ_STATE_PREFLIGHT = 1U;
inline constexpr jarvis_tap_xaml_read_state
    JARVIS_TAP_XAML_READ_STATE_TARGET_ACCEPTED = 2U;
inline constexpr jarvis_tap_xaml_read_state
    JARVIS_TAP_XAML_READ_STATE_OBSERVED = 3U;
inline constexpr jarvis_tap_xaml_read_state
    JARVIS_TAP_XAML_READ_STATE_BLOCKED = 4U;

using jarvis_tap_xaml_read_result = std::uint32_t;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_REVIEW_OBJECT_DISABLED = 0U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PREFLIGHT_ACCEPTED = 1U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_TARGET_ACCEPTED = 2U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_OBSERVATION_ACCEPTED = 3U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT = 4U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH = 5U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH = 6U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID = 7U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_CAPABILITY_NOT_CURRENT = 8U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SEQUENCE_INVALID = 9U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SLOT_INVALID = 10U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_INSTANCE_INVALID = 11U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SELECTOR_MISMATCH = 12U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_TARGET_REJECTED = 13U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SITE_QUERY_FAILED = 14U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_SERVICE_QUERY_FAILED = 15U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_CHAIN_FAILED = 16U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_COUNT_INVALID = 17U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_NOT_UNIQUE = 18U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_SOURCE_INVALID = 19U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_ORIGIN_UNSUPPORTED = 20U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_METADATA_UNSUPPORTED = 21U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_PROPERTY_HANDLE_FAILED = 22U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_INSPECTABLE_FAILED = 23U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_RUNTIME_CLASS_UNSUPPORTED = 24U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_BRUSH_READ_FAILED = 25U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL = 26U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_FOREIGN_OUTCOME_UNCERTAIN = 27U;
inline constexpr jarvis_tap_xaml_read_result
    JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE = 28U;

struct jarvis_tap_xaml_read_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    std::uint32_t surface_slot;
    std::uint32_t property_slot;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 selector_sha256;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

struct jarvis_tap_xaml_foreign_observation final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint32_t site_query_succeeded;
    std::uint32_t service_query_succeeded;
    std::uint32_t property_chain_call_attempted;
    std::uint32_t property_chain_call_succeeded;
    std::uint32_t property_source_count;
    std::uint32_t property_value_count;
    std::uint32_t matched_property_count;
    std::uint32_t property_chain_index;
    std::uint32_t property_value_source;
    std::uint64_t property_metadata_bits;
    std::uint32_t property_handle_call_succeeded;
    std::uint32_t property_value_handle_nonzero;
    std::uint32_t inspectable_call_succeeded;
    jarvis_tap_runtime_value_kind runtime_value_kind;
    jarvis_tap_runtime_class runtime_class;
    std::uint32_t exact_runtime_class_name_matched;
    std::uint32_t brush_read_succeeded;
    std::uint32_t argb;
    std::uint32_t opacity_millionths;
    std::uint32_t release_attempt_count;
    std::uint32_t release_completed_count;
    std::uint32_t property_chain_free_required;
    std::uint32_t property_chain_freed;
    std::uint32_t foreign_outcome_uncertain;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

struct jarvis_tap_xaml_read_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_xaml_read_state state;
    jarvis_tap_xaml_read_result result;
    jarvis_tap_target_result target_result;
    std::uint32_t review_bridge_compiled;
    std::uint32_t diagnostics_site_touched;
    std::uint32_t property_read_attempted;
    std::uint32_t foreign_outcome_uncertain;
    std::uint32_t release_attempt_count;
    std::uint32_t release_completed_count;
    std::uint32_t property_chain_freed;
    std::uint32_t property_read_supported;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
    jarvis_tap_runtime_property_snapshot snapshot;
};

static_assert(sizeof(jarvis_tap_xaml_read_request) == 72U);
static_assert(sizeof(jarvis_tap_xaml_foreign_observation) == 120U);
static_assert(sizeof(jarvis_tap_xaml_read_response) == 264U);

[[nodiscard]] jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_query_contract() noexcept;

[[nodiscard]] jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_preflight(
    const jarvis_tap_admission_instance* admission,
    const jarvis_tap_xaml_read_request* request,
    std::uint64_t now_monotonic_ms) noexcept;

[[nodiscard]] jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_accept_target(
    const jarvis_tap_xaml_read_response* preflight,
    jarvis_tap_target_result target_result) noexcept;

[[nodiscard]] jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_complete(
    const jarvis_tap_admission_instance* admission,
    const jarvis_tap_xaml_read_request* request,
    const jarvis_tap_xaml_read_response* target_acceptance,
    const jarvis_tap_xaml_foreign_observation* observation,
    std::uint32_t live_explorer_touched) noexcept;

[[nodiscard]] jarvis_tap_xaml_read_response
jarvis_tap_windows_xaml_read_bridge_read(
    IUnknown* site,
    const jarvis_tap_admission_instance* admission,
    const jarvis_tap_xaml_read_request* request) noexcept;
