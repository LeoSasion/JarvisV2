#include "jarvis_explorer_tap_inspectable_adapter.h"

#include <cstring>

namespace {

[[nodiscard]] jarvis_tap_inspectable_adapter_response MakeResponse(
    const jarvis_tap_inspectable_adapter_instance* const instance,
    const jarvis_tap_adapter_result result,
    const std::uint32_t forwarded,
    const jarvis_tap_canonical_property_value& canonical,
    const jarvis_tap_fingerprint_response& fingerprint) noexcept {
    return jarvis_tap_inspectable_adapter_response{
        .size = sizeof(jarvis_tap_inspectable_adapter_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = instance == nullptr
            ? JARVIS_TAP_ADAPTER_STATE_COLD
            : instance->state,
        .result = result,
        .canonical_value_count = instance == nullptr
            ? 0U
            : instance->canonical_value_count,
        .forwarded_to_fingerprint = forwarded,
        .canonical_value = canonical,
        .fingerprint = fingerprint,
        .adapter_model_supported = 1U,
        .property_read_supported = 0U,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] jarvis_tap_inspectable_adapter_response Block(
    jarvis_tap_inspectable_adapter_instance* const instance,
    const jarvis_tap_adapter_result result) noexcept {
    if (instance != nullptr) {
        instance->state = JARVIS_TAP_ADAPTER_STATE_BLOCKED;
        instance->fingerprint.state =
            JARVIS_TAP_FINGERPRINT_STATE_BLOCKED;
    }
    return MakeResponse(
        instance,
        result,
        0U,
        {},
        instance == nullptr
            ? jarvis_tap_fingerprint_query_contract()
            : jarvis_tap_fingerprint_query(&instance->fingerprint));
}

}  // namespace

void jarvis_tap_inspectable_adapter_reset(
    jarvis_tap_inspectable_adapter_instance* const instance) noexcept {
    if (instance != nullptr) {
        std::memset(instance, 0, sizeof(*instance));
        instance->state = JARVIS_TAP_ADAPTER_STATE_COLD;
        jarvis_tap_fingerprint_reset(&instance->fingerprint);
    }
}

jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_query_contract() noexcept {
    return MakeResponse(
        nullptr,
        JARVIS_TAP_ADAPTER_RESULT_MODEL_ONLY,
        0U,
        {},
        jarvis_tap_fingerprint_query_contract());
}

jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_bind(
    jarvis_tap_inspectable_adapter_instance* const instance,
    const jarvis_tap_admission_instance* const admission,
    const jarvis_transport_bind_request* const bind) noexcept {
    if (instance == nullptr || admission == nullptr || bind == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT,
            0U,
            {},
            jarvis_tap_fingerprint_query_contract());
    }
    if (instance->state != JARVIS_TAP_ADAPTER_STATE_COLD) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID);
    }

    const auto fingerprint_response =
        jarvis_tap_fingerprint_bind(
            &instance->fingerprint,
            admission,
            bind);
    if (fingerprint_response.result !=
        JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED);
    }
    instance->state = JARVIS_TAP_ADAPTER_STATE_READY;
    return MakeResponse(
        instance,
        JARVIS_TAP_ADAPTER_RESULT_ACCEPTED,
        0U,
        {},
        fingerprint_response);
}

jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_observe(
    jarvis_tap_inspectable_adapter_instance* const instance,
    const jarvis_tap_runtime_property_snapshot* const snapshot) noexcept {
    if (instance == nullptr || snapshot == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT,
            0U,
            {},
            jarvis_tap_fingerprint_query_contract());
    }
    if (instance->state != JARVIS_TAP_ADAPTER_STATE_READY &&
        instance->state != JARVIS_TAP_ADAPTER_STATE_COLLECTING) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID);
    }
    if (snapshot->size !=
        sizeof(jarvis_tap_runtime_property_snapshot)) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_SIZE_MISMATCH);
    }
    if (snapshot->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_ABI_MISMATCH);
    }
    if (snapshot->reserved != 0U ||
        snapshot->reserved2 != 0U) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    }
    if (snapshot->value_origin !=
        JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_VALUE_ORIGIN_UNSUPPORTED);
    }

    jarvis_tap_canonical_property_value canonical{};
    if (snapshot->runtime_value_kind ==
        JARVIS_TAP_RUNTIME_VALUE_NULL) {
        if (snapshot->runtime_class !=
                JARVIS_TAP_RUNTIME_CLASS_NONE ||
            snapshot->exact_runtime_class_name_matched != 0U ||
            snapshot->argb != 0U ||
            snapshot->opacity_millionths != 0U) {
            return Block(
                instance,
                JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
        }
        canonical.value_kind = JARVIS_TAP_PROPERTY_VALUE_NULL;
    }
    else if (snapshot->runtime_value_kind ==
        JARVIS_TAP_RUNTIME_VALUE_OBJECT) {
        if (snapshot->runtime_class !=
            JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH) {
            return Block(
                instance,
                JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNSUPPORTED);
        }
        if (snapshot->exact_runtime_class_name_matched != 1U) {
            return Block(
                instance,
                JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNVERIFIED);
        }
        if (snapshot->opacity_millionths >
            JARVIS_TAP_OPACITY_MILLIONTHS_MAX) {
            return Block(
                instance,
                JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
        }
        canonical.value_kind =
            JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR;
        canonical.argb = snapshot->argb;
        canonical.opacity_millionths =
            snapshot->opacity_millionths;
    }
    else {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_RUNTIME_KIND_UNSUPPORTED);
    }

    const auto index = instance->canonical_value_count;
    if (index >= JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID);
    }
    const jarvis_tap_fingerprint_request request{
        .size = sizeof(jarvis_tap_fingerprint_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = snapshot->sequence,
        .target = snapshot->target,
        .surface_slot = snapshot->surface_slot,
        .property_slot = snapshot->property_slot,
        .instance_handle = snapshot->instance_handle,
        .selector_sha256 = snapshot->selector_sha256,
        .value_kind = canonical.value_kind,
        .argb = canonical.argb,
        .opacity_millionths = canonical.opacity_millionths,
        .reserved = 0U,
    };
    const auto fingerprint_response =
        jarvis_tap_fingerprint_observe(
            &instance->fingerprint,
            &request);
    if (fingerprint_response.result !=
            JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED &&
        fingerprint_response.result !=
            JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE) {
        return Block(
            instance,
            JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED);
    }

    instance->canonical_values[index] = canonical;
    ++instance->canonical_value_count;
    const bool complete =
        fingerprint_response.result ==
            JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE;
    instance->state = complete
        ? JARVIS_TAP_ADAPTER_STATE_COMPLETE
        : JARVIS_TAP_ADAPTER_STATE_COLLECTING;
    return MakeResponse(
        instance,
        complete
            ? JARVIS_TAP_ADAPTER_RESULT_COMPLETE
            : JARVIS_TAP_ADAPTER_RESULT_ACCEPTED,
        1U,
        canonical,
        fingerprint_response);
}

jarvis_tap_inspectable_adapter_response
jarvis_tap_inspectable_adapter_query(
    const jarvis_tap_inspectable_adapter_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT,
            0U,
            {},
            jarvis_tap_fingerprint_query_contract());
    }
    const auto canonical =
        instance->canonical_value_count == 0U
            ? jarvis_tap_canonical_property_value{}
            : instance->canonical_values[
                  instance->canonical_value_count - 1U];
    return MakeResponse(
        instance,
        instance->state == JARVIS_TAP_ADAPTER_STATE_COMPLETE
            ? JARVIS_TAP_ADAPTER_RESULT_COMPLETE
            : JARVIS_TAP_ADAPTER_RESULT_MODEL_ONLY,
        0U,
        canonical,
        jarvis_tap_fingerprint_query(&instance->fingerprint));
}
