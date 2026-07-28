#include "jarvis_explorer_tap_xaml_read_bridge.h"

#include <cstdint>
#include <cstring>
#include <iostream>

namespace {

std::uint32_t scenario_count = 0U;
std::uint32_t passed_count = 0U;

void Check(const bool value) {
    ++scenario_count;
    if (value) {
        ++passed_count;
    }
}

[[nodiscard]] jarvis_transport_hash256 Hash(
    const std::uint64_t seed) noexcept {
    return jarvis_transport_hash256{
        .words = {
            seed,
            seed + 1ULL,
            seed + 2ULL,
            seed + 3ULL,
        },
    };
}

[[nodiscard]] jarvis_tap_admission_instance Admission() noexcept {
    jarvis_tap_admission_instance value{};
    value.state = JARVIS_TAP_ADMISSION_STATE_ADMITTED;
    value.attempt_count = 1U;
    value.plan_consumed = 1U;
    value.bind.size = sizeof(jarvis_transport_bind_request);
    value.bind.abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
    value.bind.target.explorer_process_id = 200U;
    value.bind.target.desktop_shell_process_id = 100U;
    value.bind.target.window_thread_id = 300U;
    value.bind.target.window_handle = 0x1234ULL;
    value.bind.target.process_start_time_utc_ticks = 0x5678ULL;
    value.bind.target.visual_tree_generation_sha256 = Hash(10ULL);
    value.bind.target.exact_window_title_sha256 = Hash(20ULL);
    value.bind.session_nonce = Hash(30ULL);
    value.bind.selector_profile_sha256 = Hash(40ULL);
    value.bind.preview_plan_sha256 = Hash(50ULL);
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++index) {
        value.bind.expected_selector_sha256[index] =
            Hash(100ULL + static_cast<std::uint64_t>(index));
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        value.bind.expected_styled_value_sha256[index] =
            Hash(200ULL + static_cast<std::uint64_t>(index));
    }
    value.bind.issued_at_monotonic_ms = 1000ULL;
    value.bind.expires_at_monotonic_ms = 121000ULL;
    value.bind.preview_duration_ms =
        JARVIS_TRANSPORT_PREVIEW_DURATION_MS;
    value.bind.required_surface_count =
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
    value.bind.required_property_count =
        JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    value.evaluated_at_monotonic_ms = 2000ULL;
    return value;
}

[[nodiscard]] jarvis_tap_xaml_read_request Request(
    const jarvis_tap_admission_instance& admission) noexcept {
    return jarvis_tap_xaml_read_request{
        .size = sizeof(jarvis_tap_xaml_read_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = 1ULL,
        .surface_slot = 0U,
        .property_slot = 0U,
        .instance_handle = 0xABCULL,
        .selector_sha256 =
            admission.bind.expected_selector_sha256[0U],
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] jarvis_tap_xaml_foreign_observation
ObjectObservation() noexcept {
    jarvis_tap_xaml_foreign_observation value{};
    value.size = sizeof(value);
    value.abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
    value.site_query_succeeded = 1U;
    value.service_query_succeeded = 1U;
    value.property_chain_call_attempted = 1U;
    value.property_chain_call_succeeded = 1U;
    value.property_source_count = 1U;
    value.property_value_count = 3U;
    value.matched_property_count = 1U;
    value.property_chain_index = 0U;
    value.property_value_source = 4U;
    value.property_metadata_bits =
        JARVIS_TAP_XAML_METADATA_IS_VALUE_HANDLE;
    value.property_handle_call_succeeded = 1U;
    value.property_value_handle_nonzero = 1U;
    value.inspectable_call_succeeded = 1U;
    value.runtime_value_kind = JARVIS_TAP_RUNTIME_VALUE_OBJECT;
    value.runtime_class =
        JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH;
    value.exact_runtime_class_name_matched = 1U;
    value.brush_read_succeeded = 1U;
    value.argb = 0xFF18202AU;
    value.opacity_millionths = 875000U;
    value.release_attempt_count = 5U;
    value.release_completed_count = 5U;
    value.property_chain_free_required = 1U;
    value.property_chain_freed = 1U;
    return value;
}

[[nodiscard]] jarvis_tap_xaml_foreign_observation
NullObservation() noexcept {
    auto value = ObjectObservation();
    value.property_metadata_bits =
        JARVIS_TAP_XAML_METADATA_IS_VALUE_NULL;
    value.property_handle_call_succeeded = 0U;
    value.property_value_handle_nonzero = 0U;
    value.inspectable_call_succeeded = 0U;
    value.runtime_value_kind = JARVIS_TAP_RUNTIME_VALUE_NULL;
    value.runtime_class = JARVIS_TAP_RUNTIME_CLASS_NONE;
    value.exact_runtime_class_name_matched = 0U;
    value.brush_read_succeeded = 0U;
    value.argb = 0U;
    value.opacity_millionths = 0U;
    value.release_attempt_count = 2U;
    value.release_completed_count = 2U;
    return value;
}

[[nodiscard]] jarvis_tap_xaml_read_response TargetAcceptance(
    const jarvis_tap_admission_instance& admission,
    const jarvis_tap_xaml_read_request& request) noexcept {
    const auto preflight =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            3000ULL);
    return jarvis_tap_xaml_read_bridge_accept_target(
        &preflight,
        JARVIS_TAP_TARGET_RESULT_ACCEPTED);
}

[[nodiscard]] jarvis_tap_xaml_read_response Complete(
    const jarvis_tap_admission_instance& admission,
    const jarvis_tap_xaml_read_request& request,
    const jarvis_tap_xaml_foreign_observation& observation) noexcept {
    const auto target = TargetAcceptance(admission, request);
    return jarvis_tap_xaml_read_bridge_complete(
        &admission,
        &request,
        &target,
        &observation,
        0U);
}

template <typename Mutation>
void CheckPreflightRejected(
    Mutation mutation,
    const jarvis_tap_xaml_read_result expected) {
    auto admission = Admission();
    auto request = Request(admission);
    mutation(admission, request);
    const auto response =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            3000ULL);
    Check(
        response.state == JARVIS_TAP_XAML_READ_STATE_BLOCKED &&
        response.result == expected &&
        response.diagnostics_site_touched == 0U &&
        response.property_read_attempted == 0U &&
        response.activation_permitted == 0U &&
        response.mutation_performed == 0U &&
        response.live_explorer_touched == 0U);
}

template <typename Mutation>
void CheckObservationRejected(
    Mutation mutation,
    const jarvis_tap_xaml_read_result expected) {
    const auto admission = Admission();
    const auto request = Request(admission);
    auto observation = ObjectObservation();
    mutation(observation);
    const auto response =
        Complete(admission, request, observation);
    Check(
        response.state == JARVIS_TAP_XAML_READ_STATE_BLOCKED &&
        response.result == expected &&
        response.property_read_supported == 0U &&
        response.execution_supported == 0U &&
        response.activation_permitted == 0U &&
        response.mutation_performed == 0U);
}

}  // namespace

int main() {
    Check(
        sizeof(jarvis_tap_xaml_read_request) == 72U &&
        sizeof(jarvis_tap_xaml_foreign_observation) == 120U &&
        sizeof(jarvis_tap_xaml_read_response) == 264U);

    const auto contract =
        jarvis_tap_xaml_read_bridge_query_contract();
    Check(
        contract.state == JARVIS_TAP_XAML_READ_STATE_DISABLED &&
        contract.result ==
            JARVIS_TAP_XAML_READ_RESULT_REVIEW_OBJECT_DISABLED &&
        contract.review_bridge_compiled == 1U &&
        contract.diagnostics_site_touched == 0U &&
        contract.property_read_supported == 0U &&
        contract.execution_supported == 0U &&
        contract.activation_permitted == 0U &&
        contract.mutation_performed == 0U &&
        contract.live_explorer_touched == 0U);

    auto admission = Admission();
    auto request = Request(admission);
    auto response =
        jarvis_tap_xaml_read_bridge_preflight(
            nullptr,
            &request,
            3000ULL);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    response =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            nullptr,
            3000ULL);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);

    CheckPreflightRejected(
        [](auto&, auto& value) { --value.size; },
        JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH);
    CheckPreflightRejected(
        [](auto&, auto& value) { ++value.abi_version; },
        JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH);
    CheckPreflightRejected(
        [](auto& value, auto&) {
            value.state = JARVIS_TAP_ADMISSION_STATE_COLD;
        },
        JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    CheckPreflightRejected(
        [](auto& value, auto&) { value.attempt_count = 2U; },
        JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    CheckPreflightRejected(
        [](auto& value, auto&) { value.plan_consumed = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    CheckPreflightRejected(
        [](auto& value, auto&) { value.reserved = 1U; },
        JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    CheckPreflightRejected(
        [](auto& value, auto&) { --value.bind.size; },
        JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);

    response =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            999ULL);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_CAPABILITY_NOT_CURRENT);
    response =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            121001ULL);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_CAPABILITY_NOT_CURRENT);
    admission.evaluated_at_monotonic_ms = 4000ULL;
    response =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            3000ULL);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_CAPABILITY_NOT_CURRENT);
    admission = Admission();
    request = Request(admission);

    CheckPreflightRejected(
        [](auto&, auto& value) {
            value.surface_slot =
                JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
        },
        JARVIS_TAP_XAML_READ_RESULT_SLOT_INVALID);
    CheckPreflightRejected(
        [](auto&, auto& value) {
            value.property_slot =
                JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
        },
        JARVIS_TAP_XAML_READ_RESULT_SLOT_INVALID);
    CheckPreflightRejected(
        [](auto&, auto& value) { value.reserved = 1U; },
        JARVIS_TAP_XAML_READ_RESULT_SLOT_INVALID);
    CheckPreflightRejected(
        [](auto&, auto& value) { value.sequence = 2ULL; },
        JARVIS_TAP_XAML_READ_RESULT_SEQUENCE_INVALID);
    CheckPreflightRejected(
        [](auto&, auto& value) { value.instance_handle = 0ULL; },
        JARVIS_TAP_XAML_READ_RESULT_INSTANCE_INVALID);
    CheckPreflightRejected(
        [](auto&, auto& value) {
            value.selector_sha256 = Hash(999ULL);
        },
        JARVIS_TAP_XAML_READ_RESULT_SELECTOR_MISMATCH);

    const auto preflight =
        jarvis_tap_xaml_read_bridge_preflight(
            &admission,
            &request,
            3000ULL);
    Check(
        preflight.state == JARVIS_TAP_XAML_READ_STATE_PREFLIGHT &&
        preflight.result ==
            JARVIS_TAP_XAML_READ_RESULT_PREFLIGHT_ACCEPTED &&
        preflight.diagnostics_site_touched == 0U);

    response = jarvis_tap_xaml_read_bridge_accept_target(
        nullptr,
        JARVIS_TAP_TARGET_RESULT_ACCEPTED);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    auto tampered_preflight = preflight;
    tampered_preflight.diagnostics_site_touched = 1U;
    response = jarvis_tap_xaml_read_bridge_accept_target(
        &tampered_preflight,
        JARVIS_TAP_TARGET_RESULT_ACCEPTED);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_ADMISSION_INVALID);
    response = jarvis_tap_xaml_read_bridge_accept_target(
        &preflight,
        JARVIS_TAP_TARGET_RESULT_CURRENT_THREAD_MISMATCH);
    Check(
        response.result ==
            JARVIS_TAP_XAML_READ_RESULT_TARGET_REJECTED &&
        response.target_result ==
            JARVIS_TAP_TARGET_RESULT_CURRENT_THREAD_MISMATCH &&
        response.diagnostics_site_touched == 0U);
    const auto target = jarvis_tap_xaml_read_bridge_accept_target(
        &preflight,
        JARVIS_TAP_TARGET_RESULT_ACCEPTED);
    Check(
        target.state ==
            JARVIS_TAP_XAML_READ_STATE_TARGET_ACCEPTED &&
        target.result ==
            JARVIS_TAP_XAML_READ_RESULT_TARGET_ACCEPTED);

    auto observation = ObjectObservation();
    response = jarvis_tap_xaml_read_bridge_complete(
        nullptr,
        &request,
        &target,
        &observation,
        0U);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_xaml_read_bridge_complete(
        &admission,
        &request,
        &target,
        nullptr,
        0U);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_INVALID_ARGUMENT);

    CheckObservationRejected(
        [](auto& value) { --value.size; },
        JARVIS_TAP_XAML_READ_RESULT_SIZE_MISMATCH);
    CheckObservationRejected(
        [](auto& value) { ++value.abi_version; },
        JARVIS_TAP_XAML_READ_RESULT_ABI_MISMATCH);

    response = jarvis_tap_xaml_read_bridge_complete(
        &admission,
        &request,
        &target,
        &observation,
        2U);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);

    CheckObservationRejected(
        [](auto& value) {
            value.foreign_outcome_uncertain = 1U;
        },
        JARVIS_TAP_XAML_READ_RESULT_FOREIGN_OUTCOME_UNCERTAIN);
    CheckObservationRejected(
        [](auto& value) { value.site_query_succeeded = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_SITE_QUERY_FAILED);
    CheckObservationRejected(
        [](auto& value) { value.service_query_succeeded = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_SERVICE_QUERY_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_chain_call_attempted = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_CHAIN_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_chain_call_succeeded = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_CHAIN_FAILED);
    CheckObservationRejected(
        [](auto& value) { value.property_source_count = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_COUNT_INVALID);
    CheckObservationRejected(
        [](auto& value) {
            value.property_source_count =
                JARVIS_TAP_XAML_READ_MAX_PROPERTY_SOURCE_COUNT + 1U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_COUNT_INVALID);
    CheckObservationRejected(
        [](auto& value) {
            value.property_value_count =
                JARVIS_TAP_XAML_READ_MAX_PROPERTY_VALUE_COUNT + 1U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_COUNT_INVALID);
    CheckObservationRejected(
        [](auto& value) { value.matched_property_count = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_NOT_UNIQUE);
    CheckObservationRejected(
        [](auto& value) { value.matched_property_count = 2U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_NOT_UNIQUE);
    CheckObservationRejected(
        [](auto& value) { value.property_chain_index = 1U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_SOURCE_INVALID);
    CheckObservationRejected(
        [](auto& value) { value.property_value_source = 3U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_ORIGIN_UNSUPPORTED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_metadata_bits |= 0x80ULL;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_METADATA_UNSUPPORTED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_chain_free_required = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE);
    CheckObservationRejected(
        [](auto& value) { value.property_chain_freed = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE);
    CheckObservationRejected(
        [](auto& value) { --value.release_completed_count; },
        JARVIS_TAP_XAML_READ_RESULT_RELEASE_INCOMPLETE);

    const auto null_observation = NullObservation();
    response = Complete(admission, request, null_observation);
    Check(
        response.state == JARVIS_TAP_XAML_READ_STATE_OBSERVED &&
        response.result ==
            JARVIS_TAP_XAML_READ_RESULT_OBSERVATION_ACCEPTED &&
        response.snapshot.runtime_value_kind ==
            JARVIS_TAP_RUNTIME_VALUE_NULL &&
        response.snapshot.value_origin ==
            JARVIS_TAP_PROPERTY_VALUE_ORIGIN_LOCAL &&
        response.property_read_supported == 0U &&
        response.activation_permitted == 0U &&
        response.mutation_performed == 0U &&
        response.live_explorer_touched == 0U);

    auto invalid_null = NullObservation();
    invalid_null.property_handle_call_succeeded = 1U;
    response = Complete(admission, request, invalid_null);
    Check(response.result ==
          JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);

    CheckObservationRejected(
        [](auto& value) { value.property_metadata_bits = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_METADATA_UNSUPPORTED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_handle_call_succeeded = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_HANDLE_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.property_value_handle_nonzero = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_PROPERTY_HANDLE_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.inspectable_call_succeeded = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_INSPECTABLE_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.exact_runtime_class_name_matched = 0U;
        },
        JARVIS_TAP_XAML_READ_RESULT_RUNTIME_CLASS_UNSUPPORTED);
    CheckObservationRejected(
        [](auto& value) { value.brush_read_succeeded = 0U; },
        JARVIS_TAP_XAML_READ_RESULT_BRUSH_READ_FAILED);
    CheckObservationRejected(
        [](auto& value) {
            value.opacity_millionths =
                JARVIS_TAP_OPACITY_MILLIONTHS_MAX + 1U;
        },
        JARVIS_TAP_XAML_READ_RESULT_VALUE_NONCANONICAL);

    response = Complete(
        admission,
        request,
        ObjectObservation());
    Check(
        response.state == JARVIS_TAP_XAML_READ_STATE_OBSERVED &&
        response.result ==
            JARVIS_TAP_XAML_READ_RESULT_OBSERVATION_ACCEPTED &&
        response.snapshot.size ==
            sizeof(jarvis_tap_runtime_property_snapshot) &&
        response.snapshot.sequence == request.sequence &&
        std::memcmp(
            &response.snapshot.target,
            &admission.bind.target,
            sizeof(response.snapshot.target)) == 0 &&
        response.snapshot.runtime_class ==
            JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH &&
        response.snapshot.exact_runtime_class_name_matched == 1U &&
        response.snapshot.argb == 0xFF18202AU &&
        response.snapshot.opacity_millionths == 875000U &&
        response.property_read_supported == 0U &&
        response.execution_supported == 0U &&
        response.activation_permitted == 0U &&
        response.mutation_performed == 0U &&
        response.live_explorer_touched == 0U);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-xaml-read-bridge-policy-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ','
        << "\"passedCount\":" << passed_count << ','
        << "\"syntheticForeignObservations\":true,"
        << "\"windowsReviewObjectExecuted\":false,"
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
