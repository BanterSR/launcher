#include "local_session.hpp"
#include "MinHook.h"
#include <array>
#include <cstring>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
unity::Api* api{};
unity::Domain* domain{};
gakumas::LogFunction logger{};
std::vector<void*> roots;
unity::String* game_origin{};
unity::String* refresh_token{};
unity::String* platform_token{};
unity::String* platform_id{};
unity::String* platform_signature{};
unity::Class* purchase_manager{};
const unity::MethodInfo* set_dmm_user_info{};
unity::FieldInfo* account_initialized{};
unity::FieldInfo* account_token_cache{};
unity::FieldInfo* saved_refresh_token{};
const unity::MethodInfo* load_refresh_data{};
const unity::MethodInfo* clear_id_cache{};
unity::Class* account_exception{};

// Unity's generated x64 signatures return UniTask<reference-type> by value.
// MSVC supplies the hidden return buffer for this 24-byte struct. No manual
// register or return-buffer patching is necessary.
struct UniTaskReference {
    unity::Object* source{};
    unity::Object* result{};
    std::int16_t token{};
    std::array<std::uint8_t,6> padding{};
};
struct CancellationToken {void* source;};
static_assert(sizeof(UniTaskReference)==24);
static_assert(sizeof(CancellationToken)==8);
using InitializeAccountFunction=UniTaskReference(*)(unity::Object*,CancellationToken,const unity::MethodInfo*);
InitializeAccountFunction original_initialize_account{};

unity::Class* Class(const char* namespaze,const char* name) {
    std::size_t count{};const auto assemblies=api->domain_get_assemblies(domain,&count);
    for(std::size_t i=0;i<count;i++) {
        auto image=api->assembly_get_image(assemblies[i]);
        if(auto type=api->class_from_name(image,namespaze,name)) {
            api->runtime_class_init(type);return type;
        }
    }
    throw std::runtime_error(std::string("IL2CPP class missing: ")+namespaze+"."+name);
}
const unity::MethodInfo* Method(unity::Class* type,const char* name,int count) {
    for(auto current=type;current;current=api->class_get_parent(current)) {
        auto method=api->class_get_method_from_name(current,name,count);
        if(method&&method->methodPointer&&api->method_get_param_count(method)==static_cast<std::uint32_t>(count))return method;
    }
    throw std::runtime_error(std::string("IL2CPP method missing: ")+name);
}
unity::FieldInfo* Field(unity::Class* type,const char* name) {
    for(auto current=type;current;current=api->class_get_parent(current)) {
        if(auto field=api->class_get_field_from_name(current,name))return field;
    }
    throw std::runtime_error(std::string("IL2CPP field missing: ")+name);
}
unity::Object* Keep(unity::Object* value) {
    if(!value)throw std::runtime_error("IL2CPP object allocation failed");
    auto handle=api->gchandle_new(value,true);
    if(!handle)throw std::runtime_error("IL2CPP object root failed");
    roots.push_back(handle);return value;
}
unity::String* String(std::wstring_view value) {
    return reinterpret_cast<unity::String*>(Keep(reinterpret_cast<unity::Object*>(
        api->string_new_utf16(value.data(),static_cast<std::int32_t>(value.size())))));
}
unity::Object* Invoke(const unity::MethodInfo* method,void* self,void** arguments) {
    unity::Exception* exception{};
    auto value=api->runtime_invoke(method,self,arguments,&exception);
    if(exception) {
        char detail[4096]{};
        if(api->format_exception)api->format_exception(exception,detail,static_cast<int>(sizeof(detail)));
        throw std::runtime_error(detail[0]?detail:"IL2CPP invocation raised a managed exception");
    }
    return value;
}
unity::Object* Construct(unity::Class* type,int count,void** arguments) {
    auto result=Keep(api->object_new(type));
    Invoke(Method(type,".ctor",count),result,arguments);return result;
}
void SetObject(unity::Object* self,const char* field,unity::Object* value) {
    auto info=api->class_get_field_from_name(purchase_manager,field);
    if(!info)throw std::runtime_error(std::string("DMM field missing: ")+field);
    api->field_set_value_object(self,info,value);
}
void ApplyPlatformIdentity(unity::Object* self) {
    SetObject(self,"_apiToken",reinterpret_cast<unity::Object*>(platform_token));
    void* args[]{platform_id,platform_signature};Invoke(set_dmm_user_info,nullptr,args);
}
unity::String* GetHost(unity::Object*,const unity::MethodInfo*) {
    try{logger(L"[URL Game API] "+std::wstring(api->string_chars(game_origin),static_cast<std::size_t>(api->string_length(game_origin))));}catch(...){}
    return game_origin;
}
bool ValidDmmArguments(unity::Object* self,const unity::MethodInfo*) {
    try{ApplyPlatformIdentity(self);logger(L"DMM launch check accepted using local launcher identity");return true;}
    catch(...){logger(L"DMM identity initialization failed");return false;}
}
bool IsApiTokenEmpty(unity::Object*,const unity::MethodInfo*) {return false;}
unity::String* GetAccountId(unity::Object*,const unity::MethodInfo*) {return platform_id;}
unity::String* GetAccountSignature(unity::Object*,const unity::MethodInfo*) {return platform_signature;}
[[noreturn]] void RaiseAccountError(const std::string& error) {
    const auto size=MultiByteToWideChar(CP_UTF8,0,error.data(),static_cast<int>(error.size()),nullptr,0);
    std::wstring detail(size>0?static_cast<std::size_t>(size):0,L'\0');
    if(size>0)MultiByteToWideChar(CP_UTF8,0,error.data(),static_cast<int>(error.size()),detail.data(),size);
    logger(L"Local credential initialization failed: "+detail);
    auto message=String(detail);
    void* args[]{message};
    auto exception=Construct(account_exception,1,args);
    api->raise_exception(reinterpret_cast<unity::Exception*>(exception));
    std::terminate();
}
UniTaskReference InitializeAccount(unity::Object* self,CancellationToken cancellation,const unity::MethodInfo* method) {
    bool was_initialized{};
    api->field_get_value(self,account_initialized,&was_initialized);
    // The exported Windows initializer is synchronous: it configures the REST
    // API key and changes _isInitialized before returning its completed UniTask.
    // Preserve that implementation and its actual result.
    auto result=original_initialize_account(self,cancellation,method);
    if(was_initialized)return result;
    bool initialized{};
    api->field_get_value(self,account_initialized,&initialized);
    if(!initialized)return result;
    std::string failure;
    try {
        // LoadOrCreate takes a nullable string label, not a bool. Its closed
        // generic MethodInfo is resolved through RefreshTokenLocalSaveData.
        void* args[]{nullptr};
        auto saved=Invoke(load_refresh_data,nullptr,args);
        if(!saved)throw std::runtime_error("RefreshTokenLocalSaveData.LoadOrCreate returned null");
        unity::Object* cache{};
        api->field_get_value(self,account_token_cache,&cache);
        if(!cache)throw std::runtime_error("AccountManager ID token cache is missing");
        api->field_set_value_object(saved,saved_refresh_token,reinterpret_cast<unity::Object*>(refresh_token));
        Invoke(clear_id_cache,cache,nullptr);
        logger(L"Original Windows account initialized; SDK refresh credential bound. Original token retrieval, refresh and custom-token sign-in remain active.");
    }catch(const std::exception& error) {
        failure=error.what();
    }
    if(!failure.empty()) {
        bool ready=false;
        api->field_set_value(self,account_initialized,&ready);
        RaiseAccountError(failure);
    }
    return result;
}
void Hook(unity::Class* type,const char* method,int parameters,void* replacement,void** original=nullptr) {
    const auto info=Method(type,method,parameters);
    const auto status=MH_CreateHook(info->methodPointer,replacement,original);
    if(status!=MH_OK)throw std::runtime_error(std::string("Cannot hook ")+method+": "+MH_StatusToString(status));
}
void StaticValue(unity::Class* type,const char* name,unity::String* value) {
    void* args[]{value};Invoke(Method(type,name,1),nullptr,args);
}
void StaticBool(unity::Class* type,const char* name,bool value) {
    void* args[]{&value};Invoke(Method(type,name,1),nullptr,args);
}
}

void gakumas::PrepareLocalSession(unity::Api& unity_api,unity::Domain* unity_domain,const SessionConfig& session,LogFunction log) {
    api=&unity_api;domain=unity_domain;logger=log;
    game_origin=String(session.gameOrigin);refresh_token=String(session.refreshToken);platform_token=String(session.platformToken);
    platform_id=String(session.platformId);platform_signature=String(L"local-launcher");
    auto api_type=Class("Campus.Common.Network","Api");
    set_dmm_user_info=Method(api_type,"SetDmmUserInfo",2);
    auto manager=Class("Campus.Common.Account","AccountManager");
    purchase_manager=Class("Campus.Common.DMM","DmmPurchaseManager");
    account_initialized=Field(manager,"_isInitialized");
    account_token_cache=Field(manager,"_idTokenResponseCache");
    auto saved_type=Class("Campus.Common.Account","RefreshTokenLocalSaveData");
    saved_refresh_token=Field(saved_type,"_firebaseRefreshToken");
    load_refresh_data=Method(saved_type,"LoadOrCreate",1);
    clear_id_cache=Method(Class("Campus.Common.Firebase","IdTokenResponseCache"),"Clear",0);
    account_exception=Class("Qua.AccountManagement","AccountManagerException");
    Method(account_exception,".ctor",1);

    // Populate DMM's local platform values via its managed API. The DMM SDK's
    // payment (Recibo) requests carry these as X-DMMGAMES-* headers and reach
    // the local SDK through the URL redirect in sdk_hook.cpp.
    auto dmm_util=Class("Dmm.Games.Sdk","DmmGamesSdkUtil");
    StaticValue(dmm_util,"SetApiToken",platform_token);
    StaticValue(dmm_util,"SetOpenId",String(session.userId));
    StaticValue(dmm_util,"SetAccessToken",platform_token);
    StaticBool(dmm_util,"SetSandbox",false);
    auto dmm=Class("Dmm.Games.Sdk","DmmGamesSdk");
    StaticBool(dmm,"set_IsInitialized",true);

    Hook(api_type,"GetHost",0,reinterpret_cast<void*>(&GetHost));
    Hook(purchase_manager,"IsValidCommandLineArgs",0,reinterpret_cast<void*>(&ValidDmmArguments));
    Hook(purchase_manager,"get_IsApiTokenEmpty",0,reinterpret_cast<void*>(&IsApiTokenEmpty));
    Hook(purchase_manager,"GetAccountId",0,reinterpret_cast<void*>(&GetAccountId));
    Hook(purchase_manager,"GetAccountIdSignature",0,reinterpret_cast<void*>(&GetAccountSignature));
    void* initialize_trampoline{};
    Hook(manager,"_InitializeAsync",1,reinterpret_cast<void*>(&InitializeAccount),&initialize_trampoline);
    original_initialize_account=reinterpret_cast<InitializeAccountFunction>(initialize_trampoline);
    logger(L"SDK refresh credential prepared; Windows account initialization, ID token retrieval and Auth/Login use original client implementations");
}
