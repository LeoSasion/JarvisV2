#include "jarvis_explorer_tap_xaml_read_bridge.h"

#include <cstring>

namespace {

[[nodiscard]] bool HashEqual(
    const jarvis_transport_hash256& left,
    const jarvis_transport_hash256& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] jarvis_tap_xaml_read_response MakeResponse(
    const jarvis_tap_xaml_read_state state,
    const jarvis_tap_xaml_read_result result,
    const jarvis_tap_target_result target_result) noexcept {
    return jarvis_tap_xaml_read_response{
        .size = sizeof(jarvis_tap_xaml_read_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = state,
        .result = result,
        .target_result = target_result,
        .review_bridge_compiled =
            JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE,
        .diagnostics_site_touched = 0U,
        .property_read_attempted = 0U,
        .foreign_outcome_uncertain = 0U,
        .release_attempt_count = 0U,
        .release_completed_count = 0U,
        .property_chain_freed = 0U,
        .property_read_supported = 0U,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
        .snapshot = {},
    };
}

[[nodiscard]] jarvis_tap_xaml_read_response Block(
    const jarvis_tap_xaml_read_result result,
    const jarvis_tap_target_result target_result =
        JARVIS_TAP_TARGET_RESULT_INVALID_ARGUMENT) noexcept {
    return MakeResponse(
        JARVIS_TAP_XAML_READ_STATE_BLOCKED,
        result,
        target_result);
}

[[nodiscard]] bool IsTargetAcceptance(
    const jarvis_tap_xaml_read_response& value) noexcept {
    return value.size == sizeof(value) &&
           value.abi_version == JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           value.state == JARVIS_TAP_XAML_READ_STATE_TARGET_ACCEPTED &&
           value.result == JARVIS_TAP_XAML_READ_RESULT_TARGET_ACCEPTED &&
           value.target_result == JARVIS_TAP_TARGET_RESULT_ACCEPTED &&
           value.diagnostics_site_touched == 0U &&
           value.property_read_attempted == 0U &&
           value.foreign_outcome_uncertain == 0U &&
           value.release_attempt_count == 0U &&
           value.release_completed_count == 0U &&
           value.property_chain_freed == 0U &&
           value.property_read_supported == 0U &&
           value.execution_supported == 0U &&
           value.activation_permitted == 0U &&
           value.mutation_performed == 0U &&
           value.live_explorer_touched == 0U &&
           value.reserved == 0U;
}

}  // namespace

jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_query_contract() noexcept {
    return MakeResponse(
        JARVIS_TAP_XAML_READ_STATE_DISABLED,
        JARVIS_TAP_XAML_READ_RESULT_REVIEW_OBJECT_DISABLED,
        JARVIS_TAP_TARGET_RESULT_INVALID_ARGUMENT);
}

jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_preflight(
    const jarvis_tap_admission_instance* const admission,
    const jarvis_tap_xaml_read_request* const request,
    const std::uint64_t now_monotonic_ms) noexcept {
    if (admission == nullptr || request == nullptr) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    }
    if (request->size != sizeof(*request)) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH);
    }
    if (admission->state != JARVIS_TAP_ADMISSION_STATE_ADMITTED ||
        admission->attempt_count != 1U ||
        admission->plan_consumed != 1U ||
        admission->reserved != 0U) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
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
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    }
    if (now_monotonic_ms < bind.issued_at_monotonic_ms ||
        now_monotonic_ms > bind.expires_at_monotonic_ms ||
        now_monotonic_ms < admission->evaluated_at_monotonic_ms) {
        return Block(
            JARVIS_TAP_XAML_READ_RESULT_CAPABILITY_NOT_CURRENT);
    }
    if (request->surface_slot >=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->property_slot >=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT ||
        request->reserved != 0U ||
        request->reserved2 != 0U) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SLOT_INVALID);
    }
    const auto expected_sequence =
        static_cast<std::uint64_t>(request->surface_slot) *
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT +
        static_cast<std::uint64_t>(request->property_slot) + 1ULL;
    if (request->sequence != expected_sequence) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SEQUENCE_INVALID);
    }
    if (request->instance_handle == 0ULL) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_INSTANCE_INVALID);
    }
    if (!HashEqual(
            request->selector_sha256,
            bind.expected_selector_sha256[request->surface_slot])) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SELECTOR_MISMATCH);
    }

    return MakeResponse(
        JARVIS_TAP_XAML_READ_STATE_PREFLIGHT,
        JARVIS_TAP_XAML_READ_RESULT_PREFLIGHT_ACCEPTED,
        JARVIS_TAP_TARGET_RESULT_INVALID_ARGUMENT);
}

jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_accept_target(
    const jarvis_tap_xaml_read_response* const preflight,
    const jarvis_tap_target_result target_result) noexcept {
    if (preflight == nullptr) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    }
    if (preflight->size != sizeof(*preflight)) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH);
    }
    if (preflight->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH);
    }
    if (preflight->state != JARVIS_TAP_XAML_READ_STATE_PREFLIGHT ||
        preflight->result !=
            JARVIS_TAP_XAML_READ_RESULT_PREFLIGHT_ACCEPTED ||
        preflight->diagnostics_site_touched != 0U ||
        preflight->property_read_attempted != 0U ||
        preflight->foreign_outcome_uncertain != 0U ||
        preflight->release_attempt_count != 0U ||
        preflight->release_completed_count != 0U ||
        preflight->property_chain_freed != 0U ||
        preflight->property_read_supported != 0U ||
        preflight->execution_supported != 0U ||
        preflight->activation_permitted != 0U ||
        preflight->mutation_performed != 0U ||
        preflight->live_explorer_touched != 0U ||
        preflight->reserved != 0U) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    }
    if (target_result != JARVIS_TAP_TARGET_RESULT_ACCEPTED) {
        return Block(
            JARVIS_TAP_XAML_READ_RESULT_TARGET_REJECTED,
            target_result);
    }
    return MakeResponse(
        JARVIS_TAP_XAML_READ_STATE_TARGET_ACCEPTED,
        JARVIS_TAP_XAML_READ_RESULT_TARGET_ACCEPTED,
        target_result);
}

jarvis_tap_xaml_read_response
jarvis_tap_xaml_read_bridge_complete(
    const jarvis_tap_admission_instance* const admission,
    const jarvis_tap_xaml_read_request* const request,
    const jarvis_tap_xaml_read_response* const target_acceptance,
    const jarvis_tap_xaml_foreign_observation* const observation,
    const std::uint32_t live_explorer_touched) noexcept {
    if (admission == nullptr ||
        request == nullptr ||
        target_acceptance == nullptr ||
        observation == nullptr) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    }
    if (!IsTargetAcceptance(*target_acceptance)) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    }
    if (observation->size != sizeof(*observation) ||
        request->size != sizeof(*request)) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH);
    }
    if (observation->abi_version !=
            JARVIS_EXPLORER_TRANSPORT_ABI_VERSION ||
        request->abi_version !=
            JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH);
    }

    auto response = *target_acceptance;
    response.diagnostics_site_touched =
        observation->site_query_succeeded;
    response.property_read_attempted =
        observation->property_chain_call_attempted;
    response.foreign_outcome_uncertain =
        observation->foreign_outcome_uncertain;
    response.release_attempt_count =
        observation->release_attempt_count;
    response.release_completed_count =
        observation->release_completed_count;
    response.property_chain_freed =
        observation->property_chain_freed;
    response.live_explorer_touched = live_explorer_touched;

    const auto reject =
        [&response](const jarvis_tap_xaml_read_result result) noexcept {
            response.state = JARVIS_TAP_XAML_READ_STATE_BLOCKED;
            response.result = result;
            response.snapshot = {};
            response.property_read_supported = 0U;
            response.execution_supported = 0U;
            response.activation_permitted = 0U;
            response.mutation_performed = 0U;
            return response;
        };

    if (live_explorer_touched > 1U ||
        observation->reserved != 0U ||
        observation->reserved2 != 0U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);
    }
    if (observation->foreign_outcome_uncertain != 0U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_FOREIGN_OUTCOME_UNCERTAIN);
    }
    if (observation->site_query_succeeded != 1U) {
        return reject(JARVIS_TAP_XAML_READ_RESULT_SITE_QUERY_FAILED);
    }
    if (observation->service_query_succeeded != 1U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_SERVICE_QUERY_FAILED);
    }
    if (observation->property_chain_call_attempted != 1U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_CHAIN_FAILED);
    }
    if (observation->property_chain_call_succeeded != 1U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_CHAIN_FAILED);
    }
    if (observation->property_source_count == 0U ||
        observation->property_source_count >
            JARVIS_TAP_XAML_READ_MAX_PROPERTY_SOURCE_COUNT ||
        observation->property_value_count == 0U ||
        observation->property_value_count >
            JARVIS_TAP_XAML_READ_MAX_PROPERTY_VALUE_COUNT) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_COUNT_INVALID);
    }
    if (observation->matched_property_count != 1U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_NOT_UNIQUE);
    }
    if (observation->property_chain_index >=
        observation->property_source_count) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_SOURCE_INVALID);
    }
    if (observation->property_value_source != 4U) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_ORIGIN_UNSUPPORTED);
    }
    if ((observation->property_metadata_bits &
            ~JARVIS_TAP_XAML_METADATA_KNOWN_MASK) != 0ULL) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_PROPERTY_METADATA_UNSUPPORTED);
    }
    if (observation->property_chain_free_required != 1U ||
        observation->property_chain_freed != 1U ||
        observation->release_attempt_count !=
            observation->release_completed_count) {
        return reject(
            JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE);
    }

    jarvis_tap_runtime_property_snapshot snapshot{
        .size = sizeof(jarvis_tap_runtime_property_snapshot),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = request->sequence,
        .target = admission->bind.target,
        .surface_slot = request->surface_slot,
        .property_slot = request->property_slot,
        .instance_handle = request->instance_handle,
        .selector_sha256 = request->selector_sha256,
        .value_origin = JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL,
        .runtime_value_kind = observation->runtime_value_kind,
        .runtime_class = observation->runtime_class,
        .exact_runtime_class_name_matched =
            observation->exact_runtime_class_name_matched,
        .argb = observation->argb,
        .opacity_millionths = observation->opacity_millionths,
        .reserved = 0U,
        .reserved2 = 0U,
    };

    if ((observation->property_metadata_bits &
            JARVIS_TAP_XAML_METADATA_IS_VALUE_NULL) != 0ULL) {
        if (observation->property_handle_call_succeeded != 0U ||
            observation->property_value_handle_nonzero != 0U ||
            observation->inspectable_call_succeeded != 0U ||
            observation->runtime_value_kind !=
                JARVIS_TAP_RUNTIME_VALUE_NULL ||
            observation->runtime_class !=
                JARVIS_TAP_RUNTIME_CLASS_NONE ||
            observation->exact_runtime_class_name_matched != 0U ||
            observation->brush_read_succeeded != 0U ||
            observation->argb != 0U ||
            observation->opacity_millionths != 0U) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);
        }
    }
    else {
        const auto handle_metadata =
            JARVIS_TAP_XAML_METADATA_IS_VALUE_HANDLE |
            JARVIS_TAP_XAML_METADATA_IS_VALUE_HANDLE_AND_EVALUATED_VALUE;
        if ((observation->property_metadata_bits & handle_metadata) ==
            0ULL) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_PROPERTY_METADATA_UNSUPPORTED);
        }
        if (observation->property_handle_call_succeeded != 1U ||
            observation->property_value_handle_nonzero != 1U) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_PROPERTY_HANDLE_FAILED);
        }
        if (observation->inspectable_call_succeeded != 1U) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_INSPECTABLE_FAILED);
        }
        if (observation->runtime_value_kind !=
                JARVIS_TAP_RUNTIME_VALUE_OBJECT ||
            observation->runtime_class !=
                JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH ||
            observation->exact_runtime_class_name_matched != 1U) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_RUNTIME_CLASS_UNSUPPORTED);
        }
        if (observation->brush_read_succeeded != 1U) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_BRUSH_READ_FAILED);
        }
        if (observation->opacity_millionths >
            JARVIS_TAP_OPACITY_MILLIONTHS_MAX) {
            return reject(
                JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);
        }
    }

    response.state = JARVIS_TAP_XAML_READ_STATE_OBSERVED;
    response.result =
        JARVIS_TAP_XAML_READ_RESULT_OBSERVATION_ACCEPTED;
    response.snapshot = snapshot;
    response.property_read_supported = 0U;
    response.execution_supported = 0U;
    response.activation_permitted = 0U;
    response.mutation_performed = 0U;
    return response;
}
