#include "jarvis_explorer_exact_thread_transport_internal.h"

#include <atomic>
#include <cstdint>
#include <iostream>
#include <thread>

namespace {

std::uint32_t scenario_count = 0U;
std::uint32_t passed_count = 0U;

void Check(const bool condition) noexcept {
    ++scenario_count;
    if (condition) {
        ++passed_count;
    }
}

struct fake_platform_context final {
    std::atomic<std::uint32_t> validate_calls{0U};
    std::atomic<std::uint32_t> install_calls{0U};
    std::atomic<std::uint32_t> remove_calls{0U};
    std::atomic<std::uint32_t> validate_result{1U};
    std::atomic<std::uint32_t> install_result{1U};
    std::atomic<std::uint32_t> remove_result{1U};
    std::atomic<std::uint32_t> failure_handle_nonzero{0U};
    std::atomic<std::uint32_t> block_install{0U};
    std::atomic<std::uint32_t> install_entered{0U};
    std::atomic<std::uint32_t> release_install{0U};
    std::uint32_t observed_process_id{0U};
    std::uint32_t observed_thread_id{0U};
    std::uint64_t observed_window_handle{0U};
    std::uint64_t observed_module_handle{0U};
    std::uint64_t observed_hook_procedure{0U};
    std::uint64_t observed_remove_handle{0U};
};

std::uint32_t ValidateTarget(
    void* const context,
    const std::uint32_t process_id,
    const std::uint32_t thread_id,
    const std::uint64_t window_handle,
    std::uint32_t* const platform_error) noexcept {
    auto* const fake = static_cast<fake_platform_context*>(context);
    fake->validate_calls.fetch_add(1U, std::memory_order_acq_rel);
    fake->observed_process_id = process_id;
    fake->observed_thread_id = thread_id;
    fake->observed_window_handle = window_handle;
    const auto result = fake->validate_result.load(std::memory_order_acquire);
    *platform_error = result == 1U ? 0U : 101U;
    return result;
}

std::uint32_t InstallHook(
    void* const context,
    const std::uint32_t thread_id,
    const std::uint64_t module_handle,
    const std::uint64_t hook_procedure,
    std::uint64_t* const hook_handle,
    std::uint32_t* const platform_error) noexcept {
    auto* const fake = static_cast<fake_platform_context*>(context);
    fake->install_calls.fetch_add(1U, std::memory_order_acq_rel);
    fake->observed_thread_id = thread_id;
    fake->observed_module_handle = module_handle;
    fake->observed_hook_procedure = hook_procedure;
    fake->install_entered.store(1U, std::memory_order_release);
    while (fake->block_install.load(std::memory_order_acquire) != 0U &&
           fake->release_install.load(std::memory_order_acquire) == 0U) {
        std::this_thread::yield();
    }
    const auto result = fake->install_result.load(std::memory_order_acquire);
    *platform_error = result == 1U ? 0U : 202U;
    *hook_handle =
        result == 1U ||
                fake->failure_handle_nonzero.load(std::memory_order_acquire) !=
                    0U
            ? 0x71727374ULL
            : 0U;
    return result;
}

std::uint32_t RemoveHook(
    void* const context,
    const std::uint64_t hook_handle,
    std::uint32_t* const platform_error) noexcept {
    auto* const fake = static_cast<fake_platform_context*>(context);
    fake->remove_calls.fetch_add(1U, std::memory_order_acq_rel);
    fake->observed_remove_handle = hook_handle;
    const auto result = fake->remove_result.load(std::memory_order_acquire);
    *platform_error = result == 1U ? 0U : 303U;
    return result;
}

[[nodiscard]] jarvis_bridge_core_init_request BridgeRequest() noexcept {
    jarvis_bridge_core_init_request request{
        .size = sizeof(jarvis_bridge_core_init_request),
        .abi_version = JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION,
        .explorer_process_id = 4242U,
        .shell_thread_id = 9001U,
        .session_nonce = 0x4A415256495332ULL,
        .host_admission_passed = 1U,
        .kill_switch_armed = 1U,
        .one_shot_permit_valid = 1U,
        .transport_scope =
            JARVIS_EXPLORER_BRIDGE_TRANSPORT_SCOPE_EXACT_THREAD,
        .settings_sha256 = {},
        .reserved0 = 0U,
        .reserved1 = 0U,
    };
    for (std::uint32_t index = 0U; index < 32U; ++index) {
        request.settings_sha256[index] =
            static_cast<std::uint8_t>(index + 1U);
    }
    return request;
}

[[nodiscard]] jarvis_exact_thread_transport_request
TransportRequest() noexcept {
    return jarvis_exact_thread_transport_request{
        .size = sizeof(jarvis_exact_thread_transport_request),
        .abi_version = JARVIS_EXACT_THREAD_TRANSPORT_ABI_VERSION,
        .explorer_process_id = 4242U,
        .shell_thread_id = 9001U,
        .shell_window_handle = 0x11112222ULL,
        .module_handle = 0x33334444ULL,
        .hook_procedure = 0x55556666ULL,
        .session_nonce = 0x4A415256495332ULL,
        .host_admission_passed = 1U,
        .kill_switch_armed = 1U,
        .one_shot_permit_valid = 1U,
        .transport_scope = JARVIS_EXACT_THREAD_SCOPE,
        .architecture_match = 1U,
        .reserved0 = 0U,
        .reserved1 = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] jarvis_exact_thread_platform_api Platform(
    fake_platform_context* const context,
    const std::uint32_t execution_kind =
        JARVIS_TRANSPORT_EXECUTION_SYNTHETIC) noexcept {
    return jarvis_exact_thread_platform_api{
        .size = sizeof(jarvis_exact_thread_platform_api),
        .execution_kind = execution_kind,
        .context = context,
        .validate_exact_target = &ValidateTarget,
        .install_exact_thread_hook = &InstallHook,
        .remove_exact_thread_hook = &RemoveHook,
    };
}

void ReadyBridge(jarvis_bridge_core_instance* const bridge) noexcept {
    jarvis_bridge_core_reset_for_test(bridge);
    const auto request = BridgeRequest();
    jarvis_bridge_core_response response{};
    static_cast<void>(jarvis_bridge_core_prepare(
        bridge,
        &request,
        &response));
}

void ResetAll(
    jarvis_bridge_core_instance* const bridge,
    jarvis_exact_thread_transport_instance* const transport,
    fake_platform_context* const context) noexcept {
    ReadyBridge(bridge);
    jarvis_exact_thread_transport_reset_for_test(transport);
    context->validate_calls.store(0U);
    context->install_calls.store(0U);
    context->remove_calls.store(0U);
    context->validate_result.store(1U);
    context->install_result.store(1U);
    context->remove_result.store(1U);
    context->failure_handle_nonzero.store(0U);
    context->block_install.store(0U);
    context->install_entered.store(0U);
    context->release_install.store(0U);
    context->observed_process_id = 0U;
    context->observed_thread_id = 0U;
    context->observed_window_handle = 0U;
    context->observed_module_handle = 0U;
    context->observed_hook_procedure = 0U;
    context->observed_remove_handle = 0U;
}

void CheckRejectedPrepare(
    jarvis_exact_thread_transport_request request,
    const jarvis_exact_thread_transport_result expected_result) noexcept {
    jarvis_bridge_core_instance bridge{};
    jarvis_exact_thread_transport_instance transport{};
    fake_platform_context context{};
    ResetAll(&bridge, &transport, &context);
    const auto platform = Platform(&context);
    jarvis_exact_thread_transport_response response{};
    const auto result = jarvis_exact_thread_transport_prepare(
        &transport,
        &request,
        &platform,
        &bridge,
        &response);
    Check(result == expected_result && response.result == expected_result &&
          response.state == JARVIS_EXACT_THREAD_STATE_BLOCKED &&
          response.pass_through == 1U &&
          response.hook_entry_published == 0U &&
          response.activation_permitted == 0U &&
          response.mutation_performed == 0U &&
          response.live_explorer_touched == 0U);
}

}  // namespace

int main() {
    jarvis_bridge_core_instance bridge{};
    jarvis_exact_thread_transport_instance transport{};
    fake_platform_context context{};
    ResetAll(&bridge, &transport, &context);
    jarvis_exact_thread_transport_response response{};

    Check(jarvis_exact_thread_transport_poll(nullptr, &response) ==
          JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT);
    Check(jarvis_exact_thread_transport_poll(&transport, nullptr) ==
          JARVIS_EXACT_THREAD_RESULT_INVALID_ARGUMENT);
    auto result = jarvis_exact_thread_transport_poll(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT &&
          response.state == JARVIS_EXACT_THREAD_STATE_COLD &&
          response.pass_through == 1U);

    auto request = TransportRequest();
    request.size -= 1U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_SIZE_MISMATCH);
    request = TransportRequest();
    request.abi_version += 1U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ABI_MISMATCH);
    request = TransportRequest();
    request.explorer_process_id = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.shell_thread_id = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.shell_window_handle = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.module_handle = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.hook_procedure = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.session_nonce = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.reserved2 = 1U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH);
    request = TransportRequest();
    request.host_admission_passed = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED);
    request = TransportRequest();
    request.kill_switch_armed = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED);
    request = TransportRequest();
    request.one_shot_permit_valid = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED);
    request = TransportRequest();
    request.transport_scope = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED);
    request = TransportRequest();
    request.architecture_match = 0U;
    CheckRejectedPrepare(request, JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED);

    ResetAll(&bridge, &transport, &context);
    request = TransportRequest();
    auto platform = Platform(&context);
    platform.execution_kind = 2U;
    result = jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED &&
          response.state == JARVIS_EXACT_THREAD_STATE_BLOCKED);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    platform.install_exact_thread_hook = nullptr;
    result = jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_ADMISSION_DENIED &&
          response.pass_through == 1U);

    ResetAll(&bridge, &transport, &context);
    bridge.shell_thread_id += 1U;
    platform = Platform(&context);
    result = jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_IDENTITY_MISMATCH &&
          response.state == JARVIS_EXACT_THREAD_STATE_BLOCKED);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    result = jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_SUCCESS &&
          response.state == JARVIS_EXACT_THREAD_STATE_READY &&
          response.prepare_attempt_count == 1U &&
          response.pass_through == 1U &&
          response.unload_permitted == 0U);
    result = jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT &&
          response.state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
          response.prepare_attempt_count == 2U &&
          response.unload_permitted == 1U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    context.validate_result.store(0U, std::memory_order_release);
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_TARGET_VALIDATION_FAILED &&
          response.state == JARVIS_EXACT_THREAD_STATE_BLOCKED &&
          response.target_validation_count == 1U &&
          response.last_platform_error == 101U &&
          context.install_calls.load() == 0U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    context.install_result.store(0U, std::memory_order_release);
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED &&
          response.state == JARVIS_EXACT_THREAD_STATE_BLOCKED &&
          response.hook_entry_published == 0U &&
          response.last_platform_error == 202U &&
          context.remove_calls.load() == 0U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    context.install_result.store(0U, std::memory_order_release);
    context.failure_handle_nonzero.store(1U, std::memory_order_release);
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_HOOK_INSTALL_FAILED &&
          response.state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
          response.hook_entry_published == 1U &&
          response.hook_removed == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U &&
          response.last_platform_error == 202U &&
          context.remove_calls.load() == 1U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_SUCCESS &&
          response.state == JARVIS_EXACT_THREAD_STATE_ACTIVE &&
          response.hook_entry_published == 1U &&
          response.hook_removed == 0U && response.pass_through == 0U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U &&
          response.live_explorer_touched == 0U &&
          response.activation_permitted == 0U &&
          response.mutation_performed == 0U);
    Check(context.observed_process_id == request.explorer_process_id &&
          context.observed_thread_id == request.shell_thread_id &&
          context.observed_window_handle == request.shell_window_handle &&
          context.observed_module_handle == request.module_handle &&
          context.observed_hook_procedure == request.hook_procedure);

    jarvis_bridge_callback_token token{};
    jarvis_bridge_core_response bridge_response{};
    auto bridge_result = jarvis_bridge_core_try_enter_callback(
        &bridge,
        request.explorer_process_id,
        request.shell_thread_id,
        &token,
        &bridge_response);
    Check(bridge_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          token.acquired == 1U && bridge_response.active_callback_count == 1U);
    result = jarvis_exact_thread_transport_quiesce(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING &&
          response.state == JARVIS_EXACT_THREAD_STATE_DRAINING &&
          response.pass_through == 1U && response.hook_removed == 1U &&
          response.unhook_attempt_count == 1U &&
          context.observed_remove_handle == 0x71727374ULL);
    jarvis_bridge_callback_token late_token{};
    bridge_result = jarvis_bridge_core_try_enter_callback(
        &bridge,
        request.explorer_process_id,
        request.shell_thread_id,
        &late_token,
        &bridge_response);
    Check(bridge_result == JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED &&
          late_token.acquired == 0U);
    bridge_result = jarvis_bridge_core_leave_callback(
        &bridge, &token, &bridge_response);
    Check(bridge_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          bridge_response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED);
    result = jarvis_exact_thread_transport_poll(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_QUIESCED &&
          response.state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
          response.pass_through == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U);
    result = jarvis_exact_thread_transport_quiesce(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_QUIESCED &&
          context.remove_calls.load() == 1U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    static_cast<void>(jarvis_exact_thread_transport_install(
        &transport, &response));
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT &&
          response.state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
          response.pass_through == 1U && context.remove_calls.load() == 1U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    static_cast<void>(jarvis_exact_thread_transport_install(
        &transport, &response));
    context.remove_result.store(0U, std::memory_order_release);
    result = jarvis_exact_thread_transport_quiesce(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_UNHOOK_FAILED &&
          response.state == JARVIS_EXACT_THREAD_STATE_FAULTED &&
          response.pass_through == 1U &&
          response.module_pin_required == 1U &&
          response.last_platform_error == 303U);
    result = jarvis_exact_thread_transport_quiesce(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_FAULTED &&
          context.remove_calls.load() == 1U);

    ResetAll(&bridge, &transport, &context);
    platform = Platform(
        &context, JARVIS_TRANSPORT_EXECUTION_WINDOWS_LIVE);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    result = jarvis_exact_thread_transport_install(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_SUCCESS &&
          response.live_explorer_touched == 1U &&
          response.activation_permitted == 0U &&
          response.mutation_performed == 0U);
    static_cast<void>(jarvis_exact_thread_transport_quiesce(
        &transport, &response));

    ResetAll(&bridge, &transport, &context);
    platform = Platform(&context);
    static_cast<void>(jarvis_exact_thread_transport_prepare(
        &transport, &request, &platform, &bridge, &response));
    context.block_install.store(1U, std::memory_order_release);
    jarvis_exact_thread_transport_result install_result =
        JARVIS_EXACT_THREAD_RESULT_STATE_CONFLICT;
    jarvis_exact_thread_transport_response install_response{};
    std::thread installer([&]() {
        install_result = jarvis_exact_thread_transport_install(
            &transport, &install_response);
    });
    while (context.install_entered.load(std::memory_order_acquire) == 0U) {
        std::this_thread::yield();
    }
    result = jarvis_exact_thread_transport_quiesce(&transport, &response);
    Check(result == JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING &&
          response.state == JARVIS_EXACT_THREAD_STATE_DRAINING &&
          response.pass_through == 1U && context.remove_calls.load() == 0U);
    context.release_install.store(1U, std::memory_order_release);
    installer.join();
    result = jarvis_exact_thread_transport_poll(&transport, &response);
    Check((install_result == JARVIS_EXACT_THREAD_RESULT_QUIESCED ||
           install_result == JARVIS_EXACT_THREAD_RESULT_QUIESCE_PENDING) &&
          result == JARVIS_EXACT_THREAD_RESULT_QUIESCED &&
          response.state == JARVIS_EXACT_THREAD_STATE_QUIESCED &&
          response.pass_through == 1U &&
          response.hook_entry_published == 1U &&
          response.hook_removed == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U &&
          context.remove_calls.load() == 1U);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,\"result\":\""
        << (passed ? "passed" : "failed")
        << "\",\"scenarioCount\":" << scenario_count
        << ",\"passedCount\":" << passed_count
        << ",\"transportCoreBuilt\":true"
        << ",\"windowsAdapterExecuted\":false"
        << ",\"liveExplorer\":\"not-run\""
        << ",\"activationPermitted\":false"
        << ",\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
