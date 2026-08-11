#include "jarvis_explorer_callwndproc_bridge_internal.h"

#include <cstdint>

namespace {

void WriteReceipt(
    jarvis_bridge_core_instance* const bridge,
    const jarvis_callwndproc_result result,
    const std::int32_t n_code,
    const std::uint32_t observed_process_id,
    const std::uint32_t observed_thread_id,
    const std::uint32_t callback_entered,
    const std::uint32_t callback_left,
    const std::uint32_t chain_called,
    const std::uint32_t body_called,
    const std::uint32_t negative_code_bypassed,
    const std::uint32_t entry_rejected,
    const std::int64_t chain_result,
    jarvis_callwndproc_receipt* const receipt) noexcept {
    if (receipt == nullptr) {
        return;
    }
    jarvis_bridge_core_response bridge_response{};
    if (bridge != nullptr) {
        static_cast<void>(jarvis_bridge_core_query(
            bridge,
            &bridge_response));
    }
    *receipt = jarvis_callwndproc_receipt{
        .size = sizeof(jarvis_callwndproc_receipt),
        .abi_version = JARVIS_CALLWNDPROC_BRIDGE_ABI_VERSION,
        .result = result,
        .n_code = n_code,
        .observed_process_id = observed_process_id,
        .observed_thread_id = observed_thread_id,
        .callback_entered = callback_entered,
        .callback_left = callback_left,
        .chain_called = chain_called,
        .body_called = body_called,
        .negative_code_bypassed = negative_code_bypassed,
        .entry_rejected = entry_rejected,
        .bridge_state = bridge == nullptr
            ? JARVIS_BRIDGE_CORE_STATE_COLD
            : bridge_response.state,
        .pass_through = bridge == nullptr ? 1U : bridge_response.pass_through,
        .active_callback_count = bridge == nullptr
            ? 0U
            : bridge_response.active_callback_count,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = bridge == nullptr
            ? 0U
            : bridge_response.live_explorer_touched,
        .chain_result = chain_result,
    };
}

[[nodiscard]] std::int64_t ChainAndRecord(
    jarvis_bridge_core_instance* const bridge,
    const jarvis_callwndproc_result result,
    const std::int32_t n_code,
    const std::uint64_t w_param,
    const std::int64_t l_param,
    const std::uint32_t observed_process_id,
    const std::uint32_t observed_thread_id,
    const std::uint32_t callback_entered,
    const std::uint32_t callback_left,
    const std::uint32_t body_called,
    const std::uint32_t negative_code_bypassed,
    const std::uint32_t entry_rejected,
    const jarvis_callwndproc_chain_fn chain,
    void* const chain_context,
    jarvis_callwndproc_receipt* const receipt) noexcept {
    const auto chain_result = chain(
        chain_context,
        n_code,
        w_param,
        l_param);
    WriteReceipt(
        bridge,
        result,
        n_code,
        observed_process_id,
        observed_thread_id,
        callback_entered,
        callback_left,
        1U,
        body_called,
        negative_code_bypassed,
        entry_rejected,
        chain_result,
        receipt);
    return chain_result;
}

}  // namespace

std::int64_t jarvis_callwndproc_dispatch(
    jarvis_bridge_core_instance* const bridge,
    const std::uint32_t observed_process_id,
    const std::uint32_t observed_thread_id,
    const std::int32_t n_code,
    const std::uint64_t w_param,
    const std::int64_t l_param,
    const jarvis_callwndproc_body_fn body,
    void* const body_context,
    const jarvis_callwndproc_chain_fn chain,
    void* const chain_context,
    jarvis_callwndproc_receipt* const receipt) noexcept {
    if (chain == nullptr) {
        WriteReceipt(
            bridge,
            JARVIS_CALLWNDPROC_RESULT_CHAIN_UNAVAILABLE,
            n_code,
            observed_process_id,
            observed_thread_id,
            0U,
            0U,
            0U,
            0U,
            n_code < 0 ? 1U : 0U,
            0U,
            0,
            receipt);
        return 0;
    }

    // Microsoft requires a negative nCode to pass directly to the next Hook
    // without any further processing.
    if (n_code < 0) {
        return chain(
            chain_context,
            n_code,
            w_param,
            l_param);
    }

    if (bridge == nullptr) {
        return ChainAndRecord(
            bridge,
            JARVIS_CALLWNDPROC_RESULT_ENTER_REJECTED,
            n_code,
            w_param,
            l_param,
            observed_process_id,
            observed_thread_id,
            0U,
            0U,
            0U,
            0U,
            1U,
            chain,
            chain_context,
            receipt);
    }

    jarvis_bridge_callback_token token{};
    jarvis_bridge_core_response bridge_response{};
    const auto enter_result = jarvis_bridge_core_try_enter_callback(
        bridge,
        observed_process_id,
        observed_thread_id,
        &token,
        &bridge_response);
    if (enter_result != JARVIS_BRIDGE_CORE_RESULT_SUCCESS) {
        return ChainAndRecord(
            bridge,
            JARVIS_CALLWNDPROC_RESULT_ENTER_REJECTED,
            n_code,
            w_param,
            l_param,
            observed_process_id,
            observed_thread_id,
            0U,
            0U,
            0U,
            0U,
            1U,
            chain,
            chain_context,
            receipt);
    }

    std::uint32_t body_called = 0U;
    if (body != nullptr) {
        body(body_context, w_param, l_param);
        body_called = 1U;
    }
    const auto leave_result = jarvis_bridge_core_leave_callback(
        bridge,
        &token,
        &bridge_response);
    return ChainAndRecord(
        bridge,
        leave_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS
            ? JARVIS_CALLWNDPROC_RESULT_PROCESSED
            : JARVIS_CALLWNDPROC_RESULT_LEAVE_FAILED,
        n_code,
        w_param,
        l_param,
        observed_process_id,
        observed_thread_id,
        1U,
        leave_result == JARVIS_BRIDGE_CORE_RESULT_SUCCESS ? 1U : 0U,
        body_called,
        0U,
        0U,
        chain,
        chain_context,
        receipt);
}
