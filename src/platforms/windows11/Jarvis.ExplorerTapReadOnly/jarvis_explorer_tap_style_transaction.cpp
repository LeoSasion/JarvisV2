#include "jarvis_explorer_tap_style_transaction.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <limits>

namespace {

[[nodiscard]] bool ValuesMatch(
    const jarvis_tap_canonical_property_value& left,
    const jarvis_tap_canonical_property_value& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] bool HashesMatch(
    const jarvis_transport_hash256& left,
    const jarvis_transport_hash256& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] bool TargetsMatch(
    const jarvis_transport_target_identity& left,
    const jarvis_transport_target_identity& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] std::uint32_t DirtyCount(
    std::uint32_t value) noexcept {
    std::uint32_t count = 0U;
    while (value != 0U) {
        count += value & 1U;
        value >>= 1U;
    }
    return count;
}

[[nodiscard]] std::uint32_t HighestDirtyIndex(
    const std::uint32_t mask) noexcept {
    for (std::uint32_t index =
             JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         index > 0U;
         --index) {
        const auto candidate = index - 1U;
        if ((mask & (1U << candidate)) != 0U) {
            return candidate;
        }
    }
    return JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
}

[[nodiscard]] bool FlagValid(const std::uint32_t value) noexcept {
    return value <= 1U;
}

[[nodiscard]] jarvis_tap_style_transaction_response MakeResponse(
    const jarvis_tap_style_transaction_instance* const instance,
    const jarvis_tap_style_transaction_result result,
    const std::uint32_t deadline_reached) noexcept {
    return jarvis_tap_style_transaction_response{
        .size = sizeof(jarvis_tap_style_transaction_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = instance == nullptr
            ? JARVIS_TAP_STYLE_TRANSACTION_STATE_COLD
            : instance->state,
        .result = result,
        .next_sequence = instance == nullptr
            ? 0U
            : instance->next_sequence,
        .verified_apply_count = instance == nullptr
            ? 0U
            : instance->verified_apply_count,
        .verified_restore_count = instance == nullptr
            ? 0U
            : instance->verified_restore_count,
        .simulated_write_attempt_count = instance == nullptr
            ? 0U
            : instance->simulated_write_attempt_count,
        .dirty_property_count = instance == nullptr
            ? 0U
            : DirtyCount(instance->dirty_mask),
        .restore_required = instance != nullptr &&
                instance->dirty_mask != 0U
            ? 1U
            : 0U,
        .deadline_reached = deadline_reached,
        .transaction_model_supported = 1U,
        .property_write_supported = 0U,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] jarvis_tap_style_transaction_response RejectBeforeWrite(
    jarvis_tap_style_transaction_instance* const instance,
    const jarvis_tap_style_transaction_result result) noexcept {
    if (instance != nullptr) {
        instance->state = instance->dirty_mask == 0U
            ? JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED
            : JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
    }
    return MakeResponse(instance, result, 0U);
}

[[nodiscard]] bool ValidateCanonical(
    const jarvis_tap_canonical_property_value& value) noexcept {
    if (value.reserved != 0U) {
        return false;
    }
    if (value.value_kind == JARVIS_TAP_PROPERTY_VALUE_NULL) {
        return value.argb == 0U &&
               value.opacity_millionths == 0U;
    }
    return value.value_kind ==
               JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR &&
           value.opacity_millionths <=
               JARVIS_TAP_OPACITY_MILLIONTHS_MAX;
}

[[nodiscard]] jarvis_tap_style_transaction_result ValidateStep(
    const jarvis_tap_style_transaction_instance& instance,
    const jarvis_tap_style_step_request& request,
    const std::uint32_t expected_index) noexcept {
    if (request.size != sizeof(jarvis_tap_style_step_request)) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_SIZE_MISMATCH;
    }
    if (request.abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_ABI_MISMATCH;
    }
    if (request.sequence != instance.next_sequence) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_SEQUENCE_INVALID;
    }
    if (!TargetsMatch(request.target, instance.bind.target)) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_IDENTITY_DRIFT;
    }
    const auto expected_surface =
        expected_index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto expected_property =
        expected_index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    if (request.surface_slot != expected_surface ||
        request.property_slot != expected_property) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_SLOT_INVALID;
    }
    if (request.instance_handle == 0U ||
        request.instance_handle !=
            instance.surface_instance_handles[expected_surface]) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_INSTANCE_INVALID;
    }
    if (!HashesMatch(
            request.selector_sha256,
            instance.selector_sha256[expected_surface])) {
        return JARVIS_TAP_STYLE_TRANSACTION_RESULT_SELECTOR_MISMATCH;
    }
    return JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED;
}

[[nodiscard]] jarvis_tap_style_transaction_response RecordAttemptFailure(
    jarvis_tap_style_transaction_instance* const instance,
    const std::uint32_t index,
    const jarvis_tap_style_transaction_result result) noexcept {
    instance->dirty_mask |= 1U << index;
    ++instance->simulated_write_attempt_count;
    ++instance->next_sequence;
    instance->state =
        JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
    return MakeResponse(instance, result, 0U);
}

}  // namespace

void jarvis_tap_style_transaction_reset(
    jarvis_tap_style_transaction_instance* const instance) noexcept {
    if (instance != nullptr) {
        std::memset(instance, 0, sizeof(*instance));
        instance->state = JARVIS_TAP_STYLE_TRANSACTION_STATE_COLD;
    }
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_query_contract() noexcept {
    return MakeResponse(
        nullptr,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY,
        0U);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_prepare(
    jarvis_tap_style_transaction_instance* const instance,
    const jarvis_tap_admission_instance* const admission,
    const jarvis_tap_inspectable_adapter_instance* const adapter,
    const jarvis_tap_style_plan_request* const request) noexcept {
    if (instance == nullptr || admission == nullptr ||
        adapter == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    if (instance->state !=
        JARVIS_TAP_STYLE_TRANSACTION_STATE_COLD) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    if (request->size != sizeof(jarvis_tap_style_plan_request)) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_ABI_MISMATCH);
    }
    if (request->reserved != 0U) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_BIND_INVALID);
    }
    if (admission->state != JARVIS_TAP_ADMISSION_STATE_ADMITTED ||
        admission->plan_consumed != 1U ||
        std::memcmp(
            &admission->bind,
            &request->bind,
            sizeof(request->bind)) != 0) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_ADMISSION_INVALID);
    }
    if (adapter->state != JARVIS_TAP_ADAPTER_STATE_COMPLETE ||
        adapter->canonical_value_count !=
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT ||
        adapter->fingerprint.state !=
            JARVIS_TAP_FINGERPRINT_STATE_COMPLETE ||
        adapter->fingerprint.observed_property_count !=
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT ||
        !TargetsMatch(
            adapter->fingerprint.target,
            request->bind.target)) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_SNAPSHOT_INCOMPLETE);
    }

    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded{};
    const auto protocol =
        jarvis_tap_encode_initialization_data(
            &request->bind,
            encoded.data(),
            encoded.size());
    if (protocol.result != JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_BIND_INVALID);
    }
    if (request->prepared_at_monotonic_ms <
            request->bind.issued_at_monotonic_ms ||
        request->prepared_at_monotonic_ms >
            request->bind.expires_at_monotonic_ms) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_CAPABILITY_NOT_CURRENT);
    }
    if (request->prepared_at_monotonic_ms >
            std::numeric_limits<std::uint64_t>::max() -
                request->bind.preview_duration_ms) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_DEADLINE_INVALID);
    }
    const auto deadline =
        request->prepared_at_monotonic_ms +
        request->bind.preview_duration_ms;
    if (deadline > request->bind.expires_at_monotonic_ms) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_DEADLINE_INVALID);
    }

    bool any_change = false;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto surface =
            index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
        if (adapter->fingerprint.surface_instance_handles[surface] == 0U ||
            !HashesMatch(
                adapter->fingerprint.expected_selector_sha256[surface],
                request->bind.expected_selector_sha256[surface]) ||
            !ValidateCanonical(adapter->canonical_values[index]) ||
            !ValidateCanonical(request->styled_values[index])) {
            return RejectBeforeWrite(
                instance,
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_VALUE_INVALID);
        }
        const jarvis_tap_fingerprint_request fingerprint_request{
            .size = sizeof(jarvis_tap_fingerprint_request),
            .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
            .sequence = static_cast<std::uint64_t>(index) + 1U,
            .target = request->bind.target,
            .surface_slot = surface,
            .property_slot =
                index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
            .instance_handle =
                adapter->fingerprint.surface_instance_handles[surface],
            .selector_sha256 =
                request->bind.expected_selector_sha256[surface],
            .value_kind =
                request->styled_values[index].value_kind,
            .argb = request->styled_values[index].argb,
            .opacity_millionths =
                request->styled_values[index].opacity_millionths,
            .reserved = 0U,
        };
        jarvis_transport_hash256 styled_hash{};
        if (jarvis_tap_fingerprint_compute_canonical(
                &fingerprint_request,
                &styled_hash) !=
                JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED ||
            !HashesMatch(
                styled_hash,
                request->bind.expected_styled_value_sha256[index])) {
            return RejectBeforeWrite(
                instance,
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_HASH_MISMATCH);
        }
        any_change = any_change ||
            !ValuesMatch(
                adapter->canonical_values[index],
                request->styled_values[index]);
    }
    if (!any_change) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_NO_CHANGE);
    }

    instance->bind = request->bind;
    instance->preview_deadline_monotonic_ms = deadline;
    instance->next_sequence = 1U;
    for (std::uint32_t surface = 0U;
         surface < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++surface) {
        instance->surface_instance_handles[surface] =
            adapter->fingerprint.surface_instance_handles[surface];
        instance->selector_sha256[surface] =
            request->bind.expected_selector_sha256[surface];
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        instance->original_values[index] =
            adapter->canonical_values[index];
        instance->styled_values[index] =
            request->styled_values[index];
    }
    instance->state =
        JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED;
    return MakeResponse(
        instance,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED,
        0U);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_record_apply(
    jarvis_tap_style_transaction_instance* const instance,
    const jarvis_tap_style_step_request* const request,
    const std::uint32_t platform_write_attempted,
    const std::uint32_t platform_write_succeeded,
    const std::uint32_t verification_read_succeeded) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    if (instance->state !=
            JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED &&
        instance->state !=
            JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLYING) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    const auto index = instance->next_apply_index;
    if (index >= JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    const auto validation = ValidateStep(*instance, *request, index);
    if (validation != JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED) {
        return RejectBeforeWrite(instance, validation);
    }
    if (!FlagValid(platform_write_attempted)) {
        if (platform_write_attempted != 0U) {
            return RecordAttemptFailure(
                instance,
                index,
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED);
        }
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED);
    }
    if (platform_write_attempted != 1U) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED);
    }
    instance->dirty_mask |= 1U << index;
    ++instance->simulated_write_attempt_count;
    ++instance->next_sequence;
    if (!FlagValid(platform_write_succeeded) ||
        platform_write_succeeded != 1U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED,
            0U);
    }
    if (!FlagValid(verification_read_succeeded) ||
        verification_read_succeeded != 1U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED,
            0U);
    }
    ++instance->verification_count;
    if (!ValidateCanonical(request->observed_value) ||
        !ValuesMatch(
            request->observed_value,
            instance->styled_values[index])) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH,
            0U);
    }

    ++instance->verified_apply_count;
    ++instance->next_apply_index;
    if (instance->verified_apply_count ==
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLIED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_APPLIED,
            0U);
    }
    instance->state =
        JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLYING;
    return MakeResponse(
        instance,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED,
        0U);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_require_restore(
    jarvis_tap_style_transaction_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    if (instance->dirty_mask == 0U) {
        if (instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED) {
            instance->state =
                JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED;
            return MakeResponse(
                instance,
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED,
                0U);
        }
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    if (instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLYING ||
        instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLIED ||
        instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED ||
        instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORING) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_REQUIRED,
            0U);
    }
    return RejectBeforeWrite(
        instance,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_tick(
    jarvis_tap_style_transaction_instance* const instance,
    const std::uint64_t now_monotonic_ms) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    if (instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED ||
        instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED) {
        return MakeResponse(
            instance,
            instance->state ==
                    JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED
                ? JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED
                : JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY,
            0U);
    }
    if (instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_COLD ||
        instance->state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    if (now_monotonic_ms <
        instance->preview_deadline_monotonic_ms) {
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY,
            0U);
    }
    if (instance->dirty_mask == 0U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT,
            1U);
    }
    instance->state =
        JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
    return MakeResponse(
        instance,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT,
        1U);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_record_restore(
    jarvis_tap_style_transaction_instance* const instance,
    const jarvis_tap_style_step_request* const request,
    const std::uint32_t platform_write_attempted,
    const std::uint32_t platform_write_succeeded,
    const std::uint32_t verification_read_succeeded) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    if (instance->state !=
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED &&
        instance->state !=
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORING) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    const auto index = HighestDirtyIndex(instance->dirty_mask);
    if (index >= JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        return RejectBeforeWrite(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID);
    }
    const auto validation = ValidateStep(*instance, *request, index);
    if (validation != JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            validation == JARVIS_TAP_STYLE_TRANSACTION_RESULT_SLOT_INVALID
                ? JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_ORDER_INVALID
                : validation,
            0U);
    }
    if (!FlagValid(platform_write_attempted)) {
        if (platform_write_attempted != 0U) {
            ++instance->simulated_write_attempt_count;
            ++instance->next_sequence;
            return MakeResponse(
                instance,
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED,
                0U);
        }
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED,
            0U);
    }
    if (platform_write_attempted != 1U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED,
            0U);
    }
    ++instance->simulated_write_attempt_count;
    ++instance->next_sequence;
    if (!FlagValid(platform_write_succeeded) ||
        platform_write_succeeded != 1U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED,
            0U);
    }
    if (!FlagValid(verification_read_succeeded) ||
        verification_read_succeeded != 1U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED,
            0U);
    }
    ++instance->verification_count;
    if (!ValidateCanonical(request->observed_value) ||
        !ValuesMatch(
            request->observed_value,
            instance->original_values[index])) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH,
            0U);
    }

    instance->dirty_mask &= ~(1U << index);
    ++instance->verified_restore_count;
    if (instance->dirty_mask == 0U) {
        instance->state =
            JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED;
        return MakeResponse(
            instance,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED,
            0U);
    }
    instance->state =
        JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORING;
    return MakeResponse(
        instance,
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED,
        0U);
}

jarvis_tap_style_transaction_response
jarvis_tap_style_transaction_query(
    const jarvis_tap_style_transaction_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT,
            0U);
    }
    auto result = JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY;
    if (instance->state ==
        JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED) {
        result = JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED;
    }
    else if (instance->dirty_mask != 0U) {
        result =
            JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_REQUIRED;
    }
    return MakeResponse(instance, result, 0U);
}
