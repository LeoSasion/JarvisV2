#include "jarvis_explorer_tap_fingerprint.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>

namespace {

constexpr std::array<std::uint32_t, 64> kSha256RoundConstants = {
    0x428A2F98U, 0x71374491U, 0xB5C0FBCFU, 0xE9B5DBA5U,
    0x3956C25BU, 0x59F111F1U, 0x923F82A4U, 0xAB1C5ED5U,
    0xD807AA98U, 0x12835B01U, 0x243185BEU, 0x550C7DC3U,
    0x72BE5D74U, 0x80DEB1FEU, 0x9BDC06A7U, 0xC19BF174U,
    0xE49B69C1U, 0xEFBE4786U, 0x0FC19DC6U, 0x240CA1CCU,
    0x2DE92C6FU, 0x4A7484AAU, 0x5CB0A9DCU, 0x76F988DAU,
    0x983E5152U, 0xA831C66DU, 0xB00327C8U, 0xBF597FC7U,
    0xC6E00BF3U, 0xD5A79147U, 0x06CA6351U, 0x14292967U,
    0x27B70A85U, 0x2E1B2138U, 0x4D2C6DFCU, 0x53380D13U,
    0x650A7354U, 0x766A0ABBU, 0x81C2C92EU, 0x92722C85U,
    0xA2BFE8A1U, 0xA81A664BU, 0xC24B8B70U, 0xC76C51A3U,
    0xD192E819U, 0xD6990624U, 0xF40E3585U, 0x106AA070U,
    0x19A4C116U, 0x1E376C08U, 0x2748774CU, 0x34B0BCB5U,
    0x391C0CB3U, 0x4ED8AA4AU, 0x5B9CCA4FU, 0x682E6FF3U,
    0x748F82EEU, 0x78A5636FU, 0x84C87814U, 0x8CC70208U,
    0x90BEFFFAU, 0xA4506CEBU, 0xBEF9A3F7U, 0xC67178F2U,
};

[[nodiscard]] constexpr std::uint32_t RotateRight(
    const std::uint32_t value,
    const std::uint32_t count) noexcept {
    return (value >> count) | (value << (32U - count));
}

void WriteUInt32(
    std::uint8_t* const output,
    const std::uint32_t value) noexcept {
    output[0] = static_cast<std::uint8_t>(value >> 24U);
    output[1] = static_cast<std::uint8_t>(value >> 16U);
    output[2] = static_cast<std::uint8_t>(value >> 8U);
    output[3] = static_cast<std::uint8_t>(value);
}

void WriteUInt64(
    std::uint8_t* const output,
    const std::uint64_t value) noexcept {
    WriteUInt32(output, static_cast<std::uint32_t>(value >> 32U));
    WriteUInt32(output + 4U, static_cast<std::uint32_t>(value));
}

void Sha256Block(
    const std::uint8_t* const block,
    std::uint32_t* const state) noexcept {
    std::array<std::uint32_t, 64> words{};
    for (std::size_t index = 0U; index < 16U; ++index) {
        const auto offset = index * 4U;
        words[index] =
            (static_cast<std::uint32_t>(block[offset]) << 24U) |
            (static_cast<std::uint32_t>(block[offset + 1U]) << 16U) |
            (static_cast<std::uint32_t>(block[offset + 2U]) << 8U) |
            static_cast<std::uint32_t>(block[offset + 3U]);
    }
    for (std::size_t index = 16U; index < words.size(); ++index) {
        const auto first = words[index - 15U];
        const auto second = words[index - 2U];
        const auto sigma0 =
            RotateRight(first, 7U) ^
            RotateRight(first, 18U) ^
            (first >> 3U);
        const auto sigma1 =
            RotateRight(second, 17U) ^
            RotateRight(second, 19U) ^
            (second >> 10U);
        words[index] =
            words[index - 16U] +
            sigma0 +
            words[index - 7U] +
            sigma1;
    }

    auto a = state[0];
    auto b = state[1];
    auto c = state[2];
    auto d = state[3];
    auto e = state[4];
    auto f = state[5];
    auto g = state[6];
    auto h = state[7];
    for (std::size_t index = 0U; index < words.size(); ++index) {
        const auto sigma1 =
            RotateRight(e, 6U) ^
            RotateRight(e, 11U) ^
            RotateRight(e, 25U);
        const auto choose = (e & f) ^ ((~e) & g);
        const auto temporary1 =
            h +
            sigma1 +
            choose +
            kSha256RoundConstants[index] +
            words[index];
        const auto sigma0 =
            RotateRight(a, 2U) ^
            RotateRight(a, 13U) ^
            RotateRight(a, 22U);
        const auto majority = (a & b) ^ (a & c) ^ (b & c);
        const auto temporary2 = sigma0 + majority;
        h = g;
        g = f;
        f = e;
        e = d + temporary1;
        d = c;
        c = b;
        b = a;
        a = temporary1 + temporary2;
    }
    state[0] += a;
    state[1] += b;
    state[2] += c;
    state[3] += d;
    state[4] += e;
    state[5] += f;
    state[6] += g;
    state[7] += h;
}

[[nodiscard]] jarvis_transport_hash256 Sha256(
    const std::uint8_t* const input,
    const std::size_t input_size) noexcept {
    std::array<std::uint32_t, 8> state = {
        0x6A09E667U,
        0xBB67AE85U,
        0x3C6EF372U,
        0xA54FF53AU,
        0x510E527FU,
        0x9B05688CU,
        0x1F83D9ABU,
        0x5BE0CD19U,
    };
    std::array<std::uint8_t, 128> padded{};
    std::memcpy(padded.data(), input, input_size);
    padded[input_size] = 0x80U;
    const std::size_t padded_size =
        input_size + 1U + 8U <= 64U ? 64U : 128U;
    WriteUInt64(
        padded.data() + padded_size - 8U,
        static_cast<std::uint64_t>(input_size) * 8U);
    Sha256Block(padded.data(), state.data());
    if (padded_size == 128U) {
        Sha256Block(padded.data() + 64U, state.data());
    }

    jarvis_transport_hash256 output{};
    auto* const bytes = reinterpret_cast<std::uint8_t*>(&output);
    for (std::size_t index = 0U; index < state.size(); ++index) {
        WriteUInt32(bytes + index * 4U, state[index]);
    }
    return output;
}

[[nodiscard]] bool HashMatches(
    const jarvis_transport_hash256& left,
    const jarvis_transport_hash256& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] bool TargetMatches(
    const jarvis_transport_target_identity& left,
    const jarvis_transport_target_identity& right) noexcept {
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

[[nodiscard]] jarvis_transport_hash256 Fingerprint(
    const jarvis_tap_fingerprint_request& request) noexcept {
    constexpr std::uint8_t kDomain[] = {
        'J', 'A', 'R', 'V', 'I', 'S', '2', '-',
        'X', 'A', 'M', 'L', '-', 'P', 'R', 'O', 'P', '-', 'V', '1',
    };
    std::array<std::uint8_t, 116> canonical{};
    std::size_t offset = 0U;
    std::memcpy(canonical.data(), kDomain, sizeof(kDomain));
    offset += sizeof(kDomain);
    WriteUInt32(
        canonical.data() + offset,
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION);
    offset += 4U;
    WriteUInt32(canonical.data() + offset, request.surface_slot);
    offset += 4U;
    WriteUInt32(canonical.data() + offset, request.property_slot);
    offset += 4U;
    WriteUInt64(canonical.data() + offset, request.instance_handle);
    offset += 8U;
    std::memcpy(
        canonical.data() + offset,
        &request.selector_sha256,
        sizeof(request.selector_sha256));
    offset += sizeof(request.selector_sha256);
    std::memcpy(
        canonical.data() + offset,
        &request.target.visual_tree_generation_sha256,
        sizeof(request.target.visual_tree_generation_sha256));
    offset += sizeof(request.target.visual_tree_generation_sha256);
    WriteUInt32(canonical.data() + offset, request.value_kind);
    offset += 4U;
    WriteUInt32(canonical.data() + offset, request.argb);
    offset += 4U;
    WriteUInt32(
        canonical.data() + offset,
        request.opacity_millionths);
    offset += 4U;
    if (offset != canonical.size()) {
        return {};
    }
    return Sha256(canonical.data(), canonical.size());
}

[[nodiscard]] jarvis_tap_fingerprint_response MakeResponse(
    const jarvis_tap_fingerprint_instance* const instance,
    const jarvis_tap_fingerprint_result result,
    const jarvis_transport_hash256& last_fingerprint) noexcept {
    return jarvis_tap_fingerprint_response{
        .size = sizeof(jarvis_tap_fingerprint_response),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .state = instance == nullptr
            ? JARVIS_TAP_FINGERPRINT_STATE_COLD
            : instance->state,
        .result = result,
        .next_sequence = instance == nullptr
            ? 0U
            : instance->next_sequence,
        .observed_property_count = instance == nullptr
            ? 0U
            : instance->observed_property_count,
        .complete = instance != nullptr &&
                instance->state ==
                    JARVIS_TAP_FINGERPRINT_STATE_COMPLETE
            ? 1U
            : 0U,
        .last_fingerprint_sha256 = last_fingerprint,
        .fingerprint_model_supported = 1U,
        .property_read_supported = 0U,
        .execution_supported = 0U,
        .activation_permitted = 0U,
        .mutation_performed = 0U,
        .live_explorer_touched = 0U,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] jarvis_tap_fingerprint_response Block(
    jarvis_tap_fingerprint_instance* const instance,
    const jarvis_tap_fingerprint_result result) noexcept {
    if (instance != nullptr) {
        instance->state = JARVIS_TAP_FINGERPRINT_STATE_BLOCKED;
    }
    return MakeResponse(instance, result, {});
}

}  // namespace

void jarvis_tap_fingerprint_reset(
    jarvis_tap_fingerprint_instance* const instance) noexcept {
    if (instance != nullptr) {
        std::memset(instance, 0, sizeof(*instance));
        instance->state = JARVIS_TAP_FINGERPRINT_STATE_COLD;
    }
}

jarvis_tap_fingerprint_response
jarvis_tap_fingerprint_query_contract() noexcept {
    return MakeResponse(
        nullptr,
        JARVIS_TAP_FINGERPRINT_RESULT_MODEL_ONLY,
        {});
}

jarvis_tap_fingerprint_result
jarvis_tap_fingerprint_compute_canonical(
    const jarvis_tap_fingerprint_request* const request,
    jarvis_transport_hash256* const output) noexcept {
    if (request == nullptr || output == nullptr) {
        return JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT;
    }
    *output = {};
    if (request->size != sizeof(jarvis_tap_fingerprint_request)) {
        return JARVIS_TAP_FINGERPRINT_RESULT_SIZE_MISMATCH;
    }
    if (request->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return JARVIS_TAP_FINGERPRINT_RESULT_ABI_MISMATCH;
    }
    if (request->surface_slot >=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->property_slot >=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT) {
        return JARVIS_TAP_FINGERPRINT_RESULT_SLOT_INVALID;
    }
    if (request->instance_handle == 0U) {
        return JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID;
    }
    if (request->reserved != 0U ||
        (request->value_kind != JARVIS_TAP_PROPERTY_VALUE_NULL &&
         request->value_kind !=
             JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR)) {
        return JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED;
    }
    if ((request->value_kind == JARVIS_TAP_PROPERTY_VALUE_NULL &&
         (request->argb != 0U ||
          request->opacity_millionths != 0U)) ||
        (request->value_kind ==
             JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR &&
         request->opacity_millionths >
             JARVIS_TAP_OPACITY_MILLIONTHS_MAX)) {
        return JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL;
    }
    *output = Fingerprint(*request);
    return JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED;
}

jarvis_tap_fingerprint_response jarvis_tap_fingerprint_bind(
    jarvis_tap_fingerprint_instance* const instance,
    const jarvis_tap_admission_instance* const admission,
    const jarvis_transport_bind_request* const bind) noexcept {
    if (instance == nullptr || admission == nullptr || bind == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT,
            {});
    }
    if (instance->state != JARVIS_TAP_FINGERPRINT_STATE_COLD) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_STATE_INVALID);
    }
    if (admission->state != JARVIS_TAP_ADMISSION_STATE_ADMITTED ||
        admission->plan_consumed != 1U ||
        std::memcmp(
            &admission->bind,
            bind,
            sizeof(*bind)) != 0) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_ADMISSION_INVALID);
    }

    std::array<
        wchar_t,
        JARVIS_TAP_INITIALIZATION_CHARS + 1U> encoded{};
    const auto protocol_receipt =
        jarvis_tap_encode_initialization_data(
            bind,
            encoded.data(),
            encoded.size());
    if (protocol_receipt.result !=
        JARVIS_TAP_PROTOCOL_RESULT_ACCEPTED) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_BIND_INVALID);
    }

    instance->state = JARVIS_TAP_FINGERPRINT_STATE_BOUND;
    instance->next_sequence = 1U;
    instance->target = bind->target;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        instance->expected_selector_sha256[index] =
            bind->expected_selector_sha256[index];
    }
    return MakeResponse(
        instance,
        JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED,
        {});
}

jarvis_tap_fingerprint_response jarvis_tap_fingerprint_observe(
    jarvis_tap_fingerprint_instance* const instance,
    const jarvis_tap_fingerprint_request* const request) noexcept {
    if (instance == nullptr || request == nullptr) {
        return MakeResponse(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT,
            {});
    }
    if (instance->state != JARVIS_TAP_FINGERPRINT_STATE_BOUND &&
        instance->state != JARVIS_TAP_FINGERPRINT_STATE_COLLECTING) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_STATE_INVALID);
    }
    if (request->size != sizeof(jarvis_tap_fingerprint_request)) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_SIZE_MISMATCH);
    }
    if (request->abi_version !=
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_ABI_MISMATCH);
    }
    if (request->sequence != instance->next_sequence) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_SEQUENCE_INVALID);
    }
    if (!TargetMatches(request->target, instance->target)) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_IDENTITY_DRIFT);
    }

    const auto expected_index = instance->observed_property_count;
    const auto expected_surface =
        expected_index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto expected_property =
        expected_index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    if (request->surface_slot != expected_surface ||
        request->property_slot != expected_property ||
        request->surface_slot >=
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT ||
        request->property_slot >=
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_SLOT_INVALID);
    }
    if (!HashMatches(
            request->selector_sha256,
            instance->expected_selector_sha256[
                request->surface_slot])) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_SELECTOR_MISMATCH);
    }
    if (request->instance_handle == 0U) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID);
    }

    auto& expected_handle =
        instance->surface_instance_handles[request->surface_slot];
    if (request->property_slot == 0U) {
        for (std::uint32_t index = 0U;
             index < request->surface_slot;
             ++index) {
            if (instance->surface_instance_handles[index] ==
                request->instance_handle) {
                return Block(
                    instance,
                    JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID);
            }
        }
        expected_handle = request->instance_handle;
    }
    else if (expected_handle != request->instance_handle) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID);
    }

    if (request->reserved != 0U ||
        (request->value_kind != JARVIS_TAP_PROPERTY_VALUE_NULL &&
         request->value_kind !=
             JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR)) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED);
    }
    if (
        (request->value_kind == JARVIS_TAP_PROPERTY_VALUE_NULL &&
         (request->argb != 0U ||
          request->opacity_millionths != 0U)) ||
        (request->value_kind ==
             JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR &&
         request->opacity_millionths >
             JARVIS_TAP_OPACITY_MILLIONTHS_MAX)
    ) {
        return Block(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL);
    }

    const auto fingerprint = Fingerprint(*request);
    instance->observed_fingerprint_sha256[expected_index] =
        fingerprint;
    instance->observed_mask |=
        static_cast<std::uint32_t>(1U << expected_index);
    ++instance->observed_property_count;
    ++instance->next_sequence;
    if (instance->observed_property_count ==
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT) {
        instance->state = JARVIS_TAP_FINGERPRINT_STATE_COMPLETE;
        return MakeResponse(
            instance,
            JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE,
            fingerprint);
    }
    instance->state = JARVIS_TAP_FINGERPRINT_STATE_COLLECTING;
    return MakeResponse(
        instance,
        JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED,
        fingerprint);
}

jarvis_tap_fingerprint_response jarvis_tap_fingerprint_query(
    const jarvis_tap_fingerprint_instance* const instance) noexcept {
    if (instance == nullptr) {
        return MakeResponse(
            nullptr,
            JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT,
            {});
    }
    const auto last = instance->observed_property_count == 0U
        ? jarvis_transport_hash256{}
        : instance->observed_fingerprint_sha256[
              instance->observed_property_count - 1U];
    return MakeResponse(
        instance,
        instance->state == JARVIS_TAP_FINGERPRINT_STATE_COMPLETE
            ? JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE
            : JARVIS_TAP_FINGERPRINT_RESULT_MODEL_ONLY,
        last);
}
