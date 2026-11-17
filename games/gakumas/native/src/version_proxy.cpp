#include "version_proxy.hpp"
#include "runtime_bootstrap.hpp"
#include <array>
#include <string>

namespace {
INIT_ONCE initialized=INIT_ONCE_STATIC_INIT;
std::array<FARPROC,17> functions{};
constexpr const char* names[]{"GetFileVersionInfoA","GetFileVersionInfoByHandle",
    "GetFileVersionInfoExA","GetFileVersionInfoExW","GetFileVersionInfoSizeA",
    "GetFileVersionInfoSizeExA","GetFileVersionInfoSizeExW","GetFileVersionInfoSizeW",
    "GetFileVersionInfoW","VerFindFileA","VerFindFileW","VerInstallFileA",
    "VerInstallFileW","VerLanguageNameA","VerLanguageNameW","VerQueryValueA","VerQueryValueW"};
BOOL CALLBACK LoadOriginal(PINIT_ONCE,void*,void**) {
    wchar_t directory[MAX_PATH+1]{};
    const UINT length=GetSystemDirectoryW(directory,MAX_PATH);
    if(!length||length>=MAX_PATH)return FALSE;
    const auto path=std::wstring(directory)+L"\\version.dll";
    const auto module=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!module)return FALSE;
    for(std::size_t i=0;i<functions.size();i++)functions[i]=GetProcAddress(module,names[i]);
    return TRUE;
}
}
extern "C" void* GakumasResolveVersionExport(unsigned int index) {
    // Unity calls version APIs before initializing its scripting runtime. Also
    // arm here in case the bootstrap worker ran before UnityPlayer was visible.
    gakumas::ArmRuntimeBootstrap();
    if(index>=functions.size()||!InitOnceExecuteOnce(&initialized,LoadOriginal,nullptr,nullptr)||!functions[index]) {
        RaiseException(ERROR_PROC_NOT_FOUND,EXCEPTION_NONCONTINUABLE,0,nullptr);return nullptr;
    }
    return reinterpret_cast<void*>(functions[index]);
}
