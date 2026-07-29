#include "jarvis_explorer_tap_readonly.h"

#include <windows.h>
#include <ocidl.h>
#include <xamlom.h>

#include <atomic>
#include <cstdint>
#include <limits>

#include <winrt/base.h>

namespace {

// {A57A77E1-A9B3-4AA1-8D41-E4FBB3B1D72F}
constexpr CLSID CLSID_JarvisExplorerReadOnlyTap = {
    0xa57a77e1,
    0xa9b3,
    0x4aa1,
    {0x8d, 0x41, 0xe4, 0xfb, 0xb3, 0xb1, 0xd7, 0x2f},
};

std::atomic<std::uint64_t> g_server_lock_balance{0U};

class ReadOnlyVisualTreeCallback
    : public winrt::implements<
          ReadOnlyVisualTreeCallback,
          IVisualTreeServiceCallback2,
          winrt::non_agile> {
public:
    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(
        ParentChildRelation,
        VisualElement,
        VisualMutationType) noexcept override {
        const auto previous = event_count_.fetch_add(
            1U,
            std::memory_order_relaxed);
        return previous < 2048U ? S_OK : S_FALSE;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(
        InstanceHandle,
        VisualElementState,
        LPCWSTR) noexcept override {
        return S_OK;
    }

private:
    std::atomic<std::uint32_t> event_count_{0U};
};

class JarvisExplorerReadOnlyTap
    : public winrt::implements<
          JarvisExplorerReadOnlyTap,
          IObjectWithSite,
          winrt::non_agile> {
public:
    HRESULT STDMETHODCALLTYPE SetSite(
        IUnknown* site) noexcept override {
        static_cast<void>(site);

        // Phase 12 deliberately compiles a real COM/TAP shape but never a
        // connectable implementation. A future phase must replace this gate
        // only after exact binary review and a separate live approval.
        static_assert(JARVIS_ENABLE_LIVE_XAML_READONLY == 0);
        return E_ACCESSDENIED;
    }

    HRESULT STDMETHODCALLTYPE GetSite(
        REFIID,
        void** output) noexcept override {
        if (output == nullptr) {
            return E_POINTER;
        }
        *output = nullptr;
        return E_ACCESSDENIED;
    }
};

template <typename T>
class TapClassFactory
    : public winrt::implements<
          TapClassFactory<T>,
          IClassFactory,
          winrt::non_agile> {
public:
    HRESULT STDMETHODCALLTYPE CreateInstance(
        IUnknown* outer,
        REFIID interface_id,
        void** output) noexcept override try {
        if (output == nullptr) {
            return E_POINTER;
        }
        *output = nullptr;
        if (outer != nullptr) {
            return CLASS_E_NOAGGREGATION;
        }
        return winrt::make<T>().as(interface_id, output);
    } catch (...) {
        return E_UNEXPECTED;
    }

    HRESULT STDMETHODCALLTYPE LockServer(
        const BOOL lock) noexcept override try {
        if (lock != FALSE) {
            ++winrt::get_module_lock();
            auto balance = g_server_lock_balance.load(
                std::memory_order_acquire);
            while (true) {
                if (balance ==
                    std::numeric_limits<std::uint64_t>::max()) {
                    --winrt::get_module_lock();
                    return HRESULT_FROM_WIN32(
                        ERROR_ARITHMETIC_OVERFLOW);
                }
                if (g_server_lock_balance.compare_exchange_weak(
                        balance,
                        balance + 1U,
                        std::memory_order_release,
                        std::memory_order_acquire)) {
                    return S_OK;
                }
            }
        }

        auto balance = g_server_lock_balance.load(
            std::memory_order_acquire);
        while (balance != 0U) {
            if (g_server_lock_balance.compare_exchange_weak(
                    balance,
                    balance - 1U,
                    std::memory_order_acq_rel,
                    std::memory_order_acquire)) {
                --winrt::get_module_lock();
                return S_OK;
            }
        }
        return HRESULT_FROM_WIN32(ERROR_INVALID_STATE);
    } catch (...) {
        return E_UNEXPECTED;
    }
};

}  // namespace

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdll-attribute-on-redeclaration"

extern "C" __declspec(dllexport) HRESULT STDAPICALLTYPE
DllGetClassObject(
    REFCLSID class_id,
    REFIID interface_id,
    void** output) {
    if (output == nullptr) {
        return E_POINTER;
    }
    *output = nullptr;
    if (class_id != CLSID_JarvisExplorerReadOnlyTap) {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    try {
        return winrt::make<
            TapClassFactory<JarvisExplorerReadOnlyTap>>()
            .as(interface_id, output);
    } catch (...) {
        return E_UNEXPECTED;
    }
}

extern "C" __declspec(dllexport) HRESULT STDAPICALLTYPE
DllCanUnloadNow() {
    return winrt::get_module_lock() == 0U ? S_OK : S_FALSE;
}

#pragma clang diagnostic pop
