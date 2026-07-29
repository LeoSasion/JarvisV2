#include "jarvis_explorer_bridge_contract.h"

namespace {

[[nodiscard]] constexpr jarvis_bridge_response MakeResponse(
    const jarvis_bridge_state state,
    const jarvis_bridge_result result) noexcept {
    return jarvis_bridge_response{
        .size = sizeof(jarvis_bridge_response),
        .abi_version = JARVIS_EXPLORER_BRIDGE_ABI_VERSION,
        .state = state,
        .result = result,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
    };
}

}  // namespace

void jarvis_bridge_model_reset(jarvis_bridge_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return;
    }

    instance->state = JARVIS_BRIDGE_STATE_COLD;
    instance->initialize_attempt_count = 0U;
}

jarvis_bridge_response jarvis_bridge_model_query_contract() noexcept {
    return MakeResponse(
        JARVIS_BRIDGE_STATE_COLD,
        JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED);
}

jarvis_bridge_response jarvis_bridge_model_initialize(
    jarvis_bridge_model_instance* const instance,
    const jarvis_bridge_init_request* const request) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            JARVIS_BRIDGE_STATE_BLOCKED,
            JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);
    }

    if (instance->initialize_attempt_count != 0U) {
        instance->state = JARVIS_BRIDGE_STATE_BLOCKED;
        return MakeResponse(
            instance->state,
            JARVIS_BRIDGE_RESULT_ALREADY_INITIALIZED);
    }
    ++instance->initialize_attempt_count;

    if (request->size != sizeof(jarvis_bridge_init_request)) {
        instance->state = JARVIS_BRIDGE_STATE_BLOCKED;
        return MakeResponse(
            instance->state,
            JARVIS_BRIDGE_RESULT_REQUEST_SIZE_MISMATCH);
    }
    if (request->abi_version != JARVIS_EXPLORER_BRIDGE_ABI_VERSION) {
        instance->state = JARVIS_BRIDGE_STATE_BLOCKED;
        return MakeResponse(
            instance->state,
            JARVIS_BRIDGE_RESULT_ABI_MISMATCH);
    }
    if (request->explorer_process_id == 0U ||
        request->shell_thread_id == 0U ||
        request->session_nonce == 0U) {
        instance->state = JARVIS_BRIDGE_STATE_BLOCKED;
        return MakeResponse(
            instance->state,
            JARVIS_BRIDGE_RESULT_IDENTITY_INVALID);
    }

    // A structurally valid request still cannot execute. Phase 7 defines only
    // the reviewable ABI and fail-closed lifecycle, never a live transport.
    instance->state = JARVIS_BRIDGE_STATE_BLOCKED;
    return MakeResponse(
        instance->state,
        JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED);
}

jarvis_bridge_response jarvis_bridge_model_quiesce(
    jarvis_bridge_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            JARVIS_BRIDGE_STATE_BLOCKED,
            JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);
    }

    instance->state = JARVIS_BRIDGE_STATE_QUIESCED;
    return MakeResponse(instance->state, JARVIS_BRIDGE_RESULT_QUIESCED);
}

jarvis_bridge_response jarvis_bridge_model_query(
    const jarvis_bridge_model_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            JARVIS_BRIDGE_STATE_BLOCKED,
            JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);
    }

    const auto result =
        instance->state == JARVIS_BRIDGE_STATE_QUIESCED
            ? JARVIS_BRIDGE_RESULT_QUIESCED
            : JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED;
    return MakeResponse(instance->state, result);
}
