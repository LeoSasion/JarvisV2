#pragma once

#include "jarvis_explorer_callwndproc_bridge.h"
#include "../Jarvis.Win10.ExplorerBridgeCore/jarvis_explorer_bridge_core_internal.h"

#include <cstdint>

using jarvis_callwndproc_chain_fn = std::int64_t (*)(
    void* context,
    std::int32_t n_code,
    std::uint64_t w_param,
    std::int64_t l_param) noexcept;

using jarvis_callwndproc_body_fn = void (*)(
    void* context,
    std::uint64_t w_param,
    std::int64_t l_param) noexcept;

[[nodiscard]] std::int64_t jarvis_callwndproc_dispatch(
    jarvis_bridge_core_instance* bridge,
    std::uint32_t observed_process_id,
    std::uint32_t observed_thread_id,
    std::int32_t n_code,
    std::uint64_t w_param,
    std::int64_t l_param,
    jarvis_callwndproc_body_fn body,
    void* body_context,
    jarvis_callwndproc_chain_fn chain,
    void* chain_context,
    jarvis_callwndproc_receipt* receipt) noexcept;
