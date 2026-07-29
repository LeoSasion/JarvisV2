#include "jarvis_explorer_tap_xaml_read_bridge.h"

#include <windows.h>
#include <winstring.h>
#include <xamlom.h>

#ifdef GetCurrentTime
#undef GetCurrentTime
#endif

#include <winrt/Windows.UI.h>
#include <winrt/Windows.UI.Xaml.Media.h>

#include <cmath>
#include <cstdint>
#include <cwchar>

#if JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE == 1

namespace {

using SolidColorBrushAbi = winrt::impl::abi_t<
    winrt::Windows::UI::Xaml::Media::ISolidColorBrush>;
using BrushAbi =
    winrt::impl::abi_t<winrt::Windows::UI::Xaml::Media::IBrush>;

inline constexpr wchar_t kPropertyNames
    [JARVIS_TRANSPORT_REQUIRED_PROPERTY_COUNT][12] = {
        L"Background",
        L"Foreground",
        L"BorderBrush",
    };
inline constexpr wchar_t kSolidColorBrushRuntimeClass[] =
    L"Windows.UI.Xaml.Media.SolidColorBrush";

[[nodiscard]] HRESULT SafeQueryInterface(
    IUnknown* const source,
    const IID& interface_id,
    void** const result,
    jarvis_tap_xaml_foreign_observation* const observation) noexcept {
    if (result == nullptr || observation == nullptr) {
        return E_POINTER;
    }
    *result = nullptr;
    if (source == nullptr) {
        return E_POINTER;
    }

    HRESULT query_result = E_UNEXPECTED;
    try {
        query_result = source->QueryInterface(interface_id, result);
    }
    catch (...) {
        if (*result != nullptr) {
            observation->foreign_outcome_uncertain = 1U;
            *result = nullptr;
        }
        return E_UNEXPECTED;
    }
    if (FAILED(query_result) && *result != nullptr) {
        observation->foreign_outcome_uncertain = 1U;
        *result = nullptr;
        return E_UNEXPECTED;
    }
    if (SUCCEEDED(query_result) && *result == nullptr) {
        return E_UNEXPECTED;
    }
    return query_result;
}

template <typename Interface>
void ReleaseConfirmed(
    Interface*& value,
    jarvis_tap_xaml_foreign_observation* const observation) noexcept {
    if (value == nullptr || observation == nullptr) {
        return;
    }
    ++observation->release_attempt_count;
    try {
        static_cast<void>(value->Release());
        ++observation->release_completed_count;
    }
    catch (...) {
        observation->foreign_outcome_uncertain = 1U;
    }
    value = nullptr;
}

void FreeSourceInfo(SourceInfo& value) noexcept {
    SysFreeString(value.FileName);
    SysFreeString(value.Hash);
    value.FileName = nullptr;
    value.Hash = nullptr;
}

void FreePropertyChainSource(
    PropertyChainSource& value) noexcept {
    SysFreeString(value.TargetType);
    SysFreeString(value.Name);
    FreeSourceInfo(value.SrcInfo);
    value.TargetType = nullptr;
    value.Name = nullptr;
}

void FreePropertyChainValue(
    PropertyChainValue& value) noexcept {
    SysFreeString(value.Type);
    SysFreeString(value.DeclaringType);
    SysFreeString(value.ValueType);
    SysFreeString(value.ItemType);
    SysFreeString(value.Value);
    SysFreeString(value.PropertyName);
    value.Type = nullptr;
    value.DeclaringType = nullptr;
    value.ValueType = nullptr;
    value.ItemType = nullptr;
    value.Value = nullptr;
    value.PropertyName = nullptr;
}

void FreeConfirmedPropertyChain(
    PropertyChainSource*& sources,
    const std::uint32_t source_count,
    PropertyChainValue*& values,
    const std::uint32_t value_count,
    jarvis_tap_xaml_foreign_observation* const observation) noexcept {
    if (observation == nullptr) {
        return;
    }
    observation->property_chain_free_required = 1U;
    const bool source_shape_valid =
        source_count == 0U || sources != nullptr;
    const bool value_shape_valid =
        value_count == 0U || values != nullptr;
    if (!source_shape_valid ||
        !value_shape_valid ||
        source_count >
            JARVIS_TAP_XAML_READ_MAX_PROPERTY_SOURCE_COUNT ||
        value_count >
            JARVIS_TAP_XAML_READ_MAX_PROPERTY_VALUE_COUNT) {
        observation->foreign_outcome_uncertain = 1U;
        return;
    }

    for (std::uint32_t index = 0U;
         index < source_count;
         ++index) {
        FreePropertyChainSource(sources[index]);
    }
    for (std::uint32_t index = 0U;
         index < value_count;
         ++index) {
        FreePropertyChainValue(values[index]);
    }
    CoTaskMemFree(sources);
    CoTaskMemFree(values);
    sources = nullptr;
    values = nullptr;
    observation->property_chain_freed = 1U;
}

[[nodiscard]] bool ExactRuntimeClassName(
    IInspectable* const inspectable,
    jarvis_tap_xaml_foreign_observation* const observation) noexcept {
    if (inspectable == nullptr || observation == nullptr) {
        return false;
    }
    HSTRING runtime_class_name = nullptr;
    HRESULT name_result = E_UNEXPECTED;
    try {
        name_result =
            inspectable->GetRuntimeClassName(&runtime_class_name);
    }
    catch (...) {
        if (runtime_class_name != nullptr) {
            observation->foreign_outcome_uncertain = 1U;
        }
        return false;
    }
    if (FAILED(name_result) || runtime_class_name == nullptr) {
        if (runtime_class_name != nullptr) {
            observation->foreign_outcome_uncertain = 1U;
            runtime_class_name = nullptr;
        }
        return false;
    }

    UINT32 length = 0U;
    const wchar_t* const raw_name =
        WindowsGetStringRawBuffer(runtime_class_name, &length);
    constexpr auto expected_length =
        static_cast<UINT32>(
            (sizeof(kSolidColorBrushRuntimeClass) / sizeof(wchar_t)) -
            1U);
    const bool matched =
        raw_name != nullptr &&
        length == expected_length &&
        std::wmemcmp(
            raw_name,
            kSolidColorBrushRuntimeClass,
            expected_length) == 0;
    WindowsDeleteString(runtime_class_name);
    return matched;
}

[[nodiscard]] bool ReadBrush(
    IInspectable* const inspectable,
    jarvis_tap_xaml_foreign_observation* const observation,
    SolidColorBrushAbi** const solid_owner,
    BrushAbi** const brush_owner) noexcept {
    if (inspectable == nullptr ||
        observation == nullptr ||
        solid_owner == nullptr ||
        brush_owner == nullptr) {
        return false;
    }
    *solid_owner = nullptr;
    *brush_owner = nullptr;

    const auto solid_guid =
        winrt::guid_of<
            winrt::Windows::UI::Xaml::Media::ISolidColorBrush>();
    const auto brush_guid =
        winrt::guid_of<winrt::Windows::UI::Xaml::Media::IBrush>();
    if (FAILED(SafeQueryInterface(
            inspectable,
            reinterpret_cast<const IID&>(solid_guid),
            reinterpret_cast<void**>(solid_owner),
            observation)) ||
        FAILED(SafeQueryInterface(
            inspectable,
            reinterpret_cast<const IID&>(brush_guid),
            reinterpret_cast<void**>(brush_owner),
            observation))) {
        return false;
    }

    winrt::Windows::UI::Color color{};
    double opacity = 0.0;
    HRESULT color_result = E_UNEXPECTED;
    HRESULT opacity_result = E_UNEXPECTED;
    try {
        color_result = (*solid_owner)->get_Color(
            winrt::put_abi(color));
        opacity_result = (*brush_owner)->get_Opacity(&opacity);
    }
    catch (...) {
        observation->foreign_outcome_uncertain = 1U;
        return false;
    }
    if (FAILED(color_result) ||
        FAILED(opacity_result) ||
        !std::isfinite(opacity) ||
        opacity < 0.0 ||
        opacity > 1.0) {
        return false;
    }

    observation->argb =
        (static_cast<std::uint32_t>(color.A) << 24U) |
        (static_cast<std::uint32_t>(color.R) << 16U) |
        (static_cast<std::uint32_t>(color.G) << 8U) |
        static_cast<std::uint32_t>(color.B);
    observation->opacity_millionths =
        static_cast<std::uint32_t>(
            std::floor(
                opacity *
                    static_cast<double>(
                        JARVIS_TAP_OPACITY_MILLIONTHS_MAX) +
                0.5));
    observation->brush_read_succeeded = 1U;
    return true;
}

[[nodiscard]] jarvis_tap_xaml_foreign_observation
CollectForeignObservation(
    IUnknown* const site,
    const jarvis_tap_xaml_read_request& request) noexcept {
    jarvis_tap_xaml_foreign_observation observation{};
    observation.size =
        sizeof(jarvis_tap_xaml_foreign_observation);
    observation.abi_version =
        JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
    IXamlDiagnostics* diagnostics = nullptr;
    IVisualTreeService2* service = nullptr;
    PropertyChainSource* property_sources = nullptr;
    PropertyChainValue* property_values = nullptr;
    IInspectable* inspectable = nullptr;
    SolidColorBrushAbi* solid_brush = nullptr;
    BrushAbi* brush = nullptr;

    do {
        if (FAILED(SafeQueryInterface(
                site,
                IID_IXamlDiagnostics,
                reinterpret_cast<void**>(&diagnostics),
                &observation))) {
            break;
        }
        observation.site_query_succeeded = 1U;

        if (FAILED(SafeQueryInterface(
                diagnostics,
                IID_IVisualTreeService2,
                reinterpret_cast<void**>(&service),
                &observation))) {
            break;
        }
        observation.service_query_succeeded = 1U;

        observation.property_chain_call_attempted = 1U;
        unsigned int source_count = 0U;
        unsigned int property_count = 0U;
        HRESULT chain_result = E_UNEXPECTED;
        try {
            chain_result = service->GetPropertyValuesChain(
                request.instance_handle,
                &source_count,
                &property_sources,
                &property_count,
                &property_values);
        }
        catch (...) {
            if (property_sources != nullptr ||
                property_values != nullptr) {
                observation.foreign_outcome_uncertain = 1U;
            }
            break;
        }
        observation.property_source_count = source_count;
        observation.property_value_count = property_count;
        if (FAILED(chain_result)) {
            if (property_sources != nullptr ||
                property_values != nullptr) {
                observation.foreign_outcome_uncertain = 1U;
            }
            break;
        }
        observation.property_chain_call_succeeded = 1U;

        const bool arrays_bounded =
            source_count > 0U &&
            source_count <=
                JARVIS_TAP_XAML_READ_MAX_PROPERTY_SOURCE_COUNT &&
            property_count > 0U &&
            property_count <=
                JARVIS_TAP_XAML_READ_MAX_PROPERTY_VALUE_COUNT &&
            property_sources != nullptr &&
            property_values != nullptr;
        if (!arrays_bounded) {
            break;
        }

        PropertyChainValue* matched_property = nullptr;
        const wchar_t* const expected_property_name =
            kPropertyNames[request.property_slot];
        for (std::uint32_t index = 0U;
             index < property_count;
             ++index) {
            if (property_values[index].PropertyName != nullptr &&
                std::wcscmp(
                    property_values[index].PropertyName,
                    expected_property_name) == 0) {
                ++observation.matched_property_count;
                matched_property = &property_values[index];
            }
        }
        if (observation.matched_property_count != 1U ||
            matched_property == nullptr) {
            break;
        }

        observation.property_chain_index =
            matched_property->PropertyChainIndex;
        observation.property_metadata_bits =
            static_cast<std::uint64_t>(
                matched_property->MetadataBits);
        if (observation.property_chain_index >= source_count) {
            break;
        }
        observation.property_value_source =
            static_cast<std::uint32_t>(
                property_sources[
                    observation.property_chain_index].Source);
        if (observation.property_value_source !=
                static_cast<std::uint32_t>(BaseValueSourceLocal) ||
            (observation.property_metadata_bits &
                ~JARVIS_TAP_XAML_METADATA_KNOWN_MASK) != 0ULL) {
            break;
        }

        if ((observation.property_metadata_bits &
                JARVIS_TAP_XAML_METADATA_IS_VALUE_NULL) != 0ULL) {
            observation.runtime_value_kind =
                JARVIS_TAP_RUNTIME_VALUE_NULL;
            observation.runtime_class =
                JARVIS_TAP_RUNTIME_CLASS_NONE;
            break;
        }

        InstanceHandle property_value_handle = 0ULL;
        HRESULT property_result = E_UNEXPECTED;
        try {
            property_result = service->GetProperty(
                request.instance_handle,
                matched_property->Index,
                &property_value_handle);
        }
        catch (...) {
            if (property_value_handle != 0ULL) {
                observation.foreign_outcome_uncertain = 1U;
            }
            break;
        }
        if (FAILED(property_result)) {
            if (property_value_handle != 0ULL) {
                observation.foreign_outcome_uncertain = 1U;
            }
            break;
        }
        observation.property_handle_call_succeeded = 1U;
        if (property_value_handle == 0ULL) {
            break;
        }
        observation.property_value_handle_nonzero = 1U;

        HRESULT inspectable_result = E_UNEXPECTED;
        try {
            inspectable_result =
                diagnostics->GetIInspectableFromHandle(
                    property_value_handle,
                    &inspectable);
        }
        catch (...) {
            if (inspectable != nullptr) {
                observation.foreign_outcome_uncertain = 1U;
                inspectable = nullptr;
            }
            break;
        }
        if (FAILED(inspectable_result) || inspectable == nullptr) {
            if (inspectable != nullptr) {
                observation.foreign_outcome_uncertain = 1U;
                inspectable = nullptr;
            }
            break;
        }
        observation.inspectable_call_succeeded = 1U;
        observation.runtime_value_kind =
            JARVIS_TAP_RUNTIME_VALUE_OBJECT;
        observation.exact_runtime_class_name_matched =
            ExactRuntimeClassName(inspectable, &observation) ? 1U : 0U;
        if (observation.exact_runtime_class_name_matched != 1U) {
            break;
        }
        observation.runtime_class =
            JARVIS_TAP_RUNTIME_CLASS_SOLID_COLOR_BRUSH;
        static_cast<void>(ReadBrush(
            inspectable,
            &observation,
            &solid_brush,
            &brush));
    } while (false);

    if (observation.property_chain_call_succeeded == 1U) {
        FreeConfirmedPropertyChain(
            property_sources,
            observation.property_source_count,
            property_values,
            observation.property_value_count,
            &observation);
    }
    else if (property_sources != nullptr ||
             property_values != nullptr) {
        observation.foreign_outcome_uncertain = 1U;
    }
    ReleaseConfirmed(brush, &observation);
    ReleaseConfirmed(solid_brush, &observation);
    ReleaseConfirmed(inspectable, &observation);
    ReleaseConfirmed(service, &observation);
    ReleaseConfirmed(diagnostics, &observation);
    return observation;
}

}  // namespace

#endif

jarvis_tap_xaml_read_response
jarvis_tap_windows_xaml_read_bridge_read(
    IUnknown* const site,
    const jarvis_tap_admission_instance* const admission,
    const jarvis_tap_xaml_read_request* const request) noexcept {
#if JARVIS_COMPILE_REVIEWED_XAML_READ_BRIDGE == 0
    static_cast<void>(site);
    static_cast<void>(admission);
    static_cast<void>(request);
    return jarvis_tap_xaml_read_bridge_query_contract();
#else
    const auto preflight =
        jarvis_tap_xaml_read_bridge_preflight(
            admission,
            request,
            GetTickCount64());
    if (preflight.result !=
        JARVIS_TAP_XAML_READ_RESULT_PREFLIGHT_ACCEPTED) {
        return preflight;
    }
    const auto target_result =
        jarvis_tap_verify_exact_target(
            admission == nullptr ? nullptr : &admission->bind,
            1U);
    const auto target_acceptance =
        jarvis_tap_xaml_read_bridge_accept_target(
            &preflight,
            target_result);
    if (target_acceptance.result !=
        JARVIS_TAP_XAML_READ_RESULT_TARGET_ACCEPTED) {
        return target_acceptance;
    }
    if (site == nullptr || request == nullptr) {
        jarvis_tap_xaml_foreign_observation observation{};
        observation.size =
            sizeof(jarvis_tap_xaml_foreign_observation);
        observation.abi_version =
            JARVIS_EXPLORER_TRANSPORT_ABI_VERSION;
        return jarvis_tap_xaml_read_bridge_complete(
            admission,
            request,
            &target_acceptance,
            &observation,
            0U);
    }

    const auto observation =
        CollectForeignObservation(site, *request);
    return jarvis_tap_xaml_read_bridge_complete(
        admission,
        request,
        &target_acceptance,
        &observation,
        1U);
#endif
}
