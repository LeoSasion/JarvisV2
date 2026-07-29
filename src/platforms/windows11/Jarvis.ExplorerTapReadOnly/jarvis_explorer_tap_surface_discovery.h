#pragma once

#include "jarvis_explorer_tap_xaml_read_bridge.h"

#include <cstdint>

struct IUnknown;

#ifndef JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK
#define JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK 0
#endif

#if JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK != 0 && \
    JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK != 1
#error JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK must be zero or one.
#endif

inline constexpr std::uint32_t
    JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT = 512U;
inline constexpr std::uint32_t
    JARVIS_TAP_DISCOVERY_MAX_EVENT_COUNT = 2048U;
inline constexpr std::uint32_t
    JARVIS_TAP_DISCOVERY_MAX_DEPTH = 64U;

using jarvis_tap_discovery_state = std::uint32_t;
inline constexpr jarvis_tap_discovery_state
    JARVIS_TAP_DISCOVERY_STATE_DISABLED = 0U;
inline constexpr jarvis_tap_discovery_state
    JARVIS_TAP_DISCOVERY_STATE_COLLECTING = 1U;
inline constexpr jarvis_tap_discovery_state
    JARVIS_TAP_DISCOVERY_STATE_COMPLETE = 2U;
inline constexpr jarvis_tap_discovery_state
    JARVIS_TAP_DISCOVERY_STATE_BLOCKED = 3U;

using jarvis_tap_discovery_result = std::uint32_t;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_REVIEW_OBJECT_DISABLED = 0U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_COMPLETE = 2U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT = 3U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SIZE_MISMATCH = 4U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_ABI_MISMATCH = 5U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID = 6U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_CAPABILITY_NOT_CURRENT = 7U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SELECTOR_PROFILE_MISMATCH = 8U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID = 9U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SEQUENCE_INVALID = 10U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID = 11U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_NODE_CAPACITY_EXCEEDED = 12U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_HANDLE_REPLAY = 13U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_REMOVE_UNKNOWN = 14U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_ORPHAN = 15U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_CYCLE = 16U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_DEPTH_EXCEEDED = 17U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE = 18U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SURFACE_COLLISION = 19U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_SLOT_INVALID = 20U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_CALLBACK_CONCURRENT = 21U;
inline constexpr jarvis_tap_discovery_result
    JARVIS_TAP_DISCOVERY_RESULT_FOREIGN_EXCEPTION = 22U;

using jarvis_tap_visual_mutation = std::uint32_t;
inline constexpr jarvis_tap_visual_mutation
    JARVIS_TAP_VISUAL_MUTATION_ADD = 0U;
inline constexpr jarvis_tap_visual_mutation
    JARVIS_TAP_VISUAL_MUTATION_REMOVE = 1U;

using jarvis_tap_visual_type = std::uint32_t;
inline constexpr jarvis_tap_visual_type
    JARVIS_TAP_VISUAL_TYPE_OTHER = 0U;
inline constexpr jarvis_tap_visual_type
    JARVIS_TAP_VISUAL_TYPE_TAB_CONTROL = 1U;
inline constexpr jarvis_tap_visual_type
    JARVIS_TAP_VISUAL_TYPE_COMMAND_BAR_CONTROL = 2U;
inline constexpr jarvis_tap_visual_type
    JARVIS_TAP_VISUAL_TYPE_GRID = 3U;
inline constexpr jarvis_tap_visual_type
    JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW = 4U;

using jarvis_tap_visual_name = std::uint32_t;
inline constexpr jarvis_tap_visual_name
    JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER = 0U;
inline constexpr jarvis_tap_visual_name
    JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID = 1U;
inline constexpr jarvis_tap_visual_name
    JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID = 2U;

struct jarvis_tap_visual_tree_event final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_tap_visual_mutation mutation;
    jarvis_tap_visual_type type;
    jarvis_tap_visual_name name;
    std::uint32_t child_index;
    std::uint64_t parent_handle;
    std::uint64_t child_handle;
    std::uint64_t instance_handle;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

struct jarvis_tap_discovery_node final {
    std::uint64_t instance_handle;
    std::uint64_t parent_handle;
    std::uint32_t child_index;
    jarvis_tap_visual_type type;
    jarvis_tap_visual_name name;
    std::uint32_t present;
    std::uint32_t reserved;
    std::uint32_t reserved2;
};

struct jarvis_tap_surface_discovery_instance final {
    jarvis_tap_discovery_state state;
    jarvis_tap_discovery_result last_result;
    std::uint64_t next_sequence;
    std::uint32_t event_count;
    std::uint32_t node_count;
    std::uint32_t present_node_count;
    std::uint32_t reserved;
    jarvis_transport_target_identity target;
    jarvis_transport_hash256 expected_selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    std::uint64_t surface_instance_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_tap_discovery_node nodes[
        JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT];
};

struct jarvis_tap_surface_discovery_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_tap_discovery_state state;
    jarvis_tap_discovery_result result;
    std::uint64_t next_sequence;
    std::uint32_t event_count;
    std::uint32_t node_count;
    std::uint32_t present_node_count;
    std::uint32_t matched_surface_count;
    std::uint64_t surface_instance_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    std::uint32_t read_request_count;
    std::uint32_t review_callback_compiled;
    std::uint32_t diagnostics_site_touched;
    std::uint32_t callback_subscription_attempted;
    std::uint32_t property_read_attempted;
    std::uint32_t property_write_supported;
    std::uint32_t execution_supported;
    std::uint32_t ready_for_live_connection;
    std::uint32_t ready_for_exact_approval;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_tap_visual_tree_event) == 64U);
static_assert(sizeof(jarvis_tap_discovery_node) == 40U);
static_assert(sizeof(jarvis_tap_surface_discovery_response) == 120U);

void jarvis_tap_surface_discovery_reset(
    jarvis_tap_surface_discovery_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_query_contract() noexcept;

[[nodiscard]] jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_bind(
    jarvis_tap_surface_discovery_instance* instance,
    const jarvis_tap_admission_instance* admission,
    std::uint64_t now_monotonic_ms) noexcept;

[[nodiscard]] jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_ingest(
    jarvis_tap_surface_discovery_instance* instance,
    const jarvis_tap_visual_tree_event* event) noexcept;

[[nodiscard]] jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_finalize(
    jarvis_tap_surface_discovery_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_query(
    const jarvis_tap_surface_discovery_instance* instance) noexcept;

[[nodiscard]] jarvis_tap_discovery_result
jarvis_tap_surface_discovery_build_read_request(
    const jarvis_tap_surface_discovery_instance* instance,
    std::uint32_t read_slot,
    jarvis_tap_xaml_read_request* request) noexcept;

void jarvis_tap_surface_discovery_fail_closed(
    jarvis_tap_surface_discovery_instance* instance,
    jarvis_tap_discovery_result result) noexcept;

[[nodiscard]] long
jarvis_tap_create_surface_discovery_callback_review(
    jarvis_tap_surface_discovery_instance* instance,
    IUnknown** output) noexcept;
