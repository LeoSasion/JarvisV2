#include "jarvis_explorer_bridge_core_internal.h"

#include <atomic>
#include <cstdint>
#include <iostream>
#include <thread>
#include <vector>

namespace {

std::uint32_t scenario_count = 0U;
std::uint32_t passed_count = 0U;

void Check(const bool condition) noexcept {
    ++scenario_count;
    if (condition) {
        ++passed_count;
    }
}

[[nodiscard]] jarvis_bridge_core_init_request ValidRequest() noexcept {
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

[[nodiscard]] bool IsNonActivating(
    const jarvis_bridge_core_response& response) noexcept {
    return response.size == sizeof(jarvis_bridge_core_response) &&
           response.abi_version == JARVIS_EXPLORER_BRIDGE_CORE_ABI_VERSION &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U;
}

void CheckRejectedRequest(
    jarvis_bridge_core_init_request request,
    const jarvis_bridge_core_result expected_result) noexcept {
    jarvis_bridge_core_instance instance{};
    jarvis_bridge_core_reset_for_test(&instance);
    jarvis_bridge_core_response response{};
    const auto result = jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response);
    Check(result == expected_result &&
          response.result == expected_result &&
          response.state == JARVIS_BRIDGE_CORE_STATE_BLOCKED &&
          response.pass_through == 1U &&
          response.unload_permitted == 0U &&
          IsNonActivating(response));
}

}  // namespace

int main() {
    jarvis_bridge_core_response response{};
    auto result = JarvisBridge_QueryContract(&response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_CORE_ONLY_NO_TRANSPORT &&
          response.state == JARVIS_BRIDGE_CORE_STATE_COLD &&
          response.pass_through == 1U &&
          response.external_entry_published == 0U &&
          IsNonActivating(response));
    Check(JarvisBridge_QueryContract(nullptr) ==
          JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT);

    jarvis_bridge_core_instance instance{};
    jarvis_bridge_core_reset_for_test(&instance);
    result = jarvis_bridge_core_query(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_CORE_ONLY_NO_TRANSPORT &&
          response.state == JARVIS_BRIDGE_CORE_STATE_COLD &&
          response.pass_through == 1U &&
          response.active_callback_count == 0U);
    Check(jarvis_bridge_core_query(nullptr, &response) ==
          JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT);
    Check(jarvis_bridge_core_query(&instance, nullptr) ==
          JARVIS_BRIDGE_CORE_RESULT_INVALID_ARGUMENT);

    auto request = ValidRequest();
    request.size -= 1U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_SIZE_MISMATCH);
    request = ValidRequest();
    request.abi_version += 1U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_ABI_MISMATCH);
    request = ValidRequest();
    request.explorer_process_id = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID);
    request = ValidRequest();
    request.shell_thread_id = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID);
    request = ValidRequest();
    request.session_nonce = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID);
    request = ValidRequest();
    for (auto& value : request.settings_sha256) {
        value = 0U;
    }
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID);
    request = ValidRequest();
    request.reserved0 = 1U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_IDENTITY_INVALID);
    request = ValidRequest();
    request.host_admission_passed = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED);
    request = ValidRequest();
    request.kill_switch_armed = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED);
    request = ValidRequest();
    request.one_shot_permit_valid = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED);
    request = ValidRequest();
    request.transport_scope = 0U;
    CheckRejectedRequest(
        request,
        JARVIS_BRIDGE_CORE_RESULT_ADMISSION_DENIED);

    jarvis_bridge_core_reset_for_test(&instance);
    request = ValidRequest();
    result = jarvis_bridge_core_prepare(&instance, &request, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          response.state == JARVIS_BRIDGE_CORE_STATE_READY &&
          response.pass_through == 1U &&
          response.generation == 1U &&
          response.initialize_attempt_count == 1U &&
          response.unload_permitted == 0U &&
          IsNonActivating(response));
    Check(instance.explorer_process_id == request.explorer_process_id &&
          instance.shell_thread_id == request.shell_thread_id &&
          instance.session_nonce == request.session_nonce &&
          instance.settings_sha256[0] == request.settings_sha256[0] &&
          instance.settings_sha256[31] == request.settings_sha256[31]);
    result = jarvis_bridge_core_prepare(&instance, &request, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_ALREADY_INITIALIZED &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.initialize_attempt_count == 2U &&
          response.pass_through == 1U &&
          response.unload_permitted == 1U);

    jarvis_bridge_core_reset_for_test(&instance);
    request = ValidRequest();
    static_cast<void>(jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response));
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_QUIESCED &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.pass_through == 1U &&
          response.external_entry_published == 0U &&
          response.module_pin_required == 0U &&
          response.unload_permitted == 1U);
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_QUIESCED &&
          response.unload_permitted == 1U);

    jarvis_bridge_core_reset_for_test(&instance);
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_STATE_CONFLICT &&
          response.state == JARVIS_BRIDGE_CORE_STATE_COLD &&
          response.pass_through == 1U);

    jarvis_bridge_core_reset_for_test(&instance);
    request = ValidRequest();
    static_cast<void>(jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response));
    result = jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id + 1U,
        request.shell_thread_id,
        request.session_nonce,
        0U,
        &response);
    Check(result ==
              JARVIS_BRIDGE_CORE_RESULT_TRANSPORT_IDENTITY_MISMATCH &&
          response.state == JARVIS_BRIDGE_CORE_STATE_READY &&
          response.pass_through == 1U &&
          response.external_entry_published == 0U &&
          response.rejected_callback_count == 1U);
    result = jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        2U,
        &response);
    Check(result ==
              JARVIS_BRIDGE_CORE_RESULT_TRANSPORT_IDENTITY_MISMATCH &&
          response.state == JARVIS_BRIDGE_CORE_STATE_READY &&
          response.live_explorer_touched == 0U &&
          response.rejected_callback_count == 2U);
    result = jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        0U,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          response.state == JARVIS_BRIDGE_CORE_STATE_ACTIVE &&
          response.pass_through == 0U &&
          response.external_entry_published == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U &&
          IsNonActivating(response));
    result = jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        0U,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_STATE_CONFLICT &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.pass_through == 1U &&
          response.module_pin_required == 1U);

    // A future transport must report that it is executing in the admitted
    // Explorer context instead of inheriting this phase's offline receipt.
    jarvis_bridge_core_reset_for_test(&instance);
    static_cast<void>(jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response));
    result = jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        1U,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          response.live_explorer_touched == 1U &&
          response.activation_permitted == 0U &&
          response.mutation_performed == 0U);
    static_cast<void>(jarvis_bridge_core_begin_quiesce(
        &instance,
        &response));
    Check(response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.live_explorer_touched == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U);

    // Republish on a fresh instance for the callback and drain scenarios.
    jarvis_bridge_core_reset_for_test(&instance);
    static_cast<void>(jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response));
    static_cast<void>(jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        0U,
        &response));
    jarvis_bridge_callback_token token{};
    result = jarvis_bridge_core_try_enter_callback(
        &instance,
        request.explorer_process_id + 1U,
        request.shell_thread_id,
        &token,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED &&
          token.acquired == 0U &&
          response.active_callback_count == 0U &&
          response.rejected_callback_count == 1U);
    result = jarvis_bridge_core_try_enter_callback(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        &token,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          token.acquired == 1U &&
          token.generation == 1U &&
          response.active_callback_count == 1U &&
          response.accepted_callback_count == 1U);
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING &&
          response.state == JARVIS_BRIDGE_CORE_STATE_DRAINING &&
          response.pass_through == 1U &&
          response.active_callback_count == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U);
    jarvis_bridge_callback_token late_token{};
    result = jarvis_bridge_core_try_enter_callback(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        &late_token,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED &&
          late_token.acquired == 0U &&
          response.active_callback_count == 1U);
    result = jarvis_bridge_core_leave_callback(
        &instance,
        &token,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS &&
          token.acquired == 0U &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.active_callback_count == 0U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U);
    result = jarvis_bridge_core_leave_callback(
        &instance,
        &token,
        &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_CALLBACK_REJECTED &&
          response.active_callback_count == 0U &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED);

    // Concurrent callbacks may complete after quiesce begins, but no new
    // callback may remain admitted after pass-through publication.
    jarvis_bridge_core_reset_for_test(&instance);
    static_cast<void>(jarvis_bridge_core_prepare(
        &instance,
        &request,
        &response));
    static_cast<void>(jarvis_bridge_core_publish_transport(
        &instance,
        request.explorer_process_id,
        request.shell_thread_id,
        request.session_nonce,
        0U,
        &response));
    std::atomic<bool> start{false};
    std::atomic<std::uint32_t> entered{0U};
    std::atomic<std::uint32_t> left{0U};
    std::atomic<std::uint32_t> leave_failures{0U};
    std::vector<std::thread> workers;
    workers.reserve(8U);
    for (std::uint32_t worker = 0U; worker < 8U; ++worker) {
        workers.emplace_back([&]() {
            while (!start.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            for (std::uint32_t attempt = 0U; attempt < 500U; ++attempt) {
                jarvis_bridge_callback_token callback_token{};
                jarvis_bridge_core_response callback_response{};
                const auto enter_result =
                    jarvis_bridge_core_try_enter_callback(
                        &instance,
                        request.explorer_process_id,
                        request.shell_thread_id,
                        &callback_token,
                        &callback_response);
                if (enter_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS) {
                    entered.fetch_add(1U, std::memory_order_acq_rel);
                    std::this_thread::yield();
                    const auto leave_result =
                        jarvis_bridge_core_leave_callback(
                            &instance,
                            &callback_token,
                            &callback_response);
                    if (leave_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS) {
                        left.fetch_add(1U, std::memory_order_acq_rel);
                    } else {
                        leave_failures.fetch_add(1U, std::memory_order_acq_rel);
                    }
                }
            }
        });
    }
    start.store(true, std::memory_order_release);
    for (std::uint32_t spin = 0U;
         spin < 100000U && entered.load(std::memory_order_acquire) == 0U;
         ++spin) {
        std::this_thread::yield();
    }
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING ||
          result == JARVIS_BRIDGE_CORE_RESULT_QUIESCED);
    for (auto& worker : workers) {
        worker.join();
    }
    result = jarvis_bridge_core_begin_quiesce(&instance, &response);
    Check(entered.load(std::memory_order_acquire) > 0U &&
          entered.load(std::memory_order_acquire) ==
              left.load(std::memory_order_acquire) &&
          leave_failures.load(std::memory_order_acquire) == 0U);
    Check(result == JARVIS_BRIDGE_CORE_RESULT_QUIESCED &&
          response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          response.active_callback_count == 0U &&
          response.pass_through == 1U &&
          response.module_pin_required == 1U &&
          response.unload_permitted == 0U &&
          IsNonActivating(response));

    // Publication and quiesce may start together. Regardless of which state
    // transition wins, the retired instance must finish closed and drained.
    bool publication_quiesce_race_closed = true;
    for (std::uint32_t attempt = 0U; attempt < 4000U; ++attempt) {
        jarvis_bridge_core_reset_for_test(&instance);
        static_cast<void>(jarvis_bridge_core_prepare(
            &instance,
            &request,
            &response));
        std::atomic<bool> race_start{false};
        jarvis_bridge_core_response publish_response{};
        jarvis_bridge_core_response quiesce_response{};
        std::thread publisher([&]() {
            while (!race_start.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            static_cast<void>(jarvis_bridge_core_publish_transport(
                &instance,
                request.explorer_process_id,
                request.shell_thread_id,
                request.session_nonce,
                0U,
                &publish_response));
        });
        std::thread quiescer([&]() {
            while (!race_start.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            static_cast<void>(jarvis_bridge_core_begin_quiesce(
                &instance,
                &quiesce_response));
        });
        race_start.store(true, std::memory_order_release);
        publisher.join();
        quiescer.join();
        static_cast<void>(jarvis_bridge_core_begin_quiesce(
            &instance,
            &response));
        if (response.state != JARVIS_BRIDGE_CORE_STATE_QUIESCED ||
            response.pass_through != 1U ||
            response.active_callback_count != 0U) {
            publication_quiesce_race_closed = false;
            break;
        }
    }
    Check(publication_quiesce_race_closed);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,\"result\":\""
        << (passed ? "passed" : "failed")
        << "\",\"scenarioCount\":" << scenario_count
        << ",\"passedCount\":" << passed_count
        << ",\"bridgeCoreBuilt\":true"
        << ",\"transportIncluded\":false"
        << ",\"hookInstallerIncluded\":false"
        << ",\"activationPermitted\":false"
        << ",\"liveExplorer\":\"not-run\""
        << ",\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
