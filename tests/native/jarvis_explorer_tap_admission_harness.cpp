#include "jarvis_explorer_tap_fingerprint.h"

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
    const jarvis_tap_admission_response& response) noexcept {
    return response.size == sizeof(response) &&
           response.abi_version ==
               JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           response.execution_supported == 0U &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U;
}

[[nodiscard]] bool IsNonLive(
    const jarvis_tap_fingerprint_response& response) noexcept {
    return response.size == sizeof(response) &&
           response.abi_version ==
               JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           response.fingerprint_model_supported == 1U &&
           response.property_read_supported == 0U &&
           response.execution_supported == 0U &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U &&
           response.reserved2 == 0U;
}

template <typename Mutator>
void CheckRejectedAdmission(
    Mutator mutator,
    const jarvis_tap_admission_result expected_result) {
    auto request = ValidAdmissionRequest();
    mutator(request);
    jarvis_tap_admission_instance instance{};
    jarvis_tap_admission_reset(&instance);
    const auto response =
        jarvis_tap_admission_evaluate(&instance, &request);
    Check(
        IsNonLive(response) &&
        response.result == expected_result &&
        response.state == JARVIS_TAP_ADMISSION_STATE_BLOCKED &&
        response.attempt_count == 1U &&
        response.plan_consumed == 0U);
}

[[nodiscard]] bool SetupFingerprint(
    jarvis_tap_admission_instance* const admission,
    jarvis_tap_fingerprint_instance* const fingerprint,
    jarvis_transport_bind_request* const bind) noexcept {
    auto request = ValidAdmissionRequest();
    *bind = request.bind;
    jarvis_tap_admission_reset(admission);
    jarvis_tap_fingerprint_reset(fingerprint);
    const auto admission_response =
        jarvis_tap_admission_evaluate(admission, &request);
    const auto bind_response =
        jarvis_tap_fingerprint_bind(
            fingerprint,
            admission,
            bind);
    return admission_response.result ==
               JARVIS_TAP_ADMISSION_RESULT_ACCEPTED &&
           bind_response.result ==
               JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED &&
           IsNonLive(bind_response);
}

[[nodiscard]] jarvis_tap_fingerprint_request Observation(
    const jarvis_transport_bind_request& bind,
    const std::uint32_t index) noexcept {
    const auto surface =
        index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto property =
        index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    return jarvis_tap_fingerprint_request{
        .size = sizeof(jarvis_tap_fingerprint_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = static_cast<std::uint64_t>(index) + 1U,
        .target = bind.target,
        .surface_slot = surface,
        .property_slot = property,
        .instance_handle =
            0x1000ULL + static_cast<std::uint64_t>(surface),
        .selector_sha256 =
            bind.expected_selector_sha256[surface],
        .value_kind = index % 2U == 0U
            ? JARVIS_TAP_PROPERTY_VALUE_NULL
            : JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR,
        .argb = index % 2U == 0U
            ? 0U
            : 0xFF102030U + index,
        .opacity_millionths = index % 2U == 0U
            ? 0U
            : 900000U + index,
        .reserved = 0U,
    };
}

template <typename Mutator>
void CheckRejectedFirstObservation(
    Mutator mutator,
    const jarvis_tap_fingerprint_result expected_result) {
    jarvis_tap_admission_instance admission{};
    jarvis_tap_fingerprint_instance fingerprint{};
    jarvis_transport_bind_request bind{};
    const bool setup =
        SetupFingerprint(&admission, &fingerprint, &bind);
    auto request = Observation(bind, 0U);
    mutator(request);
    const auto response =
        jarvis_tap_fingerprint_observe(&fingerprint, &request);
    Check(
        setup &&
        IsNonLive(response) &&
        response.result == expected_result &&
        response.state == JARVIS_TAP_FINGERPRINT_STATE_BLOCKED &&
        response.observed_property_count == 0U);
}

void PrintHash(const jarvis_transport_hash256& hash) {
    constexpr char kHex[] = "0123456789ABCDEF";
    const auto* const bytes =
        reinterpret_cast<const std::uint8_t*>(&hash);
    for (std::size_t index = 0U; index < sizeof(hash); ++index) {
        std::cout << kHex[bytes[index] >> 4U]
                  << kHex[bytes[index] & 0x0FU];
    }
}

}  // namespace

int main() {
    Check(sizeof(jarvis_tap_admission_request) == 792U &&
          sizeof(jarvis_tap_admission_instance) == 768U &&
          sizeof(jarvis_tap_admission_response) == 60U &&
          sizeof(jarvis_tap_fingerprint_request) == 176U &&
          sizeof(jarvis_tap_fingerprint_instance) == 528U &&
          sizeof(jarvis_tap_fingerprint_response) == 96U);

    const auto admission_contract =
        jarvis_tap_admission_query_contract();
    Check(IsNonLive(admission_contract) &&
          admission_contract.result ==
              JARVIS_TAP_ADMISSION_RESULT_MODEL_ONLY);
    const auto fingerprint_contract =
        jarvis_tap_fingerprint_query_contract();
    Check(IsNonLive(fingerprint_contract) &&
          fingerprint_contract.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_MODEL_ONLY);

    auto admission_request = ValidAdmissionRequest();
    jarvis_tap_admission_instance admission{};
    jarvis_tap_admission_reset(&admission);
    auto admission_response =
        jarvis_tap_admission_evaluate(nullptr, &admission_request);
    Check(admission_response.result ==
          JARVIS_TAP_ADMISSION_RESULT_INVALID_ARGUMENT);
    admission_response =
        jarvis_tap_admission_evaluate(&admission, nullptr);
    Check(admission_response.result ==
          JARVIS_TAP_ADMISSION_RESULT_INVALID_ARGUMENT);

    CheckRejectedAdmission(
        [](auto& value) { value.size -= 1U; },
        JARVIS_TAP_ADMISSION_RESULT_SIZE_MISMATCH);
    CheckRejectedAdmission(
        [](auto& value) { value.abi_version += 1U; },
        JARVIS_TAP_ADMISSION_RESULT_ABI_MISMATCH);
    CheckRejectedAdmission(
        [](auto& value) {
            value.bind.target.explorer_process_id = 0U;
        },
        JARVIS_TAP_ADMISSION_RESULT_BIND_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.controller_sha256 = {}; },
        JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.tap_dll_sha256 = {}; },
        JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.xaml_diagnostics_sha256 = {}; },
        JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.endpoint_name_sha256 = {}; },
        JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    CheckRejectedAdmission(
        [](auto& value) {
            value.evaluated_at_monotonic_ms =
                value.bind.issued_at_monotonic_ms - 1U;
        },
        JARVIS_TAP_ADMISSION_RESULT_CAPABILITY_NOT_CURRENT);
    CheckRejectedAdmission(
        [](auto& value) {
            value.evaluated_at_monotonic_ms =
                value.bind.expires_at_monotonic_ms + 1U;
        },
        JARVIS_TAP_ADMISSION_RESULT_CAPABILITY_NOT_CURRENT);
    CheckRejectedAdmission(
        [](auto& value) { value.observed_consumer_count = 1U; },
        JARVIS_TAP_ADMISSION_RESULT_EXISTING_CONSUMER);
    CheckRejectedAdmission(
        [](auto& value) { value.endpoint_candidate_count = 0U; },
        JARVIS_TAP_ADMISSION_RESULT_ENDPOINT_COUNT_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.endpoint_candidate_count = 2U; },
        JARVIS_TAP_ADMISSION_RESULT_ENDPOINT_COUNT_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.tap_export_count = 1U; },
        JARVIS_TAP_ADMISSION_RESULT_TAP_EXPORT_SET_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.import_policy_passed = 0U; },
        JARVIS_TAP_ADMISSION_RESULT_IMPORT_POLICY_FAILED);
    CheckRejectedAdmission(
        [](auto& value) { value.binary_identity_passed = 0U; },
        JARVIS_TAP_ADMISSION_RESULT_BINARY_IDENTITY_INVALID);
    CheckRejectedAdmission(
        [](auto& value) { value.recovery_ready = 0U; },
        JARVIS_TAP_ADMISSION_RESULT_RECOVERY_NOT_READY);
    CheckRejectedAdmission(
        [](auto& value) {
            value.one_shot_plan_available = 0U;
        },
        JARVIS_TAP_ADMISSION_RESULT_PLAN_UNAVAILABLE);
    CheckRejectedAdmission(
        [](auto& value) { value.reserved = 1U; },
        JARVIS_TAP_ADMISSION_RESULT_PLAN_UNAVAILABLE);

    jarvis_tap_admission_reset(&admission);
    admission_request = ValidAdmissionRequest();
    admission_response =
        jarvis_tap_admission_evaluate(
            &admission,
            &admission_request);
    Check(IsNonLive(admission_response) &&
          admission_response.result ==
              JARVIS_TAP_ADMISSION_RESULT_ACCEPTED &&
          admission_response.state ==
              JARVIS_TAP_ADMISSION_STATE_ADMITTED &&
          admission_response.attempt_count == 1U &&
          admission_response.plan_consumed == 1U &&
          admission_response.observed_consumer_count == 0U &&
          admission_response.endpoint_candidate_count == 1U);
    admission_response =
        jarvis_tap_admission_evaluate(
            &admission,
            &admission_request);
    Check(IsNonLive(admission_response) &&
          admission_response.result ==
              JARVIS_TAP_ADMISSION_RESULT_REPLAY &&
          admission_response.state ==
              JARVIS_TAP_ADMISSION_STATE_BLOCKED &&
          admission_response.attempt_count == 2U &&
          admission_response.plan_consumed == 1U);

    jarvis_tap_fingerprint_instance fingerprint{};
    jarvis_transport_bind_request bind{};
    jarvis_tap_fingerprint_reset(&fingerprint);
    auto fingerprint_response =
        jarvis_tap_fingerprint_bind(
            nullptr,
            &admission,
            &admission_request.bind);
    Check(fingerprint_response.result ==
          JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT);
    fingerprint_response =
        jarvis_tap_fingerprint_bind(
            &fingerprint,
            nullptr,
            &admission_request.bind);
    Check(fingerprint_response.result ==
          JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT);
    fingerprint_response =
        jarvis_tap_fingerprint_bind(
            &fingerprint,
            &admission,
            nullptr);
    Check(fingerprint_response.result ==
          JARVIS_TAP_FINGERPRINT_RESULT_INVALID_ARGUMENT);

    admission_request = ValidAdmissionRequest();
    jarvis_tap_admission_reset(&admission);
    jarvis_tap_fingerprint_reset(&fingerprint);
    admission_response =
        jarvis_tap_admission_evaluate(
            &admission,
            &admission_request);
    auto drifted_bind = admission_request.bind;
    drifted_bind.preview_plan_sha256 = Hash(999U);
    fingerprint_response =
        jarvis_tap_fingerprint_bind(
            &fingerprint,
            &admission,
            &drifted_bind);
    Check(admission_response.result ==
              JARVIS_TAP_ADMISSION_RESULT_ACCEPTED &&
          IsNonLive(fingerprint_response) &&
          fingerprint_response.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_ADMISSION_INVALID &&
          fingerprint_response.state ==
              JARVIS_TAP_FINGERPRINT_STATE_BLOCKED);

    jarvis_tap_admission_reset(&admission);
    jarvis_tap_fingerprint_reset(&fingerprint);
    fingerprint_response =
        jarvis_tap_fingerprint_bind(
            &fingerprint,
            &admission,
            &admission_request.bind);
    Check(IsNonLive(fingerprint_response) &&
          fingerprint_response.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_ADMISSION_INVALID &&
          fingerprint_response.state ==
              JARVIS_TAP_FINGERPRINT_STATE_BLOCKED);

    CheckRejectedFirstObservation(
        [](auto& value) { value.size -= 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_SIZE_MISMATCH);
    CheckRejectedFirstObservation(
        [](auto& value) { value.abi_version += 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_ABI_MISMATCH);
    CheckRejectedFirstObservation(
        [](auto& value) { value.sequence += 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_SEQUENCE_INVALID);
    CheckRejectedFirstObservation(
        [](auto& value) {
            value.target.visual_tree_generation_sha256 = Hash(999U);
        },
        JARVIS_TAP_FINGERPRINT_RESULT_IDENTITY_DRIFT);
    CheckRejectedFirstObservation(
        [](auto& value) { value.surface_slot = 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_SLOT_INVALID);
    CheckRejectedFirstObservation(
        [](auto& value) { value.property_slot = 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_SLOT_INVALID);
    CheckRejectedFirstObservation(
        [](auto& value) { value.selector_sha256 = Hash(999U); },
        JARVIS_TAP_FINGERPRINT_RESULT_SELECTOR_MISMATCH);
    CheckRejectedFirstObservation(
        [](auto& value) { value.instance_handle = 0U; },
        JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID);
    CheckRejectedFirstObservation(
        [](auto& value) { value.value_kind = 99U; },
        JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED);
    CheckRejectedFirstObservation(
        [](auto& value) {
            value.value_kind = JARVIS_TAP_PROPERTY_VALUE_NULL;
            value.argb = 1U;
        },
        JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL);
    CheckRejectedFirstObservation(
        [](auto& value) {
            value.value_kind =
                JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR;
            value.opacity_millionths =
                JARVIS_TAP_OPACITY_MILLIONTHS_MAX + 1U;
        },
        JARVIS_TAP_FINGERPRINT_RESULT_VALUE_NONCANONICAL);
    CheckRejectedFirstObservation(
        [](auto& value) { value.reserved = 1U; },
        JARVIS_TAP_FINGERPRINT_RESULT_VALUE_UNSUPPORTED);

    Check(SetupFingerprint(&admission, &fingerprint, &bind));
    auto first = Observation(bind, 0U);
    fingerprint_response =
        jarvis_tap_fingerprint_observe(&fingerprint, &first);
    Check(IsNonLive(fingerprint_response) &&
          fingerprint_response.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED &&
          fingerprint_response.observed_property_count == 1U);
    auto second = Observation(bind, 1U);
    second.instance_handle += 1U;
    fingerprint_response =
        jarvis_tap_fingerprint_observe(&fingerprint, &second);
    Check(IsNonLive(fingerprint_response) &&
          fingerprint_response.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_INSTANCE_INVALID &&
          fingerprint_response.state ==
              JARVIS_TAP_FINGERPRINT_STATE_BLOCKED);

    Check(SetupFingerprint(&admission, &fingerprint, &bind));
    jarvis_transport_hash256 first_fingerprint{};
    bool sequence_passed = true;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto observation = Observation(bind, index);
        fingerprint_response =
            jarvis_tap_fingerprint_observe(
                &fingerprint,
                &observation);
        if (index == 0U) {
            first_fingerprint =
                fingerprint_response.last_fingerprint_sha256;
        }
        const bool final =
            index + 1U ==
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
        sequence_passed =
            sequence_passed &&
            IsNonLive(fingerprint_response) &&
            fingerprint_response.result ==
                (final
                    ? JARVIS_TAP_FINGERPRINT_RESULT_COMPLETE
                    : JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED) &&
            fingerprint_response.observed_property_count ==
                index + 1U &&
            fingerprint_response.complete == (final ? 1U : 0U);
    }
    Check(sequence_passed &&
          fingerprint.state ==
              JARVIS_TAP_FINGERPRINT_STATE_COMPLETE &&
          fingerprint.observed_mask == 0x1FFU);
    fingerprint_response =
        jarvis_tap_fingerprint_observe(
            &fingerprint,
            &first);
    Check(IsNonLive(fingerprint_response) &&
          fingerprint_response.result ==
              JARVIS_TAP_FINGERPRINT_RESULT_STATE_INVALID &&
          fingerprint_response.state ==
              JARVIS_TAP_FINGERPRINT_STATE_BLOCKED);

    jarvis_tap_admission_instance admission_again{};
    jarvis_tap_fingerprint_instance fingerprint_again{};
    jarvis_transport_bind_request bind_again{};
    Check(SetupFingerprint(
        &admission_again,
        &fingerprint_again,
        &bind_again));
    const auto first_again = Observation(bind_again, 0U);
    const auto deterministic =
        jarvis_tap_fingerprint_observe(
            &fingerprint_again,
            &first_again);
    Check(std::memcmp(
              &first_fingerprint,
              &deterministic.last_fingerprint_sha256,
              sizeof(first_fingerprint)) == 0);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-readonly-admission-fingerprint-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ','
        << "\"passedCount\":" << passed_count << ','
        << "\"firstFingerprintSha256\":\"";
    PrintHash(first_fingerprint);
    std::cout
        << "\",\"endpointAttempted\":false,"
        << "\"tapDllLoaded\":false,"
        << "\"propertyReadSupported\":false,"
        << "\"liveConnectionCompiled\":false,"
        << "\"executionSupported\":false,"
        << "\"activationPermitted\":false,"
        << "\"liveExplorer\":\"not-run\","
        << "\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
