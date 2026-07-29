#include "jarvis_explorer_tap_readonly.h"

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace {

constexpr std::uint8_t kExactWindowTitleSha256[32] = {
    0x28U, 0xF7U, 0x09U, 0xD7U, 0x97U, 0x30U, 0x05U, 0x8EU,
    0x2AU, 0x46U, 0x15U, 0x18U, 0xE3U, 0x41U, 0x26U, 0xDAU,
    0x18U, 0xCEU, 0xDAU, 0x07U, 0x72U, 0x9EU, 0x79U, 0x2CU,
    0x92U, 0xF7U, 0xCAU, 0x12U, 0x51U, 0xE7U, 0x30U, 0xBFU,
};

[[nodiscard]] bool HashIsZero(
    const jarvis_transport_hash256& value) noexcept {
    return value.words[0] == 0U &&
           value.words[1] == 0U &&
           value.words[2] == 0U &&
           value.words[3] == 0U;
}

[[nodiscard]] bool ExactTitleHashMatches(
    const jarvis_transport_hash256& value) noexcept {
    return std::memcmp(
               &value,
               kExactWindowTitleSha256,
               sizeof(kExactWindowTitleSha256)) == 0;
}

[[nodiscard]] bool BindingIsStructurallyValid(
    const jarvis_transport_bind_request& request) noexcept {
    if (request.size != sizeof(jarvis_transport_bind_request) ||
        request.abi_version !=
            JARVIS_EXPLORER_TRANSPORT_ABI_VERSION ||
        request.target.explorer_process_id == 0U ||
        request.target.desktop_shell_process_id == 0U ||
        request.target.explorer_process_id ==
            request.target.desktop_shell_process_id ||
        request.target.window_thread_id == 0U ||
        request.target.reserved != 0U ||
        request.target.window_handle == 0U ||
        request.target.process_start_time_utc_ticks == 0U ||
        HashIsZero(request.target.visual_tree_generation_sha256) ||
        !ExactTitleHashMatches(
            request.target.exact_window_title_sha256) ||
        HashIsZero(request.session_nonce) ||
        HashIsZero(request.selector_profile_sha256) ||
        HashIsZero(request.preview_plan_sha256) ||
        request.issued_at_monotonic_ms >=
            request.expires_at_monotonic_ms ||
        request.expires_at_monotonic_ms -
                request.issued_at_monotonic_ms >
            JARVIS_TRANSPORT_MAX_CAPABILITY_AGE_MS ||
        request.preview_duration_ms !=
            JARVIS_TRANSPORT_PREVIEW_DURATION_MS ||
        request.required_surface_count !=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request.required_property_count !=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT ||
        request.reserved != 0U) {
        return false;
    }

    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        if (HashIsZero(request.expected_selector_sha256[index])) {
            return false;
        }
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        if (HashIsZero(
                request.expected_styled_value_sha256[index])) {
            return false;
        }
    }
    return true;
}

[[nodiscard]] jarvis_tap_protocol_receipt MakeReceipt(
    const jarvis_tap_protocol_result result,
    const bool parsed) noexcept {
    return jarvis_tap_protocol_receipt{
        .size = sizeof(jarvis_tap_protocol_receipt),
        .result = result,
        .parsed = parsed ? 1U : 0U,
        .canonical_length =
            static_cast<std::uint32_t>(
                JARVIS_TAP_INITIALIZATION_CHARS),
        .live_connection_compiled = 0U,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
    };
}

[[nodiscard]] int DecodeHex(const wchar_t character) noexcept {
    if (character >= L'0' && character <= L'9') {
        return static_cast<int>(character - L'0');
    }
    if (character >= L'A' && character <= L'F') {
        return static_cast<int>(character - L'A') + 10;
    }
    return -1;
}

[[nodiscard]] wchar_t EncodeHex(const std::uint8_t value) noexcept {
    constexpr wchar_t kHex[] = L"0123456789ABCDEF";
    return kHex[value & 0x0FU];
}

}  // namespace

jarvis_tap_protocol_receipt jarvis_tap_encode_initialization_data(
    const jarvis_transport_bind_request* const request,
    wchar_t* const output,
    const std::size_t output_capacity_chars) noexcept {
    if (request == nullptr || output == nullptr) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT,
            false);
    }
    if (output_capacity_chars <
        JARVIS_TAP_INITIALIZATION_CHARS + 1U) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_OUTPUT_TOO_SMALL,
            false);
    }
    if (!BindingIsStructurallyValid(*request)) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID,
            false);
    }

    std::memcpy(
        output,
        JARVIS_TAP_INITIALIZATION_PREFIX,
        JARVIS_TAP_INITIALIZATION_PREFIX_CHARS * sizeof(wchar_t));
    const auto* const bytes =
        reinterpret_cast<const std::uint8_t*>(request);
    std::size_t output_index =
        JARVIS_TAP_INITIALIZATION_PREFIX_CHARS;
    for (std::size_t index = 0U;
         index < sizeof(*request);
         ++index) {
        output[output_index++] =
            EncodeHex(static_cast<std::uint8_t>(bytes[index] >> 4U));
        output[output_index++] = EncodeHex(bytes[index]);
    }
    output[output_index] = L'\0';
    return MakeReceipt(
        JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED,
        true);
}

jarvis_tap_protocol_receipt jarvis_tap_parse_initialization_data(
    const wchar_t* const input,
    const std::size_t input_chars,
    jarvis_transport_bind_request* const request) noexcept {
    if (input == nullptr || request == nullptr) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_INVALID_ARGUMENT,
            false);
    }
    std::memset(request, 0, sizeof(*request));
    if (input_chars != JARVIS_TAP_INITIALIZATION_CHARS) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_LENGTH_MISMATCH,
            false);
    }
    if (std::memcmp(
            input,
            JARVIS_TAP_INITIALIZATION_PREFIX,
            JARVIS_TAP_INITIALIZATION_PREFIX_CHARS *
                sizeof(wchar_t)) != 0) {
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_PREFIX_MISMATCH,
            false);
    }

    auto* const bytes = reinterpret_cast<std::uint8_t*>(request);
    std::size_t input_index =
        JARVIS_TAP_INITIALIZATION_PREFIX_CHARS;
    for (std::size_t index = 0U;
         index < sizeof(*request);
         ++index) {
        const int high = DecodeHex(input[input_index++]);
        const int low = DecodeHex(input[input_index++]);
        if (high < 0 || low < 0) {
            std::memset(request, 0, sizeof(*request));
            return MakeReceipt(
                JARVIS_TAP_PROTOCOL_RESULT_NONCANONICAL_HEX,
                false);
        }
        bytes[index] = static_cast<std::uint8_t>(
            static_cast<unsigned int>(high * 16 + low));
    }

    if (!BindingIsStructurallyValid(*request)) {
        std::memset(request, 0, sizeof(*request));
        return MakeReceipt(
            JARVIS_TAP_PROTOCOL_RESULT_BINDING_INVALID,
            false);
    }
    return MakeReceipt(
        JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED,
        true);
}
