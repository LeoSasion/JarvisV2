#include "jarvis_explorer_bridge_core_internal.h"

#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace {

#if defined(_MSC_VER) && defined(_WIN32) && \
    defined(JARVIS_BRIDGE_CORE_SHARED_INSTANCE)
static_assert(std::atomic<std::uint32_t>::is_always_lock_free);
#pragma section(".jvbrdg", read, write, shared)
__declspec(allocate(".jvbrdg"))
constinit jarvis_bridge_core_instance global_instance{};
#pragma comment(linker, "/SECTION:.jvbrdg,RWS")
#else
constinit jarvis_bridge_core_instance global_instance{};
#endif

[[nodiscard]] bool IsDigestNonZero(const std::uint8_t* const digest) noexcept {
    if (digest == nullptr) {
        return false;
    }

    std::uint8_t aggregate = 0U;
    for (std::size_t index = 0U; index < 32U; ++index) {
        aggregate = static_cast<std::uint8_t>(aggregate | digest[index]);
    }
    return aggregate != 0U;
}

[[nodiscard]] jarvis_bridge_core_result ResultForState(
    const jarvis_bridge_core_state state) noexcept {
    switch (state) {
        case JARVIS_BRIDGE_CORE_STATE_READY:
        case JARVIS_BRIDGE_CORE_STATE_ACTIVE:
            return JARVIS_BRIDGE_CORE_RESULT_SUCCESS;
        case JARVIS_BRIDGE_CORE_STATE_DRAINING:
            return JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING;
        case JARVIS_BRIDGE_CORE_STATE_QUIESCED:
            return JARVIS_BRIDGE_CORE_RESULT_QUIESCED;
        case JARVIS_BRIDGE_CORE_STATE_BLOCKED:
            return JARVIS_BRIDGE_CORE_RESULT_BLOCKED;
        default:
            return JARVIS_BRIDGE_CORE_RESULT_CORE_ONLY_NO_TRANSPORT;
    }
}

[[nodiscard]] jarvis_bridge_core_result WriteResponse(
    const jarvis_bridge_core_instance* const instance,
    const jarvis_bridge_core_result result,
    jarvis_bridge_core_response* const response) noexcept {
    if (response == nullptr) {
        return JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT;
    }

    const auto state = instance == nullptr
        ? JARVIS_BRIDGE_CORE_STATE_COLD
        : instance->state.load(std::memory_order_acquire);
    const auto callback_count = instance == nullptr
        ? 0U
        : instance->active_callback_count.load(std::memory_order_acquire);
    const auto pass_through = instance == nullptr
        ? 1U
        : instance->pass_through.load(std::memory_order_acquire);
    const auto external_entry_published = instance == nullptr
        ? 0U
        : instance->external_entry_published.load(std::memory_order_acquire);
    const bool unload_permitted =
        state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
        callback_count == 0U &&
        external_entry_published == 0U;
    *response = jarvis_bridge_core_response{
        .size = sizeof(jarvis_bridge_core_response),
        .abi_version = JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION,
        .state = state,
        .result = result,
        .active_callback_count = callback_count,
        .pass_through = pass_through,
        .external_entry_published = external_entry_published,
        .module_pin_required = external_entry_published == 0U ? 0U : 1U,
        .unload_permitted = unload_permitted ? 1U : 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = instance == nullptr
            ? 0U
            : instance->live_explorer_touched.load(
                  std::memory_order_acquire),
        .initialize_attempt_count = instance == nullptr
            ? 0U
            : instance->initialize_attempt_count.load(
                  std::memory_order_acquire),
        .rejected_callback_count = instance == nullptr
            ? 0U
            : instance->rejected_callback_count.load(
                  std::memory_order_acquire),
        .generation = instance == nullptr
            ? 0U
            : instance->generation.load(std::memory_order_acquire),
        .reserved = 0U,
    };
    return result;
}

void PromoteDrainedInstance(jarvis_bridge_core_instance* const instance) noexcept {
    if (instance == nullptr ||
        instance->active_callback_count.load(std::memory_order_acquire) != 0U) {
        return;
    }

    auto expected = JARVIS_BRIDGE_CORE_STATE_DRAINING;
    static_cast<void>(instance->state.compare_exchange_strong(
        expected,
        JARVIS_BRIDGE_CORE_STATE_QUIESCED,
        std::memory_order_acq_rel,
        std::memory_order_acquire));
}

void RetireOnStateConflict(
    jarvis_bridge_core_instance* const instance) noexcept {
    instance->pass_through.store(1U, std::memory_order_release);
    auto state = instance->state.load(std::memory_order_acquire);
    while (state == JARVIS_BRIDGE_CORE_STATE_READY ||
           state == JARVIS_BRIDGE_CORE_STATE_ACTIVE) {
        if (instance->state.compare_exchange_weak(
                state,
                JARVIS_BRIDGE_CORE_STATE_DRAINING,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            break;
        }
    }
    // A concurrent publisher can observe Ready after the first store and
    // briefly publish pass-through false before losing its state transition.
    // Reassert the closed gate after retirement owns the final state.
    instance->pass_through.store(1U, std::memory_order_release);
    PromoteDrainedInstance(instance);
}

void Block(
    jarvis_bridge_core_instance* const instance,
    const jarvis_bridge_core_result result,
    jarvis_bridge_core_response* const response) noexcept {
    instance->pass_through.store(1U, std::memory_order_release);
    instance->state.store(
        JARVIS_BRIDGE_CORE_STATE_BLOCKED,
        std::memory_order_release);
    static_cast<void>(WriteResponse(instance, result, response));
}

}  // namespace

jarvis_bridge_core_instance* jarvis_bridge_core_global_instance() noexcept {
    return &global_instance;
}

void jarvis_bridge_core_reset_for_test(
    jarvis_bridge_core_instance* const instance) noexcept {
    if (instance == nullptr) {
        return;
    }

    instance->state.store(
        JARVIS_BRIDGE_CORE_STATE_COLD,
        std::memory_order_relaxed);
    instance->active_callback_count.store(0U, std::memory_order_relaxed);
    instance->pass_through.store(1U, std::memory_order_relaxed);
    instance->external_entry_published.store(0U, std::memory_order_relaxed);
    instance->initialize_attempt_count.store(0U, std::memory_order_relaxed);
    instance->rejected_callback_count.store(0U, std::memory_order_relaxed);
    instance->generation.store(0U, std::memory_order_relaxed);
    instance->live_explorer_touched.store(0U, std::memory_order_relaxed);
    instance->explorer_process_id = 0U;
    instance->shell_thread_id = 0U;
    instance->session_nonce = 0U;
    for (std::size_t index = 0U; index < 32U; ++index) {
        instance->settings_sha256[index] = 0U;
    }
}

jarvis_bridge_core_result jarvis_bridge_core_prepare(
    jarvis_bridge_core_instance* const instance,
    const jarvis_bridge_core_init_request* const request,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || request == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }

    instance->initialize_attempt_count.fetch_add(1U, std::memory_order_acq_rel);
    auto expected = JARVIS_BRIDGE_CORE_STATE_COLD;
    if (!instance->state.compare_exchange_strong(
            expected,
            JARVIS_BRIDGE_CORE_STATE_PREPARING,
            std::memory_order_acq_rel,
            std::memory_order_acquire)) {
        RetireOnStateConflict(instance);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_ALREADY_INITIALIZED,
            response);
    }

    if (request->size != sizeof(jarvis_bridge_core_init_request)) {
        Block(instance, JARVIS_BRIDGE_CORE_RESULT_SIZE_MISMATCH, response);
        return JARVIS_BRIDGE_CORE_RESULT_SIZE_MISMATCH;
    }
    if (request->abi_version != JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION) {
        Block(instance, JARVIS_BRIDGE_CORE_RESULT_ABI_MISMATCH, response);
        return JARVIS_BRIDGE_CORE_RESULT_ABI_MISMATCH;
    }
    if (request->explorer_process_id == 0U ||
        request->shell_thread_id == 0U ||
        request->session_nonce == 0U ||
        request->reserved0 != 0U ||
        request->reserved1 != 0U ||
        !IsDigestNonZero(request->settings_sha256)) {
        Block(instance, JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID, response);
        return JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID;
    }
    if (request->host_admission_passed != 1U ||
        request->kill_switch_armed != 1U ||
        request->one_shot_permit_valid != 1U ||
        request->transport_scope !=
            JARVIS_EXPLORER_BRIDGE_TRANSPORT_SCOPE_EXACT_THREAD) {
        Block(instance, JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED, response);
        return JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED;
    }

    instance->explorer_process_id = request->explorer_process_id;
    instance->shell_thread_id = request->shell_thread_id;
    instance->session_nonce = request->session_nonce;
    for (std::size_t index = 0U; index < 32U; ++index) {
        instance->settings_sha256[index] = request->settings_sha256[index];
    }
    instance->generation.store(1U, std::memory_order_release);
    instance->pass_through.store(1U, std::memory_order_release);
    instance->state.store(
        JARVIS_BRIDGE_CORE_STATE_READY,
        std::memory_order_release);
    return WriteResponse(
        instance,
        JARVIS_BRIDGE_CORE_RESULT_SUCCESS,
        response);
}

jarvis_bridge_core_result jarvis_bridge_core_publish_transport(
    jarvis_bridge_core_instance* const instance,
    const std::uint32_t explorer_process_id,
    const std::uint32_t shell_thread_id,
    const std::uint64_t session_nonce,
    const std::uint32_t live_explorer_touched,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }
    if (live_explorer_touched > 1U ||
        explorer_process_id != instance->explorer_process_id ||
        shell_thread_id != instance->shell_thread_id ||
        session_nonce != instance->session_nonce) {
        instance->rejected_callback_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_TRANSPORT_IDENTITY_MISMATCH,
            response);
    }

    // Publishing any external callback makes the module pin conservative for
    // the remainder of this Explorer lifetime. A quiesce race may therefore
    // retain an unused pin, but it can never claim an unsafe unload.
    instance->external_entry_published.store(1U, std::memory_order_release);
    instance->live_explorer_touched.store(
        live_explorer_touched,
        std::memory_order_release);
    instance->pass_through.store(0U, std::memory_order_release);
    auto expected = JARVIS_BRIDGE_CORE_STATE_READY;
    if (!instance->state.compare_exchange_strong(
            expected,
            JARVIS_BRIDGE_CORE_STATE_ACTIVE,
            std::memory_order_acq_rel,
            std::memory_order_acquire)) {
        RetireOnStateConflict(instance);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_STATE_CONFLICT,
            response);
    }
    return WriteResponse(
        instance,
        JARVIS_BRIDGE_CORE_RESULT_SUCCESS,
        response);
}

jarvis_bridge_core_result jarvis_bridge_core_try_enter_callback(
    jarvis_bridge_core_instance* const instance,
    const std::uint32_t observed_process_id,
    const std::uint32_t observed_thread_id,
    jarvis_bridge_callback_token* const token,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || token == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }

    token->acquired = 0U;
    token->generation = 0U;
    token->session_nonce = 0U;
    if (observed_process_id != instance->explorer_process_id ||
        observed_thread_id != instance->shell_thread_id ||
        instance->state.load(std::memory_order_acquire) !=
            JARVIS_BRIDGE_CORE_STATE_ACTIVE ||
        instance->pass_through.load(std::memory_order_acquire) != 0U) {
        instance->rejected_callback_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED,
            response);
    }

    instance->active_callback_count.fetch_add(1U, std::memory_order_acq_rel);
    if (instance->state.load(std::memory_order_acquire) !=
            JARVIS_BRIDGE_CORE_STATE_ACTIVE ||
        instance->pass_through.load(std::memory_order_acquire) != 0U) {
        instance->active_callback_count.fetch_sub(1U, std::memory_order_acq_rel);
        instance->rejected_callback_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        PromoteDrainedInstance(instance);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED,
            response);
    }

    token->session_nonce = instance->session_nonce;
    token->generation = instance->generation.load(std::memory_order_acquire);
    token->acquired = 1U;
    return WriteResponse(
        instance,
        JARVIS_BRIDGE_CORE_RESULT_SUCCESS,
        response);
}

jarvis_bridge_core_result jarvis_bridge_core_leave_callback(
    jarvis_bridge_core_instance* const instance,
    jarvis_bridge_callback_token* const token,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || token == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }
    if (token->acquired != 1U ||
        token->session_nonce != instance->session_nonce ||
        token->generation !=
            instance->generation.load(std::memory_order_acquire)) {
        instance->rejected_callback_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED,
            response);
    }

    token->acquired = 0U;
    const auto previous = instance->active_callback_count.fetch_sub(
        1U,
        std::memory_order_acq_rel);
    if (previous == 0U) {
        instance->active_callback_count.store(0U, std::memory_order_release);
        instance->rejected_callback_count.fetch_add(
            1U,
            std::memory_order_acq_rel);
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED,
            response);
    }

    PromoteDrainedInstance(instance);
    return WriteResponse(
        instance,
        JARVIS_BRIDGE_CORE_RESULT_SUCCESS,
        response);
}

jarvis_bridge_core_result jarvis_bridge_core_begin_quiesce(
    jarvis_bridge_core_instance* const instance,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }

    instance->pass_through.store(1U, std::memory_order_release);
    auto state = instance->state.load(std::memory_order_acquire);
    while (state == JARVIS_BRIDGE_CORE_STATE_READY ||
           state == JARVIS_BRIDGE_CORE_STATE_ACTIVE) {
        if (instance->state.compare_exchange_weak(
                state,
                JARVIS_BRIDGE_CORE_STATE_DRAINING,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            state = JARVIS_BRIDGE_CORE_STATE_DRAINING;
            break;
        }
    }

    // Preserve pass-through-before-drain while also closing the publication
    // race in which a publisher writes false between the first store and this
    // state transition.
    instance->pass_through.store(1U, std::memory_order_release);

    if (state == JARVIS_BRIDGE_CORE_STATE_COLD ||
        state == JARVIS_BRIDGE_CORE_STATE_PREPARING) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_STATE_CONFLICT,
            response);
    }
    if (state == JARVIS_BRIDGE_CORE_STATE_BLOCKED) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_BLOCKED,
            response);
    }

    PromoteDrainedInstance(instance);
    state = instance->state.load(std::memory_order_acquire);
    const auto result = state == JARVIS_BRIDGE_CORE_STATE_QUIESCED
        ? JARVIS_BRIDGE_CORE_RESULT_QUIESCED
        : JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING;
    return WriteResponse(instance, result, response);
}

jarvis_bridge_core_result jarvis_bridge_core_query(
    const jarvis_bridge_core_instance* const instance,
    jarvis_bridge_core_response* const response) noexcept {
    if (instance == nullptr || response == nullptr) {
        return WriteResponse(
            instance,
            JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT,
            response);
    }

    return WriteResponse(
        instance,
        ResultForState(instance->state.load(std::memory_order_acquire)),
        response);
}

jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL JarvisBridge_QueryContract(
    jarvis_bridge_core_response* const response) noexcept {
    return WriteResponse(
        nullptr,
        JARVIS_BRIDGE_CORE_RESULT_CORE_ONLY_NO_TRANSPORT,
        response);
}

jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL JarvisBridge_Initialize(
    const jarvis_bridge_core_init_request* const request,
    jarvis_bridge_core_response* const response) noexcept {
    return jarvis_bridge_core_prepare(&global_instance, request, response);
}

jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL JarvisBridge_Quiesce(
    jarvis_bridge_core_response* const response) noexcept {
    return jarvis_bridge_core_begin_quiesce(&global_instance, response);
}

jarvis_bridge_core_result JARVIS_BRIDGE_CORE_CALL JarvisBridge_QueryState(
    jarvis_bridge_core_response* const response) noexcept {
    return jarvis_bridge_core_query(&global_instance, response);
}
