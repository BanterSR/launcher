#pragma once
#include "il2cpp_api.hpp"
#include "session_config.hpp"

namespace gakumas {
// Native-only: copies the localization settings and starts loading the
// translation directory on a background thread. Never calls IL2CPP.
void StartLocalization(const SessionConfig& session);
// Runs on Unity's initialization thread after il2cpp_init. Resolves the text,
// I18n, master data and ADV entry points and creates (does not enable) their
// MinHook hooks. A failure removes only the localization hooks and is logged.
void PrepareLocalization(unity::Api& api,unity::Domain* domain);
}
