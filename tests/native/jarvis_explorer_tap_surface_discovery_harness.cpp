#include "../../src/Jarvis.ExplorerTapReadOnly/jarvis_explorer_tap_surface_discovery.h"

#include <cstdint>
#include <cstring>
#include <functional>
#include <iostream>
#include <string_view>

namespace {

constexpr unsigned char kSelectorHashes
    [JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT][32] = {
        {
            0x05U, 0x4fU, 0x71U, 0xd3U, 0x3dU, 0x72U, 0x0aU, 0x35U,
            0x25U, 0xbfU, 0x1aU, 0xabU, 0x83U, 0x00U, 0x4dU, 0xe1U,
            0xd0U, 0x2fU, 0xbdU, 0xe3U, 0x06U, 0xb9U, 0xcdU, 0xe5U,
            0x04U, 0x47U, 0x05U, 0xb1U, 0xb9U, 0x82U, 0xb6U, 0x4bU,
        },
        {
            0xacU, 0x82U, 0xb7U, 0x52U, 0x9cU, 0xd2U, 0x69U, 0xb4U,
            0xc6U, 0x0dU, 0xc2U, 0x13U, 0x32U, 0xf9U, 0x34U, 0x10U,
            0x3aU, 0xb8U, 0xbbU, 0xcaU, 0x1bU, 0x2cU, 0xe2U, 0xeeU,
            0xedU, 0x6aU, 0xa5U, 0x03U, 0x3aU, 0x09U, 0x57U, 0x6fU,
        },
        {
            0xa5U, 0x8dU, 0xa1U, 0xacU, 0xe7U, 0xb8U, 0xbcU, 0x9bU,
            0x40U, 0x93U, 0xd6U, 0x2eU, 0xf6U, 0xfeU, 0xb7U, 0xe6U,
            0x8aU, 0xc0U, 0x5eU, 0x1cU, 0x6dU, 0xcfU, 0xebU, 0x10U,
            0xbbU, 0x40U, 0x3cU, 0xb7U, 0x66U, 0x98U, 0x26U, 0x2aU,
        },
    };

struct Harness final {
    std::uint32_t scenario_count = 0U;
    std::uint32_t passed_count = 0U;

    void Run(
        const std::string_view,
        const std::function<bool()>& body) {
        ++scenario_count;
        if (body()) {
            ++passed_count;
        }
    }
};

void SetHash(
    jarvis_transport_hash256* const output,
    const unsigned char (&bytes)[32]) {
    std::memcpy(output, bytes, sizeof(*output));
}

jarvis_tap_admission_instance MakeAdmission() {
    jarvis_tap_admission_instance admission{};
    admission.state = JARVIS_TAP_ADMISSION_STATE_ADMITTED;
    admission.attempt_count = 1U;
    admission.plan_consumed = 1U;
    admission.bind.size = sizeof(admission.bind);
    admission.bind.abi_version =
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
    admission.bind.target.explorer_process_id = 100U;
    admission.bind.target.desktop_shell_process_id = 200U;
    admission.bind.target.window_thread_id = 300U;
    admission.bind.target.window_handle = 0x1234ULL;
    admission.bind.target.process_start_time_utc_ticks = 400ULL;
    admission.bind.target.visual_tree_generation_sha256.words[0] = 1ULL;
    admission.bind.issued_at_monotonic_ms = 100ULL;
    admission.bind.expires_at_monotonic_ms = 1000ULL;
    admission.bind.preview_duration_ms =
        JARVIS_TRANSPORT_PREVIEW_DURATION_MS;
    admission.bind.required_surface_count =
        JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
    admission.bind.required_property_count =
        JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT;
    admission.evaluated_at_monotonic_ms = 100ULL;
    for (std::uint32_t slot = 0U;
         slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++slot) {
        SetHash(
            &admission.bind.expected_selector_sha256[slot],
            kSelectorHashes[slot]);
    }
    return admission;
}

jarvis_tap_visual_tree_event MakeEvent(
    const jarvis_tap_surface_discovery_instance& instance,
    const std::uint64_t handle,
    const std::uint64_t parent,
    const jarvis_tap_visual_type type,
    const jarvis_tap_visual_name name =
        JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER,
    const jarvis_tap_visual_mutation mutation =
        JARVIS_TAP_VISUAL_MUTATION_ADD) {
    return jarvis_tap_visual_tree_event{
        .size = sizeof(jarvis_tap_visual_tree_event),
        .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
        .sequence = instance.next_sequence,
        .mutation = mutation,
        .type = type,
        .name = name,
        .child_index = 0U,
        .parent_handle = parent,
        .child_handle = handle,
        .instance_handle = handle,
        .reserved = 0U,
        .reserved2 = 0U,
    };
}

bool Bind(
    jarvis_tap_surface_discovery_instance* const instance,
    jarvis_tap_admission_instance* const admission) {
    jarvis_tap_surface_discovery_reset(instance);
    return jarvis_tap_surface_discovery_bind(
               instance,
               admission,
               200ULL)
               .result == JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
}

bool Add(
    jarvis_tap_surface_discovery_instance* const instance,
    const std::uint64_t handle,
    const std::uint64_t parent,
    const jarvis_tap_visual_type type,
    const jarvis_tap_visual_name name =
        JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER) {
    const auto event = MakeEvent(
        *instance,
        handle,
        parent,
        type,
        name);
    return jarvis_tap_surface_discovery_ingest(
               instance,
               &event)
               .result == JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED;
}

bool AddValidTree(
    jarvis_tap_surface_discovery_instance* const instance,
    const bool deep) {
    if (!Add(instance, 1ULL, 0ULL, JARVIS_TAP_VISUAL_TYPE_OTHER) ||
        !Add(
            instance,
            10ULL,
            1ULL,
            JARVIS_TAP_VISUAL_TYPE_TAB_CONTROL)) {
        return false;
    }
    std::uint64_t tab_parent = 10ULL;
    if (deep) {
        if (!Add(
                instance,
                11ULL,
                10ULL,
                JARVIS_TAP_VISUAL_TYPE_OTHER)) {
            return false;
        }
        tab_parent = 11ULL;
    }
    if (!Add(
            instance,
            12ULL,
            tab_parent,
            JARVIS_TAP_VISUAL_TYPE_GRID,
            JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID) ||
        !Add(
            instance,
            20ULL,
            1ULL,
            JARVIS_TAP_VISUAL_TYPE_COMMAND_BAR_CONTROL)) {
        return false;
    }
    std::uint64_t command_parent = 20ULL;
    if (deep) {
        if (!Add(
                instance,
                21ULL,
                20ULL,
                JARVIS_TAP_VISUAL_TYPE_OTHER)) {
            return false;
        }
        command_parent = 21ULL;
    }
    return Add(
               instance,
               22ULL,
               command_parent,
               JARVIS_TAP_VISUAL_TYPE_GRID,
               JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID) &&
           Add(
               instance,
               30ULL,
               1ULL,
               JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW);
}

jarvis_tap_discovery_result IngestInvalid(
    jarvis_tap_surface_discovery_instance* const instance,
    const jarvis_tap_visual_tree_event& event) {
    return jarvis_tap_surface_discovery_ingest(instance, &event).result;
}

}  // namespace

int main() {
    Harness harness;

    harness.Run("contract-locked", [] {
        const auto response =
            jarvis_tap_surface_discovery_query_contract();
        return response.state ==
                   JARVIS_TAP_DISCOVERY_STATE_DISABLED &&
               response.result ==
                   JARVIS_TAP_DISCOVERY_RESULT_REVIEW_OBJECT_DISABLED &&
               response.diagnostics_site_touched == 0U &&
               response.callback_subscription_attempted == 0U &&
               response.property_read_attempted == 0U &&
               response.property_write_supported == 0U &&
               response.execution_supported == 0U &&
               response.ready_for_live_connection == 0U &&
               response.ready_for_exact_approval == 0U &&
               response.activation_permitted == 0U &&
               response.mutation_performed == 0U &&
               response.live_explorer_touched == 0U;
    });
    harness.Run("valid-direct-wildcard-zero", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, false) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_COMPLETE;
    });
    harness.Run("valid-deep-wildcard-many", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_COMPLETE;
    });
    harness.Run("complete-exact-handles", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true)) {
            return false;
        }
        const auto response =
            jarvis_tap_surface_discovery_finalize(&instance);
        return response.surface_instance_handles[0] == 12ULL &&
               response.surface_instance_handles[1] == 22ULL &&
               response.surface_instance_handles[2] == 30ULL &&
               response.matched_surface_count == 3U &&
               response.read_request_count == 9U;
    });
    harness.Run("nine-read-requests-exact-order", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true) ||
            jarvis_tap_surface_discovery_finalize(&instance).result !=
                JARVIS_TAP_DISCOVERY_RESULT_COMPLETE) {
            return false;
        }
        constexpr std::uint64_t handles[] = {
            12ULL, 22ULL, 30ULL};
        for (std::uint32_t slot = 0U;
             slot < JARVIS_TRANSPORT_REQUIRED_JOURNAL_COUNT;
             ++slot) {
            jarvis_tap_xaml_read_request request{};
            if (jarvis_tap_surface_discovery_build_read_request(
                    &instance,
                    slot,
                    &request) !=
                    JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED ||
                request.sequence !=
                    static_cast<std::uint64_t>(slot) + 1ULL ||
                request.surface_slot != slot / 3U ||
                request.property_slot != slot % 3U ||
                request.instance_handle != handles[slot / 3U] ||
                std::memcmp(
                    &request.selector_sha256,
                    kSelectorHashes[slot / 3U],
                    sizeof(request.selector_sha256)) != 0) {
                return false;
            }
        }
        return true;
    });
    harness.Run("bind-null-instance", [] {
        auto admission = MakeAdmission();
        return jarvis_tap_surface_discovery_bind(
                   nullptr,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("bind-null-admission", [] {
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   nullptr,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("bind-state-replay", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
                   JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID;
    });
    harness.Run("admission-state", [] {
        auto admission = MakeAdmission();
        admission.state = JARVIS_TAP_ADMISSION_STATE_BLOCKED;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("admission-attempt-count", [] {
        auto admission = MakeAdmission();
        admission.attempt_count = 2U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("admission-plan-unconsumed", [] {
        auto admission = MakeAdmission();
        admission.plan_consumed = 0U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("bind-size", [] {
        auto admission = MakeAdmission();
        admission.bind.size = 0U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("bind-abi", [] {
        auto admission = MakeAdmission();
        admission.bind.abi_version = 2U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("bind-duration", [] {
        auto admission = MakeAdmission();
        admission.bind.preview_duration_ms = 1U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("bind-surface-count", [] {
        auto admission = MakeAdmission();
        admission.bind.required_surface_count = 2U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("bind-property-count", [] {
        auto admission = MakeAdmission();
        admission.bind.required_property_count = 2U;
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("generation-zero", [] {
        auto admission = MakeAdmission();
        admission.bind.target.visual_tree_generation_sha256 = {};
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_ADMISSION_INVALID;
    });
    harness.Run("capability-before-issued", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   99ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_CAPABILITY_NOT_CURRENT;
    });
    harness.Run("capability-expired", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   1001ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_CAPABILITY_NOT_CURRENT;
    });
    harness.Run("selector-hash-zero", [] {
        auto admission = MakeAdmission();
        admission.bind.expected_selector_sha256[0] = {};
        jarvis_tap_surface_discovery_instance instance{};
        return jarvis_tap_surface_discovery_bind(
                   &instance,
                   &admission,
                   200ULL)
                   .result ==
               JARVIS_TAP_DISCOVERY_RESULT_SELECTOR_PROFILE_MISMATCH;
    });
    for (std::uint32_t corrupted_slot = 0U;
         corrupted_slot < JARVIS_TRANSPORT_REQUIRED_SURFACE_COUNT;
         ++corrupted_slot) {
        harness.Run("selector-hash-drift", [corrupted_slot] {
            auto admission = MakeAdmission();
            ++admission.bind.expected_selector_sha256[
                  corrupted_slot]
                  .words[3];
            jarvis_tap_surface_discovery_instance instance{};
            return jarvis_tap_surface_discovery_bind(
                       &instance,
                       &admission,
                       200ULL)
                       .result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SELECTOR_PROFILE_MISMATCH;
        });
    }
    harness.Run("event-null", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               jarvis_tap_surface_discovery_ingest(
                   &instance,
                   nullptr)
                   .result ==
                   JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("event-size", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.size = 0U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_SIZE_MISMATCH;
    });
    harness.Run("event-abi", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.abi_version = 2U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_ABI_MISMATCH;
    });
    harness.Run("event-sequence", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        ++event.sequence;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_SEQUENCE_INVALID;
    });
    harness.Run("event-zero-handle", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        const auto event = MakeEvent(
            instance,
            0ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-child-mismatch", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.child_handle = 2ULL;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-self-parent", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        const auto event = MakeEvent(
            instance,
            1ULL,
            1ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-mutation", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.mutation = 9U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-type", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.type = 9U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-name", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.name = 9U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("event-reserved", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        event.reserved = 1U;
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_EVENT_INVALID;
    });
    harness.Run("add-handle-replay", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !Add(
                &instance,
                1ULL,
                0ULL,
                JARVIS_TAP_VISUAL_TYPE_OTHER)) {
            return false;
        }
        const auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_HANDLE_REPLAY;
    });
    harness.Run("remove-unknown", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        const auto event = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER,
            JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER,
            JARVIS_TAP_VISUAL_MUTATION_REMOVE);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_REMOVE_UNKNOWN;
    });
    harness.Run("remove-twice", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !Add(
                &instance,
                1ULL,
                0ULL,
                JARVIS_TAP_VISUAL_TYPE_OTHER)) {
            return false;
        }
        auto remove = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER,
            JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER,
            JARVIS_TAP_VISUAL_MUTATION_REMOVE);
        if (jarvis_tap_surface_discovery_ingest(
                &instance,
                &remove)
                .result !=
            JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED) {
            return false;
        }
        remove.sequence = instance.next_sequence;
        return IngestInvalid(&instance, remove) ==
               JARVIS_TAP_DISCOVERY_RESULT_REMOVE_UNKNOWN;
    });
    harness.Run("removed-handle-cannot-readd", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !Add(
                &instance,
                1ULL,
                0ULL,
                JARVIS_TAP_VISUAL_TYPE_OTHER)) {
            return false;
        }
        auto remove = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER,
            JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER,
            JARVIS_TAP_VISUAL_MUTATION_REMOVE);
        if (jarvis_tap_surface_discovery_ingest(
                &instance,
                &remove)
                .result !=
            JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED) {
            return false;
        }
        const auto add = MakeEvent(
            instance,
            1ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, add) ==
               JARVIS_TAP_DISCOVERY_RESULT_HANDLE_REPLAY;
    });
    harness.Run("node-capacity", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        for (std::uint64_t handle = 1ULL;
             handle <= JARVIS_TAP_DISCOVERY_MAX_NODE_COUNT;
             ++handle) {
            if (!Add(
                    &instance,
                    handle,
                    0ULL,
                    JARVIS_TAP_VISUAL_TYPE_OTHER)) {
                return false;
            }
        }
        const auto event = MakeEvent(
            instance,
            1000ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_NODE_CAPACITY_EXCEEDED;
    });
    harness.Run("finalize-null", [] {
        return jarvis_tap_surface_discovery_finalize(nullptr).result ==
               JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("finalize-empty", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("finalize-orphan", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               Add(
                   &instance,
                   1ULL,
                   999ULL,
                   JARVIS_TAP_VISUAL_TYPE_OTHER) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_ORPHAN;
    });
    harness.Run("finalize-cycle", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               Add(
                   &instance,
                   1ULL,
                   2ULL,
                   JARVIS_TAP_VISUAL_TYPE_OTHER) &&
               Add(
                   &instance,
                   2ULL,
                   1ULL,
                   JARVIS_TAP_VISUAL_TYPE_OTHER) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_CYCLE;
    });
    harness.Run("finalize-depth", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        for (std::uint64_t handle = 1ULL;
             handle <=
                 static_cast<std::uint64_t>(
                     JARVIS_TAP_DISCOVERY_MAX_DEPTH) +
                     2ULL;
             ++handle) {
            if (!Add(
                    &instance,
                    handle,
                    handle == 1ULL ? 0ULL : handle - 1ULL,
                    JARVIS_TAP_VISUAL_TYPE_OTHER)) {
                return false;
            }
        }
        return jarvis_tap_surface_discovery_finalize(&instance).result ==
               JARVIS_TAP_DISCOVERY_RESULT_DEPTH_EXCEEDED;
    });
    harness.Run("tab-missing", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true)) {
            return false;
        }
        auto remove = MakeEvent(
            instance,
            12ULL,
            11ULL,
            JARVIS_TAP_VISUAL_TYPE_GRID,
            JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID,
            JARVIS_TAP_VISUAL_MUTATION_REMOVE);
        return jarvis_tap_surface_discovery_ingest(
                   &instance,
                   &remove)
                   .result ==
                   JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("tab-duplicate", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               Add(
                   &instance,
                   13ULL,
                   11ULL,
                   JARVIS_TAP_VISUAL_TYPE_GRID,
                   JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("command-duplicate", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               Add(
                   &instance,
                   23ULL,
                   21ULL,
                   JARVIS_TAP_VISUAL_TYPE_GRID,
                   JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("navigation-duplicate", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               Add(
                   &instance,
                   31ULL,
                   1ULL,
                   JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("tab-wrong-name", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               Add(
                   &instance,
                   1ULL,
                   0ULL,
                   JARVIS_TAP_VISUAL_TYPE_TAB_CONTROL) &&
               Add(
                   &instance,
                   2ULL,
                   1ULL,
                   JARVIS_TAP_VISUAL_TYPE_GRID,
                   JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("tab-wrong-ancestor", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               Add(
                   &instance,
                   1ULL,
                   0ULL,
                   JARVIS_TAP_VISUAL_TYPE_OTHER) &&
               Add(
                   &instance,
                   2ULL,
                   1ULL,
                   JARVIS_TAP_VISUAL_TYPE_GRID,
                   JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_SURFACE_NOT_UNIQUE;
    });
    harness.Run("remove-parent-leaves-orphan", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true)) {
            return false;
        }
        auto remove = MakeEvent(
            instance,
            11ULL,
            10ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER,
            JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER,
            JARVIS_TAP_VISUAL_MUTATION_REMOVE);
        return jarvis_tap_surface_discovery_ingest(
                   &instance,
                   &remove)
                   .result ==
                   JARVIS_TAP_DISCOVERY_RESULT_ACCEPTED &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_ORPHAN;
    });
    harness.Run("ingest-after-complete", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true) ||
            jarvis_tap_surface_discovery_finalize(&instance).result !=
                JARVIS_TAP_DISCOVERY_RESULT_COMPLETE) {
            return false;
        }
        const auto event = MakeEvent(
            instance,
            99ULL,
            0ULL,
            JARVIS_TAP_VISUAL_TYPE_OTHER);
        return IngestInvalid(&instance, event) ==
               JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID;
    });
    harness.Run("finalize-replay", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission) ||
            !AddValidTree(&instance, true) ||
            jarvis_tap_surface_discovery_finalize(&instance).result !=
                JARVIS_TAP_DISCOVERY_RESULT_COMPLETE) {
            return false;
        }
        return jarvis_tap_surface_discovery_finalize(&instance).result ==
               JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID;
    });
    harness.Run("read-before-complete", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        jarvis_tap_xaml_read_request request{};
        return Bind(&instance, &admission) &&
               jarvis_tap_surface_discovery_build_read_request(
                   &instance,
                   0U,
                   &request) ==
                   JARVIS_TAP_DISCOVERY_RESULT_STATE_INVALID;
    });
    harness.Run("read-slot-invalid", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        jarvis_tap_xaml_read_request request{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_COMPLETE &&
               jarvis_tap_surface_discovery_build_read_request(
                   &instance,
                   9U,
                   &request) ==
                   JARVIS_TAP_DISCOVERY_RESULT_SLOT_INVALID;
    });
    harness.Run("read-null-output", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        return Bind(&instance, &admission) &&
               AddValidTree(&instance, true) &&
               jarvis_tap_surface_discovery_finalize(&instance).result ==
                   JARVIS_TAP_DISCOVERY_RESULT_COMPLETE &&
               jarvis_tap_surface_discovery_build_read_request(
                   &instance,
                   0U,
                   nullptr) ==
                   JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("query-null", [] {
        const auto response =
            jarvis_tap_surface_discovery_query(nullptr);
        return response.state ==
                   JARVIS_TAP_DISCOVERY_STATE_BLOCKED &&
               response.result ==
                   JARVIS_TAP_DISCOVERY_RESULT_INVALID_ARGUMENT;
    });
    harness.Run("explicit-fail-closed", [] {
        auto admission = MakeAdmission();
        jarvis_tap_surface_discovery_instance instance{};
        if (!Bind(&instance, &admission)) {
            return false;
        }
        instance.surface_instance_handles[0] = 1ULL;
        jarvis_tap_surface_discovery_fail_closed(
            &instance,
            JARVIS_TAP_DISCOVERY_RESULT_CALLBACK_CONCURRENT);
        return instance.state ==
                   JARVIS_TAP_DISCOVERY_STATE_BLOCKED &&
               instance.last_result ==
                   JARVIS_TAP_DISCOVERY_RESULT_CALLBACK_CONCURRENT &&
               instance.surface_instance_handles[0] == 0ULL;
    });

    const bool passed =
        harness.scenario_count == 58U &&
        harness.passed_count == harness.scenario_count;
    std::cout
        << "{\n"
        << "  \"schemaVersion\": 1,\n"
        << "  \"receiptType\": "
           "\"jarvisv2-explorer-xaml-surface-discovery-harness\",\n"
        << "  \"result\": \"" << (passed ? "passed" : "failed")
        << "\",\n"
        << "  \"scenarioCount\": " << harness.scenario_count << ",\n"
        << "  \"passedCount\": " << harness.passed_count << ",\n"
        << "  \"syntheticVisualTreeEvents\": true,\n"
        << "  \"windowsCallbackExecuted\": false,\n"
        << "  \"callbackSubscriptionAttempted\": false,\n"
        << "  \"propertyReadAttempted\": false,\n"
        << "  \"propertyWriteSupported\": false,\n"
        << "  \"executionSupported\": false,\n"
        << "  \"readyForLiveConnection\": false,\n"
        << "  \"readyForExactApproval\": false,\n"
        << "  \"activationPermitted\": false,\n"
        << "  \"liveExplorer\": \"not-run\",\n"
        << "  \"mutationPerformed\": false\n"
        << "}\n";
    return passed ? 0 : 1;
}
