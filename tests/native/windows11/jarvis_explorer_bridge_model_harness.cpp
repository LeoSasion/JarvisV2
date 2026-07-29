#include "jarvis_explorer_bridge_contract.h"

#include <cstdint>
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

[[nodiscard]] jarvis_bridge_init_request ValidRequest() noexcept {
    return jarvis_bridge_init_request{
        .size = sizeof(jarvis_bridge_init_request),
        .abi_version = JARVIS_EXPLORER_BRIDGE_ABI_VERSION,
        .explorer_process_id = 4242U,
        .shell_thread_id = 9001U,
        .session_nonce = 0x4A415256495332ULL,
    };
}

[[nodiscard]] bool IsAlwaysNonLive(
    const jarvis_bridge_response& response) noexcept {
    return response.size == sizeof(jarvis_bridge_response) &&
           response.abi_version == JARVIS_EXPLORER_BRIDGE_ABI_VERSION &&
           response.activation_permitted == 0U &&
           response.mutation_performed == 0U &&
           response.live_explorer_touched == 0U &&
           response.reserved == 0U;
}

}  // namespace

int main() {
    auto response = jarvis_bridge_model_query_contract();
    Check(IsAlwaysNonLive(response) &&
          response.state == JARVIS_BRIDGE_STATE_COLD &&
          response.result == JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED);

    jarvis_bridge_model_instance instance{};
    jarvis_bridge_model_reset(&instance);
    Check(instance.state == JARVIS_BRIDGE_STATE_COLD &&
          instance.initialize_attempt_count == 0U);

    auto request = ValidRequest();
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.state == JARVIS_BRIDGE_STATE_BLOCKED &&
          response.result == JARVIS_BRIDGE_RESULT_EXECUTION_UNSUPPORTED);

    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_ALREADY_INITIALIZED);

    jarvis_bridge_model_reset(&instance);
    request = ValidRequest();
    request.abi_version += 1U;
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_ABI_MISMATCH);

    jarvis_bridge_model_reset(&instance);
    request = ValidRequest();
    request.size -= 1U;
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_REQUEST_SIZE_MISMATCH);

    jarvis_bridge_model_reset(&instance);
    request = ValidRequest();
    request.explorer_process_id = 0U;
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_IDENTITY_INVALID);

    jarvis_bridge_model_reset(&instance);
    request = ValidRequest();
    request.shell_thread_id = 0U;
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_IDENTITY_INVALID);

    jarvis_bridge_model_reset(&instance);
    request = ValidRequest();
    request.session_nonce = 0U;
    response = jarvis_bridge_model_initialize(&instance, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_IDENTITY_INVALID);

    jarvis_bridge_model_reset(&instance);
    response = jarvis_bridge_model_quiesce(&instance);
    Check(IsAlwaysNonLive(response) &&
          response.state == JARVIS_BRIDGE_STATE_QUIESCED &&
          response.result == JARVIS_BRIDGE_RESULT_QUIESCED);

    response = jarvis_bridge_model_quiesce(&instance);
    Check(IsAlwaysNonLive(response) &&
          response.state == JARVIS_BRIDGE_STATE_QUIESCED &&
          response.result == JARVIS_BRIDGE_RESULT_QUIESCED);

    response = jarvis_bridge_model_query(&instance);
    Check(IsAlwaysNonLive(response) &&
          response.state == JARVIS_BRIDGE_STATE_QUIESCED &&
          response.result == JARVIS_BRIDGE_RESULT_QUIESCED);

    response = jarvis_bridge_model_initialize(nullptr, &request);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);

    response = jarvis_bridge_model_initialize(&instance, nullptr);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);

    response = jarvis_bridge_model_quiesce(nullptr);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);

    response = jarvis_bridge_model_query(nullptr);
    Check(IsAlwaysNonLive(response) &&
          response.result == JARVIS_BRIDGE_RESULT_INVALID_ARGUMENT);

    const bool passed = scenario_count == passed_count;
    std::cout
        << "{\"schemaVersion\":1,\"result\":\""
        << (passed ? "passed" : "failed")
        << "\",\"scenarioCount\":" << scenario_count
        << ",\"passedCount\":" << passed_count
        << ",\"executionSupported\":false"
        << ",\"activationPermitted\":false"
        << ",\"liveExplorer\":\"not-run\""
        << ",\"mutationPerformed\":false}"
        << '\n';
    return passed ? 0 : 1;
}
