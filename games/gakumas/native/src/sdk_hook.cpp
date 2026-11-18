#include <Windows.h>
#include <Shlwapi.h>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <string_view>
#include <stdexcept>
#include "MinHook.h"
#include "il2cpp_api.hpp"
#include "session_config.hpp"
#include "local_session.hpp"
#include "diagnostics.hpp"
#include "localization.hpp"
#include "runtime_bootstrap.hpp"

namespace {
HMODULE self_module{};
unity::Api api{};
gakumas::SessionConfig local_session{};
std::wstring sdk_origin;
std::wstring resource_origin;
std::once_flag native_setup;
std::atomic<bool> native_ready{};
std::atomic<bool> install_started{};
std::atomic<bool> runtime_entered{};
using SetUrl = std::int32_t (*)(unity::Object*,unity::String*,const unity::MethodInfo*);
SetUrl original_set_url{};
using WindowInitialize = void (*)(unity::Object*,const unity::MethodInfo*);
using WindowProcedure = LRESULT (*)(HWND,UINT,WPARAM,LPARAM,const unity::MethodInfo*);
using ApplyOrientation = void (*)(std::int32_t,const unity::MethodInfo*);
using GetGameWindow = std::intptr_t (*)(const unity::MethodInfo*);
WindowInitialize original_window_initialize{};
WindowProcedure original_window_proc{};
ApplyOrientation original_apply_orientation{};
const unity::MethodInfo* get_game_window{};
unity::FieldInfo* previous_window_proc_field{};
unity::FieldInfo* aspect_ratio_field{};
std::atomic<WNDPROC> unity_window_proc{};
// AspectRatioHandler singleton (DontDestroy) captured by Initialize and kept
// alive by a strong GC handle; only touched on Unity's main (window) thread.
unity::Object* aspect_handler{};
void* aspect_handler_root{};

void Log(std::wstring_view message) {
    gakumas::DiagnosticLog(message);
}

template<std::size_t N> bool ReadEnvironment(const wchar_t* name,wchar_t (&value)[N]) {
    const auto length=GetEnvironmentVariableW(name,value,static_cast<DWORD>(N));
    return length>0&&length<N;
}
template<std::size_t N> bool ReadSessionField(const wchar_t* field,wchar_t (&value)[N]) {
    const auto name=std::wstring(gakumas::kContextEnvironment)+L"_"+field;
    return ReadEnvironment(name.c_str(),value);
}

// The launcher normalizes both values to an HTTP(S) origin: scheme, host and
// optional port, without path, query, fragment or user information.
std::wstring RequireOrigin(const wchar_t* value,const char* name) {
    std::wstring origin=value;
    const auto delimiter=origin.find(L"://");
    if(delimiter==std::wstring::npos||(origin.substr(0,delimiter)!=L"http"&&origin.substr(0,delimiter)!=L"https")||
        delimiter+3>=origin.size()||origin.find_first_of(L"/?#@",delimiter+3)!=std::wstring::npos)
        throw std::runtime_error(std::string(name)+" must be an HTTP(S) origin");
    return origin;
}

bool ReadSession() {
    wchar_t version[16]{},console[2]{},localization[2]{},free_resolution[2]{};
    if(!ReadEnvironment(gakumas::kContextEnvironment,version)||version!=std::to_wstring(gakumas::kSessionVersion)||
        !ReadSessionField(L"SHOW_CONSOLE",console)||(console[0]!=L'0'&&console[0]!=L'1'))return false;
    if(!ReadSessionField(L"SDK_ORIGIN",local_session.sdkOrigin)||
        !ReadSessionField(L"GAME_ORIGIN",local_session.gameOrigin)||
        !ReadSessionField(L"REFRESH_TOKEN",local_session.refreshToken)||
        !ReadSessionField(L"PLATFORM_TOKEN",local_session.platformToken)||
        !ReadSessionField(L"USER_ID",local_session.userId)||
        !ReadSessionField(L"PLATFORM_ID",local_session.platformId)||
        !ReadSessionField(L"LOG_DIRECTORY",local_session.logDirectory)||
        !ReadSessionField(L"PLAYER_LOG_PATH",local_session.playerLogPath))return false;
    local_session.showConsole=console[0]==L'1'?1u:0u;
    if(!ReadSessionField(L"LOCALIZATION",localization)||(localization[0]!=L'0'&&localization[0]!=L'1'))return false;
    local_session.localization=localization[0]==L'1'?1u:0u;
    if(local_session.localization) {
        if(!ReadSessionField(L"LOCALIZATION_DIRECTORY",local_session.localizationDirectory)||
            !ReadSessionField(L"LOCALIZATION_FONT",local_session.localizationFont))return false;
    }
    if(!ReadSessionField(L"FREE_RESOLUTION",free_resolution)||(free_resolution[0]!=L'0'&&free_resolution[0]!=L'1'))return false;
    local_session.freeResolution=free_resolution[0]==L'1'?1u:0u;
    sdk_origin=RequireOrigin(local_session.sdkOrigin,"SDK origin");
    // Without RESOURCE_ORIGIN the game keeps the official asset hosts.
    if(ReadSessionField(L"RESOURCE_ORIGIN",local_session.resourceOrigin))
        resource_origin=RequireOrigin(local_session.resourceOrigin,"Resource server origin");
    return true;
}

bool EqualHost(std::wstring_view left,std::wstring_view right) {
    return left.size()==right.size() && CompareStringOrdinal(left.data(),static_cast<int>(left.size()),
        right.data(),static_cast<int>(right.size()),TRUE)==CSTR_EQUAL;
}

bool HostWithin(std::wstring_view host,std::wstring_view domain) {
    if(EqualHost(host,domain))return true;
    return host.size()>domain.size()&&host[host.size()-domain.size()-1]==L'.'&&EqualHost(host.substr(host.size()-domain.size()),domain);
}

// ReciboPath reports the DMM GAMES payment (Recibo) API paths of the client's
// DMM SDK (Dmm.Games.Sdk.Recibo.Api); its hosts are composed at runtime from
// the SDK settings, so they are matched as DMM hosts plus these paths.
bool ReciboPath(std::wstring_view suffix) {
    const auto end=suffix.find_first_of(L"?#");
    const auto path=end==std::wstring_view::npos?suffix:suffix.substr(0,end);
    for(const std::wstring_view prefix:{L"/v1/orders",L"/v1/batch/skus",L"/v1/receipts",L"/v1/pc/users/point-balance",
        L"/v2/pc/sdk/initialize",L"/v2/products/search",L"/v2/orders",L"/v2/receipts",L"/v2/credits/has",
        L"/client_access.php",L"/ensure-user/onetime-token/user-info"}) {
        if(path.starts_with(prefix)&&(path.size()==prefix.size()||path[prefix.size()]==L'/'))return true;
    }
    return false;
}

std::wstring Redirect(std::wstring_view url) {
    const auto scheme_end=url.find(L"://");if(scheme_end==std::wstring_view::npos)return {};
    const auto scheme=url.substr(0,scheme_end);
    if(!EqualHost(scheme,L"https")&&!EqualHost(scheme,L"http"))return {};
    const auto authority_start=scheme_end+3;
    const auto authority_end=url.find_first_of(L"/?#",authority_start);
    auto authority=url.substr(authority_start,authority_end==std::wstring_view::npos?url.size()-authority_start:authority_end-authority_start);
    if(authority.ends_with(L":443"))authority.remove_suffix(4);
    const wchar_t* prefix=nullptr;
    auto origin=std::wstring_view(sdk_origin);
    if(EqualHost(authority,L"identitytoolkit.googleapis.com"))prefix=L"/identitytoolkit";
    else if(EqualHost(authority,L"securetoken.googleapis.com"))prefix=L"/securetoken";
    else if(EqualHost(authority,L"firebasestorage.googleapis.com"))prefix=L"/firebasestorage";
    else if(EqualHost(authority,L"api.asset.game-gakuen-idolmaster.jp")){prefix=L"";origin=resource_origin;}
    else if(EqualHost(authority,L"object.asset.game-gakuen-idolmaster.jp")){prefix=L"/object";origin=resource_origin;}
    else if(EqualHost(authority,L"stat.game-gakuen-idolmaster.jp")){prefix=L"";origin=resource_origin;}
    else if(EqualHost(authority,L"legal.bandainamcoent.co.jp")){prefix=L"/legal";origin=resource_origin;}
    auto suffix=authority_end==std::wstring_view::npos?std::wstring_view(L"/"):url.substr(authority_end);
    if(!prefix&&(HostWithin(authority,L"dmm.com")||HostWithin(authority,L"dmm.co.jp"))&&ReciboPath(suffix))prefix=L"/recibo";
    if(!prefix||origin.empty())return {};
    if(!suffix.starts_with(L"/"))return std::wstring(origin)+prefix+L"/"+std::wstring(suffix);
    return std::wstring(origin)+prefix+std::wstring(suffix);
}

std::int32_t HookSetUrl(unity::Object* request,unity::String* url,const unity::MethodInfo* method) {
    if(!url)return original_set_url(request,url,method);
    void* handle{};
    unity::String* replacement{};
    try {
        const auto length=api.string_length(url);
        if(length<=0||length>65536)return original_set_url(request,url,method);
        const auto chars=api.string_chars(url);
        const std::wstring source(chars,static_cast<std::size_t>(length));
        const auto destination=Redirect(source);
        Log(L"[URL UnityWebRequest] "+source);
        if(destination.empty()){const auto result=original_set_url(request,url,method);
            Log(L"[URL UnityWebRequest result] "+std::to_wstring(result)+L" "+source);return result;}
        replacement=api.string_new_utf16(destination.data(),static_cast<std::int32_t>(destination.size()));
        if(!replacement){Log(L"Local URL allocation failed; request refused");return 9;}
        handle=api.gchandle_new(reinterpret_cast<unity::Object*>(replacement),false);
        if(!handle){Log(L"Local URL rooting failed; request refused");return 9;}
        Log(L"[URL Local redirect] "+source+L" -> "+destination);
    } catch(...) {if(handle)api.gchandle_free(handle);Log(L"Local URL resolution failed; request refused");return 9;}
    const auto result=original_set_url(request,replacement,method);
    try{Log(L"[URL Local redirect result] "+std::to_wstring(result));}catch(...){}
    api.gchandle_free(handle);
    return result;
}

const unity::MethodInfo* FindUrlMethod(unity::Domain* domain) {
    std::size_t count{};const auto assemblies=api.domain_get_assemblies(domain,&count);
    for(std::size_t i=0;i<count;i++) {
        const auto image=api.assembly_get_image(assemblies[i]);
        const auto name=api.image_get_name(image);
        if(!name||std::strcmp(name,"UnityEngine.UnityWebRequestModule.dll")!=0)continue;
        const auto klass=api.class_from_name(image,"UnityEngine.Networking","UnityWebRequest");
        if(!klass)return nullptr;
        const auto method=api.class_get_method_from_name(klass,"SetUrl",1);
        if(method&&api.method_get_param_count(method)==1)return method;
    }
    return nullptr;
}

bool IsWindowedStyle(DWORD style) {
    return (style&WS_CAPTION)==WS_CAPTION&&(style&WS_POPUP)==0;
}

bool EnableWindowResize(HWND window) {
    if(!window)return false;
    const auto style=static_cast<DWORD>(GetWindowLongPtrW(window,GWL_STYLE));
    if(!IsWindowedStyle(style))return false;
    constexpr DWORD resize_style=WS_THICKFRAME|WS_MAXIMIZEBOX;
    if((style&resize_style)==resize_style)return true;
    SetLastError(ERROR_SUCCESS);
    if(!SetWindowLongPtrW(window,GWL_STYLE,static_cast<LONG_PTR>(style|resize_style))) {
        const auto error=GetLastError();
        if(error!=ERROR_SUCCESS) {
            Log(L"[Window] Could not enable resizing; Win32 error="+std::to_wstring(error));
            return false;
        }
    }
    if(!SetWindowPos(window,nullptr,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED)) {
        Log(L"[Window] Could not refresh the resizable frame; Win32 error="+std::to_wstring(GetLastError()));
        return false;
    }
    Log(L"[Window] Resizable frame and maximize button enabled");
    return true;
}

// AspectRatioHandler.WindowProc keeps the windowed game at its orientation:
// a WM_WINDOWPOSCHANGING whose window width/height differs from AspectRatio
// (9/16 portrait, 16/9 landscape) by more than 0.2 is replaced with the
// preset size of WindowSizeLocalSaveData. The portrait/landscape switch
// depends on that range: StandaloneScreenOrientationController.ToggleResolution
// swaps Screen.width and Screen.height, and ScreenOrientationManager keeps a
// full-screen black mask, without a timeout, until the swapped size has the
// new orientation. A window outside the range (a maximized portrait window
// on a landscape monitor) swaps into the wrong orientation and stays black.
// Free resizing therefore lets the client handle WM_WINDOWPOSCHANGING and
// keeps dragging and maximizing inside the same range.
constexpr double kAspectTolerance=0.2;
// Margin for whole-pixel rounding, so the client never snaps a dragged size.
constexpr double kAspectBand=kAspectTolerance-0.01;

bool CurrentAspect(double& aspect) {
    if(!aspect_handler||!aspect_ratio_field)return false;
    float value{};
    api.field_get_value(aspect_handler,aspect_ratio_field,&value);
    if(!(value>0.0f))return false;
    aspect=value;
    return true;
}

HWND GameWindow() {
    return reinterpret_cast<HWND>(reinterpret_cast<GetGameWindow>(get_game_window->methodPointer)(get_game_window));
}

// WM_SIZING: moves the edge being dragged so the window stays in the range
// (the height for the top and bottom edges, the width otherwise).
void ClampSizing(WPARAM edge,RECT& rect,double aspect) {
    const auto width=rect.right-rect.left,height=rect.bottom-rect.top;
    if(width<=0||height<=0)return;
    const auto low=aspect-kAspectBand,high=aspect+kAspectBand;
    const auto ratio=static_cast<double>(width)/height;
    if(ratio>=low&&ratio<=high)return;
    if(edge==WMSZ_TOP||edge==WMSZ_BOTTOM) {
        const auto fitted=static_cast<LONG>(ratio<low?std::floor(width/low):std::ceil(width/high));
        if(edge==WMSZ_TOP)rect.top=rect.bottom-fitted;
        else rect.bottom=rect.top+fitted;
        return;
    }
    const auto fitted=static_cast<LONG>(ratio<low?std::ceil(height*low):std::floor(height*high));
    if(edge==WMSZ_LEFT||edge==WMSZ_TOPLEFT||edge==WMSZ_BOTTOMLEFT)rect.left=rect.right-fitted;
    else rect.right=rect.left+fitted;
}

// WM_GETMINMAXINFO: when the monitor work area is outside the range (portrait
// on a landscape monitor, or the reverse), the maximized window is the largest
// one whose client area has the game's aspect ratio, centred in the work area.
void FitMaximized(HWND window,MINMAXINFO& limits,double aspect) {
    if(limits.ptMaxSize.x<=0||limits.ptMaxSize.y<=0)return;
    const auto ratio=static_cast<double>(limits.ptMaxSize.x)/limits.ptMaxSize.y;
    if(ratio>=aspect-kAspectBand&&ratio<=aspect+kAspectBand)return;
    MONITORINFO monitor{};
    monitor.cbSize=sizeof(monitor);
    if(!GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),&monitor))return;
    RECT frame{};
    const auto style=static_cast<DWORD>(GetWindowLongPtrW(window,GWL_STYLE));
    const auto extended=static_cast<DWORD>(GetWindowLongPtrW(window,GWL_EXSTYLE));
    if(!AdjustWindowRectExForDpi(&frame,style,FALSE,extended,GetDpiForWindow(window)))return;
    const auto frame_width=frame.right-frame.left,frame_height=frame.bottom-frame.top;
    const auto work_width=monitor.rcWork.right-monitor.rcWork.left;
    const auto work_height=monitor.rcWork.bottom-monitor.rcWork.top;
    auto client_height=work_height-frame_height;
    auto client_width=static_cast<LONG>(std::lround(client_height*aspect));
    if(client_width>work_width-frame_width) {
        client_width=work_width-frame_width;
        client_height=static_cast<LONG>(std::lround(client_width/aspect));
    }
    if(client_width<=0||client_height<=0)return;
    const auto width=client_width+frame_width,height=client_height+frame_height;
    limits.ptMaxSize={width,height};
    limits.ptMaxPosition={monitor.rcWork.left-monitor.rcMonitor.left+(work_width-width)/2,
        monitor.rcWork.top-monitor.rcMonitor.top+(work_height-height)/2};
}

LRESULT HookWindowProc(HWND window,UINT message,WPARAM wparam,LPARAM lparam,const unity::MethodInfo* method) {
    const auto unity_proc=unity_window_proc.load(std::memory_order_acquire);
    if(unity_proc) {
        switch(message) {
        case WM_STYLECHANGING:
            // The client removes WS_MAXIMIZEBOX. When the incoming style is
            // windowed (also when leaving fullscreen), keep the resizable frame
            // and the maximize button and let Unity apply the style.
            if(static_cast<LONG_PTR>(wparam)==GWL_STYLE&&lparam) {
                const auto change=reinterpret_cast<STYLESTRUCT*>(lparam);
                if(IsWindowedStyle(change->styleNew)) {
                    change->styleNew|=WS_THICKFRAME|WS_MAXIMIZEBOX;
                    return CallWindowProcW(unity_proc,window,message,wparam,lparam);
                }
            }
            break;
        case WM_SIZING:
            // The client swallows WM_SIZING; Unity sees the drag and the
            // rectangle stays inside the client's range.
            if(lparam&&IsWindowedStyle(static_cast<DWORD>(GetWindowLongPtrW(window,GWL_STYLE)))) {
                CallWindowProcW(unity_proc,window,message,wparam,lparam);
                double aspect{};
                if(CurrentAspect(aspect))ClampSizing(wparam,*reinterpret_cast<RECT*>(lparam),aspect);
                return TRUE;
            }
            break;
        case WM_GETMINMAXINFO: {
            const auto result=original_window_proc(window,message,wparam,lparam,method);
            double aspect{};
            if(lparam&&IsWindowedStyle(static_cast<DWORD>(GetWindowLongPtrW(window,GWL_STYLE)))&&CurrentAspect(aspect))
                FitMaximized(window,*reinterpret_cast<MINMAXINFO*>(lparam),aspect);
            return result;
        }
        default:
            break;
        }
    }
    return original_window_proc(window,message,wparam,lparam,method);
}

void HookWindowInitialize(unity::Object* self,const unity::MethodInfo* method) {
    original_window_initialize(self,method);
    if(self!=aspect_handler) {
        const auto root=api.gchandle_new(self,false);
        if(!root) {
            Log(L"[Window] Could not keep AspectRatioHandler; free resizing is unavailable");
            return;
        }
        if(aspect_handler_root)api.gchandle_free(aspect_handler_root);
        aspect_handler=self;
        aspect_handler_root=root;
    }
    std::intptr_t previous{};
    api.field_get_value(self,previous_window_proc_field,&previous);
    unity_window_proc.store(reinterpret_cast<WNDPROC>(previous),std::memory_order_release);
    if(!previous) {
        Log(L"[Window] AspectRatioHandler has no saved Unity window procedure; resizing is unavailable");
        return;
    }
    const auto window=GameWindow();
    // WindowHandle.Get deliberately returns null while Unity is fullscreen.
    if(!window) {
        Log(L"[Window] Unity window procedure captured; waiting for windowed mode to enable the resizable frame");
        return;
    }
    if(EnableWindowResize(window))
        Log(L"[Window] Free resizing active within the client's portrait/landscape aspect range");
}

// WindowManager.ApplyOrientationSettings runs after each orientation change
// and moves the window to the new orientation's preset size (NudgeWindow and
// the client's WM_WINDOWPOSCHANGING). A maximized window is restored first,
// so it leaves the maximized state together with that size.
void HookApplyOrientation(std::int32_t orientation,const unity::MethodInfo* method) {
    const auto window=GameWindow();
    if(window&&IsZoomed(window)) {
        ShowWindow(window,SW_RESTORE);
        Log(L"[Window] Maximized window restored for orientation "+std::to_wstring(orientation));
    }
    original_apply_orientation(orientation,method);
}

void PrepareWindowHooks(unity::Domain* domain) {
    unity::Class* handler{};
    unity::Class* handle{};
    unity::Class* manager{};
    std::size_t count{};
    const auto assemblies=api.domain_get_assemblies(domain,&count);
    for(std::size_t i=0;i<count&&(!handler||!handle||!manager);i++) {
        const auto image=api.assembly_get_image(assemblies[i]);
        if(!handler)handler=api.class_from_name(image,"Campus.Common.StandAloneWindow","AspectRatioHandler");
        if(!handle)handle=api.class_from_name(image,"Campus.Common.StandAloneWindow","WindowHandle");
        if(!manager)manager=api.class_from_name(image,"Campus.Common.StandAloneWindow","WindowManager");
    }
    if(!handler||!handle||!manager)throw std::runtime_error("PC window control classes are unavailable");
    const auto initialize=api.class_get_method_from_name(handler,"Initialize",0);
    const auto procedure=api.class_get_method_from_name(handler,"WindowProc",4);
    const auto orientation=api.class_get_method_from_name(manager,"ApplyOrientationSettings",1);
    get_game_window=api.class_get_method_from_name(handle,"Get",0);
    previous_window_proc_field=api.class_get_field_from_name(handler,"_oldWndProcPtr");
    aspect_ratio_field=api.class_get_field_from_name(handler,"<AspectRatio>k__BackingField");
    if(!initialize||!initialize->methodPointer||!procedure||!procedure->methodPointer||
        !orientation||!orientation->methodPointer||!get_game_window||!get_game_window->methodPointer||
        !previous_window_proc_field||!aspect_ratio_field||
        api.type_get_type(api.field_get_type(aspect_ratio_field))!=unity::kTypeSingle||
        api.method_get_param_count(initialize)!=0||api.method_get_param_count(procedure)!=4||
        api.method_get_param_count(orientation)!=1||api.method_get_param_count(get_game_window)!=0)
        throw std::runtime_error("PC window control methods or AspectRatioHandler fields are unavailable");
    auto status=MH_CreateHook(initialize->methodPointer,reinterpret_cast<void*>(&HookWindowInitialize),
        reinterpret_cast<void**>(&original_window_initialize));
    if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
    status=MH_CreateHook(procedure->methodPointer,reinterpret_cast<void*>(&HookWindowProc),
        reinterpret_cast<void**>(&original_window_proc));
    if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
    status=MH_CreateHook(orientation->methodPointer,reinterpret_cast<void*>(&HookApplyOrientation),
        reinterpret_cast<void**>(&original_apply_orientation));
    if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
    Log(L"[Window] Free resize hooks prepared for AspectRatioHandler.Initialize/WindowProc and WindowManager.ApplyOrientationSettings");
}

bool SetupNativeDiagnostics() {
    std::call_once(native_setup,[]{
        if(!ReadSession())return;
        gakumas::InitializeDiagnostics(self_module,local_session);
        gakumas::StartLocalization(local_session);
        native_ready.store(true,std::memory_order_release);
        Log(L"version.dll loaded in Unity process; native-only bootstrap");
    });
    return native_ready.load(std::memory_order_acquire);
}

void RuntimeInitialized(HMODULE module,int result,DWORD runtime_thread) {
    runtime_entered.store(true,std::memory_order_release);
    if(install_started.exchange(true,std::memory_order_acq_rel))return;
    try {
        if(!SetupNativeDiagnostics())return;
        Log(L"[Runtime] il2cpp_init returned "+std::to_wstring(result)+L"; Unity initialization thread="+std::to_wstring(runtime_thread));
        Log(L"Unity initialization returned; installing on its registered thread");
        // All API calls below occur on Unity's initialization thread AFTER its
        // own initialization returned. Never attach/detach Unity-owned threads.
        if(!api.Bind(module))throw std::runtime_error("Required IL2CPP API export is missing");
        if(!api.thread_current())throw std::runtime_error("Unity initialization thread is not registered with IL2CPP; installation refused");
        const auto domain=api.domain_get();
        if(!domain)throw std::runtime_error("IL2CPP domain is unavailable after initialization");
        const auto method=FindUrlMethod(domain);
        if(!method||!method->methodPointer)throw std::runtime_error("UnityWebRequest.SetUrl(string) is unavailable after initialization");
        auto status=MH_Initialize();
        if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
        Log(L"[Runtime] Installing diagnostics and local-session hooks on registered Unity thread");
        gakumas::PrepareDiagnosticsHooks(api,domain);
        gakumas::PrepareLocalSession(api,domain,local_session,Log);
        gakumas::PrepareLocalization(api,domain);
        if(local_session.freeResolution)PrepareWindowHooks(domain);
        else Log(L"[Window] Free resolution disabled by the launcher; the client keeps its preset window sizes");
        status=MH_CreateHook(method->methodPointer,reinterpret_cast<void*>(&HookSetUrl),reinterpret_cast<void**>(&original_set_url));
        if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
        status=MH_EnableHook(MH_ALL_HOOKS);
        if(status!=MH_OK)throw std::runtime_error(MH_StatusToString(status));
    }catch(const std::exception& error) {
        const int size=MultiByteToWideChar(CP_UTF8,0,error.what(),-1,nullptr,0);
        std::wstring detail(size>0?static_cast<std::size_t>(size):1,L'\0');
        if(size>0)MultiByteToWideChar(CP_UTF8,0,error.what(),-1,detail.data(),size);
        MH_Uninitialize();
        Log(L"Hook setup failed: "+detail);
        return;
    }
    SecureZeroMemory(&local_session,sizeof(local_session));
    Log(L"SDK, Recibo redirect and DMM launch hooks installed on Unity initialization thread");
}

DWORD WINAPI Worker(void*) {
    // Win32-only work: no domain_get, assembly enumeration, thread_attach or
    // managed allocation is permitted on this worker.
    const auto deadline=GetTickCount64()+120000;
    while(GetTickCount64()<deadline) {
        if(runtime_entered.load(std::memory_order_acquire))return ERROR_SUCCESS;
        if(GetModuleHandleW(L"UnityPlayer.dll")) {
            if(!SetupNativeDiagnostics())return ERROR_SUCCESS;
            if(gakumas::ArmRuntimeBootstrap()) {
                Log(L"[Bootstrap] Unity initialization callback armed; worker makes no IL2CPP calls");
                return ERROR_SUCCESS;
            }
        }
        Sleep(25);
    }
    if(native_ready.load(std::memory_order_acquire)&&!runtime_entered.load(std::memory_order_acquire))
        Log(L"Could not arm Unity initialization callback");
    return ERROR_SUCCESS;
}
}

BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,void*) {
    if(reason==DLL_PROCESS_ATTACH){self_module=module;
        gakumas::SetRuntimeReadyCallback(RuntimeInitialized);
        gakumas::ArmRuntimeBootstrap();
        HANDLE worker=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);if(worker)CloseHandle(worker);else return FALSE;}
    return TRUE;
}

extern "C" __declspec(dllexport) std::uint32_t GakumasHookVersion() {return gakumas::kSessionVersion;}
