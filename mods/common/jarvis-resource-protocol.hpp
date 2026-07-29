// SPDX-License-Identifier: GPL-3.0-or-later
//
// JARVIS2 offline resource-lifecycle protocol.
//
// This header deliberately has no Windhawk, Win32, COM, or WinRT dependency.
// Platform adapters perform external calls without holding a protocol lock,
// then commit the result through the ticket/generation checked methods below.

#pragma once

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <mutex>
#include <optional>
#include <string_view>
#include <utility>

namespace jarvis::resource_protocol {

enum class GitState {
    Empty,
    Registered,
    Revoking,
    Retained,
    Revoked,
};

enum class GitRetainedReason {
    None,
    RevokeFailed,
    PlatformPreconditionFailed,
    RevokeTicketLostCookie,
    GitCoCreateFailedDuringRevoke,
    RevokeInterfaceFromGlobalFailed,
    ComApartmentInitializationFailed,
    AdviseWorkerComInitializationFailed,
    UnadviseWorkerComInitializationFailed,
    UnadviseFailedBeforeRevoke,
    SubscriptionTransitionStillInFlight,
    ServiceLeaseDrainTimeout,
    LeaseCapacityExceeded,
    ProtocolFailure,
};

enum class SubscriptionState {
    NotAttempted,
    Advising,
    Advised,
    MaybeAdvised,
    Unadvising,
    Unadvised,
};

enum class ProtocolStatus {
    Applied,
    Acquired,
    TerminalNoop,
    Busy,
    LeaseOutstanding,
    NotRetryable,
    Duplicate,
    StaleTicket,
    GenerationMismatch,
    CapacityExceeded,
    SequenceExhausted,
    InvalidState,
    InvalidArgument,
    ProtocolFailure,
};

struct SequenceReservation {
    ProtocolStatus status = ProtocolStatus::SequenceExhausted;
    std::uint64_t value = 0;
};

// The zero value is a permanent exhausted sentinel. The maximum value can be
// issued exactly once, after which the sequence never wraps back to one.
inline SequenceReservation ReserveNonZeroSequence(
    std::atomic<std::uint64_t>& next) noexcept {
    std::uint64_t observed = next.load(std::memory_order_acquire);
    while (observed != 0) {
        const std::uint64_t replacement =
            observed == std::numeric_limits<std::uint64_t>::max()
                ? 0
                : observed + 1;
        if (next.compare_exchange_weak(
                observed, replacement, std::memory_order_acq_rel,
                std::memory_order_acquire)) {
            return {ProtocolStatus::Acquired, observed};
        }
    }
    return {};
}

constexpr std::string_view ToString(GitState state) noexcept {
    switch (state) {
    case GitState::Empty:
        return "empty";
    case GitState::Registered:
        return "registered";
    case GitState::Revoking:
        return "revoking";
    case GitState::Retained:
        return "retained";
    case GitState::Revoked:
        return "revoked";
    }
    return "unknown";
}

constexpr std::string_view ToString(GitRetainedReason reason) noexcept {
    switch (reason) {
    case GitRetainedReason::None:
        return "none";
    case GitRetainedReason::RevokeFailed:
        return "revoke-failed";
    case GitRetainedReason::PlatformPreconditionFailed:
        return "platform-precondition-failed";
    case GitRetainedReason::RevokeTicketLostCookie:
        return "revoke-ticket-lost-cookie";
    case GitRetainedReason::GitCoCreateFailedDuringRevoke:
        return "git-cocreate-failed-during-revoke";
    case GitRetainedReason::RevokeInterfaceFromGlobalFailed:
        return "revoke-interface-from-global-failed";
    case GitRetainedReason::ComApartmentInitializationFailed:
        return "com-apartment-initialization-failed";
    case GitRetainedReason::AdviseWorkerComInitializationFailed:
        return "advise-worker-com-initialization-failed";
    case GitRetainedReason::UnadviseWorkerComInitializationFailed:
        return "unadvise-worker-com-initialization-failed";
    case GitRetainedReason::UnadviseFailedBeforeRevoke:
        return "unadvise-failed-before-git-revoke";
    case GitRetainedReason::SubscriptionTransitionStillInFlight:
        return "subscription-transition-still-in-flight";
    case GitRetainedReason::ServiceLeaseDrainTimeout:
        return "service-lease-drain-timeout";
    case GitRetainedReason::LeaseCapacityExceeded:
        return "lease-capacity-exceeded";
    case GitRetainedReason::ProtocolFailure:
        return "protocol-failure";
    }
    return "unknown";
}

constexpr std::string_view ToString(SubscriptionState state) noexcept {
    switch (state) {
    case SubscriptionState::NotAttempted:
        return "not-attempted";
    case SubscriptionState::Advising:
        return "advising";
    case SubscriptionState::Advised:
        return "advised";
    case SubscriptionState::MaybeAdvised:
        return "maybe-advised";
    case SubscriptionState::Unadvising:
        return "unadvising";
    case SubscriptionState::Unadvised:
        return "unadvised";
    }
    return "unknown";
}

constexpr std::string_view ToString(ProtocolStatus status) noexcept {
    switch (status) {
    case ProtocolStatus::Applied:
        return "applied";
    case ProtocolStatus::Acquired:
        return "acquired";
    case ProtocolStatus::TerminalNoop:
        return "terminal-noop";
    case ProtocolStatus::Busy:
        return "busy";
    case ProtocolStatus::LeaseOutstanding:
        return "lease-outstanding";
    case ProtocolStatus::NotRetryable:
        return "not-retryable";
    case ProtocolStatus::Duplicate:
        return "duplicate";
    case ProtocolStatus::StaleTicket:
        return "stale-ticket";
    case ProtocolStatus::GenerationMismatch:
        return "generation-mismatch";
    case ProtocolStatus::CapacityExceeded:
        return "capacity-exceeded";
    case ProtocolStatus::SequenceExhausted:
        return "sequence-exhausted";
    case ProtocolStatus::InvalidState:
        return "invalid-state";
    case ProtocolStatus::InvalidArgument:
        return "invalid-argument";
    case ProtocolStatus::ProtocolFailure:
        return "protocol-failure";
    }
    return "unknown";
}

struct GitLeaseTicket {
    std::uint64_t generation = 0;
    std::uint64_t lease_id = 0;

    [[nodiscard]] constexpr bool valid() const noexcept {
        return generation != 0 && lease_id != 0;
    }
};

struct GitRevokeTicket {
    std::uint64_t generation = 0;
    std::uint64_t attempt = 0;
    std::uint64_t ticket_id = 0;

    [[nodiscard]] constexpr bool valid() const noexcept {
        return generation != 0 && attempt != 0 && ticket_id != 0;
    }
};

struct GitLeaseResult {
    ProtocolStatus status = ProtocolStatus::InvalidState;
    GitLeaseTicket ticket{};
};

struct GitRevokeResult {
    ProtocolStatus status = ProtocolStatus::InvalidState;
    GitRevokeTicket ticket{};
};

enum class GitCookieKnowledge {
    Absent,
    Present,
    UnknownMayBePresent,
};

enum class GitOperation {
    None,
    Register,
    AcquireLease,
    CookieForLease,
    ReleaseLease,
    CloseAdmission,
    WaitForNoLeases,
    BeginRevoke,
    CookieForRevoke,
    CompleteRevoke,
    RetainRegisteredResource,
    Receipt,
};

constexpr std::string_view ToString(
    GitCookieKnowledge knowledge) noexcept {
    switch (knowledge) {
    case GitCookieKnowledge::Absent:
        return "absent";
    case GitCookieKnowledge::Present:
        return "present";
    case GitCookieKnowledge::UnknownMayBePresent:
        return "unknown-may-be-present";
    }
    return "unknown";
}

constexpr std::string_view ToString(GitOperation operation) noexcept {
    switch (operation) {
    case GitOperation::None:
        return "none";
    case GitOperation::Register:
        return "register";
    case GitOperation::AcquireLease:
        return "acquire-lease";
    case GitOperation::CookieForLease:
        return "cookie-for-lease";
    case GitOperation::ReleaseLease:
        return "release-lease";
    case GitOperation::CloseAdmission:
        return "close-admission";
    case GitOperation::WaitForNoLeases:
        return "wait-for-no-leases";
    case GitOperation::BeginRevoke:
        return "begin-revoke";
    case GitOperation::CookieForRevoke:
        return "cookie-for-revoke";
    case GitOperation::CompleteRevoke:
        return "complete-revoke";
    case GitOperation::RetainRegisteredResource:
        return "retain-registered-resource";
    case GitOperation::Receipt:
        return "receipt";
    }
    return "unknown";
}

struct GitCookieResult {
    ProtocolStatus status = ProtocolStatus::InvalidState;
    std::optional<std::uint32_t> cookie;
};

struct GitWaitResult {
    ProtocolStatus status = ProtocolStatus::InvalidState;
    bool no_leases = false;
};

// The hook pointer is null in production. It exists only so the offline
// harness can deterministically force each public lock boundary to fail before
// the lock is acquired.
struct GitLifecycleTestHooks {
    using FailBeforeLock =
        bool (*)(GitOperation operation, void* context) noexcept;

    FailBeforeLock fail_before_lock = nullptr;
    void* context = nullptr;
    std::uint64_t initial_lease_sequence = 1;
    std::uint64_t initial_revoke_attempt_sequence = 1;
    std::uint64_t initial_revoke_ticket_sequence = 1;
};

inline constexpr std::size_t kGitLeaseCapacity = 64;

struct GitReceipt {
    GitState state = GitState::Empty;
    std::uint64_t generation = 0;
    std::uint64_t active_leases = 0;
    std::uint64_t revoke_attempts = 0;
    std::uint64_t successful_revokes = 0;
    std::int64_t last_error = 0;
    // Kept for source compatibility. A failed snapshot deliberately sets this
    // to true while cookie_knowledge is UnknownMayBePresent so fallback code
    // can never mistake an unavailable snapshot for proof of absence.
    bool cookie_present = true;
    bool admission_open = false;
    bool retry_eligible = false;
    std::uint64_t lease_capacity = kGitLeaseCapacity;
    std::uint64_t lease_capacity_failures = 0;
    GitRetainedReason retained_reason = GitRetainedReason::None;
    ProtocolStatus snapshot_status = ProtocolStatus::ProtocolFailure;
    GitCookieKnowledge cookie_knowledge =
        GitCookieKnowledge::UnknownMayBePresent;
    std::uint64_t protocol_failure_count = 0;
    GitOperation last_failure_operation = GitOperation::None;
    std::uint64_t sequence_exhaustions = 0;
};

// Owns an opaque 32-bit GIT cookie but never exposes it through GitReceipt.
// A cookie can only be observed with a valid lease or the active revoke ticket.
class GitLifecycle {
public:
    explicit GitLifecycle(
        const GitLifecycleTestHooks* test_hooks = nullptr) noexcept
        : test_hooks_(test_hooks),
          next_lease_id_(
              test_hooks ? test_hooks->initial_lease_sequence : 1),
          next_revoke_attempt_(
              test_hooks
                  ? test_hooks->initial_revoke_attempt_sequence
                  : 1),
          next_revoke_ticket_id_(
              test_hooks
                  ? test_hooks->initial_revoke_ticket_sequence
                  : 1) {}

    GitLifecycle(const GitLifecycle&) = delete;
    GitLifecycle& operator=(const GitLifecycle&) = delete;

    ProtocolStatus Register(std::uint64_t generation,
                            std::uint32_t cookie) noexcept {
        constexpr GitOperation operation = GitOperation::Register;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (generation == 0 || cookie == 0) {
                return ProtocolStatus::InvalidArgument;
            }
            if (state_ != GitState::Empty) {
                return ProtocolStatus::InvalidState;
            }

            generation_ = generation;
            cookie_ = cookie;
            state_ = GitState::Registered;
            admission_open_ = true;
            last_error_ = 0;
            retry_eligible_ = false;
            retained_reason_ = GitRetainedReason::None;
            active_lease_ids_.fill(0);
            active_lease_count_ = 0;
            lease_capacity_failures_ = 0;
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    // Retained resources reject ordinary callback access. A close/recovery
    // adapter may explicitly request a control lease so it can finish a
    // best-effort Unadvise before retrying revoke.
    GitLeaseResult AcquireLease(
        bool allow_close_control = false) noexcept {
        constexpr GitOperation operation = GitOperation::AcquireLease;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, {}};
        }
        try {
            std::lock_guard lock(mutex_);
            const bool registered = state_ == GitState::Registered;
            const bool retained_control =
                allow_close_control && state_ == GitState::Retained &&
                retry_eligible_;
            const bool registered_access =
                registered &&
                (admission_open_ || allow_close_control);
            if ((!registered_access && !retained_control) ||
                cookie_ == 0) {
                return {};
            }

            auto free_slot = std::find(
                active_lease_ids_.begin(), active_lease_ids_.end(), 0);
            if (free_slot == active_lease_ids_.end()) {
                ++lease_capacity_failures_;
                return {ProtocolStatus::CapacityExceeded, {}};
            }
            if (lease_sequence_exhausted_ || next_lease_id_ == 0) {
                SaturatingIncrement(sequence_exhaustions_);
                return {ProtocolStatus::SequenceExhausted, {}};
            }

            const std::uint64_t lease_id = next_lease_id_;
            if (std::find(active_lease_ids_.begin(),
                          active_lease_ids_.end(),
                          lease_id) != active_lease_ids_.end()) {
                // A monotonically increasing sequence can only collide if the
                // object has been corrupted. Never probe or wrap into an ABA.
                SaturatingIncrement(sequence_exhaustions_);
                lease_sequence_exhausted_ = true;
                return {ProtocolStatus::SequenceExhausted, {}};
            }

            *free_slot = lease_id;
            ++active_lease_count_;
            if (lease_id ==
                std::numeric_limits<std::uint64_t>::max()) {
                lease_sequence_exhausted_ = true;
            } else {
                next_lease_id_ = lease_id + 1;
            }
            return {
                ProtocolStatus::Acquired,
                GitLeaseTicket{generation_, lease_id},
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, {}};
        }
    }

    [[nodiscard]] GitCookieResult CookieForLease(
        const GitLeaseTicket& ticket) const noexcept {
        constexpr GitOperation operation = GitOperation::CookieForLease;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, std::nullopt};
        }
        try {
            std::lock_guard lock(mutex_);
            if (ticket.generation != generation_) {
                return {
                    ProtocolStatus::GenerationMismatch, std::nullopt};
            }
            if (ticket.lease_id == 0 ||
                std::find(active_lease_ids_.begin(),
                          active_lease_ids_.end(),
                          ticket.lease_id) ==
                    active_lease_ids_.end()) {
                return {ProtocolStatus::StaleTicket, std::nullopt};
            }
            if (cookie_ == 0) {
                return {ProtocolStatus::InvalidState, std::nullopt};
            }
            return {
                ProtocolStatus::Acquired,
                std::optional<std::uint32_t>{cookie_},
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, std::nullopt};
        }
    }

    ProtocolStatus ReleaseLease(
        const GitLeaseTicket& ticket) noexcept {
        constexpr GitOperation operation = GitOperation::ReleaseLease;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::unique_lock lock(mutex_);
            if (ticket.generation != generation_) {
                return ProtocolStatus::GenerationMismatch;
            }
            auto slot = std::find(active_lease_ids_.begin(),
                                  active_lease_ids_.end(),
                                  ticket.lease_id);
            if (ticket.lease_id == 0 ||
                slot == active_lease_ids_.end()) {
                return ProtocolStatus::StaleTicket;
            }
            *slot = 0;
            --active_lease_count_;
            lock.unlock();
            lease_cv_.notify_all();
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    ProtocolStatus CloseAdmission() noexcept {
        constexpr GitOperation operation = GitOperation::CloseAdmission;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ == GitState::Empty ||
                state_ == GitState::Revoked) {
                admission_open_ = false;
                return ProtocolStatus::TerminalNoop;
            }
            if (!admission_open_) {
                return ProtocolStatus::TerminalNoop;
            }
            admission_open_ = false;
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    [[nodiscard]] GitWaitResult WaitForNoLeases(
        std::chrono::milliseconds timeout) noexcept {
        constexpr GitOperation operation = GitOperation::WaitForNoLeases;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, false};
        }
        try {
            std::unique_lock lock(mutex_);
            const bool no_leases = lease_cv_.wait_for(
                lock, timeout,
                [&] { return active_lease_count_ == 0; });
            return {
                no_leases ? ProtocolStatus::Applied
                          : ProtocolStatus::LeaseOutstanding,
                no_leases,
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, false};
        }
    }

    GitRevokeResult BeginRevoke() noexcept {
        constexpr GitOperation operation = GitOperation::BeginRevoke;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, {}};
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ == GitState::Empty ||
                state_ == GitState::Revoked) {
                return {ProtocolStatus::TerminalNoop, {}};
            }
            if (state_ == GitState::Revoking) {
                return {ProtocolStatus::Busy, {}};
            }
            if (state_ == GitState::Retained && !retry_eligible_) {
                return {ProtocolStatus::NotRetryable, {}};
            }
            if (state_ != GitState::Registered &&
                state_ != GitState::Retained) {
                return {ProtocolStatus::InvalidState, {}};
            }
            if (active_lease_count_ != 0) {
                return {ProtocolStatus::LeaseOutstanding, {}};
            }
            if (admission_open_) {
                return {ProtocolStatus::InvalidState, {}};
            }
            if (cookie_ == 0) {
                return {ProtocolStatus::InvalidState, {}};
            }
            if (revoke_attempt_sequence_exhausted_ ||
                revoke_ticket_sequence_exhausted_ ||
                next_revoke_attempt_ == 0 ||
                next_revoke_ticket_id_ == 0) {
                SaturatingIncrement(sequence_exhaustions_);
                return {ProtocolStatus::SequenceExhausted, {}};
            }

            const std::uint64_t attempt = next_revoke_attempt_;
            const std::uint64_t ticket_id =
                next_revoke_ticket_id_;
            AdvanceSequenceLocked(
                next_revoke_attempt_,
                revoke_attempt_sequence_exhausted_);
            AdvanceSequenceLocked(
                next_revoke_ticket_id_,
                revoke_ticket_sequence_exhausted_);
            revoke_attempts_ = attempt;
            active_revoke_ticket_ = ticket_id;
            state_ = GitState::Revoking;
            retry_eligible_ = false;
            return {
                ProtocolStatus::Acquired,
                GitRevokeTicket{
                    generation_, attempt, ticket_id,
                },
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, {}};
        }
    }

    [[nodiscard]] GitCookieResult CookieForRevoke(
        const GitRevokeTicket& ticket) const noexcept {
        constexpr GitOperation operation = GitOperation::CookieForRevoke;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, std::nullopt};
        }
        try {
            std::lock_guard lock(mutex_);
            if (ticket.generation != generation_) {
                return {
                    ProtocolStatus::GenerationMismatch, std::nullopt};
            }
            if (!IsActiveRevokeTicketLocked(ticket)) {
                return {ProtocolStatus::StaleTicket, std::nullopt};
            }
            if (cookie_ == 0) {
                return {ProtocolStatus::InvalidState, std::nullopt};
            }
            return {
                ProtocolStatus::Acquired,
                std::optional<std::uint32_t>{cookie_},
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return {ProtocolStatus::ProtocolFailure, std::nullopt};
        }
    }

    ProtocolStatus CompleteRevoke(const GitRevokeTicket& ticket,
                                  bool succeeded,
                                  std::int64_t error = 0,
                                   bool retry_eligible = false,
                                   GitRetainedReason retained_reason =
                                       GitRetainedReason::None) noexcept {
        constexpr GitOperation operation = GitOperation::CompleteRevoke;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (ticket.generation != generation_) {
                return ProtocolStatus::GenerationMismatch;
            }
            if (!IsActiveRevokeTicketLocked(ticket)) {
                return ProtocolStatus::StaleTicket;
            }

            active_revoke_ticket_ = 0;
            if (succeeded) {
                // The opaque cookie is cleared only after the platform adapter
                // has reported successful revocation for this exact ticket.
                cookie_ = 0;
                state_ = GitState::Revoked;
                admission_open_ = false;
                SaturatingIncrement(successful_revokes_);
                last_error_ = 0;
                retry_eligible_ = false;
                retained_reason_ = GitRetainedReason::None;
            } else {
                state_ = GitState::Retained;
                last_error_ = error;
                retry_eligible_ = retry_eligible;
                retained_reason_ =
                    retained_reason == GitRetainedReason::None
                        ? GitRetainedReason::RevokeFailed
                        : retained_reason;
            }
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    // Records a fail-safe retention discovered before a revoke call can
    // safely begin, for example COM apartment or CoCreateInstance failure.
    ProtocolStatus RetainRegisteredResource(
        std::int64_t error,
        bool retry_eligible,
        GitRetainedReason retained_reason =
            GitRetainedReason::None) noexcept {
        constexpr GitOperation operation =
            GitOperation::RetainRegisteredResource;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ != GitState::Registered &&
                state_ != GitState::Retained) {
                return ProtocolStatus::InvalidState;
            }
            state_ = GitState::Retained;
            admission_open_ = false;
            last_error_ = error;
            retry_eligible_ = retry_eligible;
            retained_reason_ =
                retained_reason == GitRetainedReason::None
                    ? GitRetainedReason::PlatformPreconditionFailed
                    : retained_reason;
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    [[nodiscard]] GitReceipt Receipt() const noexcept {
        constexpr GitOperation operation = GitOperation::Receipt;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return FailureReceipt();
        }
        try {
            std::lock_guard lock(mutex_);
            const bool cookie_present = cookie_ != 0;
            return {
                state_,
                generation_,
                active_lease_count_,
                revoke_attempts_,
                successful_revokes_,
                last_error_,
                cookie_present,
                admission_open_,
                retry_eligible_,
                kGitLeaseCapacity,
                lease_capacity_failures_,
                retained_reason_,
                ProtocolStatus::Applied,
                cookie_present ? GitCookieKnowledge::Present
                               : GitCookieKnowledge::Absent,
                protocol_failure_count_.load(
                    std::memory_order_acquire),
                last_failure_operation_.load(
                    std::memory_order_acquire),
                sequence_exhaustions_,
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return FailureReceipt();
        }
    }

private:
    [[nodiscard]] bool ShouldFailBeforeLock(
        GitOperation operation) const noexcept {
        return test_hooks_ && test_hooks_->fail_before_lock &&
               test_hooks_->fail_before_lock(
                   operation, test_hooks_->context);
    }

    ProtocolStatus RecordProtocolFailure(
        GitOperation operation) const noexcept {
        last_failure_operation_.store(
            operation, std::memory_order_release);
        std::uint64_t observed =
            protocol_failure_count_.load(std::memory_order_acquire);
        while (observed !=
               std::numeric_limits<std::uint64_t>::max()) {
            if (protocol_failure_count_.compare_exchange_weak(
                    observed, observed + 1,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire)) {
                break;
            }
        }
        return ProtocolStatus::ProtocolFailure;
    }

    [[nodiscard]] GitReceipt FailureReceipt() const noexcept {
        GitReceipt receipt;
        receipt.state = GitState::Retained;
        receipt.generation = 0;
        receipt.active_leases = kGitLeaseCapacity;
        receipt.last_error = -1;
        receipt.cookie_present = true;
        receipt.admission_open = false;
        receipt.retry_eligible = false;
        receipt.retained_reason = GitRetainedReason::ProtocolFailure;
        receipt.snapshot_status = ProtocolStatus::ProtocolFailure;
        receipt.cookie_knowledge =
            GitCookieKnowledge::UnknownMayBePresent;
        receipt.protocol_failure_count =
            protocol_failure_count_.load(std::memory_order_acquire);
        receipt.last_failure_operation =
            last_failure_operation_.load(std::memory_order_acquire);
        return receipt;
    }

    static void SaturatingIncrement(
        std::uint64_t& value) noexcept {
        if (value != std::numeric_limits<std::uint64_t>::max()) {
            ++value;
        }
    }

    static void AdvanceSequenceLocked(
        std::uint64_t& next,
        bool& exhausted) noexcept {
        if (next == std::numeric_limits<std::uint64_t>::max()) {
            exhausted = true;
        } else {
            ++next;
        }
    }

    [[nodiscard]] bool IsActiveRevokeTicketLocked(
        const GitRevokeTicket& ticket) const noexcept {
        return state_ == GitState::Revoking &&
               ticket.generation == generation_ &&
               ticket.attempt == revoke_attempts_ &&
               ticket.ticket_id != 0 &&
               ticket.ticket_id == active_revoke_ticket_;
    }

    mutable std::mutex mutex_;
    std::condition_variable lease_cv_;
    const GitLifecycleTestHooks* test_hooks_ = nullptr;
    mutable std::atomic<std::uint64_t> protocol_failure_count_{0};
    mutable std::atomic<GitOperation> last_failure_operation_{
        GitOperation::None};
    GitState state_ = GitState::Empty;
    std::uint64_t generation_ = 0;
    std::uint64_t next_lease_id_;
    std::uint64_t next_revoke_attempt_;
    std::uint64_t next_revoke_ticket_id_;
    bool lease_sequence_exhausted_ = false;
    bool revoke_attempt_sequence_exhausted_ = false;
    bool revoke_ticket_sequence_exhausted_ = false;
    std::uint64_t active_revoke_ticket_ = 0;
    std::uint64_t revoke_attempts_ = 0;
    std::uint64_t successful_revokes_ = 0;
    std::uint32_t cookie_ = 0;
    bool admission_open_ = false;
    std::array<std::uint64_t, kGitLeaseCapacity>
        active_lease_ids_{};
    std::uint64_t active_lease_count_ = 0;
    std::uint64_t lease_capacity_failures_ = 0;
    std::uint64_t sequence_exhaustions_ = 0;
    std::int64_t last_error_ = 0;
    bool retry_eligible_ = false;
    GitRetainedReason retained_reason_ =
        GitRetainedReason::None;
};

enum class SubscriptionOperation {
    None,
    BeginAdvise,
    CompleteAdvise,
    BeginUnadvise,
    CompleteUnadvise,
    Receipt,
};

struct SubscriptionLifecycleTestHooks {
    using FailBeforeLock =
        bool (*)(SubscriptionOperation operation,
                 void* context) noexcept;

    FailBeforeLock fail_before_lock = nullptr;
    void* context = nullptr;
};

struct SubscriptionReceipt {
    SubscriptionState state = SubscriptionState::NotAttempted;
    std::uint64_t advise_attempts = 0;
    std::uint64_t unadvise_attempts = 0;
    std::int64_t last_error = 0;
    bool best_effort_unadvise_required = false;
    // This latch records that external Advise/Unadvise work may have happened
    // even when the corresponding state commit could not acquire its lock.
    bool external_uncertainty_latched = false;
    ProtocolStatus snapshot_status = ProtocolStatus::ProtocolFailure;
    std::uint64_t protocol_failure_count = 0;
    SubscriptionOperation last_failure_operation =
        SubscriptionOperation::None;
};

class SubscriptionLifecycle {
public:
    explicit SubscriptionLifecycle(
        const SubscriptionLifecycleTestHooks* test_hooks =
            nullptr) noexcept
        : test_hooks_(test_hooks) {}

    ProtocolStatus BeginAdvise() noexcept {
        constexpr SubscriptionOperation operation =
            SubscriptionOperation::BeginAdvise;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ != SubscriptionState::NotAttempted) {
                return ProtocolStatus::InvalidState;
            }
            state_ = SubscriptionState::Advising;
            external_uncertainty_latched_.store(
                true, std::memory_order_release);
            SaturatingIncrement(advise_attempts_);
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    ProtocolStatus CompleteAdvise(bool succeeded,
                                  std::int64_t error = 0) noexcept {
        constexpr SubscriptionOperation operation =
            SubscriptionOperation::CompleteAdvise;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ != SubscriptionState::Advising) {
                return ProtocolStatus::InvalidState;
            }
            state_ = succeeded ? SubscriptionState::Advised
                               : SubscriptionState::MaybeAdvised;
            external_uncertainty_latched_.store(
                true, std::memory_order_release);
            last_error_ = succeeded ? 0 : error;
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    ProtocolStatus BeginUnadvise() noexcept {
        constexpr SubscriptionOperation operation =
            SubscriptionOperation::BeginUnadvise;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ == SubscriptionState::Unadvised) {
                return ProtocolStatus::TerminalNoop;
            }
            if (state_ != SubscriptionState::Advised &&
                state_ != SubscriptionState::MaybeAdvised) {
                return ProtocolStatus::InvalidState;
            }
            state_ = SubscriptionState::Unadvising;
            external_uncertainty_latched_.store(
                true, std::memory_order_release);
            SaturatingIncrement(unadvise_attempts_);
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    ProtocolStatus CompleteUnadvise(bool succeeded,
                                    std::int64_t error = 0) noexcept {
        constexpr SubscriptionOperation operation =
            SubscriptionOperation::CompleteUnadvise;
        if (ShouldFailBeforeLock(operation)) {
            return RecordProtocolFailure(operation);
        }
        try {
            std::lock_guard lock(mutex_);
            if (state_ != SubscriptionState::Unadvising) {
                return ProtocolStatus::InvalidState;
            }
            state_ = succeeded ? SubscriptionState::Unadvised
                               : SubscriptionState::MaybeAdvised;
            external_uncertainty_latched_.store(
                !succeeded, std::memory_order_release);
            last_error_ = succeeded ? 0 : error;
            return ProtocolStatus::Applied;
        } catch (...) {
            return RecordProtocolFailure(operation);
        }
    }

    [[nodiscard]] SubscriptionReceipt Receipt() const noexcept {
        constexpr SubscriptionOperation operation =
            SubscriptionOperation::Receipt;
        if (ShouldFailBeforeLock(operation)) {
            RecordProtocolFailure(operation);
            return FailureReceipt();
        }
        try {
            std::lock_guard lock(mutex_);
            const bool external_uncertainty =
                external_uncertainty_latched_.load(
                    std::memory_order_acquire);
            return {
                state_,
                advise_attempts_,
                unadvise_attempts_,
                last_error_,
                external_uncertainty ||
                    CouldRequireBestEffortUnadvise(state_),
                external_uncertainty,
                ProtocolStatus::Applied,
                protocol_failure_count_.load(
                    std::memory_order_acquire),
                last_failure_operation_.load(
                    std::memory_order_acquire),
            };
        } catch (...) {
            RecordProtocolFailure(operation);
            return FailureReceipt();
        }
    }

private:
    [[nodiscard]] bool ShouldFailBeforeLock(
        SubscriptionOperation operation) const noexcept {
        return test_hooks_ && test_hooks_->fail_before_lock &&
               test_hooks_->fail_before_lock(
                   operation, test_hooks_->context);
    }

    ProtocolStatus RecordProtocolFailure(
        SubscriptionOperation operation) const noexcept {
        if (operation == SubscriptionOperation::CompleteAdvise ||
            operation == SubscriptionOperation::BeginUnadvise ||
            operation == SubscriptionOperation::CompleteUnadvise) {
            // These operations run after Advise may have succeeded, or after
            // Unadvise was attempted. A failed state commit cannot prove that
            // the external subscription is absent.
            external_uncertainty_latched_.store(
                true, std::memory_order_release);
        }
        last_failure_operation_.store(
            operation, std::memory_order_release);
        std::uint64_t observed =
            protocol_failure_count_.load(std::memory_order_acquire);
        while (observed !=
               std::numeric_limits<std::uint64_t>::max()) {
            if (protocol_failure_count_.compare_exchange_weak(
                    observed, observed + 1,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire)) {
                break;
            }
        }
        return ProtocolStatus::ProtocolFailure;
    }

    [[nodiscard]] SubscriptionReceipt FailureReceipt() const noexcept {
        return {
            SubscriptionState::MaybeAdvised,
            0,
            0,
            -1,
            true,
            true,
            ProtocolStatus::ProtocolFailure,
            protocol_failure_count_.load(std::memory_order_acquire),
            last_failure_operation_.load(
                std::memory_order_acquire),
        };
    }

    static void SaturatingIncrement(
        std::uint64_t& value) noexcept {
        if (value != std::numeric_limits<std::uint64_t>::max()) {
            ++value;
        }
    }

    static bool CouldRequireBestEffortUnadvise(
        SubscriptionState state) noexcept {
        return state == SubscriptionState::Advising ||
               state == SubscriptionState::Advised ||
               state == SubscriptionState::MaybeAdvised ||
               state == SubscriptionState::Unadvising;
    }

    mutable std::mutex mutex_;
    const SubscriptionLifecycleTestHooks* test_hooks_ = nullptr;
    mutable std::atomic<std::uint64_t>
        protocol_failure_count_{0};
    mutable std::atomic<SubscriptionOperation>
        last_failure_operation_{SubscriptionOperation::None};
    mutable std::atomic<bool> external_uncertainty_latched_{false};
    SubscriptionState state_ = SubscriptionState::NotAttempted;
    std::uint64_t advise_attempts_ = 0;
    std::uint64_t unadvise_attempts_ = 0;
    std::int64_t last_error_ = 0;
};

enum class ApartmentInitKind {
    Succeeded,
    AlreadyInitialized,
    ChangedMode,
    Failed,
};

enum class ApartmentState {
    NotAttempted,
    Initialized,
    AlreadyInitialized,
    ChangedMode,
    Failed,
    Balanced,
};

constexpr std::string_view ToString(ApartmentState state) noexcept {
    switch (state) {
    case ApartmentState::NotAttempted:
        return "not-attempted";
    case ApartmentState::Initialized:
        return "initialized";
    case ApartmentState::AlreadyInitialized:
        return "already-initialized";
    case ApartmentState::ChangedMode:
        return "changed-mode";
    case ApartmentState::Failed:
        return "failed";
    case ApartmentState::Balanced:
        return "balanced";
    }
    return "unknown";
}

struct ApartmentReceipt {
    ApartmentState state = ApartmentState::NotAttempted;
    std::uint64_t initialize_calls = 0;
    std::uint64_t uninitialize_calls = 0;
    std::int64_t last_error = 0;
    bool requires_uninitialize = false;
};

// Pure bookkeeping for CoInitializeEx/CoUninitialize balance. S_FALSE is
// represented by AlreadyInitialized and still requires one matching close.
class ApartmentBalance {
public:
    ProtocolStatus RecordInitialize(ApartmentInitKind kind,
                                    std::int64_t error = 0) {
        std::lock_guard lock(mutex_);
        if (state_ != ApartmentState::NotAttempted) {
            return ProtocolStatus::Duplicate;
        }

        ++initialize_calls_;
        last_error_ = 0;
        switch (kind) {
        case ApartmentInitKind::Succeeded:
            state_ = ApartmentState::Initialized;
            requires_uninitialize_ = true;
            break;
        case ApartmentInitKind::AlreadyInitialized:
            state_ = ApartmentState::AlreadyInitialized;
            requires_uninitialize_ = true;
            break;
        case ApartmentInitKind::ChangedMode:
            state_ = ApartmentState::ChangedMode;
            last_error_ = error;
            break;
        case ApartmentInitKind::Failed:
            state_ = ApartmentState::Failed;
            last_error_ = error;
            break;
        }
        return ProtocolStatus::Applied;
    }

    ProtocolStatus Close() {
        std::lock_guard lock(mutex_);
        if (state_ == ApartmentState::Balanced ||
            state_ == ApartmentState::ChangedMode ||
            state_ == ApartmentState::Failed ||
            state_ == ApartmentState::NotAttempted) {
            return ProtocolStatus::TerminalNoop;
        }
        if (!requires_uninitialize_) {
            return ProtocolStatus::InvalidState;
        }

        requires_uninitialize_ = false;
        ++uninitialize_calls_;
        state_ = ApartmentState::Balanced;
        return ProtocolStatus::Applied;
    }

    [[nodiscard]] ApartmentReceipt Receipt() const {
        std::lock_guard lock(mutex_);
        return {
            state_,
            initialize_calls_,
            uninitialize_calls_,
            last_error_,
            requires_uninitialize_,
        };
    }

private:
    mutable std::mutex mutex_;
    ApartmentState state_ = ApartmentState::NotAttempted;
    std::uint64_t initialize_calls_ = 0;
    std::uint64_t uninitialize_calls_ = 0;
    std::int64_t last_error_ = 0;
    bool requires_uninitialize_ = false;
};

enum class UiRecordState {
    Reserved,
    Initializing,
    Initialized,
    Cleaning,
    Cleaned,
    InitFailed,
    Unreachable,
    Retained,
    LateCleanedRetained,
};

enum class UiCleanupOutcome {
    Cleaned,
    Unreachable,
    Retained,
};

enum class UiRegisterStatus {
    Registered,
    Duplicate,
    GenerationMismatch,
    InvalidCapabilities,
    CapacityExhausted,
    InvalidArgument,
};

constexpr std::size_t kUiThreadRegistryCapacity = 64;
constexpr std::size_t kUiReceiptReasonCapacity = 96;

// Cleanup callbacks publish reasons while crossing noexcept platform
// boundaries. Keep the text inline so publishing a terminal receipt can never
// allocate. The truncated bit makes an overlong adapter reason observable.
struct UiReceiptReason {
    std::array<char, kUiReceiptReasonCapacity> text{};
    std::uint8_t length = 0;
    bool truncated = false;

    constexpr UiReceiptReason() noexcept = default;

    constexpr UiReceiptReason(const char* value) noexcept {
        Assign(value);
    }

    constexpr void Assign(const char* value) noexcept {
        text.fill('\0');
        length = 0;
        truncated = false;
        if (value == nullptr) {
            return;
        }

        std::size_t index = 0;
        while (value[index] != '\0' && index + 1 < text.size()) {
            text[index] = value[index];
            ++index;
        }
        length = static_cast<std::uint8_t>(index);
        truncated = value[index] != '\0';
    }

    [[nodiscard]] constexpr bool empty() const noexcept {
        return length == 0;
    }

    [[nodiscard]] constexpr const char* data() const noexcept {
        return text.data();
    }

    [[nodiscard]] constexpr std::string_view view() const noexcept {
        return {text.data(), length};
    }

    [[nodiscard]] constexpr bool operator==(
        std::string_view other) const noexcept {
        return view() == other;
    }
};

constexpr std::string_view ToString(UiRecordState state) noexcept {
    switch (state) {
    case UiRecordState::Reserved:
        return "reserved";
    case UiRecordState::Initializing:
        return "initializing";
    case UiRecordState::Initialized:
        return "initialized";
    case UiRecordState::Cleaning:
        return "cleaning";
    case UiRecordState::Cleaned:
        return "cleaned";
    case UiRecordState::InitFailed:
        return "init-failed";
    case UiRecordState::Unreachable:
        return "unreachable";
    case UiRecordState::Retained:
        return "retained";
    case UiRecordState::LateCleanedRetained:
        return "late-cleaned-retained";
    }
    return "unknown";
}

struct UiThreadIdentity {
    std::uint64_t logical_thread_id = 0;
    std::uint64_t creation_stamp = 0;

    friend constexpr bool operator==(const UiThreadIdentity&,
                                     const UiThreadIdentity&) noexcept =
        default;
};

enum class ThreadHandleCapability {
    SynchronizeAndQueryLimited,
};

enum class UiCapability : std::uint32_t {
    ThreadHandle = 1u << 0,
    AgileDispatcher = 1u << 1,
    CleanupEvent = 1u << 2,
};

constexpr std::uint32_t UiCapabilityMask(
    UiCapability capability) noexcept {
    return static_cast<std::uint32_t>(capability);
}

constexpr std::uint32_t kUiCapabilityMask =
    UiCapabilityMask(UiCapability::ThreadHandle) |
    UiCapabilityMask(UiCapability::AgileDispatcher) |
    UiCapabilityMask(UiCapability::CleanupEvent);

struct UiThreadCapabilities {
    ThreadHandleCapability thread_handle =
        ThreadHandleCapability::SynchronizeAndQueryLimited;
    bool has_thread_handle = false;
    bool has_agile_dispatcher = false;
    bool has_cleanup_event = false;

    [[nodiscard]] constexpr std::uint32_t CreatedMask() const noexcept {
        return (has_thread_handle
                    ? UiCapabilityMask(UiCapability::ThreadHandle)
                    : 0u) |
               (has_agile_dispatcher
                    ? UiCapabilityMask(UiCapability::AgileDispatcher)
                    : 0u) |
               (has_cleanup_event
                    ? UiCapabilityMask(UiCapability::CleanupEvent)
                    : 0u);
    }
};

enum class UiWindowRole : std::uint8_t {
    TaskbarBridge,
    XamlHost,
    Count,
};

constexpr std::string_view ToString(UiWindowRole role) noexcept {
    switch (role) {
    case UiWindowRole::TaskbarBridge:
        return "taskbar-bridge";
    case UiWindowRole::XamlHost:
        return "xaml-host";
    case UiWindowRole::Count:
        break;
    }
    return "unknown";
}

struct UiWindowRoleCounts {
    std::uint64_t active = 0;
    std::uint64_t created = 0;
    std::uint64_t destroyed = 0;
    std::uint64_t replacements = 0;
    std::uint64_t failed_destroy_attempts = 0;
    std::uint64_t destroy_without_active = 0;
};

constexpr std::size_t kUiWindowRoleCount =
    static_cast<std::size_t>(UiWindowRole::Count);

struct UiWindowRoleReceipt {
    std::array<UiWindowRoleCounts, kUiWindowRoleCount> roles{};

    [[nodiscard]] const UiWindowRoleCounts& For(
        UiWindowRole role) const noexcept {
        static constexpr UiWindowRoleCounts invalid{};
        const auto index = static_cast<std::size_t>(role);
        return index < roles.size() ? roles[index] : invalid;
    }
};

// HWND identity deliberately stays outside this model. The adapter classifies
// a local bootstrap HWND, calls the original operation, and commits only its
// confirmed outcome here. Per-role counts support multiple same-role windows
// on one UI thread without retaining a window handle or address.
class UiWindowRoleLifecycle {
public:
    ProtocolStatus ObserveCreated(UiWindowRole role) {
        std::lock_guard lock(mutex_);
        UiWindowRoleCounts* counts = FindRoleLocked(role);
        if (counts == nullptr) {
            return ProtocolStatus::InvalidArgument;
        }

        ++counts->created;
        ++counts->active;
        if (counts->destroyed > counts->replacements) {
            ++counts->replacements;
        }
        return ProtocolStatus::Applied;
    }

    ProtocolStatus CompleteDestroy(UiWindowRole role, bool succeeded) {
        std::lock_guard lock(mutex_);
        UiWindowRoleCounts* counts = FindRoleLocked(role);
        if (counts == nullptr) {
            return ProtocolStatus::InvalidArgument;
        }

        if (!succeeded) {
            ++counts->failed_destroy_attempts;
            return ProtocolStatus::Applied;
        }

        ++counts->destroyed;
        if (counts->active != 0) {
            --counts->active;
        } else {
            ++counts->destroy_without_active;
        }
        return ProtocolStatus::Applied;
    }

    [[nodiscard]] UiWindowRoleReceipt Receipt() const {
        std::lock_guard lock(mutex_);
        return {roles_};
    }

private:
    UiWindowRoleCounts* FindRoleLocked(UiWindowRole role) noexcept {
        const auto index = static_cast<std::size_t>(role);
        return index < roles_.size() ? &roles_[index] : nullptr;
    }

    mutable std::mutex mutex_;
    std::array<UiWindowRoleCounts, kUiWindowRoleCount> roles_{};
};

struct UiRegisterResult {
    UiRegisterStatus status = UiRegisterStatus::InvalidArgument;
    std::uint64_t record_id = 0;
};

struct UiCleanupTicket {
    std::uint64_t generation = 0;
    std::uint64_t record_id = 0;
    std::uint64_t attempt = 0;
    std::uint64_t ticket_id = 0;

    [[nodiscard]] constexpr bool valid() const noexcept {
        return generation != 0 && record_id != 0 && attempt != 0 &&
               ticket_id != 0;
    }
};

struct UiCleanupResult {
    ProtocolStatus status = ProtocolStatus::InvalidState;
    UiCleanupTicket ticket{};
};

struct UiThreadReceipt {
    std::uint64_t record_id = 0;
    std::uint64_t generation = 0;
    UiThreadIdentity identity{};
    UiRecordState state = UiRecordState::Reserved;
    std::uint64_t cleanup_attempts = 0;
    std::int64_t last_error = 0;
    bool retry_eligible = false;
    bool terminal = false;
    std::uint32_t capability_created_mask = 0;
    std::uint32_t capability_released_mask = 0;
    std::uint32_t capability_retained_mask = 0;
    bool capabilities_terminal = false;
    UiReceiptReason reason;
};

template <typename T>
struct UiFixedSnapshot {
    std::array<T, kUiThreadRegistryCapacity> entries{};
    std::size_t count = 0;
    bool capacity_exhausted = false;

    [[nodiscard]] constexpr T* begin() noexcept {
        return entries.data();
    }

    [[nodiscard]] constexpr const T* begin() const noexcept {
        return entries.data();
    }

    [[nodiscard]] constexpr T* end() noexcept {
        return entries.data() + count;
    }

    [[nodiscard]] constexpr const T* end() const noexcept {
        return entries.data() + count;
    }

    [[nodiscard]] constexpr std::size_t size() const noexcept {
        return count;
    }

    [[nodiscard]] constexpr bool empty() const noexcept {
        return count == 0;
    }

    [[nodiscard]] constexpr T& operator[](std::size_t index) noexcept {
        return entries[index];
    }

    [[nodiscard]] constexpr const T& operator[](
        std::size_t index) const noexcept {
        return entries[index];
    }

    constexpr bool TryAppend(const T& value) noexcept {
        if (count >= entries.size()) {
            capacity_exhausted = true;
            return false;
        }
        entries[count++] = value;
        return true;
    }
};

using UiCleanupSnapshot = UiFixedSnapshot<UiCleanupTicket>;
using UiThreadReceiptSnapshot = UiFixedSnapshot<UiThreadReceipt>;

// The registry intentionally contains no HWND. Platform-specific thread
// handles and DispatcherQueue objects are owned by an adapter keyed by
// record_id; this core records that the required narrow capabilities exist.
class UiThreadRegistry {
public:
    explicit UiThreadRegistry(std::uint64_t generation)
        : generation_(generation) {}

    UiThreadRegistry(const UiThreadRegistry&) = delete;
    UiThreadRegistry& operator=(const UiThreadRegistry&) = delete;

    UiRegisterResult Reserve(
        std::uint64_t generation,
        UiThreadIdentity identity,
        UiThreadCapabilities capabilities) {
        std::lock_guard lock(mutex_);
        if (generation == 0 || generation != generation_) {
            return {UiRegisterStatus::GenerationMismatch, 0};
        }
        if (identity.logical_thread_id == 0 || identity.creation_stamp == 0) {
            return {UiRegisterStatus::InvalidArgument, 0};
        }
        const std::uint32_t capability_created_mask =
            capabilities.CreatedMask();
        if (capability_created_mask != kUiCapabilityMask ||
            capabilities.thread_handle !=
                ThreadHandleCapability::SynchronizeAndQueryLimited) {
            return {UiRegisterStatus::InvalidCapabilities, 0};
        }

        for (std::size_t index = 0; index < record_count_; ++index) {
            if (records_[index].identity == identity) {
                return {
                    UiRegisterStatus::Duplicate,
                    records_[index].record_id,
                };
            }
        }
        if (record_count_ >= records_.size()) {
            return {UiRegisterStatus::CapacityExhausted, 0};
        }

        const std::uint64_t record_id = next_record_id_++;
        Record& record = records_[record_count_++];
        record.record_id = record_id;
        record.identity = identity;
        record.state = UiRecordState::Reserved;
        // Record the adapter's observed owners, not a hand-authored constant.
        // The current adapter requires all three independent capabilities, but
        // keeping this assignment data-derived prevents a future optional
        // owner from silently appearing in receipts.
        record.capability_created_mask = capability_created_mask;
        return {UiRegisterStatus::Registered, record_id};
    }

    ProtocolStatus BeginInitialization(std::uint64_t record_id) {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (record->state != UiRecordState::Reserved) {
            return ProtocolStatus::InvalidState;
        }
        record->state = UiRecordState::Initializing;
        record->reason = "initializing";
        return ProtocolStatus::Applied;
    }

    ProtocolStatus CompleteInitialization(
        std::uint64_t record_id,
        bool succeeded,
        std::int64_t error = 0,
        UiReceiptReason reason = {}) {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (record->state != UiRecordState::Initializing) {
            return ProtocolStatus::InvalidState;
        }
        record->last_error = succeeded ? 0 : error;
        record->retry_eligible = false;
        record->state = succeeded ? UiRecordState::Initialized
                                  : UiRecordState::InitFailed;
        record->reason = reason.empty()
                             ? (succeeded ? "initialized"
                                          : "initialization-failed")
                             : reason;
        return ProtocolStatus::Applied;
    }

    // Covers failures before an adapter runtime record exists (for example,
    // make_shared or runtime-registry publication failure). Reserved and
    // Initializing are both owned by this initialization transaction.
    ProtocolStatus FailInitialization(
        std::uint64_t record_id,
        std::int64_t error,
        UiReceiptReason reason = "initialization-failed") {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (record->state != UiRecordState::Reserved &&
            record->state != UiRecordState::Initializing) {
            return ProtocolStatus::InvalidState;
        }
        record->state = UiRecordState::InitFailed;
        record->last_error = error;
        record->retry_eligible = false;
        record->active_ticket_id = 0;
        record->reason =
            reason.empty() ? UiReceiptReason{"initialization-failed"}
                           : reason;
        return ProtocolStatus::Applied;
    }

    // Convenience for pure tests that don't need to model the transactional
    // reserve/initialize boundary explicitly.
    UiRegisterResult RegisterInitialized(
        std::uint64_t generation,
        UiThreadIdentity identity,
        UiThreadCapabilities capabilities) {
        UiRegisterResult result =
            Reserve(generation, identity, capabilities);
        if (result.status != UiRegisterStatus::Registered) {
            return result;
        }
        if (BeginInitialization(result.record_id) !=
                ProtocolStatus::Applied ||
            CompleteInitialization(result.record_id, true) !=
                ProtocolStatus::Applied) {
            return {UiRegisterStatus::InvalidArgument, 0};
        }
        return result;
    }

    // Returns immutable cleanup capabilities. The platform adapter executes
    // dispatch/XAML cleanup after this method returns, hence outside the lock.
    // Capacity is proven before any record changes to Cleaning; every
    // subsequent operation is fixed-capacity and noexcept, so an exception can
    // never strand a Cleaning record without its owner ticket.
    UiCleanupSnapshot SnapshotForCleanup() {
        std::lock_guard lock(mutex_);
        UiCleanupSnapshot tickets;
        std::size_t eligible = 0;
        for (std::size_t index = 0; index < record_count_; ++index) {
            if (records_[index].state == UiRecordState::Initialized) {
                ++eligible;
            }
        }
        if (eligible > tickets.entries.size()) {
            tickets.capacity_exhausted = true;
            return tickets;
        }

        for (std::size_t index = 0; index < record_count_; ++index) {
            Record& record = records_[index];
            if (record.state != UiRecordState::Initialized) {
                continue;
            }
            (void)tickets.TryAppend(BeginCleanupLocked(record));
        }
        return tickets;
    }

    UiCleanupResult BeginCleanup(std::uint64_t record_id) {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return {};
        }
        if (record->state == UiRecordState::Initialized) {
            return {
                ProtocolStatus::Acquired,
                BeginCleanupLocked(*record),
            };
        }
        return {
            IsTerminal(record->state)
                ? ProtocolStatus::TerminalNoop
                : ProtocolStatus::Busy,
            {},
        };
    }

    UiCleanupResult BeginRetry(std::uint64_t record_id) {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return {};
        }
        if (record->state != UiRecordState::Retained) {
            return {IsTerminal(record->state)
                        ? ProtocolStatus::TerminalNoop
                        : ProtocolStatus::InvalidState,
                    {}};
        }
        if (!record->retry_eligible) {
            return {ProtocolStatus::NotRetryable, {}};
        }
        return {
            ProtocolStatus::Acquired,
            BeginCleanupLocked(*record),
        };
    }

    ProtocolStatus CompleteCleanup(const UiCleanupTicket& ticket,
                                   UiCleanupOutcome outcome,
                                   std::int64_t error = 0,
                                   bool retry_eligible = false,
                                   UiReceiptReason reason = {}) {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(ticket.record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (ticket.generation != generation_) {
            return ProtocolStatus::GenerationMismatch;
        }
        if (record->state != UiRecordState::Cleaning ||
            ticket.attempt != record->cleanup_attempts ||
            ticket.ticket_id == 0 ||
            ticket.ticket_id != record->active_ticket_id) {
            return ProtocolStatus::StaleTicket;
        }

        record->active_ticket_id = 0;
        record->last_ticket_id = ticket.ticket_id;
        record->last_error = error;
        record->retry_eligible = false;
        switch (outcome) {
        case UiCleanupOutcome::Cleaned:
            record->state = UiRecordState::Cleaned;
            record->last_error = 0;
            record->reason = reason.empty()
                                 ? UiReceiptReason{"cleaned"}
                                 : reason;
            break;
        case UiCleanupOutcome::Unreachable:
            record->state = UiRecordState::Unreachable;
            record->reason =
                reason.empty()
                    ? UiReceiptReason{"thread-unreachable"}
                    : reason;
            break;
        case UiCleanupOutcome::Retained:
            record->state = UiRecordState::Retained;
            // The cleanup contract permits exactly one bounded retry. Even if
            // an adapter accidentally asks for another retry after attempt
            // two, the protocol publishes a non-retryable terminal receipt.
            record->retry_eligible =
                retry_eligible && record->cleanup_attempts < 2;
            record->reason =
                reason.empty()
                    ? UiReceiptReason{"cleanup-retained"}
                    : reason;
            break;
        }
        return ProtocolStatus::Applied;
    }

    // Records the terminal disposition of adapter-owned raw capabilities.
    // The adapter closes handles/releases projected objects outside this
    // registry lock, then commits only the confirmed outcome. Masks may be
    // committed in independent fixed-capacity steps, but each capability has
    // exactly one terminal action.
    ProtocolStatus CompleteCapabilityDisposition(
        std::uint64_t record_id,
        std::uint32_t released_mask,
        std::uint32_t retained_mask) noexcept {
        try {
            std::lock_guard lock(mutex_);
            Record* record = FindRecordLocked(record_id);
            if (record == nullptr) {
                return ProtocolStatus::StaleTicket;
            }
            const std::uint32_t disposition =
                released_mask | retained_mask;
            const std::uint32_t already_terminal =
                record->capability_released_mask |
                record->capability_retained_mask;
            if (disposition == 0 ||
                (released_mask & retained_mask) != 0 ||
                (disposition & ~record->capability_created_mask) != 0) {
                return ProtocolStatus::InvalidArgument;
            }
            if ((disposition & already_terminal) != 0) {
                return ProtocolStatus::InvalidState;
            }
            record->capability_released_mask |= released_mask;
            record->capability_retained_mask |= retained_mask;
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    // A callback that completed after a bounded timeout may close the exact
    // retained attempt, provided no newer retry ticket has been issued.
    ProtocolStatus CompleteLateClean(const UiCleanupTicket& ticket,
                                     UiReceiptReason reason =
                                         "late-cleaned") {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(ticket.record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (ticket.generation != generation_) {
            return ProtocolStatus::GenerationMismatch;
        }
        if (record->state != UiRecordState::Retained ||
            record->active_ticket_id != 0 ||
            record->last_ticket_id != ticket.ticket_id ||
            record->cleanup_attempts != ticket.attempt) {
            return ProtocolStatus::StaleTicket;
        }

        record->state = UiRecordState::LateCleanedRetained;
        record->last_error = 0;
        record->retry_eligible = false;
        record->reason =
            reason.empty() ? UiReceiptReason{"late-cleaned"} : reason;
        return ProtocolStatus::Applied;
    }

    ProtocolStatus MarkThreadExited(
        std::uint64_t record_id,
        std::int64_t error = 0,
        UiReceiptReason reason = "thread-exited") {
        std::lock_guard lock(mutex_);
        Record* record = FindRecordLocked(record_id);
        if (record == nullptr) {
            return ProtocolStatus::StaleTicket;
        }
        if (IsTerminal(record->state)) {
            return ProtocolStatus::TerminalNoop;
        }

        record->active_ticket_id = 0;
        record->state = UiRecordState::Unreachable;
        record->last_error = error;
        record->retry_eligible = false;
        record->reason =
            reason.empty() ? UiReceiptReason{"thread-exited"} : reason;
        return ProtocolStatus::Applied;
    }

    // Makes missing final outcomes explicit rather than dropping records.
    void SealGeneration(std::int64_t error = 0) {
        std::lock_guard lock(mutex_);
        for (std::size_t index = 0; index < record_count_; ++index) {
            Record& record = records_[index];
            const std::uint32_t terminal_capabilities =
                record.capability_released_mask |
                record.capability_retained_mask;
            record.capability_retained_mask |=
                record.capability_created_mask &
                ~terminal_capabilities;
            if (IsTerminal(record.state)) {
                continue;
            }
            if (record.active_ticket_id != 0) {
                // Preserve the exact in-flight ticket so a callback that was
                // already dispatched before sealing can still publish a
                // late-cleaned-retained terminal receipt.
                record.last_ticket_id = record.active_ticket_id;
            }
            record.active_ticket_id = 0;
            record.state = UiRecordState::Retained;
            record.last_error = error;
            record.retry_eligible = false;
            record.reason = "generation-sealed-without-terminal-cleanup";
        }
    }

    [[nodiscard]] UiThreadReceiptSnapshot Receipts() const {
        std::lock_guard lock(mutex_);
        UiThreadReceiptSnapshot receipts;
        if (record_count_ > receipts.entries.size()) {
            receipts.capacity_exhausted = true;
            return receipts;
        }
        for (std::size_t index = 0; index < record_count_; ++index) {
            const Record& record = records_[index];
            (void)receipts.TryAppend({
                record.record_id,
                generation_,
                record.identity,
                record.state,
                record.cleanup_attempts,
                record.last_error,
                record.retry_eligible,
                IsTerminal(record.state),
                record.capability_created_mask,
                record.capability_released_mask,
                record.capability_retained_mask,
                (record.capability_released_mask |
                 record.capability_retained_mask) ==
                    record.capability_created_mask,
                record.reason,
            });
        }
        return receipts;
    }

private:
    struct Record {
        std::uint64_t record_id = 0;
        UiThreadIdentity identity{};
        UiRecordState state = UiRecordState::Reserved;
        std::uint64_t cleanup_attempts = 0;
        std::uint64_t active_ticket_id = 0;
        std::uint64_t last_ticket_id = 0;
        std::int64_t last_error = 0;
        bool retry_eligible = false;
        std::uint32_t capability_created_mask = 0;
        std::uint32_t capability_released_mask = 0;
        std::uint32_t capability_retained_mask = 0;
        UiReceiptReason reason = "reserved";
    };

    static constexpr bool IsTerminal(UiRecordState state) noexcept {
        return state == UiRecordState::Cleaned ||
               state == UiRecordState::InitFailed ||
               state == UiRecordState::Unreachable ||
               state == UiRecordState::Retained ||
               state == UiRecordState::LateCleanedRetained;
    }

    UiCleanupTicket BeginCleanupLocked(Record& record) noexcept {
        record.state = UiRecordState::Cleaning;
        ++record.cleanup_attempts;
        record.active_ticket_id = next_ticket_id_++;
        record.retry_eligible = false;
        record.reason = "cleanup-dispatched";
        return {
            generation_,
            record.record_id,
            record.cleanup_attempts,
            record.active_ticket_id,
        };
    }

    Record* FindRecordLocked(std::uint64_t record_id) noexcept {
        for (std::size_t index = 0; index < record_count_; ++index) {
            if (records_[index].record_id == record_id) {
                return &records_[index];
            }
        }
        return nullptr;
    }

    mutable std::mutex mutex_;
    std::uint64_t generation_ = 0;
    std::uint64_t next_record_id_ = 1;
    std::uint64_t next_ticket_id_ = 1;
    std::array<Record, kUiThreadRegistryCapacity> records_{};
    std::size_t record_count_ = 0;
};

enum class DispatchState {
    Empty,
    Registered,
    Claimed,
    Invoked,
    Cancelled,
    Completed,
    Retained,
};

enum class HookState {
    Absent,
    InstalledTracked,
    InstalledUntracked,
    Removed,
    Retained,
};

// Callback ownership is intentionally independent from hook ownership.  A
// failed unhook must never erase the fact that a callback has already claimed
// its reference and is still running.
enum class CallbackPhase {
    None,
    Pending,
    Claimed,
    Completed,
    Cancelled,
};

enum class DispatchRegisterStatus {
    Registered,
    SlotOccupied,
    Poisoned,
    InvalidArgument,
    ProtocolFailure,
};

enum class DispatchClaimStatus {
    Claimed,
    Duplicate,
    Late,
    Poisoned,
    Empty,
    ProtocolFailure,
};

enum class DispatchCancelStatus {
    Cancelled,
    ClaimWon,
    TerminalNoop,
    Empty,
    ProtocolFailure,
};

enum class DispatchReason {
    None,
    Registered,
    CallbackClaimed,
    Cancelled,
    SenderCancelled,
    SendTimeout,
    ClaimWon,
    HookTrackingFailed,
    HookRemovalFailed,
    HookRemoved,
    HookRetrySucceeded,
    TrackingInvariantFailed,
    CallbackCompleted,
    CallbackFailed,
    TargetExited,
    TargetRevalidationFailed,
    CallbackNotInvoked,
    HookInstallFailed,
    ProtocolFailure,
};

enum class DispatchRetainedReason {
    None,
    HookRemovalFailed,
    TrackingInvariantFailed,
    ProtocolFailure,
};

enum class DispatchResourceKind {
    SenderReference,
    CallbackReference,
    HookHandle,
};

enum class DispatchResourceDisposition {
    Absent,
    Inflight,
    Released,
    Retained,
};

struct DispatchResourceReceipt {
    DispatchResourceKind kind =
        DispatchResourceKind::SenderReference;
    DispatchResourceDisposition disposition =
        DispatchResourceDisposition::Absent;
    DispatchRetainedReason retained_reason =
        DispatchRetainedReason::None;
};

constexpr std::string_view ToString(DispatchState state) noexcept {
    switch (state) {
    case DispatchState::Empty:
        return "empty";
    case DispatchState::Registered:
        return "registered";
    case DispatchState::Claimed:
        return "claimed";
    case DispatchState::Invoked:
        return "invoked";
    case DispatchState::Cancelled:
        return "cancelled";
    case DispatchState::Completed:
        return "completed";
    case DispatchState::Retained:
        return "retained";
    }
    return "unknown";
}

constexpr std::string_view ToString(HookState state) noexcept {
    switch (state) {
    case HookState::Absent:
        return "absent";
    case HookState::InstalledTracked:
        return "installed-tracked";
    case HookState::InstalledUntracked:
        return "installed-untracked";
    case HookState::Removed:
        return "removed";
    case HookState::Retained:
        return "retained";
    }
    return "unknown";
}

constexpr std::string_view ToString(CallbackPhase phase) noexcept {
    switch (phase) {
    case CallbackPhase::None:
        return "none";
    case CallbackPhase::Pending:
        return "pending";
    case CallbackPhase::Claimed:
        return "claimed";
    case CallbackPhase::Completed:
        return "completed";
    case CallbackPhase::Cancelled:
        return "cancelled";
    }
    return "unknown";
}

constexpr std::string_view ToString(DispatchReason reason) noexcept {
    switch (reason) {
    case DispatchReason::None:
        return "none";
    case DispatchReason::Registered:
        return "registered";
    case DispatchReason::CallbackClaimed:
        return "callback-claimed";
    case DispatchReason::Cancelled:
        return "cancelled";
    case DispatchReason::SenderCancelled:
        return "sender-cancelled";
    case DispatchReason::SendTimeout:
        return "send-timeout";
    case DispatchReason::ClaimWon:
        return "claim-won";
    case DispatchReason::HookTrackingFailed:
        return "hook-tracking-failed";
    case DispatchReason::HookRemovalFailed:
        return "hook-removal-failed";
    case DispatchReason::HookRemoved:
        return "hook-removed";
    case DispatchReason::HookRetrySucceeded:
        return "hook-retry-succeeded";
    case DispatchReason::TrackingInvariantFailed:
        return "tracking-invariant-failed";
    case DispatchReason::CallbackCompleted:
        return "callback-completed";
    case DispatchReason::CallbackFailed:
        return "callback-failed";
    case DispatchReason::TargetExited:
        return "target-exited";
    case DispatchReason::TargetRevalidationFailed:
        return "target-revalidation-failed";
    case DispatchReason::CallbackNotInvoked:
        return "callback-not-invoked";
    case DispatchReason::HookInstallFailed:
        return "hook-install-failed";
    case DispatchReason::ProtocolFailure:
        return "protocol-failure";
    }
    return "unknown";
}

constexpr std::string_view ToString(
    DispatchRetainedReason reason) noexcept {
    switch (reason) {
    case DispatchRetainedReason::None:
        return "none";
    case DispatchRetainedReason::HookRemovalFailed:
        return "hook-removal-failed";
    case DispatchRetainedReason::TrackingInvariantFailed:
        return "tracking-invariant-failed";
    case DispatchRetainedReason::ProtocolFailure:
        return "protocol-failure";
    }
    return "unknown";
}

struct DispatchReceipt {
    std::uint64_t dispatch_id = 0;
    std::uint64_t generation = 0;
    DispatchState state = DispatchState::Empty;
    HookState hook_state = HookState::Absent;
    CallbackPhase callback_phase = CallbackPhase::None;
    std::uint64_t resources_created = 0;
    std::uint64_t resources_released = 0;
    std::uint64_t resources_retained = 0;
    std::uint64_t resources_inflight = 0;
    std::uint64_t late_callbacks = 0;
    std::uint64_t duplicate_callbacks = 0;
    std::uint64_t double_release = 0;
    std::int64_t last_error = 0;
    bool sender_ref_held = false;
    bool callback_ref_held = false;
    bool poisoned = false;
    DispatchReason reason = DispatchReason::None;
    DispatchRetainedReason retained_reason =
        DispatchRetainedReason::None;
    bool protocol_failure = false;
    std::array<DispatchResourceReceipt, 3> resources{{
        {DispatchResourceKind::SenderReference,
         DispatchResourceDisposition::Absent,
         DispatchRetainedReason::None},
        {DispatchResourceKind::CallbackReference,
         DispatchResourceDisposition::Absent,
         DispatchRetainedReason::None},
        {DispatchResourceKind::HookHandle,
         DispatchResourceDisposition::Absent,
         DispatchRetainedReason::None},
    }};
};

// One object represents exactly one dispatch slot for one activation
// generation. Callback claim and sender cancellation are mutually exclusive.
class DispatchSlot {
public:
    DispatchRegisterStatus Register(std::uint64_t generation,
                                    std::uint64_t dispatch_id,
                                    bool hook_installed,
                                    bool hook_tracked) noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (generation == 0 || dispatch_id == 0) {
                return DispatchRegisterStatus::InvalidArgument;
            }
            if (poisoned_) {
                return DispatchRegisterStatus::Poisoned;
            }
            if (state_ != DispatchState::Empty) {
                return DispatchRegisterStatus::SlotOccupied;
            }

            generation_ = generation;
            dispatch_id_ = dispatch_id;
            hook_state_ = hook_installed
                              ? (hook_tracked
                                     ? HookState::InstalledTracked
                                     : HookState::InstalledUntracked)
                              : HookState::Absent;
            callback_phase_ = CallbackPhase::Pending;
            sender_ref_held_ = true;
            callback_ref_held_ = true;
            sender_disposition_ =
                DispatchResourceDisposition::Inflight;
            callback_disposition_ =
                DispatchResourceDisposition::Inflight;
            hook_disposition_ =
                hook_installed
                    ? DispatchResourceDisposition::Inflight
                    : DispatchResourceDisposition::Absent;
            retained_reason_ = DispatchRetainedReason::None;
            reason_ = DispatchReason::Registered;
            state_ = DispatchState::Registered;
            return DispatchRegisterStatus::Registered;
        } catch (...) {
            return DispatchRegisterStatus::ProtocolFailure;
        }
    }

    DispatchClaimStatus ClaimCallback() noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (callback_phase_ == CallbackPhase::None) {
                return DispatchClaimStatus::Empty;
            }
            if (callback_phase_ == CallbackPhase::Pending) {
                if (poisoned_) {
                    return DispatchClaimStatus::Poisoned;
                }
                // Fixed reason assignment and the state transition are both
                // non-throwing. A caller that observes Claimed therefore owns
                // the still-held callback reference without a partial commit.
                reason_ = DispatchReason::CallbackClaimed;
                callback_phase_ = CallbackPhase::Claimed;
                state_ = DispatchState::Claimed;
                return DispatchClaimStatus::Claimed;
            }
            if (callback_phase_ == CallbackPhase::Claimed ||
                callback_phase_ == CallbackPhase::Completed) {
                SaturatingIncrementDispatchCount(
                    duplicate_callbacks_);
                return DispatchClaimStatus::Duplicate;
            }

            SaturatingIncrementDispatchCount(late_callbacks_);
            return DispatchClaimStatus::Late;
        } catch (...) {
            return DispatchClaimStatus::ProtocolFailure;
        }
    }

    DispatchCancelStatus Cancel(std::int64_t error,
                                DispatchReason reason,
                                bool poison) noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (callback_phase_ == CallbackPhase::None) {
                return DispatchCancelStatus::Empty;
            }
            if (poison) {
                poisoned_ = true;
            }
            if (callback_phase_ == CallbackPhase::Pending) {
                last_error_ = error;
                reason_ = reason == DispatchReason::None
                              ? DispatchReason::Cancelled
                              : reason;
                callback_phase_ = CallbackPhase::Cancelled;
                state_ = DispatchState::Cancelled;
                ReleaseSenderLocked();
                // Removing the pending slot wins the only claim-or-cancel
                // race. A late hook sees no slot and never dereferences the
                // raw value, so its callback reference can be retired before
                // hook removal.
                ReleaseCallbackLocked();
                UpdateTerminalLocked();
                return DispatchCancelStatus::Cancelled;
            }
            if (callback_phase_ == CallbackPhase::Claimed) {
                last_error_ = error;
                if (reason != DispatchReason::None) {
                    reason_ = reason;
                }
                ReleaseSenderLocked();
                UpdateTerminalLocked();
                return DispatchCancelStatus::ClaimWon;
            }
            return DispatchCancelStatus::TerminalNoop;
        } catch (...) {
            return DispatchCancelStatus::ProtocolFailure;
        }
    }

    ProtocolStatus MarkHookTrackingFailure(
        std::int64_t error,
        DispatchReason reason =
            DispatchReason::HookTrackingFailed) noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (hook_state_ != HookState::InstalledTracked) {
                return ProtocolStatus::InvalidState;
            }
            hook_state_ = HookState::InstalledUntracked;
            last_error_ = error;
            reason_ = reason == DispatchReason::None
                          ? DispatchReason::HookTrackingFailed
                          : reason;
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    ProtocolStatus CompleteHookRemoval(
        bool succeeded,
        std::int64_t error = 0,
        DispatchReason reason = DispatchReason::None) noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (hook_state_ == HookState::Absent ||
                hook_state_ == HookState::Removed) {
                return ProtocolStatus::TerminalNoop;
            }
            if (hook_state_ != HookState::InstalledTracked &&
                hook_state_ != HookState::InstalledUntracked &&
                hook_state_ != HookState::Retained) {
                return ProtocolStatus::InvalidState;
            }

            if (!succeeded) {
                hook_state_ = HookState::Retained;
                retained_reason_ = ToDispatchRetainedReason(reason);
                hook_disposition_ =
                    DispatchResourceDisposition::Retained;
                poisoned_ = true;
                last_error_ = error;
                reason_ = reason == DispatchReason::None
                              ? DispatchReason::HookRemovalFailed
                              : reason;
                UpdateTerminalLocked();
                return ProtocolStatus::Applied;
            }

            hook_state_ = HookState::Removed;
            retained_reason_ = DispatchRetainedReason::None;
            hook_disposition_ =
                DispatchResourceDisposition::Released;
            if (reason != DispatchReason::None) {
                reason_ = reason;
            }
            UpdateTerminalLocked();
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    ProtocolStatus CompleteCallback(
        bool succeeded,
        std::int64_t error = 0,
        DispatchReason reason = DispatchReason::None) noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (callback_phase_ == CallbackPhase::Completed) {
                SaturatingIncrementDispatchCount(
                    duplicate_callbacks_);
                return ProtocolStatus::Duplicate;
            }
            if (callback_phase_ != CallbackPhase::Claimed ||
                !callback_ref_held_) {
                return ProtocolStatus::InvalidState;
            }

            callback_phase_ = CallbackPhase::Completed;
            state_ = DispatchState::Invoked;
            ReleaseCallbackLocked();
            if (!succeeded) {
                poisoned_ = true;
                last_error_ = error;
                reason_ = reason == DispatchReason::None
                              ? DispatchReason::CallbackFailed
                              : reason;
            } else {
                reason_ = reason == DispatchReason::None
                              ? DispatchReason::CallbackCompleted
                              : reason;
            }
            UpdateTerminalLocked();
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    ProtocolStatus SenderDone() noexcept {
        try {
            std::lock_guard lock(mutex_);
            if (!sender_ref_held_) {
                return ProtocolStatus::TerminalNoop;
            }
            ReleaseSenderLocked();
            UpdateTerminalLocked();
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    ProtocolStatus Poison(
        std::int64_t error,
        DispatchReason reason =
            DispatchReason::ProtocolFailure) noexcept {
        try {
            std::lock_guard lock(mutex_);
            poisoned_ = true;
            last_error_ = error;
            if (reason != DispatchReason::None) {
                reason_ = reason;
            }
            if (hook_state_ == HookState::Retained &&
                reason == DispatchReason::ProtocolFailure) {
                retained_reason_ =
                    DispatchRetainedReason::ProtocolFailure;
            }
            return ProtocolStatus::Applied;
        } catch (...) {
            return ProtocolStatus::ProtocolFailure;
        }
    }

    [[nodiscard]] DispatchReceipt Receipt() const noexcept {
        try {
            std::lock_guard lock(mutex_);
            DispatchReceipt receipt;
            receipt.dispatch_id = dispatch_id_;
            receipt.generation = generation_;
            receipt.state = state_;
            receipt.hook_state = hook_state_;
            receipt.callback_phase = callback_phase_;
            receipt.late_callbacks = late_callbacks_;
            receipt.duplicate_callbacks = duplicate_callbacks_;
            receipt.double_release = double_release_;
            receipt.last_error = last_error_;
            receipt.sender_ref_held = sender_ref_held_;
            receipt.callback_ref_held = callback_ref_held_;
            receipt.poisoned = poisoned_;
            receipt.reason = reason_;
            receipt.retained_reason = retained_reason_;
            receipt.resources = {{
                {DispatchResourceKind::SenderReference,
                 sender_disposition_,
                 DispatchRetainedReason::None},
                {DispatchResourceKind::CallbackReference,
                 callback_disposition_,
                 DispatchRetainedReason::None},
                {DispatchResourceKind::HookHandle,
                 hook_disposition_,
                 hook_disposition_ ==
                         DispatchResourceDisposition::Retained
                     ? retained_reason_
                     : DispatchRetainedReason::None},
            }};
            for (const auto& resource : receipt.resources) {
                if (resource.disposition !=
                    DispatchResourceDisposition::Absent) {
                    ++receipt.resources_created;
                }
                switch (resource.disposition) {
                case DispatchResourceDisposition::Released:
                    ++receipt.resources_released;
                    break;
                case DispatchResourceDisposition::Retained:
                    ++receipt.resources_retained;
                    break;
                case DispatchResourceDisposition::Inflight:
                    ++receipt.resources_inflight;
                    break;
                case DispatchResourceDisposition::Absent:
                    break;
                }
            }
            return receipt;
        } catch (...) {
            DispatchReceipt failed;
            failed.poisoned = true;
            failed.reason = DispatchReason::ProtocolFailure;
            failed.protocol_failure = true;
            return failed;
        }
    }

private:
    static void SaturatingIncrementDispatchCount(
        std::uint64_t& value) noexcept {
        if (value !=
            std::numeric_limits<std::uint64_t>::max()) {
            ++value;
        }
    }

    static DispatchRetainedReason ToDispatchRetainedReason(
        DispatchReason reason) noexcept {
        switch (reason) {
        case DispatchReason::TrackingInvariantFailed:
            return DispatchRetainedReason::TrackingInvariantFailed;
        case DispatchReason::ProtocolFailure:
            return DispatchRetainedReason::ProtocolFailure;
        case DispatchReason::None:
        case DispatchReason::HookRemovalFailed:
        default:
            return DispatchRetainedReason::HookRemovalFailed;
        }
    }

    void ReleaseSenderLocked() {
        if (!sender_ref_held_) {
            SaturatingIncrementDispatchCount(
                double_release_);
            return;
        }
        sender_ref_held_ = false;
        sender_disposition_ =
            DispatchResourceDisposition::Released;
    }

    void ReleaseCallbackLocked() {
        if (!callback_ref_held_) {
            SaturatingIncrementDispatchCount(
                double_release_);
            return;
        }
        callback_ref_held_ = false;
        callback_disposition_ =
            DispatchResourceDisposition::Released;
    }

    void UpdateTerminalLocked() {
        if (callback_phase_ == CallbackPhase::Claimed) {
            state_ = DispatchState::Claimed;
            return;
        }
        if (callback_phase_ == CallbackPhase::Pending) {
            state_ = DispatchState::Registered;
            return;
        }
        if (!sender_ref_held_ && !callback_ref_held_ &&
            (hook_state_ == HookState::Absent ||
             hook_state_ == HookState::Removed)) {
            state_ = DispatchState::Completed;
            return;
        }
        if (hook_state_ == HookState::Retained) {
            state_ = DispatchState::Retained;
            return;
        }
        if (callback_phase_ == CallbackPhase::Completed) {
            state_ = DispatchState::Invoked;
        } else if (callback_phase_ == CallbackPhase::Cancelled) {
            state_ = DispatchState::Cancelled;
        }
    }

    mutable std::mutex mutex_;
    std::uint64_t dispatch_id_ = 0;
    std::uint64_t generation_ = 0;
    DispatchState state_ = DispatchState::Empty;
    HookState hook_state_ = HookState::Absent;
    CallbackPhase callback_phase_ = CallbackPhase::None;
    bool sender_ref_held_ = false;
    bool callback_ref_held_ = false;
    bool poisoned_ = false;
    DispatchResourceDisposition sender_disposition_ =
        DispatchResourceDisposition::Absent;
    DispatchResourceDisposition callback_disposition_ =
        DispatchResourceDisposition::Absent;
    DispatchResourceDisposition hook_disposition_ =
        DispatchResourceDisposition::Absent;
    std::uint64_t late_callbacks_ = 0;
    std::uint64_t duplicate_callbacks_ = 0;
    std::uint64_t double_release_ = 0;
    std::int64_t last_error_ = 0;
    DispatchReason reason_ = DispatchReason::None;
    DispatchRetainedReason retained_reason_ =
        DispatchRetainedReason::None;
};

}  // namespace jarvis::resource_protocol
