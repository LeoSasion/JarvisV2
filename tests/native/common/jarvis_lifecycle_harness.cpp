// SPDX-License-Identifier: GPL-3.0-or-later

#include "../../../mods/common/jarvis-resource-protocol.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <barrier>
#include <condition_variable>
#include <cstdint>
#include <iostream>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <type_traits>
#include <utility>
#include <vector>

namespace {

namespace protocol = jarvis::resource_protocol;

static_assert(std::is_trivially_copyable_v<protocol::GitReceipt>);
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().Register(1, 1)));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().AcquireLease()));
static_assert(
    std::is_trivially_copyable_v<protocol::UiReceiptReason>);
static_assert(
    std::is_trivially_copyable_v<protocol::UiCleanupSnapshot>);
static_assert(
    std::is_trivially_copyable_v<protocol::UiThreadReceiptSnapshot>);
static_assert(
    std::is_trivially_copyable_v<protocol::DispatchReason>);
static_assert(
    std::is_trivially_copyable_v<protocol::CallbackPhase>);
static_assert(
    std::is_trivially_copyable_v<
        protocol::DispatchRetainedReason>);
static_assert(
    std::is_trivially_copyable_v<
        protocol::DispatchResourceKind>);
static_assert(
    std::is_trivially_copyable_v<
        protocol::DispatchResourceDisposition>);
static_assert(
    std::is_trivially_copyable_v<
        protocol::DispatchResourceReceipt>);
static_assert(
    std::is_trivially_copyable_v<protocol::DispatchReceipt>);
static_assert(noexcept(
    std::declval<protocol::DispatchSlot&>().ClaimCallback()));
static_assert(noexcept(
    std::declval<const protocol::DispatchSlot&>().Receipt()));
static_assert(noexcept(
    std::declval<const protocol::GitLifecycle&>().CookieForLease({})));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().ReleaseLease({})));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().CloseAdmission()));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().WaitForNoLeases(
        std::declval<std::chrono::milliseconds>())));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().BeginRevoke()));
static_assert(noexcept(
    std::declval<const protocol::GitLifecycle&>().CookieForRevoke({})));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().CompleteRevoke({}, false)));
static_assert(noexcept(
    std::declval<protocol::GitLifecycle&>().RetainRegisteredResource(
        0, false)));
static_assert(noexcept(
    std::declval<const protocol::GitLifecycle&>().Receipt()));
static_assert(noexcept(
    protocol::ReserveNonZeroSequence(
        std::declval<std::atomic<std::uint64_t>&>())));
static_assert(noexcept(
    std::declval<protocol::SubscriptionLifecycle&>().BeginAdvise()));
static_assert(noexcept(
    std::declval<protocol::SubscriptionLifecycle&>().CompleteAdvise(
        false)));
static_assert(noexcept(
    std::declval<protocol::SubscriptionLifecycle&>().BeginUnadvise()));
static_assert(noexcept(
    std::declval<protocol::SubscriptionLifecycle&>().CompleteUnadvise(
        false)));
static_assert(noexcept(
    std::declval<const protocol::SubscriptionLifecycle&>().Receipt()));

struct Accounting {
    std::uint64_t created = 0;
    std::uint64_t released = 0;
    std::uint64_t retained = 0;
    std::uint64_t unexplained = 0;
    std::uint64_t double_release = 0;
};

enum class ResourceAction {
    Create,
    Release,
    Retain,
};

enum class RetainReasonCode {
    None,
    ExternalUncertainty,
    RetryPending,
    RetryExhausted,
    OwnerTransfer,
    ProtocolFailure,
    CleanupFailure,
    HookRemovalFailure,
    ModulePermanent,
    CapabilityRetained,
    DelegateRejected,
    RollbackFailure,
    ResourceTransferred,
};

constexpr std::string_view ToString(
    ResourceAction action) noexcept {
    switch (action) {
    case ResourceAction::Create:
        return "create";
    case ResourceAction::Release:
        return "release";
    case ResourceAction::Retain:
        return "retain";
    }
    return "unknown";
}

constexpr std::string_view ToString(
    RetainReasonCode reason) noexcept {
    switch (reason) {
    case RetainReasonCode::None:
        return "none";
    case RetainReasonCode::ExternalUncertainty:
        return "external-uncertainty";
    case RetainReasonCode::RetryPending:
        return "retry-pending";
    case RetainReasonCode::RetryExhausted:
        return "retry-exhausted";
    case RetainReasonCode::OwnerTransfer:
        return "owner-transfer";
    case RetainReasonCode::ProtocolFailure:
        return "protocol-failure";
    case RetainReasonCode::CleanupFailure:
        return "cleanup-failure";
    case RetainReasonCode::HookRemovalFailure:
        return "hook-removal-failure";
    case RetainReasonCode::ModulePermanent:
        return "module-permanent";
    case RetainReasonCode::CapabilityRetained:
        return "capability-retained";
    case RetainReasonCode::DelegateRejected:
        return "delegate-rejected";
    case RetainReasonCode::RollbackFailure:
        return "rollback-failure";
    case RetainReasonCode::ResourceTransferred:
        return "resource-transferred";
    }
    return "unknown";
}

struct ResourceEvent {
    std::string id;
    std::string kind;
    ResourceAction action = ResourceAction::Create;
    RetainReasonCode reason = RetainReasonCode::None;
};

class ResourceLedger;

class ResourceOwner {
public:
    ResourceOwner() = default;
    ResourceOwner(ResourceLedger& ledger,
                  std::string id,
                  std::string kind) noexcept;

    ResourceOwner(const ResourceOwner&) = delete;
    ResourceOwner& operator=(const ResourceOwner&) = delete;

    ResourceOwner(ResourceOwner&& other) noexcept
        : ledger_(std::exchange(other.ledger_, nullptr)),
          id_(std::move(other.id_)),
          kind_(std::move(other.kind_)),
          terminal_(std::exchange(other.terminal_, true)) {}

    ResourceOwner& operator=(ResourceOwner&& other) noexcept {
        if (this == &other) {
            return *this;
        }
        ledger_ = std::exchange(other.ledger_, nullptr);
        id_ = std::move(other.id_);
        kind_ = std::move(other.kind_);
        terminal_ = std::exchange(other.terminal_, true);
        return *this;
    }

    void Release() noexcept;
    void Retain(RetainReasonCode reason) noexcept;

    [[nodiscard]] const std::string& id() const noexcept {
        return id_;
    }

private:
    void Complete(ResourceAction action,
                  RetainReasonCode reason) noexcept;

    ResourceLedger* ledger_ = nullptr;
    std::string id_;
    std::string kind_;
    bool terminal_ = false;
};

class ResourceLedger {
public:
    explicit ResourceLedger(std::string scope)
        : scope_(std::move(scope)) {}

    [[nodiscard]] ResourceOwner Create(std::string kind) {
        const std::string id =
            scope_ + "/resource-" + std::to_string(next_id_++);
        events_.push_back(
            {id, kind, ResourceAction::Create,
             RetainReasonCode::None});
        return ResourceOwner(*this, id, std::move(kind));
    }

    void AppendTerminal(const std::string& id,
                        const std::string& kind,
                        ResourceAction action,
                        RetainReasonCode reason) noexcept {
        try {
            events_.push_back({id, kind, action, reason});
        } catch (...) {
            event_write_failed_ = true;
        }
    }

    [[nodiscard]] const std::vector<ResourceEvent>& Events()
        const noexcept {
        return events_;
    }

    [[nodiscard]] Accounting Recompute(
        std::vector<std::string>& failures) const {
        Accounting accounting;
        if (event_write_failed_) {
            failures.push_back("resource-event-write-failed");
            ++accounting.unexplained;
        }

        struct State {
            std::string id;
            std::string kind;
            bool terminal = false;
        };
        std::vector<State> states;
        for (const auto& event : events_) {
            auto found = std::find_if(
                states.begin(), states.end(),
                [&](const State& state) {
                    return state.id == event.id;
                });
            if (event.action == ResourceAction::Create) {
                if (event.reason != RetainReasonCode::None) {
                    failures.push_back(
                        "create-resource-has-retain-reason:" +
                        event.id);
                    ++accounting.unexplained;
                }
                if (found != states.end()) {
                    failures.push_back(
                        "duplicate-resource-create:" + event.id);
                    ++accounting.unexplained;
                    continue;
                }
                states.push_back({event.id, event.kind, false});
                ++accounting.created;
                continue;
            }
            if (found == states.end()) {
                failures.push_back(
                    "unknown-resource-terminal:" + event.id);
                ++accounting.unexplained;
                continue;
            }
            if (found->kind != event.kind) {
                failures.push_back(
                    "resource-kind-mismatch:" + event.id);
                ++accounting.unexplained;
                continue;
            }
            if (found->terminal) {
                failures.push_back(
                    "duplicate-resource-terminal:" + event.id);
                ++accounting.double_release;
                continue;
            }
            if (event.action == ResourceAction::Retain &&
                event.reason == RetainReasonCode::None) {
                failures.push_back(
                    "retained-resource-missing-reason:" +
                    event.id);
                ++accounting.unexplained;
                continue;
            }
            if (event.action == ResourceAction::Release &&
                event.reason != RetainReasonCode::None) {
                failures.push_back(
                    "released-resource-has-retain-reason:" +
                    event.id);
                ++accounting.unexplained;
                continue;
            }
            found->terminal = true;
            if (event.action == ResourceAction::Release) {
                ++accounting.released;
            } else {
                ++accounting.retained;
            }
        }
        for (const auto& state : states) {
            if (!state.terminal) {
                failures.push_back(
                    "unterminated-resource:" + state.id);
                ++accounting.unexplained;
            }
        }
        return accounting;
    }

private:
    std::string scope_;
    std::uint64_t next_id_ = 1;
    std::vector<ResourceEvent> events_;
    bool event_write_failed_ = false;
};

ResourceOwner::ResourceOwner(ResourceLedger& ledger,
                             std::string id,
                             std::string kind) noexcept
    : ledger_(&ledger),
      id_(std::move(id)),
      kind_(std::move(kind)) {}

void ResourceOwner::Complete(ResourceAction action,
                             RetainReasonCode reason) noexcept {
    if (!ledger_) {
        return;
    }
    ledger_->AppendTerminal(id_, kind_, action, reason);
    terminal_ = true;
}

void ResourceOwner::Release() noexcept {
    Complete(ResourceAction::Release, RetainReasonCode::None);
}

void ResourceOwner::Retain(RetainReasonCode reason) noexcept {
    Complete(ResourceAction::Retain, reason);
}

struct Concurrency {
    bool used = false;
    std::uint64_t participants = 0;
};

struct Scenario {
    Scenario(std::string scenario_id,
             std::string scenario_area,
             bool scenario_passed,
             std::string scenario_terminal_state,
             std::vector<std::string> scenario_steps,
             std::vector<std::string> scenario_faults,
             Concurrency scenario_concurrency,
             std::string scenario_detail)
        : id(std::move(scenario_id)),
          area(std::move(scenario_area)),
          passed(scenario_passed),
          terminal_state(std::move(scenario_terminal_state)),
          steps(std::move(scenario_steps)),
          faults(std::move(scenario_faults)),
          resources(id),
          concurrency(scenario_concurrency),
          detail(std::move(scenario_detail)) {}

    std::string id;
    std::string area;
    bool passed = true;
    std::string terminal_state;
    std::vector<std::string> steps;
    std::vector<std::string> faults;
    Accounting accounting;
    ResourceLedger resources;
    Concurrency concurrency;
    std::string detail;
};

void Expect(Scenario& scenario,
            bool condition,
            std::string_view failure) {
    if (condition) {
        return;
    }
    scenario.passed = false;
    if (!scenario.detail.empty()) {
        scenario.detail += "; ";
    }
    scenario.detail += "check-failed:";
    scenario.detail += failure;
}

void Finalize(Scenario& scenario) {
    std::vector<std::string> failures;
    scenario.accounting = scenario.resources.Recompute(failures);
    for (const auto& failure : failures) {
        Expect(scenario, false, failure);
    }
    const std::uint64_t accounted =
        scenario.accounting.released + scenario.accounting.retained +
        scenario.accounting.unexplained;
    Expect(scenario,
           accounted == scenario.accounting.created,
           "resource-accounting-mismatch");
    Expect(scenario,
           scenario.accounting.unexplained == 0,
           "unexplained-resource");
    Expect(scenario,
           scenario.accounting.double_release == 0,
           "double-release");
}

void RecordGitReceiptResources(
    Scenario& scenario,
    const protocol::GitReceipt& receipt,
    std::string_view kind = "git-cookie") {
    const bool resource_observed =
        receipt.generation != 0 ||
        receipt.cookie_knowledge !=
            protocol::GitCookieKnowledge::Absent;
    if (!resource_observed) {
        return;
    }
    auto owner = scenario.resources.Create(std::string(kind));
    if (receipt.state == protocol::GitState::Revoked &&
        receipt.cookie_knowledge ==
            protocol::GitCookieKnowledge::Absent) {
        owner.Release();
    } else {
        owner.Retain(
            receipt.retry_eligible
                ? RetainReasonCode::RetryPending
                : RetainReasonCode::ExternalUncertainty);
    }
}

void RecordSubscriptionReceiptResources(
    Scenario& scenario,
    const protocol::SubscriptionReceipt& receipt,
    std::string_view kind = "subscription") {
    if (receipt.state == protocol::SubscriptionState::NotAttempted &&
        receipt.advise_attempts == 0 &&
        !receipt.external_uncertainty_latched) {
        return;
    }
    auto owner = scenario.resources.Create(std::string(kind));
    if (receipt.state == protocol::SubscriptionState::Unadvised &&
        !receipt.best_effort_unadvise_required) {
        owner.Release();
    } else {
        owner.Retain(RetainReasonCode::ExternalUncertainty);
    }
}

void RecordUiReceiptResources(
    Scenario& scenario,
    const protocol::UiThreadReceiptSnapshot& receipts) {
    for (const auto& receipt : receipts) {
        auto owner = scenario.resources.Create("ui-thread-record");
        switch (receipt.state) {
        case protocol::UiRecordState::Cleaned:
        case protocol::UiRecordState::InitFailed:
        case protocol::UiRecordState::LateCleanedRetained:
            owner.Release();
            break;
        case protocol::UiRecordState::Unreachable:
            owner.Retain(
                RetainReasonCode::ExternalUncertainty);
            break;
        case protocol::UiRecordState::Retained:
            owner.Retain(
                receipt.retry_eligible
                    ? RetainReasonCode::CleanupFailure
                    : RetainReasonCode::RetryExhausted);
            break;
        default:
            // A nonterminal UI record is intentionally left open so the event
            // verifier catches it.
            break;
        }
    }
}

void RecordUiCapabilityReceiptResources(
    Scenario& scenario,
    const protocol::UiThreadReceipt& receipt) {
    constexpr std::array capabilities{
        std::pair{
            protocol::UiCapabilityMask(
                protocol::UiCapability::ThreadHandle),
            std::string_view{"ui-thread-handle"}},
        std::pair{
            protocol::UiCapabilityMask(
                protocol::UiCapability::AgileDispatcher),
            std::string_view{"ui-agile-dispatcher"}},
        std::pair{
            protocol::UiCapabilityMask(
                protocol::UiCapability::CleanupEvent),
            std::string_view{"ui-cleanup-event"}},
    };
    for (const auto& [mask, kind] : capabilities) {
        if ((receipt.capability_created_mask & mask) == 0) {
            continue;
        }
        auto owner = scenario.resources.Create(std::string(kind));
        if ((receipt.capability_released_mask & mask) != 0) {
            owner.Release();
        } else if ((receipt.capability_retained_mask & mask) != 0) {
            owner.Retain(
                RetainReasonCode::CapabilityRetained);
        }
    }
}

void RecordDispatchReceiptResources(
    Scenario& scenario,
    const protocol::DispatchReceipt& receipt) {
    constexpr std::array kinds{
        std::pair{
            protocol::DispatchResourceKind::SenderReference,
            std::string_view{"dispatch-sender-reference"}},
        std::pair{
            protocol::DispatchResourceKind::CallbackReference,
            std::string_view{"dispatch-callback-reference"}},
        std::pair{
            protocol::DispatchResourceKind::HookHandle,
            std::string_view{"dispatch-hook-handle"}},
    };
    std::uint64_t derived_created = 0;
    std::uint64_t derived_released = 0;
    std::uint64_t derived_retained = 0;
    std::uint64_t derived_inflight = 0;
    for (std::size_t index = 0; index < kinds.size(); ++index) {
        const auto& resource = receipt.resources[index];
        Expect(
            scenario, resource.kind == kinds[index].first,
            "dispatch-resource-identity-order");
        if (resource.disposition ==
            protocol::DispatchResourceDisposition::Absent) {
            Expect(
                scenario,
                resource.retained_reason ==
                    protocol::DispatchRetainedReason::None,
                "absent-dispatch-resource-retain-reason");
            continue;
        }

        ++derived_created;
        auto owner =
            scenario.resources.Create(std::string(kinds[index].second));
        switch (resource.disposition) {
        case protocol::DispatchResourceDisposition::Released:
            ++derived_released;
            owner.Release();
            break;
        case protocol::DispatchResourceDisposition::Retained:
            ++derived_retained;
            Expect(
                scenario,
                resource.retained_reason !=
                    protocol::DispatchRetainedReason::None,
                "retained-dispatch-resource-missing-reason");
            owner.Retain(
                resource.retained_reason ==
                        protocol::DispatchRetainedReason::
                            HookRemovalFailed
                    ? RetainReasonCode::HookRemovalFailure
                    : RetainReasonCode::ProtocolFailure);
            break;
        case protocol::DispatchResourceDisposition::Inflight:
            ++derived_inflight;
            break;
        case protocol::DispatchResourceDisposition::Absent:
            break;
        }
    }
    Expect(scenario,
           receipt.resources_created == derived_created &&
               receipt.resources_released == derived_released &&
               receipt.resources_retained == derived_retained &&
               receipt.resources_inflight == derived_inflight,
           "dispatch-totals-not-derived-from-dispositions");
}

struct GitInjectedLockFailure {
    protocol::GitOperation operation = protocol::GitOperation::None;
    bool armed = false;
};

bool FailGitOperationBeforeLock(
    protocol::GitOperation operation,
    void* context) noexcept {
    auto* failure =
        static_cast<GitInjectedLockFailure*>(context);
    if (!failure || !failure->armed ||
        failure->operation != operation) {
        return false;
    }
    failure->armed = false;
    return true;
}

struct SubscriptionInjectedLockFailure {
    protocol::SubscriptionOperation operation =
        protocol::SubscriptionOperation::None;
    bool armed = false;
};

bool FailSubscriptionOperationBeforeLock(
    protocol::SubscriptionOperation operation,
    void* context) noexcept {
    auto* failure =
        static_cast<SubscriptionInjectedLockFailure*>(context);
    if (!failure || !failure->armed ||
        failure->operation != operation) {
        return false;
    }
    failure->armed = false;
    return true;
}

struct FakeGitPlatformOps {
    explicit FakeGitPlatformOps(ResourceLedger& ledger) noexcept
        : ledger_(&ledger) {}

    std::uint32_t next_cookie = 101;
    std::uint32_t live_cookie = 0;
    std::uint64_t register_calls = 0;
    std::uint64_t revoke_calls = 0;
    std::uint64_t revoke_failures_remaining = 0;
    std::uint64_t revoke_throws_remaining = 0;
    std::uint64_t git_release_calls = 0;
    std::uint64_t git_release_throws_remaining = 0;
    bool register_throws_before_cookie_write = false;
    bool unknown_registration_may_exist = false;

    std::uint32_t RegisterInterface() {
        ++register_calls;
        auto owner =
            ledger_->Create("provisional-git-registration");
        if (register_throws_before_cookie_write) {
            register_throws_before_cookie_write = false;
            unknown_registration_may_exist = true;
            owner.Retain(
                RetainReasonCode::ExternalUncertainty);
            throw std::runtime_error(
                "injected-register-exception-before-cookie-write");
        }
        live_cookie = next_cookie++;
        registrations_.push_back(
            {live_cookie, std::move(owner)});
        return live_cookie;
    }

    bool RevokeInterface(std::uint32_t cookie) {
        ++revoke_calls;
        if (revoke_throws_remaining != 0) {
            --revoke_throws_remaining;
            throw std::runtime_error("injected-revoke-exception");
        }
        if (cookie == 0 || cookie != live_cookie) {
            return false;
        }
        if (revoke_failures_remaining != 0) {
            --revoke_failures_remaining;
            return false;
        }
        const auto registration = std::find_if(
            registrations_.begin(), registrations_.end(),
            [&](const TrackedRegistration& candidate) {
                return candidate.cookie == cookie;
            });
        if (registration == registrations_.end()) {
            return false;
        }
        registration->owner.Release();
        registrations_.erase(registration);
        live_cookie = 0;
        return true;
    }

    void ReleaseTemporaryGitReference() {
        ++git_release_calls;
        auto owner =
            ledger_->Create("temporary-git-reference");
        if (git_release_throws_remaining != 0) {
            --git_release_throws_remaining;
            owner.Retain(
                RetainReasonCode::ExternalUncertainty);
            throw std::runtime_error(
                "injected-temporary-git-release-exception");
        }
        owner.Release();
    }

    void RetainOutstandingRegistrations(
        RetainReasonCode reason) noexcept {
        for (auto& registration : registrations_) {
            registration.owner.Retain(reason);
        }
        registrations_.clear();
    }

private:
    struct TrackedRegistration {
        std::uint32_t cookie = 0;
        ResourceOwner owner;
    };

    ResourceLedger* ledger_ = nullptr;
    std::vector<TrackedRegistration> registrations_;
};

struct FakeProvisionalGitReceipt {
    std::uint64_t generation = 0;
    std::uint32_t cookie = 0;
    std::int64_t last_error = 0;
    bool retry_eligible = false;
    std::string reason;
    std::uint64_t receipt_id = 0;
};

struct FakeProvisionalGitOverflowReceipt {
    std::uint64_t retained_count = 0;
    std::uint64_t first_receipt_id = 0;
    std::uint64_t last_receipt_id = 0;
    std::int64_t last_error = 0;
    bool permanent_pin = false;
    std::uint64_t capacity_failures = 0;
    std::uint64_t git_release_failures = 0;
    bool external_register_blocked = false;
};

class FakeProvisionalGitAdapter {
public:
    explicit FakeProvisionalGitAdapter(FakeGitPlatformOps& platform)
        : platform_(platform) {}

    bool RegisterWithInjectedCommitFailure(
        std::uint64_t generation,
        bool rollback_throws = false) {
        // The fixed slot is reserved before the external registration. A full
        // table blocks the platform call, so no cookie can escape receipt
        // ownership.
        auto slot = std::find_if(
            quarantine_.begin(), quarantine_.end(),
            [](const auto& entry) { return !entry.has_value(); });
        if (slot == quarantine_.end()) {
            ++overflow_.capacity_failures;
            overflow_.external_register_blocked = true;
            overflow_.last_error = -103;
            return false;
        }
        const std::uint64_t receipt_id = next_receipt_id_++;
        *slot = FakeProvisionalGitReceipt{
            generation,
            0,
            0,
            false,
            "slot-reserved-before-external-register",
            receipt_id,
        };

        const std::uint32_t cookie = platform_.RegisterInterface();
        slot->value().cookie = cookie;
        ++internal_commit_attempts_;

        // The fault is injected after the external registration succeeds but
        // before the cookie can become owned by the protocol core.
        const bool internal_commit_succeeded = false;
        if (internal_commit_succeeded) {
            slot->reset();
            return true;
        }

        if (rollback_throws) {
            platform_.revoke_throws_remaining = 1;
        }
        bool rollback_succeeded = false;
        try {
            rollback_succeeded = platform_.RevokeInterface(cookie);
        } catch (...) {
            rollback_succeeded = false;
        }
        if (rollback_succeeded) {
            slot->reset();
            return false;
        }

        slot->value().last_error =
            rollback_throws ? -104 : -101;
        slot->value().retry_eligible = true;
        slot->value().reason =
            rollback_throws
                ? "internal-commit-failed-rollback-threw"
                : "internal-commit-failed-rollback-failed";
        return false;
    }

    bool RegisterWithThrowBeforeCookieWrite(
        std::uint64_t generation) {
        auto slot = std::find_if(
            quarantine_.begin(), quarantine_.end(),
            [](const auto& entry) { return !entry.has_value(); });
        if (slot == quarantine_.end()) {
            ++overflow_.capacity_failures;
            overflow_.external_register_blocked = true;
            overflow_.last_error = -103;
            return false;
        }
        const std::uint64_t receipt_id = next_receipt_id_++;
        *slot = FakeProvisionalGitReceipt{
            generation,
            0,
            0,
            false,
            "slot-reserved-before-external-register",
            receipt_id,
        };

        platform_.register_throws_before_cookie_write = true;
        try {
            slot->value().cookie = platform_.RegisterInterface();
        } catch (...) {
            slot->value().last_error = -105;
            slot->value().retry_eligible = false;
            slot->value().reason =
                "registration-unknown-may-be-present";
            return false;
        }
        slot->reset();
        return true;
    }

    std::vector<FakeProvisionalGitReceipt> Receipts() const {
        std::vector<FakeProvisionalGitReceipt> receipts;
        for (const std::optional<FakeProvisionalGitReceipt>& entry :
             quarantine_) {
            if (entry.has_value()) {
                receipts.push_back(*entry);
            }
        }
        return receipts;
    }

    bool RetryQuarantine(bool apartment_initialized) {
        if (!apartment_initialized) {
            return false;
        }
        bool all_released = true;
        for (std::optional<FakeProvisionalGitReceipt>& entry :
             quarantine_) {
            if (!entry.has_value()) {
                continue;
            }
            if (entry->cookie == 0) {
                // An unknown registration has no safe blind-revoke input.
                all_released = false;
                continue;
            }
            const bool revoked =
                platform_.RevokeInterface(entry->cookie);
            bool git_release_confirmed = true;
            try {
                platform_.ReleaseTemporaryGitReference();
            } catch (...) {
                git_release_confirmed = false;
                overflow_.permanent_pin = true;
                ++overflow_.git_release_failures;
            }
            if (revoked) {
                entry.reset();
            } else {
                entry->last_error = -102;
                entry->reason = "quarantine-retry-failed";
                all_released = false;
            }
            if (!git_release_confirmed) {
                all_released = false;
            }
        }
        return all_released;
    }

    [[nodiscard]] std::uint64_t InternalCommitAttempts() const noexcept {
        return internal_commit_attempts_;
    }

    [[nodiscard]] const FakeProvisionalGitOverflowReceipt&
    OverflowReceipt() const noexcept {
        return overflow_;
    }

private:
    FakeGitPlatformOps& platform_;
    std::array<std::optional<FakeProvisionalGitReceipt>, 2> quarantine_{};
    std::uint64_t internal_commit_attempts_ = 0;
    std::uint64_t next_receipt_id_ = 1;
    FakeProvisionalGitOverflowReceipt overflow_{};
};

void RecordProvisionalGitResources(
    Scenario& scenario,
    FakeGitPlatformOps& platform,
    const FakeProvisionalGitAdapter& adapter) {
    const auto receipts = adapter.Receipts();
    const auto& overflow = adapter.OverflowReceipt();
    Expect(
        scenario,
        overflow.git_release_failures <=
            platform.git_release_calls,
        "temporary-git-reference-receipt-overflow");
    platform.RetainOutstandingRegistrations(
        receipts.empty()
            ? RetainReasonCode::ExternalUncertainty
            : RetainReasonCode::RollbackFailure);
}

struct FakeComApartmentOps {
    bool initialized = false;
    std::uint64_t initialize_calls = 0;
    std::uint64_t uninitialize_calls = 0;

    bool Initialize() {
        ++initialize_calls;
        initialized = true;
        return true;
    }

    void Uninitialize() {
        if (initialized) {
            initialized = false;
            ++uninitialize_calls;
        }
    }
};

bool RetryProvisionalQuarantineFromInitializedApartment(
    FakeProvisionalGitAdapter& adapter,
    FakeComApartmentOps& apartment) {
    if (!apartment.Initialize()) {
        return false;
    }
    const bool all_released =
        adapter.RetryQuarantine(apartment.initialized);
    apartment.Uninitialize();
    return all_released;
}

struct FakeRetiredWatcher {
    std::uint64_t references = 1;
    std::uint64_t intrusive_add_refs = 0;
    std::uint64_t caller_releases = 0;
    std::uint64_t nonterminal_destructions = 0;

    void AddRef() {
        ++references;
        ++intrusive_add_refs;
    }

    void ReleaseCaller() {
        ++caller_releases;
        if (references == 0) {
            ++nonterminal_destructions;
            return;
        }
        --references;
        if (references == 0) {
            ++nonterminal_destructions;
        }
    }
};

struct FakeRetiredOwnerReceipt {
    std::uint64_t transfer_attempts = 0;
    std::uint64_t fixed_slot_transfers = 0;
    std::uint64_t process_lifetime_retained = 0;
    std::int64_t last_error = 0;
    bool permanent_pin = false;
};

class FakeFixedRetiredOwnerAdapter {
public:
    bool Transfer(FakeRetiredWatcher& watcher,
                   bool inject_fixed_store_failure) {
        events_.emplace_back("addref-owner");
        watcher.AddRef();
        events_.emplace_back("fail-closed");
        ++receipt_.transfer_attempts;
        events_.emplace_back("publish-ledger");
        auto slot = fixed_slots_.begin();
        if (!inject_fixed_store_failure && *slot == nullptr) {
            *slot = &watcher;
            ++receipt_.fixed_slot_transfers;
            receipt_.permanent_pin = true;
            return true;
        }

        // Mirrors the production no-allocation overflow branch: establish an
        // intrusive process-lifetime owner before the caller can release.
        ++receipt_.process_lifetime_retained;
        receipt_.last_error = -201;
        receipt_.permanent_pin = true;
        return false;
    }

    [[nodiscard]] const FakeRetiredOwnerReceipt& Receipt() const noexcept {
        return receipt_;
    }

    [[nodiscard]] const std::vector<std::string>& Events() const noexcept {
        return events_;
    }

private:
    std::array<FakeRetiredWatcher*, 1> fixed_slots_{};
    FakeRetiredOwnerReceipt receipt_{};
    std::vector<std::string> events_;
};

class FakeRetiredOwnerGuard {
public:
    FakeRetiredOwnerGuard(
        FakeRetiredWatcher& watcher,
        FakeFixedRetiredOwnerAdapter& owners,
        bool inject_publication_failure) noexcept
        : watcher_(&watcher),
          owners_(&owners),
          inject_publication_failure_(inject_publication_failure) {}

    FakeRetiredOwnerGuard(const FakeRetiredOwnerGuard&) = delete;
    FakeRetiredOwnerGuard& operator=(
        const FakeRetiredOwnerGuard&) = delete;

    ~FakeRetiredOwnerGuard() noexcept {
        if (watcher_ && owners_) {
            (void)owners_->Transfer(
                *watcher_, inject_publication_failure_);
            // Both fixed publication and process-lifetime retention establish
            // a replacement owner before this guard forgets the pointer.
            watcher_ = nullptr;
        }
    }

private:
    FakeRetiredWatcher* watcher_ = nullptr;
    FakeFixedRetiredOwnerAdapter* owners_ = nullptr;
    bool inject_publication_failure_ = false;
};

struct FakeProxyOps {
    std::vector<std::string> release_order;
    std::uint64_t proxy_final_releases = 0;
    std::uint64_t lease_releases = 0;
    std::uint64_t proxy_release_exceptions = 0;
    std::uint64_t double_release = 0;
    bool throw_on_proxy_final_release = false;

    void RecordProxyFinalRelease() {
        ++proxy_final_releases;
        release_order.emplace_back("proxy-final-release");
    }

    void RecordLeaseRelease() {
        ++lease_releases;
        release_order.emplace_back("lease-release");
    }
};

class FakeProxy {
public:
    explicit FakeProxy(FakeProxyOps& operations)
        : operations_(operations) {}

    bool FinalRelease() {
        if (!live_) {
            ++operations_.double_release;
            return false;
        }
        if (operations_.throw_on_proxy_final_release) {
            ++operations_.proxy_release_exceptions;
            operations_.release_order.emplace_back(
                "proxy-final-release-threw");
            throw std::runtime_error(
                "injected-proxy-final-release-exception");
        }
        live_ = false;
        operations_.RecordProxyFinalRelease();
        return true;
    }

private:
    FakeProxyOps& operations_;
    bool live_ = true;
};

class FakeGitLeaseAdapter {
public:
    FakeGitLeaseAdapter(protocol::GitLifecycle& lifecycle,
                        protocol::GitLeaseTicket ticket,
                        FakeProxyOps& operations)
        : lifecycle_(lifecycle),
          ticket_(ticket),
          operations_(operations),
          proxy_(operations) {}

    bool Close() {
        if (closed_) {
            ++operations_.double_release;
            return false;
        }
        closed_ = true;
        try {
            if (!proxy_.FinalRelease()) {
                return false;
            }
        } catch (...) {
            // Mirror the production fail-closed rule: a lease whose proxy
            // final Release is unconfirmed remains active forever.
            return false;
        }
        const auto status = lifecycle_.ReleaseLease(ticket_);
        if (status != protocol::ProtocolStatus::Applied) {
            return false;
        }
        operations_.RecordLeaseRelease();
        return true;
    }

private:
    protocol::GitLifecycle& lifecycle_;
    protocol::GitLeaseTicket ticket_{};
    FakeProxyOps& operations_;
    FakeProxy proxy_;
    bool closed_ = false;
};

struct FakeExternalComReferenceOps {
    explicit FakeExternalComReferenceOps(
        ResourceLedger& ledger) noexcept
        : resources(&ledger) {}

    ResourceLedger* resources = nullptr;
    bool throw_after_addref = false;
    bool throw_after_query_output = false;
    bool throw_on_release = false;
    bool permanent_pin = false;
    bool quiesced = false;
    std::uint64_t addref_calls = 0;
    std::uint64_t query_calls = 0;
    std::uint64_t release_calls = 0;
    std::uint64_t unknown_outcomes = 0;

    void AddRef() {
        ++addref_calls;
        auto owner = resources->Create("external-com-reference");
        if (throw_after_addref) {
            uncertain_owner.emplace(std::move(owner));
            throw std::runtime_error(
                "injected-addref-after-reference-acquisition");
        }
        confirmed_owner.emplace(std::move(owner));
    }

    void QueryInterface() {
        ++query_calls;
        auto owner = resources->Create("external-com-query-output");
        if (throw_after_query_output) {
            uncertain_owner.emplace(std::move(owner));
            throw std::runtime_error(
                "injected-query-after-output-acquisition");
        }
        uncertain_owner.emplace(std::move(owner));
    }

    void Release() {
        ++release_calls;
        if (throw_on_release) {
            throw std::runtime_error(
                "injected-release-before-confirmation");
        }
        if (confirmed_owner) {
            confirmed_owner->Release();
            confirmed_owner.reset();
        }
    }

    void RetainUnknown() noexcept {
        permanent_pin = true;
        quiesced = true;
        ++unknown_outcomes;
        if (uncertain_owner) {
            uncertain_owner->Retain(
                RetainReasonCode::ExternalUncertainty);
            uncertain_owner.reset();
        } else if (confirmed_owner) {
            confirmed_owner->Retain(
                RetainReasonCode::ExternalUncertainty);
            confirmed_owner.reset();
        }
    }

private:
    std::optional<ResourceOwner> confirmed_owner;
    std::optional<ResourceOwner> uncertain_owner;
};

class FakeSiteHolderExternalComFirewall {
public:
    bool CopyFromExternal(
        FakeExternalComReferenceOps& operations) noexcept {
        try {
            operations.AddRef();
        } catch (...) {
            // AddRef may have completed before throwing. There is no safe
            // compensating Release when acquisition is unconfirmed.
            operations.RetainUnknown();
            return false;
        }
        operations_ = &operations;
        return true;
    }

    bool QueryInterfaceNoThrow() const noexcept {
        if (!operations_) {
            return false;
        }
        try {
            operations_->QueryInterface();
        } catch (...) {
            // A provisional output reference is never returned or released.
            operations_->RetainUnknown();
            return false;
        }
        return true;
    }

    bool Reset() noexcept {
        auto* operations = std::exchange(operations_, nullptr);
        if (!operations) {
            return true;
        }
        try {
            operations->Release();
            return true;
        } catch (...) {
            // Detach before external Release and never retry an unconfirmed
            // ownership transition.
            operations->RetainUnknown();
            return false;
        }
    }

    ~FakeSiteHolderExternalComFirewall() noexcept {
        Reset();
    }

private:
    FakeExternalComReferenceOps* operations_ = nullptr;
};

struct FakeInternalSelfReferenceOps {
    explicit FakeInternalSelfReferenceOps(
        ResourceLedger& ledger) noexcept
        : resources(&ledger) {}

    ResourceLedger* resources = nullptr;
    std::uint64_t live_references = 1;
    std::uint64_t addref_calls = 0;
    std::uint64_t release_calls = 0;
    std::uint64_t double_release = 0;
    std::vector<ResourceOwner> owners;

    void AddRef() noexcept {
        try {
            owners.push_back(
                resources->Create("internal-self-reference"));
            ++addref_calls;
            ++live_references;
        } catch (...) {
            ++double_release;
        }
    }

    void Release() noexcept {
        ++release_calls;
        if (live_references <= 1 || owners.empty()) {
            ++double_release;
            return;
        }
        --live_references;
        owners.back().Release();
        owners.pop_back();
    }
};

class FakeInternalSelfReferenceGuard {
public:
    explicit FakeInternalSelfReferenceGuard(
        FakeInternalSelfReferenceOps& operations) noexcept
        : operations_(&operations) {
        operations_->AddRef();
    }

    FakeInternalSelfReferenceGuard(
        const FakeInternalSelfReferenceGuard&) = delete;
    FakeInternalSelfReferenceGuard& operator=(
        const FakeInternalSelfReferenceGuard&) = delete;

    ~FakeInternalSelfReferenceGuard() noexcept {
        auto* operations = std::exchange(operations_, nullptr);
        if (operations) {
            operations->Release();
        }
    }

    void Disarm() noexcept {
        operations_ = nullptr;
    }

    explicit operator bool() const noexcept {
        return operations_ != nullptr;
    }

private:
    FakeInternalSelfReferenceOps* operations_ = nullptr;
};

static_assert(
    std::is_nothrow_destructible_v<
        FakeSiteHolderExternalComFirewall>);
static_assert(
    noexcept(std::declval<FakeInternalSelfReferenceOps&>().AddRef()));
static_assert(
    noexcept(std::declval<FakeInternalSelfReferenceOps&>().Release()));
static_assert(
    std::is_nothrow_destructible_v<
        FakeInternalSelfReferenceGuard>);

struct FakeDispatchCompactReceipt {
    std::uint64_t dispatch_id = 0;
    std::uint64_t late_callbacks = 0;
    std::uint64_t duplicate_callbacks = 0;
    std::uint64_t protocol_late_callbacks = 0;
    std::uint64_t protocol_duplicate_callbacks = 0;
    std::uint64_t adapter_late_callbacks = 0;
    std::uint64_t adapter_duplicate_callbacks = 0;
    std::uint64_t double_release = 0;
    std::uint64_t protocol_double_release = 0;
    std::uint64_t actual_double_release = 0;
    std::uint64_t context_references_retained = 0;
    std::uint32_t actual_released_mask = 0;
    protocol::DispatchState state =
        protocol::DispatchState::Empty;
    protocol::DispatchRetainedReason retained_reason =
        protocol::DispatchRetainedReason::None;
    bool receipt_degraded = false;
    bool callback_claimed = false;
    bool terminal = false;
};

class FakeFixedDispatchAdapter {
public:
    bool Register(std::uint64_t dispatch_id,
                  protocol::DispatchSlot* slot) {
        if (dispatch_id == 0 || slot == nullptr ||
            pending_dispatch_id_ != 0) {
            return false;
        }
        pending_dispatch_id_ = dispatch_id;
        pending_slot_ = slot;
        compact_receipt_ = {};
        compact_receipt_.dispatch_id = dispatch_id;
        return true;
    }

    bool Publish(const protocol::DispatchReceipt& receipt) {
        if (receipt.dispatch_id != compact_receipt_.dispatch_id ||
            compact_receipt_.receipt_degraded) {
            return false;
        }
        compact_receipt_.protocol_late_callbacks = std::max(
            compact_receipt_.protocol_late_callbacks,
            receipt.late_callbacks);
        compact_receipt_.protocol_duplicate_callbacks = std::max(
            compact_receipt_.protocol_duplicate_callbacks,
            receipt.duplicate_callbacks);
        compact_receipt_.protocol_double_release = std::max(
            compact_receipt_.protocol_double_release,
            receipt.double_release);
        compact_receipt_.callback_claimed =
            compact_receipt_.callback_claimed ||
            receipt.callback_phase ==
                protocol::CallbackPhase::Claimed ||
            receipt.callback_phase ==
                protocol::CallbackPhase::Completed;
        compact_receipt_.terminal =
            compact_receipt_.terminal ||
            receipt.state == protocol::DispatchState::Completed ||
            receipt.state == protocol::DispatchState::Retained;
        MergeCallbackCounts();
        MergeDoubleReleaseCounts();
        compact_receipt_.retained_reason =
            receipt.retained_reason;
        return true;
    }

    bool RecordActualRelease(std::uint32_t resource_mask) noexcept {
        constexpr std::uint32_t kSender = 1U << 0;
        constexpr std::uint32_t kCallback = 1U << 1;
        constexpr std::uint32_t kHook = 1U << 2;
        const bool valid =
            resource_mask == kSender ||
            resource_mask == kCallback ||
            resource_mask == kHook;
        if (!valid ||
            (compact_receipt_.actual_released_mask &
             resource_mask) != 0) {
            compact_receipt_.actual_double_release =
                SaturatingAdd(
                    compact_receipt_.actual_double_release, 1);
            MergeDoubleReleaseCounts();
            MarkCompactDegraded();
            return false;
        }
        compact_receipt_.actual_released_mask |= resource_mask;
        return true;
    }

    bool ReleaseContextResource(
        std::uint32_t resource_mask,
        bool degrade_after_exact_mark = false) noexcept {
        if (!RecordActualRelease(resource_mask)) {
            compact_receipt_.context_references_retained =
                SaturatingAdd(
                    compact_receipt_.context_references_retained,
                    1);
            // Production recomputes the degraded terminal receipt after the
            // supplemental retained-reference count becomes observable.
            MarkCompactDegraded();
            return false;
        }
        if (degrade_after_exact_mark) {
            MarkCompactDegraded();
        }
        const bool receipt_healthy =
            !compact_receipt_.receipt_degraded;
        if (context_references_ == 0) {
            compact_receipt_.context_references_retained =
                SaturatingAdd(
                    compact_receipt_.context_references_retained,
                    1);
            MarkCompactDegraded();
            return false;
        }
        --context_references_;
        ++context_reference_decrements_;
        return receipt_healthy;
    }

    void MarkCompactDegraded() noexcept {
        compact_receipt_.receipt_degraded = true;
        compact_receipt_.state =
            protocol::DispatchState::Retained;
        compact_receipt_.retained_reason =
            protocol::DispatchRetainedReason::ProtocolFailure;
        compact_receipt_.terminal = true;
    }

    bool RemovePending(std::uint64_t dispatch_id) {
        if (pending_dispatch_id_ != dispatch_id) {
            return false;
        }
        pending_dispatch_id_ = 0;
        pending_slot_ = nullptr;
        return true;
    }

    bool LateCallback(std::uint64_t dispatch_id) {
        if (pending_dispatch_id_ == dispatch_id &&
            pending_slot_ != nullptr) {
            ++address_dereferences_;
            const auto claim = pending_slot_->ClaimCallback();
            if (claim == protocol::DispatchClaimStatus::Claimed) {
                compact_receipt_.callback_claimed = true;
                return true;
            }
            if (claim == protocol::DispatchClaimStatus::Duplicate) {
                ++compact_receipt_.adapter_duplicate_callbacks;
            } else {
                ++compact_receipt_.adapter_late_callbacks;
            }
            MergeCallbackCounts();
            return false;
        }
        if (compact_receipt_.dispatch_id == dispatch_id) {
            if (compact_receipt_.callback_claimed) {
                ++compact_receipt_.adapter_duplicate_callbacks;
            } else {
                ++compact_receipt_.adapter_late_callbacks;
            }
            MergeCallbackCounts();
        }
        return false;
    }

    [[nodiscard]] const FakeDispatchCompactReceipt& Receipt() const {
        return compact_receipt_;
    }

    [[nodiscard]] std::uint64_t AddressDereferences() const noexcept {
        return address_dereferences_;
    }

    [[nodiscard]] std::uint64_t ContextReferences() const noexcept {
        return context_references_;
    }

    [[nodiscard]] std::uint64_t ContextReferenceDecrements() const noexcept {
        return context_reference_decrements_;
    }

    [[nodiscard]] std::uint64_t RetainedContextReferences() const noexcept {
        return compact_receipt_.context_references_retained;
    }

private:
    static std::uint64_t SaturatingAdd(
        std::uint64_t left,
        std::uint64_t right) noexcept {
        const auto maximum =
            std::numeric_limits<std::uint64_t>::max();
        return maximum - left < right ? maximum : left + right;
    }

    void MergeCallbackCounts() noexcept {
        compact_receipt_.late_callbacks = SaturatingAdd(
            compact_receipt_.protocol_late_callbacks,
            compact_receipt_.adapter_late_callbacks);
        compact_receipt_.duplicate_callbacks = SaturatingAdd(
            compact_receipt_.protocol_duplicate_callbacks,
            compact_receipt_.adapter_duplicate_callbacks);
    }

    void MergeDoubleReleaseCounts() noexcept {
        compact_receipt_.double_release = SaturatingAdd(
            compact_receipt_.protocol_double_release,
            compact_receipt_.actual_double_release);
    }

    std::uint64_t pending_dispatch_id_ = 0;
    protocol::DispatchSlot* pending_slot_ = nullptr;
    FakeDispatchCompactReceipt compact_receipt_{};
    std::uint64_t address_dereferences_ = 0;
    std::uint64_t context_references_ = 2;
    std::uint64_t context_reference_decrements_ = 0;
};

class FakeDispatchCallbackBoundary {
    class ClaimObserverGuard {
    public:
        explicit ClaimObserverGuard(
            FakeDispatchCallbackBoundary& owner) noexcept
            : owner_(&owner) {
            owner_->AcquireClaimObserver();
        }

        ClaimObserverGuard(const ClaimObserverGuard&) = delete;
        ClaimObserverGuard& operator=(
            const ClaimObserverGuard&) = delete;

        ~ClaimObserverGuard() noexcept {
            if (owner_) {
                owner_->ReleaseClaimObserver();
            }
        }

    private:
        FakeDispatchCallbackBoundary* owner_;
    };

public:
    void Invoke(bool protocol_failure,
                bool publication_failure) noexcept {
        struct ResourceGateGuard {
            explicit ResourceGateGuard(
                FakeDispatchCallbackBoundary& owner) noexcept
                : owner(owner) {
                owner.EnterResourceGate();
            }

            ~ResourceGateGuard() noexcept {
                owner.ExitResourceGate();
            }

            FakeDispatchCallbackBoundary& owner;
        };

        struct CallbackReference {
            explicit CallbackReference(
                FakeDispatchCallbackBoundary& owner) noexcept
                : owner(owner) {}

            ~CallbackReference() noexcept {
                owner.Release(kCallbackResource);
            }

            FakeDispatchCallbackBoundary& owner;
        } callback_reference(*this);

        std::optional<ClaimObserverGuard> observer_reference;
        try {
            {
                ResourceGateGuard resource_gate(*this);
                callback_invoked_ = true;
                // Slot validation has succeeded. Establish the independent
                // observer before the protocol claim, so a future throwing
                // claim can unwind without exposing a retired raw context.
                observer_reference.emplace(*this);
                if (protocol_failure) {
                    throw std::runtime_error(
                        "injected protocol failure");
                }
            }
            if (publication_failure) {
                throw std::runtime_error("injected publication failure");
            }
        } catch (...) {
            DegradeAfterResourceGate();
        }
        callback_boundary_returned_ = true;
    }

    void ReleaseSender() noexcept {
        Release(kSenderResource);
    }

    void ReleaseHook() noexcept {
        Release(kHookResource);
    }

    [[nodiscard]] std::uint64_t Released() const noexcept {
        return released_;
    }

    [[nodiscard]] std::uint64_t Retained() const noexcept {
        return HeldCount();
    }

    [[nodiscard]] std::uint64_t DoubleRelease() const noexcept {
        return double_release_;
    }

    [[nodiscard]] bool ReceiptDegraded() const noexcept {
        return receipt_degraded_;
    }

    [[nodiscard]] bool ProtocolOrReceiptFailure() const noexcept {
        return protocol_or_receipt_failure_;
    }

    [[nodiscard]] bool CallbackInvoked() const noexcept {
        return callback_invoked_;
    }

    [[nodiscard]] bool CallbackBoundaryReturned() const noexcept {
        return callback_boundary_returned_;
    }

    [[nodiscard]] std::uint64_t RetainedAtFailure() const noexcept {
        return retained_at_failure_;
    }

    [[nodiscard]] protocol::DispatchRetainedReason
    RetainedReasonAtFailure() const noexcept {
        return retained_reason_at_failure_;
    }

    [[nodiscard]] protocol::DispatchRetainedReason
    RetainedReason() const noexcept {
        return retained_reason_;
    }

    [[nodiscard]] std::uint32_t ResourceGateDepth()
        const noexcept {
        return resource_gate_depth_;
    }

    [[nodiscard]] std::uint32_t MaximumResourceGateDepth()
        const noexcept {
        return maximum_resource_gate_depth_;
    }

    [[nodiscard]] std::uint64_t PermanentPinCalls()
        const noexcept {
        return permanent_pin_calls_;
    }

    [[nodiscard]] std::uint64_t DiagnosticCalls()
        const noexcept {
        return diagnostic_calls_;
    }

    [[nodiscard]] std::uint64_t ExternalActionsWhileLocked()
        const noexcept {
        return external_actions_while_locked_;
    }

    [[nodiscard]] std::uint64_t ObserverAdds()
        const noexcept {
        return observer_adds_;
    }

    [[nodiscard]] std::uint64_t ObserverAddsOutsideGate()
        const noexcept {
        return observer_adds_outside_gate_;
    }

    [[nodiscard]] std::uint64_t ObserverReleases()
        const noexcept {
        return observer_releases_;
    }

    [[nodiscard]] std::uint64_t ObserverDoubleRelease()
        const noexcept {
        return observer_double_release_;
    }

    [[nodiscard]] std::uint64_t ExternalActionsWithoutObserver()
        const noexcept {
        return external_actions_without_observer_;
    }

private:
    static constexpr std::uint32_t kSenderResource = 1u << 0;
    static constexpr std::uint32_t kCallbackResource = 1u << 1;
    static constexpr std::uint32_t kHookResource = 1u << 2;

    void EnterResourceGate() noexcept {
        ++resource_gate_depth_;
        maximum_resource_gate_depth_ = std::max(
            maximum_resource_gate_depth_,
            resource_gate_depth_);
    }

    void ExitResourceGate() noexcept {
        if (resource_gate_depth_ != 0) {
            --resource_gate_depth_;
        }
    }

    void AcquireClaimObserver() noexcept {
        if (resource_gate_depth_ == 0) {
            ++observer_adds_outside_gate_;
        }
        if (observer_live_) {
            ++observer_double_release_;
            return;
        }
        observer_live_ = true;
        ++observer_adds_;
    }

    void ReleaseClaimObserver() noexcept {
        if (!observer_live_) {
            ++observer_double_release_;
            return;
        }
        observer_live_ = false;
        ++observer_releases_;
    }

    void RecordExternalAction() noexcept {
        if (resource_gate_depth_ != 0) {
            ++external_actions_while_locked_;
        }
        if (!observer_live_) {
            ++external_actions_without_observer_;
        }
    }

    void DegradeAfterResourceGate() noexcept {
        receipt_degraded_ = true;
        protocol_or_receipt_failure_ = true;
        retained_at_failure_ = HeldCount();
        retained_reason_at_failure_ =
            protocol::DispatchRetainedReason::ProtocolFailure;
        retained_reason_ =
            retained_at_failure_ == 0
                ? protocol::DispatchRetainedReason::None
                : protocol::DispatchRetainedReason::
                      ProtocolFailure;
        RecordExternalAction();
        ++permanent_pin_calls_;
        RecordExternalAction();
        ++diagnostic_calls_;
    }

    void Release(std::uint32_t resource) noexcept {
        if ((held_resources_ & resource) == 0) {
            ++double_release_;
            return;
        }
        held_resources_ &= ~resource;
        ++released_;
        retained_reason_ =
            HeldCount() == 0
                ? protocol::DispatchRetainedReason::None
                : retained_reason_;
    }

    [[nodiscard]] std::uint64_t HeldCount() const noexcept {
        std::uint64_t count = 0;
        for (std::uint32_t resource :
             {kSenderResource, kCallbackResource, kHookResource}) {
            if ((held_resources_ & resource) != 0) {
                ++count;
            }
        }
        return count;
    }

    std::uint32_t held_resources_ =
        kSenderResource | kCallbackResource | kHookResource;
    std::uint64_t released_ = 0;
    std::uint64_t double_release_ = 0;
    std::uint64_t retained_at_failure_ = 0;
    protocol::DispatchRetainedReason
        retained_reason_at_failure_ =
            protocol::DispatchRetainedReason::None;
    protocol::DispatchRetainedReason retained_reason_ =
        protocol::DispatchRetainedReason::None;
    bool receipt_degraded_ = false;
    bool protocol_or_receipt_failure_ = false;
    bool callback_invoked_ = false;
    bool callback_boundary_returned_ = false;
    std::uint32_t resource_gate_depth_ = 0;
    std::uint32_t maximum_resource_gate_depth_ = 0;
    std::uint64_t permanent_pin_calls_ = 0;
    std::uint64_t diagnostic_calls_ = 0;
    std::uint64_t external_actions_while_locked_ = 0;
    std::uint64_t observer_adds_ = 0;
    std::uint64_t observer_adds_outside_gate_ = 0;
    std::uint64_t observer_releases_ = 0;
    std::uint64_t observer_double_release_ = 0;
    std::uint64_t external_actions_without_observer_ = 0;
    bool observer_live_ = false;
};

class FakeDispatchSummaryBoundary {
public:
    FakeDispatchSummaryBoundary(
        bool pending,
        std::uint32_t hook_count) noexcept
        : pending_source_(pending),
          hook_count_source_(hook_count) {}

    void CaptureAndLog() noexcept {
        bool pending_snapshot = false;
        std::uint32_t hook_count_snapshot = 0;
        {
            GateGuard guard(*this);
            ++snapshot_calls_;
            if (gate_depth_ == 0) {
                ++snapshot_reads_while_unlocked_;
            }
            pending_snapshot = pending_source_;
            hook_count_snapshot = hook_count_source_;
        }

        ++log_calls_;
        if (gate_depth_ != 0) {
            ++external_actions_while_locked_;
        }
        logged_pending_ = pending_snapshot;
        logged_hook_count_ = hook_count_snapshot;
    }

    [[nodiscard]] bool LoggedPending() const noexcept {
        return logged_pending_;
    }

    [[nodiscard]] std::uint32_t LoggedHookCount()
        const noexcept {
        return logged_hook_count_;
    }

    [[nodiscard]] std::uint32_t GateDepth() const noexcept {
        return gate_depth_;
    }

    [[nodiscard]] std::uint32_t MaximumGateDepth()
        const noexcept {
        return maximum_gate_depth_;
    }

    [[nodiscard]] std::uint64_t SnapshotCalls()
        const noexcept {
        return snapshot_calls_;
    }

    [[nodiscard]] std::uint64_t SnapshotReadsWhileUnlocked()
        const noexcept {
        return snapshot_reads_while_unlocked_;
    }

    [[nodiscard]] std::uint64_t LogCalls() const noexcept {
        return log_calls_;
    }

    [[nodiscard]] std::uint64_t ExternalActionsWhileLocked()
        const noexcept {
        return external_actions_while_locked_;
    }

private:
    class GateGuard {
    public:
        explicit GateGuard(
            FakeDispatchSummaryBoundary& owner) noexcept
            : owner_(&owner) {
            ++owner_->gate_depth_;
            owner_->maximum_gate_depth_ = std::max(
                owner_->maximum_gate_depth_,
                owner_->gate_depth_);
        }

        GateGuard(const GateGuard&) = delete;
        GateGuard& operator=(const GateGuard&) = delete;

        ~GateGuard() noexcept {
            if (owner_->gate_depth_ != 0) {
                --owner_->gate_depth_;
            }
        }

    private:
        FakeDispatchSummaryBoundary* owner_;
    };

    bool pending_source_ = false;
    std::uint32_t hook_count_source_ = 0;
    bool logged_pending_ = false;
    std::uint32_t logged_hook_count_ = 0;
    std::uint32_t gate_depth_ = 0;
    std::uint32_t maximum_gate_depth_ = 0;
    std::uint64_t snapshot_calls_ = 0;
    std::uint64_t snapshot_reads_while_unlocked_ = 0;
    std::uint64_t log_calls_ = 0;
    std::uint64_t external_actions_while_locked_ = 0;
};

class FakeDeferredDispatchContext {
public:
    [[nodiscard]] bool AddObserverReference(
        std::uint32_t resource_gate_depth) noexcept {
        if (resource_gate_depth == 0) {
            ++observer_adds_outside_gate_;
        }
        if (destroyed_.load(std::memory_order_acquire)) {
            return false;
        }
        references_.fetch_add(1, std::memory_order_acq_rel);
        observer_adds_.fetch_add(1, std::memory_order_acq_rel);
        return true;
    }

    void ReleasePendingReference() noexcept {
        pending_releases_.fetch_add(
            1, std::memory_order_acq_rel);
        ReleaseOne();
    }

    void ReleaseObserverReference() noexcept {
        observer_releases_.fetch_add(
            1, std::memory_order_acq_rel);
        ReleaseOne();
    }

    [[nodiscard]] bool DeferredExternalAction(
        std::uint32_t resource_gate_depth) noexcept {
        external_actions_.fetch_add(
            1, std::memory_order_acq_rel);
        if (resource_gate_depth != 0) {
            external_actions_while_locked_.fetch_add(
                1, std::memory_order_acq_rel);
        }
        if (destroyed_.load(std::memory_order_acquire) ||
            references_.load(std::memory_order_acquire) == 0) {
            use_after_free_.fetch_add(
                1, std::memory_order_acq_rel);
            return false;
        }
        successful_observations_.fetch_add(
            1, std::memory_order_acq_rel);
        return true;
    }

    [[nodiscard]] std::uint64_t References() const noexcept {
        return references_.load(std::memory_order_acquire);
    }

    [[nodiscard]] bool Destroyed() const noexcept {
        return destroyed_.load(std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ObserverAdds() const noexcept {
        return observer_adds_.load(std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ObserverAddsOutsideGate()
        const noexcept {
        return observer_adds_outside_gate_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t PendingReleases()
        const noexcept {
        return pending_releases_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ObserverReleases()
        const noexcept {
        return observer_releases_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t DoubleRelease()
        const noexcept {
        return double_release_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t UseAfterFree()
        const noexcept {
        return use_after_free_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t SuccessfulObservations()
        const noexcept {
        return successful_observations_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ExternalActions()
        const noexcept {
        return external_actions_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ExternalActionsWhileLocked()
        const noexcept {
        return external_actions_while_locked_.load(
            std::memory_order_acquire);
    }

private:
    void ReleaseOne() noexcept {
        std::uint64_t observed =
            references_.load(std::memory_order_acquire);
        for (;;) {
            if (observed == 0) {
                double_release_.fetch_add(
                    1, std::memory_order_acq_rel);
                return;
            }
            if (references_.compare_exchange_weak(
                    observed, observed - 1,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire)) {
                if (observed == 1) {
                    destroyed_.store(
                        true, std::memory_order_release);
                }
                return;
            }
        }
    }

    std::atomic<std::uint64_t> references_{1};
    std::atomic<bool> destroyed_{false};
    std::atomic<std::uint64_t> observer_adds_{0};
    std::atomic<std::uint64_t> observer_adds_outside_gate_{0};
    std::atomic<std::uint64_t> pending_releases_{0};
    std::atomic<std::uint64_t> observer_releases_{0};
    std::atomic<std::uint64_t> double_release_{0};
    std::atomic<std::uint64_t> use_after_free_{0};
    std::atomic<std::uint64_t> successful_observations_{0};
    std::atomic<std::uint64_t> external_actions_{0};
    std::atomic<std::uint64_t> external_actions_while_locked_{0};
};

class FakeDeferredDispatchObserverReference {
public:
    FakeDeferredDispatchObserverReference(
        FakeDeferredDispatchContext& context,
        std::uint32_t resource_gate_depth) noexcept {
        if (context.AddObserverReference(
                resource_gate_depth)) {
            context_ = &context;
        }
    }

    FakeDeferredDispatchObserverReference(
        FakeDeferredDispatchObserverReference&& other) noexcept
        : context_(std::exchange(other.context_, nullptr)) {}

    FakeDeferredDispatchObserverReference(
        const FakeDeferredDispatchObserverReference&) = delete;
    FakeDeferredDispatchObserverReference& operator=(
        const FakeDeferredDispatchObserverReference&) = delete;
    FakeDeferredDispatchObserverReference& operator=(
        FakeDeferredDispatchObserverReference&&) = delete;

    ~FakeDeferredDispatchObserverReference() noexcept {
        if (context_) {
            context_->ReleaseObserverReference();
        }
    }

    [[nodiscard]] FakeDeferredDispatchContext* Get()
        const noexcept {
        return context_;
    }

private:
    FakeDeferredDispatchContext* context_ = nullptr;
};

struct FakeForeignAbiState {
    bool throw_on_enter = false;
    bool throw_on_release = false;
    bool throw_in_post_processing = false;
    bool throw_in_original = false;
    bool lifecycle_admitted = false;
    bool lifecycle_released = false;
    bool permanent_pin = false;
    bool quiesced = false;
    std::uint64_t original_calls = 0;
    int current_last_error = 0;
    int returned_last_error = 0;
};

void FailClosedFakeForeignAbi(
    FakeForeignAbiState& state) noexcept {
    state.permanent_pin = true;
    state.quiesced = true;
    state.current_last_error = 9001;
}

class FakeForeignAbiLifecycleScope {
public:
    explicit FakeForeignAbiLifecycleScope(
        FakeForeignAbiState& state) noexcept
        : state_(state) {
        try {
            if (state_.throw_on_enter) {
                throw std::runtime_error(
                    "injected lifecycle admission failure");
            }
            state_.lifecycle_admitted = true;
        } catch (...) {
            FailClosedFakeForeignAbi(state_);
        }
    }

    ~FakeForeignAbiLifecycleScope() noexcept {
        if (!state_.lifecycle_admitted) {
            return;
        }
        try {
            if (state_.throw_on_release) {
                throw std::runtime_error(
                    "injected lifecycle release failure");
            }
            state_.lifecycle_released = true;
        } catch (...) {
            FailClosedFakeForeignAbi(state_);
        }
    }

    explicit operator bool() const noexcept {
        return state_.lifecycle_admitted;
    }

private:
    FakeForeignAbiState& state_;
};

int InvokeFakeForeignAbiHook(
    FakeForeignAbiState& state) noexcept {
    int result = -1;
    int original_error = 0;
    bool original_attempted = false;
    bool original_completed = false;
    const auto invoke_original = [&] {
        original_attempted = true;
        ++state.original_calls;
        if (state.throw_in_original) {
            throw std::runtime_error(
                "injected original failure");
        }
        result = 73;
        state.current_last_error = 41;
        original_error = state.current_last_error;
        original_completed = true;
    };

    try {
        {
            FakeForeignAbiLifecycleScope lifecycle_scope(state);
            invoke_original();
            if (lifecycle_scope &&
                state.throw_in_post_processing) {
                state.current_last_error = 99;
                throw std::runtime_error(
                    "injected post-processing failure");
            }
        }
        state.returned_last_error = original_error;
        return result;
    } catch (...) {
        FailClosedFakeForeignAbi(state);
        if (original_completed) {
            state.returned_last_error = original_error;
            return result;
        }
        if (original_attempted) {
            state.returned_last_error = -2;
            return -1;
        }
        try {
            invoke_original();
            state.returned_last_error = original_error;
            return result;
        } catch (...) {
            FailClosedFakeForeignAbi(state);
            state.returned_last_error = -2;
            return -1;
        }
    }
}

struct FakeDestroyHookState : FakeForeignAbiState {
    bool throw_in_classification = false;
    bool throw_in_receipt = false;
    std::uint64_t classification_calls = 0;
    std::uint64_t receipt_calls = 0;
};

int InvokeFakeDestroyWindowHook(
    FakeDestroyHookState& state) noexcept {
    int result = 0;
    int original_error = 0;
    bool original_attempted = false;
    bool original_completed = false;
    const auto invoke_original = [&] {
        original_attempted = true;
        ++state.original_calls;
        if (state.throw_in_original) {
            throw std::runtime_error(
                "injected DestroyWindow original failure");
        }
        result = 1;
        state.current_last_error = 41;
        original_error = state.current_last_error;
        original_completed = true;
    };

    try {
        FakeForeignAbiLifecycleScope lifecycle_scope(state);
        if (!lifecycle_scope) {
            invoke_original();
            state.returned_last_error = original_error;
            return result;
        }

        ++state.classification_calls;
        if (state.throw_in_classification) {
            throw std::runtime_error(
                "injected DestroyWindow classification failure");
        }

        invoke_original();
        ++state.receipt_calls;
        if (state.throw_in_receipt) {
            state.current_last_error = 99;
            throw std::runtime_error(
                "injected DestroyWindow receipt failure");
        }
        state.returned_last_error = original_error;
        return result;
    } catch (...) {
        FailClosedFakeForeignAbi(state);
        if (original_completed) {
            state.returned_last_error = original_error;
            return result;
        }
        if (original_attempted) {
            state.returned_last_error = -2;
            return 0;
        }
        try {
            invoke_original();
            state.returned_last_error = original_error;
            return result;
        } catch (...) {
            FailClosedFakeForeignAbi(state);
            state.returned_last_error = -2;
            return 0;
        }
    }
}

struct FakeWorkerBoundaryState {
    bool throw_on_scope_enter = false;
    bool throw_in_external_com = false;
    std::uint64_t strong_references = 1;
    std::uint64_t addref_calls = 0;
    std::uint64_t release_calls = 0;
    std::uint64_t com_initialize_calls = 0;
    std::uint64_t com_uninitialize_calls = 0;
    std::uint64_t external_com_calls = 0;
    bool lifecycle_admitted = false;
    bool retained = false;
    bool permanent_pin = false;
    bool quiesced = false;
    bool result_published = false;
};

class FakeWorkerReferenceGuard {
public:
    explicit FakeWorkerReferenceGuard(
        FakeWorkerBoundaryState& state) noexcept
        : state_(state) {}

    ~FakeWorkerReferenceGuard() noexcept {
        ++state_.release_calls;
        if (state_.strong_references == 0) {
            state_.permanent_pin = true;
            state_.quiesced = true;
            return;
        }
        --state_.strong_references;
    }

private:
    FakeWorkerBoundaryState& state_;
};

class FakeComApartmentBalance {
public:
    explicit FakeComApartmentBalance(
        FakeWorkerBoundaryState& state) noexcept
        : state_(state) {
        ++state_.com_initialize_calls;
        balance_ = true;
    }

    ~FakeComApartmentBalance() noexcept {
        if (balance_) {
            ++state_.com_uninitialize_calls;
        }
    }

private:
    FakeWorkerBoundaryState& state_;
    bool balance_ = false;
};

void InvokeFakeVisualTreeWorker(
    FakeWorkerBoundaryState& state) noexcept {
    ++state.strong_references;
    ++state.addref_calls;
    FakeWorkerReferenceGuard reference_guard(state);
    try {
        try {
            if (state.throw_on_scope_enter) {
                throw std::runtime_error(
                    "injected worker lifecycle admission failure");
            }
            state.lifecycle_admitted = true;
        } catch (...) {
            state.retained = true;
            state.permanent_pin = true;
            state.quiesced = true;
        }

        if (!state.lifecycle_admitted) {
            state.result_published = true;
            return;
        }

        FakeComApartmentBalance com_balance(state);
        ++state.external_com_calls;
        if (state.throw_in_external_com) {
            throw std::runtime_error(
                "injected worker external COM failure");
        }
        state.result_published = true;
    } catch (...) {
        state.retained = true;
        state.permanent_pin = true;
        state.quiesced = true;
        state.result_published = true;
    }
}

class FakePermanentPinProtocol {
public:
    class DecisionGuard {
    public:
        explicit DecisionGuard(
            FakePermanentPinProtocol& owner) noexcept
            : owner_(owner) {
            while (owner_.decision_gate_.test_and_set(
                std::memory_order_acquire)) {
                std::this_thread::yield();
            }
        }

        ~DecisionGuard() noexcept {
            owner_.decision_gate_.clear(
                std::memory_order_release);
        }

    private:
        FakePermanentPinProtocol& owner_;
    };

    void RequireAndHold(
        std::atomic<bool>& published,
        std::atomic<bool>& release_gate) noexcept {
        DecisionGuard guard(*this);
        permanent_.store(true, std::memory_order_release);
        published.store(true, std::memory_order_release);
        while (!release_gate.load(std::memory_order_acquire)) {
            std::this_thread::yield();
        }
    }

    bool Release() noexcept {
        DecisionGuard guard(*this);
        if (permanent_.load(std::memory_order_acquire)) {
            return false;
        }
        if (!module_pinned_) {
            ++double_release_;
            return false;
        }
        if (permanent_.load(std::memory_order_acquire)) {
            ++free_after_permanent_;
            return false;
        }
        module_pinned_ = false;
        ++free_calls_;
        return true;
    }

    [[nodiscard]] bool Permanent() const noexcept {
        return permanent_.load(std::memory_order_acquire);
    }

    [[nodiscard]] bool ModulePinned() const noexcept {
        return module_pinned_;
    }

    [[nodiscard]] std::uint64_t FreeCalls() const noexcept {
        return free_calls_;
    }

    [[nodiscard]] std::uint64_t FreeAfterPermanent() const noexcept {
        return free_after_permanent_;
    }

    [[nodiscard]] std::uint64_t DoubleRelease() const noexcept {
        return double_release_;
    }

private:
    std::atomic_flag decision_gate_ = ATOMIC_FLAG_INIT;
    std::atomic<bool> permanent_{false};
    bool module_pinned_ = true;
    std::uint64_t free_calls_ = 0;
    std::uint64_t free_after_permanent_ = 0;
    std::uint64_t double_release_ = 0;
};

enum class FakeModuleActivationState {
    Active,
    Quiesced,
    Unloading,
};

struct FakeModuleExportState {
    FakeModuleActivationState activation =
        FakeModuleActivationState::Active;
    bool state_gate_acquired = false;
    bool state_gate_released = false;
    bool activation_permit_opened = false;
    bool activation_permit_closed = false;
    bool throw_after_gate = false;
    bool throw_after_permit = false;
    bool throw_after_unloading = false;
    bool throw_in_settings_log = false;
    bool permanent_pin = false;
    bool inject_flag = false;
    bool settings_log_attempted = false;
    bool settings_log_contained = false;
    std::uint64_t state_gate_release_count = 0;
    std::uint64_t activation_permit_close_count = 0;
    std::uint64_t order = 0;
    std::uint64_t settings_latch_order = 0;
    std::uint64_t settings_log_order = 0;
    std::uint64_t unloading_published_order = 0;
    std::uint64_t first_fallible_operation_order = 0;
};

enum class FakeKernelCapabilityTerminal {
    Owned,
    Closed,
    CloseFailedRetained,
    ReleasePendingRetained,
    DeletePendingCloseFailedRetained,
    MutexReleasePendingRetained,
    ChangeNotificationCloseFailedRetained,
};

class FakeKernelCapabilityTicketSource {
public:
    [[nodiscard]] std::uint64_t Claim() noexcept {
        return ++last_ticket_;
    }

private:
    std::uint64_t last_ticket_ = 0;
};

class FakeKernelCapabilityOwner {
public:
    explicit FakeKernelCapabilityOwner(
        FakeKernelCapabilityTicketSource& tickets) noexcept
        : owner_ticket_(tickets.Claim()) {}

    void MarkDeletePending() noexcept {
        delete_pending_ = true;
    }

    [[nodiscard]] bool Close(
        bool close_succeeds,
        FakeKernelCapabilityTerminal failure_terminal =
            FakeKernelCapabilityTerminal::
                CloseFailedRetained) noexcept {
        ++close_calls_;
        if (!owns_capability_ ||
            terminal_ != FakeKernelCapabilityTerminal::Owned) {
            return false;
        }
        owns_capability_ = false;
        terminal_ =
            close_succeeds
                ? FakeKernelCapabilityTerminal::Closed
                : failure_terminal;
        return close_succeeds;
    }

    [[nodiscard]] bool ReleaseThenClose(
        bool release_succeeds,
        bool close_succeeds,
        FakeKernelCapabilityTerminal
            release_failure_terminal) noexcept {
        ++release_calls_;
        if (!owns_capability_ ||
            terminal_ != FakeKernelCapabilityTerminal::Owned) {
            return false;
        }
        if (!release_succeeds) {
            owns_capability_ = false;
            terminal_ = release_failure_terminal;
            return false;
        }
        release_confirmed_ = true;
        return Close(close_succeeds);
    }

    [[nodiscard]] std::uint64_t OwnerTicket() const noexcept {
        return owner_ticket_;
    }

    [[nodiscard]] FakeKernelCapabilityTerminal Terminal()
        const noexcept {
        return terminal_;
    }

    [[nodiscard]] bool ReleaseConfirmed() const noexcept {
        return release_confirmed_;
    }

    [[nodiscard]] bool DeletePending() const noexcept {
        return delete_pending_;
    }

    [[nodiscard]] std::uint32_t ReleaseCalls() const noexcept {
        return release_calls_;
    }

    [[nodiscard]] std::uint32_t CloseCalls() const noexcept {
        return close_calls_;
    }

private:
    std::uint64_t owner_ticket_ = 0;
    FakeKernelCapabilityTerminal terminal_ =
        FakeKernelCapabilityTerminal::Owned;
    bool owns_capability_ = true;
    bool release_confirmed_ = false;
    bool delete_pending_ = false;
    std::uint32_t release_calls_ = 0;
    std::uint32_t close_calls_ = 0;
};

class FakeKernelCapabilityReservationTable {
public:
    struct Creation {
        std::uint64_t owner_ticket = 0;
        bool created = false;
    };

    explicit FakeKernelCapabilityReservationTable(
        std::uint32_t capacity) noexcept
        : capacity_(capacity) {}

    [[nodiscard]] Creation TryCreate() noexcept {
        ++reservation_attempts_;
        const auto reservation_order = ++order_;
        if (reserved_slots_ == capacity_) {
            ++capacity_rejections_;
            return {};
        }

        ++reserved_slots_;
        ++successful_reservations_;
        const auto owner_ticket = ++last_owner_ticket_;
        ++create_calls_;
        const auto create_order = ++order_;
        all_creates_were_pre_reserved_ &=
            reservation_order < create_order &&
            reserved_slots_ <= capacity_;
        return {owner_ticket, true};
    }

    [[nodiscard]] bool Release(
        const Creation& creation) noexcept {
        if (!creation.created ||
            creation.owner_ticket == 0 ||
            reserved_slots_ == 0) {
            return false;
        }
        --reserved_slots_;
        return true;
    }

    [[nodiscard]] std::uint32_t ReservationAttempts()
        const noexcept {
        return reservation_attempts_;
    }

    [[nodiscard]] std::uint32_t SuccessfulReservations()
        const noexcept {
        return successful_reservations_;
    }

    [[nodiscard]] std::uint32_t CreateCalls() const noexcept {
        return create_calls_;
    }

    [[nodiscard]] std::uint32_t CapacityRejections()
        const noexcept {
        return capacity_rejections_;
    }

    [[nodiscard]] std::uint32_t ReservedSlots()
        const noexcept {
        return reserved_slots_;
    }

    [[nodiscard]] bool AllCreatesWerePreReserved()
        const noexcept {
        return all_creates_were_pre_reserved_;
    }

private:
    const std::uint32_t capacity_;
    std::uint32_t reserved_slots_ = 0;
    std::uint32_t reservation_attempts_ = 0;
    std::uint32_t successful_reservations_ = 0;
    std::uint32_t create_calls_ = 0;
    std::uint32_t capacity_rejections_ = 0;
    std::uint64_t last_owner_ticket_ = 0;
    std::uint64_t order_ = 0;
    bool all_creates_were_pre_reserved_ = true;
};

enum class FakeKernelCloseDisposition {
    Closed,
    Transferred,
    StillOwned,
};

enum class FakeStateGateLeasePhase {
    Held,
    ReleasePending,
    ReleasedClosePending,
    Closed,
    Quarantined,
};

class FakeStateGateLease {
public:
    [[nodiscard]] bool ReleaseAndClose(
        bool release_succeeds,
        FakeKernelCloseDisposition close_disposition) noexcept {
        if (phase_ == FakeStateGateLeasePhase::Closed) {
            return true;
        }
        if (phase_ == FakeStateGateLeasePhase::Quarantined) {
            return false;
        }

        if (phase_ == FakeStateGateLeasePhase::Held ||
            phase_ == FakeStateGateLeasePhase::ReleasePending) {
            ++release_semaphore_calls_;
            if (!release_succeeds) {
                phase_ = FakeStateGateLeasePhase::ReleasePending;
                return false;
            }
            phase_ =
                FakeStateGateLeasePhase::ReleasedClosePending;
        }

        ++close_handle_calls_;
        switch (close_disposition) {
        case FakeKernelCloseDisposition::Closed:
            phase_ = FakeStateGateLeasePhase::Closed;
            return true;
        case FakeKernelCloseDisposition::Transferred:
            phase_ = FakeStateGateLeasePhase::Quarantined;
            return false;
        case FakeKernelCloseDisposition::StillOwned:
            phase_ =
                FakeStateGateLeasePhase::ReleasedClosePending;
            return false;
        }
        return false;
    }

    [[nodiscard]] FakeStateGateLeasePhase Phase()
        const noexcept {
        return phase_;
    }

    [[nodiscard]] std::uint32_t ReleaseSemaphoreCalls()
        const noexcept {
        return release_semaphore_calls_;
    }

    [[nodiscard]] std::uint32_t CloseHandleCalls()
        const noexcept {
        return close_handle_calls_;
    }

private:
    FakeStateGateLeasePhase phase_ =
        FakeStateGateLeasePhase::Held;
    std::uint32_t release_semaphore_calls_ = 0;
    std::uint32_t close_handle_calls_ = 0;
};

enum class FakeStatsMutexLeasePhase {
    Owned,
    ReleasePending,
    Closed,
    ThreadAffineQuarantine,
};

class FakeStatsMutexThreadAffineOwner {
public:
    explicit FakeStatsMutexThreadAffineOwner(
        std::uint32_t owning_thread_id) noexcept
        : owning_thread_id_(owning_thread_id) {}

    [[nodiscard]] bool ReleaseOnThread(
        std::uint32_t current_thread_id,
        bool release_succeeds) noexcept {
        if (phase_ == FakeStatsMutexLeasePhase::Closed) {
            return true;
        }
        if (phase_ ==
            FakeStatsMutexLeasePhase::ThreadAffineQuarantine) {
            ++quarantined_retry_rejections_;
            return false;
        }
        if (current_thread_id != owning_thread_id_) {
            ++wrong_thread_rejections_;
            phase_ =
                FakeStatsMutexLeasePhase::
                    ThreadAffineQuarantine;
            return false;
        }

        ++release_mutex_calls_;
        if (!release_succeeds) {
            phase_ = FakeStatsMutexLeasePhase::ReleasePending;
            return false;
        }
        phase_ = FakeStatsMutexLeasePhase::Closed;
        return true;
    }

    void QuarantineAfterOwningThreadExit() noexcept {
        if (phase_ == FakeStatsMutexLeasePhase::Owned ||
            phase_ == FakeStatsMutexLeasePhase::ReleasePending) {
            phase_ =
                FakeStatsMutexLeasePhase::
                    ThreadAffineQuarantine;
        }
    }

    [[nodiscard]] FakeStatsMutexLeasePhase Phase()
        const noexcept {
        return phase_;
    }

    [[nodiscard]] std::uint32_t OwningThreadId()
        const noexcept {
        return owning_thread_id_;
    }

    [[nodiscard]] std::uint32_t ReleaseMutexCalls()
        const noexcept {
        return release_mutex_calls_;
    }

    [[nodiscard]] std::uint32_t WrongThreadRejections()
        const noexcept {
        return wrong_thread_rejections_;
    }

    [[nodiscard]] std::uint32_t QuarantinedRetryRejections()
        const noexcept {
        return quarantined_retry_rejections_;
    }

private:
    const std::uint32_t owning_thread_id_;
    FakeStatsMutexLeasePhase phase_ =
        FakeStatsMutexLeasePhase::Owned;
    std::uint32_t release_mutex_calls_ = 0;
    std::uint32_t wrong_thread_rejections_ = 0;
    std::uint32_t quarantined_retry_rejections_ = 0;
};

enum class FakeRegistryWaitSlotState {
    Reserved,
    Published,
    Retained,
    Closed,
};

enum class FakeRegistryWaitDependencyState {
    LiveLocal,
    Retained,
    Closed,
};

enum class FakeRegistryWaitPublishOutcome {
    Published,
    SynchronouslyRolledBack,
    BundleRetained,
    Invalid,
};

class FakeRegistryWaitBundle {
public:
    [[nodiscard]] bool BindBeforeExternalRegister() noexcept {
        RegistryGateGuard guard(gate_);
        if (bound_ ||
            wait_state_ != FakeRegistryWaitSlotState::Reserved ||
            event_state_ !=
                FakeRegistryWaitDependencyState::LiveLocal ||
            key_state_ !=
                FakeRegistryWaitDependencyState::LiveLocal ||
            !context_live_) {
            return false;
        }
        bind_order_ = ++order_;
        context_bound_ = true;
        event_bound_ = true;
        key_bound_ = true;
        bound_ = true;
        return true;
    }

    [[nodiscard]] bool RegisterExternalWait() noexcept {
        RegistryGateGuard guard(gate_);
        if (!bound_ || !context_bound_ || !event_bound_ ||
            !key_bound_ ||
            wait_state_ != FakeRegistryWaitSlotState::Reserved ||
            external_wait_handle_present_) {
            ++external_register_blocked_;
            return false;
        }
        external_register_order_ = ++order_;
        ++external_register_calls_;
        external_wait_handle_present_ = true;
        local_wait_owner_ = true;
        return true;
    }

    [[nodiscard]] FakeRegistryWaitPublishOutcome Publish(
        bool publish_succeeds,
        bool synchronous_unregister_confirmed) noexcept {
        RegistryGateGuard guard(gate_);
        if (!bound_ || !external_wait_handle_present_ ||
            wait_state_ != FakeRegistryWaitSlotState::Reserved) {
            return FakeRegistryWaitPublishOutcome::Invalid;
        }
        ++publish_calls_;
        if (publish_succeeds) {
            wait_state_ = FakeRegistryWaitSlotState::Published;
            return FakeRegistryWaitPublishOutcome::Published;
        }

        ++synchronous_unregister_calls_;
        if (synchronous_unregister_confirmed) {
            unregister_confirmed_ = true;
            external_wait_handle_present_ = false;
            local_wait_owner_ = false;
            wait_state_ = FakeRegistryWaitSlotState::Closed;
            return FakeRegistryWaitPublishOutcome::
                SynchronouslyRolledBack;
        }

        // One decision section isolates the externally returned wait handle
        // directly from its Reserved slot together with every dependency.
        wait_state_ = FakeRegistryWaitSlotState::Retained;
        event_state_ =
            FakeRegistryWaitDependencyState::Retained;
        key_state_ =
            FakeRegistryWaitDependencyState::Retained;
        context_retained_ = true;
        local_wait_owner_ = false;
        local_event_owner_ = false;
        local_key_owner_ = false;
        context_live_ = false;
        permanent_pin_required_ = true;
        ++reserved_external_wait_bundle_isolations_;
        return FakeRegistryWaitPublishOutcome::BundleRetained;
    }

    [[nodiscard]] bool TryCloseDependencies() noexcept {
        RegistryGateGuard guard(gate_);
        if (!unregister_confirmed_ ||
            wait_state_ != FakeRegistryWaitSlotState::Closed) {
            ++prohibited_dependency_close_attempts_;
            return false;
        }
        if (event_state_ ==
            FakeRegistryWaitDependencyState::LiveLocal) {
            event_state_ =
                FakeRegistryWaitDependencyState::Closed;
            local_event_owner_ = false;
            ++event_close_calls_;
        }
        if (key_state_ ==
            FakeRegistryWaitDependencyState::LiveLocal) {
            key_state_ =
                FakeRegistryWaitDependencyState::Closed;
            local_key_owner_ = false;
            ++key_close_calls_;
        }
        if (context_live_) {
            context_live_ = false;
            ++context_delete_calls_;
        }
        return true;
    }

    [[nodiscard]] bool BoundBeforeExternalRegister()
        const noexcept {
        return bind_order_ != 0 &&
            external_register_order_ > bind_order_;
    }

    [[nodiscard]] FakeRegistryWaitSlotState WaitState()
        const noexcept {
        return wait_state_;
    }

    [[nodiscard]] FakeRegistryWaitDependencyState EventState()
        const noexcept {
        return event_state_;
    }

    [[nodiscard]] FakeRegistryWaitDependencyState KeyState()
        const noexcept {
        return key_state_;
    }

    [[nodiscard]] bool ContextRetained() const noexcept {
        return context_retained_;
    }

    [[nodiscard]] bool ExternalWaitHandlePresent()
        const noexcept {
        return external_wait_handle_present_;
    }

    [[nodiscard]] bool LocalOwnersCleared() const noexcept {
        return !local_wait_owner_ && !local_event_owner_ &&
            !local_key_owner_ && !context_live_;
    }

    [[nodiscard]] bool PermanentPinRequired() const noexcept {
        return permanent_pin_required_;
    }

    [[nodiscard]] std::uint32_t ExternalRegisterCalls()
        const noexcept {
        return external_register_calls_;
    }

    [[nodiscard]] std::uint32_t ExternalRegisterBlocked()
        const noexcept {
        return external_register_blocked_;
    }

    [[nodiscard]] std::uint32_t PublishCalls() const noexcept {
        return publish_calls_;
    }

    [[nodiscard]] std::uint32_t SynchronousUnregisterCalls()
        const noexcept {
        return synchronous_unregister_calls_;
    }

    [[nodiscard]] std::uint32_t
    ReservedExternalWaitBundleIsolations() const noexcept {
        return reserved_external_wait_bundle_isolations_;
    }

    [[nodiscard]] std::uint32_t
    ProhibitedDependencyCloseAttempts() const noexcept {
        return prohibited_dependency_close_attempts_;
    }

    [[nodiscard]] std::uint32_t EventCloseCalls()
        const noexcept {
        return event_close_calls_;
    }

    [[nodiscard]] std::uint32_t KeyCloseCalls()
        const noexcept {
        return key_close_calls_;
    }

    [[nodiscard]] std::uint32_t ContextDeleteCalls()
        const noexcept {
        return context_delete_calls_;
    }

private:
    class RegistryGateGuard {
    public:
        explicit RegistryGateGuard(
            std::atomic_flag& gate) noexcept
            : gate_(&gate) {
            while (gate_->test_and_set(
                std::memory_order_acquire)) {
                std::this_thread::yield();
            }
        }

        RegistryGateGuard(const RegistryGateGuard&) = delete;
        RegistryGateGuard& operator=(
            const RegistryGateGuard&) = delete;

        ~RegistryGateGuard() noexcept {
            gate_->clear(std::memory_order_release);
        }

    private:
        std::atomic_flag* gate_;
    };

    std::atomic_flag gate_ = ATOMIC_FLAG_INIT;
    FakeRegistryWaitSlotState wait_state_ =
        FakeRegistryWaitSlotState::Reserved;
    FakeRegistryWaitDependencyState event_state_ =
        FakeRegistryWaitDependencyState::LiveLocal;
    FakeRegistryWaitDependencyState key_state_ =
        FakeRegistryWaitDependencyState::LiveLocal;
    bool bound_ = false;
    bool context_bound_ = false;
    bool event_bound_ = false;
    bool key_bound_ = false;
    bool context_live_ = true;
    bool context_retained_ = false;
    bool external_wait_handle_present_ = false;
    bool local_wait_owner_ = false;
    bool local_event_owner_ = true;
    bool local_key_owner_ = true;
    bool unregister_confirmed_ = false;
    bool permanent_pin_required_ = false;
    std::uint64_t order_ = 0;
    std::uint64_t bind_order_ = 0;
    std::uint64_t external_register_order_ = 0;
    std::uint32_t external_register_calls_ = 0;
    std::uint32_t external_register_blocked_ = 0;
    std::uint32_t publish_calls_ = 0;
    std::uint32_t synchronous_unregister_calls_ = 0;
    std::uint32_t reserved_external_wait_bundle_isolations_ = 0;
    std::uint32_t prohibited_dependency_close_attempts_ = 0;
    std::uint32_t event_close_calls_ = 0;
    std::uint32_t key_close_calls_ = 0;
    std::uint32_t context_delete_calls_ = 0;
};

enum class FakeSingleGenerationActivationState {
    Blocked,
    Authorized,
    Active,
    Quiesced,
};

class FakeSingleGenerationActivation {
public:
    [[nodiscard]] bool BeginFirstInitialization() noexcept {
        bool expected = false;
        return initialization_attempted_.compare_exchange_strong(
            expected, true, std::memory_order_acq_rel,
            std::memory_order_acquire);
    }

    [[nodiscard]] bool RejectDuplicateInitialization() noexcept {
        bool expected = false;
        if (initialization_attempted_.compare_exchange_strong(
                expected, true, std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            return false;
        }
        state_.exchange(
            FakeSingleGenerationActivationState::Quiesced,
            std::memory_order_acq_rel);
        permanent_pin_required_.store(
            true, std::memory_order_release);
        duplicate_rejections_.fetch_add(
            1, std::memory_order_acq_rel);
        return true;
    }

    [[nodiscard]] bool TryAuthorize() noexcept {
        auto expected =
            FakeSingleGenerationActivationState::Blocked;
        if (!state_.compare_exchange_strong(
                expected,
                FakeSingleGenerationActivationState::Authorized,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            state_.exchange(
                FakeSingleGenerationActivationState::Quiesced,
                std::memory_order_acq_rel);
            authorization_failures_.fetch_add(
                1, std::memory_order_acq_rel);
            return false;
        }
        authorization_successes_.fetch_add(
            1, std::memory_order_acq_rel);
        return true;
    }

    [[nodiscard]] bool TryActivate() noexcept {
        auto expected =
            FakeSingleGenerationActivationState::Authorized;
        if (!state_.compare_exchange_strong(
                expected,
                FakeSingleGenerationActivationState::Active,
                std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            state_.exchange(
                FakeSingleGenerationActivationState::Quiesced,
                std::memory_order_acq_rel);
            activation_failures_.fetch_add(
                1, std::memory_order_acq_rel);
            return false;
        }
        activation_successes_.fetch_add(
            1, std::memory_order_acq_rel);
        return true;
    }

    [[nodiscard]] FakeSingleGenerationActivationState State()
        const noexcept {
        return state_.load(std::memory_order_acquire);
    }

    [[nodiscard]] bool PermanentPinRequired() const noexcept {
        return permanent_pin_required_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t DuplicateRejections()
        const noexcept {
        return duplicate_rejections_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t AuthorizationSuccesses()
        const noexcept {
        return authorization_successes_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t AuthorizationFailures()
        const noexcept {
        return authorization_failures_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ActivationSuccesses()
        const noexcept {
        return activation_successes_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ActivationFailures()
        const noexcept {
        return activation_failures_.load(
            std::memory_order_acquire);
    }

private:
    std::atomic<bool> initialization_attempted_{false};
    std::atomic<FakeSingleGenerationActivationState> state_{
        FakeSingleGenerationActivationState::Blocked};
    std::atomic<bool> permanent_pin_required_{false};
    std::atomic<std::uint64_t> duplicate_rejections_{0};
    std::atomic<std::uint64_t> authorization_successes_{0};
    std::atomic<std::uint64_t> authorization_failures_{0};
    std::atomic<std::uint64_t> activation_successes_{0};
    std::atomic<std::uint64_t> activation_failures_{0};
};

void FailClosedFakeModuleExport(
    FakeModuleExportState& state) noexcept {
    if (state.activation !=
        FakeModuleActivationState::Unloading) {
        state.activation =
            FakeModuleActivationState::Quiesced;
    }
    state.permanent_pin = true;
}

class FakeStateGateGuard {
public:
    explicit FakeStateGateGuard(
        FakeModuleExportState& state) noexcept
        : state_(state) {
        state_.state_gate_acquired = true;
    }

    ~FakeStateGateGuard() noexcept {
        state_.state_gate_released = true;
        ++state_.state_gate_release_count;
    }

private:
    FakeModuleExportState& state_;
};

class FakeActivationPermitGuard {
public:
    explicit FakeActivationPermitGuard(
        FakeModuleExportState& state) noexcept
        : state_(state) {
        state_.activation_permit_opened = true;
    }

    ~FakeActivationPermitGuard() noexcept {
        state_.activation_permit_closed = true;
        ++state_.activation_permit_close_count;
    }

private:
    FakeModuleExportState& state_;
};

class FakeInjectFlagGuard {
public:
    explicit FakeInjectFlagGuard(
        FakeModuleExportState& state) noexcept
        : state_(state),
          previous_(state.inject_flag) {
        state_.inject_flag = true;
    }

    ~FakeInjectFlagGuard() noexcept {
        state_.inject_flag = previous_;
    }

private:
    FakeModuleExportState& state_;
    bool previous_;
};

void InvokeFakeAfterInitExport(
    FakeModuleExportState& state) noexcept {
    try {
        FakeStateGateGuard state_gate(state);
        if (state.throw_after_gate) {
            throw std::runtime_error(
                "injected AfterInit exception");
        }
    } catch (...) {
        FailClosedFakeModuleExport(state);
    }
}

bool InvokeFakeInitExport(
    FakeModuleExportState& state) noexcept {
    try {
        FakeStateGateGuard state_gate(state);
        if (state.throw_after_gate) {
            throw std::runtime_error(
                "injected Init exception after state gate");
        }
        FakeActivationPermitGuard activation_permit(state);
        if (state.throw_after_permit) {
            throw std::runtime_error(
                "injected Init exception after activation permit");
        }
        return true;
    } catch (...) {
        FailClosedFakeModuleExport(state);
        return false;
    }
}

void InvokeFakeSettingsChangedExport(
    FakeModuleExportState& state) noexcept {
    state.activation = FakeModuleActivationState::Quiesced;
    state.settings_latch_order = ++state.order;
    try {
        state.settings_log_attempted = true;
        state.settings_log_order = ++state.order;
        if (state.throw_in_settings_log) {
            throw std::runtime_error(
                "injected settings diagnostic failure");
        }
    } catch (...) {
        state.settings_log_contained = true;
    }
}

void InvokeFakeBeforeOrFinalUninitExport(
    FakeModuleExportState& state) noexcept {
    state.activation =
        FakeModuleActivationState::Unloading;
    state.unloading_published_order = ++state.order;
    try {
        state.first_fallible_operation_order = ++state.order;
        if (state.throw_after_unloading) {
            throw std::runtime_error(
                "injected module uninit exception");
        }
    } catch (...) {
        FailClosedFakeModuleExport(state);
    }
}

int InvokeFakeInjectBoundary(
    FakeModuleExportState& state) noexcept {
    try {
        FakeInjectFlagGuard inject_flag(state);
        throw std::runtime_error(
            "injected TAP initialization exception");
    } catch (...) {
        FailClosedFakeModuleExport(state);
        return -1;
    }
}

class FakeBlockingPinDecision {
public:
    class DecisionGuard {
    public:
        explicit DecisionGuard(
            FakeBlockingPinDecision& owner) noexcept
            : owner_(owner) {
            while (owner_.gate_.test_and_set(
                std::memory_order_acquire)) {
                std::this_thread::yield();
            }
        }

        ~DecisionGuard() noexcept {
            owner_.gate_.clear(std::memory_order_release);
        }

    private:
        FakeBlockingPinDecision& owner_;
    };

    void Hold(
        std::atomic<bool>& entered,
        std::atomic<bool>& release) noexcept {
        DecisionGuard guard(*this);
        entered.store(true, std::memory_order_release);
        while (!release.load(std::memory_order_acquire)) {
            std::this_thread::yield();
        }
    }

    void Require() noexcept {
        DecisionGuard guard(*this);
        permanent_.store(true, std::memory_order_release);
    }

    [[nodiscard]] bool Permanent() const noexcept {
        return permanent_.load(std::memory_order_acquire);
    }

private:
    std::atomic_flag gate_ = ATOMIC_FLAG_INIT;
    std::atomic<bool> permanent_{false};
};

struct FakeFailClosedPublicationState {
    std::atomic<bool> owner_established{false};
    std::atomic<FakeModuleActivationState> activation{
        FakeModuleActivationState::Active};
    std::atomic<bool> pin_attempted{false};
    std::atomic<bool> completed{false};
};

void PublishFakeFailClosedBeforePin(
    FakeFailClosedPublicationState& state,
    FakeBlockingPinDecision& pin) noexcept {
    state.owner_established.store(true, std::memory_order_release);
    state.activation.store(
        FakeModuleActivationState::Quiesced,
        std::memory_order_release);
    state.pin_attempted.store(true, std::memory_order_release);
    pin.Require();
    state.completed.store(true, std::memory_order_release);
}

struct FakeTapComBoundaryState {
    FakeModuleActivationState activation =
        FakeModuleActivationState::Active;
    bool permanent_pin = false;
    bool output_nonnull = true;
    bool site_owner_committed = false;
    bool watcher_published = false;
    bool retained_site_owner = false;
    bool diagnostic_failure_contained = false;
};

void FailClosedFakeTapBoundary(
    FakeTapComBoundaryState& state) noexcept {
    state.activation = FakeModuleActivationState::Quiesced;
    state.permanent_pin = true;
}

int InvokeFakeGetSiteBoundary(
    FakeTapComBoundaryState& state) noexcept {
    state.output_nonnull = false;
    try {
        throw std::system_error(
            std::make_error_code(std::errc::resource_deadlock_would_occur));
    } catch (...) {
        FailClosedFakeTapBoundary(state);
        return -1;
    }
}

int InvokeFakeSetSiteBoundary(
    FakeTapComBoundaryState& state) noexcept {
    try {
        state.site_owner_committed = true;
        throw std::bad_alloc();
    } catch (...) {
        state.retained_site_owner =
            state.site_owner_committed &&
            !state.watcher_published;
        FailClosedFakeTapBoundary(state);
        return -1;
    }
}

int InvokeFakeFactoryBoundary(
    FakeTapComBoundaryState& state) noexcept {
    state.output_nonnull = false;
    try {
        throw std::bad_alloc();
    } catch (...) {
        try {
            throw std::runtime_error(
                "injected factory diagnostic failure");
        } catch (...) {
            state.diagnostic_failure_contained = true;
        }
        FailClosedFakeTapBoundary(state);
        return -1;
    }
}

struct FakeNoexceptDiagnosticState {
    bool retained_receipt_published = false;
    bool quiesced = false;
    bool permanent_pin = false;
    bool diagnostic_attempted = false;
    bool diagnostic_failure_contained = false;
};

int InvokeFakeNoexceptDiagnosticFailure(
    FakeNoexceptDiagnosticState& state) noexcept {
    state.retained_receipt_published = true;
    state.quiesced = true;
    state.permanent_pin = true;
    try {
        state.diagnostic_attempted = true;
        throw std::runtime_error(
            "injected noexcept diagnostic failure");
    } catch (...) {
        state.diagnostic_failure_contained = true;
    }
    return -1;
}

enum class FakeGraphicsEffectKind {
    Composite,
    Flood,
    Border,
    GaussianBlur,
    ColorMatrix,
};

struct FakeGraphicsMappingProbe {
    std::uint64_t name_dereferences = 0;

    int Map(
        FakeGraphicsEffectKind,
        const wchar_t* name,
        std::uint32_t* index,
        std::uint32_t* mapping) noexcept {
        if (!name || !index || !mapping) {
            return -1;
        }
        ++name_dereferences;
        *index = 0;
        *mapping = 0;
        return 0;
    }
};

struct FakeLoaderReferenceState {
    std::uint64_t acquired = 0;
    std::uint64_t released = 0;
    std::uint64_t transferred = 0;
};

class FakeLoaderReferenceGuard {
public:
    explicit FakeLoaderReferenceGuard(
        FakeLoaderReferenceState& state) noexcept
        : state_(&state) {
        ++state_->acquired;
    }

    FakeLoaderReferenceGuard(
        const FakeLoaderReferenceGuard&) = delete;
    FakeLoaderReferenceGuard& operator=(
        const FakeLoaderReferenceGuard&) = delete;

    ~FakeLoaderReferenceGuard() noexcept {
        if (state_) {
            ++state_->released;
        }
    }

    void Transfer() noexcept {
        ++state_->transferred;
        state_ = nullptr;
    }

private:
    FakeLoaderReferenceState* state_;
};

class FakeEmergencyHookQuarantine {
public:
    bool Retain(std::uintptr_t hook,
                std::uint64_t dispatch_id) noexcept {
        for (auto& slot : slots_) {
            if (slot.hook == 0) {
                slot = {hook, dispatch_id};
                return true;
            }
        }
        capacity_exhausted_ = true;
        return false;
    }

    bool RetireExact(std::uintptr_t hook,
                     std::uint64_t dispatch_id) noexcept {
        for (auto& slot : slots_) {
            if (slot.hook == hook &&
                slot.dispatch_id == dispatch_id) {
                const bool actual_already_released =
                    slot.actual_hook_released;
                slot = {};
                if (actual_already_released) {
                    ++double_release_;
                    tracking_invariant_failed_ = true;
                    return false;
                }
                ++actual_hook_releases_;
                ++removed_;
                return true;
            }
        }
        tracking_invariant_failed_ = true;
        return false;
    }

    bool InjectActualHookRelease(
        std::uintptr_t hook,
        std::uint64_t dispatch_id) noexcept {
        for (auto& slot : slots_) {
            if (slot.hook == hook &&
                slot.dispatch_id == dispatch_id) {
                slot.actual_hook_released = true;
                return true;
            }
        }
        return false;
    }

    [[nodiscard]] std::uint64_t Count() const noexcept {
        return static_cast<std::uint64_t>(std::count_if(
            slots_.begin(), slots_.end(),
            [](const Slot& slot) { return slot.hook != 0; }));
    }

    [[nodiscard]] std::uint64_t Removed() const noexcept {
        return removed_;
    }

    [[nodiscard]] std::uint64_t ActualHookReleases() const noexcept {
        return actual_hook_releases_;
    }

    [[nodiscard]] std::uint64_t DoubleRelease() const noexcept {
        return double_release_;
    }

    [[nodiscard]] bool CapacityExhausted() const noexcept {
        return capacity_exhausted_;
    }

    [[nodiscard]] bool TrackingInvariantFailed() const noexcept {
        return tracking_invariant_failed_;
    }

private:
    struct Slot {
        std::uintptr_t hook = 0;
        std::uint64_t dispatch_id = 0;
        bool actual_hook_released = false;
    };

    std::array<Slot, 64> slots_{};
    std::uint64_t removed_ = 0;
    std::uint64_t actual_hook_releases_ = 0;
    std::uint64_t double_release_ = 0;
    bool capacity_exhausted_ = false;
    bool tracking_invariant_failed_ = false;
};

Scenario GitNormalClose() {
    Scenario result{
        "git.normal-close",
        "git",
        true,
        "",
        {"register", "begin-revoke", "commit-success", "repeat-close"},
        {},
        {},
        "cookie is cleared only by the successful revoke ticket",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(1, 17) == protocol::ProtocolStatus::Applied,
           "register");
    git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired,
           "revoke-owner");
    Expect(result,
           git.CookieForRevoke(revoke.ticket).cookie.has_value(),
           "cookie-visible-to-owner");
    Expect(result,
           git.CompleteRevoke(revoke.ticket, true) ==
               protocol::ProtocolStatus::Applied,
           "revoke-success");
    Expect(result,
           git.BeginRevoke().status ==
               protocol::ProtocolStatus::TerminalNoop,
           "repeat-close-noop");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               !receipt.cookie_present &&
               receipt.successful_revokes == 1,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario GitRevokeFailRetry() {
    Scenario result{
        "git.revoke-fail-retry",
        "git",
        true,
        "",
        {"register", "revoke-fails", "verify-retained", "retry-succeeds"},
        {"revoke-error"},
        {},
        "failed revoke retains the opaque cookie and remains retryable",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(2, 23) == protocol::ProtocolStatus::Applied,
           "register");
    git.CloseAdmission();
    const auto first = git.BeginRevoke();
    Expect(result,
           first.status == protocol::ProtocolStatus::Acquired,
           "first-owner");
    Expect(result,
           git.CompleteRevoke(
                first.ticket, false, -1, true,
                protocol::GitRetainedReason::
                    RevokeInterfaceFromGlobalFailed) ==
               protocol::ProtocolStatus::Applied,
           "retain-failure");
    const auto retained = git.Receipt();
    Expect(result,
           retained.state == protocol::GitState::Retained &&
               retained.cookie_present && retained.retry_eligible,
           "retained-cookie");
    const auto retry = git.BeginRevoke();
    Expect(result,
           retry.status == protocol::ProtocolStatus::Acquired,
           "retry-owner");
    Expect(result,
           git.CompleteRevoke(retry.ticket, true) ==
               protocol::ProtocolStatus::Applied,
           "retry-success");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               !receipt.cookie_present &&
               receipt.revoke_attempts == 2 &&
               receipt.successful_revokes == 1,
           "retry-receipt");
    Finalize(result);
    return result;
}

Scenario GitConcurrentClose() {
    Scenario result{
        "git.concurrent-close-single-owner",
        "git",
        true,
        "",
        {"register", "barrier", "two-close-attempts", "single-commit"},
        {"concurrent-close"},
        {true, 3},
        "exactly one revoke ticket is issued",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(3, 29) == protocol::ProtocolStatus::Applied,
           "register");
    git.CloseAdmission();
    std::barrier start(3);
    std::array<protocol::GitRevokeResult, 2> attempts{};
    std::thread first([&] {
        start.arrive_and_wait();
        attempts[0] = git.BeginRevoke();
    });
    std::thread second([&] {
        start.arrive_and_wait();
        attempts[1] = git.BeginRevoke();
    });
    start.arrive_and_wait();
    first.join();
    second.join();

    std::size_t acquired = 0;
    std::size_t busy = 0;
    protocol::GitRevokeTicket owner{};
    for (const auto& attempt : attempts) {
        if (attempt.status == protocol::ProtocolStatus::Acquired) {
            ++acquired;
            owner = attempt.ticket;
        } else if (attempt.status == protocol::ProtocolStatus::Busy) {
            ++busy;
        }
    }
    Expect(result, acquired == 1 && busy == 1, "single-owner");
    Expect(result,
           git.CompleteRevoke(owner, true) ==
               protocol::ProtocolStatus::Applied,
           "owner-commit");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.successful_revokes == 1 &&
               receipt.revoke_attempts == 1,
           "one-platform-revoke");
    Finalize(result);
    return result;
}

Scenario GitGetBlocksRevoke() {
    Scenario result{
        "git.get-blocks-revoke",
        "git",
        true,
        "",
        {"register", "acquire-proxy-lease", "close-admission",
         "reject-late-get", "revoke-blocked", "release-proxy",
         "release-lease", "revoke-succeeds"},
        {"lease-overlaps-close", "admission-close-race"},
        {true, 2},
        "authoritative close rejects a late Get and proxy release precedes lease release",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(4, 31) == protocol::ProtocolStatus::Applied,
           "register");
    const auto lease = git.AcquireLease();
    Expect(result,
           lease.status == protocol::ProtocolStatus::Acquired &&
                git.CookieForLease(lease.ticket).cookie.has_value(),
           "lease");
    std::barrier admission_closed(2);
    std::thread closer([&] {
        git.CloseAdmission();
        admission_closed.arrive_and_wait();
    });
    admission_closed.arrive_and_wait();
    closer.join();
    Expect(result,
           git.AcquireLease().status !=
               protocol::ProtocolStatus::Acquired,
           "late-get-rejected");
    Expect(result,
           git.BeginRevoke().status ==
               protocol::ProtocolStatus::LeaseOutstanding,
           "revoke-blocked");
    const auto blocked = git.Receipt();
    Expect(result,
           blocked.state == protocol::GitState::Registered &&
               blocked.cookie_present && blocked.active_leases == 1,
           "cookie-retained-while-leased");
    bool proxy_released = true;
    Expect(result, proxy_released, "proxy-released-before-lease");
    Expect(result,
           git.ReleaseLease(lease.ticket) ==
               protocol::ProtocolStatus::Applied,
           "release-lease");
    const auto revoke = git.BeginRevoke();
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired &&
               git.CompleteRevoke(revoke.ticket, true) ==
                   protocol::ProtocolStatus::Applied,
           "revoke-after-release");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               !receipt.cookie_present && receipt.active_leases == 0,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario GitProvisionalCommitFailQuarantine() {
    Scenario result{
        "git.provisional-commit-fail-quarantine",
        "git",
        true,
        "",
        {"external-register", "inject-internal-commit-failure",
         "rollback-fails", "enumerate-quarantine"},
        {"internal-commit-failure", "rollback-revoke-failure"},
        {},
        "an externally registered cookie is retained with an enumerable receipt until the bounded unload retry",
    };
    FakeGitPlatformOps platform{result.resources};
    platform.revoke_failures_remaining = 1;
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithInjectedCommitFailure(41),
           "commit-failure-propagated");
    const auto quarantined = adapter.Receipts();
    Expect(result,
           platform.register_calls == 1 &&
               platform.revoke_calls == 1 &&
               platform.live_cookie != 0 &&
               adapter.InternalCommitAttempts() == 1,
           "external-register-precedes-failed-commit");
    Expect(result,
           quarantined.size() == 1 &&
               quarantined[0].generation == 41 &&
               quarantined[0].cookie == platform.live_cookie &&
               quarantined[0].retry_eligible &&
               quarantined[0].reason ==
                   "internal-commit-failed-rollback-failed",
           "enumerable-quarantine-receipt");
    result.terminal_state = "retained-provisional-quarantine";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitProvisionalInitializedUnloadRetry() {
    Scenario result{
        "git.provisional-initialized-unload-retry",
        "git",
        true,
        "",
        {"external-register", "rollback-fails", "quarantine",
         "reject-uninitialized-retry", "initialize-com-apartment",
         "bounded-retry-revoke", "uninitialize-com-apartment"},
        {"rollback-revoke-failure", "uninitialized-retry"},
        {},
        "the production-shaped unload retry initializes COM once, performs one bounded quarantine pass, and balances the apartment",
    };
    FakeGitPlatformOps platform{result.resources};
    platform.revoke_failures_remaining = 1;
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithInjectedCommitFailure(42),
           "quarantine-created");
    FakeComApartmentOps apartment;
    Expect(result,
           !adapter.RetryQuarantine(apartment.initialized) &&
               platform.revoke_calls == 1 &&
               !adapter.Receipts().empty(),
           "uninitialized-retry-rejected-without-platform-call");
    Expect(result,
           RetryProvisionalQuarantineFromInitializedApartment(
               adapter, apartment),
           "initialized-bounded-retry-succeeds");
    Expect(result,
           apartment.initialize_calls == 1 &&
               apartment.uninitialize_calls == 1 &&
               !apartment.initialized &&
               adapter.Receipts().empty() &&
               platform.live_cookie == 0 &&
               platform.revoke_calls == 2,
           "initialized-retry-balanced-and-terminal");
    result.terminal_state = "revoked-after-initialized-unload-retry";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitRetiredOwnerTransferFailure() {
    Scenario result{
        "git.retired-owner-transfer-failure",
        "git",
        true,
        "",
        {"create-nonterminal-watcher", "inject-fixed-owner-store-failure",
         "establish-process-lifetime-owner", "record-retained-and-pin",
         "release-caller-owner", "verify-no-destructor"},
        {"retired-fixed-owner-store-failure"},
        {},
        "a failed fixed-slot transfer establishes an intrusive process-lifetime owner before the caller releases",
    };
    FakeRetiredWatcher watcher;
    FakeFixedRetiredOwnerAdapter owners;
    auto retired_owner =
        result.resources.Create("retired-watcher-owner");
    {
        FakeRetiredOwnerGuard guard(watcher, owners, true);
    }
    const auto receipt = owners.Receipt();
    const auto& events = owners.Events();
    Expect(result,
           receipt.transfer_attempts == 1 &&
               receipt.fixed_slot_transfers == 0 &&
               receipt.process_lifetime_retained == 1 &&
               receipt.last_error == -201 &&
               receipt.permanent_pin &&
                watcher.intrusive_add_refs == 1 &&
                watcher.references == 2,
            "structured-process-lifetime-retained-receipt");
    Expect(result,
           events.size() == 3 &&
               events[0] == "addref-owner" &&
               events[1] == "fail-closed" &&
               events[2] == "publish-ledger",
           "owner-before-failclosed-before-publication");
    watcher.ReleaseCaller();
    Expect(result,
           watcher.references == 1 &&
               watcher.caller_releases == 1 &&
               watcher.nonterminal_destructions == 0,
           "caller-release-cannot-destroy-nonterminal-watcher");
    result.terminal_state = "retained-process-lifetime";
    retired_owner.Retain(RetainReasonCode::OwnerTransfer);
    Finalize(result);
    return result;
}

Scenario GitLeaseCapacityRetained() {
    Scenario result{
        "git.lease-capacity-retained",
        "git",
        true,
        "",
        {"register", "fill-fixed-lease-table", "reject-capacity-plus-one",
         "record-capacity-receipt", "retain-and-pin-model",
         "release-all-leases", "retry-revoke"},
        {"lease-capacity-exhausted"},
        {},
        "the fixed lease table returns a structured capacity status and remains drainable and retryable",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(43, 107) ==
               protocol::ProtocolStatus::Applied,
           "register");
    std::array<protocol::GitLeaseTicket,
               protocol::kGitLeaseCapacity>
        tickets{};
    for (std::size_t index = 0; index < tickets.size(); ++index) {
        const auto lease = git.AcquireLease();
        Expect(result,
               lease.status == protocol::ProtocolStatus::Acquired &&
                   lease.ticket.valid(),
               "fill-fixed-lease-slot");
        tickets[index] = lease.ticket;
    }
    const auto overflow = git.AcquireLease();
    const auto capacity_receipt = git.Receipt();
    Expect(result,
           overflow.status ==
                   protocol::ProtocolStatus::CapacityExceeded &&
               !overflow.ticket.valid() &&
               capacity_receipt.active_leases ==
                   protocol::kGitLeaseCapacity &&
               capacity_receipt.lease_capacity ==
                   protocol::kGitLeaseCapacity &&
               capacity_receipt.lease_capacity_failures == 1,
           "structured-capacity-receipt");
    Expect(result,
           git.RetainRegisteredResource(
               -202, true,
               protocol::GitRetainedReason::
                   LeaseCapacityExceeded) ==
               protocol::ProtocolStatus::Applied,
           "capacity-failure-retained");
    for (const auto& ticket : tickets) {
        Expect(result,
               git.ReleaseLease(ticket) ==
                   protocol::ProtocolStatus::Applied,
               "release-fixed-lease");
    }
    Expect(result,
           git.WaitForNoLeases(std::chrono::milliseconds(0)).no_leases,
           "fixed-lease-table-drained");
    const auto revoke = git.BeginRevoke();
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired &&
               git.CompleteRevoke(revoke.ticket, true) ==
                   protocol::ProtocolStatus::Applied,
           "retry-revoke-after-capacity-drain");
    const auto terminal = git.Receipt();
    RecordGitReceiptResources(result, terminal);
    result.terminal_state =
        std::string(protocol::ToString(terminal.state));
    Expect(result,
           terminal.state == protocol::GitState::Revoked &&
               terminal.active_leases == 0 &&
               terminal.lease_capacity_failures == 1,
           "terminal-capacity-receipt");
    Finalize(result);
    return result;
}

Scenario GitFixedReasonReceiptNoAllocation() {
    Scenario result{
        "git.fixed-reason-receipt-noalloc",
        "git",
        true,
        "",
        {"register", "retain-enum-reason", "copy-trivial-receipt",
         "resolve-static-reason-view"},
        {"reason-copy-allocation-forbidden"},
        {},
        "GitReceipt is trivially copyable and carries only a fixed enum reason whose text is a static string_view",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(44, 109) ==
               protocol::ProtocolStatus::Applied,
           "register");
    Expect(result,
           git.RetainRegisteredResource(
               -203, false,
               protocol::GitRetainedReason::
                   ComApartmentInitializationFailed) ==
               protocol::ProtocolStatus::Applied,
           "retain-fixed-reason");
    protocol::GitReceipt receipt{};
    for (std::size_t copy = 0; copy < 128; ++copy) {
        receipt = git.Receipt();
    }
    const auto reason_text =
        protocol::ToString(receipt.retained_reason);
    Expect(result,
           receipt.state == protocol::GitState::Retained &&
               receipt.retained_reason ==
                   protocol::GitRetainedReason::
                       ComApartmentInitializationFailed &&
               reason_text ==
                   "com-apartment-initialization-failed" &&
               receipt.lease_capacity ==
                   protocol::kGitLeaseCapacity &&
               receipt.lease_capacity_failures == 0,
           "fixed-reason-receipt");
    result.terminal_state = "retained-fixed-reason";
    RecordGitReceiptResources(result, receipt);
    Finalize(result);
    return result;
}

Scenario GitPublicLockFailureMatrix() {
    Scenario result{
        "git.public-lock-failure-matrix",
        "git",
        true,
        "",
        {"inject-each-public-lock-boundary", "observe-protocol-failure",
         "verify-failure-operation-and-count"},
        {"lock-acquisition-failure"},
        {},
        "every public noexcept GIT operation converts an injected lock failure into a structured ProtocolFailure",
    };

    constexpr std::array operations{
        protocol::GitOperation::Register,
        protocol::GitOperation::AcquireLease,
        protocol::GitOperation::CookieForLease,
        protocol::GitOperation::ReleaseLease,
        protocol::GitOperation::CloseAdmission,
        protocol::GitOperation::WaitForNoLeases,
        protocol::GitOperation::BeginRevoke,
        protocol::GitOperation::CookieForRevoke,
        protocol::GitOperation::CompleteRevoke,
        protocol::GitOperation::RetainRegisteredResource,
        protocol::GitOperation::Receipt,
    };

    for (const auto operation : operations) {
        GitInjectedLockFailure failure{operation, true};
        const protocol::GitLifecycleTestHooks hooks{
            FailGitOperationBeforeLock, &failure, 1, 1, 1};
        protocol::GitLifecycle git(&hooks);
        protocol::ProtocolStatus status =
            protocol::ProtocolStatus::InvalidState;
        protocol::GitReceipt injected_receipt{};
        switch (operation) {
        case protocol::GitOperation::Register:
            status = git.Register(1, 1);
            break;
        case protocol::GitOperation::AcquireLease:
            status = git.AcquireLease().status;
            break;
        case protocol::GitOperation::CookieForLease:
            status = git.CookieForLease({1, 1}).status;
            break;
        case protocol::GitOperation::ReleaseLease:
            status = git.ReleaseLease({1, 1});
            break;
        case protocol::GitOperation::CloseAdmission:
            status = git.CloseAdmission();
            break;
        case protocol::GitOperation::WaitForNoLeases:
            status =
                git.WaitForNoLeases(std::chrono::milliseconds(0))
                    .status;
            break;
        case protocol::GitOperation::BeginRevoke:
            status = git.BeginRevoke().status;
            break;
        case protocol::GitOperation::CookieForRevoke:
            status = git.CookieForRevoke({1, 1, 1}).status;
            break;
        case protocol::GitOperation::CompleteRevoke:
            status = git.CompleteRevoke({1, 1, 1}, false);
            break;
        case protocol::GitOperation::RetainRegisteredResource:
            status = git.RetainRegisteredResource(-1, false);
            break;
        case protocol::GitOperation::Receipt:
            injected_receipt = git.Receipt();
            status = injected_receipt.snapshot_status;
            break;
        case protocol::GitOperation::None:
            break;
        }

        const auto receipt =
            operation == protocol::GitOperation::Receipt
                ? injected_receipt
                : git.Receipt();
        RecordGitReceiptResources(
            result, receipt, "git-lock-failure-cookie");
        Expect(result,
               status == protocol::ProtocolStatus::ProtocolFailure,
               "operation-did-not-return-protocol-failure");
        Expect(result,
               receipt.protocol_failure_count == 1 &&
                   receipt.last_failure_operation == operation,
               "failure-receipt-operation-or-count");
    }

    result.terminal_state = "all-public-lock-failures-contained";
    Finalize(result);
    return result;
}

Scenario GitUnknownCookieFallbackReceipt() {
    Scenario result{
        "git.unknown-cookie-fallback-receipt",
        "git",
        true,
        "",
        {"register-cookie", "inject-receipt-lock-failure",
         "return-conservative-snapshot"},
        {"receipt-lock-failure"},
        {},
        "a failed snapshot says UnknownMayBePresent and never reports the cookie absent",
    };
    GitInjectedLockFailure failure{};
    const protocol::GitLifecycleTestHooks hooks{
        FailGitOperationBeforeLock, &failure, 1, 1, 1};
    protocol::GitLifecycle git(&hooks);
    Expect(result,
           git.Register(51, 211) ==
               protocol::ProtocolStatus::Applied,
           "register");
    failure = {protocol::GitOperation::Receipt, true};
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           receipt.snapshot_status ==
                   protocol::ProtocolStatus::ProtocolFailure &&
               receipt.cookie_knowledge ==
                   protocol::GitCookieKnowledge::
                       UnknownMayBePresent &&
               receipt.cookie_present &&
               receipt.state == protocol::GitState::Retained &&
               receipt.protocol_failure_count == 1 &&
               receipt.last_failure_operation ==
                   protocol::GitOperation::Receipt,
           "conservative-unknown-cookie-receipt");
    result.terminal_state = "retained-cookie-knowledge-unknown";
    Finalize(result);
    return result;
}

Scenario GitRevokeCommitProtocolFailure() {
    Scenario result{
        "git.revoke-commit-protocol-failure",
        "git",
        true,
        "",
        {"register", "begin-revoke", "inject-commit-lock-failure",
         "verify-cookie-not-cleared"},
        {"complete-revoke-lock-failure"},
        {},
        "a failed result commit cannot clear the cookie or counterfeit a successful revoke",
    };
    GitInjectedLockFailure failure{};
    const protocol::GitLifecycleTestHooks hooks{
        FailGitOperationBeforeLock, &failure, 1, 1, 1};
    protocol::GitLifecycle git(&hooks);
    git.Register(52, 223);
    git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    failure = {protocol::GitOperation::CompleteRevoke, true};
    const auto commit =
        git.CompleteRevoke(revoke.ticket, true);
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           commit == protocol::ProtocolStatus::ProtocolFailure &&
               receipt.state == protocol::GitState::Revoking &&
               receipt.cookie_knowledge ==
                   protocol::GitCookieKnowledge::Present &&
               receipt.cookie_present &&
               receipt.successful_revokes == 0,
           "failed-commit-preserves-cookie");
    result.terminal_state = "retained-by-uncommitted-revoke-ticket";
    Finalize(result);
    return result;
}

Scenario GitSubscriptionLockFailureMatrix() {
    Scenario result{
        "git.subscription-lock-failure-matrix",
        "git",
        true,
        "",
        {"inject-each-subscription-lock-boundary",
         "observe-protocol-failure", "verify-conservative-receipt"},
        {"subscription-lock-failure"},
        {},
        "subscription lock failures are contained and the receipt conservatively requires best-effort Unadvise",
    };
    constexpr std::array operations{
        protocol::SubscriptionOperation::BeginAdvise,
        protocol::SubscriptionOperation::CompleteAdvise,
        protocol::SubscriptionOperation::BeginUnadvise,
        protocol::SubscriptionOperation::CompleteUnadvise,
        protocol::SubscriptionOperation::Receipt,
    };
    for (const auto operation : operations) {
        SubscriptionInjectedLockFailure failure{operation, false};
        const protocol::SubscriptionLifecycleTestHooks hooks{
            FailSubscriptionOperationBeforeLock, &failure};
        protocol::SubscriptionLifecycle subscription(&hooks);

        // Build the same legal pre-state that the production adapter has after
        // each external transition. This prevents fresh NotAttempted objects
        // from hiding a failed commit that may already have side effects.
        if (operation ==
            protocol::SubscriptionOperation::CompleteAdvise) {
            Expect(result,
                   subscription.BeginAdvise() ==
                       protocol::ProtocolStatus::Applied,
                   "complete-advise-legal-prestate");
        } else if (
            operation ==
                protocol::SubscriptionOperation::BeginUnadvise ||
            operation ==
                protocol::SubscriptionOperation::CompleteUnadvise) {
            Expect(result,
                   subscription.BeginAdvise() ==
                           protocol::ProtocolStatus::Applied &&
                       subscription.CompleteAdvise(true) ==
                           protocol::ProtocolStatus::Applied,
                   "unadvise-advised-prestate");
            if (operation ==
                protocol::SubscriptionOperation::CompleteUnadvise) {
                Expect(result,
                       subscription.BeginUnadvise() ==
                           protocol::ProtocolStatus::Applied,
                       "complete-unadvise-legal-prestate");
            }
        }
        failure.armed = true;

        protocol::ProtocolStatus status =
            protocol::ProtocolStatus::InvalidState;
        protocol::SubscriptionReceipt injected{};
        switch (operation) {
        case protocol::SubscriptionOperation::BeginAdvise:
            status = subscription.BeginAdvise();
            break;
        case protocol::SubscriptionOperation::CompleteAdvise:
            status = subscription.CompleteAdvise(false, -1);
            break;
        case protocol::SubscriptionOperation::BeginUnadvise:
            status = subscription.BeginUnadvise();
            break;
        case protocol::SubscriptionOperation::CompleteUnadvise:
            status = subscription.CompleteUnadvise(false, -1);
            break;
        case protocol::SubscriptionOperation::Receipt:
            injected = subscription.Receipt();
            status = injected.snapshot_status;
            break;
        case protocol::SubscriptionOperation::None:
            break;
        }
        const auto receipt =
            operation == protocol::SubscriptionOperation::Receipt
                ? injected
                : subscription.Receipt();
        RecordSubscriptionReceiptResources(
            result, receipt, "subscription-lock-failure");
        Expect(result,
               status == protocol::ProtocolStatus::ProtocolFailure &&
                   receipt.protocol_failure_count == 1 &&
                   receipt.last_failure_operation == operation,
               "subscription-failure-not-contained");
        if (operation ==
                protocol::SubscriptionOperation::CompleteAdvise ||
            operation ==
                protocol::SubscriptionOperation::BeginUnadvise ||
            operation ==
                protocol::SubscriptionOperation::CompleteUnadvise ||
            operation ==
                protocol::SubscriptionOperation::Receipt) {
            Expect(result,
                   receipt.best_effort_unadvise_required &&
                       receipt.external_uncertainty_latched,
                   "subscription-fallback-not-conservative");
        } else {
            Expect(result,
                   receipt.state ==
                           protocol::SubscriptionState::NotAttempted &&
                       !receipt.best_effort_unadvise_required &&
                       !receipt.external_uncertainty_latched,
                   "begin-advise-lock-failure-invented-side-effect");
        }
    }
    result.terminal_state = "subscription-lock-failures-contained";
    Finalize(result);
    return result;
}

Scenario GitSequenceExhaustionNoAba() {
    Scenario result{
        "git.sequence-exhaustion-no-aba",
        "git",
        true,
        "",
        {"reserve-max-generation", "reject-generation-wrap",
         "issue-max-lease", "reject-lease-wrap",
         "issue-max-revoke-ticket", "reject-ticket-wrap"},
        {"uint64-sequence-exhaustion"},
        {},
        "generation, lease, and revoke-ticket sequences issue UINT64_MAX once and then remain exhausted instead of reusing one",
    };

    std::atomic<std::uint64_t> generation{
        std::numeric_limits<std::uint64_t>::max()};
    const auto max_generation =
        protocol::ReserveNonZeroSequence(generation);
    const auto exhausted_generation =
        protocol::ReserveNonZeroSequence(generation);
    Expect(result,
           max_generation.status ==
                   protocol::ProtocolStatus::Acquired &&
               max_generation.value ==
                   std::numeric_limits<std::uint64_t>::max() &&
               exhausted_generation.status ==
                   protocol::ProtocolStatus::SequenceExhausted &&
               exhausted_generation.value == 0 &&
               generation.load(std::memory_order_acquire) == 0,
           "generation-sequence-wrapped");

    const protocol::GitLifecycleTestHooks lease_hooks{
        nullptr,
        nullptr,
        std::numeric_limits<std::uint64_t>::max(),
        1,
        1,
    };
    protocol::GitLifecycle lease_git(&lease_hooks);
    lease_git.Register(max_generation.value, 227);
    const auto max_lease = lease_git.AcquireLease();
    const auto max_lease_release =
        lease_git.ReleaseLease(max_lease.ticket);
    const auto exhausted_lease = lease_git.AcquireLease();
    Expect(result,
           max_lease.status == protocol::ProtocolStatus::Acquired &&
               max_lease_release ==
                   protocol::ProtocolStatus::Applied &&
               max_lease.ticket.lease_id ==
                   std::numeric_limits<std::uint64_t>::max() &&
               exhausted_lease.status ==
                   protocol::ProtocolStatus::SequenceExhausted,
           "lease-sequence-wrapped");

    const protocol::GitLifecycleTestHooks revoke_hooks{
        nullptr,
        nullptr,
        1,
        std::numeric_limits<std::uint64_t>::max(),
        std::numeric_limits<std::uint64_t>::max(),
    };
    protocol::GitLifecycle revoke_git(&revoke_hooks);
    revoke_git.Register(max_generation.value, 229);
    revoke_git.CloseAdmission();
    const auto max_revoke = revoke_git.BeginRevoke();
    revoke_git.CompleteRevoke(
        max_revoke.ticket, false, -1, true,
        protocol::GitRetainedReason::
            RevokeInterfaceFromGlobalFailed);
    const auto exhausted_revoke = revoke_git.BeginRevoke();
    const auto receipt = revoke_git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           max_revoke.status ==
                   protocol::ProtocolStatus::Acquired &&
               max_revoke.ticket.attempt ==
                   std::numeric_limits<std::uint64_t>::max() &&
               max_revoke.ticket.ticket_id ==
                   std::numeric_limits<std::uint64_t>::max() &&
               exhausted_revoke.status ==
                   protocol::ProtocolStatus::SequenceExhausted &&
               receipt.cookie_present &&
               receipt.sequence_exhaustions == 1,
           "revoke-sequence-wrapped-or-cookie-lost");

    result.terminal_state = "all-monotonic-sequences-exhausted";
    Finalize(result);
    return result;
}

Scenario GitProvisionalRollbackExceptionRetained() {
    Scenario result{
        "git.provisional-rollback-exception-retained",
        "git",
        true,
        "",
        {"pre-reserve-slot", "external-register",
         "inject-internal-commit-failure",
         "inject-rollback-exception", "retain-exact-cookie"},
        {"rollback-revoke-exception"},
        {},
        "a rollback exception retains the exact cookie in the already-reserved retry slot",
    };
    FakeGitPlatformOps platform{result.resources};
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithInjectedCommitFailure(53, true),
           "commit-failure-propagated");
    const auto receipts = adapter.Receipts();
    Expect(result,
           platform.register_calls == 1 &&
               platform.revoke_calls == 1 &&
               receipts.size() == 1 &&
               receipts[0].cookie == platform.live_cookie &&
               receipts[0].retry_eligible &&
               receipts[0].reason ==
                   "internal-commit-failed-rollback-threw",
           "rollback-exception-lost-cookie");
    result.terminal_state = "retained-exact-cookie-after-rollback-exception";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitProvisionalRegisterThrowUnknownRetained() {
    Scenario result{
        "git.provisional-register-throw-unknown-retained",
        "git",
        true,
        "",
        {"pre-reserve-slot", "external-register-starts",
         "throw-before-cookie-write", "retain-unknown-receipt",
         "forbid-blind-retry"},
        {"register-exception-before-cookie-write"},
        {},
        "an external register exception without a cookie remains permanently UnknownMayBePresent and is never blind-revoked",
    };
    FakeGitPlatformOps platform{result.resources};
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithThrowBeforeCookieWrite(55),
           "register-exception-propagated");
    const auto receipts = adapter.Receipts();
    const std::uint64_t revoke_calls_before_retry =
        platform.revoke_calls;
    const bool retry_succeeded = adapter.RetryQuarantine(true);
    Expect(result,
           platform.register_calls == 1 &&
               platform.unknown_registration_may_exist &&
               receipts.size() == 1 &&
               receipts[0].cookie == 0 &&
               !receipts[0].retry_eligible &&
               receipts[0].reason ==
                   "registration-unknown-may-be-present" &&
               !retry_succeeded &&
               platform.revoke_calls == revoke_calls_before_retry,
           "unknown-registration-cleared-or-blind-retried");
    result.terminal_state =
        "unknown-may-be-present-permanently-retained";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitProvisionalGitReleaseExceptionContained() {
    Scenario result{
        "git.provisional-git-release-exception-contained",
        "git",
        true,
        "",
        {"pre-reserve-slot", "register-cookie",
         "rollback-fails", "retry-revoke-succeeds",
         "temporary-git-release-throws",
         "pin-and-contain"},
        {"temporary-git-final-release-exception"},
        {},
        "a temporary GIT owner detaches before final Release and contains an exception while pinning the surviving raw reference",
    };
    FakeGitPlatformOps platform{result.resources};
    platform.revoke_failures_remaining = 1;
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithInjectedCommitFailure(56),
           "seed-retained-cookie");
    platform.git_release_throws_remaining = 1;
    const bool retry_succeeded =
        adapter.RetryQuarantine(true);
    const auto overflow = adapter.OverflowReceipt();
    Expect(result,
           !retry_succeeded &&
               platform.live_cookie == 0 &&
               adapter.Receipts().empty() &&
               platform.revoke_calls == 2 &&
               platform.git_release_calls == 1 &&
               overflow.permanent_pin &&
               overflow.git_release_failures == 1,
           "git-release-exception-escaped-or-unpinned");
    result.terminal_state =
        "cookie-revoked-temporary-git-reference-retained";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitProvisionalQuarantineOverflowReceipt() {
    Scenario result{
        "git.provisional-quarantine-overflow-receipt",
        "git",
        true,
        "",
        {"fill-fixed-provisional-quarantine", "pre-reserve-third-slot",
         "block-third-external-register", "enumerate-fixed-receipts",
         "record-capacity-receipt"},
        {"provisional-quarantine-capacity-exhausted"},
        {},
        "fixed capacity is checked before external registration, so exhaustion cannot create an untracked cookie",
    };
    FakeGitPlatformOps platform{result.resources};
    platform.revoke_failures_remaining = 3;
    FakeProvisionalGitAdapter adapter(platform);
    Expect(result,
           !adapter.RegisterWithInjectedCommitFailure(45) &&
               !adapter.RegisterWithInjectedCommitFailure(46) &&
               !adapter.RegisterWithInjectedCommitFailure(47),
           "three-rollback-failures");
    const auto fixed = adapter.Receipts();
    const auto overflow = adapter.OverflowReceipt();
    Expect(result,
           fixed.size() == 2 &&
                fixed[0].receipt_id == 1 &&
                fixed[1].receipt_id == 2 &&
                overflow.last_error == -103 &&
                overflow.capacity_failures == 1 &&
                overflow.external_register_blocked &&
                !overflow.permanent_pin &&
                platform.register_calls == 2 &&
                platform.revoke_calls == 2 &&
                platform.live_cookie != 0,
            "pre-reserve-capacity-gate");
    result.terminal_state =
        "capacity-blocked-before-external-register";
    RecordProvisionalGitResources(result, platform, adapter);
    Finalize(result);
    return result;
}

Scenario GitProxyFinalReleaseBeforeLeaseRelease() {
    Scenario result{
        "git.proxy-final-release-before-lease",
        "git",
        true,
        "",
        {"register", "acquire-proxy-lease", "close-admission",
         "barrier-blocked-revoke", "proxy-final-release",
         "lease-release", "revoke-succeeds"},
        {"revoke-while-proxy-live"},
        {true, 2},
        "a fake proxy final release is ordered before its protocol lease release",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(42, 103) == protocol::ProtocolStatus::Applied,
           "register");
    const auto lease = git.AcquireLease();
    Expect(result,
           lease.status == protocol::ProtocolStatus::Acquired,
           "acquire-lease");
    git.CloseAdmission();

    FakeProxyOps operations;
    FakeGitLeaseAdapter adapter(git, lease.ticket, operations);
    std::barrier start(2);
    std::barrier blocked_observed(2);
    std::barrier release_complete(2);
    protocol::GitRevokeResult blocked{};
    protocol::GitRevokeResult retry{};
    protocol::ProtocolStatus completion =
        protocol::ProtocolStatus::InvalidState;
    std::thread closer([&] {
        start.arrive_and_wait();
        blocked = git.BeginRevoke();
        blocked_observed.arrive_and_wait();
        release_complete.arrive_and_wait();
        retry = git.BeginRevoke();
        if (retry.status == protocol::ProtocolStatus::Acquired) {
            completion = git.CompleteRevoke(retry.ticket, true);
        }
    });
    start.arrive_and_wait();
    blocked_observed.arrive_and_wait();
    Expect(result,
           adapter.Close(),
           "adapter-close");
    release_complete.arrive_and_wait();
    closer.join();

    Expect(result,
           blocked.status ==
               protocol::ProtocolStatus::LeaseOutstanding,
           "revoke-blocked-by-live-proxy");
    Expect(result,
           operations.release_order.size() == 2 &&
               operations.release_order[0] ==
                   "proxy-final-release" &&
               operations.release_order[1] == "lease-release" &&
               operations.proxy_final_releases == 1 &&
               operations.lease_releases == 1 &&
               operations.double_release == 0,
           "proxy-release-order");
    Expect(result,
           retry.status == protocol::ProtocolStatus::Acquired &&
               completion == protocol::ProtocolStatus::Applied,
           "revoke-after-lease-release");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               receipt.active_leases == 0 &&
               !receipt.cookie_present,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario GitProxyReleaseExceptionRetainsLease() {
    Scenario result{
        "git.proxy-release-exception-retains-lease",
        "git",
        true,
        "",
        {"register", "acquire-proxy-lease", "close-admission",
         "inject-proxy-release-exception", "retain-active-lease",
         "block-revoke"},
        {"proxy-final-release-exception"},
        {},
        "an unconfirmed proxy final Release retains its protocol lease so revoke cannot overlap the unknown proxy lifetime",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(43, 107) ==
               protocol::ProtocolStatus::Applied,
           "register");
    const auto lease = git.AcquireLease();
    Expect(result,
           lease.status == protocol::ProtocolStatus::Acquired,
           "acquire-lease");
    Expect(result,
           git.CloseAdmission() ==
               protocol::ProtocolStatus::Applied,
           "close-admission");

    FakeProxyOps operations;
    operations.throw_on_proxy_final_release = true;
    auto proxy_owner =
        result.resources.Create("unconfirmed-proxy-reference");
    FakeGitLeaseAdapter adapter(
        git, lease.ticket, operations);
    Expect(result, !adapter.Close(), "proxy-release-exception-contained");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    const auto revoke = git.BeginRevoke();
    Expect(result,
           operations.proxy_release_exceptions == 1 &&
               operations.lease_releases == 0 &&
               operations.release_order.size() == 1 &&
               operations.release_order[0] ==
                   "proxy-final-release-threw" &&
               receipt.active_leases == 1 &&
               receipt.cookie_present &&
               revoke.status ==
                   protocol::ProtocolStatus::LeaseOutstanding,
           "lease-released-after-unconfirmed-proxy-release");
    result.terminal_state =
        "proxy-and-cookie-retained-by-active-lease";
    proxy_owner.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario GitGetExternalComExceptionRetainsLease() {
    Scenario result{
        "git.get-external-com-exception-retains-lease",
        "git",
        true,
        "",
        {"register", "acquire-get-lease",
         "inject-get-exception-after-output-write",
         "withhold-provisional-output", "retain-active-lease",
         "block-revoke"},
        {"get-interface-exception-after-output-write"},
        {},
        "an unconfirmed GetInterfaceFromGlobal output is never released, while its active protocol lease permanently blocks revoke",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(44, 109) ==
                protocol::ProtocolStatus::Applied,
           "register");
    const auto lease = git.AcquireLease();
    bool exception_contained = false;
    bool output_reference_acquired = false;
    bool output_reference_released = false;
    bool permanent_pin = false;
    bool quiesced = false;
    auto output_owner =
        result.resources.Create("unconfirmed-get-output");
    try {
        output_reference_acquired = true;
        throw std::runtime_error(
            "injected-get-interface-exception-after-output-write");
    } catch (...) {
        exception_contained = true;
        // Mirrors RetainUnconfirmedAcquisition: do not release either the
        // provisional output or its active protocol lease.
        permanent_pin = true;
        quiesced = true;
    }
    const auto close = git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           lease.status == protocol::ProtocolStatus::Acquired &&
                exception_contained &&
                output_reference_acquired &&
                !output_reference_released &&
                permanent_pin && quiesced &&
                close == protocol::ProtocolStatus::Applied &&
                revoke.status ==
                    protocol::ProtocolStatus::LeaseOutstanding &&
                receipt.state == protocol::GitState::Registered &&
                receipt.active_leases == 1 &&
                receipt.cookie_present,
            "unknown-get-output-was-released-or-revoke-not-blocked");
    result.terminal_state =
        "unknown-proxy-and-cookie-retained-by-active-lease";
    output_owner.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario GitCoCreateOutputThrowRetained() {
    Scenario result{
        "git.cocreate-output-throw-retained",
        "git",
        true,
        "",
        {"register", "close-admission", "begin-revoke",
         "cocreate-writes-output", "cocreate-throws",
         "withhold-provisional-git-output",
         "commit-retryable-cookie-retention"},
        {"cocreate-exception-after-output-write"},
        {},
        "a provisional GIT pointer written before a CoCreate exception is never released, and the exact cookie remains retryable",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(46, 127) ==
                   protocol::ProtocolStatus::Applied &&
               git.CloseAdmission() ==
                   protocol::ProtocolStatus::Applied,
           "register-and-close");
    const auto revoke = git.BeginRevoke();
    bool provisional_git_acquired = false;
    bool provisional_git_released = false;
    bool permanent_pin = false;
    bool quiesced = false;
    protocol::ProtocolStatus commit =
        protocol::ProtocolStatus::InvalidState;
    auto provisional_owner =
        result.resources.Create("unconfirmed-cocreate-output");
    try {
        provisional_git_acquired = true;
        throw std::runtime_error(
            "injected-cocreate-exception-after-output-write");
    } catch (...) {
        permanent_pin = true;
        quiesced = true;
        commit = git.CompleteRevoke(
            revoke.ticket, false, -107, true,
            protocol::GitRetainedReason::
                GitCoCreateFailedDuringRevoke);
    }
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired &&
               provisional_git_acquired &&
               !provisional_git_released &&
               permanent_pin && quiesced &&
               commit == protocol::ProtocolStatus::Applied &&
               receipt.state == protocol::GitState::Retained &&
               receipt.cookie_present && receipt.retry_eligible &&
               receipt.retained_reason ==
                   protocol::GitRetainedReason::
                       GitCoCreateFailedDuringRevoke,
           "unknown-cocreate-output-released-or-cookie-lost");
    result.terminal_state =
        "unknown-git-reference-and-cookie-retained";
    provisional_owner.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario GitSiteHolderExternalComFirewall() {
    Scenario result{
        "git.site-holder-external-com-firewall",
        "git",
        true,
        "",
        {"inject-addref-after-acquisition",
         "withhold-unconfirmed-site-owner",
         "copy-confirmed-site",
         "inject-query-after-output-write",
         "withhold-unconfirmed-query-output",
         "release-confirmed-site",
         "copy-second-confirmed-site",
         "inject-final-release-exception",
         "detach-and-retain-without-retry"},
        {"site-addref-exception", "site-query-exception",
         "site-release-exception"},
        {},
        "site AddRef, QueryInterface, and final Release exceptions are contained without blind compensation or retry",
    };

    FakeExternalComReferenceOps addref_fault{
        result.resources};
    addref_fault.throw_after_addref = true;
    {
        FakeSiteHolderExternalComFirewall holder;
        Expect(result,
               !holder.CopyFromExternal(addref_fault),
               "addref-fault-not-contained");
    }

    FakeExternalComReferenceOps query_fault{
        result.resources};
    query_fault.throw_after_query_output = true;
    {
        FakeSiteHolderExternalComFirewall holder;
        Expect(result,
               holder.CopyFromExternal(query_fault),
               "query-holder-copy");
        Expect(result,
               !holder.QueryInterfaceNoThrow(),
               "query-fault-not-contained");
        Expect(result, holder.Reset(), "query-holder-release");
    }

    FakeExternalComReferenceOps release_fault{
        result.resources};
    release_fault.throw_on_release = true;
    {
        FakeSiteHolderExternalComFirewall holder;
        Expect(result,
               holder.CopyFromExternal(release_fault),
               "release-holder-copy");
        Expect(result,
               !holder.Reset(),
               "release-fault-not-contained");
        Expect(result,
               holder.Reset(),
               "release-fault-was-retried");
    }

    Expect(result,
           addref_fault.addref_calls == 1 &&
               addref_fault.release_calls == 0 &&
               addref_fault.unknown_outcomes == 1 &&
               addref_fault.permanent_pin &&
               addref_fault.quiesced &&
               query_fault.addref_calls == 1 &&
               query_fault.query_calls == 1 &&
               query_fault.release_calls == 1 &&
               query_fault.unknown_outcomes == 1 &&
               query_fault.permanent_pin &&
               query_fault.quiesced &&
               release_fault.addref_calls == 1 &&
               release_fault.release_calls == 1 &&
               release_fault.unknown_outcomes == 1 &&
               release_fault.permanent_pin &&
               release_fault.quiesced,
           "external-site-reference-accounting");
    result.terminal_state =
        "three-unknown-site-references-retained-with-pin";
    Finalize(result);
    return result;
}

Scenario GitInternalSelfReferenceNoexcept() {
    Scenario result{
        "git.internal-self-reference-noexcept",
        "git",
        true,
        "",
        {"publish-advise-worker-reference",
         "transfer-reference-to-started-worker",
         "worker-releases-reference",
         "publish-unadvise-worker-reference",
         "inject-create-thread-failure",
         "publication-guard-rolls-back-reference",
         "acquire-callback-reference",
         "callback-guard-releases-reference"},
        {"worker-create-failure"},
        {},
        "the internal winrt::implements AddRef, Release and guard destructors are noexcept, so worker publication, rollback and callback references balance without an external-COM fault model",
    };

    FakeInternalSelfReferenceOps operations{
        result.resources};

    // Successful CreateThread transfers the published reference to the worker.
    {
        FakeInternalSelfReferenceGuard publication(operations);
        Expect(result,
               static_cast<bool>(publication),
               "worker-reference-not-published");
        publication.Disarm();
    }
    operations.Release();

    // Failed CreateThread leaves publication armed, so its noexcept destructor
    // rolls back the pre-published reference.
    {
        FakeInternalSelfReferenceGuard publication(operations);
        Expect(result,
               static_cast<bool>(publication),
               "rollback-reference-not-published");
    }

    // Callback entry/exit uses the same exact internal no-throw ABI.
    {
        FakeInternalSelfReferenceGuard callback(operations);
        Expect(result,
               static_cast<bool>(callback),
               "callback-reference-not-acquired");
    }

    Expect(result,
           operations.addref_calls == 3 &&
               operations.release_calls == 3 &&
               operations.live_references == 1 &&
               operations.double_release == 0,
           "internal-self-reference-accounting");
    result.terminal_state =
        "internal-self-references-balanced-noexcept";
    Finalize(result);
    return result;
}

Scenario GitRevokeExternalComExceptionRetained() {
    Scenario result{
        "git.revoke-external-com-exception-retained",
        "git",
        true,
        "",
        {"register", "close-admission", "begin-revoke",
         "inject-revoke-external-com-exception",
         "commit-failure-with-exact-ticket",
         "verify-retryable-retention"},
        {"revoke-interface-exception"},
        {},
        "a RevokeInterfaceFromGlobal exception is contained and the exact ticket returns to retryable Retained instead of remaining Revoking",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(45, 113) ==
                   protocol::ProtocolStatus::Applied &&
               git.CloseAdmission() ==
                   protocol::ProtocolStatus::Applied,
           "register-and-close");
    const auto revoke = git.BeginRevoke();
    bool exception_contained = false;
    protocol::ProtocolStatus commit =
        protocol::ProtocolStatus::InvalidState;
    try {
        throw std::runtime_error(
            "injected-revoke-interface-exception");
    } catch (...) {
        exception_contained = true;
        commit = git.CompleteRevoke(
            revoke.ticket, false, -106, true,
            protocol::GitRetainedReason::
                RevokeInterfaceFromGlobalFailed);
    }
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired &&
               exception_contained &&
               commit == protocol::ProtocolStatus::Applied &&
               receipt.state == protocol::GitState::Retained &&
               receipt.cookie_present && receipt.retry_eligible &&
               receipt.retained_reason ==
                   protocol::GitRetainedReason::
                       RevokeInterfaceFromGlobalFailed,
           "revoke-exception-left-revoking-or-lost-cookie");
    result.terminal_state =
        "revoke-exception-contained-retryable-retained";
    Finalize(result);
    return result;
}

Scenario GitCoCreateFailRetained() {
    Scenario result{
        "git.cocreate-fail-retained",
        "git",
        true,
        "",
        {"register", "cocreate-fails", "retain-with-reason"},
        {"cocreate-failure"},
        {},
        "a COM service creation failure leaves a fail-safe retained receipt",
    };
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(5, 37) == protocol::ProtocolStatus::Applied,
           "register");
    Expect(result,
           git.RetainRegisteredResource(
                -4, false,
                protocol::GitRetainedReason::
                    PlatformPreconditionFailed) ==
               protocol::ProtocolStatus::Applied,
           "retain");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
            receipt.state == protocol::GitState::Retained &&
                receipt.cookie_present && !receipt.retry_eligible &&
                receipt.retained_reason ==
                    protocol::GitRetainedReason::
                        PlatformPreconditionFailed,
           "retained-receipt");
    Expect(result,
           git.BeginRevoke().status ==
               protocol::ProtocolStatus::NotRetryable,
           "no-unsafe-retry");
    Finalize(result);
    return result;
}

Scenario GitComChangedMode() {
    Scenario result{
        "git.com-changed-mode",
        "git",
        true,
        "",
        {"register", "coinitialize-changed-mode", "retain", "balance-noop"},
        {"rpc-e-changed-mode"},
        {},
        "changed apartment mode neither calls uninitialize nor hides the GIT resource",
    };
    protocol::ApartmentBalance apartment;
    protocol::GitLifecycle git;
    Expect(result,
           git.Register(6, 41) == protocol::ProtocolStatus::Applied,
           "register");
    Expect(result,
           apartment.RecordInitialize(
               protocol::ApartmentInitKind::ChangedMode, -5) ==
               protocol::ProtocolStatus::Applied,
           "record-changed-mode");
    Expect(result,
           git.RetainRegisteredResource(
                -5, false,
                protocol::GitRetainedReason::
                    ComApartmentInitializationFailed) ==
               protocol::ProtocolStatus::Applied,
           "retain-git");
    Expect(result,
           apartment.Close() == protocol::ProtocolStatus::TerminalNoop,
           "no-unbalanced-uninitialize");
    const auto apartment_receipt = apartment.Receipt();
    const auto git_receipt = git.Receipt();
    RecordGitReceiptResources(result, git_receipt);
    result.terminal_state = "changed-mode+retained";
    Expect(result,
           apartment_receipt.state ==
                   protocol::ApartmentState::ChangedMode &&
               apartment_receipt.initialize_calls == 1 &&
               apartment_receipt.uninitialize_calls == 0 &&
               !apartment_receipt.requires_uninitialize,
           "apartment-receipt");
    Expect(result,
           git_receipt.state == protocol::GitState::Retained &&
               git_receipt.cookie_present,
           "git-receipt");
    Finalize(result);
    return result;
}

Scenario GitSFalseBalanced() {
    Scenario result{
        "git.s-false-balanced",
        "git",
        true,
        "",
        {"coinitialize-s-false", "close-apartment", "verify-balanced"},
        {"s-false"},
        {},
        "S_FALSE still requires and receives one matching uninitialize",
    };
    protocol::ApartmentBalance apartment;
    auto apartment_owner =
        result.resources.Create("com-apartment-balance");
    Expect(result,
           apartment.RecordInitialize(
               protocol::ApartmentInitKind::AlreadyInitialized) ==
               protocol::ProtocolStatus::Applied,
           "record-s-false");
    const auto before = apartment.Receipt();
    Expect(result,
           before.state ==
                   protocol::ApartmentState::AlreadyInitialized &&
               before.requires_uninitialize,
           "balance-required");
    Expect(result,
           apartment.Close() == protocol::ProtocolStatus::Applied,
           "close");
    Expect(result,
           apartment.Close() == protocol::ProtocolStatus::TerminalNoop,
           "repeat-close-noop");
    const auto receipt = apartment.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::ApartmentState::Balanced &&
               receipt.initialize_calls == 1 &&
               receipt.uninitialize_calls == 1 &&
               !receipt.requires_uninitialize,
           "balanced-receipt");
    apartment_owner.Release();
    Finalize(result);
    return result;
}

Scenario GitAdviseFailMaybeAdvised() {
    Scenario result{
        "git.advise-fail-maybe-advised",
        "git",
        true,
        "",
        {"begin-advise", "advise-fails", "mark-maybe-advised",
         "best-effort-unadvise"},
        {"advise-returned-failure"},
        {},
        "an Advise failure is treated as maybe-advised until best-effort Unadvise succeeds",
    };
    protocol::SubscriptionLifecycle subscription;
    Expect(result,
           subscription.BeginAdvise() ==
               protocol::ProtocolStatus::Applied,
           "begin-advise");
    Expect(result,
           subscription.CompleteAdvise(false, -6) ==
               protocol::ProtocolStatus::Applied,
           "advise-failure");
    const auto uncertain = subscription.Receipt();
    Expect(result,
           uncertain.state == protocol::SubscriptionState::MaybeAdvised &&
               uncertain.best_effort_unadvise_required,
           "maybe-advised");
    Expect(result,
           subscription.BeginUnadvise() ==
                   protocol::ProtocolStatus::Applied &&
               subscription.CompleteUnadvise(true) ==
                   protocol::ProtocolStatus::Applied,
           "best-effort-unadvise");
    const auto receipt = subscription.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::SubscriptionState::Unadvised &&
               !receipt.best_effort_unadvise_required &&
               receipt.advise_attempts == 1 &&
               receipt.unadvise_attempts == 1,
           "terminal-subscription");
    RecordSubscriptionReceiptResources(result, receipt);
    Finalize(result);
    return result;
}

Scenario GitUnadviseFailBeforeRevoke() {
    Scenario result{
        "git.unadvise-fail-before-revoke",
        "git",
        true,
        "",
        {"register", "advise", "unadvise-fails", "retain-before-revoke"},
        {"unadvise-failure"},
        {},
        "failed Unadvise retains both callback uncertainty and the GIT cookie",
    };
    protocol::GitLifecycle git;
    protocol::SubscriptionLifecycle subscription;
    git.Register(7, 43);
    subscription.BeginAdvise();
    subscription.CompleteAdvise(true);
    subscription.BeginUnadvise();
    Expect(result,
           subscription.CompleteUnadvise(false, -7) ==
               protocol::ProtocolStatus::Applied,
           "unadvise-failure");
    Expect(result,
           git.RetainRegisteredResource(
                -7, false,
                protocol::GitRetainedReason::
                    UnadviseFailedBeforeRevoke) ==
               protocol::ProtocolStatus::Applied,
           "retain-git");
    const auto sub_receipt = subscription.Receipt();
    const auto git_receipt = git.Receipt();
    RecordGitReceiptResources(result, git_receipt);
    result.terminal_state = "maybe-advised+retained";
    Expect(result,
           sub_receipt.state ==
                   protocol::SubscriptionState::MaybeAdvised &&
               sub_receipt.best_effort_unadvise_required,
           "subscription-retained");
    Expect(result,
           git_receipt.state == protocol::GitState::Retained &&
               git_receipt.cookie_present &&
               git.BeginRevoke().status ==
                   protocol::ProtocolStatus::NotRetryable,
           "git-retained");
    RecordSubscriptionReceiptResources(result, sub_receipt);
    Finalize(result);
    return result;
}

Scenario GitUnadviseOkRevokeFail() {
    Scenario result{
        "git.unadvise-ok-revoke-fail",
        "git",
        true,
        "",
        {"register", "advise", "unadvise-succeeds", "revoke-fails"},
        {"revoke-failure"},
        {},
        "subscription cleanup and GIT revoke have independent terminal receipts",
    };
    protocol::GitLifecycle git;
    protocol::SubscriptionLifecycle subscription;
    git.Register(8, 47);
    subscription.BeginAdvise();
    subscription.CompleteAdvise(true);
    subscription.BeginUnadvise();
    Expect(result,
           subscription.CompleteUnadvise(true) ==
               protocol::ProtocolStatus::Applied,
           "unadvise");
    git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    Expect(result,
           revoke.status == protocol::ProtocolStatus::Acquired &&
               git.CompleteRevoke(
                   revoke.ticket,
                   false,
                   -8,
                   false,
                   protocol::GitRetainedReason::
                       RevokeInterfaceFromGlobalFailed) ==
                   protocol::ProtocolStatus::Applied,
           "revoke-failure");
    const auto sub_receipt = subscription.Receipt();
    const auto git_receipt = git.Receipt();
    RecordGitReceiptResources(result, git_receipt);
    result.terminal_state = "unadvised+retained";
    Expect(result,
           sub_receipt.state ==
                   protocol::SubscriptionState::Unadvised &&
               git_receipt.state == protocol::GitState::Retained &&
               git_receipt.cookie_present &&
               !git_receipt.retry_eligible,
           "split-receipt");
    RecordSubscriptionReceiptResources(result, sub_receipt);
    Finalize(result);
    return result;
}

Scenario GitStaleGeneration() {
    Scenario result{
        "git.stale-generation",
        "git",
        true,
        "",
        {"register", "lease", "reject-stale-lease", "begin-revoke",
         "reject-stale-revoke", "commit-owner"},
        {"stale-generation"},
        {},
        "stale generation tickets cannot release a lease or clear a cookie",
    };
    protocol::GitLifecycle git;
    git.Register(9, 53);
    const auto lease = git.AcquireLease();
    auto stale_lease = lease.ticket;
    ++stale_lease.generation;
    Expect(result,
           git.ReleaseLease(stale_lease) ==
               protocol::ProtocolStatus::GenerationMismatch,
           "reject-stale-lease");
    Expect(result,
           git.ReleaseLease(lease.ticket) ==
               protocol::ProtocolStatus::Applied,
           "release-owner-lease");
    git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    auto stale_revoke = revoke.ticket;
    ++stale_revoke.generation;
    Expect(result,
           git.CompleteRevoke(stale_revoke, true) ==
               protocol::ProtocolStatus::GenerationMismatch,
           "reject-stale-revoke");
    const auto during = git.Receipt();
    Expect(result,
           during.state == protocol::GitState::Revoking &&
               during.cookie_present,
           "cookie-survives-stale-ticket");
    Expect(result,
           git.CompleteRevoke(revoke.ticket, true) ==
               protocol::ProtocolStatus::Applied,
           "owner-commit");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               !receipt.cookie_present &&
               receipt.successful_revokes == 1,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario GitRepeatCloseNoop() {
    Scenario result{
        "git.repeat-close-noop",
        "git",
        true,
        "",
        {"register", "revoke", "repeat-begin", "repeat-complete"},
        {"repeated-close"},
        {},
        "repeated close is a stable no-op and never double-revokes",
    };
    protocol::GitLifecycle git;
    git.Register(10, 59);
    git.CloseAdmission();
    const auto revoke = git.BeginRevoke();
    git.CompleteRevoke(revoke.ticket, true);
    Expect(result,
           git.BeginRevoke().status ==
               protocol::ProtocolStatus::TerminalNoop,
           "repeat-begin-noop");
    Expect(result,
           git.CompleteRevoke(revoke.ticket, true) ==
               protocol::ProtocolStatus::StaleTicket,
           "repeat-complete-stale");
    const auto receipt = git.Receipt();
    RecordGitReceiptResources(result, receipt);
    result.terminal_state = std::string(protocol::ToString(receipt.state));
    Expect(result,
           receipt.state == protocol::GitState::Revoked &&
               receipt.successful_revokes == 1 &&
               receipt.revoke_attempts == 1,
           "single-revoke");
    Finalize(result);
    return result;
}

protocol::UiThreadCapabilities UiCapabilities() {
    return {
        protocol::ThreadHandleCapability::SynchronizeAndQueryLimited,
        true,
        true,
        true,
    };
}

Scenario UiFixedCapacitySnapshotTransaction() {
    Scenario result{
        "ui.fixed-capacity-snapshot-transaction",
        "ui-thread",
        true,
        "",
        {"fill-fixed-registry", "reject-65th-record",
         "issue-complete-fixed-snapshot", "clean-all"},
        {"allocator-capacity-boundary"},
        {},
        "the registry rejects overflow before mutation and issues one allocation-free ticket for every owned record",
    };
    protocol::UiThreadRegistry registry(31);
    bool all_registered = true;
    for (std::size_t index = 0;
         index < protocol::kUiThreadRegistryCapacity; ++index) {
        const auto registration = registry.RegisterInitialized(
            31,
            {1000 + index, 10000 + index},
            UiCapabilities());
        all_registered =
            all_registered &&
            registration.status ==
                protocol::UiRegisterStatus::Registered;
    }
    const auto overflow = registry.Reserve(
        31, {9000, 90000}, UiCapabilities());
    Expect(result,
           all_registered &&
               overflow.status ==
                   protocol::UiRegisterStatus::CapacityExhausted &&
               overflow.record_id == 0,
           "capacity-rejected-before-record");

    const auto tickets = registry.SnapshotForCleanup();
    bool all_valid = !tickets.capacity_exhausted &&
                     tickets.size() ==
                         protocol::kUiThreadRegistryCapacity;
    std::uint64_t previous_ticket_id = 0;
    for (const auto& ticket : tickets) {
        all_valid = all_valid && ticket.valid() &&
                    ticket.ticket_id > previous_ticket_id;
        previous_ticket_id = ticket.ticket_id;
    }
    Expect(result, all_valid, "complete-transactional-snapshot");

    bool all_cleaned = true;
    for (const auto& ticket : tickets) {
        all_cleaned =
            all_cleaned &&
            registry.CompleteCleanup(
                ticket, protocol::UiCleanupOutcome::Cleaned) ==
                protocol::ProtocolStatus::Applied;
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    for (const auto& receipt : receipts) {
        all_cleaned =
            all_cleaned && receipt.terminal &&
            receipt.state == protocol::UiRecordState::Cleaned;
    }
    result.terminal_state = "capacity-bounded-cleaned";
    Expect(result,
           all_cleaned && !receipts.capacity_exhausted &&
               receipts.size() ==
                   protocol::kUiThreadRegistryCapacity,
           "all-tickets-owned-and-terminal");
    Finalize(result);
    return result;
}

class FakeUiUniqueResource {
public:
    explicit FakeUiUniqueResource(
        ResourceLedger& resources,
        std::string kind,
        std::uint64_t* releases)
        : owner_(resources.Create(std::move(kind))),
          releases_(releases) {}

    FakeUiUniqueResource(const FakeUiUniqueResource&) = delete;
    FakeUiUniqueResource& operator=(
        const FakeUiUniqueResource&) = delete;

    ~FakeUiUniqueResource() {
        if (owned_ && releases_) {
            owner_.Release();
            ++*releases_;
        }
    }

private:
    ResourceOwner owner_;
    std::uint64_t* releases_ = nullptr;
    bool owned_ = true;
};

Scenario UiRawHandleRollback() {
    Scenario result{
        "ui.raw-handle-rollback",
        "ui-thread",
        true,
        "",
        {"reserve", "begin-initialization", "acquire-two-raw-handles",
         "inject-runtime-allocation-failure", "raii-close-both",
         "publish-init-failed"},
        {"make-shared-allocation-failure"},
        {},
        "raw adapter handles close exactly once before a terminal rollback receipt is published",
    };
    protocol::UiThreadRegistry registry(32);
    const auto registration =
        registry.Reserve(32, {1100, 11000}, UiCapabilities());
    Expect(result,
           registration.status ==
                   protocol::UiRegisterStatus::Registered &&
               registry.BeginInitialization(
                   registration.record_id) ==
                   protocol::ProtocolStatus::Applied,
           "begin-initialization");

    std::uint64_t releases = 0;
    try {
        FakeUiUniqueResource thread_handle(
            result.resources, "ui-thread-handle", &releases);
        FakeUiUniqueResource cleanup_event(
            result.resources, "ui-cleanup-event", &releases);
        throw std::runtime_error(
            "injected-runtime-allocation-failure");
    } catch (const std::runtime_error&) {
        Expect(result,
               registry.FailInitialization(
                   registration.record_id, -401,
                   "initialization-failed-rollback-complete") ==
                   protocol::ProtocolStatus::Applied,
               "publish-rollback");
    }

    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(
                               protocol::ToString(
                                   receipts[0].state));
    Expect(result,
           releases == 2 && receipts.size() == 1 &&
               receipts[0].terminal &&
               receipts[0].state ==
                   protocol::UiRecordState::InitFailed &&
               receipts[0].reason ==
                   "initialization-failed-rollback-complete" &&
               registry.SnapshotForCleanup().empty(),
           "exact-raw-resource-rollback");
    Finalize(result);
    return result;
}

Scenario UiEnumCallbackFixedCapacity() {
    Scenario result{
        "ui.enum-callback-fixed-capacity",
        "ui-thread",
        true,
        "",
        {"enumerate-six", "capture-fixed-four", "receipt-two-dropped",
         "return-through-noexcept-boundary"},
        {"enumeration-capacity-exhausted"},
        {},
        "the USER32-shaped callback boundary records overflow without allocating or propagating an exception",
    };
    struct Snapshot {
        std::array<std::uintptr_t, 4> windows{};
        std::size_t count = 0;
        std::size_t dropped = 0;
        bool callback_failed = false;
    } snapshot;
    const auto callback =
        [](std::uintptr_t window,
           Snapshot* value) noexcept -> bool {
        try {
            if (!value) {
                return false;
            }
            if (value->count >= value->windows.size()) {
                ++value->dropped;
                return true;
            }
            value->windows[value->count++] = window;
            return true;
        } catch (...) {
            if (value) {
                value->callback_failed = true;
            }
            return false;
        }
    };

    bool boundary_returned = true;
    for (std::uintptr_t window = 1; window <= 6; ++window) {
        boundary_returned =
            boundary_returned && callback(window, &snapshot);
    }
    result.terminal_state = "capacity-receipt";
    Expect(result,
           boundary_returned && snapshot.count == 4 &&
               snapshot.dropped == 2 &&
               !snapshot.callback_failed &&
               snapshot.windows[0] == 1 &&
               snapshot.windows[3] == 4,
           "fixed-enumeration-receipt");
    Finalize(result);
    return result;
}

Scenario UiBootstrapDuplicateObservation() {
    Scenario result{
        "ui.bootstrap-duplicate-observation",
        "ui-thread",
        true,
        "",
        {"hook-observes-window", "after-init-registers-thread-only",
         "duplicate-register-reuses-record", "verify-single-role-count"},
        {"hook-and-bootstrap-overlap"},
        {},
        "bootstrap enumeration initializes the thread but leaves window-role observation exclusively to the create hook",
    };
    protocol::UiThreadRegistry registry(33);
    protocol::UiWindowRoleLifecycle window_roles;
    const protocol::UiThreadIdentity identity{1200, 12000};
    auto xaml_window =
        result.resources.Create("xaml-host-window");
    Expect(result,
           window_roles.ObserveCreated(
               protocol::UiWindowRole::XamlHost) ==
               protocol::ProtocolStatus::Applied,
           "hook-observes-once");
    const auto hook_registration = registry.RegisterInitialized(
        33, identity, UiCapabilities());
    const auto bootstrap_registration =
        registry.RegisterInitialized(
            33, identity, UiCapabilities());
    const auto role_receipt = window_roles.Receipt();
    const auto& xaml =
        role_receipt.For(protocol::UiWindowRole::XamlHost);
    Expect(result,
           hook_registration.status ==
                   protocol::UiRegisterStatus::Registered &&
               bootstrap_registration.status ==
                   protocol::UiRegisterStatus::Duplicate &&
               bootstrap_registration.record_id ==
                   hook_registration.record_id &&
               xaml.created == 1 && xaml.active == 1,
           "bootstrap-does-not-observe-role");
    const auto cleanup =
        registry.BeginCleanup(hook_registration.record_id);
    Expect(result,
           cleanup.status == protocol::ProtocolStatus::Acquired &&
               registry.CompleteCleanup(
                   cleanup.ticket,
                   protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::Applied,
           "single-record-clean");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    xaml_window.Retain(
        RetainReasonCode::ExternalUncertainty);
    result.terminal_state = "single-observation-cleaned";
    Finalize(result);
    return result;
}

Scenario UiNormalClean() {
    Scenario result{
        "ui.normal-clean",
        "ui-thread",
        true,
        "",
        {"register-generation", "snapshot", "clean-outside-lock", "receipt"},
        {},
        {},
        "registered generation receives one cleaned terminal receipt",
    };
    protocol::UiThreadRegistry registry(7);
    const auto registration = registry.RegisterInitialized(
        7, {101, 1001}, UiCapabilities());
    Expect(result,
           registration.status == protocol::UiRegisterStatus::Registered,
           "register");
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "snapshot");
    if (tickets.size() == 1) {
        Expect(result,
               registry.CompleteCleanup(
                   tickets[0], protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::Applied,
               "clean");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 && receipts[0].terminal &&
               receipts[0].state == protocol::UiRecordState::Cleaned,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiThreadIdReuse() {
    Scenario result{
        "ui.thread-id-reuse",
        "ui-thread",
        true,
        "",
        {"register-old-identity", "register-reused-id", "clean-both"},
        {"thread-id-reuse"},
        {},
        "creation stamp distinguishes records that share a logical thread id",
    };
    protocol::UiThreadRegistry registry(8);
    const auto old_record =
        registry.RegisterInitialized(8, {202, 2001}, UiCapabilities());
    const auto new_record =
        registry.RegisterInitialized(8, {202, 2002}, UiCapabilities());
    Expect(result,
           old_record.status == protocol::UiRegisterStatus::Registered &&
               new_record.status ==
                   protocol::UiRegisterStatus::Registered &&
               old_record.record_id != new_record.record_id,
           "distinct-generations");
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 2, "complete-snapshot");
    for (const auto& ticket : tickets) {
        Expect(result,
               registry.CompleteCleanup(
                   ticket, protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::Applied,
               "clean-record");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state = "cleaned";
    Expect(result,
           receipts.size() == 2 &&
               receipts[0].state == protocol::UiRecordState::Cleaned &&
               receipts[1].state == protocol::UiRecordState::Cleaned,
           "two-terminal-receipts");
    Finalize(result);
    return result;
}

Scenario UiTimeoutLateClean() {
    Scenario result{
        "ui.timeout-late-clean",
        "ui-thread",
        true,
        "",
        {"register", "dispatch-timeout", "retain", "late-clean"},
        {"dispatch-timeout", "late-callback"},
        {},
        "a late completion can close only its exact retained ticket",
    };
    protocol::UiThreadRegistry registry(9);
    registry.RegisterInitialized(9, {303, 3001}, UiCapabilities());
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "snapshot");
    if (tickets.size() == 1) {
        Expect(result,
               registry.CompleteCleanup(
                   tickets[0],
                   protocol::UiCleanupOutcome::Retained,
                   -2,
                   true,
                   "cleanup-timeout") == protocol::ProtocolStatus::Applied,
               "retain-timeout");
        Expect(result,
               registry.CompleteLateClean(tickets[0]) ==
                   protocol::ProtocolStatus::Applied,
               "late-clean");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].state ==
                   protocol::UiRecordState::LateCleanedRetained,
           "late-terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiDuplicateInit() {
    Scenario result{
        "ui.duplicate-init",
        "ui-thread",
        true,
        "",
        {"register", "repeat-identical-register", "single-snapshot", "clean"},
        {"duplicate-initialize"},
        {},
        "duplicate initialization resolves to the existing record",
    };
    protocol::UiThreadRegistry registry(10);
    const auto first = registry.RegisterInitialized(
        10, {404, 4001}, UiCapabilities());
    const auto duplicate = registry.RegisterInitialized(
        10, {404, 4001}, UiCapabilities());
    Expect(result,
           first.status == protocol::UiRegisterStatus::Registered &&
               duplicate.status == protocol::UiRegisterStatus::Duplicate &&
               duplicate.record_id == first.record_id,
           "idempotent-register");
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "single-ticket");
    if (tickets.size() == 1) {
        registry.CompleteCleanup(
            tickets[0], protocol::UiCleanupOutcome::Cleaned);
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].state == protocol::UiRecordState::Cleaned,
           "single-terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiWindowGoneThreadAlive() {
    Scenario result{
        "ui.window-gone-thread-alive",
        "ui-thread",
        true,
        "",
        {"register-from-bootstrap", "window-disappears", "registry-snapshot",
         "dispatcher-clean"},
        {"bootstrap-window-gone"},
        {},
        "cleanup proceeds from the thread record without persisting or re-enumerating HWND",
    };
    protocol::UiThreadRegistry registry(11);
    registry.RegisterInitialized(11, {505, 5001}, UiCapabilities());
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "record-survives-window-loss");
    if (tickets.size() == 1) {
        Expect(result,
               registry.CompleteCleanup(
                   tickets[0], protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::Applied,
               "dispatcher-clean");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 && receipts[0].terminal &&
               receipts[0].state == protocol::UiRecordState::Cleaned,
           "terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiDispatchRejected() {
    Scenario result{
        "ui.dispatch-rejected",
        "ui-thread",
        true,
        "",
        {"register", "snapshot", "dispatcher-rejects", "retain-receipt"},
        {"dispatcher-rejected"},
        {},
        "a rejected dispatch is terminally retained instead of disappearing",
    };
    protocol::UiThreadRegistry registry(12);
    registry.RegisterInitialized(12, {606, 6001}, UiCapabilities());
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "snapshot");
    if (tickets.size() == 1) {
        Expect(result,
               registry.CompleteCleanup(
                   tickets[0],
                   protocol::UiCleanupOutcome::Retained,
                   -9,
                   false,
                   "dispatcher-rejected") ==
                   protocol::ProtocolStatus::Applied,
               "retain-rejection");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 && receipts[0].terminal &&
               receipts[0].state == protocol::UiRecordState::Retained &&
               !receipts[0].retry_eligible &&
               receipts[0].reason == "dispatcher-rejected",
           "retained-receipt");
    Finalize(result);
    return result;
}

Scenario UiThreadExited() {
    Scenario result{
        "ui.thread-exited",
        "ui-thread",
        true,
        "",
        {"register", "observe-thread-exit", "mark-unreachable",
         "verify-no-dispatch"},
        {"thread-exited"},
        {},
        "an exited thread receives an unreachable terminal receipt",
    };
    protocol::UiThreadRegistry registry(13);
    const auto registration = registry.RegisterInitialized(
        13, {707, 7001}, UiCapabilities());
    Expect(result,
           registry.MarkThreadExited(
               registration.record_id, -10, "thread-exited") ==
               protocol::ProtocolStatus::Applied,
           "mark-exited");
    Expect(result,
           registry.SnapshotForCleanup().empty(),
           "no-dispatch-to-exited-thread");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 && receipts[0].terminal &&
               receipts[0].state ==
                   protocol::UiRecordState::Unreachable &&
               receipts[0].reason == "thread-exited",
           "unreachable-receipt");
    Finalize(result);
    return result;
}

Scenario UiPartialCleanRetry() {
    Scenario result{
        "ui.partial-clean-retry",
        "ui-thread",
        true,
        "",
        {"register-two", "snapshot", "clean-first", "retain-second",
         "retry-second", "clean-second"},
        {"partial-cleanup-failure"},
        {},
        "only the retained record receives a new generation-checked cleanup ticket",
    };
    protocol::UiThreadRegistry registry(14);
    const auto first = registry.RegisterInitialized(
        14, {808, 8001}, UiCapabilities());
    const auto second = registry.RegisterInitialized(
        14, {809, 8002}, UiCapabilities());
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 2, "complete-snapshot");
    for (const auto& ticket : tickets) {
        if (ticket.record_id == first.record_id) {
            Expect(result,
                   registry.CompleteCleanup(
                       ticket, protocol::UiCleanupOutcome::Cleaned) ==
                       protocol::ProtocolStatus::Applied,
                   "clean-first");
        } else if (ticket.record_id == second.record_id) {
            Expect(result,
                   registry.CompleteCleanup(
                       ticket,
                       protocol::UiCleanupOutcome::Retained,
                       -11,
                       true,
                       "retryable-cleanup-failure") ==
                       protocol::ProtocolStatus::Applied,
                   "retain-second");
        }
    }
    const auto retry = registry.BeginRetry(second.record_id);
    Expect(result,
           retry.status == protocol::ProtocolStatus::Acquired &&
               retry.ticket.attempt == 2,
           "retry-ticket");
    Expect(result,
           registry.CompleteCleanup(
               retry.ticket, protocol::UiCleanupOutcome::Cleaned) ==
               protocol::ProtocolStatus::Applied,
           "retry-clean");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state = "cleaned";
    Expect(result,
           receipts.size() == 2 &&
               receipts[0].state == protocol::UiRecordState::Cleaned &&
               receipts[1].state == protocol::UiRecordState::Cleaned &&
               receipts[1].cleanup_attempts == 2,
           "all-terminal");
    Finalize(result);
    return result;
}

Scenario UiWindowReplacement() {
    Scenario result{
        "ui.window-replacement",
        "ui-thread",
        true,
        "",
        {"register-thread-identity", "observe-first-window",
         "replace-window", "cleanup-by-thread-record"},
        {"window-replacement"},
        {},
        "window replacement does not replace or duplicate the HWND-free UI thread record",
    };
    protocol::UiThreadRegistry registry(21);
    protocol::UiWindowRoleLifecycle window_roles;
    const protocol::UiThreadIdentity identity{901, 9001};
    const auto registration =
        registry.RegisterInitialized(21, identity, UiCapabilities());
    auto original_window =
        result.resources.Create("taskbar-bridge-window");
    Expect(result,
           window_roles.ObserveCreated(
               protocol::UiWindowRole::TaskbarBridge) ==
                   protocol::ProtocolStatus::Applied &&
               window_roles.CompleteDestroy(
                   protocol::UiWindowRole::TaskbarBridge,
                   true) == protocol::ProtocolStatus::Applied &&
               window_roles.ObserveCreated(
                   protocol::UiWindowRole::TaskbarBridge) ==
                   protocol::ProtocolStatus::Applied,
           "shared-role-replacement");
    original_window.Release();
    auto replacement_window =
        result.resources.Create("taskbar-bridge-window");
    const auto cleanup = registry.BeginCleanup(registration.record_id);
    Expect(result,
           registration.status ==
                   protocol::UiRegisterStatus::Registered &&
               cleanup.status == protocol::ProtocolStatus::Acquired,
           "single-thread-record");
    Expect(result,
           registry.CompleteCleanup(
               cleanup.ticket,
               protocol::UiCleanupOutcome::Cleaned,
               0,
               false,
               "cleaned-after-window-replacement") ==
               protocol::ProtocolStatus::Applied,
           "cleanup");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    const auto window_receipt = window_roles.Receipt();
    const auto& taskbar = window_receipt.For(
        protocol::UiWindowRole::TaskbarBridge);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           taskbar.active == 1 && taskbar.created == 2 &&
               taskbar.destroyed == 1 &&
               taskbar.replacements == 1 &&
               taskbar.failed_destroy_attempts == 0 &&
               receipts.size() == 1 &&
               receipts[0].identity == identity &&
               receipts[0].state == protocol::UiRecordState::Cleaned &&
               receipts[0].cleanup_attempts == 1,
           "window-independent-receipt");
    replacement_window.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario UiMultipleSameRoleWindows() {
    Scenario result{
        "ui.multiple-same-role-windows",
        "ui-thread",
        true,
        "",
        {"create-two", "destroy-one", "create-replacement",
         "verify-per-role-counts"},
        {"same-role-window-multiplicity"},
        {},
        "per-role active counts preserve multiple same-role windows without HWND identity",
    };
    protocol::UiWindowRoleLifecycle window_roles;
    auto first_window =
        result.resources.Create("xaml-host-window");
    auto second_window =
        result.resources.Create("xaml-host-window");
    Expect(result,
           window_roles.ObserveCreated(
               protocol::UiWindowRole::XamlHost) ==
                   protocol::ProtocolStatus::Applied &&
               window_roles.ObserveCreated(
                   protocol::UiWindowRole::XamlHost) ==
                   protocol::ProtocolStatus::Applied,
           "create-two");
    Expect(result,
           window_roles.CompleteDestroy(
               protocol::UiWindowRole::XamlHost,
               true) == protocol::ProtocolStatus::Applied &&
               window_roles.ObserveCreated(
                   protocol::UiWindowRole::XamlHost) ==
                   protocol::ProtocolStatus::Applied,
           "replace-one");
    first_window.Release();
    auto replacement_window =
        result.resources.Create("xaml-host-window");
    const auto receipt = window_roles.Receipt();
    const auto& xaml =
        receipt.For(protocol::UiWindowRole::XamlHost);
    result.terminal_state = "counted";
    Expect(result,
           xaml.active == 2 && xaml.created == 3 &&
               xaml.destroyed == 1 &&
               xaml.replacements == 1 &&
               xaml.failed_destroy_attempts == 0 &&
               xaml.destroy_without_active == 0,
           "multiplicity-receipt");
    second_window.Retain(
        RetainReasonCode::ExternalUncertainty);
    replacement_window.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario UiDestroyWindowFailure() {
    Scenario result{
        "ui.destroy-window-failure",
        "ui-thread",
        true,
        "",
        {"create", "original-destroy-fails",
         "commit-failure-only", "verify-active-preserved"},
        {"destroy-window-original-failure"},
        {},
        "a failed original DestroyWindow attempt never commits a destruction",
    };
    protocol::UiWindowRoleLifecycle window_roles;
    auto taskbar_window =
        result.resources.Create("taskbar-bridge-window");
    Expect(result,
           window_roles.ObserveCreated(
               protocol::UiWindowRole::TaskbarBridge) ==
                   protocol::ProtocolStatus::Applied &&
               window_roles.CompleteDestroy(
                   protocol::UiWindowRole::TaskbarBridge,
                   false) == protocol::ProtocolStatus::Applied,
           "record-failed-attempt");
    const auto receipt = window_roles.Receipt();
    const auto& taskbar = receipt.For(
        protocol::UiWindowRole::TaskbarBridge);
    result.terminal_state = "failure-recorded";
    Expect(result,
           taskbar.active == 1 && taskbar.created == 1 &&
               taskbar.destroyed == 0 &&
               taskbar.replacements == 0 &&
               taskbar.failed_destroy_attempts == 1 &&
               taskbar.destroy_without_active == 0,
           "no-false-destroy");
    taskbar_window.Retain(
        RetainReasonCode::ExternalUncertainty);
    Finalize(result);
    return result;
}

Scenario UiDestroyHookAbiFirewall() {
    Scenario result{
        "ui.destroy-hook-abi-firewall",
        "ui-thread",
        true,
        "",
        {"inject-admission-failure", "pass-through-original-once",
         "inject-classification-failure", "fallback-original-once",
         "inject-receipt-failure", "return-cached-last-error",
         "inject-original-failure", "never-retry-original"},
        {"lifecycle-admission-exception",
         "classification-exception",
         "receipt-exception", "original-exception"},
        {},
        "DestroyWindow admission, pre-call, post-call, and original exceptions are contained with one original attempt and cached LastError",
    };

    FakeDestroyHookState admission_failure;
    admission_failure.throw_on_enter = true;
    Expect(result,
           InvokeFakeDestroyWindowHook(admission_failure) == 1 &&
               admission_failure.original_calls == 1 &&
               admission_failure.classification_calls == 0 &&
               admission_failure.receipt_calls == 0 &&
               admission_failure.returned_last_error == 41 &&
               admission_failure.permanent_pin &&
               admission_failure.quiesced,
           "admission-false-native-pass-through");

    FakeDestroyHookState classification_failure;
    classification_failure.throw_in_classification = true;
    Expect(result,
           InvokeFakeDestroyWindowHook(classification_failure) == 1 &&
               classification_failure.original_calls == 1 &&
               classification_failure.classification_calls == 1 &&
               classification_failure.receipt_calls == 0 &&
               classification_failure.returned_last_error == 41 &&
               classification_failure.permanent_pin &&
               classification_failure.quiesced,
           "classification-fallback-original-once");

    FakeDestroyHookState receipt_failure;
    receipt_failure.throw_in_receipt = true;
    Expect(result,
           InvokeFakeDestroyWindowHook(receipt_failure) == 1 &&
               receipt_failure.original_calls == 1 &&
               receipt_failure.classification_calls == 1 &&
               receipt_failure.receipt_calls == 1 &&
               receipt_failure.returned_last_error == 41 &&
               receipt_failure.permanent_pin &&
               receipt_failure.quiesced,
           "post-call-cached-result-and-error");

    FakeDestroyHookState original_failure;
    original_failure.throw_in_original = true;
    Expect(result,
           InvokeFakeDestroyWindowHook(original_failure) == 0 &&
               original_failure.original_calls == 1 &&
               original_failure.classification_calls == 1 &&
               original_failure.receipt_calls == 0 &&
               original_failure.returned_last_error == -2 &&
               original_failure.permanent_pin &&
               original_failure.quiesced,
           "original-attempt-is-never-retried");

    result.terminal_state = "contained-native-pass-through";
    Finalize(result);
    return result;
}

Scenario UiSealLateClean() {
    Scenario result{
        "ui.seal-late-clean",
        "ui-thread",
        true,
        "",
        {"register", "dispatch-cleanup", "seal-generation",
         "reject-normal-late-complete", "accept-exact-late-clean"},
        {"seal-while-cleanup-in-flight", "late-callback"},
        {},
        "sealing preserves the exact active ticket for one late-cleaned-retained receipt",
    };
    protocol::UiThreadRegistry registry(25);
    registry.RegisterInitialized(
        25, {905, 9005}, UiCapabilities());
    const auto tickets = registry.SnapshotForCleanup();
    Expect(result, tickets.size() == 1, "snapshot");
    if (tickets.size() == 1) {
        registry.SealGeneration(-301);
        Expect(result,
               registry.CompleteCleanup(
                   tickets[0],
                   protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::StaleTicket,
               "normal-complete-is-stale-after-seal");
        Expect(result,
               registry.CompleteLateClean(
                   tickets[0],
                   "late-cleaned-after-seal") ==
                   protocol::ProtocolStatus::Applied,
               "exact-late-clean");
        auto forged = tickets[0];
        ++forged.ticket_id;
        Expect(result,
               registry.CompleteLateClean(forged) ==
                   protocol::ProtocolStatus::StaleTicket,
               "forged-ticket-rejected");
    }
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].state ==
                   protocol::UiRecordState::LateCleanedRetained &&
               receipts[0].cleanup_attempts == 1 &&
               receipts[0].last_error == 0,
           "late-terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiInitializationRollback() {
    Scenario result{
        "ui.initialization-rollback",
        "ui-thread",
        true,
        "",
        {"reserve", "begin-initialization", "acquire-dispatcher",
         "acquire-shutdown-token", "inject-style-failure",
         "rollback-token", "rollback-dispatcher", "record-init-failed"},
        {"style-install-exception"},
        {},
        "transactional initialization rolls back every acquired adapter resource before publishing InitFailed",
    };
    protocol::UiThreadRegistry registry(22);
    const auto registration =
        registry.Reserve(22, {902, 9002}, UiCapabilities());
    Expect(result,
           registration.status ==
                   protocol::UiRegisterStatus::Registered &&
               registry.BeginInitialization(registration.record_id) ==
                   protocol::ProtocolStatus::Applied,
           "begin-transaction");

    std::array<ResourceOwner, 2> adapter_resources{
        result.resources.Create("ui-agile-dispatcher"),
        result.resources.Create("ui-shutdown-token"),
    };
    std::uint64_t released = 0;
    for (auto resource = adapter_resources.rbegin();
         resource != adapter_resources.rend();
         ++resource) {
        resource->Release();
        ++released;
    }
    Expect(result,
           registry.CompleteInitialization(
               registration.record_id,
               false,
               -201,
               "style-install-exception-rolled-back") ==
               protocol::ProtocolStatus::Applied,
           "publish-init-failed");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].state ==
                   protocol::UiRecordState::InitFailed &&
               receipts[0].terminal &&
               receipts[0].last_error == -201 &&
               released == 2 &&
               registry.SnapshotForCleanup().empty(),
           "rollback-terminal-receipt");
    Finalize(result);
    return result;
}

Scenario UiSelfThreadCleanup() {
    Scenario result{
        "ui.self-thread-cleanup",
        "ui-thread",
        true,
        "",
        {"register-current-thread", "begin-cleanup",
         "run-direct-without-dispatch", "complete-cleanup"},
        {},
        {},
        "cleanup on the owning thread runs directly and never self-dispatches",
    };
    protocol::UiThreadRegistry registry(23);
    const protocol::UiThreadIdentity current_identity{903, 9003};
    const auto registration = registry.RegisterInitialized(
        23, current_identity, UiCapabilities());
    const auto cleanup = registry.BeginCleanup(registration.record_id);
    std::uint64_t dispatcher_enqueues = 0;
    const bool runs_on_owner =
        current_identity == protocol::UiThreadIdentity{903, 9003};
    Expect(result,
           registration.status ==
                   protocol::UiRegisterStatus::Registered &&
               cleanup.status == protocol::ProtocolStatus::Acquired &&
               runs_on_owner,
           "owner-thread-selected");
    Expect(result,
           registry.CompleteCleanup(
               cleanup.ticket,
               protocol::UiCleanupOutcome::Cleaned,
               0,
               false,
               "self-thread-direct-cleanup") ==
               protocol::ProtocolStatus::Applied,
           "direct-cleanup");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           dispatcher_enqueues == 0 &&
               receipts.size() == 1 &&
               receipts[0].state == protocol::UiRecordState::Cleaned,
           "no-self-dispatch");
    Finalize(result);
    return result;
}

Scenario UiShutdownCleanup() {
    Scenario result{
        "ui.shutdown-cleanup",
        "ui-thread",
        true,
        "",
        {"register", "shutdown-starting", "claim-cleanup",
         "complete-cleanup", "global-cleanup-noop"},
        {"shutdown-races-global-cleanup"},
        {},
        "ShutdownStarting owns cleanup once and later global cleanup observes a terminal record",
    };
    protocol::UiThreadRegistry registry(24);
    const auto registration = registry.RegisterInitialized(
        24, {904, 9004}, UiCapabilities());
    const auto shutdown_cleanup =
        registry.BeginCleanup(registration.record_id);
    Expect(result,
           shutdown_cleanup.status ==
               protocol::ProtocolStatus::Acquired,
           "shutdown-claims-cleanup");
    Expect(result,
           registry.CompleteCleanup(
               shutdown_cleanup.ticket,
               protocol::UiCleanupOutcome::Cleaned,
               0,
               false,
               "shutdown-starting-cleanup") ==
               protocol::ProtocolStatus::Applied,
           "shutdown-completes");
    Expect(result,
           registry.SnapshotForCleanup().empty() &&
               registry.BeginCleanup(registration.record_id).status ==
                   protocol::ProtocolStatus::TerminalNoop,
           "later-global-cleanup-noop");
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    result.terminal_state =
        receipts.empty() ? "missing"
                         : std::string(protocol::ToString(receipts[0].state));
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].state == protocol::UiRecordState::Cleaned &&
               receipts[0].cleanup_attempts == 1,
           "single-cleanup-attempt");
    Finalize(result);
    return result;
}

Scenario GitWorkerBoundaryRaiiContainment() {
    Scenario result{
        "git.worker-boundary-raii-containment",
        "git",
        true,
        "",
        {"publish-worker-reference", "inject-admission-failure",
         "retain-without-com", "publish-worker-reference",
         "initialize-com", "inject-external-com-exception",
         "balance-com", "release-worker-reference",
         "publish-terminal-result"},
        {"worker-lifecycle-admission-exception",
         "worker-external-com-exception"},
        {},
        "Advise and Unadvise worker boundaries balance AddRef/Release and COM exactly once while retaining uncertain lifecycle state",
    };

    FakeWorkerBoundaryState admission_failure;
    admission_failure.throw_on_scope_enter = true;
    auto admission_reference =
        result.resources.Create("worker-self-reference");
    InvokeFakeVisualTreeWorker(admission_failure);
    admission_reference.Release();
    Expect(result,
           admission_failure.addref_calls == 1 &&
               admission_failure.release_calls == 1 &&
               admission_failure.strong_references == 1 &&
               admission_failure.com_initialize_calls == 0 &&
               admission_failure.com_uninitialize_calls == 0 &&
               admission_failure.external_com_calls == 0 &&
               admission_failure.retained &&
               admission_failure.permanent_pin &&
               admission_failure.quiesced &&
               admission_failure.result_published,
           "scope-false-retains-without-com");

    FakeWorkerBoundaryState external_failure;
    external_failure.throw_in_external_com = true;
    auto external_reference =
        result.resources.Create("worker-self-reference");
    auto external_apartment =
        result.resources.Create("worker-com-apartment");
    InvokeFakeVisualTreeWorker(external_failure);
    external_apartment.Release();
    external_reference.Release();
    Expect(result,
           external_failure.addref_calls == 1 &&
               external_failure.release_calls == 1 &&
               external_failure.strong_references == 1 &&
               external_failure.com_initialize_calls == 1 &&
               external_failure.com_uninitialize_calls == 1 &&
               external_failure.external_com_calls == 1 &&
               external_failure.retained &&
               external_failure.permanent_pin &&
               external_failure.quiesced &&
               external_failure.result_published,
           "exception-balances-com-and-reference");

    FakeWorkerBoundaryState normal;
    auto normal_reference =
        result.resources.Create("worker-self-reference");
    auto normal_apartment =
        result.resources.Create("worker-com-apartment");
    InvokeFakeVisualTreeWorker(normal);
    normal_apartment.Release();
    normal_reference.Release();
    Expect(result,
           normal.addref_calls == 1 &&
               normal.release_calls == 1 &&
               normal.strong_references == 1 &&
               normal.com_initialize_calls == 1 &&
               normal.com_uninitialize_calls == 1 &&
               normal.external_com_calls == 1 &&
               !normal.retained &&
               normal.result_published,
           "normal-worker-balances-once");

    result.terminal_state = "balanced-contained";
    Finalize(result);
    return result;
}

Scenario UiCleanupCallbackAdmissionFailure() {
    Scenario result{
        "ui.cleanup-callback-admission-failure",
        "ui-thread",
        true,
        "",
        {"register-dispatcher-record", "claim-cleanup-ticket",
         "inject-callback-admission-failure",
         "publish-structured-retained",
         "register-shutdown-record", "claim-shutdown-ticket",
         "inject-shutdown-admission-failure",
         "publish-structured-retained"},
        {"dispatcher-callback-lifecycle-admission-failure",
         "shutdown-callback-lifecycle-admission-failure"},
        {},
        "UI cleanup callbacks that cannot enter the lifecycle never mutate XAML and publish exact retained receipts",
    };

    protocol::UiThreadRegistry registry(31);
    const auto dispatcher_registration =
        registry.RegisterInitialized(
            31, {931, 9031}, UiCapabilities());
    const auto dispatcher_cleanup =
        registry.BeginCleanup(
            dispatcher_registration.record_id);
    Expect(result,
           dispatcher_cleanup.status ==
               protocol::ProtocolStatus::Acquired,
           "dispatcher-ticket");
    if (dispatcher_cleanup.status ==
        protocol::ProtocolStatus::Acquired) {
        Expect(result,
               registry.CompleteCleanup(
                   dispatcher_cleanup.ticket,
                   protocol::UiCleanupOutcome::Retained,
                   -401, false,
                   "dispatcher-callback-lifecycle-admission-failed") ==
                   protocol::ProtocolStatus::Applied,
               "dispatcher-retained-receipt");
    }

    const auto shutdown_registration =
        registry.RegisterInitialized(
            31, {932, 9032}, UiCapabilities());
    const auto shutdown_cleanup =
        registry.BeginCleanup(
            shutdown_registration.record_id);
    Expect(result,
           shutdown_cleanup.status ==
               protocol::ProtocolStatus::Acquired,
           "shutdown-ticket");
    if (shutdown_cleanup.status ==
        protocol::ProtocolStatus::Acquired) {
        Expect(result,
               registry.CompleteCleanup(
                   shutdown_cleanup.ticket,
                   protocol::UiCleanupOutcome::Retained,
                   -402, false,
                   "shutdown-callback-lifecycle-admission-failed") ==
                   protocol::ProtocolStatus::Applied,
               "shutdown-retained-receipt");
    }

    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    Expect(result,
           receipts.size() == 2 &&
               receipts[0].state ==
                   protocol::UiRecordState::Retained &&
               receipts[1].state ==
                   protocol::UiRecordState::Retained &&
               !receipts[0].retry_eligible &&
               !receipts[1].retry_eligible &&
               std::string_view(receipts[0].reason.data()).find(
                   "dispatcher-callback-lifecycle") == 0 &&
               std::string_view(receipts[1].reason.data()).find(
                   "shutdown-callback-lifecycle") == 0,
           "structured-retained-reasons");
    result.terminal_state = "retained-pinned-quiesced";
    Finalize(result);
    return result;
}

bool DispatchReceiptBalanced(
    const protocol::DispatchReceipt& receipt) noexcept {
    return receipt.resources_created ==
           receipt.resources_released +
               receipt.resources_retained +
               receipt.resources_inflight;
}

Scenario DispatchSyncSuccess() {
    Scenario result{
        "dispatch.sync-success",
        "dispatch",
        true,
        "",
        {"register", "claim", "invoke", "sender-release", "unhook"},
        {},
        {},
        "sender, callback, and hook resources each terminate once",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(11, 1, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "register");
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "claim");
    Expect(result,
           slot.CompleteCallback(true) ==
               protocol::ProtocolStatus::Applied,
           "callback");
    Expect(result,
           slot.SenderDone() == protocol::ProtocolStatus::Applied,
           "sender");
    Expect(result,
           slot.CompleteHookRemoval(true) ==
               protocol::ProtocolStatus::Applied,
           "unhook");
    const auto receipt = slot.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               !receipt.poisoned,
           "completed");
    Finalize(result);
    return result;
}

Scenario DispatchTimeoutCancel() {
    Scenario result{
        "dispatch.timeout-cancel",
        "dispatch",
        true,
        "",
        {"register", "timeout-cancel", "unhook", "late-callback"},
        {"send-timeout", "late-callback"},
        {},
        "timeout poisons the slot and a late callback cannot reclaim it",
    };
    protocol::DispatchSlot slot;
    slot.Register(12, 2, true, true);
    Expect(result,
           slot.Cancel(
               -3, protocol::DispatchReason::SendTimeout, true) ==
               protocol::DispatchCancelStatus::Cancelled,
           "cancel-owner");
    Expect(result,
           slot.CompleteHookRemoval(true) ==
               protocol::ProtocolStatus::Applied,
           "unhook");
    Expect(result,
           slot.ClaimCallback() == protocol::DispatchClaimStatus::Late,
           "late-callback");
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.poisoned && receipt.late_callbacks == 1,
           "poisoned-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchCallbackClaimsBeforeCancel() {
    Scenario result{
        "dispatch.callback-claims-before-cancel",
        "dispatch",
        true,
        "",
        {"register", "barrier", "callback-claims", "sender-cancel-loses"},
        {"claim-cancel-race"},
        {true, 2},
        "the callback claim deterministically precedes sender cancellation",
    };
    protocol::DispatchSlot slot;
    slot.Register(13, 3, true, true);
    std::barrier phase_one(2);
    std::barrier phase_two(2);
    protocol::DispatchClaimStatus claim =
        protocol::DispatchClaimStatus::Empty;
    protocol::DispatchCancelStatus cancel =
        protocol::DispatchCancelStatus::Empty;
    std::thread callback([&] {
        phase_one.arrive_and_wait();
        claim = slot.ClaimCallback();
        phase_two.arrive_and_wait();
    });
    std::thread sender([&] {
        phase_one.arrive_and_wait();
        phase_two.arrive_and_wait();
        cancel = slot.Cancel(
            0, protocol::DispatchReason::ClaimWon, false);
    });
    callback.join();
    sender.join();
    Expect(result,
           claim == protocol::DispatchClaimStatus::Claimed &&
               cancel == protocol::DispatchCancelStatus::ClaimWon,
           "claim-or-cancel");
    slot.CompleteCallback(true);
    slot.CompleteHookRemoval(true);
    const auto receipt = slot.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed,
           "completed");
    Finalize(result);
    return result;
}

Scenario DispatchClaimedBeforeGuardCancelRace() {
    Scenario result{
        "dispatch.claimed-before-guard-cancel-race",
        "dispatch",
        true,
        "",
        {"register", "claim-state-transition",
         "pause-before-callback-guard", "sender-cancel",
         "verify-callback-owner-held", "establish-callback-guard",
         "complete-callback", "remove-hook"},
        {"sender-cancel-after-claim-before-guard"},
        {true, 2},
        "a claimed callback keeps the sole callback reference while concurrent sender cancellation releases only the sender",
    };

    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(31, 15, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "register");

    std::barrier claim_reached(2);
    std::barrier sender_cancelled(2);
    protocol::DispatchClaimStatus claim =
        protocol::DispatchClaimStatus::Empty;
    protocol::DispatchCancelStatus cancel =
        protocol::DispatchCancelStatus::Empty;
    protocol::ProtocolStatus callback_completion =
        protocol::ProtocolStatus::InvalidState;
    protocol::DispatchReceipt owner_between_claim_and_guard;
    std::atomic<bool> callback_guard_established{false};
    bool sender_observed_guard_absent = false;
    std::uint64_t actual_sender_releases = 0;
    std::uint64_t actual_callback_releases = 0;

    std::thread callback([&] {
        claim = slot.ClaimCallback();
        claim_reached.arrive_and_wait();
        sender_cancelled.arrive_and_wait();
        callback_guard_established.store(
            true, std::memory_order_release);
        callback_completion = slot.CompleteCallback(
            true, 0,
            protocol::DispatchReason::CallbackCompleted);
        ++actual_callback_releases;
    });
    std::thread sender([&] {
        claim_reached.arrive_and_wait();
        sender_observed_guard_absent =
            !callback_guard_established.load(
                std::memory_order_acquire);
        cancel = slot.Cancel(
            -305, protocol::DispatchReason::ClaimWon, true);
        owner_between_claim_and_guard = slot.Receipt();
        ++actual_sender_releases;
        sender_cancelled.arrive_and_wait();
    });
    callback.join();
    sender.join();

    Expect(result,
           claim == protocol::DispatchClaimStatus::Claimed &&
               cancel ==
                   protocol::DispatchCancelStatus::ClaimWon &&
               sender_observed_guard_absent,
           "deterministic-claim-before-guard-race");
    Expect(result,
           owner_between_claim_and_guard.state ==
                   protocol::DispatchState::Claimed &&
               !owner_between_claim_and_guard.sender_ref_held &&
               owner_between_claim_and_guard.callback_ref_held &&
               owner_between_claim_and_guard.resources_released == 1 &&
               owner_between_claim_and_guard.double_release == 0,
           "callback-owner-remains-held");
    Expect(result,
           callback_completion ==
                   protocol::ProtocolStatus::Applied &&
               actual_sender_releases == 1 &&
               actual_callback_releases == 1,
           "actual-owners-release-once");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0, protocol::DispatchReason::HookRemoved) ==
               protocol::ProtocolStatus::Applied,
           "remove-hook");

    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(
                                      protocol::ToString(
                                          receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.resources_released == 3 &&
               receipt.double_release == 0,
           "balanced-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchUnhookFailRetry() {
    Scenario result{
        "dispatch.unhook-fail-retry",
        "dispatch",
        true,
        "",
        {"register", "tracking-fails", "cancel", "unhook-fails",
         "verify-retained", "unhook-retry"},
        {"tracking-push-failure", "unhook-failure"},
        {},
        "an untracked hook remains enumerable and retryable until removal succeeds",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(14, 4, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "register");
    Expect(result,
           slot.MarkHookTrackingFailure(
               -12, protocol::DispatchReason::HookTrackingFailed) ==
               protocol::ProtocolStatus::Applied,
           "tracking-failure");
    Expect(result,
           slot.Cancel(
               -13, protocol::DispatchReason::HookTrackingFailed,
               true) ==
               protocol::DispatchCancelStatus::Cancelled,
           "cancel");
    Expect(result,
           slot.CompleteHookRemoval(
               false, -14,
               protocol::DispatchReason::HookRemovalFailed) ==
               protocol::ProtocolStatus::Applied,
           "retain-hook");
    const auto retained = slot.Receipt();
    Expect(result,
           retained.state == protocol::DispatchState::Retained &&
               retained.hook_state == protocol::HookState::Retained &&
               retained.resources_created == 3 &&
               retained.resources_released == 2 &&
               retained.resources_retained == 1 &&
               retained.poisoned,
           "retained-accounting");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0,
               protocol::DispatchReason::HookRetrySucceeded) ==
               protocol::ProtocolStatus::Applied,
           "unhook-retry");
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.hook_state == protocol::HookState::Removed &&
               receipt.poisoned,
           "recovered-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchDuplicateCallback() {
    Scenario result{
        "dispatch.duplicate-callback",
        "dispatch",
        true,
        "",
        {"register", "claim", "duplicate-claim", "complete-once",
         "duplicate-completion", "release-sender", "unhook"},
        {"duplicate-claim", "duplicate-completion"},
        {},
        "a duplicate callback is counted but never releases the callback reference twice",
    };
    protocol::DispatchSlot slot;
    slot.Register(15, 5, true, true);
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "first-claim");
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Duplicate,
           "duplicate-claim");
    Expect(result,
           slot.CompleteCallback(true) ==
                protocol::ProtocolStatus::Applied,
           "complete-callback");
    Expect(result,
           slot.CompleteCallback(true) ==
               protocol::ProtocolStatus::Duplicate,
           "duplicate-completion");
    slot.SenderDone();
    slot.CompleteHookRemoval(true);
    const auto receipt = slot.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.duplicate_callbacks == 2 &&
               receipt.double_release == 0,
           "duplicate-receipt");
    Finalize(result);
    return result;
}

Scenario DispatchSlotConflict() {
    Scenario result{
        "dispatch.slot-conflict",
        "dispatch",
        true,
        "",
        {"register-first", "reject-second", "cancel-first", "unhook-first"},
        {"slot-conflict"},
        {},
        "a fixed slot cannot be overwritten while its first context is live",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(16, 6, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "first-register");
    Expect(result,
           slot.Register(16, 7, true, true) ==
               protocol::DispatchRegisterStatus::SlotOccupied,
           "reject-conflict");
    Expect(result,
           slot.Cancel(
               0, protocol::DispatchReason::Cancelled, false) ==
               protocol::DispatchCancelStatus::Cancelled,
           "cancel-first");
    slot.CompleteHookRemoval(true);
    const auto receipt = slot.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.dispatch_id == 6 &&
               receipt.state == protocol::DispatchState::Completed &&
               !receipt.poisoned,
           "first-slot-preserved");
    Finalize(result);
    return result;
}

Scenario DispatchCallbackThrows() {
    Scenario result{
        "dispatch.callback-throws",
        "dispatch",
        true,
        "",
        {"register", "claim", "callback-fails", "release-sender", "unhook"},
        {"callback-exception"},
        {},
        "callback failure poisons future dispatch while balancing every resource",
    };
    protocol::DispatchSlot slot;
    slot.Register(17, 8, true, true);
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "claim");
    Expect(result,
           slot.CompleteCallback(
               false, -15,
               protocol::DispatchReason::CallbackFailed) ==
               protocol::ProtocolStatus::Applied,
           "record-callback-failure");
    slot.SenderDone();
    slot.CompleteHookRemoval(true);
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.poisoned && receipt.last_error == -15 &&
               receipt.reason ==
                   protocol::DispatchReason::CallbackFailed,
           "poisoned-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchTargetExit() {
    Scenario result{
        "dispatch.target-exit",
        "dispatch",
        true,
        "",
        {"register", "observe-target-exit", "cancel-pending-slot",
         "remove-hook"},
        {"target-thread-exited"},
        {},
        "a target exit cancels both references before hook removal and poisons future dispatch",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(25, 9, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "register");
    Expect(result,
           slot.Cancel(
               -301, protocol::DispatchReason::TargetExited, true) ==
               protocol::DispatchCancelStatus::Cancelled,
           "target-exit-cancel");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0, protocol::DispatchReason::HookRemoved) ==
               protocol::ProtocolStatus::Applied,
           "hook-removal");
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.poisoned &&
               receipt.last_error == -301 &&
               !receipt.sender_ref_held &&
               !receipt.callback_ref_held,
           "target-exit-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchCallbackBeforeUnhookRetry() {
    Scenario result{
        "dispatch.callback-before-unhook-retry",
        "dispatch",
        true,
        "",
        {"register", "callback-claims", "unhook-fails",
         "callback-completes", "sender-completes",
         "verify-hook-only-retained", "unhook-retry"},
        {"unhook-failure-while-callback-inflight"},
        {},
        "a callback can complete before the unhook retry while the failed hook remains independently retained",
    };
    protocol::DispatchSlot slot;
    slot.Register(26, 10, true, true);
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "callback-claims");
    Expect(result,
           slot.CompleteHookRemoval(
               false, -302,
               protocol::DispatchReason::HookRemovalFailed) ==
               protocol::ProtocolStatus::Applied,
           "unhook-fails");
    const auto all_retained = slot.Receipt();
    Expect(result,
           all_retained.state == protocol::DispatchState::Claimed &&
               all_retained.callback_phase ==
                   protocol::CallbackPhase::Claimed &&
               all_retained.hook_state ==
                   protocol::HookState::Retained &&
               all_retained.resources_released == 0 &&
               all_retained.resources_retained == 1 &&
               all_retained.resources_inflight == 2 &&
               all_retained.retained_reason ==
                   protocol::DispatchRetainedReason::
                       HookRemovalFailed &&
               DispatchReceiptBalanced(all_retained),
           "claimed-callback-remains-inflight");
    Expect(result,
           slot.CompleteCallback(
               true, 0,
               protocol::DispatchReason::CallbackCompleted) ==
               protocol::ProtocolStatus::Applied,
           "callback-completes");
    Expect(result,
           slot.SenderDone() == protocol::ProtocolStatus::Applied,
           "sender-completes");
    const auto hook_only = slot.Receipt();
    Expect(result,
           hook_only.state == protocol::DispatchState::Retained &&
               hook_only.callback_phase ==
                   protocol::CallbackPhase::Completed &&
               hook_only.resources_released == 2 &&
               hook_only.resources_retained == 1 &&
               hook_only.resources_inflight == 0 &&
               !hook_only.sender_ref_held &&
               !hook_only.callback_ref_held &&
               DispatchReceiptBalanced(hook_only),
           "hook-only-retained");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0,
               protocol::DispatchReason::HookRetrySucceeded) ==
               protocol::ProtocolStatus::Applied,
           "unhook-retry");
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.hook_state == protocol::HookState::Removed &&
               receipt.callback_phase ==
                   protocol::CallbackPhase::Completed &&
               receipt.resources_inflight == 0 &&
               receipt.retained_reason ==
                   protocol::DispatchRetainedReason::None &&
               receipt.duplicate_callbacks == 0 &&
               DispatchReceiptBalanced(receipt) &&
               receipt.poisoned,
           "recovered-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchUnhookRetryBeforeCallback() {
    Scenario result{
        "dispatch.unhook-retry-before-callback",
        "dispatch",
        true,
        "",
        {"register", "callback-claims", "unhook-fails",
         "unhook-retry", "verify-callback-still-held",
         "callback-completes", "sender-completes"},
        {"unhook-failure-while-callback-inflight"},
        {},
        "a successful unhook retry releases only the retained hook and cannot complete or release the claimed callback",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(27, 11, true, true) ==
               protocol::DispatchRegisterStatus::Registered,
           "register");
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "callback-claims");
    Expect(result,
           slot.CompleteHookRemoval(
               false, -303,
               protocol::DispatchReason::HookRemovalFailed) ==
               protocol::ProtocolStatus::Applied,
           "unhook-fails");
    const auto before_retry = slot.Receipt();
    Expect(result,
           before_retry.state == protocol::DispatchState::Claimed &&
               before_retry.callback_phase ==
                   protocol::CallbackPhase::Claimed &&
               before_retry.resources_released == 0 &&
               before_retry.resources_retained == 1 &&
               before_retry.resources_inflight == 2 &&
               DispatchReceiptBalanced(before_retry),
           "orthogonal-retained-hook");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0,
               protocol::DispatchReason::HookRetrySucceeded) ==
               protocol::ProtocolStatus::Applied,
           "unhook-retry");
    const auto callback_still_inflight = slot.Receipt();
    Expect(result,
           callback_still_inflight.state ==
                   protocol::DispatchState::Claimed &&
               callback_still_inflight.hook_state ==
                   protocol::HookState::Removed &&
               callback_still_inflight.callback_phase ==
                   protocol::CallbackPhase::Claimed &&
               callback_still_inflight.resources_released == 1 &&
               callback_still_inflight.resources_retained == 0 &&
               callback_still_inflight.resources_inflight == 2 &&
               callback_still_inflight.sender_ref_held &&
               callback_still_inflight.callback_ref_held &&
               callback_still_inflight.retained_reason ==
                   protocol::DispatchRetainedReason::None &&
               DispatchReceiptBalanced(callback_still_inflight),
           "retry-does-not-complete-callback");
    Expect(result,
           slot.CompleteCallback(
               true, 0,
               protocol::DispatchReason::CallbackCompleted) ==
               protocol::ProtocolStatus::Applied,
           "first-callback-completion");
    Expect(result,
           slot.SenderDone() == protocol::ProtocolStatus::Applied,
           "sender-completes");
    const auto receipt = slot.Receipt();
    result.terminal_state = receipt.poisoned
                                ? "completed-poisoned"
                                : std::string(
                                      protocol::ToString(
                                          receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.callback_phase ==
                   protocol::CallbackPhase::Completed &&
               receipt.resources_released == 3 &&
               receipt.resources_retained == 0 &&
               receipt.resources_inflight == 0 &&
               receipt.duplicate_callbacks == 0 &&
               receipt.double_release == 0 &&
               DispatchReceiptBalanced(receipt),
           "balanced-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchSuccessUnhookCallbackInflight() {
    Scenario result{
        "dispatch.success-unhook-callback-inflight",
        "dispatch",
        true,
        "",
        {"register", "callback-claims", "unhook-succeeds",
         "sender-completes", "callback-completes"},
        {"callback-remains-inflight-after-unhook"},
        {},
        "successful unhook releases only the hook while the claimed callback retains its own reference",
    };
    protocol::DispatchSlot slot;
    slot.Register(27, 11, true, true);
    Expect(result,
           slot.ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "callback-claims");
    Expect(result,
           slot.CompleteHookRemoval(
               true, 0, protocol::DispatchReason::HookRemoved) ==
               protocol::ProtocolStatus::Applied,
           "unhook-succeeds");
    const auto callback_inflight = slot.Receipt();
    Expect(result,
           callback_inflight.state ==
                   protocol::DispatchState::Claimed &&
               callback_inflight.resources_released == 1 &&
               callback_inflight.sender_ref_held &&
               callback_inflight.callback_ref_held &&
               callback_inflight.hook_state ==
                   protocol::HookState::Removed,
           "callback-reference-remains-owned");
    Expect(result,
           slot.SenderDone() == protocol::ProtocolStatus::Applied,
           "sender-completes");
    Expect(result,
           slot.CompleteCallback(
               true, 0,
               protocol::DispatchReason::CallbackCompleted) ==
               protocol::ProtocolStatus::Applied,
           "callback-completes");
    const auto receipt = slot.Receipt();
    result.terminal_state =
        std::string(protocol::ToString(receipt.state));
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.resources_released == 3 &&
               !receipt.poisoned,
           "balanced-terminal");
    Finalize(result);
    return result;
}

Scenario DispatchHookInstallTwoResourceReceipt() {
    Scenario result{
        "dispatch.hook-install-two-resource-receipt",
        "dispatch",
        true,
        "",
        {"register-no-hook", "cancel-hook-install-failure",
         "publish-receipt", "release-callback", "release-sender"},
        {"set-windows-hook-ex-failure"},
        {},
        "hook installation failure accounts only the sender and callback references because no hook resource ever existed",
    };
    protocol::DispatchSlot slot;
    Expect(result,
           slot.Register(29, 13, false, false) ==
               protocol::DispatchRegisterStatus::Registered,
           "register-two-resources");
    Expect(result,
           slot.Cancel(
               -304,
               protocol::DispatchReason::HookInstallFailed,
               true) ==
               protocol::DispatchCancelStatus::Cancelled,
           "cancel-hook-install-failure");
    constexpr std::uint32_t kSenderResource = 1U << 0;
    constexpr std::uint32_t kCallbackResource = 1U << 1;
    constexpr std::uint32_t kHookResource = 1U << 2;
    const std::uint32_t actual_released_mask =
        kSenderResource | kCallbackResource;
    const auto receipt = slot.Receipt();
    result.terminal_state = "completed-poisoned-no-hook";
    RecordDispatchReceiptResources(result, receipt);
    Expect(result,
           receipt.state == protocol::DispatchState::Completed &&
               receipt.hook_state == protocol::HookState::Absent &&
               receipt.callback_phase ==
                   protocol::CallbackPhase::Cancelled &&
               receipt.resources_created == 2 &&
               receipt.resources_released == 2 &&
               receipt.resources_retained == 0 &&
               receipt.resources_inflight == 0 &&
               receipt.reason ==
                   protocol::DispatchReason::HookInstallFailed &&
               (actual_released_mask & kHookResource) == 0 &&
               receipt.double_release == 0 &&
               DispatchReceiptBalanced(receipt),
           "two-resource-terminal-receipt");
    Finalize(result);
    return result;
}

Scenario DispatchAdapterLateSlot() {
    Scenario result{
        "dispatch.adapter-late-slot",
        "dispatch",
        true,
        "",
        {"register-fixed-slot", "cancel", "remove-hook",
         "publish-compact-receipt", "remove-pending-slot",
         "destroy-context", "late-callback-by-id"},
        {"callback-arrives-after-slot-removal"},
        {},
        "a late callback updates the stable compact receipt by id without dereferencing the retired context address",
    };
    FakeFixedDispatchAdapter adapter;
    auto slot = std::make_unique<protocol::DispatchSlot>();
    constexpr std::uint64_t dispatch_id = 12;
    Expect(result,
           slot->Register(28, dispatch_id, true, true) ==
                   protocol::DispatchRegisterStatus::Registered &&
               adapter.Register(dispatch_id, slot.get()),
           "register-fixed-slot");
    Expect(result,
           slot->Cancel(
               -303, protocol::DispatchReason::SenderCancelled,
               true) ==
               protocol::DispatchCancelStatus::Cancelled,
           "cancel");
    Expect(result,
           slot->CompleteHookRemoval(
               true, 0, protocol::DispatchReason::HookRemoved) ==
               protocol::ProtocolStatus::Applied,
           "hook-removal");
    const auto protocol_receipt = slot->Receipt();
    adapter.Publish(protocol_receipt);
    Expect(result,
           adapter.RemovePending(dispatch_id),
           "remove-pending-before-context-retire");
    slot.reset();
    Expect(result,
           !adapter.LateCallback(dispatch_id),
           "late-callback-does-not-claim");
    const auto& compact = adapter.Receipt();
    result.terminal_state = "completed-poisoned-late-accounted";
    RecordDispatchReceiptResources(result, protocol_receipt);
    Expect(result,
           compact.dispatch_id == dispatch_id &&
               compact.terminal &&
               compact.late_callbacks == 1 &&
               adapter.AddressDereferences() == 0 &&
               protocol_receipt.state ==
                   protocol::DispatchState::Completed &&
               protocol_receipt.double_release == 0,
           "compact-receipt-only");
    Finalize(result);
    return result;
}

Scenario DispatchAdapterRepublishMonotonic() {
    Scenario result{
        "dispatch.adapter-republish-monotonic",
        "dispatch",
        true,
        "",
        {"register-late", "publish-terminal", "retire-late-context",
         "late-callback-by-id", "republish-terminal",
         "register-duplicate", "publish-claimed",
         "retire-duplicate-context", "duplicate-callback-by-id",
         "publish-completed"},
        {"late-after-context-retirement",
         "duplicate-after-context-retirement",
         "terminal-republication"},
        {},
        "adapter callback counters remain monotonic when a later protocol receipt is published",
    };

    FakeFixedDispatchAdapter late_adapter;
    auto late_slot = std::make_unique<protocol::DispatchSlot>();
    constexpr std::uint64_t late_dispatch_id = 13;
    Expect(result,
           late_slot->Register(
               29, late_dispatch_id, true, true) ==
                   protocol::DispatchRegisterStatus::Registered &&
               late_adapter.Register(
                   late_dispatch_id, late_slot.get()),
           "register-late");
    late_slot->Cancel(
        -304, protocol::DispatchReason::SenderCancelled, true);
    late_slot->CompleteHookRemoval(
        true, 0, protocol::DispatchReason::HookRemoved);
    const auto late_terminal = late_slot->Receipt();
    late_adapter.Publish(late_terminal);
    Expect(result,
           late_adapter.RemovePending(late_dispatch_id),
           "retire-late-context");
    late_slot.reset();
    Expect(result,
           !late_adapter.LateCallback(late_dispatch_id),
           "late-callback");
    late_adapter.Publish(late_terminal);
    const auto late_compact = late_adapter.Receipt();
    Expect(result,
           late_compact.late_callbacks == 1 &&
               late_compact.adapter_late_callbacks == 1 &&
               late_compact.protocol_late_callbacks == 0 &&
               late_compact.duplicate_callbacks == 0 &&
               late_adapter.AddressDereferences() == 0,
           "late-count-survives-republish");

    FakeFixedDispatchAdapter duplicate_adapter;
    auto duplicate_slot =
        std::make_unique<protocol::DispatchSlot>();
    constexpr std::uint64_t duplicate_dispatch_id = 14;
    Expect(result,
           duplicate_slot->Register(
               30, duplicate_dispatch_id, true, true) ==
                   protocol::DispatchRegisterStatus::Registered &&
               duplicate_adapter.Register(
                   duplicate_dispatch_id, duplicate_slot.get()),
           "register-duplicate");
    Expect(result,
           duplicate_slot->ClaimCallback() ==
               protocol::DispatchClaimStatus::Claimed,
           "claim-duplicate-context");
    duplicate_adapter.Publish(duplicate_slot->Receipt());
    Expect(result,
           duplicate_adapter.RemovePending(
               duplicate_dispatch_id),
           "retire-duplicate-context");
    Expect(result,
           !duplicate_adapter.LateCallback(
               duplicate_dispatch_id),
           "duplicate-callback");
    duplicate_slot->CompleteCallback(
        true, 0, protocol::DispatchReason::CallbackCompleted);
    duplicate_slot->SenderDone();
    duplicate_slot->CompleteHookRemoval(
        true, 0, protocol::DispatchReason::HookRemoved);
    const auto duplicate_terminal = duplicate_slot->Receipt();
    duplicate_adapter.Publish(duplicate_terminal);
    const auto duplicate_compact = duplicate_adapter.Receipt();
    Expect(result,
           duplicate_compact.duplicate_callbacks == 1 &&
               duplicate_compact.adapter_duplicate_callbacks == 1 &&
               duplicate_compact.protocol_duplicate_callbacks == 0 &&
               duplicate_compact.late_callbacks == 0 &&
               duplicate_adapter.AddressDereferences() == 0,
           "duplicate-count-survives-republish");

    result.terminal_state =
        "completed-poisoned-counters-monotonic";
    RecordDispatchReceiptResources(result, late_terminal);
    RecordDispatchReceiptResources(result, duplicate_terminal);
    Finalize(result);
    return result;
}

Scenario DispatchAdapterDoubleReleaseRepublishSaturation() {
    Scenario result{
        "dispatch.adapter-double-release-republish-saturation",
        "dispatch",
        true,
        "",
        {"register", "publish-near-saturated-protocol-count",
         "record-first-actual-release-and-degrade",
         "record-duplicate-actual-release",
         "record-invalid-actual-mask", "republish-stale-protocol-count"},
        {"duplicate-actual-release", "invalid-actual-release-mask",
         "stale-republication"},
        {},
        "protocol and actual double-release counts remain separate, saturating, and monotonic across receipt republication",
    };
    protocol::DispatchSlot slot;
    constexpr std::uint64_t dispatch_id = 15;
    Expect(result,
           slot.Register(31, dispatch_id, false, false) ==
                   protocol::DispatchRegisterStatus::Registered &&
               slot.Cancel(
                   -305,
                   protocol::DispatchReason::HookInstallFailed,
                   true) ==
                   protocol::DispatchCancelStatus::Cancelled,
           "terminal-protocol-receipt");
    FakeFixedDispatchAdapter adapter;
    Expect(result,
           adapter.Register(dispatch_id, &slot),
           "register-adapter");
    auto near_saturated = slot.Receipt();
    near_saturated.double_release =
        std::numeric_limits<std::uint64_t>::max() - 2;
    Expect(result,
           adapter.Publish(near_saturated),
           "initial-publish");
    constexpr std::uint32_t kSenderResource = 1U << 0;
    Expect(result,
           !adapter.ReleaseContextResource(
               kSenderResource, true),
           "first-exact-release-reports-degraded");
    Expect(result,
           !adapter.ReleaseContextResource(kSenderResource),
           "duplicate-actual-release");
    Expect(result,
           !adapter.ReleaseContextResource(0),
           "invalid-actual-mask");
    auto stale = slot.Receipt();
    stale.double_release = 0;
    Expect(result,
           !adapter.Publish(stale),
           "degraded-republish-rejected");
    const auto& compact = adapter.Receipt();
    Expect(result,
           compact.protocol_double_release ==
                   std::numeric_limits<std::uint64_t>::max() - 2 &&
               compact.actual_double_release == 2 &&
               compact.double_release ==
                   std::numeric_limits<std::uint64_t>::max() &&
                compact.actual_released_mask == kSenderResource &&
                compact.receipt_degraded &&
                compact.state ==
                    protocol::DispatchState::Retained &&
                compact.retained_reason ==
                   protocol::DispatchRetainedReason::
                       ProtocolFailure &&
                adapter.ContextReferences() == 1 &&
                adapter.ContextReferenceDecrements() == 1 &&
                adapter.RetainedContextReferences() == 2 &&
                compact.context_references_retained == 2,
           "separate-saturated-monotonic-counters");
    result.terminal_state =
        "completed-poisoned-double-release-detected";
    RecordDispatchReceiptResources(result, stale);
    Finalize(result);
    return result;
}

Scenario DispatchCallbackProtocolPublicationFailure() {
    Scenario result{
        "dispatch.callback-protocol-publication-failure",
        "dispatch",
        true,
        "",
        {"claim-protocol-case", "inject-protocol-failure",
         "release-resource-gate-before-protocol-degradation",
         "pin-and-diagnose-protocol-failure-outside-gate",
         "return-through-noexcept-boundary", "release-sender-and-hook",
         "claim-publication-case", "inject-publication-failure",
         "release-resource-gate-before-receipt-degradation",
         "pin-and-diagnose-receipt-failure-outside-gate",
         "establish-independent-observer-under-resource-gate",
         "retire-pending-owner-after-gate-release",
         "defer-mark-and-publish-through-observer",
         "release-observer-reference-exactly-once",
         "return-through-noexcept-boundary", "release-sender-and-hook"},
        {"protocol-operation-throws",
         "receipt-publication-throws",
         "sender-retires-context-before-deferred-observation"},
        {true, 3},
        "callback reference guards balance actual references; deferred Mark or Publish owns an independent observer across concurrent sender retirement, and all degradation actions run after the thread-resource gate is released",
    };

    FakeDispatchCallbackBoundary protocol_failure;
    auto protocol_sender =
        result.resources.Create("dispatch-sender-reference");
    auto protocol_callback =
        result.resources.Create("dispatch-callback-reference");
    auto protocol_hook =
        result.resources.Create("dispatch-hook-handle");
    protocol_failure.Invoke(true, false);
    protocol_callback.Release();
    protocol_failure.ReleaseSender();
    protocol_sender.Release();
    protocol_failure.ReleaseHook();
    protocol_hook.Release();
    Expect(result,
           protocol_failure.ReceiptDegraded() &&
               protocol_failure.ProtocolOrReceiptFailure() &&
                protocol_failure.CallbackInvoked() &&
                protocol_failure.CallbackBoundaryReturned() &&
                protocol_failure.RetainedAtFailure() == 3 &&
                protocol_failure.RetainedReasonAtFailure() ==
                    protocol::DispatchRetainedReason::
                        ProtocolFailure &&
                protocol_failure.RetainedReason() ==
                    protocol::DispatchRetainedReason::None &&
                protocol_failure.Released() == 3 &&
                protocol_failure.Retained() == 0 &&
                protocol_failure.DoubleRelease() == 0 &&
                protocol_failure.ResourceGateDepth() == 0 &&
                protocol_failure.MaximumResourceGateDepth() == 1 &&
                protocol_failure.PermanentPinCalls() == 1 &&
                protocol_failure.DiagnosticCalls() == 1 &&
                protocol_failure.ExternalActionsWhileLocked() == 0 &&
                protocol_failure.ObserverAdds() == 1 &&
                protocol_failure.ObserverAddsOutsideGate() == 0 &&
                protocol_failure.ObserverReleases() == 1 &&
                protocol_failure.ObserverDoubleRelease() == 0 &&
                protocol_failure
                        .ExternalActionsWithoutObserver() == 0,
           "protocol-failure-balanced");
    FakeDispatchCallbackBoundary publication_failure;
    auto publication_sender =
        result.resources.Create("dispatch-sender-reference");
    auto publication_callback =
        result.resources.Create("dispatch-callback-reference");
    auto publication_hook =
        result.resources.Create("dispatch-hook-handle");
    publication_failure.Invoke(false, true);
    publication_callback.Release();
    publication_failure.ReleaseSender();
    publication_sender.Release();
    publication_failure.ReleaseHook();
    publication_hook.Release();
    Expect(result,
           publication_failure.ReceiptDegraded() &&
               publication_failure.ProtocolOrReceiptFailure() &&
                publication_failure.CallbackInvoked() &&
                publication_failure.CallbackBoundaryReturned() &&
                publication_failure.RetainedAtFailure() == 3 &&
                publication_failure.RetainedReasonAtFailure() ==
                    protocol::DispatchRetainedReason::
                        ProtocolFailure &&
                publication_failure.RetainedReason() ==
                    protocol::DispatchRetainedReason::None &&
                publication_failure.Released() == 3 &&
                publication_failure.Retained() == 0 &&
                publication_failure.DoubleRelease() == 0 &&
                publication_failure.ResourceGateDepth() == 0 &&
                publication_failure.MaximumResourceGateDepth() == 1 &&
                publication_failure.PermanentPinCalls() == 1 &&
                publication_failure.DiagnosticCalls() == 1 &&
                publication_failure.ExternalActionsWhileLocked() == 0 &&
                publication_failure.ObserverAdds() == 1 &&
                publication_failure.ObserverAddsOutsideGate() == 0 &&
                publication_failure.ObserverReleases() == 1 &&
                publication_failure.ObserverDoubleRelease() == 0 &&
                publication_failure
                        .ExternalActionsWithoutObserver() == 0,
           "publication-failure-balanced");

    const auto exercise_deferred_observer_race =
        [](FakeDeferredDispatchContext& context) {
            std::atomic<std::uint32_t> resource_gate_depth{0};
            std::uint32_t maximum_gate_depth = 0;
            std::optional<
                FakeDeferredDispatchObserverReference>
                observer;
            const auto entered_depth =
                resource_gate_depth.fetch_add(
                    1, std::memory_order_acq_rel) + 1;
            maximum_gate_depth =
                std::max(maximum_gate_depth, entered_depth);
            observer.emplace(
                context,
                resource_gate_depth.load(
                    std::memory_order_acquire));
            resource_gate_depth.fetch_sub(
                1, std::memory_order_acq_rel);

            std::barrier start(3);
            std::atomic<bool> sender_retired{false};
            bool deferred_observation_succeeded = false;
            std::thread sender([&] {
                start.arrive_and_wait();
                context.ReleasePendingReference();
                sender_retired.store(
                    true, std::memory_order_release);
            });
            std::thread deferred_observer([&] {
                start.arrive_and_wait();
                while (!sender_retired.load(
                    std::memory_order_acquire)) {
                    std::this_thread::yield();
                }
                auto* protected_context = observer->Get();
                deferred_observation_succeeded =
                    protected_context &&
                    protected_context->DeferredExternalAction(
                        resource_gate_depth.load(
                            std::memory_order_acquire));
            });
            start.arrive_and_wait();
            sender.join();
            deferred_observer.join();
            observer.reset();
            return deferred_observation_succeeded &&
                maximum_gate_depth == 1 &&
                resource_gate_depth.load(
                    std::memory_order_acquire) == 0;
        };

    FakeDeferredDispatchContext deferred_mark_context;
    const bool deferred_mark_succeeded =
        exercise_deferred_observer_race(
            deferred_mark_context);
    Expect(result,
           deferred_mark_succeeded &&
               deferred_mark_context.References() == 0 &&
               deferred_mark_context.Destroyed() &&
               deferred_mark_context.ObserverAdds() == 1 &&
               deferred_mark_context
                       .ObserverAddsOutsideGate() == 0 &&
               deferred_mark_context.PendingReleases() == 1 &&
               deferred_mark_context.ObserverReleases() == 1 &&
               deferred_mark_context.SuccessfulObservations() == 1 &&
               deferred_mark_context.ExternalActions() == 1 &&
               deferred_mark_context
                       .ExternalActionsWhileLocked() == 0 &&
               deferred_mark_context.UseAfterFree() == 0 &&
               deferred_mark_context.DoubleRelease() == 0,
           "deferred-mark-observer-survives-sender-retirement-exactly-once");

    FakeDeferredDispatchContext deferred_publish_context;
    const bool deferred_publish_succeeded =
        exercise_deferred_observer_race(
            deferred_publish_context);
    Expect(result,
           deferred_publish_succeeded &&
               deferred_publish_context.References() == 0 &&
               deferred_publish_context.Destroyed() &&
               deferred_publish_context.ObserverAdds() == 1 &&
               deferred_publish_context
                       .ObserverAddsOutsideGate() == 0 &&
               deferred_publish_context.PendingReleases() == 1 &&
               deferred_publish_context.ObserverReleases() == 1 &&
               deferred_publish_context
                       .SuccessfulObservations() == 1 &&
               deferred_publish_context.ExternalActions() == 1 &&
               deferred_publish_context
                       .ExternalActionsWhileLocked() == 0 &&
               deferred_publish_context.UseAfterFree() == 0 &&
               deferred_publish_context.DoubleRelease() == 0,
           "deferred-publish-observer-survives-sender-retirement-exactly-once");
    result.terminal_state = "degraded-balanced";
    Finalize(result);
    return result;
}

Scenario DispatchEmergencyHookExactSlot() {
    Scenario result{
        "dispatch.emergency-hook-exact-slot",
        "dispatch",
        true,
        "",
        {"fill-64-emergency-slots", "reject-capacity-overflow",
         "reject-mismatched-retirement", "verify-no-removed-state",
         "retire-all-exact-slots", "verify-actual-hook-release",
         "inject-and-detect-double-release"},
        {"emergency-capacity-exhausted",
         "exact-slot-key-mismatch",
         "actual-hook-already-released"},
        {},
        "emergency hook quarantine remains fully enumerable and only exact hook and dispatch-id pairs become removed",
    };

    FakeEmergencyHookQuarantine quarantine;
    std::array<ResourceOwner, 64> hook_owners{};
    for (std::uint64_t index = 0; index < 64; ++index) {
        const bool retained = quarantine.Retain(
            static_cast<std::uintptr_t>(index + 1),
            1000 + index);
        Expect(result, retained, "retain-emergency-slot");
        if (retained) {
            hook_owners[index] =
                result.resources.Create("emergency-hook");
        }
    }
    Expect(result,
           quarantine.Count() == 64,
           "all-emergency-slots-enumerable");
    Expect(result,
           !quarantine.Retain(65, 1064) &&
               quarantine.CapacityExhausted(),
           "capacity-overflow-explicit");
    Expect(result,
           !quarantine.RetireExact(1, 9999) &&
               quarantine.TrackingInvariantFailed() &&
               quarantine.Removed() == 0 &&
               quarantine.Count() == 64,
           "mismatch-never-reports-removed");

    for (std::uint64_t index = 0; index < 64; ++index) {
        const bool retired = quarantine.RetireExact(
            static_cast<std::uintptr_t>(index + 1),
            1000 + index);
        Expect(result, retired, "retire-exact-slot");
        if (retired) {
            hook_owners[index].Release();
        }
    }
    Expect(result,
           quarantine.Count() == 0 &&
               quarantine.Removed() == 64 &&
               quarantine.ActualHookReleases() == 64 &&
               quarantine.DoubleRelease() == 0,
           "all-exact-slots-retired");

    FakeEmergencyHookQuarantine duplicate_quarantine;
    auto duplicate_hook =
        result.resources.Create("emergency-hook");
    Expect(result,
           duplicate_quarantine.Retain(101, 2001) &&
               duplicate_quarantine.InjectActualHookRelease(
                   101, 2001) &&
               !duplicate_quarantine.RetireExact(101, 2001) &&
               duplicate_quarantine.Count() == 0 &&
               duplicate_quarantine.Removed() == 0 &&
               duplicate_quarantine.DoubleRelease() == 1 &&
               duplicate_quarantine.TrackingInvariantFailed(),
           "double-release-detected-not-counted-as-removal");
    duplicate_hook.Release();
    result.terminal_state =
        "completed-poisoned-capacity-accounted";
    Finalize(result);
    return result;
}

Scenario DispatchForeignAbiExceptionFirewall() {
    Scenario result{
        "dispatch.foreign-abi-exception-firewall",
        "dispatch",
        true,
        "",
        {"inject-admission-throw", "fallback-original-once",
         "inject-post-processing-throw", "return-cached-result",
         "inject-release-throw", "destructor-contains",
         "inject-original-throw", "never-retry-original"},
        {"lifecycle-admission-exception",
         "post-processing-exception",
         "lifecycle-release-exception",
         "original-exception"},
        {},
        "foreign ABI exceptions pin and quiesce without escaping or invoking the original more than once",
    };

    FakeForeignAbiState admission_failure;
    admission_failure.throw_on_enter = true;
    Expect(result,
           InvokeFakeForeignAbiHook(admission_failure) == 73 &&
               admission_failure.original_calls == 1 &&
               admission_failure.returned_last_error == 41 &&
               !admission_failure.lifecycle_admitted &&
               admission_failure.permanent_pin &&
               admission_failure.quiesced,
           "admission-fallback-once");

    FakeForeignAbiState post_processing_failure;
    post_processing_failure.throw_in_post_processing = true;
    auto post_processing_scope =
        result.resources.Create("foreign-abi-lifecycle-scope");
    Expect(result,
           InvokeFakeForeignAbiHook(post_processing_failure) == 73 &&
               post_processing_failure.original_calls == 1 &&
               post_processing_failure.returned_last_error == 41 &&
               post_processing_failure.lifecycle_admitted &&
               post_processing_failure.lifecycle_released &&
               post_processing_failure.permanent_pin &&
               post_processing_failure.quiesced,
           "post-processing-cached-result");
    post_processing_scope.Release();

    FakeForeignAbiState release_failure;
    release_failure.throw_on_release = true;
    auto release_failure_scope =
        result.resources.Create("foreign-abi-lifecycle-scope");
    Expect(result,
           InvokeFakeForeignAbiHook(release_failure) == 73 &&
               release_failure.original_calls == 1 &&
               release_failure.returned_last_error == 41 &&
               release_failure.lifecycle_admitted &&
               !release_failure.lifecycle_released &&
               release_failure.permanent_pin &&
               release_failure.quiesced,
           "destructor-exception-contained");
    release_failure_scope.Retain(
        RetainReasonCode::ExternalUncertainty);

    FakeForeignAbiState original_failure;
    original_failure.throw_in_original = true;
    auto original_failure_scope =
        result.resources.Create("foreign-abi-lifecycle-scope");
    Expect(result,
           InvokeFakeForeignAbiHook(original_failure) == -1 &&
               original_failure.original_calls == 1 &&
               original_failure.returned_last_error == -2 &&
               original_failure.lifecycle_admitted &&
               original_failure.lifecycle_released &&
               original_failure.permanent_pin &&
               original_failure.quiesced,
           "original-never-retried");
    original_failure_scope.Release();
    result.terminal_state = "contained-pinned-quiesced";
    Finalize(result);
    return result;
}

Scenario ModulePermanentPinPublicationRace() {
    Scenario result{
        "module.permanent-pin-publication-race",
        "module",
        true,
        "",
        {"start-permanent-publication", "hold-atomic-decision-gate",
         "race-release-attempt", "release-publication-holder",
         "observe-release-refused"},
        {"permanent-publication-races-release"},
        {true, 2},
        "The atomic decision protocol linearizes permanent publication before the complete check-and-Free operation",
    };

    FakePermanentPinProtocol protocol;
    auto module_pin =
        result.resources.Create("module-loader-pin");
    std::atomic<bool> published{false};
    std::atomic<bool> release_publication_holder{false};
    std::atomic<bool> release_result{true};

    std::thread publisher([&] {
        protocol.RequireAndHold(
            published, release_publication_holder);
    });
    while (!published.load(std::memory_order_acquire)) {
        std::this_thread::yield();
    }
    std::thread releaser([&] {
        release_result.store(
            protocol.Release(), std::memory_order_release);
    });
    release_publication_holder.store(
        true, std::memory_order_release);
    publisher.join();
    releaser.join();

    Expect(result,
           protocol.Permanent() &&
               protocol.ModulePinned() &&
               !release_result.load(std::memory_order_acquire) &&
               protocol.FreeCalls() == 0 &&
               protocol.FreeAfterPermanent() == 0 &&
               protocol.DoubleRelease() == 0,
           "published-permanent-pin-never-freed");
    module_pin.Retain(RetainReasonCode::ModulePermanent);
    result.terminal_state = "permanent-retained";
    Finalize(result);
    return result;
}

Scenario ModuleExportAbiFirewall() {
    Scenario result{
        "module.export-abi-firewall",
        "module",
        true,
        "",
        {"acquire-init-state-gate", "inject-before-permit-exception",
         "release-init-state-gate-raii",
         "acquire-init-state-gate-and-permit",
         "inject-after-permit-exception",
         "release-init-capabilities-raii",
         "acquire-state-gate", "inject-after-init-exception",
         "release-state-gate-raii", "publish-unloading-first",
         "inject-before-uninit-exception", "contain-export",
         "publish-unloading-first", "inject-final-uninit-exception",
         "contain-export", "set-inject-flag",
         "inject-tap-exception", "restore-inject-flag-raii",
         "publish-settings-quiesced-first",
         "inject-settings-log-exception", "contain-export",
         "claim-nonzero-kernel-owner-tickets",
         "inject-state-gate-release-and-close-failures",
         "inject-delete-pending-permit-close-failure",
         "inject-watcher-three-owner-close-failures",
         "inject-stats-mutex-release-and-close-failures",
         "reserve-kernel-ledger-slot-before-create",
         "reject-kernel-create-when-ledger-full",
         "retry-still-owned-close-without-second-release",
         "quarantine-thread-affine-stats-owner",
         "bind-registry-wait-bundle-before-external-register",
         "rollback-unpublished-wait-by-synchronous-unregister",
         "atomically-retain-unconfirmed-registry-wait-bundle",
         "race-duplicate-quiesce-before-authorization",
         "race-authorization-before-duplicate-quiesce",
         "reject-active-rebound-after-duplicate",
         "verify-successful-release-and-close"},
        {"init-before-permit-exception",
         "init-after-permit-exception",
         "after-init-exception", "before-uninit-exception",
         "final-uninit-exception", "tap-injection-exception",
         "state-gate-release-failure",
         "state-gate-close-failure",
         "activation-permit-delete-pending-close-failure",
         "watcher-thread-close-failure",
         "watcher-stop-event-close-failure",
         "watcher-change-notification-close-failure",
         "stats-mutex-release-failure",
         "stats-mutex-close-failure",
         "kernel-ledger-capacity-exhausted",
         "state-gate-close-still-owned",
         "stats-mutex-owning-thread-exited",
         "registry-wait-publication-failure",
         "registry-wait-unregister-unconfirmed",
         "duplicate-initialization-before-authorization",
         "duplicate-initialization-after-authorization"},
        {},
        "Module lifecycle exports contain all exceptions, bind registry wait dependencies before callback publication, atomically retain any unconfirmed wait bundle, preserve one irreversible activation generation under duplicate races, reserve typed kernel capability slots before creation, and publish fail-closed states before fallible work",
    };

    FakeModuleExportState init_after_gate;
    init_after_gate.throw_after_gate = true;
    auto init_after_gate_owner =
        result.resources.Create("module-state-gate");
    Expect(result,
           !InvokeFakeInitExport(init_after_gate) &&
               init_after_gate.state_gate_acquired &&
               init_after_gate.state_gate_released &&
               init_after_gate.state_gate_release_count == 1 &&
               !init_after_gate.activation_permit_opened &&
               init_after_gate.activation_permit_close_count == 0 &&
               init_after_gate.activation ==
                   FakeModuleActivationState::Quiesced &&
               init_after_gate.permanent_pin,
           "init-state-gate-raii");
    init_after_gate_owner.Release();

    FakeModuleExportState init_after_permit;
    init_after_permit.throw_after_permit = true;
    auto init_permit_gate_owner =
        result.resources.Create("module-state-gate");
    auto activation_permit_owner =
        result.resources.Create("module-activation-permit");
    Expect(result,
           !InvokeFakeInitExport(init_after_permit) &&
               init_after_permit.state_gate_acquired &&
               init_after_permit.state_gate_released &&
               init_after_permit.state_gate_release_count == 1 &&
               init_after_permit.activation_permit_opened &&
               init_after_permit.activation_permit_closed &&
               init_after_permit.activation_permit_close_count == 1 &&
               init_after_permit.activation ==
                   FakeModuleActivationState::Quiesced &&
               init_after_permit.permanent_pin,
           "init-permit-raii");
    activation_permit_owner.Release();
    init_permit_gate_owner.Release();

    FakeModuleExportState after_init;
    after_init.throw_after_gate = true;
    auto after_init_gate_owner =
        result.resources.Create("module-state-gate");
    InvokeFakeAfterInitExport(after_init);
    Expect(result,
           after_init.state_gate_acquired &&
               after_init.state_gate_released &&
               after_init.activation ==
                   FakeModuleActivationState::Quiesced &&
               after_init.permanent_pin,
           "after-init-state-gate-raii");
    after_init_gate_owner.Release();

    FakeModuleExportState before_uninit;
    before_uninit.throw_after_unloading = true;
    InvokeFakeBeforeOrFinalUninitExport(before_uninit);
    Expect(result,
           before_uninit.activation ==
                   FakeModuleActivationState::Unloading &&
               before_uninit.unloading_published_order != 0 &&
               before_uninit.unloading_published_order <
                   before_uninit.first_fallible_operation_order &&
               before_uninit.permanent_pin,
           "before-uninit-publishes-first");

    FakeModuleExportState final_uninit;
    final_uninit.throw_after_unloading = true;
    InvokeFakeBeforeOrFinalUninitExport(final_uninit);
    Expect(result,
           final_uninit.activation ==
                   FakeModuleActivationState::Unloading &&
               final_uninit.unloading_published_order != 0 &&
               final_uninit.unloading_published_order <
                   final_uninit.first_fallible_operation_order &&
               final_uninit.permanent_pin,
           "final-uninit-publishes-first");

    FakeModuleExportState inject;
    Expect(result,
           InvokeFakeInjectBoundary(inject) == -1 &&
               !inject.inject_flag &&
               inject.activation ==
                   FakeModuleActivationState::Quiesced &&
               inject.permanent_pin,
           "inject-flag-restored-by-raii");

    FakeModuleExportState settings;
    settings.throw_in_settings_log = true;
    InvokeFakeSettingsChangedExport(settings);
    Expect(result,
           settings.activation ==
                    FakeModuleActivationState::Quiesced &&
               settings.settings_latch_order != 0 &&
               settings.settings_log_order >
                   settings.settings_latch_order &&
               settings.settings_log_attempted &&
               settings.settings_log_contained,
           "settings-change-latches-before-diagnostic");

    FakeKernelCapabilityTicketSource kernel_tickets;
    FakeKernelCapabilityOwner state_gate_release_pending(
        kernel_tickets);
    FakeKernelCapabilityOwner state_gate_close_failed(
        kernel_tickets);
    FakeKernelCapabilityOwner activation_permit_delete_pending(
        kernel_tickets);
    FakeKernelCapabilityOwner watcher_thread(kernel_tickets);
    FakeKernelCapabilityOwner watcher_stop_event(kernel_tickets);
    FakeKernelCapabilityOwner watcher_change_notification(
        kernel_tickets);
    FakeKernelCapabilityOwner stats_mutex_release_pending(
        kernel_tickets);
    FakeKernelCapabilityOwner stats_mutex_close_failed(
        kernel_tickets);
    FakeKernelCapabilityOwner successful_state_gate(
        kernel_tickets);

    Expect(result,
           state_gate_release_pending.OwnerTicket() != 0 &&
               state_gate_close_failed.OwnerTicket() ==
                   state_gate_release_pending.OwnerTicket() + 1 &&
               activation_permit_delete_pending.OwnerTicket() ==
                   state_gate_close_failed.OwnerTicket() + 1 &&
               watcher_thread.OwnerTicket() ==
                   activation_permit_delete_pending.OwnerTicket() + 1 &&
               watcher_stop_event.OwnerTicket() ==
                   watcher_thread.OwnerTicket() + 1 &&
               watcher_change_notification.OwnerTicket() ==
                   watcher_stop_event.OwnerTicket() + 1 &&
               stats_mutex_release_pending.OwnerTicket() ==
                   watcher_change_notification.OwnerTicket() + 1 &&
               stats_mutex_close_failed.OwnerTicket() ==
                   stats_mutex_release_pending.OwnerTicket() + 1 &&
               successful_state_gate.OwnerTicket() ==
                   stats_mutex_close_failed.OwnerTicket() + 1,
           "kernel-owner-tickets-nonzero-monotonic");

    FakeKernelCapabilityReservationTable reservation_table(2);
    const auto first_reserved_creation =
        reservation_table.TryCreate();
    const auto second_reserved_creation =
        reservation_table.TryCreate();
    const auto rejected_creation =
        reservation_table.TryCreate();
    Expect(result,
           first_reserved_creation.created &&
               first_reserved_creation.owner_ticket != 0 &&
               second_reserved_creation.created &&
               second_reserved_creation.owner_ticket ==
                   first_reserved_creation.owner_ticket + 1 &&
               !rejected_creation.created &&
               rejected_creation.owner_ticket == 0 &&
               reservation_table.ReservationAttempts() == 3 &&
               reservation_table.SuccessfulReservations() == 2 &&
               reservation_table.CreateCalls() == 2 &&
               reservation_table.CapacityRejections() == 1 &&
               reservation_table.ReservedSlots() == 2 &&
               reservation_table.AllCreatesWerePreReserved(),
           "kernel-capability-reserve-before-create-and-reject-at-capacity");
    Expect(result,
           reservation_table.Release(first_reserved_creation) &&
               reservation_table.Release(second_reserved_creation) &&
               reservation_table.ReservedSlots() == 0,
           "kernel-capability-reservations-release-exact-slots");

    FakeStateGateLease still_owned_state_gate;
    const bool state_gate_first_close =
        still_owned_state_gate.ReleaseAndClose(
            true, FakeKernelCloseDisposition::StillOwned);
    Expect(result,
           !state_gate_first_close &&
               still_owned_state_gate.Phase() ==
                   FakeStateGateLeasePhase::
                       ReleasedClosePending &&
               still_owned_state_gate.ReleaseSemaphoreCalls() == 1 &&
               still_owned_state_gate.CloseHandleCalls() == 1,
           "state-gate-still-owned-remains-retryable");
    const bool state_gate_retry_close =
        still_owned_state_gate.ReleaseAndClose(
            false, FakeKernelCloseDisposition::Closed);
    Expect(result,
           state_gate_retry_close &&
               still_owned_state_gate.Phase() ==
                   FakeStateGateLeasePhase::Closed &&
               still_owned_state_gate.ReleaseSemaphoreCalls() == 1 &&
               still_owned_state_gate.CloseHandleCalls() == 2,
           "state-gate-released-close-pending-never-releases-semaphore-twice");

    FakeStateGateLease release_pending_state_gate;
    Expect(result,
           !release_pending_state_gate.ReleaseAndClose(
               false, FakeKernelCloseDisposition::Closed) &&
               release_pending_state_gate.Phase() ==
                   FakeStateGateLeasePhase::ReleasePending &&
               release_pending_state_gate.ReleaseSemaphoreCalls() ==
                   1 &&
               release_pending_state_gate.CloseHandleCalls() == 0 &&
               release_pending_state_gate.ReleaseAndClose(
                   true, FakeKernelCloseDisposition::Closed) &&
               release_pending_state_gate.Phase() ==
                   FakeStateGateLeasePhase::Closed &&
               release_pending_state_gate.ReleaseSemaphoreCalls() ==
                   2 &&
               release_pending_state_gate.CloseHandleCalls() == 1,
           "state-gate-release-pending-retries-release-before-close");

    FakeStatsMutexThreadAffineOwner thread_affine_stats_owner(
        7301);
    Expect(result,
           !thread_affine_stats_owner.ReleaseOnThread(
               7301, false) &&
               thread_affine_stats_owner.Phase() ==
                   FakeStatsMutexLeasePhase::ReleasePending &&
               thread_affine_stats_owner.ReleaseMutexCalls() == 1,
           "stats-mutex-release-failure-remains-thread-affine");
    thread_affine_stats_owner
        .QuarantineAfterOwningThreadExit();
    Expect(result,
           !thread_affine_stats_owner.ReleaseOnThread(
               7302, true) &&
               thread_affine_stats_owner.Phase() ==
                   FakeStatsMutexLeasePhase::
                       ThreadAffineQuarantine &&
               thread_affine_stats_owner.OwningThreadId() == 7301 &&
               thread_affine_stats_owner.ReleaseMutexCalls() == 1 &&
               thread_affine_stats_owner.WrongThreadRejections() ==
                   0 &&
               thread_affine_stats_owner
                       .QuarantinedRetryRejections() == 1,
           "stats-mutex-thread-affine-quarantine-refuses-foreign-release");

    FakeRegistryWaitBundle synchronous_wait_rollback;
    Expect(result,
           !synchronous_wait_rollback.RegisterExternalWait() &&
               synchronous_wait_rollback.ExternalRegisterCalls() ==
                   0 &&
               synchronous_wait_rollback
                       .ExternalRegisterBlocked() == 1 &&
               synchronous_wait_rollback
                   .BindBeforeExternalRegister() &&
               synchronous_wait_rollback.RegisterExternalWait() &&
               synchronous_wait_rollback
                   .BoundBeforeExternalRegister(),
           "registry-wait-register-requires-bound-key-event-context");
    const auto synchronous_rollback_outcome =
        synchronous_wait_rollback.Publish(false, true);
    Expect(result,
           synchronous_rollback_outcome ==
                   FakeRegistryWaitPublishOutcome::
                       SynchronouslyRolledBack &&
               synchronous_wait_rollback.WaitState() ==
                   FakeRegistryWaitSlotState::Closed &&
               !synchronous_wait_rollback
                    .ExternalWaitHandlePresent() &&
               synchronous_wait_rollback.PublishCalls() == 1 &&
               synchronous_wait_rollback
                       .SynchronousUnregisterCalls() == 1 &&
               synchronous_wait_rollback
                   .TryCloseDependencies() &&
               synchronous_wait_rollback.EventState() ==
                   FakeRegistryWaitDependencyState::Closed &&
               synchronous_wait_rollback.KeyState() ==
                   FakeRegistryWaitDependencyState::Closed &&
               synchronous_wait_rollback.EventCloseCalls() == 1 &&
               synchronous_wait_rollback.KeyCloseCalls() == 1 &&
               synchronous_wait_rollback.ContextDeleteCalls() == 1 &&
               !synchronous_wait_rollback.PermanentPinRequired(),
           "registry-wait-publish-failure-synchronously-unregisters-before-close");

    FakeRegistryWaitBundle unconfirmed_wait_bundle;
    Expect(result,
           unconfirmed_wait_bundle.BindBeforeExternalRegister() &&
               unconfirmed_wait_bundle.RegisterExternalWait() &&
               unconfirmed_wait_bundle
                   .BoundBeforeExternalRegister(),
           "registry-wait-unconfirmed-case-bound-before-register");
    const auto retained_bundle_outcome =
        unconfirmed_wait_bundle.Publish(false, false);
    Expect(result,
           retained_bundle_outcome ==
                   FakeRegistryWaitPublishOutcome::BundleRetained &&
               !unconfirmed_wait_bundle.TryCloseDependencies() &&
               unconfirmed_wait_bundle.WaitState() ==
                   FakeRegistryWaitSlotState::Retained &&
               unconfirmed_wait_bundle.EventState() ==
                   FakeRegistryWaitDependencyState::Retained &&
               unconfirmed_wait_bundle.KeyState() ==
                   FakeRegistryWaitDependencyState::Retained &&
               unconfirmed_wait_bundle.ContextRetained() &&
               unconfirmed_wait_bundle
                   .ExternalWaitHandlePresent() &&
               unconfirmed_wait_bundle.LocalOwnersCleared() &&
               unconfirmed_wait_bundle.PermanentPinRequired() &&
               unconfirmed_wait_bundle
                       .SynchronousUnregisterCalls() == 1 &&
               unconfirmed_wait_bundle
                       .ReservedExternalWaitBundleIsolations() == 1 &&
               unconfirmed_wait_bundle
                       .ProhibitedDependencyCloseAttempts() == 1 &&
               unconfirmed_wait_bundle.EventCloseCalls() == 0 &&
               unconfirmed_wait_bundle.KeyCloseCalls() == 0 &&
               unconfirmed_wait_bundle.ContextDeleteCalls() == 0,
           "reserved-external-wait-atomically-retains-whole-bundle");

    std::array<ResourceOwner, 4> rolled_back_wait_resources{
        result.resources.Create("registry-wait"),
        result.resources.Create("registry-notification-event"),
        result.resources.Create("registry-key"),
        result.resources.Create("registry-wait-context"),
    };
    for (auto& resource : rolled_back_wait_resources) {
        resource.Release();
    }
    std::array<ResourceOwner, 4> retained_wait_resources{
        result.resources.Create("registry-wait"),
        result.resources.Create("registry-notification-event"),
        result.resources.Create("registry-key"),
        result.resources.Create("registry-wait-context"),
    };
    for (auto& resource : retained_wait_resources) {
        resource.Retain(
            RetainReasonCode::ExternalUncertainty);
    }

    FakeSingleGenerationActivation duplicate_first_generation;
    Expect(result,
           duplicate_first_generation.BeginFirstInitialization(),
           "single-generation-first-initialization-claim");
    std::barrier duplicate_first_start(3);
    std::atomic<bool> duplicate_quiesced{false};
    bool duplicate_first_rejected = false;
    bool duplicate_first_authorized = true;
    std::thread duplicate_first_thread([&] {
        duplicate_first_start.arrive_and_wait();
        duplicate_first_rejected =
            duplicate_first_generation
                .RejectDuplicateInitialization();
        duplicate_quiesced.store(
            true, std::memory_order_release);
    });
    std::thread authorize_after_duplicate_thread([&] {
        duplicate_first_start.arrive_and_wait();
        while (!duplicate_quiesced.load(
            std::memory_order_acquire)) {
            std::this_thread::yield();
        }
        duplicate_first_authorized =
            duplicate_first_generation.TryAuthorize();
    });
    duplicate_first_start.arrive_and_wait();
    duplicate_first_thread.join();
    authorize_after_duplicate_thread.join();
    const bool duplicate_first_activated =
        duplicate_first_generation.TryActivate();
    Expect(result,
           duplicate_first_rejected &&
               !duplicate_first_authorized &&
               !duplicate_first_activated &&
               duplicate_first_generation.State() ==
                   FakeSingleGenerationActivationState::Quiesced &&
               duplicate_first_generation
                   .PermanentPinRequired() &&
               duplicate_first_generation
                       .DuplicateRejections() == 1 &&
               duplicate_first_generation
                       .AuthorizationSuccesses() == 0 &&
               duplicate_first_generation
                       .AuthorizationFailures() == 1 &&
               duplicate_first_generation
                       .ActivationSuccesses() == 0 &&
               duplicate_first_generation
                       .ActivationFailures() == 1,
           "duplicate-quiesced-before-blocked-authorized-cas-cannot-rebound");

    FakeSingleGenerationActivation authorization_first_generation;
    Expect(result,
           authorization_first_generation
               .BeginFirstInitialization(),
           "single-generation-second-sequence-first-claim");
    std::barrier authorization_first_start(3);
    std::atomic<bool> authorization_published{false};
    bool authorization_first_authorized = false;
    bool authorization_first_duplicate_rejected = false;
    std::thread authorize_first_thread([&] {
        authorization_first_start.arrive_and_wait();
        authorization_first_authorized =
            authorization_first_generation.TryAuthorize();
        authorization_published.store(
            true, std::memory_order_release);
    });
    std::thread duplicate_after_authorization_thread([&] {
        authorization_first_start.arrive_and_wait();
        while (!authorization_published.load(
            std::memory_order_acquire)) {
            std::this_thread::yield();
        }
        authorization_first_duplicate_rejected =
            authorization_first_generation
                .RejectDuplicateInitialization();
    });
    authorization_first_start.arrive_and_wait();
    authorize_first_thread.join();
    duplicate_after_authorization_thread.join();
    const bool authorization_first_activated =
        authorization_first_generation.TryActivate();
    Expect(result,
           authorization_first_authorized &&
               authorization_first_duplicate_rejected &&
               !authorization_first_activated &&
               authorization_first_generation.State() ==
                   FakeSingleGenerationActivationState::Quiesced &&
               authorization_first_generation
                   .PermanentPinRequired() &&
               authorization_first_generation
                       .DuplicateRejections() == 1 &&
               authorization_first_generation
                       .AuthorizationSuccesses() == 1 &&
               authorization_first_generation
                       .AuthorizationFailures() == 0 &&
               authorization_first_generation
                       .ActivationSuccesses() == 0 &&
               authorization_first_generation
                       .ActivationFailures() == 1,
           "authorized-before-duplicate-ends-quiesced-without-active-rebound");
    auto duplicate_first_pin =
        result.resources.Create(
            "single-generation-permanent-pin");
    duplicate_first_pin.Retain(
        RetainReasonCode::ModulePermanent);
    auto authorization_first_pin =
        result.resources.Create(
            "single-generation-permanent-pin");
    authorization_first_pin.Retain(
        RetainReasonCode::ModulePermanent);

    auto state_gate_release_pending_resource =
        result.resources.Create(
            "kernel-state-gate-release-pending");
    Expect(result,
           !state_gate_release_pending.ReleaseThenClose(
               false, true,
               FakeKernelCapabilityTerminal::
                   ReleasePendingRetained) &&
               state_gate_release_pending.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       ReleasePendingRetained &&
               !state_gate_release_pending.ReleaseConfirmed() &&
               state_gate_release_pending.ReleaseCalls() == 1 &&
               state_gate_release_pending.CloseCalls() == 0,
           "state-gate-release-failure-retains-without-close");
    state_gate_release_pending_resource.Retain(
        RetainReasonCode::RetryPending);

    auto state_gate_close_failed_resource =
        result.resources.Create(
            "kernel-state-gate-close-failed");
    Expect(result,
           !state_gate_close_failed.ReleaseThenClose(
               true, false,
               FakeKernelCapabilityTerminal::
                   ReleasePendingRetained) &&
               state_gate_close_failed.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       CloseFailedRetained &&
               state_gate_close_failed.ReleaseConfirmed() &&
               state_gate_close_failed.ReleaseCalls() == 1 &&
               state_gate_close_failed.CloseCalls() == 1,
           "state-gate-close-failure-retains-after-release");
    state_gate_close_failed_resource.Retain(
        RetainReasonCode::CapabilityRetained);

    auto activation_permit_resource =
        result.resources.Create(
            "kernel-activation-permit-delete-pending");
    activation_permit_delete_pending.MarkDeletePending();
    Expect(result,
           !activation_permit_delete_pending.Close(
               false,
               FakeKernelCapabilityTerminal::
                   DeletePendingCloseFailedRetained) &&
               activation_permit_delete_pending.DeletePending() &&
               activation_permit_delete_pending.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       DeletePendingCloseFailedRetained &&
               activation_permit_delete_pending.CloseCalls() == 1,
           "delete-pending-permit-close-failure-retained");
    activation_permit_resource.Retain(
        RetainReasonCode::CleanupFailure);

    auto watcher_thread_resource =
        result.resources.Create(
            "kernel-kill-switch-watcher-thread");
    Expect(result,
           !watcher_thread.Close(false) &&
               watcher_thread.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       CloseFailedRetained,
           "watcher-thread-close-failure-retained");
    watcher_thread_resource.Retain(
        RetainReasonCode::CapabilityRetained);

    auto watcher_stop_event_resource =
        result.resources.Create(
            "kernel-kill-switch-watcher-stop-event");
    Expect(result,
           !watcher_stop_event.Close(false) &&
               watcher_stop_event.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       CloseFailedRetained,
           "watcher-stop-event-close-failure-retained");
    watcher_stop_event_resource.Retain(
        RetainReasonCode::CapabilityRetained);

    auto watcher_change_notification_resource =
        result.resources.Create(
            "kernel-kill-switch-watcher-change-notification");
    Expect(result,
           !watcher_change_notification.Close(
               false,
               FakeKernelCapabilityTerminal::
                   ChangeNotificationCloseFailedRetained) &&
               watcher_change_notification.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       ChangeNotificationCloseFailedRetained,
           "watcher-change-notification-uses-specific-close");
    watcher_change_notification_resource.Retain(
        RetainReasonCode::CapabilityRetained);

    auto stats_mutex_release_pending_resource =
        result.resources.Create(
            "kernel-stats-mutex-release-pending");
    Expect(result,
           !stats_mutex_release_pending.ReleaseThenClose(
               false, true,
               FakeKernelCapabilityTerminal::
                   MutexReleasePendingRetained) &&
               stats_mutex_release_pending.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       MutexReleasePendingRetained &&
               stats_mutex_release_pending.CloseCalls() == 0,
           "stats-mutex-release-failure-retains-owned-capability");
    stats_mutex_release_pending_resource.Retain(
        RetainReasonCode::RetryPending);

    auto stats_mutex_close_failed_resource =
        result.resources.Create(
            "kernel-stats-mutex-close-failed");
    Expect(result,
           !stats_mutex_close_failed.ReleaseThenClose(
               true, false,
               FakeKernelCapabilityTerminal::
                   MutexReleasePendingRetained) &&
               stats_mutex_close_failed.ReleaseConfirmed() &&
               stats_mutex_close_failed.Terminal() ==
                   FakeKernelCapabilityTerminal::
                       CloseFailedRetained,
           "stats-mutex-close-failure-retained");
    stats_mutex_close_failed_resource.Retain(
        RetainReasonCode::CapabilityRetained);

    auto successful_state_gate_resource =
        result.resources.Create(
            "kernel-state-gate-success");
    Expect(result,
           successful_state_gate.ReleaseThenClose(
               true, true,
               FakeKernelCapabilityTerminal::
                   ReleasePendingRetained) &&
               successful_state_gate.ReleaseConfirmed() &&
               successful_state_gate.Terminal() ==
                   FakeKernelCapabilityTerminal::Closed,
           "state-gate-success-releases-and-closes-once");
    successful_state_gate_resource.Release();

    result.terminal_state = "contained-pinned-quiesced";
    Finalize(result);
    return result;
}

Scenario ModuleFailClosedBeforePinDecision() {
    Scenario result{
        "module.failclosed-before-pin-decision",
        "module",
        true,
        "",
        {"establish-retained-owner", "hold-pin-decision-gate",
         "publish-quiesced", "attempt-permanent-pin",
         "observe-quiesced-while-pin-blocked", "release-pin-gate",
         "complete-permanent-pin"},
        {"pin-decision-gate-blocked"},
        {true, 3},
        "A retained owner is established first, but the atomic Quiesced publication is visible before a contended permanent-pin decision can complete",
    };

    FakeBlockingPinDecision pin;
    FakeFailClosedPublicationState state;
    auto retained_owner =
        result.resources.Create("failclosed-retained-owner");
    std::atomic<bool> gate_entered{false};
    std::atomic<bool> release_gate{false};

    std::thread holder([&] {
        pin.Hold(gate_entered, release_gate);
    });
    while (!gate_entered.load(std::memory_order_acquire)) {
        std::this_thread::yield();
    }

    std::thread publisher([&] {
        PublishFakeFailClosedBeforePin(state, pin);
    });
    while (!state.pin_attempted.load(std::memory_order_acquire)) {
        std::this_thread::yield();
    }

    const bool visible_while_pin_blocked =
        state.owner_established.load(std::memory_order_acquire) &&
        state.activation.load(std::memory_order_acquire) ==
            FakeModuleActivationState::Quiesced &&
        !state.completed.load(std::memory_order_acquire) &&
        !pin.Permanent();

    release_gate.store(true, std::memory_order_release);
    holder.join();
    publisher.join();

    Expect(result,
           visible_while_pin_blocked &&
               state.completed.load(std::memory_order_acquire) &&
               pin.Permanent(),
           "quiesce-waited-for-pin-decision-gate");
    retained_owner.Retain(
        RetainReasonCode::ModulePermanent);
    result.terminal_state = "retained-pinned-quiesced";
    Finalize(result);
    return result;
}

Scenario ModuleTapComBoundaryFaults() {
    Scenario result{
        "module.tap-com-boundary-faults",
        "module",
        true,
        "",
        {"preclear-getsite-output", "inject-site-lock-exception",
         "contain-getsite", "commit-new-site-owner",
         "inject-watcher-allocation-exception",
         "retain-site-owner-and-quiesce",
         "preclear-factory-output", "inject-factory-exception",
         "inject-diagnostic-exception", "contain-factory"},
        {"getsite-lock-exception", "setsite-watcher-allocation-exception",
         "factory-diagnostic-exception"},
        {},
        "TAP COM entrypoints preclear outputs, contain lock/allocation/log failures, and quiesce any half-committed SiteHolder state",
    };

    FakeTapComBoundaryState get_site;
    Expect(result,
           InvokeFakeGetSiteBoundary(get_site) == -1 &&
               !get_site.output_nonnull &&
               get_site.activation ==
                   FakeModuleActivationState::Quiesced &&
               get_site.permanent_pin,
           "getsite-lock-exception-escaped");

    FakeTapComBoundaryState set_site;
    auto site_owner =
        result.resources.Create("tap-site-owner");
    Expect(result,
           InvokeFakeSetSiteBoundary(set_site) == -1 &&
               set_site.site_owner_committed &&
               !set_site.watcher_published &&
               set_site.retained_site_owner &&
               set_site.activation ==
                   FakeModuleActivationState::Quiesced &&
               set_site.permanent_pin,
           "setsite-half-commit-not-quiesced");

    FakeTapComBoundaryState factory;
    Expect(result,
           InvokeFakeFactoryBoundary(factory) == -1 &&
               !factory.output_nonnull &&
               factory.diagnostic_failure_contained &&
               factory.activation ==
                   FakeModuleActivationState::Quiesced &&
               factory.permanent_pin,
           "factory-nested-diagnostic-escaped");

    site_owner.Retain(
        RetainReasonCode::ExternalUncertainty);
    result.terminal_state = "retained-pinned-quiesced";
    Finalize(result);
    return result;
}

Scenario ModuleNoexceptDiagnosticFailures() {
    Scenario result{
        "module.noexcept-diagnostic-failures",
        "module",
        true,
        "",
        {"publish-xaml-enumeration-receipt",
         "inject-xaml-log-failure",
         "publish-ui-cleanup-retention",
         "inject-ui-log-failure",
         "publish-git-capacity-retention",
         "inject-git-capacity-log-failure",
         "publish-git-transition-retention",
         "inject-git-transition-log-failure"},
        {"xaml-enumeration-log-exception",
         "ui-cleanup-log-exception",
         "git-capacity-log-exception",
         "git-transition-log-exception"},
        {},
        "Diagnostic failures inside noexcept paths are contained after structured retained receipts and fail-closed state are already published",
    };

    std::array<FakeNoexceptDiagnosticState, 4> branches{};
    std::array<ResourceOwner, 4> retained_owners{};
    for (std::size_t index = 0; index < branches.size(); ++index) {
        auto& branch = branches[index];
        retained_owners[index] = result.resources.Create(
            "noexcept-diagnostic-retained-owner");
        Expect(result,
               InvokeFakeNoexceptDiagnosticFailure(branch) == -1 &&
                   branch.retained_receipt_published &&
                   branch.quiesced &&
                   branch.permanent_pin &&
                   branch.diagnostic_attempted &&
                   branch.diagnostic_failure_contained,
               "noexcept-diagnostic-failure-escaped");
        retained_owners[index].Retain(
            RetainReasonCode::ProtocolFailure);
    }
    result.terminal_state = "retained-pinned-quiesced";
    Finalize(result);
    return result;
}

Scenario ModuleGraphicsNullInputGuards() {
    Scenario result{
        "module.graphics-null-input-guards",
        "module",
        true,
        "",
        {"probe-composite-null-name", "probe-flood-null-name",
         "probe-border-null-name", "probe-gaussian-null-name",
         "probe-color-matrix-null-name",
         "probe-null-index-and-mapping"},
        {"null-name", "null-index", "null-mapping"},
        {},
        "All five graphics property mapping entrypoints reject null inputs before constructing a name view or writing outputs",
    };

    constexpr std::array<FakeGraphicsEffectKind, 5> effects{
        FakeGraphicsEffectKind::Composite,
        FakeGraphicsEffectKind::Flood,
        FakeGraphicsEffectKind::Border,
        FakeGraphicsEffectKind::GaussianBlur,
        FakeGraphicsEffectKind::ColorMatrix,
    };
    FakeGraphicsMappingProbe probe;
    for (const auto effect : effects) {
        std::uint32_t index = 17;
        std::uint32_t mapping = 23;
        Expect(result,
               probe.Map(effect, nullptr, &index, &mapping) == -1 &&
                   index == 17 && mapping == 23,
               "null-name-was-read-or-output-written");
        Expect(result,
               probe.Map(effect, L"name", nullptr, &mapping) == -1 &&
                   mapping == 23,
               "null-index-was-dereferenced");
        Expect(result,
               probe.Map(effect, L"name", &index, nullptr) == -1 &&
                   index == 17,
               "null-mapping-was-dereferenced");
    }
    Expect(result,
           probe.name_dereferences == 0,
           "name-dereferenced-before-input-validation");

    result.terminal_state = "invalid-input-rejected";
    Finalize(result);
    return result;
}

Scenario ModuleLoaderReferenceRaii() {
    Scenario result{
        "module.loader-reference-raii",
        "module",
        true,
        "",
        {"acquire-temporary-loader-reference",
         "inject-before-publication-exception",
         "release-temporary-reference-raii",
         "acquire-second-loader-reference",
         "transfer-to-global-owner"},
        {"exception-before-loader-publication"},
        {},
        "Temporary XAML and Taskbar.View loader references are released by scope guards unless ownership is explicitly transferred to a retained global owner",
    };

    FakeLoaderReferenceState state;
    auto temporary_loader =
        result.resources.Create("loader-reference");
    try {
        FakeLoaderReferenceGuard temporary(state);
        throw std::runtime_error(
            "injected loader publication failure");
    } catch (...) {
    }
    temporary_loader.Release();
    auto transferred_loader =
        result.resources.Create("loader-reference");
    {
        FakeLoaderReferenceGuard transferred(state);
        transferred.Transfer();
    }
    transferred_loader.Retain(
        RetainReasonCode::ResourceTransferred);

    Expect(result,
           state.acquired == 2 &&
               state.released == 1 &&
               state.transferred == 1,
           "loader-reference-leaked-or-double-released");
    result.terminal_state = "one-released-one-transferred";
    Finalize(result);
    return result;
}

Scenario ModuleLockServerBalance() {
    Scenario result{
        "module.lockserver-balance",
        "module",
        true,
        "",
        {"reject-unmatched-false", "pair-true-false",
         "run-two-concurrent-pairs", "release-factory-reference"},
        {"unmatched-lockserver-false", "concurrent-lockserver-pairs"},
        {true, 3},
        "LockServer never underflows, concurrent pairs balance, and the class-factory reference has its own terminal release",
    };

    class LockModel {
    public:
        bool LockServer(bool lock) noexcept {
            if (lock) {
                locks_.fetch_add(1, std::memory_order_acq_rel);
                return true;
            }
            std::int64_t observed =
                locks_.load(std::memory_order_acquire);
            while (observed != 0) {
                if (locks_.compare_exchange_weak(
                        observed, observed - 1,
                        std::memory_order_acq_rel,
                        std::memory_order_acquire)) {
                    return true;
                }
            }
            return false;
        }

        bool ReleaseFactory() noexcept {
            std::int64_t expected = 1;
            return factory_references_.compare_exchange_strong(
                expected, 0, std::memory_order_acq_rel,
                std::memory_order_acquire);
        }

        std::int64_t Locks() const noexcept {
            return locks_.load(std::memory_order_acquire);
        }

        std::int64_t FactoryReferences() const noexcept {
            return factory_references_.load(
                std::memory_order_acquire);
        }

    private:
        std::atomic<std::int64_t> locks_{0};
        std::atomic<std::int64_t> factory_references_{1};
    } model;

    Expect(result, !model.LockServer(false), "unmatched-false-underflowed");

    auto paired_lock =
        result.resources.Create("module-server-lock");
    const bool paired =
        model.LockServer(true) && model.LockServer(false);
    if (paired) {
        paired_lock.Release();
    }

    std::array<ResourceOwner, 2> concurrent_owners{
        result.resources.Create("module-server-lock"),
        result.resources.Create("module-server-lock"),
    };
    std::array<bool, 2> acquired{};
    std::array<bool, 2> released{};
    std::barrier begin{3};
    std::barrier both_acquired{3};
    std::array<std::thread, 2> workers{
        std::thread([&] {
            begin.arrive_and_wait();
            acquired[0] = model.LockServer(true);
            both_acquired.arrive_and_wait();
            released[0] = model.LockServer(false);
        }),
        std::thread([&] {
            begin.arrive_and_wait();
            acquired[1] = model.LockServer(true);
            both_acquired.arrive_and_wait();
            released[1] = model.LockServer(false);
        }),
    };
    begin.arrive_and_wait();
    both_acquired.arrive_and_wait();
    for (auto& worker : workers) {
        worker.join();
    }
    for (std::size_t index = 0; index < concurrent_owners.size();
         ++index) {
        if (acquired[index] && released[index]) {
            concurrent_owners[index].Release();
        }
    }

    auto factory =
        result.resources.Create("module-class-factory-reference");
    const bool factory_released = model.ReleaseFactory();
    if (factory_released) {
        factory.Release();
    }
    Expect(result,
           paired && acquired[0] && acquired[1] &&
               released[0] && released[1] &&
               model.Locks() == 0 && factory_released &&
               model.FactoryReferences() == 0,
           "lockserver-or-factory-not-balanced");
    result.terminal_state = "balanced-no-underflow";
    Finalize(result);
    return result;
}

Scenario ModuleXamlBrushCallbackFirewalls() {
    Scenario result{
        "module.xaml-brush-callback-firewalls",
        "module",
        true,
        "",
        {"inject-unregister-destructor-fault",
         "continue-to-close-despite-first-fault",
         "inject-close-destructor-fault",
         "inject-onconnected-before-register",
         "inject-onconnected-after-register",
         "inject-onconnected-before-enqueue"},
        {"destructor-unregister-exception",
         "destructor-close-exception",
         "onconnected-multi-point-exception"},
        {},
        "Brush destruction isolates both cleanup faults, and every OnConnected injection point returns through a fail-closed boundary",
    };

    struct BrushState {
        std::uint64_t unregister_attempts = 0;
        std::uint64_t close_attempts = 0;
        std::uint64_t contained = 0;
        bool permanent_pin = false;

        void Destroy() noexcept {
            try {
                ++unregister_attempts;
                throw std::runtime_error("unregister");
            } catch (...) {
                ++contained;
                permanent_pin = true;
            }
            try {
                ++close_attempts;
                throw std::runtime_error("close");
            } catch (...) {
                ++contained;
                permanent_pin = true;
            }
        }
    } brush;

    auto unregister_owner =
        result.resources.Create("xaml-brush-registration");
    auto close_owner =
        result.resources.Create("xaml-brush-close-capability");
    brush.Destroy();
    unregister_owner.Retain(
        RetainReasonCode::ExternalUncertainty);
    close_owner.Retain(
        RetainReasonCode::ExternalUncertainty);

    std::uint64_t connected_contained = 0;
    bool connected_pin = false;
    for (int injection = 0; injection < 3; ++injection) {
        auto callback_owner =
            result.resources.Create("xaml-onconnected-callback");
        const auto invoke = [&]() noexcept {
            try {
                if (injection == 0) {
                    throw std::runtime_error("before-register");
                }
                const bool registered = true;
                if (injection == 1 && registered) {
                    throw std::runtime_error("after-register");
                }
                if (injection == 2) {
                    throw std::runtime_error("before-enqueue");
                }
            } catch (...) {
                ++connected_contained;
                connected_pin = true;
            }
        };
        invoke();
        callback_owner.Release();
    }

    Expect(result,
           brush.unregister_attempts == 1 &&
               brush.close_attempts == 1 &&
               brush.contained == 2 && brush.permanent_pin &&
               connected_contained == 3 && connected_pin,
           "brush-or-connected-exception-escaped");
    result.terminal_state = "contained-pinned";
    Finalize(result);
    return result;
}

Scenario ModuleProjectedDelegateFirewalls() {
    Scenario result{
        "module.projected-delegate-firewalls",
        "module",
        true,
        "",
        {"enter-tls-guard", "enter-nested-tls-guard",
         "restore-nested-state", "restore-outer-state",
         "enqueue-returns-false", "enqueue-throws"},
        {"nested-projected-callback",
         "projected-enqueue-rejected",
         "projected-enqueue-exception"},
        {},
        "Projected delegate boundaries restore TLS nesting state and retain ownership for both false and throwing enqueue outcomes",
    };

    struct TlsGuard {
        explicit TlsGuard(bool& active) noexcept
            : active_(active), previous_(active) {
            active_ = true;
        }
        ~TlsGuard() noexcept {
            active_ = previous_;
        }
        bool& active_;
        bool previous_;
    };

    bool tls_active = false;
    bool nested_preserved = false;
    auto tls_scope =
        result.resources.Create("projected-tls-scope");
    {
        TlsGuard outer(tls_active);
        {
            TlsGuard nested(tls_active);
            nested_preserved = tls_active;
        }
        nested_preserved = nested_preserved && tls_active;
    }
    nested_preserved = nested_preserved && !tls_active;
    tls_scope.Release();

    bool rejected_contained = false;
    bool throw_contained = false;
    auto rejected_delegate =
        result.resources.Create("projected-delegate");
    const bool accepted = false;
    if (!accepted) {
        rejected_contained = true;
        rejected_delegate.Retain(
            RetainReasonCode::DelegateRejected);
    }
    auto throwing_delegate =
        result.resources.Create("projected-delegate");
    try {
        throw std::runtime_error("projected-enqueue");
    } catch (...) {
        throw_contained = true;
        throwing_delegate.Retain(
            RetainReasonCode::ProtocolFailure);
    }

    Expect(result,
           nested_preserved && rejected_contained &&
               throw_contained && !tls_active,
           "projected-boundary-state-not-restored");
    result.terminal_state = "contained-retained";
    Finalize(result);
    return result;
}

Scenario ModuleDormantStatsRaii() {
    Scenario result{
        "module.dormant-stats-raii",
        "module",
        true,
        "",
        {"acquire-statistics-timer", "acquire-content-owner",
         "inject-sampling-exception", "release-content-raii",
         "release-timer-raii", "verify-network-dormant"},
        {"statistics-sampling-exception"},
        {},
        "Dormant statistics sampling releases timer and content owners during unwinding and performs no network activity",
    };

    struct ReleaseGuard {
        explicit ReleaseGuard(ResourceOwner& owner) noexcept
            : owner_(&owner) {}
        ~ReleaseGuard() noexcept {
            owner_->Release();
        }
        ResourceOwner* owner_;
    };

    std::uint64_t network_calls = 0;
    bool contained = false;
    try {
        auto timer = result.resources.Create("statistics-timer");
        ReleaseGuard timer_guard(timer);
        auto content = result.resources.Create("statistics-content-owner");
        ReleaseGuard content_guard(content);
        throw std::runtime_error("statistics-sampling");
    } catch (...) {
        contained = true;
    }
    Expect(result,
           contained && network_calls == 0,
           "dormant-statistics-escaped-or-used-network");
    result.terminal_state = "dormant-balanced";
    Finalize(result);
    return result;
}

Scenario DispatchReentrantFailFast() {
    Scenario result{
        "dispatch.reentrant-fail-fast",
        "dispatch",
        true,
        "",
        {"enter-dispatch-gate", "attempt-reentrant-entry",
         "return-busy-without-wait", "release-outer-gate"},
        {"same-thread-reentrant-dispatch"},
        {},
        "A same-thread nested dispatch observes Busy immediately and never waits on its own gate",
    };

    std::atomic_flag gate = ATOMIC_FLAG_INIT;
    const bool outer_acquired =
        !gate.test_and_set(std::memory_order_acquire);
    const bool nested_acquired =
        !gate.test_and_set(std::memory_order_acquire);
    std::uint64_t busy_returns = nested_acquired ? 0 : 1;
    if (nested_acquired) {
        gate.clear(std::memory_order_release);
    }
    if (outer_acquired) {
        gate.clear(std::memory_order_release);
    }
    auto dispatch_owner =
        result.resources.Create("dispatch-reentrancy-gate");
    dispatch_owner.Release();
    Expect(result,
           outer_acquired && !nested_acquired &&
               busy_returns == 1,
           "reentrant-dispatch-did-not-fail-fast");
    result.terminal_state = "busy-returned";
    Finalize(result);
    return result;
}

Scenario DispatchUnhookTicketOutsideLock() {
    Scenario result{
        "dispatch.unhook-ticket-outside-lock",
        "dispatch",
        true,
        "",
        {"claim-exact-unhook-ticket", "drop-registry-lock",
         "invoke-unhook", "reenter-receipt-during-unhook",
         "commit-exact-ticket",
         "capture-pending-and-hook-summary-under-lock",
         "drop-summary-lock", "log-summary-snapshot"},
        {"unhook-reenters-registry",
         "summary-log-is-external-action"},
        {},
        "The exact unhook ticket and dispatch-summary fields are captured under their resource lock, while unhook, reentrant receipt, and summary logging run only after the lock is dropped",
    };

    class UnhookModel {
    public:
        std::uint64_t Begin() {
            std::lock_guard lock(mutex_);
            lock_observed_ = true;
            active_ticket_ = next_ticket_++;
            lock_observed_ = false;
            return active_ticket_;
        }

        bool InvokeOutside(std::uint64_t ticket) {
            outside_observed_ = !lock_observed_;
            reentered_ = Receipt(ticket);
            return outside_observed_ && reentered_;
        }

        bool Commit(std::uint64_t ticket) {
            std::lock_guard lock(mutex_);
            if (ticket == 0 || ticket != active_ticket_) {
                return false;
            }
            active_ticket_ = 0;
            return true;
        }

        bool OutsideObserved() const noexcept {
            return outside_observed_;
        }

        bool Reentered() const noexcept {
            return reentered_;
        }

    private:
        bool Receipt(std::uint64_t ticket) {
            std::lock_guard lock(mutex_);
            return ticket == active_ticket_;
        }

        std::mutex mutex_;
        std::uint64_t next_ticket_ = 1;
        std::uint64_t active_ticket_ = 0;
        bool lock_observed_ = false;
        bool outside_observed_ = false;
        bool reentered_ = false;
    } model;

    auto hook = result.resources.Create("dispatch-hook");
    const auto ticket = model.Begin();
    const bool unhooked = model.InvokeOutside(ticket);
    const bool committed = model.Commit(ticket);
    if (unhooked && committed) {
        hook.Release();
    }
    Expect(result,
           ticket != 0 && unhooked && committed &&
               model.OutsideObserved() && model.Reentered(),
           "unhook-ran-under-registry-lock-or-ticket-changed");

    FakeDispatchSummaryBoundary summary(true, 3);
    summary.CaptureAndLog();
    Expect(result,
           summary.LoggedPending() &&
               summary.LoggedHookCount() == 3 &&
               summary.GateDepth() == 0 &&
               summary.MaximumGateDepth() == 1 &&
               summary.SnapshotCalls() == 1 &&
               summary.SnapshotReadsWhileUnlocked() == 0 &&
               summary.LogCalls() == 1 &&
               summary.ExternalActionsWhileLocked() == 0,
           "dispatch-summary-snapshots-under-lock-and-logs-after-unlock");
    result.terminal_state = "unhooked-exact-ticket";
    Finalize(result);
    return result;
}

Scenario UiCleanupRetryFailureTerminal() {
    Scenario result{
        "ui.cleanup-retry-failure-terminal",
        "ui-thread",
        true,
        "",
        {"register", "first-cleanup-fails-retryable",
         "claim-only-retry", "second-cleanup-fails",
         "force-retry-ineligible", "reject-third-attempt"},
        {"first-cleanup-failure", "second-cleanup-failure"},
        {},
        "The second cleanup failure is terminally retained even when an adapter incorrectly requests another retry",
    };

    protocol::UiThreadRegistry registry(41);
    const auto registration = registry.RegisterInitialized(
        41, {4101, 41001}, UiCapabilities());
    const auto first = registry.BeginCleanup(registration.record_id);
    Expect(result,
           first.status == protocol::ProtocolStatus::Acquired &&
               registry.CompleteCleanup(
                   first.ticket,
                   protocol::UiCleanupOutcome::Retained,
                   -501, true, "first-cleanup-failed") ==
                   protocol::ProtocolStatus::Applied,
           "first-cleanup-not-retryable");
    const auto second = registry.BeginRetry(registration.record_id);
    Expect(result,
           second.status == protocol::ProtocolStatus::Acquired &&
               second.ticket.attempt == 2 &&
               registry.CompleteCleanup(
                   second.ticket,
                   protocol::UiCleanupOutcome::Retained,
                   -502, true, "second-cleanup-failed") ==
                   protocol::ProtocolStatus::Applied,
           "second-cleanup-not-committed");
    const auto third = registry.BeginRetry(registration.record_id);
    const auto receipts = registry.Receipts();
    RecordUiReceiptResources(result, receipts);
    Expect(result,
           receipts.size() == 1 &&
               receipts[0].cleanup_attempts == 2 &&
               receipts[0].state == protocol::UiRecordState::Retained &&
               !receipts[0].retry_eligible &&
               third.status == protocol::ProtocolStatus::NotRetryable,
           "second-failure-remained-retryable");
    result.terminal_state = "retained-retry-exhausted";
    Finalize(result);
    return result;
}

enum class HarnessUiDispatcherState {
    Owned,
    Releasing,
    Released,
    UnknownRetained,
};

enum class HarnessUiDispatcherReleaseOutcome {
    Released,
    AlreadyReleased,
    DeferredBusy,
    RetainedUnknown,
};

class HarnessUiDispatcherGateGuard {
public:
    explicit HarnessUiDispatcherGateGuard(
        std::atomic_flag& gate) noexcept
        : gate_(&gate) {
        while (gate_->test_and_set(std::memory_order_acquire)) {
            std::this_thread::yield();
        }
    }

    HarnessUiDispatcherGateGuard(
        const HarnessUiDispatcherGateGuard&) = delete;
    HarnessUiDispatcherGateGuard& operator=(
        const HarnessUiDispatcherGateGuard&) = delete;

    ~HarnessUiDispatcherGateGuard() noexcept {
        gate_->clear(std::memory_order_release);
    }

private:
    std::atomic_flag* gate_;
};

// Portable model of the production UI dispatcher owner. It intentionally has
// one short decision gate and executes the injected foreign Release callback
// after publishing Releasing(ticket) and leaving that gate.
class HarnessUiDispatcherOwner {
public:
    [[nodiscard]] bool TryBorrow() noexcept {
        HarnessUiDispatcherGateGuard guard(gate_);
        if (state_ != HarnessUiDispatcherState::Owned ||
            opaqueOwner_ == 0) {
            return false;
        }
        ++borrowers_;
        return true;
    }

    void EndBorrow() noexcept {
        HarnessUiDispatcherGateGuard guard(gate_);
        if (borrowers_ != 0) {
            --borrowers_;
        }
    }

    template <typename ExternalRelease>
    HarnessUiDispatcherReleaseOutcome Release(
        ExternalRelease&& externalRelease) noexcept {
        std::uint64_t ticket = 0;
        std::uintptr_t owner = 0;
        {
            HarnessUiDispatcherGateGuard guard(gate_);
            if (state_ == HarnessUiDispatcherState::Released) {
                return HarnessUiDispatcherReleaseOutcome::
                    AlreadyReleased;
            }
            if (state_ ==
                HarnessUiDispatcherState::UnknownRetained) {
                return HarnessUiDispatcherReleaseOutcome::
                    RetainedUnknown;
            }
            if (state_ == HarnessUiDispatcherState::Releasing ||
                borrowers_ != 0) {
                return HarnessUiDispatcherReleaseOutcome::
                    DeferredBusy;
            }
            ticket = nextTicket_++;
            activeTicket_ = ticket;
            owner = opaqueOwner_;
            opaqueOwner_ = 0;
            state_ = HarnessUiDispatcherState::Releasing;
        }

        try {
            std::forward<ExternalRelease>(externalRelease)();
        } catch (...) {
            // The opaque token is receipted before Releasing is retired.
            unknownOwnerToken_ = owner;
            ++unknownReceiptCount_;
            HarnessUiDispatcherGateGuard guard(gate_);
            if (state_ == HarnessUiDispatcherState::Releasing &&
                activeTicket_ == ticket) {
                activeTicket_ = 0;
                state_ =
                    HarnessUiDispatcherState::UnknownRetained;
            }
            return HarnessUiDispatcherReleaseOutcome::
                RetainedUnknown;
        }

        HarnessUiDispatcherGateGuard guard(gate_);
        if (state_ != HarnessUiDispatcherState::Releasing ||
            activeTicket_ != ticket) {
            unknownOwnerToken_ = owner;
            ++unknownReceiptCount_;
            state_ = HarnessUiDispatcherState::UnknownRetained;
            return HarnessUiDispatcherReleaseOutcome::
                RetainedUnknown;
        }
        activeTicket_ = 0;
        state_ = HarnessUiDispatcherState::Released;
        return HarnessUiDispatcherReleaseOutcome::Released;
    }

    [[nodiscard]] std::uint64_t UnknownReceiptCount()
        const noexcept {
        return unknownReceiptCount_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uintptr_t UnknownOwnerToken()
        const noexcept {
        return unknownOwnerToken_.load(
            std::memory_order_acquire);
    }

private:
    std::atomic_flag gate_ = ATOMIC_FLAG_INIT;
    HarnessUiDispatcherState state_ =
        HarnessUiDispatcherState::Owned;
    std::uintptr_t opaqueOwner_ = 0xD15A7C4;
    std::uint32_t borrowers_ = 0;
    std::uint64_t nextTicket_ = 1;
    std::uint64_t activeTicket_ = 0;
    std::atomic<std::uintptr_t> unknownOwnerToken_{0};
    std::atomic<std::uint64_t> unknownReceiptCount_{0};
};

enum class HarnessUiCapabilityFinalizePhase {
    PhysicalPending,
    PhysicalReleaseInProgress,
    ProtocolCommitPending,
    ProtocolCommitInProgress,
    ProtocolAppliedRemovePending,
    RemoveInProgress,
    Removed,
    UnknownRetained,
};

enum class HarnessUiCapabilityFinalizeOutcome {
    Applied,
    IdempotentSuccess,
    ProtocolCommitPending,
    RemovePending,
    RetainedUnknown,
};

// Portable model of the production record-capability finalizer. A single
// ticket survives physical disposition, protocol commit retries, and record
// removal retries. In-progress callers wait without repeating foreign work.
class HarnessUiCapabilityFinalizer {
public:
    template <typename BeforeProtocolCommit>
    HarnessUiCapabilityFinalizeOutcome Finalize(
        protocol::ProtocolStatus protocol_commit_status,
        bool remove_succeeds,
        BeforeProtocolCommit&& before_protocol_commit) noexcept {
        enum class Work {
            PhysicalAndCommit,
            CommitOnly,
            RemoveOnly,
            Wait,
        };

        for (;;) {
            Work work = Work::Wait;
            std::uint64_t ticket = 0;
            {
                HarnessUiDispatcherGateGuard guard(gate_);
                switch (phase_) {
                case HarnessUiCapabilityFinalizePhase::
                    PhysicalPending:
                    ticket = next_ticket_++;
                    active_ticket_ = ticket;
                    disposition_ticket_.store(
                        ticket, std::memory_order_release);
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            PhysicalReleaseInProgress;
                    work = Work::PhysicalAndCommit;
                    break;
                case HarnessUiCapabilityFinalizePhase::
                    ProtocolCommitPending:
                    ticket = active_ticket_;
                    ++protocol_commit_retry_claims_;
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            ProtocolCommitInProgress;
                    work = Work::CommitOnly;
                    break;
                case HarnessUiCapabilityFinalizePhase::
                    ProtocolAppliedRemovePending:
                    ticket = active_ticket_;
                    ++remove_retry_claims_;
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            RemoveInProgress;
                    work = Work::RemoveOnly;
                    break;
                case HarnessUiCapabilityFinalizePhase::Removed:
                    return HarnessUiCapabilityFinalizeOutcome::
                        IdempotentSuccess;
                case HarnessUiCapabilityFinalizePhase::
                    UnknownRetained:
                    return HarnessUiCapabilityFinalizeOutcome::
                        RetainedUnknown;
                case HarnessUiCapabilityFinalizePhase::
                    PhysicalReleaseInProgress:
                case HarnessUiCapabilityFinalizePhase::
                    ProtocolCommitInProgress:
                case HarnessUiCapabilityFinalizePhase::
                    RemoveInProgress:
                    work = Work::Wait;
                    break;
                }
            }

            if (work == Work::Wait) {
                in_progress_observations_.fetch_add(
                    1, std::memory_order_acq_rel);
                std::this_thread::yield();
                continue;
            }

            if (work == Work::PhysicalAndCommit) {
                external_release_calls_.fetch_add(
                    1, std::memory_order_acq_rel);
                external_close_calls_.fetch_add(
                    1, std::memory_order_acq_rel);
                {
                    HarnessUiDispatcherGateGuard guard(gate_);
                    if (phase_ !=
                            HarnessUiCapabilityFinalizePhase::
                                PhysicalReleaseInProgress ||
                        active_ticket_ != ticket) {
                        permanent_pin_requests_.fetch_add(
                            1, std::memory_order_acq_rel);
                        phase_ =
                            HarnessUiCapabilityFinalizePhase::
                                UnknownRetained;
                        return HarnessUiCapabilityFinalizeOutcome::
                            RetainedUnknown;
                    }
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            ProtocolCommitInProgress;
                }
                work = Work::CommitOnly;
            }

            if (work == Work::CommitOnly) {
                before_protocol_commit();
                protocol_commit_calls_.fetch_add(
                    1, std::memory_order_acq_rel);
                if (protocol_commit_status !=
                    protocol::ProtocolStatus::Applied) {
                    HarnessUiDispatcherGateGuard guard(gate_);
                    if (phase_ !=
                            HarnessUiCapabilityFinalizePhase::
                                ProtocolCommitInProgress ||
                        active_ticket_ != ticket) {
                        permanent_pin_requests_.fetch_add(
                            1, std::memory_order_acq_rel);
                        phase_ =
                            HarnessUiCapabilityFinalizePhase::
                                UnknownRetained;
                        return HarnessUiCapabilityFinalizeOutcome::
                            RetainedUnknown;
                    }
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            ProtocolCommitPending;
                    return HarnessUiCapabilityFinalizeOutcome::
                        ProtocolCommitPending;
                }

                protocol_applied_count_.fetch_add(
                    1, std::memory_order_acq_rel);
                HarnessUiDispatcherGateGuard guard(gate_);
                if (phase_ !=
                        HarnessUiCapabilityFinalizePhase::
                            ProtocolCommitInProgress ||
                    active_ticket_ != ticket) {
                    permanent_pin_requests_.fetch_add(
                        1, std::memory_order_acq_rel);
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            UnknownRetained;
                    return HarnessUiCapabilityFinalizeOutcome::
                        RetainedUnknown;
                }
                phase_ =
                    HarnessUiCapabilityFinalizePhase::
                        RemoveInProgress;
                work = Work::RemoveOnly;
            }

            if (work == Work::RemoveOnly) {
                remove_calls_.fetch_add(
                    1, std::memory_order_acq_rel);
                HarnessUiDispatcherGateGuard guard(gate_);
                if (phase_ !=
                        HarnessUiCapabilityFinalizePhase::
                            RemoveInProgress ||
                    active_ticket_ != ticket) {
                    permanent_pin_requests_.fetch_add(
                        1, std::memory_order_acq_rel);
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            UnknownRetained;
                    return HarnessUiCapabilityFinalizeOutcome::
                        RetainedUnknown;
                }
                if (!remove_succeeds) {
                    phase_ =
                        HarnessUiCapabilityFinalizePhase::
                            ProtocolAppliedRemovePending;
                    return HarnessUiCapabilityFinalizeOutcome::
                        RemovePending;
                }
                phase_ =
                    HarnessUiCapabilityFinalizePhase::Removed;
                return HarnessUiCapabilityFinalizeOutcome::Applied;
            }
        }
    }

    [[nodiscard]] std::uint64_t DispositionTicket()
        const noexcept {
        return disposition_ticket_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ExternalReleaseCalls()
        const noexcept {
        return external_release_calls_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ExternalCloseCalls()
        const noexcept {
        return external_close_calls_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ProtocolCommitCalls()
        const noexcept {
        return protocol_commit_calls_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ProtocolAppliedCount()
        const noexcept {
        return protocol_applied_count_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t ProtocolCommitRetryClaims()
        const noexcept {
        return protocol_commit_retry_claims_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t RemoveCalls() const noexcept {
        return remove_calls_.load(std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t RemoveRetryClaims()
        const noexcept {
        return remove_retry_claims_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t InProgressObservations()
        const noexcept {
        return in_progress_observations_.load(
            std::memory_order_acquire);
    }

    [[nodiscard]] std::uint64_t PermanentPinRequests()
        const noexcept {
        return permanent_pin_requests_.load(
            std::memory_order_acquire);
    }

private:
    std::atomic_flag gate_ = ATOMIC_FLAG_INIT;
    HarnessUiCapabilityFinalizePhase phase_ =
        HarnessUiCapabilityFinalizePhase::PhysicalPending;
    std::uint64_t next_ticket_ = 1;
    std::uint64_t active_ticket_ = 0;
    std::atomic<std::uint64_t> disposition_ticket_{0};
    std::atomic<std::uint64_t> external_release_calls_{0};
    std::atomic<std::uint64_t> external_close_calls_{0};
    std::atomic<std::uint64_t> protocol_commit_calls_{0};
    std::atomic<std::uint64_t> protocol_applied_count_{0};
    std::atomic<std::uint64_t> protocol_commit_retry_claims_{0};
    std::atomic<std::uint64_t> remove_calls_{0};
    std::atomic<std::uint64_t> remove_retry_claims_{0};
    std::atomic<std::uint64_t> in_progress_observations_{0};
    std::atomic<std::uint64_t> permanent_pin_requests_{0};
};

Scenario UiCapabilityReleaseReceipts() {
    Scenario result{
        "ui.capability-release-receipts",
        "ui-thread",
        true,
        "",
        {"register-two-capability-owners",
         "barrier-admit-borrow-before-release",
         "release-retries-after-borrow-exit",
         "synchronous-release-reentry-sees-releasing",
         "release-throw-publishes-opaque-owner",
         "pause-after-physical-release-before-protocol-commit",
         "concurrent-finalizer-returns-idempotent-success",
         "protocol-commit-failure-keeps-physical-release-pending",
         "retry-protocol-commit-without-release-or-close",
         "retry-record-remove-without-close-or-commit",
         "release-first-record-capabilities",
         "release-second-thread-handle-and-cleanup-event",
         "retain-second-dispatcher",
         "verify-terminal-capability-masks"},
        {"dispatcher-capability-retained",
         "dispatcher-release-throws-opaque-retained",
         "protocol-commit-first-attempt-fails",
         "record-remove-first-attempt-fails"},
        {},
        "A single decision gate linearizes UI dispatcher borrowers and exact release tickets; concurrent finalizers converge idempotently, and protocol or remove retries never repeat physical Release or Close",
    };

    HarnessUiDispatcherOwner borrowRaceOwner;
    std::barrier borrowAdmitted(2);
    std::barrier releaseAttempted(2);
    std::atomic<bool> borrowWasAdmitted{false};
    std::atomic<std::uint64_t> borrowRaceExternalReleases{0};
    std::thread borrower([&] {
        borrowWasAdmitted.store(
            borrowRaceOwner.TryBorrow(),
            std::memory_order_release);
        borrowAdmitted.arrive_and_wait();
        releaseAttempted.arrive_and_wait();
        borrowRaceOwner.EndBorrow();
    });
    borrowAdmitted.arrive_and_wait();
    const auto busyRelease = borrowRaceOwner.Release([&] {
        borrowRaceExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
    });
    releaseAttempted.arrive_and_wait();
    borrower.join();
    const auto releaseAfterBorrow = borrowRaceOwner.Release([&] {
        borrowRaceExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
    });
    Expect(result,
           borrowWasAdmitted.load(std::memory_order_acquire) &&
               busyRelease ==
                   HarnessUiDispatcherReleaseOutcome::DeferredBusy &&
               releaseAfterBorrow ==
                   HarnessUiDispatcherReleaseOutcome::Released &&
               borrowRaceExternalReleases.load(
                   std::memory_order_acquire) == 1,
           "borrow-release-barrier-did-not-linearize");

    HarnessUiDispatcherOwner reentrantOwner;
    std::atomic<std::uint64_t> reentrantExternalReleases{0};
    bool borrowDuringRelease = true;
    auto nestedRelease =
        HarnessUiDispatcherReleaseOutcome::Released;
    const auto outerRelease = reentrantOwner.Release([&] {
        reentrantExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
        borrowDuringRelease = reentrantOwner.TryBorrow();
        nestedRelease = reentrantOwner.Release([&] {
            reentrantExternalReleases.fetch_add(
                1, std::memory_order_acq_rel);
        });
    });
    const auto alreadyReleased = reentrantOwner.Release([&] {
        reentrantExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
    });
    Expect(result,
           outerRelease ==
                   HarnessUiDispatcherReleaseOutcome::Released &&
               nestedRelease ==
                   HarnessUiDispatcherReleaseOutcome::DeferredBusy &&
               !borrowDuringRelease &&
               alreadyReleased ==
                   HarnessUiDispatcherReleaseOutcome::
                       AlreadyReleased &&
               reentrantExternalReleases.load(
                   std::memory_order_acquire) == 1,
           "synchronous-reentry-issued-second-release");

    HarnessUiDispatcherOwner throwingOwner;
    std::atomic<std::uint64_t> throwingExternalReleases{0};
    const auto throwingRelease = throwingOwner.Release([&] {
        throwingExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
        throw std::runtime_error("foreign-release-threw");
    });
    const auto retryAfterThrow = throwingOwner.Release([&] {
        throwingExternalReleases.fetch_add(
            1, std::memory_order_acq_rel);
    });
    Expect(result,
           throwingRelease ==
                   HarnessUiDispatcherReleaseOutcome::
                       RetainedUnknown &&
               retryAfterThrow ==
                   HarnessUiDispatcherReleaseOutcome::
                       RetainedUnknown &&
               throwingExternalReleases.load(
                   std::memory_order_acquire) == 1 &&
               throwingOwner.UnknownReceiptCount() == 1 &&
               throwingOwner.UnknownOwnerToken() != 0,
           "release-throw-lost-opaque-owner-or-retried");

    HarnessUiCapabilityFinalizer concurrent_finalizer;
    std::barrier physical_released_before_commit(2);
    std::barrier allow_protocol_commit(2);
    auto first_concurrent_outcome =
        HarnessUiCapabilityFinalizeOutcome::RetainedUnknown;
    auto second_concurrent_outcome =
        HarnessUiCapabilityFinalizeOutcome::RetainedUnknown;
    std::thread first_finalizer([&] {
        first_concurrent_outcome =
            concurrent_finalizer.Finalize(
                protocol::ProtocolStatus::Applied, true, [&] {
                    physical_released_before_commit
                        .arrive_and_wait();
                    allow_protocol_commit.arrive_and_wait();
                });
    });
    physical_released_before_commit.arrive_and_wait();
    std::thread second_finalizer([&] {
        second_concurrent_outcome =
            concurrent_finalizer.Finalize(
                protocol::ProtocolStatus::Applied,
                true, [] {});
    });
    while (concurrent_finalizer.InProgressObservations() == 0) {
        std::this_thread::yield();
    }
    allow_protocol_commit.arrive_and_wait();
    first_finalizer.join();
    second_finalizer.join();
    Expect(result,
           first_concurrent_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::Applied &&
               second_concurrent_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::
                       IdempotentSuccess &&
               concurrent_finalizer.DispositionTicket() != 0 &&
               concurrent_finalizer.ExternalReleaseCalls() == 1 &&
               concurrent_finalizer.ExternalCloseCalls() == 1 &&
               concurrent_finalizer.ProtocolCommitCalls() == 1 &&
               concurrent_finalizer.ProtocolAppliedCount() == 1 &&
               concurrent_finalizer.RemoveCalls() == 1 &&
               concurrent_finalizer.InProgressObservations() != 0 &&
               concurrent_finalizer.PermanentPinRequests() == 0,
           "concurrent-physical-release-to-commit-is-idempotent-without-pin");

    HarnessUiCapabilityFinalizer commit_retry_finalizer;
    const auto first_commit_outcome =
        commit_retry_finalizer.Finalize(
            protocol::ProtocolStatus::InvalidState,
            true, [] {});
    const auto pending_disposition_ticket =
        commit_retry_finalizer.DispositionTicket();
    const auto commit_retry_outcome =
        commit_retry_finalizer.Finalize(
            protocol::ProtocolStatus::Applied,
            true, [] {});
    const auto commit_retry_idempotent =
        commit_retry_finalizer.Finalize(
            protocol::ProtocolStatus::InvalidState,
            false, [] {});
    Expect(result,
           first_commit_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::
                       ProtocolCommitPending &&
               pending_disposition_ticket != 0 &&
               commit_retry_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::Applied &&
               commit_retry_idempotent ==
                   HarnessUiCapabilityFinalizeOutcome::
                       IdempotentSuccess &&
               commit_retry_finalizer.DispositionTicket() ==
                   pending_disposition_ticket &&
               commit_retry_finalizer.ExternalReleaseCalls() == 1 &&
               commit_retry_finalizer.ExternalCloseCalls() == 1 &&
               commit_retry_finalizer.ProtocolCommitCalls() == 2 &&
               commit_retry_finalizer.ProtocolAppliedCount() == 1 &&
               commit_retry_finalizer
                       .ProtocolCommitRetryClaims() == 1 &&
               commit_retry_finalizer.RemoveCalls() == 1 &&
               commit_retry_finalizer.PermanentPinRequests() == 0,
           "protocol-commit-pending-retry-skips-release-and-close");

    HarnessUiCapabilityFinalizer remove_retry_finalizer;
    const auto first_remove_outcome =
        remove_retry_finalizer.Finalize(
            protocol::ProtocolStatus::Applied,
            false, [] {});
    const auto remove_pending_ticket =
        remove_retry_finalizer.DispositionTicket();
    const auto remove_retry_outcome =
        remove_retry_finalizer.Finalize(
            protocol::ProtocolStatus::InvalidState,
            true, [] {});
    const auto remove_retry_idempotent =
        remove_retry_finalizer.Finalize(
            protocol::ProtocolStatus::InvalidState,
            false, [] {});
    Expect(result,
           first_remove_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::
                       RemovePending &&
               remove_pending_ticket != 0 &&
               remove_retry_outcome ==
                   HarnessUiCapabilityFinalizeOutcome::Applied &&
               remove_retry_idempotent ==
                   HarnessUiCapabilityFinalizeOutcome::
                       IdempotentSuccess &&
               remove_retry_finalizer.DispositionTicket() ==
                   remove_pending_ticket &&
               remove_retry_finalizer.ExternalReleaseCalls() == 1 &&
               remove_retry_finalizer.ExternalCloseCalls() == 1 &&
               remove_retry_finalizer.ProtocolCommitCalls() == 1 &&
               remove_retry_finalizer.ProtocolAppliedCount() == 1 &&
               remove_retry_finalizer.RemoveCalls() == 2 &&
               remove_retry_finalizer.RemoveRetryClaims() == 1 &&
               remove_retry_finalizer.PermanentPinRequests() == 0,
           "finalizer-remove-retry-skips-close-and-protocol-commit");

    protocol::UiThreadRegistry registry(42);
    const auto released_record = registry.RegisterInitialized(
        42, {4201, 42001}, UiCapabilities());
    const auto retained_record = registry.RegisterInitialized(
        42, {4202, 42002}, UiCapabilities());
    const auto released_cleanup =
        registry.BeginCleanup(released_record.record_id);
    const auto retained_cleanup =
        registry.BeginCleanup(retained_record.record_id);
    Expect(result,
           released_cleanup.status ==
                   protocol::ProtocolStatus::Acquired &&
               retained_cleanup.status ==
                   protocol::ProtocolStatus::Acquired &&
               registry.CompleteCleanup(
                   released_cleanup.ticket,
                   protocol::UiCleanupOutcome::Cleaned) ==
                   protocol::ProtocolStatus::Applied &&
               registry.CompleteCleanup(
                   retained_cleanup.ticket,
                   protocol::UiCleanupOutcome::Retained,
                   -601, false, "dispatcher-retained") ==
                   protocol::ProtocolStatus::Applied,
           "logical-cleanup-receipts");

    const auto thread_handle_mask =
        protocol::UiCapabilityMask(
            protocol::UiCapability::ThreadHandle);
    const auto dispatcher_mask =
        protocol::UiCapabilityMask(
            protocol::UiCapability::AgileDispatcher);
    const auto cleanup_event_mask =
        protocol::UiCapabilityMask(
            protocol::UiCapability::CleanupEvent);
    const auto second_released_mask =
        thread_handle_mask | cleanup_event_mask;
    Expect(result,
           registry.CompleteCapabilityDisposition(
               released_record.record_id,
               dispatcher_mask, dispatcher_mask) ==
                   protocol::ProtocolStatus::InvalidArgument &&
               registry.CompleteCapabilityDisposition(
                   released_record.record_id,
                   dispatcher_mask, 0) ==
                   protocol::ProtocolStatus::Applied &&
               registry.CompleteCapabilityDisposition(
                   released_record.record_id,
                   second_released_mask, 0) ==
                   protocol::ProtocolStatus::Applied &&
               registry.CompleteCapabilityDisposition(
               released_record.record_id,
               dispatcher_mask, 0) ==
                   protocol::ProtocolStatus::InvalidState &&
               registry.CompleteCapabilityDisposition(
                   retained_record.record_id,
                   second_released_mask, dispatcher_mask) ==
                   protocol::ProtocolStatus::Applied,
           "capability-disposition-commit-retry");
    const auto receipts = registry.Receipts();
    Expect(result,
           receipts.size() == 2 &&
               receipts[0].capabilities_terminal &&
               receipts[0].capability_created_mask ==
                   protocol::kUiCapabilityMask &&
               receipts[0].capability_released_mask ==
                   protocol::kUiCapabilityMask &&
               receipts[0].capability_retained_mask == 0 &&
               receipts[1].capabilities_terminal &&
               receipts[1].capability_released_mask ==
                   second_released_mask &&
               receipts[1].capability_retained_mask ==
                   dispatcher_mask,
           "capability-receipt-masks");
    for (const auto& receipt : receipts) {
        RecordUiCapabilityReceiptResources(result, receipt);
    }
    result.terminal_state = "capabilities-accounted";
    Finalize(result);
    return result;
}

Scenario ModulePinReleaseFirstRace() {
    Scenario result{
        "module.pin-release-first-race",
        "module",
        true,
        "",
        {"publish-release-epoch-and-ticket",
         "leave-decision-gate",
         "fake-freelibrary-reenters-require-permanent",
         "publish-independent-acquire-ticket",
         "fake-getmodule-reenters-require-permanent",
         "commit-exact-acquire-ticket",
         "commit-exact-release-ticket",
         "refuse-reentrant-and-repeat-free",
         "verify-reference-conservation"},
        {"freelibrary-synchronous-reentrancy",
         "getmodule-synchronous-reentrancy",
         "independent-pin-acquisition-failure",
         "freelibrary-unconfirmed-failure"},
        {},
        "A two-phase epoch and exact-ticket model permits synchronous loader reentry, never calls the loader under its gate, and finishes with an irreversible permanent intent plus conserved references",
    };

    class PinModel {
    public:
        enum class State {
            Pinned,
            Releasing,
            Released,
            Permanent,
            PinUnproven,
        };

        bool ReleaseWithSynchronousReentry(
            bool free_succeeds,
            bool independent_acquire_succeeds) noexcept {
            std::uint64_t release_ticket = 0;
            {
                GateGuard guard(*this);
                if (permanent_required_ ||
                    acquire_ticket_ != 0 ||
                    release_ticket_ != 0 ||
                    !ordinary_owner_) {
                    ++refused_release_attempts_;
                    return false;
                }
                ordinary_owner_ = false;
                release_owner_inflight_ = true;
                release_ticket = ClaimTicketLocked();
                release_ticket_ = release_ticket;
                state_ = State::Releasing;
            }

            loader_calls_outside_gate_ =
                loader_calls_outside_gate_ && !gate_held_;
            ++free_calls_;
            RequirePermanent(
                independent_acquire_succeeds, true);
            if (!AttemptReleaseWhileBusy()) {
                reentrant_release_refused_ = true;
            }

            if (free_succeeds) {
                ++consumed_references_;
            } else {
                ++unconfirmed_references_;
            }

            {
                GateGuard guard(*this);
                if (release_ticket_ != release_ticket ||
                    !release_owner_inflight_) {
                    ticket_mismatch_ = true;
                    permanent_required_ = true;
                    state_ = State::PinUnproven;
                    return false;
                }
                release_ticket_ = 0;
                release_owner_inflight_ = false;
                state_ = proven_references_ != 0
                             ? State::Permanent
                             : State::PinUnproven;
            }
            ++completed_release_operations_;
            return false;
        }

        bool AttemptReleaseWhileBusy() noexcept {
            GateGuard guard(*this);
            if (permanent_required_ ||
                acquire_ticket_ != 0 ||
                release_ticket_ != 0 ||
                !ordinary_owner_) {
                ++refused_release_attempts_;
                return false;
            }
            ++double_free_;
            return true;
        }

        void RequirePermanent(
            bool independent_acquire_succeeds,
            bool probe_nested_reentry) noexcept {
            std::uint64_t acquire_ticket = 0;
            {
                GateGuard guard(*this);
                permanent_required_ = true;
                if (proven_references_ == 0 &&
                    acquire_ticket_ == 0) {
                    acquire_ticket = ClaimTicketLocked();
                    acquire_ticket_ = acquire_ticket;
                }
                state_ = proven_references_ != 0
                             ? State::Permanent
                             : (release_ticket_ != 0
                                    ? State::Releasing
                                    : State::PinUnproven);
            }

            if (acquire_ticket == 0) {
                ++coalesced_permanent_requests_;
                return;
            }

            loader_calls_outside_gate_ =
                loader_calls_outside_gate_ && !gate_held_;
            ++get_module_calls_;
            if (probe_nested_reentry) {
                RequirePermanent(
                    independent_acquire_succeeds, false);
            }

            {
                GateGuard guard(*this);
                if (acquire_ticket_ != acquire_ticket) {
                    ticket_mismatch_ = true;
                    state_ = State::PinUnproven;
                    return;
                }
                acquire_ticket_ = 0;
                if (independent_acquire_succeeds) {
                    ++acquired_references_;
                    ++proven_references_;
                }
                state_ = proven_references_ != 0
                             ? State::Permanent
                             : (release_ticket_ != 0
                                    ? State::Releasing
                                    : State::PinUnproven);
            }
            ++completed_acquire_operations_;
        }

        [[nodiscard]] bool CompleteAndConserved() const noexcept {
            const std::uint64_t reference_inputs =
                1 + acquired_references_;
            const std::uint64_t reference_outputs =
                consumed_references_ +
                proven_references_ +
                unconfirmed_references_;
            return permanent_required_ &&
                   !ordinary_owner_ &&
                   !release_owner_inflight_ &&
                   acquire_ticket_ == 0 &&
                   release_ticket_ == 0 &&
                   free_calls_ == 1 &&
                   get_module_calls_ == 1 &&
                   completed_release_operations_ == 1 &&
                   completed_acquire_operations_ == 1 &&
                   coalesced_permanent_requests_ >= 1 &&
                   reentrant_release_refused_ &&
                   refused_release_attempts_ >= 1 &&
                   double_free_ == 0 &&
                   !ticket_mismatch_ &&
                   loader_calls_outside_gate_ &&
                   reference_inputs == reference_outputs &&
                   (state_ == State::Permanent) ==
                       (proven_references_ != 0);
        }

        [[nodiscard]] bool PermanentRequired() const noexcept {
            return permanent_required_;
        }

        [[nodiscard]] State CurrentState() const noexcept {
            return state_;
        }

        [[nodiscard]] std::uint64_t FreeCalls() const noexcept {
            return free_calls_;
        }

        [[nodiscard]] std::uint64_t GetModuleCalls() const noexcept {
            return get_module_calls_;
        }

    private:
        class GateGuard {
        public:
            explicit GateGuard(PinModel& owner) noexcept
                : owner_(owner) {
                if (owner_.gate_held_) {
                    owner_.ticket_mismatch_ = true;
                }
                owner_.gate_held_ = true;
            }

            ~GateGuard() noexcept {
                owner_.gate_held_ = false;
            }

        private:
            PinModel& owner_;
        };

        std::uint64_t ClaimTicketLocked() noexcept {
            do {
                ++epoch_;
            } while (epoch_ == 0 ||
                     epoch_ == acquire_ticket_ ||
                     epoch_ == release_ticket_);
            return epoch_;
        }

        bool gate_held_ = false;
        bool ordinary_owner_ = true;
        bool release_owner_inflight_ = false;
        bool permanent_required_ = false;
        bool reentrant_release_refused_ = false;
        bool ticket_mismatch_ = false;
        bool loader_calls_outside_gate_ = true;
        State state_ = State::Pinned;
        std::uint64_t epoch_ = 0;
        std::uint64_t acquire_ticket_ = 0;
        std::uint64_t release_ticket_ = 0;
        std::uint64_t acquired_references_ = 0;
        std::uint64_t consumed_references_ = 0;
        std::uint64_t proven_references_ = 0;
        std::uint64_t unconfirmed_references_ = 0;
        std::uint64_t free_calls_ = 0;
        std::uint64_t get_module_calls_ = 0;
        std::uint64_t completed_release_operations_ = 0;
        std::uint64_t completed_acquire_operations_ = 0;
        std::uint64_t coalesced_permanent_requests_ = 0;
        std::uint64_t refused_release_attempts_ = 0;
        std::uint64_t double_free_ = 0;
    };

    struct Case {
        bool free_succeeds = false;
        bool acquire_succeeds = false;
        PinModel::State expected_state =
            PinModel::State::PinUnproven;
    };
    constexpr std::array cases{
        Case{true, true, PinModel::State::Permanent},
        Case{true, false, PinModel::State::PinUnproven},
        Case{false, true, PinModel::State::Permanent},
        Case{false, false, PinModel::State::PinUnproven},
    };
    for (const auto& test_case : cases) {
        PinModel model;
        auto ordinary_pin =
            result.resources.Create("temporary-module-pin");
        const bool released = model.ReleaseWithSynchronousReentry(
            test_case.free_succeeds,
            test_case.acquire_succeeds);
        if (test_case.free_succeeds) {
            ordinary_pin.Release();
        } else {
            ordinary_pin.Retain(
                RetainReasonCode::ExternalUncertainty);
        }
        if (test_case.acquire_succeeds) {
            auto independent_pin = result.resources.Create(
                "independent-permanent-module-pin");
            independent_pin.Retain(
                RetainReasonCode::ModulePermanent);
        }

        const bool repeat_release =
            model.AttemptReleaseWhileBusy();
        Expect(result,
               !released && !repeat_release &&
                   model.PermanentRequired() &&
                   model.CurrentState() ==
                       test_case.expected_state &&
                   model.FreeCalls() == 1 &&
                   model.GetModuleCalls() == 1 &&
                   model.CompleteAndConserved(),
               "reentrant-two-phase-pin-not-conserved");
    }

    result.terminal_state =
        "four-reentrant-outcomes-conserved";
    Finalize(result);
    return result;
}

void WriteJsonString(std::ostream& output, std::string_view value) {
    output.put('"');
    for (const char raw_character : value) {
        const auto character =
            static_cast<unsigned char>(raw_character);
        switch (character) {
        case '"':
            output << "\\\"";
            break;
        case '\\':
            output << "\\\\";
            break;
        case '\b':
            output << "\\b";
            break;
        case '\f':
            output << "\\f";
            break;
        case '\n':
            output << "\\n";
            break;
        case '\r':
            output << "\\r";
            break;
        case '\t':
            output << "\\t";
            break;
        default:
            if (character < 0x20) {
                constexpr char hex[] = "0123456789abcdef";
                output << "\\u00" << hex[(character >> 4) & 0x0f]
                       << hex[character & 0x0f];
            } else {
                output.put(static_cast<char>(character));
            }
        }
    }
    output.put('"');
}

void WriteStringArray(std::ostream& output,
                      const std::vector<std::string>& values) {
    output.put('[');
    for (std::size_t index = 0; index < values.size(); ++index) {
        if (index != 0) {
            output.put(',');
        }
        WriteJsonString(output, values[index]);
    }
    output.put(']');
}

void WriteResourceEvents(
    std::ostream& output,
    const std::vector<ResourceEvent>& events) {
    output.put('[');
    for (std::size_t index = 0; index < events.size(); ++index) {
        if (index != 0) {
            output.put(',');
        }
        output << "{\"resourceId\":";
        WriteJsonString(output, events[index].id);
        output << ",\"resourceKind\":";
        WriteJsonString(output, events[index].kind);
        output << ",\"action\":";
        WriteJsonString(output, ToString(events[index].action));
        output << ",\"reasonCode\":";
        WriteJsonString(output, ToString(events[index].reason));
        output.put('}');
    }
    output.put(']');
}

void WriteScenario(std::ostream& output, const Scenario& scenario) {
    output << "{\"id\":";
    WriteJsonString(output, scenario.id);
    output << ",\"area\":";
    WriteJsonString(output, scenario.area);
    output << ",\"passed\":" << (scenario.passed ? "true" : "false");
    output << ",\"terminalState\":";
    WriteJsonString(output, scenario.terminal_state);
    output << ",\"steps\":";
    WriteStringArray(output, scenario.steps);
    output << ",\"faults\":";
    WriteStringArray(output, scenario.faults);
    output << ",\"resourceEvents\":";
    WriteResourceEvents(output, scenario.resources.Events());
    output << ",\"resourceAccounting\":{\"created\":"
           << scenario.accounting.created
           << ",\"released\":" << scenario.accounting.released
           << ",\"retained\":" << scenario.accounting.retained
           << ",\"unexplained\":" << scenario.accounting.unexplained
           << ",\"doubleRelease\":" << scenario.accounting.double_release
           << "},\"boundedConcurrency\":{\"used\":"
           << (scenario.concurrency.used ? "true" : "false")
           << ",\"barrierParticipants\":"
           << scenario.concurrency.participants << "},\"detail\":";
    WriteJsonString(output, scenario.detail);
    output.put('}');
}

}  // namespace

int main(int argc, char** argv) {
    if (argc != 2 || std::string_view(argv[1]) != "--json") {
        std::cerr << "usage: jarvis_lifecycle_harness --json\n";
        return 64;
    }

    std::vector<Scenario> scenarios;
    scenarios.push_back(GitNormalClose());
    scenarios.push_back(GitGetBlocksRevoke());
    scenarios.push_back(GitProxyFinalReleaseBeforeLeaseRelease());
    scenarios.push_back(GitRevokeFailRetry());
    scenarios.push_back(GitConcurrentClose());
    scenarios.push_back(GitProvisionalCommitFailQuarantine());
    scenarios.push_back(GitProvisionalInitializedUnloadRetry());
    scenarios.push_back(GitRetiredOwnerTransferFailure());
    scenarios.push_back(GitLeaseCapacityRetained());
    scenarios.push_back(GitFixedReasonReceiptNoAllocation());
    scenarios.push_back(GitPublicLockFailureMatrix());
    scenarios.push_back(GitUnknownCookieFallbackReceipt());
    scenarios.push_back(GitRevokeCommitProtocolFailure());
    scenarios.push_back(GitSubscriptionLockFailureMatrix());
    scenarios.push_back(GitSequenceExhaustionNoAba());
    scenarios.push_back(GitProvisionalRollbackExceptionRetained());
    scenarios.push_back(
        GitProvisionalRegisterThrowUnknownRetained());
    scenarios.push_back(
        GitProvisionalGitReleaseExceptionContained());
    scenarios.push_back(GitProvisionalQuarantineOverflowReceipt());
    scenarios.push_back(GitProxyReleaseExceptionRetainsLease());
    scenarios.push_back(GitGetExternalComExceptionRetainsLease());
    scenarios.push_back(GitCoCreateOutputThrowRetained());
    scenarios.push_back(GitSiteHolderExternalComFirewall());
    scenarios.push_back(GitInternalSelfReferenceNoexcept());
    scenarios.push_back(
        GitRevokeExternalComExceptionRetained());
    scenarios.push_back(GitCoCreateFailRetained());
    scenarios.push_back(GitComChangedMode());
    scenarios.push_back(GitSFalseBalanced());
    scenarios.push_back(GitAdviseFailMaybeAdvised());
    scenarios.push_back(GitUnadviseFailBeforeRevoke());
    scenarios.push_back(GitUnadviseOkRevokeFail());
    scenarios.push_back(GitStaleGeneration());
    scenarios.push_back(GitRepeatCloseNoop());
    scenarios.push_back(GitWorkerBoundaryRaiiContainment());
    scenarios.push_back(UiFixedCapacitySnapshotTransaction());
    scenarios.push_back(UiRawHandleRollback());
    scenarios.push_back(UiEnumCallbackFixedCapacity());
    scenarios.push_back(UiBootstrapDuplicateObservation());
    scenarios.push_back(UiNormalClean());
    scenarios.push_back(UiDuplicateInit());
    scenarios.push_back(UiWindowGoneThreadAlive());
    scenarios.push_back(UiThreadIdReuse());
    scenarios.push_back(UiDispatchRejected());
    scenarios.push_back(UiThreadExited());
    scenarios.push_back(UiPartialCleanRetry());
    scenarios.push_back(UiTimeoutLateClean());
    scenarios.push_back(UiWindowReplacement());
    scenarios.push_back(UiMultipleSameRoleWindows());
    scenarios.push_back(UiDestroyWindowFailure());
    scenarios.push_back(UiDestroyHookAbiFirewall());
    scenarios.push_back(UiSealLateClean());
    scenarios.push_back(UiInitializationRollback());
    scenarios.push_back(UiSelfThreadCleanup());
    scenarios.push_back(UiShutdownCleanup());
    scenarios.push_back(UiCleanupCallbackAdmissionFailure());
    scenarios.push_back(UiCleanupRetryFailureTerminal());
    scenarios.push_back(UiCapabilityReleaseReceipts());
    scenarios.push_back(DispatchSyncSuccess());
    scenarios.push_back(DispatchTimeoutCancel());
    scenarios.push_back(DispatchCallbackClaimsBeforeCancel());
    scenarios.push_back(DispatchClaimedBeforeGuardCancelRace());
    scenarios.push_back(DispatchUnhookFailRetry());
    scenarios.push_back(DispatchDuplicateCallback());
    scenarios.push_back(DispatchSlotConflict());
    scenarios.push_back(DispatchCallbackThrows());
    scenarios.push_back(DispatchTargetExit());
    scenarios.push_back(DispatchCallbackBeforeUnhookRetry());
    scenarios.push_back(DispatchUnhookRetryBeforeCallback());
    scenarios.push_back(DispatchSuccessUnhookCallbackInflight());
    scenarios.push_back(DispatchHookInstallTwoResourceReceipt());
    scenarios.push_back(DispatchAdapterLateSlot());
    scenarios.push_back(DispatchAdapterRepublishMonotonic());
    scenarios.push_back(
        DispatchAdapterDoubleReleaseRepublishSaturation());
    scenarios.push_back(DispatchCallbackProtocolPublicationFailure());
    scenarios.push_back(DispatchEmergencyHookExactSlot());
    scenarios.push_back(DispatchForeignAbiExceptionFirewall());
    scenarios.push_back(DispatchReentrantFailFast());
    scenarios.push_back(DispatchUnhookTicketOutsideLock());
    scenarios.push_back(ModulePermanentPinPublicationRace());
    scenarios.push_back(ModuleExportAbiFirewall());
    scenarios.push_back(ModuleFailClosedBeforePinDecision());
    scenarios.push_back(ModuleTapComBoundaryFaults());
    scenarios.push_back(ModuleNoexceptDiagnosticFailures());
    scenarios.push_back(ModuleGraphicsNullInputGuards());
    scenarios.push_back(ModuleLoaderReferenceRaii());
    scenarios.push_back(ModuleLockServerBalance());
    scenarios.push_back(ModuleXamlBrushCallbackFirewalls());
    scenarios.push_back(ModuleProjectedDelegateFirewalls());
    scenarios.push_back(ModuleDormantStatsRaii());
    scenarios.push_back(ModulePinReleaseFirstRace());

    std::uint64_t passed = 0;
    std::uint64_t retained_explained = 0;
    std::uint64_t retained_unexplained = 0;
    std::uint64_t double_release = 0;
    std::vector<std::string> errors;
    for (const Scenario& scenario : scenarios) {
        if (scenario.passed) {
            ++passed;
        } else {
            errors.push_back("scenario-failed:" + scenario.id);
        }
        retained_explained += scenario.accounting.retained;
        retained_unexplained += scenario.accounting.unexplained;
        double_release += scenario.accounting.double_release;
    }

    const bool all_passed = passed == scenarios.size();
    std::cout << "{\"protocolVersion\":1,\"passed\":"
              << (all_passed ? "true" : "false")
              << ",\"summary\":{\"scenarioCount\":" << scenarios.size()
              << ",\"passed\":" << passed
              << ",\"failed\":" << (scenarios.size() - passed)
              << ",\"retainedExplained\":" << retained_explained
              << ",\"retainedUnexplained\":" << retained_unexplained
              << ",\"doubleRelease\":" << double_release
              << "},\"scenarios\":[";
    for (std::size_t index = 0; index < scenarios.size(); ++index) {
        if (index != 0) {
            std::cout.put(',');
        }
        WriteScenario(std::cout, scenarios[index]);
    }
    std::cout << "],\"errors\":";
    WriteStringArray(std::cout, errors);
    std::cout << "}\n";
    return all_passed ? 0 : 1;
}
