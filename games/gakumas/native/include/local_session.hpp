#pragma once
#include "il2cpp_api.hpp"
#include "session_config.hpp"
#include <string_view>

namespace gakumas {
using LogFunction=void(*)(std::wstring_view);
void PrepareLocalSession(unity::Api& api,unity::Domain* domain,const SessionConfig& session,LogFunction log);
}
