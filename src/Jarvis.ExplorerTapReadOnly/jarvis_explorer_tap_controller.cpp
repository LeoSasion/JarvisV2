#include "jarvis_explorer_tap_fingerprint.h"

#include <iostream>
#include <string_view>

int wmain(const int argument_count, wchar_t** const arguments) {
    const bool describe =
        argument_count == 2 &&
        std::wstring_view(arguments[1]) == L"--describe";

    if (!describe) {
        std::cout
            << "{\"schemaVersion\":1,"
            << "\"receiptType\":\"jarvisv2-readonly-tap-controller\","
            << "\"result\":\"blocked\","
            << "\"error\":\"phase12-describe-only\","
            << "\"liveConnectionCompiled\":false,"
            << "\"executionSupported\":false,"
            << "\"activationPermitted\":false,"
            << "\"liveExplorer\":\"not-run\","
            << "\"mutationPerformed\":false}"
            << '\n';
        return 2;
    }

    std::cout
        << "{\"schemaVersion\":1,"
        << "\"receiptType\":\"jarvisv2-readonly-tap-controller\","
        << "\"result\":\"passed-build-description\","
        << "\"abiVersion\":"
        << JARVIS_EXPLORER_TRANSPORT_ABI_VERSION << ','
        << "\"initializationCharacters\":"
        << JARVIS_TAP_INITIALIZATION_CHARS << ','
        << "\"exactPidRequired\":true,"
        << "\"exactTidRequired\":true,"
        << "\"exactHwndRequired\":true,"
        << "\"existingDiagnosticsConsumerPolicy\":\"reject\","
        << "\"endpointAttemptLimit\":0,"
        << "\"tapDllLoadSupported\":false,"
        << "\"offlineAdmissionModelSupported\":true,"
        << "\"offlineEndpointCandidateLimit\":1,"
        << "\"offlineFingerprintModelSupported\":true,"
        << "\"propertyReadSupported\":false,"
        << "\"liveConnectionCompiled\":false,"
        << "\"executionSupported\":false,"
        << "\"activationPermitted\":false,"
        << "\"liveExplorer\":\"not-run\","
        << "\"mutationPerformed\":false}"
        << '\n';
    return 0;
}
