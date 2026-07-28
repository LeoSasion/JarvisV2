#include "jarvis_explorer_tap_admission.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>

namespace {

[[nodiscard]] bool HashIsZero(
    const jarvis_transport_hash256& value) noexcept {
    return value.words[0] == 0U &&
           value.words[1] == 0U &&
           value.words[2] == 0U &&
           value.words[3] == 0U;
}

[[nodiscard]] jarvis_tap_admission_response MakeResponse(
    const jarvis_tap_admission_instance* const instance,
    const jarvis_tap_admission_result result,
    const std::uint32_t consumer_count,
    const std::uint32_t endpoint_count,
    const std::uint32_t binary_identity_accepted,
    const std::uint32_t recovery_ready) noexcept {
    return jarvis_tap_admission_response{
        .size = sizeof(jarvis_tap_admission_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = instance == nullptr
            ? JARVIS_TAP_ADMISSION_STATE_COLD
            : instance->state,
        .result = result,
        .attempt_count = instance == nullptr
            ? 0U
            : instance->attempt_count,
        .plan_consumed = instance == nullptr
            ? 0U
            : instance->plan_consumed,
        .observed_consumer_count = consumer_count,
        .endpoint_candidate_count = endpoint_count,
        .binary_identity_accepted = binary_identity_accepted,
        .recovery_ready = recovery_ready,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
    };
}

[[nodiscard]] jarvis_tap_admission_response Block(
    jarvis_tap_admission_instance* const instance,
    const jarvis_tap_admission_request* const request,
    const jarvis_tap_admission_result result) noexcept {
    if (instance != nullptr) {
        instance->state = JARVIS_TAP_ADMISSION_STATE_BLOCKED;
    }
    return MakeResponse(
        instance,
        result,
        request == nullptr ? 0U : request->observed_consumer_count,
        request == nullptr ? 0U : request->endpoint_candidate_count,
        request == nullptr ? 0U : request->binary_identity_passed,
        request == nullptr ? 0U : request->recovery_ready);
}

}  // namespace

void jarvis_tap_admission_reset(
    jarvis_tap_admission_instance* const instance) noexcept {
    if (instance != nullptr) {
        std::memset(instance, 0, sizeof(*instance));
        instance->state = JARVIS_TAP_ADMISSION_STATE_COLD;
    }
}

jarvis_tap_admission_response
jarvis_tap_admission_query_contract() noexcept {
    return MakeResponse(
        nullptr,
        JARVIS_TAP_ADMISSION_RESULT_MODEL_ONLY,
        0U,
        0U,
        0U,
        0U);
}

jarvis_tap_admission_response jarvis_tap_admission_evaluate(
    jarvis_tap_admission_instance* const instance,
    const jarvis_tap_admission_request* const request) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_ADMISSION_RESULT_INVALID_ARGUMENT,
            0U,
            0U,
            0U,
            0U);
    }

    if (instance->state != JARVIS_TAP_ADMISSION_STATE_COLD ||
        instance->attempt_count != 0U) {
        if (instance->attempt_count !=
            std::numeric_limits<std::uint32_t>::max()) {
            ++instance->attempt_count;
        }
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_REPLAY);
    }
    instance->attempt_count = 1U;

    if (request->size != sizeof(jarvis_tap_admission_request)) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_ABI_MISMATCH);
    }

    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded{};
    const auto protocol_receipt =
        jarvis_tap_encode_initialization_data(
            &request->bind,
            encoded.data(),
            encoded.size());
    if (protocol_receipt.result !=
            JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED ||
        protocol_receipt.live_connection_compiled != 0U ||
        protocol_receipt.execution_supported != 0U ||
        protocol_receipt.activation_permitted != 0U ||
        protocol_receipt.mutation_performed != 0U ||
        protocol_receipt.live_explorer_touched != 0U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_BIND_INVALID);
    }

    if (HashIsZero(request->controller_sha256) ||
        HashIsZero(request->tap_dll_sha256) ||
        HashIsZero(request->xaml_diagnostics_sha256) ||
        HashIsZero(request->endpoint_name_sha256)) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    }
    if (request->evaluated_at_monotonic_ms <
            request->bind.issued_at_monotonic_ms ||
        request->evaluated_at_monotonic_ms >
            request->bind.expires_at_monotonic_ms) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_CAPABILITY_NOT_CURRENT);
    }
    if (request->observed_consumer_count != 0U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_EXISTING_CONSUMER);
    }
    if (request->endpoint_candidate_count != 1U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_ENDPOINT_COUNT_INVALID);
    }
    if (request->tap_export_count != 2U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_TAP_EXPORT_SET_INVALID);
    }
    if (request->import_policy_passed != 1U ||
        request->binary_identity_passed != 1U) {
        return Block(
            instance,
            request,
            request->import_policy_passed != 1U
                ? JARVIS_TAP_ADMISSION_RESULT_IMPORT_POLICY_FAILED
                : JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    }
    if (request->recovery_ready != 1U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_RECOVERY_NOT_READY);
    }
    if (request->one_shot_plan_available != 1U ||
        request->reserved != 0U) {
        return Block(
            instance,
            request,
            JARVIS_TAP_ADMISSION_RESULT_PLAN_UNAVAILABLE);
    }

    instance->state = JARVIS_TAP_ADMISSION_STATE_ADMITTED;
    instance->plan_consumed = 1U;
    instance->bind = request->bind;
    instance->controller_sha256 = request->controller_sha256;
    instance->tap_dll_sha256 = request->tap_dll_sha256;
    instance->xaml_diagnostics_sha256 =
        request->xaml_diagnostics_sha256;
    instance->endpoint_name_sha256 =
        request->endpoint_name_sha256;
    instance->evaluated_at_monotonic_ms =
        request->evaluated_at_monotonic_ms;

    return MakeResponse(
        instance,
        JARVIS_TAP_ADMISSION_RESULT_ACCEPTED,
        0U,
        1U,
        1U,
        1U);
}

jarvis_tap_admission_response jarvis_tap_admission_query(
    const jarvis_tap_admission_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_ADMISSION_RESULT_INVALID_ARGUMENT,
            0U,
            0U,
            0U,
            0U);
    }
    return MakeResponse(
        instance,
        instance->state == JARVIS_TAP_ADMISSION_STATE_ADMITTED
            ? JARVIS_TAP_ADMISSION_RESULT_ACCEPTED
            : JARVIS_TAP_ADMISSION_RESULT_MODEL_ONLY,
        0U,
        instance->state == JARVIS_TAP_ADMISSION_STATE_ADMITTED
            ? 1U
            : 0U,
        instance->state == JARVIS_TAP_ADMISSION_STATE_ADMITTED
            ? 1U
            : 0U,
        instance->state == JARVIS_TAP_ADMISSION_STATE_ADMITTED
            ? 1U
            : 0U);
}
