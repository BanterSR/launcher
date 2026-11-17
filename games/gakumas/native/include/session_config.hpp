#pragma once
#include <Windows.h>
#include <cstdint>

namespace gakumas {
constexpr std::uint32_t kSessionVersion=9;
constexpr wchar_t kContextEnvironment[]=L"GAKUMAS_LAUNCH_CONTEXT";
struct SessionConfig {
    wchar_t sdkOrigin[256];
    wchar_t gameOrigin[256];
    wchar_t resourceOrigin[256];
    wchar_t refreshToken[4096];
    wchar_t platformToken[4096];
    wchar_t userId[128];
    wchar_t platformId[128];
    std::uint32_t showConsole;
    wchar_t logDirectory[1024];
    wchar_t playerLogPath[1024];
    std::uint32_t localization;
    wchar_t localizationDirectory[1024];
    wchar_t localizationFont[1024];
};
}
