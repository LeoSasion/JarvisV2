#include "jarvis_explorer_transport_contract.h"

#include <cstdint>
#include <iostream>

namespace {

constexpr std::uint64_t kNowMs = 100000ULL;
constexpr std::uint64_t kIssuedMs = 90000ULL;
constexpr std::uint64_t kExpiresMs = 210000ULL;

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

[[nodiscard]] jarvis_transport_target_identity Identity() noexcept {
    return jarvis_transport_target_identity{
        .explorer_process_id = 4242U,
        .desktop_shell_process_id = 1000U,
        .window_thread_id = 9001U,
        .reserved = 0U,
        .window_handle = 0x1234ULL,
        .process_start_time_utc_ticks = 638000000000000000ULL,
        .visual_tree_generation_sha256 = Hash(5U),
        .exact_window_title_sha256 = Hash(10U),
    };
}

[[nodiscard]] jarvis_transport_bind_request BindRequest() noexcept {
    jarvis_transport_bind_request request{
        .size = sizeof(jarvis_transport_bind_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .target = Identity(),
        .session_nonce = Hash(20U),
        .selector_profile_sha256 = Hash(30U),
        .preview_plan_sha256 = Hash(40U),
        .expected_selector_sha256 = {},
        .expected_styled_value_sha256 = {},
        .issued_at_monotonic_ms = kIssuedMs,
        .expires_at_monotonic_ms = kExpiresMs,
        .preview_duration_ms = JARVIS_TRANSPORT_PREVIEW_DURATION_MS,
        .required_surface_count =
            JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT,
        .required_property_count =
            JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
        .reserved = 0U,
    };
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
            Hash(200U + index * 10U);
    }
    return request;
}

[[nodiscard]] bool IsNonLive(
    const jarvis_transport_response& response) noexcept {
    return response.size == sizeof(jarvis_transport_response) &&
           response.abi_version ==
               JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           response.execution_supported == 0U &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U;
}

[[nodiscard]] jarvis_transport_model_instance BoundInstance() noexcept {
    jarvis_transport_model_instance instance{};
    jarvis_transport_model_reset(&instance);
    const auto request = BindRequest();
    static_cast<void>(
        jarvis_transport_model_bind(&instance, &request, kNowMs));
    return instance;
}

[[nodiscard]] jarvis_transport_surface_request SurfaceRequest(
    const jarvis_transport_model_instance& instance,
    const std::uint32_t surface_slot) noexcept {
    return jarvis_transport_surface_request{
        .size = sizeof(jarvis_transport_surface_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = instance.next_sequence,
        .target = instance.target,
        .surface_slot = surface_slot,
        .match_count = 1U,
        .instance_handle = 0x5000ULL + surface_slot,
        .selector_sha256 = Hash(50U + surface_slot * 10U),
    };
}

void ObserveAll(jarvis_transport_model_instance* const instance) noexcept {
    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        const auto request = SurfaceRequest(*instance, slot);
        static_cast<void>(
            jarvis_transport_model_observe_surface(instance, &request));
    }
}

[[nodiscard]] jarvis_transport_property_request PropertyRequest(
    const jarvis_transport_model_instance& instance,
    const std::uint32_t flat_index,
    const std::uint64_t hash_seed,
    const std::uint64_t observed_at_ms = kNowMs + 100U) noexcept {
    const auto surface_slot =
        flat_index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto property_slot =
        flat_index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    return jarvis_transport_property_request{
        .size = sizeof(jarvis_transport_property_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = instance.next_sequence,
        .target = instance.target,
        .surface_slot = surface_slot,
        .property_slot = property_slot,
        .instance_handle =
            instance.surface_instance_handles[surface_slot],
        .value_sha256 = Hash(hash_seed),
        .observed_at_monotonic_ms = observed_at_ms,
    };
}

void JournalAll(jarvis_transport_model_instance* const instance) noexcept {
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto request = PropertyRequest(
            *instance,
            index,
            100U + index * 10U);
        static_cast<void>(
            jarvis_transport_model_journal_original(instance, &request));
    }
}

[[nodiscard]] jarvis_transport_model_instance
JournaledInstance() noexcept {
    auto instance = BoundInstance();
    ObserveAll(&instance);
    JournalAll(&instance);
    return instance;
}

void ApplyAll(jarvis_transport_model_instance* const instance) noexcept {
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto request = PropertyRequest(
            *instance,
            index,
            200U + index * 10U,
            kNowMs + 100U + index);
        static_cast<void>(
            jarvis_transport_model_record_apply(instance, &request, 1U));
    }
}

void RestoreAll(jarvis_transport_model_instance* const instance) noexcept {
    while (instance->applied_property_count != 0U) {
        const auto index = instance->applied_property_count - 1U;
        const auto request = PropertyRequest(
            *instance,
            index,
            100U + index * 10U,
            kNowMs + 70000U);
        static_cast<void>(
            jarvis_transport_model_record_restore(instance, &request, 1U));
    }
}

}  // namespace

int main() {
    auto response = jarvis_transport_model_query_contract();
    Check(IsNonLive(response) &&
          response.state == JARVIS_TRANSPORT_STATE_COLD &&
          response.result == JARVIS_TRANSPORT_RESULT_MODEL_ONLY);

    jarvis_transport_model_instance instance{};
    jarvis_transport_model_reset(&instance);
    Check(instance.state == JARVIS_TRANSPORT_STATE_COLD &&
          instance.bind_attempt_count == 0U &&
          instance.next_sequence == 0U);

    auto bind_request = BindRequest();
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(IsNonLive(response) &&
          response.state == JARVIS_TRANSPORT_STATE_BOUND &&
          response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.next_sequence == 1U);

    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.state == JARVIS_TRANSPORT_STATE_BLOCKED &&
          response.result == JARVIS_TRANSPORT_RESULT_BIND_REPLAY);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.size -= 1U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.abi_version += 1U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ABI_MISMATCH);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.explorer_process_id = 0U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.desktop_shell_process_id = 0U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.desktop_shell_process_id =
        bind_request.target.explorer_process_id;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.window_thread_id = 0U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.window_handle = 0U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.process_start_time_utc_ticks = 0U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.visual_tree_generation_sha256 = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.target.exact_window_title_sha256 = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.session_nonce = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.selector_profile_sha256 = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.preview_plan_sha256 = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.expected_selector_sha256[1] = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.expected_styled_value_sha256[7] = {};
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.reserved = 1U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.issued_at_monotonic_ms = kNowMs + 1U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.expires_at_monotonic_ms = kNowMs;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.issued_at_monotonic_ms = 1U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.preview_duration_ms = 59000U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.required_surface_count = 4U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    instance = {};
    jarvis_transport_model_reset(&instance);
    bind_request = BindRequest();
    bind_request.required_property_count = 4U;
    response = jarvis_transport_model_bind(
        &instance,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID);

    bind_request = BindRequest();
    response = jarvis_transport_model_bind(
        nullptr,
        &bind_request,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    instance = {};
    jarvis_transport_model_reset(&instance);
    response = jarvis_transport_model_bind(
        &instance,
        nullptr,
        kNowMs);
    Check(response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    instance = BoundInstance();
    auto surface_request = SurfaceRequest(instance, 0U);
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.observed_surface_count == 1U &&
          response.next_sequence == 2U);

    instance = {};
    jarvis_transport_model_reset(&instance);
    surface_request = SurfaceRequest(BoundInstance(), 0U);
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_STATE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.size -= 1U;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.abi_version += 1U;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ABI_MISMATCH);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    ++surface_request.sequence;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SEQUENCE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    ++surface_request.target.explorer_process_id;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    ++surface_request.target.visual_tree_generation_sha256.words[0];
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 1U);
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.match_count = 2U;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.instance_handle = 0U;
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.selector_sha256 = {};
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    surface_request.selector_sha256 = Hash(999U);
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_INVALID);

    instance = BoundInstance();
    surface_request = SurfaceRequest(instance, 0U);
    static_cast<void>(
        jarvis_transport_model_observe_surface(
            &instance,
            &surface_request));
    surface_request = SurfaceRequest(instance, 1U);
    surface_request.instance_handle =
        instance.surface_instance_handles[0];
    response = jarvis_transport_model_observe_surface(
        &instance,
        &surface_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SURFACE_NOT_UNIQUE);

    instance = BoundInstance();
    ObserveAll(&instance);
    Check(instance.state == JARVIS_TRANSPORT_STATE_DISCOVERED &&
          instance.observed_surface_count == 3U &&
          instance.next_sequence == 4U);

    instance = BoundInstance();
    auto property_request = PropertyRequest(
        instance,
        0U,
        100U);
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_STATE_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.journaled_property_count == 1U);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    property_request.size -= 1U;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    property_request.abi_version += 1U;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ABI_MISMATCH);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    ++property_request.sequence;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_SEQUENCE_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    ++property_request.target.window_thread_id;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    ++property_request.target.visual_tree_generation_sha256.words[0];
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    property_request.surface_slot = 3U;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    property_request.property_slot = 3U;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    ++property_request.instance_handle;
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 100U);
    property_request.value_sha256 = {};
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 1U, 110U);
    response = jarvis_transport_model_journal_original(
        &instance,
        &property_request);
    Check(response.result == JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID);

    instance = BoundInstance();
    ObserveAll(&instance);
    JournalAll(&instance);
    Check(instance.state == JARVIS_TRANSPORT_STATE_JOURNALED &&
          instance.journaled_property_count == 9U &&
          instance.next_sequence == 13U);

    instance = BoundInstance();
    ObserveAll(&instance);
    property_request = PropertyRequest(instance, 0U, 200U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_STATE_INVALID);

    instance = JournaledInstance();
    property_request = PropertyRequest(instance, 1U, 210U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_APPLY_INVALID);

    instance = JournaledInstance();
    property_request = PropertyRequest(instance, 0U, 999U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_APPLY_INVALID);

    instance = JournaledInstance();
    property_request = PropertyRequest(
        instance,
        0U,
        200U,
        kExpiresMs);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);

    instance = JournaledInstance();
    property_request = PropertyRequest(
        instance,
        0U,
        200U,
        kExpiresMs - JARVIS_TRANSPORT_PREVIEW_DURATION_MS + 1U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED);

    instance = JournaledInstance();
    property_request = PropertyRequest(instance, 0U, 200U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        0U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_APPLY_FAILED &&
          response.state == JARVIS_TRANSPORT_STATE_BLOCKED &&
          response.applied_property_count == 0U);

    instance = JournaledInstance();
    property_request = PropertyRequest(instance, 0U, 200U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.state == JARVIS_TRANSPORT_STATE_APPLYING &&
          response.capability_consumed == 1U &&
          instance.preview_deadline_monotonic_ms ==
              kNowMs + 100U +
                  JARVIS_TRANSPORT_PREVIEW_DURATION_MS);

    property_request = PropertyRequest(instance, 1U, 210U);
    ++property_request.target.window_handle;
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    instance = JournaledInstance();
    property_request = PropertyRequest(instance, 0U, 200U);
    static_cast<void>(
        jarvis_transport_model_record_apply(
            &instance,
            &property_request,
            1U));
    property_request = PropertyRequest(instance, 1U, 210U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        0U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_APPLY_FAILED &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED &&
          response.applied_property_count == 1U);

    property_request = PropertyRequest(instance, 1U, 210U);
    response = jarvis_transport_model_record_apply(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_STATE_INVALID &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    response = jarvis_transport_model_quiesce(&instance);
    Check(response.result ==
              JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    property_request = PropertyRequest(instance, 1U, 110U);
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_RESTORE_INVALID);

    property_request = PropertyRequest(instance, 0U, 999U);
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_RESTORE_INVALID);

    property_request = PropertyRequest(instance, 0U, 100U);
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        0U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_RESTORE_FAILED &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    property_request = PropertyRequest(instance, 0U, 100U);
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_RESTORED &&
          response.state == JARVIS_TRANSPORT_STATE_RESTORED &&
          response.applied_property_count == 0U &&
          response.restored_property_count == 1U);

    instance = JournaledInstance();
    ApplyAll(&instance);
    Check(instance.state == JARVIS_TRANSPORT_STATE_APPLIED &&
          instance.applied_property_count == 9U &&
          instance.simulated_mutation_count == 9U);

    response = jarvis_transport_model_tick(
        &instance,
        instance.preview_deadline_monotonic_ms - 1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.state == JARVIS_TRANSPORT_STATE_APPLIED);

    response = jarvis_transport_model_quiesce(&instance);
    Check(response.result ==
              JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    response = jarvis_transport_model_tick(
        &instance,
        instance.preview_deadline_monotonic_ms);
    Check(response.result ==
              JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED &&
          response.restore_required == 1U);

    property_request = PropertyRequest(instance, 8U, 180U);
    ++property_request.target.visual_tree_generation_sha256.words[0];
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        1U);
    Check(response.result ==
              JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT &&
          response.state ==
              JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED);

    property_request = PropertyRequest(instance, 8U, 180U);
    response = jarvis_transport_model_record_restore(
        &instance,
        &property_request,
        1U);
    Check(response.result == JARVIS_TRANSPORT_RESULT_ACCEPTED &&
          response.state == JARVIS_TRANSPORT_STATE_RESTORING &&
          response.applied_property_count == 8U);

    RestoreAll(&instance);
    Check(instance.state == JARVIS_TRANSPORT_STATE_RESTORED &&
          instance.applied_property_count == 0U &&
          instance.restored_property_count == 9U &&
          instance.simulated_mutation_count == 18U);

    response = jarvis_transport_model_quiesce(&instance);
    Check(response.result == JARVIS_TRANSPORT_RESULT_QUIESCED &&
          response.state == JARVIS_TRANSPORT_STATE_QUIESCED);

    instance = BoundInstance();
    response = jarvis_transport_model_quiesce(&instance);
    Check(response.result == JARVIS_TRANSPORT_RESULT_QUIESCED &&
          response.state == JARVIS_TRANSPORT_STATE_QUIESCED);

    response = jarvis_transport_model_query(&instance);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_MODEL_ONLY &&
          response.state == JARVIS_TRANSPORT_STATE_QUIESCED);

    response = jarvis_transport_model_query(nullptr);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    response = jarvis_transport_model_tick(nullptr, kNowMs);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    property_request = {};
    response = jarvis_transport_model_record_restore(
        nullptr,
        &property_request,
        1U);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    response = jarvis_transport_model_record_apply(
        nullptr,
        &property_request,
        1U);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    response = jarvis_transport_model_observe_surface(
        nullptr,
        &surface_request);
    Check(IsNonLive(response) &&
          response.result == JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-explorer-transport-model-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ","
        << "\"passedCount\":" << passed_count << ","
        << "\"executionSupported\":false,"
        << "\"activationPermitted\":false,"
        << "\"liveExplorer\":\"not-run\","
        << "\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
