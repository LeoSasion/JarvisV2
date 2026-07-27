#include "jarvis_explorer_tap_readonly.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>

namespace {

std::uint32_t scenario_count = 0U;
std::uint32_t passed_count = 0U;

void Check(const bool condition) noexcept {
    ++scenario_count;
    if (condition) {
        ++passed_count;
    }
}

[[nodiscard]] jarvis_transport_hash256 Hash(
    const std::uint64_t seed) noexcept {
    return jarvis_transport_hash256{{
        seed,
        seed + 1U,
        seed + 2U,
        seed + 3U,
    }};
}

void SetExactTitleHash(
    jarvis_transport_hash256* const output) noexcept {
    constexpr std::uint8_t kHash[32] = {
        0x28U, 0xF7U, 0x09U, 0xD7U, 0x97U, 0x30U, 0x05U, 0x8EU,
        0x2AU, 0x46U, 0x15U, 0x18U, 0xE3U, 0x41U, 0x26U, 0xDAU,
        0x18U, 0xCEU, 0xDAU, 0x07U, 0x72U, 0x9EU, 0x79U, 0x2CU,
        0x92U, 0xF7U, 0xCAU, 0x12U, 0x51U, 0xE7U, 0x30U, 0xBFU,
    };
    std::memcpy(output, kHash, sizeof(kHash));
}

[[nodiscard]] jarvis_transport_bind_request ValidRequest() noexcept {
    jarvis_transport_bind_request request{
        .size = sizeof(jarvis_transport_bind_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .target = {
            .explorer_process_id = 4242U,
            .desktop_shell_process_id = 1000U,
            .window_thread_id = 9001U,
            .reserved = 0U,
            .window_handle = 0x1234ULL,
            .process_start_time_utc_ticks = 638000000000000000ULL,
            .visual_tree_generation_sha256 = Hash(10U),
            .exact_window_title_sha256 = {},
        },
        .session_nonce = Hash(20U),
        .selector_profile_sha256 = Hash(30U),
        .preview_plan_sha256 = Hash(40U),
        .expected_selector_sha256 = {},
        .expected_styled_value_sha256 = {},
        .issued_at_monotonic_ms = 90000ULL,
        .expires_at_monotonic_ms = 210000ULL,
        .preview_duration_ms = JARVIS_TRANSPORT_PREVIEW_DURATION_MS,
        .required_surface_count =
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT,
        .required_property_count =
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
        .reserved = 0U,
    };
    SetExactTitleHash(&request.target.exact_window_title_sha256);
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        request.expected_selector_sha256[index] =
            Hash(50U + index * 10U);
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        request.expected_styled_value_sha256[index] =
            Hash(100U + index * 10U);
    }
    return request;
}

[[nodiscard]] bool IsNonLive(
    const jarvis_tap_protocol_receipt& receipt) noexcept {
    return receipt.size == sizeof(jarvis_tap_protocol_receipt) &&
           receipt.canonical_length ==
               JARVIS_TAP_INITIALIZATION_CHARS &&
           receipt.live_connection_compiled == 0U &&
           receipt.execution_supported == 0U &&
           receipt.activation_permitted == 0U &&
           receipt.mutation_performed == 0U &&
           receipt.live_explorer_touched == 0U &&
           receipt.reserved == 0U;
}

template <typename Mutator>
void CheckRejectedBinding(Mutator mutator) {
    auto request = ValidRequest();
    mutator(request);
    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded{};
    const auto receipt = jarvis_tap_encode_initialization_data(
        &request,
        encoded.data(),
        encoded.size());
    Check(
        IsNonLive(receipt) &&
        receipt.result ==
            JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID &&
        receipt.parsed == 0U);
}

}  // namespace

int main() {
    Check(JARVIS_TAP_INITIALIZATION_PREFIX_CHARS == 19U &&
          JARVIS_TAP_INITIALIZATION_PAYLOAD_CHARS == 1232U &&
          JARVIS_TAP_INITIALIZATION_CHARS == 1251U);

    const auto request = ValidRequest();
    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded{};
    auto receipt = jarvis_tap_encode_initialization_data(
        &request,
        encoded.data(),
        encoded.size());
    Check(IsNonLive(receipt) &&
          receipt.result == JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED &&
          receipt.parsed == 1U &&
          encoded[JARVIS_TAP_INITIALIZATION_CHARS] == L'\0');

    jarvis_transport_bind_request parsed{};
    receipt = jarvis_tap_parse_initialization_data(
        encoded.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(IsNonLive(receipt) &&
          receipt.result == JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED &&
          receipt.parsed == 1U &&
          std::memcmp(&request, &parsed, sizeof(request)) == 0);

    Check(IsNonLive(receipt));

    receipt = jarvis_tap_encode_initialization_data(
        nullptr,
        encoded.data(),
        encoded.size());
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT);

    receipt = jarvis_tap_encode_initialization_data(
        &request,
        nullptr,
        encoded.size());
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT);

    receipt = jarvis_tap_encode_initialization_data(
        &request,
        encoded.data(),
        JARVIS_TAP_INITIALIZATION_CHARS);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_OUTPUT_TOO_SMALL);

    auto invalid_title_request = ValidRequest();
    invalid_title_request.target.exact_window_title_sha256 = Hash(999U);
    receipt = jarvis_tap_encode_initialization_data(
        &invalid_title_request,
        encoded.data(),
        encoded.size());
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID);

    receipt = jarvis_tap_parse_initialization_data(
        nullptr,
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT);

    receipt = jarvis_tap_parse_initialization_data(
        encoded.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        nullptr);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT);

    static_cast<void>(jarvis_tap_encode_initialization_data(
        &request,
        encoded.data(),
        encoded.size()));
    receipt = jarvis_tap_parse_initialization_data(
        encoded.data(),
        JARVIS_TAP_INITIALIZATION_CHARS - 1U,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_LENGTH_MISMATCH);

    receipt = jarvis_tap_parse_initialization_data(
        encoded.data(),
        JARVIS_TAP_INITIALIZATION_CHARS + 1U,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_LENGTH_MISMATCH);

    auto modified = encoded;
    modified[0] = L'X';
    receipt = jarvis_tap_parse_initialization_data(
        modified.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_PREFIX_MISMATCH);

    modified = encoded;
    modified[JARVIS_TAP_INITIALIZATION_PREFIX_CHARS] = L'a';
    receipt = jarvis_tap_parse_initialization_data(
        modified.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_NONCANONICAL_HEX);

    modified = encoded;
    modified[JARVIS_TAP_INITIALIZATION_PREFIX_CHARS] = L'Z';
    receipt = jarvis_tap_parse_initialization_data(
        modified.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(receipt.result ==
          JARVIS_TAP_PROTOCOL_RESULT_NONCANONICAL_HEX);

    modified = encoded;
    for (std::size_t index = 0U; index < 8U; ++index) {
        modified[JARVIS_TAP_INITIALIZATION_PREFIX_CHARS + index] = L'0';
    }
    receipt = jarvis_tap_parse_initialization_data(
        modified.data(),
        JARVIS_TAP_INITIALIZATION_CHARS,
        &parsed);
    Check(receipt.result ==
              JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID &&
          parsed.size == 0U);

    CheckRejectedBinding([](auto& value) { value.size -= 1U; });
    CheckRejectedBinding([](auto& value) { value.abi_version += 1U; });
    CheckRejectedBinding(
        [](auto& value) { value.target.explorer_process_id = 0U; });
    CheckRejectedBinding(
        [](auto& value) { value.target.desktop_shell_process_id = 0U; });
    CheckRejectedBinding([](auto& value) {
        value.target.desktop_shell_process_id =
            value.target.explorer_process_id;
    });
    CheckRejectedBinding(
        [](auto& value) { value.target.window_thread_id = 0U; });
    CheckRejectedBinding(
        [](auto& value) { value.target.reserved = 1U; });
    CheckRejectedBinding(
        [](auto& value) { value.target.window_handle = 0U; });
    CheckRejectedBinding([](auto& value) {
        value.target.process_start_time_utc_ticks = 0U;
    });
    CheckRejectedBinding([](auto& value) {
        value.target.visual_tree_generation_sha256 = {};
    });
    CheckRejectedBinding(
        [](auto& value) { value.session_nonce = {}; });
    CheckRejectedBinding(
        [](auto& value) { value.selector_profile_sha256 = {}; });
    CheckRejectedBinding(
        [](auto& value) { value.preview_plan_sha256 = {}; });
    CheckRejectedBinding([](auto& value) {
        value.expected_selector_sha256[1] = {};
    });
    CheckRejectedBinding([](auto& value) {
        value.expected_styled_value_sha256[7] = {};
    });
    CheckRejectedBinding([](auto& value) {
        value.issued_at_monotonic_ms =
            value.expires_at_monotonic_ms;
    });
    CheckRejectedBinding([](auto& value) {
        value.issued_at_monotonic_ms = 1U;
    });
    CheckRejectedBinding([](auto& value) {
        value.preview_duration_ms -= 1U;
    });
    CheckRejectedBinding([](auto& value) {
        value.required_surface_count += 1U;
    });
    CheckRejectedBinding([](auto& value) {
        value.required_property_count += 1U;
    });
    CheckRejectedBinding(
        [](auto& value) { value.reserved = 1U; });

    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded_again{};
    const auto second_receipt = jarvis_tap_encode_initialization_data(
        &request,
        encoded_again.data(),
        encoded_again.size());
    Check(second_receipt.result ==
              JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED &&
          std::memcmp(
              encoded.data(),
              encoded_again.data(),
              encoded.size() * sizeof(wchar_t)) == 0);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-readonly-tap-protocol-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ','
        << "\"passedCount\":" << passed_count << ','
        << "\"tapDllLoaded\":false,"
        << "\"liveConnectionCompiled\":false,"
        << "\"executionSupported\":false,"
        << "\"activationPermitted\":false,"
        << "\"liveExplorer\":\"not-run\","
        << "\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
