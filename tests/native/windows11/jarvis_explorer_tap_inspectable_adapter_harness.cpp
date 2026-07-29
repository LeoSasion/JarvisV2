#include "jarvis_explorer_tap_inspectable_adapter.h"

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

[[nodiscard]] jarvis_transport_bind_request ValidBind() noexcept {
    jarvis_transport_bind_request bind{
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
    SetExactTitleHash(&bind.target.exact_window_title_sha256);
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        bind.expected_selector_sha256[index] =
            Hash(50U + index * 10U);
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        bind.expected_styled_value_sha256[index] =
            Hash(100U + index * 10U);
    }
    return bind;
}

[[nodiscard]] jarvis_tap_admission_request
ValidAdmissionRequest() noexcept {
    return jarvis_tap_admission_request{
        .size = sizeof(jarvis_tap_admission_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .bind = ValidBind(),
        .controller_sha256 = Hash(200U),
        .tap_dll_sha256 = Hash(210U),
        .xaml_diagnostics_sha256 = Hash(220U),
        .endpoint_name_sha256 = Hash(230U),
        .evaluated_at_monotonic_ms = 100000ULL,
        .observed_consumer_count = 0U,
        .endpoint_candidate_count = 1U,
        .tap_export_count = 2U,
        .import_policy_passed = 1U,
        .binary_identity_passed = 1U,
        .recovery_ready = 1U,
        .one_shot_plan_available = 1U,
        .reserved = 0U,
    };
}

[[nodiscard]] bool IsNonLive(
    const jarvis_tap_inspectable_adapter_response& response) noexcept {
    return response.size == sizeof(response) &&
           response.abi_version ==
               JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           response.adapter_model_supported == 1U &&
           response.property_read_supported == 0U &&
           response.execution_supported == 0U &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U &&
           response.reserved2 == 0U;
}

[[nodiscard]] bool Setup(
    jarvis_tap_admission_instance* const admission,
    jarvis_tap_inspectable_adapter_instance* const adapter,
    jarvis_transport_bind_request* const bind) noexcept {
    auto request = ValidAdmissionRequest();
    *bind = request.bind;
    jarvis_tap_admission_reset(admission);
    jarvis_tap_inspectable_adapter_reset(adapter);
    const auto admitted =
        jarvis_tap_admission_evaluate(admission, &request);
    const auto bound =
        jarvis_tap_inspectable_adapter_bind(
            adapter,
            admission,
            bind);
    return admitted.result ==
               JARVIS_TAP_ADMISSION_RESULT_ACCEPTED &&
           bound.result == JARVIS_TAP_ADAPTER_RESULT_ACCEPTED &&
           IsNonLive(bound);
}

[[nodiscard]] jarvis_tap_runtime_property_snapshot Snapshot(
    const jarvis_transport_bind_request& bind,
    const std::uint32_t index) noexcept {
    const auto surface =
        index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto property =
        index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const bool is_null = index % 2U == 0U;
    return jarvis_tap_runtime_property_snapshot{
        .size = sizeof(jarvis_tap_runtime_property_snapshot),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = static_cast<std::uint64_t>(index) + 1U,
        .target = bind.target,
        .surface_slot = surface,
        .property_slot = property,
        .instance_handle =
            0x1000ULL + static_cast<std::uint64_t>(surface),
        .selector_sha256 =
            bind.expected_selector_sha256[surface],
        .value_origin = JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL,
        .runtime_value_kind = is_null
            ? JARVIS_TAP_RUNTIME_VALUE_NULL
            : JARVIS_TAP_RUNTIME_VALUE_OBJECT,
        .runtime_class = is_null
            ? JARVIS_TAP_RUNTIME_CLASS_NONE
            : JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH,
        .exact_runtime_class_name_matched = is_null ? 0U : 1U,
        .argb = is_null ? 0U : 0xFF102030U + index,
        .opacity_millionths = is_null ? 0U : 900000U + index,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

template <typename Mutator>
void CheckRejected(
    Mutator mutator,
    const jarvis_tap_adapter_result expected_result) {
    jarvis_tap_admission_instance admission{};
    jarvis_tap_inspectable_adapter_instance adapter{};
    jarvis_transport_bind_request bind{};
    const bool setup = Setup(&admission, &adapter, &bind);
    auto snapshot = Snapshot(bind, 0U);
    mutator(snapshot);
    const auto response =
        jarvis_tap_inspectable_adapter_observe(
            &adapter,
            &snapshot);
    Check(
        setup &&
        IsNonLive(response) &&
        response.result == expected_result &&
        response.state == JARVIS_TAP_ADAPTER_STATE_BLOCKED &&
        response.forwarded_to_fingerprint == 0U &&
        adapter.fingerprint.state ==
            JARVIS_TAP_FINGERPRINT_STATE_BLOCKED);
}

}  // namespace

int main() {
    Check(sizeof(jarvis_tap_canonical_property_value) == 16U &&
          sizeof(jarvis_tap_runtime_property_snapshot) == 192U &&
          sizeof(jarvis_tap_inspectable_adapter_instance) == 680U &&
          sizeof(jarvis_tap_inspectable_adapter_response) == 168U);
    const auto contract =
        jarvis_tap_inspectable_adapter_query_contract();
    Check(IsNonLive(contract) &&
          contract.result == JARVIS_TAP_ADAPTER_RESULT_MODEL_ONLY);

    jarvis_tap_inspectable_adapter_instance adapter{};
    jarvis_tap_admission_instance admission{};
    auto request = ValidAdmissionRequest();
    jarvis_tap_inspectable_adapter_reset(&adapter);
    auto response = jarvis_tap_inspectable_adapter_bind(
        nullptr,
        &admission,
        &request.bind);
    Check(response.result ==
          JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_inspectable_adapter_bind(
        &adapter,
        nullptr,
        &request.bind);
    Check(response.result ==
          JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_inspectable_adapter_bind(
        &adapter,
        &admission,
        nullptr);
    Check(response.result ==
          JARVIS_TAP_ADAPTER_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_inspectable_adapter_bind(
        &adapter,
        &admission,
        &request.bind);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED &&
          response.state == JARVIS_TAP_ADAPTER_STATE_BLOCKED);

    jarvis_transport_bind_request bind{};
    Check(Setup(&admission, &adapter, &bind));
    response = jarvis_tap_inspectable_adapter_bind(
        &adapter,
        &admission,
        &bind);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID &&
          response.state == JARVIS_TAP_ADAPTER_STATE_BLOCKED);

    CheckRejected(
        [](auto& value) { value.size -= 1U; },
        JARVIS_TAP_ADAPTER_RESULT_SIZE_MISMATCH);
    CheckRejected(
        [](auto& value) { value.abi_version += 1U; },
        JARVIS_TAP_ADAPTER_RESULT_ABI_MISMATCH);
    CheckRejected(
        [](auto& value) { value.value_origin = 2U; },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_ORIGIN_UNSUPPORTED);
    CheckRejected(
        [](auto& value) { value.runtime_value_kind = 99U; },
        JARVIS_TAP_ADAPTER_RESULT_RUNTIME_KIND_UNSUPPORTED);
    CheckRejected(
        [](auto& value) {
            value.runtime_value_kind =
                JARVIS_TAP_RUNTIME_VALUE_OBJECT;
            value.runtime_class = 99U;
            value.exact_runtime_class_name_matched = 1U;
        },
        JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNSUPPORTED);
    CheckRejected(
        [](auto& value) {
            value.runtime_value_kind =
                JARVIS_TAP_RUNTIME_VALUE_OBJECT;
            value.runtime_class =
                JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH;
            value.exact_runtime_class_name_matched = 0U;
        },
        JARVIS_TAP_ADAPTER_RESULT_RUNTIME_CLASS_UNVERIFIED);
    CheckRejected(
        [](auto& value) {
            value.runtime_class =
                JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH;
        },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) {
            value.exact_runtime_class_name_matched = 1U;
        },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) { value.argb = 1U; },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) { value.opacity_millionths = 1U; },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) {
            value.runtime_value_kind =
                JARVIS_TAP_RUNTIME_VALUE_OBJECT;
            value.runtime_class =
                JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH;
            value.exact_runtime_class_name_matched = 1U;
            value.opacity_millionths =
                JARVIS_TAP_OPACITY_MILLIONTHS_MAX + 1U;
        },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) { value.reserved = 1U; },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) { value.reserved2 = 1U; },
        JARVIS_TAP_ADAPTER_RESULT_VALUE_NONCANONICAL);
    CheckRejected(
        [](auto& value) { value.sequence += 1U; },
        JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED);
    CheckRejected(
        [](auto& value) {
            value.target.visual_tree_generation_sha256 = Hash(999U);
        },
        JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED);
    CheckRejected(
        [](auto& value) { value.selector_sha256 = Hash(999U); },
        JARVIS_TAP_ADAPTER_RESULT_FINGERPRINT_REJECTED);

    Check(Setup(&admission, &adapter, &bind));
    bool sequence_passed = true;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto snapshot = Snapshot(bind, index);
        response = jarvis_tap_inspectable_adapter_observe(
            &adapter,
            &snapshot);
        const bool complete =
            index + 1U ==
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
        sequence_passed =
            sequence_passed &&
            IsNonLive(response) &&
            response.forwarded_to_fingerprint == 1U &&
            response.result ==
                (complete
                    ? JARVIS_TAP_ADAPTER_RESULT_COMPLETE
                    : JARVIS_TAP_ADAPTER_RESULT_ACCEPTED) &&
            response.canonical_value_count == index + 1U;
    }
    Check(sequence_passed &&
          adapter.state == JARVIS_TAP_ADAPTER_STATE_COMPLETE &&
          adapter.fingerprint.state ==
              JARVIS_TAP_FINGERPRINT_STATE_COMPLETE &&
          adapter.canonical_value_count ==
              JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT);
    Check(
        adapter.canonical_values[0U].value_kind ==
            JARVIS_TAP_PROPERTY_VALUE_NULL &&
        adapter.canonical_values[1U].value_kind ==
            JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR &&
        adapter.canonical_values[1U].argb == 0xFF102031U &&
        adapter.canonical_values[1U].opacity_millionths == 900001U);
    const auto query =
        jarvis_tap_inspectable_adapter_query(&adapter);
    Check(IsNonLive(query) &&
          query.result == JARVIS_TAP_ADAPTER_RESULT_COMPLETE &&
          query.state == JARVIS_TAP_ADAPTER_STATE_COMPLETE);
    auto extra = Snapshot(bind, 0U);
    response = jarvis_tap_inspectable_adapter_observe(
        &adapter,
        &extra);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_ADAPTER_RESULT_STATE_INVALID &&
          response.state == JARVIS_TAP_ADAPTER_STATE_BLOCKED);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-inspectable-adapter-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ','
        << "\"passedCount\":" << passed_count << ','
        << "\"iInspectableReadAttempted\":false,"
        << "\"propertyReadSupported\":false,"
        << "\"endpointAttempted\":false,"
        << "\"tapDllLoaded\":false,"
        << "\"executionSupported\":false,"
        << "\"activationPermitted\":false,"
        << "\"liveExplorer\":\"not-run\","
        << "\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
