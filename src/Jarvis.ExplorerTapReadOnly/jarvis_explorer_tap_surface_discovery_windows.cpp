#include "jarvis_explorer_tap_surface_discovery.h"

#include <windows.h>
#include <xamlom.h>

#include <cwchar>
#include <mutex>

#include <winrt/base.h>

#if JARVIS_COMPILE_REVIEWED_XAML_SURFACE_CALLBACK == 1

namespace {

[[nodiscard]] bool BstrEquals(
    const BSTR value,
    const wchar_t* const expected) noexcept {
    if (value == nullptr || expected == nullptr) {
        return false;
    }
    const auto expected_length = std::wcslen(expected);
    return SysStringLen(value) == expected_length &&
           std::wmemcmp(value, expected, expected_length) == 0;
}

[[nodiscard]] jarvis_tap_visual_type ClassifyType(
    const BSTR value) noexcept {
    if (BstrEquals(
            value,
            L"FileExplorerExtensions.FileExplorerTabControl")) {
        return JARVIS_TAP_VISUAL_TYPE_TAB_CONTROL;
    }
    if (BstrEquals(
            value,
            L"FileExplorerExtensions.CommandBarControl")) {
        return JARVIS_TAP_VISUAL_TYPE_COMMAND_BAR_CONTROL;
    }
    if (BstrEquals(
            value,
            L"Microsoft.UI.Xaml.Controls.Grid")) {
        return JARVIS_TAP_VISUAL_TYPE_GRID;
    }
    if (BstrEquals(
            value,
            L"Microsoft.UI.Xaml.Controls.NavigationView")) {
        return JARVIS_TAP_VISUAL_TYPE_NAVIGATION_VIEW;
    }
    return JARVIS_TAP_VISUAL_TYPE_OTHER;
}

[[nodiscard]] jarvis_tap_visual_name ClassifyName(
    const BSTR value) noexcept {
    if (BstrEquals(value, L"TabContainerGrid")) {
        return JARVIS_TAP_VISUAL_NAME_TAB_CONTAINER_GRID;
    }
    if (BstrEquals(value, L"CommandBarControlRootGrid")) {
        return JARVIS_TAP_VISUAL_NAME_COMMAND_BAR_ROOT_GRID;
    }
    return JARVIS_TAP_VISUAL_NAME_NONE_OR_OTHER;
}

class JarvisReviewedSurfaceDiscoveryCallback
    : public winrt::implements<
          JarvisReviewedSurfaceDiscoveryCallback,
          IVisualTreeServiceCallback2,
          winrt::non_agile> {
public:
    explicit JarvisReviewedSurfaceDiscoveryCallback(
        jarvis_tap_surface_discovery_instance* const instance) noexcept
        : instance_(instance) {}

    HRESULT STDMETHODCALLTYPE OnVisualTreeChange(
        const ParentChildRelation relation,
        const VisualElement element,
        const VisualMutationType mutation_type) noexcept override try {
        std::lock_guard<std::mutex> lock(gate_);
        if (instance_ == nullptr) {
            return E_POINTER;
        }
        if (sequence_ >=
            JARVIS_TAP_DISCOVERY_MAX_EVENT_COUNT) {
            jarvis_tap_surface_discovery_fail_closed(
                instance_,
                JARVIS_TAP_DISCOVERY_RESULT_NODE_CAPACITY_EXCEEDED);
            return HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW);
        }
        ++sequence_;
        const jarvis_tap_visual_tree_event event{
            .size = sizeof(jarvis_tap_visual_tree_event),
            .abi_version = JARVIS_EXPLORER_TRANSPORT_ABI_VERSION,
            .sequence = sequence_,
            .mutation =
                mutation_type == Add
                    ? JARVIS_TAP_VISUAL_MUTATION_ADD
                    : mutation_type == Remove
                        ? JARVIS_TAP_VISUAL_MUTATION_REMOVE
                        : 2U,
            .type = ClassifyType(element.Type),
            .name = ClassifyName(element.Name),
            .child_index = relation.ChildIndex,
            .parent_handle = relation.Parent,
            .child_handle = relation.Child,
            .instance_handle = element.Handle,
            .reserved = 0U,
            .reserved2 = 0U,
        };
        const auto response =
            jarvis_tap_surface_discovery_ingest(instance_, &event);
        return response.state == JARVIS_TAP_DISCOVERY_STATE_BLOCKED
            ? E_UNEXPECTED
            : S_OK;
    } catch (...) {
        jarvis_tap_surface_discovery_fail_closed(
            instance_,
            JARVIS_TAP_DISCOVERY_RESULT_FOREIGN_EXCEPTION);
        return E_UNEXPECTED;
    }

    HRESULT STDMETHODCALLTYPE OnElementStateChanged(
        InstanceHandle,
        VisualElementState,
        LPCWSTR) noexcept override {
        return S_OK;
    }

private:
    jarvis_tap_surface_discovery_instance* instance_;
    std::mutex gate_;
    std::uint64_t sequence_ = 0ULL;
};

}  // namespace

long jarvis_tap_create_surface_discovery_callback_review(
    jarvis_tap_surface_discovery_instance* const instance,
    IUnknown** const output) noexcept try {
    if (output == nullptr) {
        return E_POINTER;
    }
    *output = nullptr;
    if (instance == nullptr ||
        instance->state != JARVIS_TAP_DISCOVERY_STATE_COLLECTING) {
        return E_INVALIDARG;
    }
    return winrt::make<
        JarvisReviewedSurfaceDiscoveryCallback>(instance)
        .as(
            __uuidof(IVisualTreeServiceCallback2),
            reinterpret_cast<void**>(output));
} catch (...) {
    if (output != nullptr) {
        *output = nullptr;
    }
    jarvis_tap_surface_discovery_fail_closed(
        instance,
        JARVIS_TAP_DISCOVERY_RESULT_FOREIGN_EXCEPTION);
    return E_UNEXPECTED;
}

#else

long jarvis_tap_create_surface_discovery_callback_review(
    jarvis_tap_surface_discovery_instance* const instance,
    IUnknown** const output) noexcept {
    if (output == nullptr) {
        return static_cast<long>(0x80004003UL);
    }
    *output = nullptr;
    static_cast<void>(instance);
    return static_cast<long>(0x80070005UL);
}

#endif
