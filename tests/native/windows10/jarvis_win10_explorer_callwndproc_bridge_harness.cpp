#include "jarvis_explorer_callwndproc_bridge_internal.h"

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

struct chain_context final {
    jarvis_bridge_core_instance* bridge{nullptr};
    std::atomic<std::uint32_t> call_count{0U};
    std::atomic<std::uint32_t> zero_active_observed{0U};
    std::int32_t observed_n_code{0};
    std::uint64_t observed_w_param{0U};
    std::int64_t observed_l_param{0};
    std::int64_t result{0x112233445566778LL};
};

std::int64_t Chain(
    void* const raw_context,
    const std::int32_t n_code,
    const std::uint64_t w_param,
    const std::int64_t l_param) noexcept {
    auto* const context = static_cast<chain_context*>(raw_context);
    context->call_count.fetch_add(1U, std::memory_order_acq_rel);
    context->observed_n_code = n_code;
    context->observed_w_param = w_param;
    context->observed_l_param = l_param;
    if (context->bridge != nullptr) {
        jarvis_bridge_core_response response{};
        static_cast<void>(jarvis_bridge_core_query(
            context->bridge,
            &response));
        if (response.active_callback_count == 0U) {
            context->zero_active_observed.fetch_add(
                1U,
                std::memory_order_acq_rel);
        }
    }
    return context->result;
}

struct body_context final {
    std::atomic<std::uint32_t> call_count{0U};
    std::atomic<std::uint32_t> entered{0U};
    std::atomic<std::uint32_t> release{1U};
};

void ResetChain(
    chain_context* const context,
    jarvis_bridge_core_instance* const bridge) noexcept {
    context->bridge = bridge;
    context->call_count.store(0U);
    context->zero_active_observed.store(0U);
    context->observed_n_code = 0;
    context->observed_w_param = 0U;
    context->observed_l_param = 0;
    context->result = 0x112233445566778LL;
}

void ResetBody(body_context* const context) noexcept {
    context->call_count.store(0U);
    context->entered.store(0U);
    context->release.store(1U);
}

void Body(
    void* const raw_context,
    const std::uint64_t,
    const std::int64_t) noexcept {
    auto* const context = static_cast<body_context*>(raw_context);
    context->call_count.fetch_add(1U, std::memory_order_acq_rel);
    context->entered.store(1U, std::memory_order_release);
    while (context->release.load(std::memory_order_acquire) == 0U) {
        std::this_thread::yield();
    }
}

[[nodiscard]] jarvis_bridge_core_init_request Request() noexcept {
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

void Prepare(
    jarvis_bridge_core_instance* const bridge,
    const bool publish) noexcept {
    jarvis_bridge_core_reset_for_test(bridge);
    const auto request = Request();
    jarvis_bridge_core_response response{};
    static_cast<void>(jarvis_bridge_core_prepare(
        bridge,
        &request,
        &response));
    if (publish) {
        static_cast<void>(jarvis_bridge_core_publish_transport(
            bridge,
            request.explorer_process_id,
            request.shell_thread_id,
            request.session_nonce,
            0U,
            &response));
    }
}

}  // namespace

int main() {
    jarvis_callwndproc_receipt receipt{};
    receipt.size = 0xA5A5A5A5U;
    receipt.chain_result = 0x12345678;
    chain_context chain{};
    auto dispatch_result = jarvis_callwndproc_dispatch(
        nullptr,
        1U,
        2U,
        -1,
        3U,
        4,
        nullptr,
        nullptr,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          receipt.size == 0xA5A5A5A5U &&
          receipt.chain_result == 0x12345678);
    Check(receipt.abi_version == 0U && receipt.callback_entered == 0U &&
          receipt.body_called == 0U && receipt.chain_called == 0U);
    Check(chain.observed_n_code == -1 && chain.observed_w_param == 3U &&
          chain.observed_l_param == 4);

    receipt = jarvis_callwndproc_receipt{};
    dispatch_result = jarvis_callwndproc_dispatch(
        nullptr,
        1U,
        2U,
        0,
        3U,
        4,
        nullptr,
        nullptr,
        nullptr,
        nullptr,
        &receipt);
    Check(dispatch_result == 0 &&
          receipt.result == JARVIS_CALLWNDPROC_RESULT_CHAIN_UNAVAILABLE &&
          receipt.chain_called == 0U &&
          receipt.size == sizeof(jarvis_callwndproc_receipt) &&
          receipt.abi_version == JARVIS_CALLWNDPROC_BRIDGE_ABI_VERSION);

    jarvis_bridge_core_instance bridge{};
    Prepare(&bridge, false);
    ResetChain(&chain, &bridge);
    body_context body{};
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4242U,
        9001U,
        0,
        10U,
        20,
        &Body,
        &body,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          body.call_count.load() == 0U &&
          receipt.result == JARVIS_CALLWNDPROC_RESULT_ENTER_REJECTED &&
          receipt.entry_rejected == 1U && receipt.pass_through == 1U);

    Prepare(&bridge, true);
    ResetChain(&chain, &bridge);
    ResetBody(&body);
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4243U,
        9001U,
        0,
        10U,
        20,
        &Body,
        &body,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          body.call_count.load() == 0U && receipt.entry_rejected == 1U &&
          receipt.callback_entered == 0U &&
          receipt.active_callback_count == 0U);

    ResetChain(&chain, &bridge);
    ResetBody(&body);
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4242U,
        9002U,
        0,
        10U,
        20,
        &Body,
        &body,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          body.call_count.load() == 0U && receipt.entry_rejected == 1U &&
          receipt.callback_entered == 0U);

    ResetChain(&chain, &bridge);
    ResetBody(&body);
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4242U,
        9001U,
        0,
        0xAAU,
        0xBB,
        &Body,
        &body,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          body.call_count.load() == 1U &&
          chain.zero_active_observed.load() == 1U &&
          receipt.result == JARVIS_CALLWNDPROC_RESULT_PROCESSED &&
          receipt.callback_entered == 1U && receipt.callback_left == 1U &&
          receipt.body_called == 1U && receipt.chain_called == 1U &&
          receipt.active_callback_count == 0U &&
          receipt.activation_permitted == 0U &&
          receipt.mutation_performed == 0U);

    ResetChain(&chain, &bridge);
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4242U,
        9001U,
        0,
        1U,
        2,
        nullptr,
        nullptr,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && receipt.body_called == 0U &&
          receipt.callback_entered == 1U && receipt.callback_left == 1U &&
          chain.zero_active_observed.load() == 1U);

    Prepare(&bridge, true);
    ResetChain(&chain, &bridge);
    ResetBody(&body);
    body.release.store(0U, std::memory_order_release);
    jarvis_callwndproc_receipt race_receipt{};
    std::int64_t race_result = 0;
    std::thread callback([&]() {
        race_result = jarvis_callwndproc_dispatch(
            &bridge,
            4242U,
            9001U,
            0,
            30U,
            40,
            &Body,
            &body,
            &Chain,
            &chain,
            &race_receipt);
    });
    while (body.entered.load(std::memory_order_acquire) == 0U) {
        std::this_thread::yield();
    }
    jarvis_bridge_core_response bridge_response{};
    auto bridge_result = jarvis_bridge_core_begin_quiesce(
        &bridge,
        &bridge_response);
    Check(bridge_result == JARVIS_BRIDGE_CORE_RESULT_QUIESCE_PENDING &&
          bridge_response.state == JARVIS_BRIDGE_CORE_STATE_DRAINING &&
          bridge_response.pass_through == 1U &&
          bridge_response.active_callback_count == 1U);
    body.release.store(1U, std::memory_order_release);
    callback.join();
    static_cast<void>(jarvis_bridge_core_query(&bridge, &bridge_response));
    Check(race_result == chain.result &&
          bridge_response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          bridge_response.active_callback_count == 0U &&
          race_receipt.callback_entered == 1U &&
          race_receipt.callback_left == 1U &&
          race_receipt.chain_called == 1U &&
          chain.zero_active_observed.load() == 1U);
    ResetChain(&chain, &bridge);
    ResetBody(&body);
    dispatch_result = jarvis_callwndproc_dispatch(
        &bridge,
        4242U,
        9001U,
        0,
        50U,
        60,
        &Body,
        &body,
        &Chain,
        &chain,
        &receipt);
    Check(dispatch_result == chain.result && chain.call_count.load() == 1U &&
          body.call_count.load() == 0U &&
          receipt.result == JARVIS_CALLWNDPROC_RESULT_ENTER_REJECTED &&
          receipt.pass_through == 1U && receipt.callback_entered == 0U);

    Prepare(&bridge, true);
    std::atomic<bool> start{false};
    std::atomic<std::uint32_t> started_dispatches{0U};
    std::atomic<std::uint32_t> dispatch_failures{0U};
    std::atomic<std::uint32_t> total_chains{0U};
    std::vector<std::thread> workers;
    workers.reserve(8U);
    for (std::uint32_t worker = 0U; worker < 8U; ++worker) {
        workers.emplace_back([&]() {
            chain_context worker_chain{};
            worker_chain.bridge = &bridge;
            while (!start.load(std::memory_order_acquire)) {
                std::this_thread::yield();
            }
            for (std::uint32_t attempt = 0U; attempt < 500U; ++attempt) {
                started_dispatches.fetch_add(1U, std::memory_order_acq_rel);
                jarvis_callwndproc_receipt worker_receipt{};
                const auto worker_result = jarvis_callwndproc_dispatch(
                    &bridge,
                    4242U,
                    9001U,
                    0,
                    attempt,
                    static_cast<std::int64_t>(attempt),
                    nullptr,
                    nullptr,
                    &Chain,
                    &worker_chain,
                    &worker_receipt);
                if (worker_result != worker_chain.result ||
                    worker_receipt.chain_called != 1U) {
                    dispatch_failures.fetch_add(
                        1U,
                        std::memory_order_acq_rel);
                }
            }
            total_chains.fetch_add(
                worker_chain.call_count.load(std::memory_order_acquire),
                std::memory_order_acq_rel);
        });
    }
    start.store(true, std::memory_order_release);
    for (std::uint32_t spin = 0U;
         spin < 100000U &&
         started_dispatches.load(std::memory_order_acquire) == 0U;
         ++spin) {
        std::this_thread::yield();
    }
    static_cast<void>(jarvis_bridge_core_begin_quiesce(
        &bridge,
        &bridge_response));
    for (auto& worker : workers) {
        worker.join();
    }
    static_cast<void>(jarvis_bridge_core_begin_quiesce(
        &bridge,
        &bridge_response));
    Check(total_chains.load(std::memory_order_acquire) == 4000U &&
          dispatch_failures.load(std::memory_order_acquire) == 0U &&
          bridge_response.state == JARVIS_BRIDGE_CORE_STATE_QUIESCED &&
          bridge_response.active_callback_count == 0U &&
          bridge_response.pass_through == 1U);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,\"result\":\""
        << (passed ? "passed" : "failed")
        << "\",\"scenarioCount\":" << scenario_count
        << ",\"passedCount\":" << passed_count
        << ",\"callbackCoreBuilt\":true"
        << ",\"windowsCallbackDllExecuted\":false"
        << ",\"callbackBodyMutationIncluded\":false"
        << ",\"liveExplorer\":\"not-run\""
        << ",\"activationPermitted\":false"
        << ",\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
