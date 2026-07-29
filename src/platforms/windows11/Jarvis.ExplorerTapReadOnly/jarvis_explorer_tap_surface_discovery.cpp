#include "jarvis_explorer_tap_surface_discovery.h"

#include <cstring>

namespace {

constexpr unsigned char kSelectorHashes
    [JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT][32] = {
        {
            0x05U, 0x4fU, 0x71U, 0xd3U, 0x3dU, 0x72U, 0x0aU, 0x35U,
            0x25U, 0xbfU, 0x1aU, 0xabU, 0x83U, 0x00U, 0x4dU, 0xe1U,
            0xd0U, 0x2fU, 0xbdU, 0xe3U, 0x06U, 0xb9U, 0xcdU, 0xe5U,
            0x04U, 0x47U, 0x05U, 0xb1U, 0xb9U, 0x82U, 0xb6U, 0x4bU,
        },
        {
            0xacU, 0x82U, 0xb7U, 0x52U, 0x9cU, 0xd2U, 0x69U, 0xb4U,
            0xc6U, 0x0dU, 0xc2U, 0x13U, 0x32U, 0xf9U, 0x34U, 0x10U,
            0x3aU, 0xb8U, 0xbbU, 0xcaU, 0x1bU, 0x2cU, 0xe2U, 0xeeU,
            0xedU, 0x6aU, 0xa5U, 0x03U, 0x3aU, 0x09U, 0x57U, 0x6fU,
        },
        {
            0xa5U, 0x8dU, 0xa1U, 0xacU, 0xe7U, 0xb8U, 0xbcU, 0x9bU,
            0x40U, 0x93U, 0xd6U, 0x2eU, 0xf6U, 0xfeU, 0xb7U, 0xe6U,
            0x8aU, 0xc0U, 0x5eU, 0x1cU, 0x6dU, 0xcfU, 0xebU, 0x10U,
            0xbbU, 0x40U, 0x3cU, 0xb7U, 0x66U, 0x98U, 0x26U, 0x2aU,
        },
    };

[[nodiscard]] bool HashEqual(
    const jarvis_transport_hash256& left,
    const jarvis_transport_hash256& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] bool HashIsZero(
    const jarvis_transport_hash256& value) noexcept {
    const jarvis_transport_hash256 zero{};
    return HashEqual(value, zero);
}

[[nodiscard]] bool SelectorHashMatches(
    const jarvis_transport_hash256& value,
    const std::uint32_t slot) noexcept {
    return slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT &&
           std::memcmp(
               &value,
               kSelectorHashes[slot],
               sizeof(value)) == 0;
}

[[nodiscard]] std::uint32_t MatchedSurfaceCount(
    const jarvis_tap_surface_discovery_instance* const instance) noexcept {
    if (instance == nullptr) {
        return 0U;
    }
    std::uint32_t count = 0U;
    for (const auto handle : instance->surface_instance_handles) {
        count += handle == 0ULL ? 0U : 1U;
    }
    return count;
}

[[nodiscard]] jarvis_tap_surface_discovery_response MakeResponse(
    const jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_discovery_state state,
    const jarvis_tap_discovery_result result) noexcept {
    jarvis_tap_surface_discovery_response response{
        .size = sizeof(jarvis_tap_surface_discovery_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = state,
        .result = result,
        .next_sequence = instance == nullptr
            ? 0ULL
            : instance->next_sequence,
        .event_count = instance == nullptr ? 0U : instance->event_count,
        .node_count = instance == nullptr ? 0U : instance->node_count,
        .present_node_count =
            instance == nullptr ? 0U : instance->present_node_count,
        .matched_surface_count = MatchedSurfaceCount(instance),
        .surface_instance_handles = {},
        .read_request_count =
            state == JARVIS_TAP_DISCOVERY_STATE_COMPLETE
                ? JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT
                : 0U,
        .review_callback_compiled =
            JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK,
        .diagnostics_site_touched = 0U,
        .callback_subscription_attempted = 0U,
        .property_read_attempted = 0U,
        .property_write_supported = 0U,
        .execution_supported = 0U,
        .ready_for_live_connection = 0U,
        .ready_for_exact_approval = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
    };
    if (instance != nullptr) {
        for (std::uint32_t slot = 0U;
             slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
             ++slot) {
            response.surface_instance_handles[slot] =
                instance->surface_instance_handles[slot];
        }
    }
    return response;
}

[[nodiscard]] jarvis_tap_surface_discovery_response Block(
    jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_discovery_result result) noexcept {
    jarvis_tap_surface_discovery_fail_closed(instance, result);
    return MakeResponse(
        instance,
        JARVIS_TAP_DISCOVERY_STATE_BLOCKED,
        result);
}

[[nodiscard]] std::uint32_t FindNode(
    const jarvis_tap_surface_discovery_instance& instance,
    const std::uint64_t handle) noexcept {
    for (std::uint32_t index = 0U;
         index < instance.node_count;
         ++index) {
        if (instance.nodes[index].instance_handle == handle) {
            return index;
        }
    }
    return JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT;
}

[[nodiscard]] bool NodeMatchesSurface(
    const jarvis_tap_discovery_node& node,
    const std::uint32_t slot) noexcept {
    switch (slot) {
        case 0U:
            return node.type == JARVIS_TAP_VISUAL_TYPE_GRID &&
                   node.name ==
                       JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID;
        case 1U:
            return node.type == JARVIS_TAP_VISUAL_TYPE_GRID &&
                   node.name ==
                       JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID;
        case 2U:
            return node.type ==
                   JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW;
        default:
            return false;
    }
}

[[nodiscard]] jarvis_tap_visual_type RequiredAncestorType(
    const std::uint32_t slot) noexcept {
    switch (slot) {
        case 0U:
            return JARVIS_TAP_VISUAL_TYPE_TAB_CONTROL;
        case 1U:
            return JARVIS_TAP_VISUAL_TYPE_COMMAND_BAR_CONTROL;
        default:
            return JARVIS_TAP_VISUAL_TYPE_OTHER;
    }
}

[[nodiscard]] jarvis_tap_discovery_result ValidateAncestry(
    const jarvis_tap_surface_discovery_instance& instance,
    const std::uint32_t node_index,
    const jarvis_tap_visual_type required_ancestor,
    bool* const matched) noexcept {
    if (matched == nullptr ||
        node_index >= instance.node_count) {
        return JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    }
    *matched = required_ancestor ==
        JARVIS_TAP_VISUAL_TYPE_OTHER;

    std::uint64_t parent = instance.nodes[node_index].parent_handle;
    std::uint32_t depth = 0U;
    while (parent != 0ULL) {
        if (depth >= JARVIS_TAP_DISCOVERY_MAX_DEPTH) {
            return JARVIS_TAP_DISCOVERY_RESULT_DEPTH_EXCEEDED;
        }
        const auto parent_index = FindNode(instance, parent);
        if (parent_index >= instance.node_count ||
            instance.nodes[parent_index].present == 0U) {
            return JARVIS_TAP_DISCOVERY_RESULT_ORPHAN;
        }
        if (parent_index == node_index) {
            return JARVIS_TAP_DISCOVERY_RESULT_CYCLE;
        }
        if (instance.nodes[parent_index].type == required_ancestor) {
            *matched = true;
        }
        parent = instance.nodes[parent_index].parent_handle;
        ++depth;
    }
    return JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
}

}  // namespace

void jarvis_tap_surface_discovery_reset(
    jarvis_tap_surface_discovery_instance* const instance) noexcept {
    if (instance != nullptr) {
        *instance = {};
    }
}

jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_query_contract() noexcept {
    return MakeResponse(
        nullptr,
        JARVIS_TAP_DISCOVERY_STATE_DISABLED,
        JARVIS_TAP_DISCOVERY_RESULT_REVIEW_OBJECT_DISABLED);
}

jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_bind(
    jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_admission_instance* const admission,
    const std::uint64_t now_monotonic_ms) noexcept {
    if (instance == nullptr || admission == nullptr) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TAP_DISCOVERY_STATE_DISABLED ||
        instance->next_sequence != 0ULL ||
        instance->event_count != 0U ||
        instance->node_count != 0U) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID);
    }
    if (admission->state != JARVIS_TAP_ADMISSION_STATE_ADMITTED ||
        admission->attempt_count != 1U ||
        admission->plan_consumed != 1U ||
        admission->reserved != 0U) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID);
    }
    const auto& bind = admission->bind;
    if (bind.size != sizeof(bind) ||
        bind.abi_version != JARVIS_EXPLORER_TRANSPORT_ABI_VERSION ||
        bind.target.reserved != 0U ||
        bind.reserved != 0U ||
        bind.required_surface_count !=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        bind.required_property_count !=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT ||
        bind.preview_duration_ms !=
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS ||
        HashIsZero(bind.target.visual_tree_generation_sha256)) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID);
    }
    if (now_monotonic_ms < bind.issued_at_monotonic_ms ||
        now_monotonic_ms > bind.expires_at_monotonic_ms ||
        now_monotonic_ms < admission->evaluated_at_monotonic_ms) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_CAPABILITY_NOT_CURRENT);
    }
    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        if (!SelectorHashMatches(
                bind.expected_selector_sha256[slot],
                slot)) {
            return Block(
                instance,
                JARVIS_TAP_DISCOVERY_RESULT_SELECTOR_PROFILE_MISMATCH);
        }
    }

    instance->state = JARVIS_TAP_DISCOVERY_STATE_COLLECTING;
    instance->last_result = JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
    instance->next_sequence = 1ULL;
    instance->target = bind.target;
    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        instance->expected_selector_sha256[slot] =
            bind.expected_selector_sha256[slot];
    }
    return MakeResponse(
        instance,
        instance->state,
        instance->last_result);
}

jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_ingest(
    jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_visual_tree_event* const event) noexcept {
    if (instance == nullptr || event == nullptr) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TAP_DISCOVERY_STATE_COLLECTING) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID);
    }
    if (event->size != sizeof(*event)) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_SIZE_MISMATCH);
    }
    if (event->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_ABI_MISMATCH);
    }
    if (event->sequence != instance->next_sequence) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_SEQUENCE_INVALID);
    }
    if (instance->event_count >=
        JARVIS_TAP_DISCOVERY_MAX_EVENT_COUNT) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_NODE_CAPACITY_EXCEEDED);
    }
    if ((event->mutation != JARVIS_TAP_VISUAL_MUTATION_ADD &&
         event->mutation != JARVIS_TAP_VISUAL_MUTATION_REMOVE) ||
        event->type > JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW ||
        event->name >
            JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID ||
        event->child_handle == 0ULL ||
        event->instance_handle != event->child_handle ||
        event->parent_handle == event->child_handle ||
        event->reserved != 0U ||
        event->reserved2 != 0U) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID);
    }

    const auto existing = FindNode(*instance, event->instance_handle);
    if (event->mutation == JARVIS_TAP_VISUAL_MUTATION_ADD) {
        if (existing < instance->node_count) {
            return Block(
                instance,
                JARVIS_TAP_DISCOVERY_RESULT_HANDLE_REPLAY);
        }
        if (instance->node_count >=
            JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT) {
            return Block(
                instance,
                JARVIS_TAP_DISCOVERY_RESULT_NODE_CAPACITY_EXCEEDED);
        }
        instance->nodes[instance->node_count] =
            jarvis_tap_discovery_node{
                .instance_handle = event->instance_handle,
                .parent_handle = event->parent_handle,
                .child_index = event->child_index,
                .type = event->type,
                .name = event->name,
                .present = 1U,
                .reserved = 0U,
                .reserved2 = 0U,
            };
        ++instance->node_count;
        ++instance->present_node_count;
    } else {
        if (existing >= instance->node_count ||
            instance->nodes[existing].present == 0U) {
            return Block(
                instance,
                JARVIS_TAP_DISCOVERY_RESULT_REMOVE_UNKNOWN);
        }
        instance->nodes[existing].present = 0U;
        --instance->present_node_count;
    }

    ++instance->event_count;
    ++instance->next_sequence;
    instance->last_result = JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
    return MakeResponse(
        instance,
        instance->state,
        instance->last_result);
}

jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_finalize(
    jarvis_tap_surface_discovery_instance* const instance) noexcept {
    if (instance == nullptr) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TAP_DISCOVERY_STATE_COLLECTING) {
        return Block(
            instance,
            JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID);
    }

    std::uint32_t match_counts[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT] = {};
    std::uint64_t match_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT] = {};

    for (std::uint32_t index = 0U;
         index < instance->node_count;
         ++index) {
        const auto& node = instance->nodes[index];
        if (node.present == 0U) {
            continue;
        }

        bool ancestry_valid = false;
        const auto ancestry_result = ValidateAncestry(
            *instance,
            index,
            JARVIS_TAP_VISUAL_TYPE_OTHER,
            &ancestry_valid);
        if (ancestry_result !=
            JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED) {
            return Block(instance, ancestry_result);
        }

        for (std::uint32_t slot = 0U;
             slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
             ++slot) {
            if (!NodeMatchesSurface(node, slot)) {
                continue;
            }
            bool selector_matched = false;
            const auto selector_result = ValidateAncestry(
                *instance,
                index,
                RequiredAncestorType(slot),
                &selector_matched);
            if (selector_result !=
                JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED) {
                return Block(instance, selector_result);
            }
            if (selector_matched) {
                ++match_counts[slot];
                match_handles[slot] = node.instance_handle;
            }
        }
    }

    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        if (match_counts[slot] != 1U ||
            match_handles[slot] == 0ULL) {
            return Block(
                instance,
                JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE);
        }
        for (std::uint32_t previous = 0U;
             previous < slot;
             ++previous) {
            if (match_handles[slot] == match_handles[previous]) {
                return Block(
                    instance,
                    JARVIS_TAP_DISCOVERY_RESULT_SURFACE_COLLISION);
            }
        }
    }

    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        instance->surface_instance_handles[slot] =
            match_handles[slot];
    }
    instance->state = JARVIS_TAP_DISCOVERY_STATE_COMPLETE;
    instance->last_result = JARVIS_TAP_DISCOVERY_RESULT_COMPLETE;
    return MakeResponse(
        instance,
        instance->state,
        instance->last_result);
}

jarvis_tap_surface_discovery_response
jarvis_tap_surface_discovery_query(
    const jarvis_tap_surface_discovery_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_DISCOVERY_STATE_BLOCKED,
            JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT);
    }
    return MakeResponse(
        instance,
        instance->state,
        instance->last_result);
}

jarvis_tap_discovery_result
jarvis_tap_surface_discovery_build_read_request(
    const jarvis_tap_surface_discovery_instance* const instance,
    const std::uint32_t read_slot,
    jarvis_tap_xaml_read_request* const request) noexcept {
    if (instance == nullptr || request == nullptr) {
        return JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    }
    *request = {};
    if (instance->state != JARVIS_TAP_DISCOVERY_STATE_COMPLETE) {
        return JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID;
    }
    if (read_slot >= JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        return JARVIS_TAP_DISCOVERY_RESULT_SLOT_INVALID;
    }
    const auto surface_slot =
        read_slot / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto property_slot =
        read_slot % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    request->size = sizeof(*request);
    request->abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
    request->sequence = static_cast<std::uint64_t>(read_slot) + 1ULL;
    request->surface_slot = surface_slot;
    request->property_slot = property_slot;
    request->instance_handle =
        instance->surface_instance_handles[surface_slot];
    request->selector_sha256 =
        instance->expected_selector_sha256[surface_slot];
    return JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
}

void jarvis_tap_surface_discovery_fail_closed(
    jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_discovery_result result) noexcept {
    if (instance == nullptr) {
        return;
    }
    instance->state = JARVIS_TAP_DISCOVERY_STATE_BLOCKED;
    instance->last_result = result;
    for (auto& handle : instance->surface_instance_handles) {
        handle = 0ULL;
    }
}
