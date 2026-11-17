#include "diagnostics.hpp"
#include "MinHook.h"
#include <Windows.h>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

namespace {
std::mutex output_lock;
HANDLE console_output=INVALID_HANDLE_VALUE;
std::wstring folder;
std::wstring player_path;
unity::Api* api{};
unity::Domain* domain{};
std::atomic<unsigned long long> request_sequence{};
using UnityLog=void(*)(std::int32_t,std::int32_t,unity::String*,unity::Object*,const unity::MethodInfo*);
using UnityException=void(*)(unity::Exception*,unity::Object*,const unity::MethodInfo*);
using HttpSend=unity::Object*(*)(unity::Object*,unity::Object*,std::int32_t,void*,const unity::MethodInfo*);
UnityLog original_log{};
UnityException original_exception{};
HttpSend original_http_send{};
const unity::MethodInfo* request_uri{};
const unity::MethodInfo* request_method{};
const unity::MethodInfo* uri_string{};
const unity::MethodInfo* method_string{};
thread_local bool logging_unity=false;

std::wstring ManagedString(unity::String* value) {
    if(!value)return L"<null>";
    const auto count=api->string_length(value);
    if(count<0||count>4*1024*1024)return L"<invalid string length>";
    return {api->string_chars(value),static_cast<std::size_t>(count)};
}
unity::Class* FindClass(const char* namespaze,const char* name) {
    std::size_t count{};const auto assemblies=api->domain_get_assemblies(domain,&count);
    for(std::size_t i=0;i<count;i++)if(auto type=api->class_from_name(api->assembly_get_image(assemblies[i]),namespaze,name))return type;
    return nullptr;
}
const unity::MethodInfo* FindMethod(unity::Class* type,const char* name,int arguments) {
    return type?api->class_get_method_from_name(type,name,arguments):nullptr;
}
unity::Object* Invoke(const unity::MethodInfo* method,unity::Object* instance) {
    if(!method)return nullptr;
    unity::Exception* exception{};auto result=api->runtime_invoke(method,instance,nullptr,&exception);
    return exception?nullptr:result;
}
std::wstring Utf8(const char* value,int size) {
    const auto count=MultiByteToWideChar(CP_UTF8,0,value,size,nullptr,0);
    if(count<=0)return {};
    std::wstring result(static_cast<std::size_t>(count),L'\0');
    MultiByteToWideChar(CP_UTF8,0,value,size,result.data(),count);return result;
}
void Console(std::wstring_view value) {
    if(console_output==INVALID_HANDLE_VALUE)return;
    DWORD written{};
    const wchar_t* cursor=value.data();std::size_t left=value.size();
    while(left>0) {
        const DWORD count=static_cast<DWORD>(left>16000?16000:left);
        if(!WriteConsoleW(console_output,cursor,count,&written,nullptr)||written==0)break;
        cursor+=written;left-=written;
    }
}
DWORD WINAPI TailPlayerLog(void*) {
    HANDLE file=INVALID_HANDLE_VALUE;std::string pending;
    for(;;) {
        if(file==INVALID_HANDLE_VALUE) {
            file=CreateFileW(player_path.c_str(),GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
                nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE){Sleep(100);continue;}
        }
        std::array<char,16384> buffer{};DWORD count{};
        if(!ReadFile(file,buffer.data(),static_cast<DWORD>(buffer.size()),&count,nullptr)) {
            CloseHandle(file);file=INVALID_HANDLE_VALUE;Sleep(100);continue;
        }
        if(count==0){Sleep(75);continue;}
        pending.append(buffer.data(),count);
        for(auto newline=pending.find('\n');newline!=std::string::npos;newline=pending.find('\n')) {
            auto line=Utf8(pending.data(),static_cast<int>(newline));
            if(!line.empty()&&line.back()==L'\r')line.pop_back();
            {std::lock_guard guard(output_lock);Console(L"[Unity.Player] "+line+L"\r\n");}
            pending.erase(0,newline+1);
        }
        if(pending.size()>1024*1024){std::lock_guard guard(output_lock);Console(L"[Unity.Player] "+Utf8(pending.data(),static_cast<int>(pending.size())));pending.clear();}
    }
}
void CaptureUnityLog(std::int32_t type,std::int32_t options,unity::String* text,unity::Object* context,const unity::MethodInfo* method) {
    if(!logging_unity) {
        logging_unity=true;
        try{gakumas::DiagnosticLog(L"[Unity.Log type="+std::to_wstring(type)+L"] "+ManagedString(text));}catch(...){}
        logging_unity=false;
    }
    original_log(type,options,text,context,method);
}
void CaptureUnityException(unity::Exception* exception,unity::Object* context,const unity::MethodInfo* method) {
    if(!logging_unity) {
        logging_unity=true;
        try {
            std::array<char,32768> text{};std::array<char,65536> stack{};
            if(api->format_exception)api->format_exception(exception,text.data(),static_cast<int>(text.size()));
            if(api->format_stack_trace)api->format_stack_trace(exception,stack.data(),static_cast<int>(stack.size()));
            gakumas::DiagnosticLog(L"[Unity.Exception] "+Utf8(text.data(),static_cast<int>(strnlen_s(text.data(),text.size())))+
                L"\n"+Utf8(stack.data(),static_cast<int>(strnlen_s(stack.data(),stack.size()))));
        }catch(...){}
        logging_unity=false;
    }
    original_exception(exception,context,method);
}
unity::Object* CaptureHttpSend(unity::Object* self,unity::Object* request,std::int32_t completion,void* cancellation,const unity::MethodInfo* method) {
    const auto id=++request_sequence;
    try {
        const auto uri=Invoke(request_uri,request);const auto verb=Invoke(request_method,request);
        if(!uri||!verb){gakumas::DiagnosticLog(L"[URL System.Net.Http] Request URI/method not initialized");return original_http_send(self,request,completion,cancellation,method);}
        const auto url=ManagedString(reinterpret_cast<unity::String*>(Invoke(uri_string,uri)));
        const auto name=ManagedString(reinterpret_cast<unity::String*>(Invoke(method_string,verb)));
        gakumas::DiagnosticLog(L"[URL System.Net.Http #"+std::to_wstring(id)+L"] "+name+L" "+url);
    }catch(...){gakumas::DiagnosticLog(L"[URL System.Net.Http] Could not format request URI");}
    return original_http_send(self,request,completion,cancellation,method);
}
template<typename T> void TryHook(const unity::MethodInfo* method,void* replacement,T& original,const wchar_t* label) {
    if(!method||!method->methodPointer){gakumas::DiagnosticLog(std::wstring(L"[Diagnostics] Method unavailable: ")+label);return;}
    const auto status=MH_CreateHook(method->methodPointer,replacement,reinterpret_cast<void**>(&original));
    gakumas::DiagnosticLog(std::wstring(L"[Diagnostics] ")+label+L": "+(status==MH_OK?L"prepared":L"not installed"));
}
}

void gakumas::InitializeDiagnostics(HMODULE,const SessionConfig& config) {
    folder=config.logDirectory;player_path=config.playerLogPath;CreateDirectoryW(folder.c_str(),nullptr);
    if(config.showConsole) {
        if(GetConsoleWindow()!=nullptr||AllocConsole()) {
            SetConsoleOutputCP(CP_UTF8);SetConsoleCP(CP_UTF8);
            SetConsoleTitleW((L"Gakumas Client / Hook Console - PID "+std::to_wstring(GetCurrentProcessId())).c_str());
            console_output=CreateFileW(L"CONOUT$",GENERIC_WRITE,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_EXISTING,0,nullptr);
            const auto menu=GetSystemMenu(GetConsoleWindow(),FALSE);if(menu)DeleteMenu(menu,SC_CLOSE,MF_BYCOMMAND);
            HANDLE thread=CreateThread(nullptr,0,TailPlayerLog,nullptr,0,nullptr);if(thread)CloseHandle(thread);
        }
    }
    DiagnosticLog(L"[Loader] version.dll startup bootstrap; console="+std::wstring(config.showConsole?L"on":L"off"));
    DiagnosticLog(L"[Loader] Unity Player log: "+player_path);
}
void gakumas::DiagnosticLog(std::wstring_view message) {
    std::lock_guard guard(output_lock);
    SYSTEMTIME now{};GetLocalTime(&now);wchar_t timestamp[80]{};
    swprintf_s(timestamp,L"%04u-%02u-%02u %02u:%02u:%02u.%03u [pid=%lu] ",now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,now.wMilliseconds,GetCurrentProcessId());
    const auto line=std::wstring(timestamp)+std::wstring(message)+L"\r\n";Console(line);
    if(folder.empty())return;
    const auto path=folder+L"\\sdk-hook-"+std::to_wstring(GetCurrentProcessId())+L".log";
    const auto file=CreateFileW(path.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return;
    const auto length=WideCharToMultiByte(CP_UTF8,0,line.data(),static_cast<int>(line.size()),nullptr,0,nullptr,nullptr);
    if(length>0){std::string bytes(static_cast<std::size_t>(length),'\0');WideCharToMultiByte(CP_UTF8,0,line.data(),static_cast<int>(line.size()),bytes.data(),length,nullptr,nullptr);DWORD written{};WriteFile(file,bytes.data(),static_cast<DWORD>(bytes.size()),&written,nullptr);}
    CloseHandle(file);
}
void gakumas::PrepareDiagnosticsHooks(unity::Api& unity_api,unity::Domain* unity_domain) {
    api=&unity_api;domain=unity_domain;
    const auto debug=FindClass("UnityEngine","DebugLogHandler");
    TryHook(FindMethod(debug,"Internal_Log",4),reinterpret_cast<void*>(&CaptureUnityLog),original_log,L"Unity.DebugLogHandler.Internal_Log");
    TryHook(FindMethod(debug,"Internal_LogException",2),reinterpret_cast<void*>(&CaptureUnityException),original_exception,L"Unity.DebugLogHandler.Internal_LogException");
    const auto request=FindClass("System.Net.Http","HttpRequestMessage");
    request_uri=FindMethod(request,"get_RequestUri",0);request_method=FindMethod(request,"get_Method",0);
    uri_string=FindMethod(FindClass("System","Uri"),"get_AbsoluteUri",0);
    method_string=FindMethod(FindClass("System.Net.Http","HttpMethod"),"get_Method",0);
    if(request_uri&&request_method&&uri_string&&method_string)
        TryHook(FindMethod(FindClass("System.Net.Http","HttpClient"),"SendAsyncWorker",3),reinterpret_cast<void*>(&CaptureHttpSend),original_http_send,L"System.Net.Http.HttpClient.SendAsyncWorker");
}
