#pragma once

#include <cstdint>

// Phase 11 defines a portable, offline state-machine model for a future
// standalone XAML Diagnostics transport. It is not a TAP DLL, loader, injector,
// process controller, or live Explorer implementation.

inline constexpr std::uint32_t JARVIS_EXPLORER_TRANSPORT_ABI_VERSION = 1U;
inline constexpr std::uint32_t JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT = 3U;
inline constexpr std::uint32_t JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT = 3U;
inline constexpr std::uint32_t JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT = 9U;
inline constexpr std::uint32_t JARVIS_TRANSPORT_PREVIEW_DURATION_MS = 60000U;
inline constexpr std::uint64_t JARVIS_TRANSPORT_MAX_CAPABILITY_AGE_MS = 120000ULL;

using jarvis_transport_state = std::uint32_t;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_COLD = 0U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_BOUND = 1U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_DISCOVERED = 2U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_JOURNALED = 3U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_APPLYING = 4U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_APPLIED = 5U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_RESTORE_REQUIRED = 6U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_RESTORING = 7U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_RESTORED = 8U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_QUIESCED = 9U;
inline constexpr jarvis_transport_state JARVIS_TRANSPORT_STATE_BLOCKED = 10U;

using jarvis_transport_result = std::uint32_t;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_MODEL_ONLY = 0U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_ACCEPTED = 1U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_INVALID_ARGUMENT = 2U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_SIZE_MISMATCH = 3U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_ABI_MISMATCH = 4U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_STATE_INVALID = 5U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_BIND_REPLAY = 6U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_IDENTITY_INVALID = 7U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_IDENTITY_DRIFT = 8U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_GENERATION_DRIFT = 9U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_CAPABILITY_INVALID = 10U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_CAPABILITY_EXPIRED = 11U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_SEQUENCE_INVALID = 12U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_SURFACE_INVALID = 13U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_SURFACE_NOT_UNIQUE = 14U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_JOURNAL_INVALID = 15U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_APPLY_INVALID = 16U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_APPLY_FAILED = 17U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_RESTORE_INVALID = 18U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_RESTORE_FAILED = 19U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_RESTORE_REQUIRED = 20U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_RESTORED = 21U;
inline constexpr jarvis_transport_result JARVIS_TRANSPORT_RESULT_QUIESCED = 22U;

struct jarvis_transport_hash256 final {
    std::uint64_t words[4];
};

struct jarvis_transport_target_identity final {
    std::uint32_t explorer_process_id;
    std::uint32_t desktop_shell_process_id;
    std::uint32_t window_thread_id;
    std::uint32_t reserved;
    std::uint64_t window_handle;
    std::uint64_t process_start_time_utc_ticks;
    jarvis_transport_hash256 visual_tree_generation_sha256;
    jarvis_transport_hash256 exact_window_title_sha256;
};

struct jarvis_transport_bind_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_transport_target_identity target;
    jarvis_transport_hash256 session_nonce;
    jarvis_transport_hash256 selector_profile_sha256;
    jarvis_transport_hash256 preview_plan_sha256;
    jarvis_transport_hash256 expected_selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 expected_styled_value_sha256[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint64_t issued_at_monotonic_ms;
    std::uint64_t expires_at_monotonic_ms;
    std::uint32_t preview_duration_ms;
    std::uint32_t required_surface_count;
    std::uint32_t required_property_count;
    std::uint32_t reserved;
};

struct jarvis_transport_surface_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_transport_target_identity target;
    std::uint32_t surface_slot;
    std::uint32_t match_count;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 selector_sha256;
};

struct jarvis_transport_property_request final {
    std::uint32_t size;
    std::uint32_t abi_version;
    std::uint64_t sequence;
    jarvis_transport_target_identity target;
    std::uint32_t surface_slot;
    std::uint32_t property_slot;
    std::uint64_t instance_handle;
    jarvis_transport_hash256 value_sha256;
    std::uint64_t observed_at_monotonic_ms;
};

struct jarvis_transport_response final {
    std::uint32_t size;
    std::uint32_t abi_version;
    jarvis_transport_state state;
    jarvis_transport_result result;
    std::uint32_t next_sequence;
    std::uint32_t observed_surface_count;
    std::uint32_t journaled_property_count;
    std::uint32_t applied_property_count;
    std::uint32_t restored_property_count;
    std::uint32_t capability_consumed;
    std::uint32_t restore_required;
    std::uint32_t execution_supported;
    std::uint32_t activation_permitted;
    std::uint32_t mutation_performed;
    std::uint32_t live_explorer_touched;
    std::uint32_t reserved;
};

struct jarvis_transport_model_instance final {
    jarvis_transport_state state;
    std::uint32_t bind_attempt_count;
    std::uint64_t next_sequence;
    jarvis_transport_target_identity target;
    jarvis_transport_hash256 session_nonce;
    jarvis_transport_hash256 selector_profile_sha256;
    jarvis_transport_hash256 preview_plan_sha256;
    jarvis_transport_hash256 expected_selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 expected_styled_value_sha256[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint64_t issued_at_monotonic_ms;
    std::uint64_t expires_at_monotonic_ms;
    std::uint64_t preview_deadline_monotonic_ms;
    std::uint64_t surface_instance_handles[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 surface_selector_sha256[
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT];
    jarvis_transport_hash256 original_value_sha256[
        JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT];
    std::uint32_t observed_surface_count;
    std::uint32_t journaled_property_count;
    std::uint32_t applied_property_count;
    std::uint32_t restored_property_count;
    std::uint32_t capability_consumed;
    std::uint32_t restore_required;
    std::uint32_t simulated_mutation_count;
    std::uint32_t reserved;
};

static_assert(sizeof(jarvis_transport_hash256) == 32U);
static_assert(sizeof(jarvis_transport_target_identity) == 96U);
static_assert(sizeof(jarvis_transport_bind_request) == 616U);
static_assert(sizeof(jarvis_transport_surface_request) == 160U);
static_assert(sizeof(jarvis_transport_property_request) == 168U);
static_assert(sizeof(jarvis_transport_response) == 64U);
static_assert(sizeof(jarvis_transport_model_instance) == 1056U);

void jarvis_transport_model_reset(
    jarvis_transport_model_instance* instance) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_query_contract() noexcept;

[[nodiscard]] jarvis_transport_response jarvis_transport_model_bind(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_bind_request* request,
    std::uint64_t now_monotonic_ms) noexcept;

[[nodiscard]] jarvis_transport_response jarvis_transport_model_observe_surface(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_surface_request* request) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_journal_original(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_property_request* request) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_record_apply(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_property_request* request,
    std::uint32_t platform_write_succeeded) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_tick(
    jarvis_transport_model_instance* instance,
    std::uint64_t now_monotonic_ms) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_record_restore(
    jarvis_transport_model_instance* instance,
    const jarvis_transport_property_request* request,
    std::uint32_t platform_write_succeeded) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_quiesce(
    jarvis_transport_model_instance* instance) noexcept;

[[nodiscard]] jarvis_transport_response
jarvis_transport_model_query(
    const jarvis_transport_model_instance* instance) noexcept;
