#pragma once

#include "../Jarvis.ExplorerTransportModel/jarvis_explorer_transport_contract.h"

#include <cstddef>
#include <cstdint>

#ifndef JARVIS_ENABLE_LIVE_XAML_READONLY
#define JARVIS_ENABLE_LIVE_XAML_READONLY 0
#endif

#if JARVIS_ENABLE_LIVE_XAML_READONLY != 0
#error Phase 12 must be compiled with live XAML Diagnostics disabled.
#endif

inline constexpr wchar_t JARVIS_TAP_INITIALIZATION_PREFIX[] =
    L"JARVIS2-XAML-RO-V1:";
inline constexpr std::size_t JARVIS_TAP_INITIALIZATION_PREFIX_CHARS =
    (sizeof(JARVIS_TAP_INITIALIZATION_PREFIX) / sizeof(wchar_t)) - 1U;
inline constexpr std::size_t JARVIS_TAP_INITIALIZATION_PAYLOAD_CHARS =
    sizeof(jarvis_transport_bind_request) * 2U;
inline constexpr std::size_t JARVIS_TAP_INITIALIZATION_CHARS =
    JARVIS_TAP_INITIALIZATION_PREFIX_CHARS +
    JARVIS_TAP_INITIALIZATION_PAYLOAD_CHARS;

using jarvis_tap_protocol_result = std::uint32_t;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED = 0U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT = 1U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_LENGTH_MISMATCH = 2U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_PREFIX_MISMATCH = 3U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_NONCANONICAL_HEX = 4U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID = 5U;
inline constexpr jarvis_tap_protocol_result
    JARVIS_TAP_PROTOCOL_RESULT_OUTPUT_TOO_SMALL = 6U;

using jarvis_tap_target_result = std::uint32_t;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_ACCEPTED = 0U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_INVALID_ARGUMENT = 1U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_PROCESS_MISMATCH = 2U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_DESKTOP_SHELL_MISMATCH = 3U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_WINDOW_INVALID = 4U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_WINDOW_IDENTITY_MISMATCH = 5U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_PROCESS_START_MISMATCH = 6U;
inline constexpr jarvis_tap_target_result
    JARVIS_TAP_TARGET_RESULT_CURRENT_THREAD_MISMATCH = 7U;

struct jarvis_tap_protocol_receipt final {
    std::uint32_t size;
    jarvis_tap_protocol_result result;
    std::uint32_t parsed;
    std::uint32_t canonical_length;
    std::uint32_t live_connection_compiled;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_tap_protocol_receipt) == 40U);
static_assert(JARVIS_TAP_INITIALIZATION_PREFIX_CHARS == 19U);
static_assert(JARVIS_TAP_INITIALIZATION_PAYLOAD_CHARS == 1232U);
static_assert(JARVIS_TAP_INITIALIZATION_CHARS == 1251U);

[[nodiscard]] jarvis_tap_protocol_receipt
jarvis_tap_encode_initialization_data(
    const jarvis_transport_bind_request* request,
    wchar_t* output,
    std::size_t output_capacity_chars) noexcept;

[[nodiscard]] jarvis_tap_protocol_receipt
jarvis_tap_parse_initialization_data(
    const wchar_t* input,
    std::size_t input_chars,
    jarvis_transport_bind_request* request) noexcept;

[[nodiscard]] jarvis_tap_target_result
jarvis_tap_verify_exact_target(
    const jarvis_transport_bind_request* request,
    std::uint32_t require_current_thread) noexcept;
