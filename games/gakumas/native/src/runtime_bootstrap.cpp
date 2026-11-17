#include "runtime_bootstrap.hpp"
#include "session_config.hpp"
#include <atomic>
#include <cstdint>
#include <cstring>

namespace {
using Resolver=FARPROC(WINAPI*)(HMODULE,LPCSTR);
using Initialize=int(*)(const char*);
using InitializeUtf16=int(*)(const wchar_t*);
std::atomic<Resolver> original_resolver{};
std::atomic<Initialize> initialize{};
std::atomic<InitializeUtf16> initialize_utf16{};
std::atomic<gakumas::RuntimeReadyCallback> callback{};
std::atomic<HMODULE> runtime_module{};
std::atomic<bool> armed{};
std::atomic_flag arm_lock=ATOMIC_FLAG_INIT;

int InitializeRuntime(const char* name) {
    const auto original=initialize.load(std::memory_order_acquire);
    const int result=original(name);
    // This callback executes on the same thread, strictly after Unity's own
    // il2cpp_init has returned. No attach/detach and no native-thread polling.
    if(const auto ready=callback.load(std::memory_order_acquire))
        ready(runtime_module.load(std::memory_order_acquire),result,GetCurrentThreadId());
    return result;
}
int InitializeRuntimeUtf16(const wchar_t* name) {
    const auto original=initialize_utf16.load(std::memory_order_acquire);
    const int result=original(name);
    if(const auto ready=callback.load(std::memory_order_acquire))
        ready(runtime_module.load(std::memory_order_acquire),result,GetCurrentThreadId());
    return result;
}
FARPROC WINAPI ResolveUnityExport(HMODULE module,LPCSTR name) {
    const auto original=original_resolver.load(std::memory_order_acquire);
    const auto actual=original(module,name);
    if(!actual||reinterpret_cast<std::uintptr_t>(name)<=0xFFFF)return actual;
    if(module!=GetModuleHandleW(L"GameAssembly.dll"))return actual;
    if(std::strcmp(name,"il2cpp_init")==0) {
        runtime_module.store(module,std::memory_order_release);
        initialize.store(reinterpret_cast<Initialize>(actual),std::memory_order_release);
        return reinterpret_cast<FARPROC>(&InitializeRuntime);
    }
    if(std::strcmp(name,"il2cpp_init_utf16")==0) {
        runtime_module.store(module,std::memory_order_release);
        initialize_utf16.store(reinterpret_cast<InitializeUtf16>(actual),std::memory_order_release);
        return reinterpret_cast<FARPROC>(&InitializeRuntimeUtf16);
    }
    return actual;
}

bool PatchUnityImport(HMODULE module) {
    auto base=reinterpret_cast<unsigned char*>(module);
    const auto dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE)return false;
    const auto nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return false;
    const auto image_size=nt->OptionalHeader.SizeOfImage;
    const auto imports=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if(!imports.VirtualAddress||imports.VirtualAddress>=image_size)return false;
    auto descriptor=reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base+imports.VirtualAddress);
    for(;reinterpret_cast<unsigned char*>(descriptor)+sizeof(*descriptor)<=base+image_size&&descriptor->Name;descriptor++) {
        if(!descriptor->OriginalFirstThunk||!descriptor->FirstThunk)continue;
        if(descriptor->OriginalFirstThunk>=image_size||descriptor->FirstThunk>=image_size)continue;
        auto names=reinterpret_cast<IMAGE_THUNK_DATA64*>(base+descriptor->OriginalFirstThunk);
        auto addresses=reinterpret_cast<IMAGE_THUNK_DATA64*>(base+descriptor->FirstThunk);
        for(;reinterpret_cast<unsigned char*>(names)+sizeof(*names)<=base+image_size&&
              reinterpret_cast<unsigned char*>(addresses)+sizeof(*addresses)<=base+image_size&&names->u1.AddressOfData;names++,addresses++) {
            if(IMAGE_SNAP_BY_ORDINAL64(names->u1.Ordinal)||names->u1.AddressOfData>=image_size)continue;
            const auto import=reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base+names->u1.AddressOfData);
            if(std::strcmp(reinterpret_cast<const char*>(import->Name),"GetProcAddress")!=0)continue;
            auto slot=reinterpret_cast<void* volatile*>(&addresses->u1.Function);
            const auto current=reinterpret_cast<void*>(addresses->u1.Function);
            if(current==reinterpret_cast<void*>(&ResolveUnityExport))return true;
            original_resolver.store(reinterpret_cast<Resolver>(current),std::memory_order_release);
            DWORD old{};if(!VirtualProtect(const_cast<void**>(slot),sizeof(void*),PAGE_READWRITE,&old))return false;
            InterlockedExchangePointer(slot,reinterpret_cast<void*>(&ResolveUnityExport));
            DWORD ignored{};VirtualProtect(const_cast<void**>(slot),sizeof(void*),old,&ignored);
            return true;
        }
    }
    return false;
}
}

void gakumas::SetRuntimeReadyCallback(RuntimeReadyCallback ready) {callback.store(ready,std::memory_order_release);}
bool gakumas::RuntimeBootstrapArmed() {return armed.load(std::memory_order_acquire);}
bool gakumas::ArmRuntimeBootstrap() {
    if(armed.load(std::memory_order_acquire))return true;
    wchar_t context[256]{};
    const auto length=GetEnvironmentVariableW(kContextEnvironment,context,256);
    if(!length||length>=256)return false;
    if(arm_lock.test_and_set(std::memory_order_acquire))return false;
    const auto module=GetModuleHandleW(L"UnityPlayer.dll");
    const bool result=module&&PatchUnityImport(module);
    if(result)armed.store(true,std::memory_order_release);
    arm_lock.clear(std::memory_order_release);return result;
}
