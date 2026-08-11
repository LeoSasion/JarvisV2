#include "jarvis_explorer_exact_thread_transport_internal.h"

#include <atomic>
#include <cstdint>

namespace {

[[nodiscard]] jarvis_exact_thread_transport_result ResultForState(
    const jarvis_exact_thread_transport_state state) noexcept {
    switch (state) {
        case JARVIS_EXACT_THREAD_STATE_READY:
        case JARVIS_EXACT_THREAD_STATE_ACTIVE:
            return JARVIS_EXACT_THREAD_RESULT_SUCCESS;
        case JARVIS_EXACT_THREAD_STATE_INSTALLING:
        case JARVIS_EXACT_THREAD_STATE_DRAINING:
            return JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING;
        case JARVIS_EXACT_THREAD_STATE_QUIESCED:
            return JARVIS_EXACT_THREAD_RESULT_QUIESCED;
        case JARVIS_EXACT_THREAD_STATE_BLOCKED:
            return JARVIS_EXACT_THREAD_RESULT_BLOCKED;
        case JARVIS_EXACT_THREAD_STATE_FAULTED:
            return JARVIS_EXACT_THREAD_RESULT_FAULTED;
        default:
            return JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT;
    }
}

[[nodiscard]] jarvis_exact_thread_transport_result WriteResponse(
    const jarvis_exact_thread_transport_instance* const instance,
    const jarvis_exact_thread_transport_result result,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (response == nullptr) {
        return JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT;
    }

    jarvis_bridge_core_response bridge_response{};
    const bool has_bridge = instance != nullptr && instance->bridge != nullptr;
    if (has_bridge) {
        static_cast<void>(jarvis_bridge_core_query(
            instance->bridge,
            &bridge_response));
    }
    const auto state = instance == nullptr
        ? JARVIS_EXACT_THREAD_STATE_COLD
        : instance->state.load(std::memory_order_acquire);
    const auto hook_entry_published = instance == nullptr
        ? 0U
        : instance->hook_entry_published.load(std::memory_order_acquire);
    const auto hook_removed = instance == nullptr
        ? 0U
        : instance->hook_removed.load(std::memory_order_acquire);
    const auto module_pin_required =
        hook_entry_published != 0U ||
        (has_bridge && bridge_response.module_pin_required != 0U)
        ? 1U
        : 0U;
    const auto unload_permitted =
        state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
        hook_entry_published == 0U && has_bridge &&
        bridge_response.unload_permitted == 1U
        ? 1U
        : 0U;
    *response = jarvis_exact_thread_transport_response{
        .size = sizeof(jarvis_exact_thread_transport_response),
        .abi_version = JARVIS_EXACT_THREAD_TRANSPORT_ABI_VERSION,
        .state = state,
        .result = result,
        .explorer_process_id = instance == nullptr
            ? 0U
            : instance->explorer_process_id,
        .shell_thread_id = instance == nullptr
            ? 0U
            : instance->shell_thread_id,
        .prepare_attempt_count = instance == nullptr
            ? 0U
            : instance->prepare_attempt_count.load(std::memory_order_acquire),
        .install_attempt_count = instance == nullptr
            ? 0U
            : instance->install_attempt_count.load(std::memory_order_acquire),
        .unhook_attempt_count = instance == nullptr
            ? 0U
            : instance->unhook_attempt_count.load(std::memory_order_acquire),
        .target_validation_count = instance == nullptr
            ? 0U
            : instance->target_validation_count.load(
                  std::memory_order_acquire),
        .hook_entry_published = hook_entry_published,
        .hook_removed = hook_removed,
        .pass_through = has_bridge ? bridge_response.pass_through : 1U,
        .module_pin_required = module_pin_required,
        .unload_permitted = unload_permitted,
        .live_explorer_touched = instance == nullptr
            ? 0U
            : instance->live_explorer_touched.load(
                  std::memory_order_acquire),
        .mutation_performed = 0U,
        .activation_permitted = 0U,
        .last_platform_error = instance == nullptr
            ? 0U
            : instance->last_platform_error.load(std::memory_order_acquire),
        .reserved = 0U,
    };
    return result;
}

void CloseBridge(
    jarvis_exact_thread_transport_instance* const instance) noexcept {
    if (instance != nullptr && instance->bridge != nullptr) {
        jarvis_bridge_core_response bridge_response{};
        static_cast<void>(jarvis_bridge_core_begin_quiesce(
            instance->bridge,
            &bridge_response));
    }
}

void Block(
    jarvis_exact_thread_transport_instance* const instance,
    const jarvis_exact_thread_transport_result result,
    jarvis_exact_thread_transport_response* const response) noexcept {
    instance->cancel_requested.store(1U, std::memory_order_release);
    CloseBridge(instance);
    instance->state.store(
        JARVIS_EXACT_THREAD_STATE_BLOCKED,
        std::memory_order_release);
    static_cast<void>(WriteResponse(instance, result, response));
}

[[nodiscard]] jarvis_exact_thread_transport_result CompleteUnhook(
    jarvis_exact_thread_transport_instance* const instance,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (instance->install_in_flight.load(std::memory_order_acquire) != 0U) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING,
            response);
    }

    const auto hook_handle =
        instance->hook_handle.load(std::memory_order_acquire);
    if (hook_handle != 0U &&
        instance->hook_removed.load(std::memory_order_acquire) == 0U) {
        auto expected = 0U;
        if (!instance->unhook_started.compare_exchange_strong(
                expected,
                1U,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            return WriteResponse(
                instance,
                JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING,
                response);
        }

        instance->unhook_attempt_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        std::uint32_t platform_error = 0U;
        if (instance->platform.remove_exact_thread_hook(
                instance->platform.context,
                hook_handle,
                &platform_error) != 1U) {
            instance->last_platform_error.store(
                platform_error,
                std::memory_order_release);
            instance->state.store(
                JARVIS_EXACT_THREAD_STATE_FAULTED,
                std::memory_order_release);
            return WriteResponse(
                instance,
                JARVIS_EXACT_THREAD_RESULT_UNHOOK_FAILED,
                response);
        }
        instance->last_platform_error.store(0U, std::memory_order_release);
        instance->hook_removed.store(1U, std::memory_order_release);
        instance->hook_handle.store(0U, std::memory_order_release);
    }

    jarvis_bridge_core_response bridge_response{};
    const auto bridge_result = jarvis_bridge_core_begin_quiesce(
        instance->bridge,
        &bridge_response);
    if (bridge_result == JARVIS_BRIDGE_CORE_RESULT_QUIESCED) {
        instance->state.store(
            JARVIS_EXACT_THREAD_STATE_QUIESCED,
            std::memory_order_release);
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_QUIESCED,
            response);
    }
    return WriteResponse(
        instance,
        JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING,
        response);
}

}  // namespace

void jarvis_exact_thread_transport_reset_for_test(
    jarvis_exact_thread_transport_instance* const instance) noexcept {
    if (instance == nullptr) {
        return;
    }
    instance->state.store(JARVIS_EXACT_THREAD_STATE_COLD);
    instance->prepare_attempt_count.store(0U);
    instance->install_attempt_count.store(0U);
    instance->unhook_attempt_count.store(0U);
    instance->target_validation_count.store(0U);
    instance->hook_entry_published.store(0U);
    instance->hook_removed.store(0U);
    instance->live_explorer_touched.store(0U);
    instance->last_platform_error.store(0U);
    instance->cancel_requested.store(0U);
    instance->install_in_flight.store(0U);
    instance->unhook_started.store(0U);
    instance->hook_handle.store(0U);
    instance->platform = jarvis_exact_thread_platform_api{};
    instance->bridge = nullptr;
    instance->explorer_process_id = 0U;
    instance->shell_thread_id = 0U;
    instance->shell_window_handle = 0U;
    instance->module_handle = 0U;
    instance->hook_procedure = 0U;
    instance->session_nonce = 0U;
}

jarvis_exact_thread_transport_result jarvis_exact_thread_transport_prepare(
    jarvis_exact_thread_transport_instance* const instance,
    const jarvis_exact_thread_transport_request* const request,
    const jarvis_exact_thread_platform_api* const platform,
    jarvis_bridge_core_instance* const bridge,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (instance == nullptr || request == nullptr || platform == nullptr ||
        bridge == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT,
            response);
    }

    instance->prepare_attempt_count.fetch_add(1U, std::memory_order_acq_rel);
    auto expected = JARVIS_EXACT_THREAD_STATE_COLD;
    if (!instance->state.compare_exchange_strong(
            expected,
            JARVIS_EXACT_THREAD_STATE_READY,
            std::memory_order_acq_rel,
            std::memory_order_acquire)) {
        static_cast<void>(jarvis_exact_thread_transport_quiesce(
            instance,
            response));
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT,
            response);
    }

    instance->bridge = bridge;
    if (request->size != sizeof(jarvis_exact_thread_transport_request) ||
        platform->size != sizeof(jarvis_exact_thread_platform_api)) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_SIZE_MISMATCH, response);
        return JARVIS_EXACT_THREAD_RESULT_SIZE_MISMATCH;
    }
    if (request->abi_version !=
        JARVIS_EXACT_THREAD_TRANSPORT_ABI_VERSION) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_ABI_MISMATCH, response);
        return JARVIS_EXACT_THREAD_RESULT_ABI_MISMATCH;
    }
    if (request->explorer_process_id == 0U ||
        request->shell_thread_id == 0U ||
        request->shell_window_handle == 0U ||
        request->module_handle == 0U || request->hook_procedure == 0U ||
        request->session_nonce == 0U || request->reserved0 != 0U ||
        request->reserved1 != 0U || request->reserved2 != 0U) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH, response);
        return JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH;
    }
    if (request->host_admission_passed != 1U ||
        request->kill_switch_armed != 1U ||
        request->one_shot_permit_valid != 1U ||
        request->transport_scope != JARVIS_EXACT_THREAD_SCOPE ||
        request->architecture_match != 1U) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED, response);
        return JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED;
    }
    if ((platform->execution_kind != JARVIS_TRANSPORT_EXECUTION_SYNTHETIC &&
         platform->execution_kind !=
             JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE) ||
        platform->validate_exact_target == nullptr ||
        platform->install_exact_thread_hook == nullptr ||
        platform->remove_exact_thread_hook == nullptr) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED, response);
        return JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED;
    }

    jarvis_bridge_core_response bridge_response{};
    static_cast<void>(jarvis_bridge_core_query(bridge, &bridge_response));
    if (bridge_response.state != JARVIS_BRIDGE_CORE_STATE_READY ||
        bridge_response.pass_through != 1U ||
        bridge_response.external_entry_published != 0U) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_BRIDGE_NOT_READY, response);
        return JARVIS_EXACT_THREAD_RESULT_BRIDGE_NOT_READY;
    }
    if (bridge->explorer_process_id != request->explorer_process_id ||
        bridge->shell_thread_id != request->shell_thread_id ||
        bridge->session_nonce != request->session_nonce) {
        Block(instance, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH, response);
        return JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH;
    }

    instance->platform = *platform;
    instance->explorer_process_id = request->explorer_process_id;
    instance->shell_thread_id = request->shell_thread_id;
    instance->shell_window_handle = request->shell_window_handle;
    instance->module_handle = request->module_handle;
    instance->hook_procedure = request->hook_procedure;
    instance->session_nonce = request->session_nonce;
    return WriteResponse(instance, JARVIS_EXACT_THREAD_RESULT_SUCCESS, response);
}

jarvis_exact_thread_transport_result jarvis_exact_thread_transport_install(
    jarvis_exact_thread_transport_instance* const instance,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT,
            response);
    }
    instance->install_attempt_count.fetch_add(1U, std::memory_order_acq_rel);
    auto expected = JARVIS_EXACT_THREAD_STATE_READY;
    if (!instance->state.compare_exchange_strong(
            expected,
            JARVIS_EXACT_THREAD_STATE_INSTALLING,
            std::memory_order_acq_rel,
            std::memory_order_acquire)) {
        static_cast<void>(jarvis_exact_thread_transport_quiesce(
            instance,
            response));
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT,
            response);
    }
    instance->install_in_flight.store(1U, std::memory_order_release);

    instance->target_validation_count.fetch_add(1U, std::memory_order_acq_rel);
    std::uint32_t platform_error = 0U;
    if (instance->platform.validate_exact_target(
            instance->platform.context,
            instance->explorer_process_id,
            instance->shell_thread_id,
            instance->shell_window_handle,
            &platform_error) != 1U) {
        instance->last_platform_error.store(
            platform_error,
            std::memory_order_release);
        instance->install_in_flight.store(0U, std::memory_order_release);
        Block(
            instance,
            JARVIS_EXACT_THREAD_RESULT_TARGET_VALIDATION_FAILED,
            response);
        return JARVIS_EXACT_THREAD_RESULT_TARGET_VALIDATION_FAILED;
    }

    std::uint64_t hook_handle = 0U;
    const auto install_status = instance->platform.install_exact_thread_hook(
            instance->platform.context,
            instance->shell_thread_id,
            instance->module_handle,
            instance->hook_procedure,
            &hook_handle,
            &platform_error);
    if (install_status != 1U || hook_handle == 0U) {
        instance->last_platform_error.store(
            platform_error,
            std::memory_order_release);
        if (hook_handle != 0U) {
            // A contradictory platform result is treated as a published Hook,
            // never as proof that no external entry exists.
            instance->hook_handle.store(
                hook_handle,
                std::memory_order_release);
            instance->hook_entry_published.store(
                1U,
                std::memory_order_release);
            instance->live_explorer_touched.store(
                instance->platform.execution_kind ==
                        JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE
                    ? 1U
                    : 0U,
                std::memory_order_release);
            jarvis_bridge_core_response bridge_response{};
            static_cast<void>(jarvis_bridge_core_publish_transport(
                instance->bridge,
                instance->explorer_process_id,
                instance->shell_thread_id,
                instance->session_nonce,
                instance->live_explorer_touched.load(
                    std::memory_order_acquire),
                &bridge_response));
            instance->install_in_flight.store(
                0U,
                std::memory_order_release);
            CloseBridge(instance);
            instance->state.store(
                JARVIS_EXACT_THREAD_STATE_DRAINING,
                std::memory_order_release);
            const auto cleanup_result = CompleteUnhook(instance, response);
            if (cleanup_result == JARVIS_EXACT_THREAD_RESULT_UNHOOK_FAILED) {
                return cleanup_result;
            }
            instance->last_platform_error.store(
                platform_error,
                std::memory_order_release);
            return WriteResponse(
                instance,
                JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED,
                response);
        }
        instance->install_in_flight.store(0U, std::memory_order_release);
        if (instance->cancel_requested.load(std::memory_order_acquire) != 0U) {
            instance->state.store(
                JARVIS_EXACT_THREAD_STATE_DRAINING,
                std::memory_order_release);
            return CompleteUnhook(instance, response);
        }
        Block(
            instance,
            JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED,
            response);
        return JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED;
    }

    instance->hook_handle.store(hook_handle, std::memory_order_release);
    instance->hook_entry_published.store(1U, std::memory_order_release);
    instance->live_explorer_touched.store(
        instance->platform.execution_kind ==
                JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE
            ? 1U
            : 0U,
        std::memory_order_release);
    jarvis_bridge_core_response bridge_response{};
    const auto bridge_result = jarvis_bridge_core_publish_transport(
        instance->bridge,
        instance->explorer_process_id,
        instance->shell_thread_id,
        instance->session_nonce,
        instance->live_explorer_touched.load(std::memory_order_acquire),
        &bridge_response);

    auto installing = JARVIS_EXACT_THREAD_STATE_INSTALLING;
    const bool activated =
        bridge_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
        instance->cancel_requested.load(std::memory_order_acquire) == 0U &&
        instance->state.compare_exchange_strong(
            installing,
            JARVIS_EXACT_THREAD_STATE_ACTIVE,
            std::memory_order_acq_rel,
            std::memory_order_acquire);
    instance->install_in_flight.store(0U, std::memory_order_release);
    if (!activated ||
        instance->cancel_requested.load(std::memory_order_acquire) != 0U ||
        instance->state.load(std::memory_order_acquire) !=
            JARVIS_EXACT_THREAD_STATE_ACTIVE) {
        CloseBridge(instance);
        instance->state.store(
            JARVIS_EXACT_THREAD_STATE_DRAINING,
            std::memory_order_release);
        return CompleteUnhook(instance, response);
    }
    instance->last_platform_error.store(0U, std::memory_order_release);
    return WriteResponse(instance, JARVIS_EXACT_THREAD_RESULT_SUCCESS, response);
}

jarvis_exact_thread_transport_result jarvis_exact_thread_transport_quiesce(
    jarvis_exact_thread_transport_instance* const instance,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT,
            response);
    }
    const auto state = instance->state.load(std::memory_order_acquire);
    if (state == JARVIS_EXACT_THREAD_STATE_FAULTED) {
        CloseBridge(instance);
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_FAULTED,
            response);
    }
    if (state == JARVIS_EXACT_THREAD_STATE_COLD ||
        state == JARVIS_EXACT_THREAD_STATE_BLOCKED) {
        CloseBridge(instance);
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT,
            response);
    }
    if (state == JARVIS_EXACT_THREAD_STATE_QUIESCED) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_QUIESCED,
            response);
    }

    instance->cancel_requested.store(1U, std::memory_order_release);
    CloseBridge(instance);
    instance->state.store(
        JARVIS_EXACT_THREAD_STATE_DRAINING,
        std::memory_order_release);
    return CompleteUnhook(instance, response);
}

jarvis_exact_thread_transport_result jarvis_exact_thread_transport_poll(
    jarvis_exact_thread_transport_instance* const instance,
    jarvis_exact_thread_transport_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT,
            response);
    }
    const auto state = instance->state.load(std::memory_order_acquire);
    if (state == JARVIS_EXACT_THREAD_STATE_DRAINING) {
        return CompleteUnhook(instance, response);
    }
    return WriteResponse(instance, ResultForState(state), response);
}
