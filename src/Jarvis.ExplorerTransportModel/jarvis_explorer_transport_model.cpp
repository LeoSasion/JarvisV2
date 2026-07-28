#include "jarvis_explorer_transport_contract.h"

#include <cstddef>
#include <cstring>

namespace {

[[nodiscard]] bool HashIsZero(
    const jarvis_transport_hash256& value) noexcept {
    return value.words[0] == 0U &&
           value.words[1] == 0U &&
           value.words[2] == 0U &&
           value.words[3] == 0U;
}

[[nodiscard]] bool HashEquals(
    const jarvis_transport_hash256& left,
    const jarvis_transport_hash256& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] bool IdentityIsStructurallyValid(
    const jarvis_transport_target_identity& target) noexcept {
    return target.explorer_process_id != 0U &&
           target.desktop_shell_process_id != 0U &&
           target.explorer_process_id != target.desktop_shell_process_id &&
           target.window_thread_id != 0U &&
           target.reserved == 0U &&
           target.window_handle != 0U &&
           target.process_start_time_utc_ticks != 0U &&
           !HashIsZero(target.visual_tree_generation_sha256) &&
           !HashIsZero(target.exact_window_title_sha256);
}

[[nodiscard]] bool IdentityEquals(
    const jarvis_transport_target_identity& left,
    const jarvis_transport_target_identity& right) noexcept {
    return left.explorer_process_id == right.explorer_process_id &&
           left.desktop_shell_process_id == right.desktop_shell_process_id &&
           left.window_thread_id == right.window_thread_id &&
           left.reserved == right.reserved &&
           left.window_handle == right.window_handle &&
           left.process_start_time_utc_ticks ==
               right.process_start_time_utc_ticks &&
           HashEquals(
               left.visual_tree_generation_sha256,
               right.visual_tree_generation_sha256) &&
           HashEquals(
               left.exact_window_title_sha256,
               right.exact_window_title_sha256);
}

[[nodiscard]] bool GenerationEquals(
    const jarvis_transport_target_identity& left,
    const jarvis_transport_target_identity& right) noexcept {
    return HashEquals(
        left.visual_tree_generation_sha256,
        right.visual_tree_generation_sha256);
}

[[nodiscard]] jarvis_transport_response MakeResponse(
    const jarvis_transport_model_instance* instance,
    const jarvis_transport_result result) noexcept {
    const auto state = instance == nullptr
        ? JARVIS_TRANSPORT_STATE_BLOCKED
        : instance->state;
    const auto next_sequence = instance == nullptr
        ? 0U
        : static_cast<std::uint32_t>(instance->next_sequence);
    return jarvis_transport_response{
        .size = sizeof(jarvis_transport_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = state,
        .result = result,
        .next_sequence = next_sequence,
        .observed_surface_count = instance == nullptr
            ? 0U
            : instance->observed_surface_count,
        .journaled_property_count = instance == nullptr
            ? 0U
            : instance->journaled_property_count,
        .applied_property_count = instance == nullptr
            ? 0U
            : instance->applied_property_count,
        .restored_property_count = instance == nullptr
            ? 0U
            : instance->restored_property_count,
        .capability_consumed = instance == nullptr
            ? 0U
            : instance->capability_consumed,
        .restore_required = instance == nullptr
            ? 0U
            : instance->restore_required,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
    };
}

void LatchGuardFailure(
    jarvis_transport_model_instance* instance) noexcept {
    if (instance->applied_property_count != 0U) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
    } else {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
    }
}

[[nodiscard]] jarvis_transport_result ValidateCommonPropertyRequest(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_property_request* request) noexcept {
    if (request == nullptr) {
        return JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT;
    }
    if (request->size != sizeof(jarvis_transport_property_request)) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH;
    }
    if (request->abi_version != JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_ABI_MISMATCH;
    }
    if (request->sequence != instance->next_sequence) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_SEQUENCE_INVALID;
    }
    if (!IdentityEquals(request->target, instance->target)) {
        const auto result = GenerationEquals(request->target, instance->target)
            ? JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT
            : JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT;
        LatchGuardFailure(instance);
        return result;
    }
    if (request->surface_slot >=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->property_slot >=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID;
    }
    if (request->instance_handle == 0U ||
        request->instance_handle !=
            instance->surface_instance_handles[request->surface_slot]) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT;
    }
    if (HashIsZero(request->value_sha256)) {
        LatchGuardFailure(instance);
        return JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID;
    }
    return JARVIS_TRANSPORT_RESULT_ACCEPTED;
}

[[nodiscard]] std::uint32_t FlatPropertyIndex(
    const std::uint32_t surface_slot,
    const std::uint32_t property_slot) noexcept {
    return surface_slot * JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT +
           property_slot;
}

}  // namespace

void jarvis_transport_model_reset(
    jarvis_transport_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return;
    }
    std::memset(instance, 0, sizeof(*instance));
    instance->state = JARVIS_TRANSPORT_STATE_COLD;
}

jarvis_transport_response jarvis_transport_model_query_contract() noexcept {
    jarvis_transport_model_instance instance{};
    jarvis_transport_model_reset(&instance);
    return MakeResponse(&instance, JARVIS_TRANSPORT_RESULT_MODEL_ONLY);
}

jarvis_transport_response jarvis_transport_model_bind(
    jarvis_transport_model_instance* const instance,
    const jarvis_transport_bind_request* const request,
    const std::uint64_t now_monotonic_ms) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->bind_attempt_count != 0U ||
        instance->state != JARVIS_TRANSPORT_STATE_COLD) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_BIND_REPLAY);
    }
    ++instance->bind_attempt_count;

    if (request->size != sizeof(jarvis_transport_bind_request)) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version != JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_ABI_MISMATCH);
    }
    if (!IdentityIsStructurallyValid(request->target)) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);
    }
    if (HashIsZero(request->session_nonce) ||
        HashIsZero(request->selector_profile_sha256) ||
        HashIsZero(request->preview_plan_sha256) ||
        request->reserved != 0U) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        if (HashIsZero(request->expected_selector_sha256[index])) {
            instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
            return MakeResponse(
                instance,
                JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);
        }
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        if (HashIsZero(request->expected_styled_value_sha256[index])) {
            instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
            return MakeResponse(
                instance,
                JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);
        }
    }
    if (request->issued_at_monotonic_ms > now_monotonic_ms ||
        request->expires_at_monotonic_ms <= now_monotonic_ms) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);
    }
    if (request->expires_at_monotonic_ms <=
            request->issued_at_monotonic_ms ||
        request->expires_at_monotonic_ms -
                request->issued_at_monotonic_ms >
            JARVIS_TRANSPORT_MAX_CAPABILITY_AGE_MS ||
        request->preview_duration_ms !=
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS ||
        request->required_surface_count !=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->required_property_count !=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT) {
        instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);
    }

    instance->target = request->target;
    instance->session_nonce = request->session_nonce;
    instance->selector_profile_sha256 =
        request->selector_profile_sha256;
    instance->preview_plan_sha256 = request->preview_plan_sha256;
    std::memcpy(
        instance->expected_selector_sha256,
        request->expected_selector_sha256,
        sizeof(instance->expected_selector_sha256));
    std::memcpy(
        instance->expected_styled_value_sha256,
        request->expected_styled_value_sha256,
        sizeof(instance->expected_styled_value_sha256));
    instance->issued_at_monotonic_ms =
        request->issued_at_monotonic_ms;
    instance->expires_at_monotonic_ms =
        request->expires_at_monotonic_ms;
    instance->next_sequence = 1U;
    instance->state = JARVIS_TRANSPORT_STATE_BOUND;
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_observe_surface(
    jarvis_transport_model_instance* const instance,
    const jarvis_transport_surface_request* const request) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TRANSPORT_STATE_BOUND) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_STATE_INVALID);
    }
    if (request->size != sizeof(jarvis_transport_surface_request)) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version != JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_ABI_MISMATCH);
    }
    if (request->sequence != instance->next_sequence) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_SEQUENCE_INVALID);
    }
    if (!IdentityEquals(request->target, instance->target)) {
        const auto result = GenerationEquals(request->target, instance->target)
            ? JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT
            : JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT;
        LatchGuardFailure(instance);
        return MakeResponse(instance, result);
    }
    if (request->surface_slot != instance->observed_surface_count ||
        request->surface_slot >=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->match_count != 1U ||
        request->instance_handle == 0U ||
        HashIsZero(request->selector_sha256) ||
        !HashEquals(
            request->selector_sha256,
            instance->expected_selector_sha256[request->surface_slot])) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);
    }
    for (std::uint32_t index = 0U;
         index < instance->observed_surface_count;
         ++index) {
        if (instance->surface_instance_handles[index] ==
            request->instance_handle) {
            LatchGuardFailure(instance);
            return MakeResponse(
                instance,
                JARVIS_TRANSPORT_RESULT_SURFACE_NOT_UNIQUE);
        }
    }

    instance->surface_instance_handles[request->surface_slot] =
        request->instance_handle;
    instance->surface_selector_sha256[request->surface_slot] =
        request->selector_sha256;
    ++instance->observed_surface_count;
    ++instance->next_sequence;
    if (instance->observed_surface_count ==
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT) {
        instance->state = JARVIS_TRANSPORT_STATE_DISCOVERED;
    }
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_journal_original(
    jarvis_transport_model_instance* const instance,
    const jarvis_transport_property_request* const request) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TRANSPORT_STATE_DISCOVERED) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_STATE_INVALID);
    }
    const auto validation =
        ValidateCommonPropertyRequest(instance, request);
    if (validation != JARVIS_TRANSPORT_RESULT_ACCEPTED) {
        return MakeResponse(instance, validation);
    }
    const auto expected_index = instance->journaled_property_count;
    const auto actual_index = FlatPropertyIndex(
        request->surface_slot,
        request->property_slot);
    if (actual_index != expected_index) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID);
    }

    instance->original_value_sha256[actual_index] =
        request->value_sha256;
    ++instance->journaled_property_count;
    ++instance->next_sequence;
    if (instance->journaled_property_count ==
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        instance->state = JARVIS_TRANSPORT_STATE_JOURNALED;
    }
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_record_apply(
    jarvis_transport_model_instance* const instance,
    const jarvis_transport_property_request* const request,
    const std::uint32_t platform_write_succeeded) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TRANSPORT_STATE_JOURNALED &&
        instance->state != JARVIS_TRANSPORT_STATE_APPLYING) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_STATE_INVALID);
    }
    const auto validation =
        ValidateCommonPropertyRequest(instance, request);
    if (validation != JARVIS_TRANSPORT_RESULT_ACCEPTED) {
        return MakeResponse(instance, validation);
    }
    if (request->observed_at_monotonic_ms >=
        instance->expires_at_monotonic_ms) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);
    }
    const auto actual_index = FlatPropertyIndex(
        request->surface_slot,
        request->property_slot);
    if (actual_index != instance->applied_property_count) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_APPLY_INVALID);
    }
    if (!HashEquals(
            request->value_sha256,
            instance->expected_styled_value_sha256[actual_index])) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_APPLY_INVALID);
    }

    if (instance->capability_consumed == 0U) {
        if (instance->expires_at_monotonic_ms -
                request->observed_at_monotonic_ms <
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS) {
            instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
            return MakeResponse(
                instance,
                JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);
        }
        instance->capability_consumed = 1U;
        instance->preview_deadline_monotonic_ms =
            request->observed_at_monotonic_ms +
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS;
    }

    if (request->observed_at_monotonic_ms >=
        instance->preview_deadline_monotonic_ms) {
        LatchGuardFailure(instance);
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED);
    }
    if (platform_write_succeeded != 1U) {
        if (instance->applied_property_count == 0U) {
            instance->state = JARVIS_TRANSPORT_STATE_BLOCKED;
        } else {
            instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
            instance->restore_required = 1U;
        }
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_APPLY_FAILED);
    }

    ++instance->applied_property_count;
    ++instance->simulated_mutation_count;
    ++instance->next_sequence;
    instance->state =
        instance->applied_property_count ==
                JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT
            ? JARVIS_TRANSPORT_STATE_APPLIED
            : JARVIS_TRANSPORT_STATE_APPLYING;
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_tick(
    jarvis_transport_model_instance* const instance,
    const std::uint64_t now_monotonic_ms) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if ((instance->state == JARVIS_TRANSPORT_STATE_APPLYING ||
         instance->state == JARVIS_TRANSPORT_STATE_APPLIED ||
         instance->state == JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED) &&
        instance->preview_deadline_monotonic_ms != 0U &&
        now_monotonic_ms >=
            instance->preview_deadline_monotonic_ms) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED);
    }
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_record_restore(
    jarvis_transport_model_instance* const instance,
    const jarvis_transport_property_request* const request,
    const std::uint32_t platform_write_succeeded) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->state != JARVIS_TRANSPORT_STATE_APPLIED &&
        instance->state != JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED &&
        instance->state != JARVIS_TRANSPORT_STATE_RESTORING) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_STATE_INVALID);
    }
    if (instance->applied_property_count == 0U) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORED;
        instance->restore_required = 0U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORED);
    }
    const auto validation =
        ValidateCommonPropertyRequest(instance, request);
    if (validation != JARVIS_TRANSPORT_RESULT_ACCEPTED) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
        return MakeResponse(instance, validation);
    }
    const auto expected_index = instance->applied_property_count - 1U;
    const auto actual_index = FlatPropertyIndex(
        request->surface_slot,
        request->property_slot);
    if (actual_index != expected_index ||
        !HashEquals(
            request->value_sha256,
            instance->original_value_sha256[expected_index])) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORE_INVALID);
    }
    if (platform_write_succeeded != 1U) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORE_FAILED);
    }

    --instance->applied_property_count;
    ++instance->restored_property_count;
    ++instance->simulated_mutation_count;
    ++instance->next_sequence;
    if (instance->applied_property_count == 0U) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORED;
        instance->restore_required = 0U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORED);
    }
    instance->state = JARVIS_TRANSPORT_STATE_RESTORING;
    instance->restore_required = 1U;
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_ACCEPTED);
}

jarvis_transport_response jarvis_transport_model_quiesce(
    jarvis_transport_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    if (instance->applied_property_count != 0U ||
        instance->state == JARVIS_TRANSPORT_STATE_APPLYING ||
        instance->state == JARVIS_TRANSPORT_STATE_APPLIED ||
        instance->state == JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED ||
        instance->state == JARVIS_TRANSPORT_STATE_RESTORING) {
        instance->state = JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED;
        instance->restore_required = 1U;
        return MakeResponse(
            instance,
            JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED);
    }
    instance->state = JARVIS_TRANSPORT_STATE_QUIESCED;
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_QUIESCED);
}

jarvis_transport_response jarvis_transport_model_query(
    const jarvis_transport_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);
    }
    return MakeResponse(instance, JARVIS_TRANSPORT_RESULT_MODEL_ONLY);
}
