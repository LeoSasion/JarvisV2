#include "jarvis_explorer_tap_style_transaction.h"

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

[[nodiscard]] jarvis_tap_canonical_property_value Original(
    const std::uint32_t index) noexcept {
    if (index % 2U == 0U) {
        return {};
    }
    return jarvis_tap_canonical_property_value{
        .value_kind = JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR,
        .argb = 0xFF102030U + index,
        .opacity_millionths = 900000U + index,
        .reserved = 0U,
    };
}

[[nodiscard]] jarvis_tap_canonical_property_value Styled(
    const std::uint32_t index) noexcept {
    return jarvis_tap_canonical_property_value{
        .value_kind = JARVIS_TAP_PROPERTY_VALUE_SOLID_COLOR,
        .argb = 0xFFAA5500U + index,
        .opacity_millionths = 950000U + index,
        .reserved = 0U,
    };
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
    for (std::uint32_t surface = 0U;
         surface < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++surface) {
        bind.expected_selector_sha256[surface] =
            Hash(50U + surface * 10U);
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto surface =
            index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
        const auto value = Styled(index);
        const jarvis_tap_fingerprint_request request{
            .size = sizeof(jarvis_tap_fingerprint_request),
            .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
            .sequence = static_cast<std::uint64_t>(index) + 1U,
            .target = bind.target,
            .surface_slot = surface,
            .property_slot =
                index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
            .instance_handle =
                0x1000ULL + static_cast<std::uint64_t>(surface),
            .selector_sha256 =
                bind.expected_selector_sha256[surface],
            .value_kind = value.value_kind,
            .argb = value.argb,
            .opacity_millionths = value.opacity_millionths,
            .reserved = 0U,
        };
        jarvis_transport_hash256 output{};
        if (jarvis_tap_fingerprint_compute_canonical(
                &request,
                &output) ==
            JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED) {
            bind.expected_styled_value_sha256[index] = output;
        }
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

[[nodiscard]] jarvis_tap_runtime_property_snapshot RuntimeSnapshot(
    const jarvis_transport_bind_request& bind,
    const std::uint32_t index) noexcept {
    const auto surface =
        index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto property =
        index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    const auto value = Original(index);
    const bool is_null =
        value.value_kind == JARVIS_TAP_PROPERTY_VALUE_NULL;
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
        .argb = value.argb,
        .opacity_millionths = value.opacity_millionths,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

[[nodiscard]] bool Setup(
    jarvis_tap_admission_instance* const admission,
    jarvis_tap_inspectable_adapter_instance* const adapter,
    jarvis_transport_bind_request* const bind) noexcept {
    auto request = ValidAdmissionRequest();
    *bind = request.bind;
    jarvis_tap_admission_reset(admission);
    jarvis_tap_inspectable_adapter_reset(adapter);
    if (jarvis_tap_admission_evaluate(admission, &request).result !=
            JARVIS_TAP_ADMISSION_RESULT_ACCEPTED ||
        jarvis_tap_inspectable_adapter_bind(
            adapter,
            admission,
            bind).result != JARVIS_TAP_ADAPTER_RESULT_ACCEPTED) {
        return false;
    }
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        const auto snapshot = RuntimeSnapshot(*bind, index);
        const auto response =
            jarvis_tap_inspectable_adapter_observe(
                adapter,
                &snapshot);
        const bool final =
            index + 1U ==
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
        if (response.result !=
            (final
                ? JARVIS_TAP_ADAPTER_RESULT_COMPLETE
                : JARVIS_TAP_ADAPTER_RESULT_ACCEPTED)) {
            return false;
        }
    }
    return true;
}

[[nodiscard]] jarvis_tap_style_plan_request Plan(
    const jarvis_transport_bind_request& bind) noexcept {
    jarvis_tap_style_plan_request request{
        .size = sizeof(jarvis_tap_style_plan_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .bind = bind,
        .styled_values = {},
        .prepared_at_monotonic_ms = 100000ULL,
        .reserved = 0U,
    };
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        request.styled_values[index] = Styled(index);
    }
    return request;
}

[[nodiscard]] jarvis_tap_style_step_request Step(
    const jarvis_tap_style_transaction_instance& instance,
    const std::uint32_t index,
    const jarvis_tap_canonical_property_value& observed) noexcept {
    const auto surface =
        index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    return jarvis_tap_style_step_request{
        .size = sizeof(jarvis_tap_style_step_request),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = instance.next_sequence,
        .target = instance.bind.target,
        .surface_slot = surface,
        .property_slot =
            index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
        .instance_handle =
            instance.surface_instance_handles[surface],
        .selector_sha256 = instance.selector_sha256[surface],
        .observed_value = observed,
    };
}

[[nodiscard]] bool IsNonLive(
    const jarvis_tap_style_transaction_response& response) noexcept {
    return response.size == sizeof(response) &&
           response.abi_version ==
               JARVIS_EXPLORER_TRANSPORT_ABI_VERSION &&
           response.transaction_model_supported == 1U &&
           response.property_write_supported == 0U &&
           response.execution_supported == 0U &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U &&
           response.reserved2 == 0U;
}

[[nodiscard]] bool Prepare(
    jarvis_tap_style_transaction_instance* const transaction,
    jarvis_tap_admission_instance* const admission,
    jarvis_tap_inspectable_adapter_instance* const adapter,
    jarvis_transport_bind_request* const bind) noexcept {
    if (!Setup(admission, adapter, bind)) {
        return false;
    }
    auto plan = Plan(*bind);
    jarvis_tap_style_transaction_reset(transaction);
    const auto response = jarvis_tap_style_transaction_prepare(
        transaction,
        admission,
        adapter,
        &plan);
    return IsNonLive(response) &&
           response.result ==
               JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED &&
           response.state ==
               JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED &&
           response.restore_required == 0U;
}

template <typename Mutator>
void CheckRejectedPlan(
    Mutator mutator,
    const jarvis_tap_style_transaction_result expected_result) {
    jarvis_tap_admission_instance admission{};
    jarvis_tap_inspectable_adapter_instance adapter{};
    jarvis_transport_bind_request bind{};
    const bool setup = Setup(&admission, &adapter, &bind);
    auto plan = Plan(bind);
    mutator(plan);
    jarvis_tap_style_transaction_instance transaction{};
    jarvis_tap_style_transaction_reset(&transaction);
    const auto response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admission,
        &adapter,
        &plan);
    Check(
        setup &&
        IsNonLive(response) &&
        response.result == expected_result &&
        response.state ==
            JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED &&
        response.simulated_write_attempt_count == 0U &&
        response.restore_required == 0U);
}

}  // namespace

int main() {
    Check(sizeof(jarvis_tap_style_plan_request) == 784U &&
          sizeof(jarvis_tap_style_step_request) == 176U &&
          sizeof(jarvis_tap_style_transaction_instance) == 1072U &&
          sizeof(jarvis_tap_style_transaction_response) == 80U);
    const auto contract =
        jarvis_tap_style_transaction_query_contract();
    Check(IsNonLive(contract) &&
          contract.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY);

    jarvis_tap_admission_instance admission{};
    jarvis_tap_inspectable_adapter_instance adapter{};
    jarvis_tap_style_transaction_instance transaction{};
    auto bind = ValidBind();
    auto plan = Plan(bind);
    jarvis_tap_style_transaction_reset(&transaction);
    auto response = jarvis_tap_style_transaction_prepare(
        nullptr,
        &admission,
        &adapter,
        &plan);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        nullptr,
        &adapter,
        &plan);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admission,
        nullptr,
        &plan);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admission,
        &adapter,
        nullptr);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_INVALID_ARGUMENT);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admission,
        &adapter,
        &plan);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_ADMISSION_INVALID &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED);

    CheckRejectedPlan(
        [](auto& value) { value.size -= 1U; },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_SIZE_MISMATCH);
    CheckRejectedPlan(
        [](auto& value) { value.abi_version += 1U; },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_ABI_MISMATCH);
    CheckRejectedPlan(
        [](auto& value) { value.reserved = 1U; },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_BIND_INVALID);
    CheckRejectedPlan(
        [](auto& value) {
            value.bind.preview_plan_sha256 = Hash(999U);
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_ADMISSION_INVALID);
    CheckRejectedPlan(
        [](auto& value) {
            value.prepared_at_monotonic_ms =
                value.bind.issued_at_monotonic_ms - 1U;
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_CAPABILITY_NOT_CURRENT);
    CheckRejectedPlan(
        [](auto& value) {
            value.prepared_at_monotonic_ms =
                value.bind.expires_at_monotonic_ms + 1U;
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_CAPABILITY_NOT_CURRENT);
    CheckRejectedPlan(
        [](auto& value) {
            value.prepared_at_monotonic_ms = 160001ULL;
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_DEADLINE_INVALID);
    CheckRejectedPlan(
        [](auto& value) {
            value.styled_values[0U].reserved = 1U;
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_VALUE_INVALID);
    CheckRejectedPlan(
        [](auto& value) {
            value.styled_values[0U].argb += 1U;
        },
        JARVIS_TAP_STYLE_TRANSACTION_RESULT_STYLED_HASH_MISMATCH);

    jarvis_tap_admission_instance admitted{};
    jarvis_tap_inspectable_adapter_instance complete_adapter{};
    jarvis_transport_bind_request complete_bind{};
    Check(Setup(&admitted, &complete_adapter, &complete_bind));
    auto no_change = Plan(complete_bind);
    bool no_change_hashes = true;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        no_change.styled_values[index] = Original(index);
        const auto surface =
            index / JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
        const jarvis_tap_fingerprint_request fingerprint_request{
            .size = sizeof(jarvis_tap_fingerprint_request),
            .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
            .sequence = static_cast<std::uint64_t>(index) + 1U,
            .target = complete_bind.target,
            .surface_slot = surface,
            .property_slot =
                index % JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT,
            .instance_handle =
                0x1000ULL + static_cast<std::uint64_t>(surface),
            .selector_sha256 =
                complete_bind.expected_selector_sha256[surface],
            .value_kind = no_change.styled_values[index].value_kind,
            .argb = no_change.styled_values[index].argb,
            .opacity_millionths =
                no_change.styled_values[index].opacity_millionths,
            .reserved = 0U,
        };
        no_change_hashes =
            no_change_hashes &&
            jarvis_tap_fingerprint_compute_canonical(
                &fingerprint_request,
                &no_change.bind.expected_styled_value_sha256[index]) ==
                JARVIS_TAP_FINGERPRINT_RESULT_ACCEPTED;
    }
    Check(no_change_hashes);
    admitted.bind = no_change.bind;
    jarvis_tap_style_transaction_reset(&transaction);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admitted,
        &complete_adapter,
        &no_change);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_NO_CHANGE &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    Check(transaction.preview_deadline_monotonic_ms == 160000ULL &&
          transaction.next_sequence == 1U &&
          transaction.dirty_mask == 0U &&
          std::memcmp(
              transaction.original_values,
              adapter.canonical_values,
              sizeof(transaction.original_values)) == 0);
    response = jarvis_tap_style_transaction_prepare(
        &transaction,
        &admission,
        &adapter,
        &plan);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_STATE_INVALID &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    auto step = Step(transaction, 0U, Styled(0U));
    step.size -= 1U;
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_SIZE_MISMATCH &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_BLOCKED &&
          response.restore_required == 0U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    step.sequence += 1U;
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_SEQUENCE_INVALID &&
          response.restore_required == 0U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    step.target.visual_tree_generation_sha256 = Hash(999U);
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_IDENTITY_DRIFT &&
          response.restore_required == 0U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    step.surface_slot = 1U;
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_SLOT_INVALID);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    step.instance_handle += 1U;
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_INSTANCE_INVALID);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    step.selector_sha256 = Hash(999U);
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
          JARVIS_TAP_STYLE_TRANSACTION_RESULT_SELECTOR_MISMATCH);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 0U, 0U, 0U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_NOT_ATTEMPTED &&
          response.restore_required == 0U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 0U, 0U);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED &&
          response.restore_required == 1U &&
          response.dirty_property_count == 1U &&
          response.simulated_write_attempt_count == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 0U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED &&
          response.restore_required == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Original(0U));
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH &&
          response.restore_required == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    auto noncanonical = Styled(0U);
    noncanonical.reserved = 1U;
    step = Step(transaction, 0U, noncanonical);
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH &&
          response.restore_required == 1U &&
          response.dirty_property_count == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    step = Step(transaction, 0U, Styled(0U));
    response = jarvis_tap_style_transaction_record_apply(
        &transaction, &step, 1U, 2U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED &&
          response.restore_required == 1U &&
          response.dirty_property_count == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    bool first_three = true;
    for (std::uint32_t index = 0U; index < 3U; ++index) {
        step = Step(transaction, index, Styled(index));
        response = jarvis_tap_style_transaction_record_apply(
            &transaction, &step, 1U, 1U, 1U);
        first_three =
            first_three &&
            response.result ==
                JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED;
    }
    Check(first_three &&
          transaction.verified_apply_count == 3U &&
          transaction.dirty_mask == 0x7U);
    response =
        jarvis_tap_style_transaction_require_restore(&transaction);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_REQUIRED &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED);

    step = Step(transaction, 1U, Original(1U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORE_ORDER_INVALID &&
          response.dirty_property_count == 3U);
    step = Step(transaction, 2U, Original(2U));
    step.target.visual_tree_generation_sha256 = Hash(999U);
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_IDENTITY_DRIFT &&
          response.dirty_property_count == 3U);
    step = Step(transaction, 2U, Original(2U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 0U, 0U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_WRITE_FAILED &&
          response.dirty_property_count == 3U);
    step = Step(transaction, 2U, Original(2U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 0U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_FAILED &&
          response.dirty_property_count == 3U);
    step = Step(transaction, 2U, Styled(2U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH &&
          response.dirty_property_count == 3U);
    auto invalid_original = Original(2U);
    invalid_original.reserved = 1U;
    step = Step(transaction, 2U, invalid_original);
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_VERIFY_MISMATCH &&
          response.dirty_property_count == 3U);
    step = Step(transaction, 2U, Original(2U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED &&
          response.dirty_property_count == 2U);
    step = Step(transaction, 1U, Original(1U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    step = Step(transaction, 0U, Original(0U));
    response = jarvis_tap_style_transaction_record_restore(
        &transaction, &step, 1U, 1U, 1U);
    Check(IsNonLive(response) &&
          response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED &&
          response.dirty_property_count == 0U &&
          response.verified_restore_count == 3U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    response = jarvis_tap_style_transaction_tick(
        &transaction,
        159999ULL);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_MODEL_ONLY &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_PREPARED);
    response = jarvis_tap_style_transaction_tick(
        &transaction,
        160000ULL);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_QUIESCED &&
          response.deadline_reached == 1U);

    Check(Prepare(
        &transaction,
        &admission,
        &adapter,
        &bind));
    bool full_apply = true;
    for (std::uint32_t index = 0U;
         index < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++index) {
        step = Step(transaction, index, Styled(index));
        response = jarvis_tap_style_transaction_record_apply(
            &transaction, &step, 1U, 1U, 1U);
        const bool final =
            index + 1U ==
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
        full_apply =
            full_apply &&
            response.result ==
                (final
                    ? JARVIS_TAP_STYLE_TRANSACTION_RESULT_APPLIED
                    : JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED);
    }
    Check(full_apply &&
          transaction.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_APPLIED &&
          transaction.dirty_mask == 0x1FFU &&
          transaction.verified_apply_count == 9U);
    response = jarvis_tap_style_transaction_tick(
        &transaction,
        160000ULL);
    Check(response.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_TIMEOUT &&
          response.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORE_REQUIRED &&
          response.deadline_reached == 1U);

    bool full_restore = true;
    for (std::uint32_t reverse = 0U;
         reverse < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
         ++reverse) {
        const auto index =
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT - 1U - reverse;
        step = Step(transaction, index, Original(index));
        response = jarvis_tap_style_transaction_record_restore(
            &transaction, &step, 1U, 1U, 1U);
        const bool final =
            reverse + 1U ==
            JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
        full_restore =
            full_restore &&
            response.result ==
                (final
                    ? JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED
                    : JARVIS_TAP_STYLE_TRANSACTION_RESULT_ACCEPTED);
    }
    Check(full_restore &&
          transaction.state ==
              JARVIS_TAP_STYLE_TRANSACTION_STATE_RESTORED &&
          transaction.dirty_mask == 0U &&
          transaction.verified_restore_count == 9U &&
          transaction.simulated_write_attempt_count == 18U);
    const auto query =
        jarvis_tap_style_transaction_query(&transaction);
    Check(IsNonLive(query) &&
          query.result ==
              JARVIS_TAP_STYLE_TRANSACTION_RESULT_RESTORED &&
          query.restore_required == 0U);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-style-transaction-test\","
        << "\"result\":\"" << (passed ? "passed" : "failed") << "\","
        << "\"scenarioCount\":" << scenario_count << ','
        << "\"passedCount\":" << passed_count << ','
        << "\"simulatedWriteAttempts\":true,"
        << "\"platformWriteAttempted\":false,"
        << "\"propertyWriteSupported\":false,"
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
