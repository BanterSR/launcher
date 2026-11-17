#pragma once
#include "il2cpp_api.hpp"
#include "session_config.hpp"
#include <string_view>
namespace gakumas {
void InitializeDiagnostics(HMODULE module,const SessionConfig& config);
void DiagnosticLog(std::wstring_view message);
void PrepareDiagnosticsHooks(unity::Api& api,unity::Domain* domain);
}
