#include "localization.hpp"
#include "diagnostics.hpp"
#include "json.hpp"
#include "MinHook.h"
#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstring>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace {
namespace fs=std::filesystem;
using gakumas::json::Kind;
using gakumas::json::Value;

struct WideHash {
    using is_transparent=void;
    std::size_t operator()(std::wstring_view value) const noexcept {return std::hash<std::wstring_view>{}(value);}
};
struct WideEqual {
    using is_transparent=void;
    bool operator()(std::wstring_view left,std::wstring_view right) const noexcept {return left==right;}
};
using TextMap=std::unordered_map<std::wstring,std::wstring,WideHash,WideEqual>;
using TextSet=std::unordered_set<std::wstring_view,WideHash,WideEqual>;

// TMP_Text.TextInputSources: TextInputBox (prefab default) and TextString
// (text setter) are both parsed from m_text by ParseInputText.
constexpr std::int32_t kTextInputBox=0;
constexpr std::int32_t kTextString=3;
constexpr std::size_t kDynamicCacheLimit=4096;
constexpr wchar_t kKeySeparator=L'\x1f';
// TMP_FontAsset.CreateFontAsset(path, faceIndex, samplingPointSize,
// atlasPadding, GlyphRenderMode.SDFAA, atlasWidth, atlasHeight) creates a
// dynamic multi-atlas font asset.
constexpr std::int32_t kFontFace=0;
constexpr std::int32_t kFontSamplingSize=72;
constexpr std::int32_t kFontPadding=7;
constexpr std::int32_t kGlyphRenderModeSdfaa=4165;
constexpr std::int32_t kFontAtlasSize=2048;
constexpr std::int32_t kHideDontUnloadUnusedAsset=32;

struct MasterTable {
    std::wstring name;
    fs::path file;
    Value document;
    bool valid{};
    bool indexed{};
    std::unordered_set<std::wstring> keyPaths;
    std::vector<std::wstring> rootKeys;
    std::unordered_map<std::wstring,std::vector<std::wstring>> arrayKeys;
    std::unordered_map<std::wstring,const Value*,WideHash,WideEqual> rows;
};

struct State {
    bool enabled{};
    fs::path directory;
    std::wstring font;
    TextMap i18n;
    TextMap generic;
    TextMap templates;
    TextSet coreOutputs;
    TextSet masterOutputs;
    std::vector<std::unique_ptr<MasterTable>> masters;
    HANDLE coreReady{};
    HANDLE masterReady{};
};
State state;

unity::Api* api{};
unity::Domain* domain{};

void Log(std::wstring_view message) {
    gakumas::DiagnosticLog(std::wstring(L"[Localize] ")+std::wstring(message));
}
std::wstring Widen(std::string_view text) {
    try{return gakumas::json::Utf8ToWide(text);}catch(...){return {};}
}
std::wstring ErrorText(const std::exception& error) {
    auto text=Widen(error.what());
    return text.empty()?L"unknown error":text;
}

// ---------------------------------------------------------------- loading

std::string ReadBytes(const fs::path& path) {
    std::ifstream input(path,std::ios::binary);
    if(!input)throw std::runtime_error("cannot open file");
    std::string bytes((std::istreambuf_iterator<char>(input)),std::istreambuf_iterator<char>());
    if(input.bad())throw std::runtime_error("cannot read file");
    return bytes;
}
std::size_t LoadTextMap(const fs::path& path,TextMap& target) {
    const auto document=gakumas::json::Parse(ReadBytes(path));
    if(document.kind!=Kind::Object)throw std::runtime_error("top-level value must be an object of strings");
    std::size_t count{};
    for(std::size_t i=0;i<document.names.size();i++) {
        const auto& value=document.items[i];
        if(value.kind!=Kind::String)throw std::runtime_error("every translation value must be a string");
        if(document.names[i].empty()||value.text.empty())continue;
        target.insert_or_assign(document.names[i],value.text);count++;
    }
    return count;
}
bool EndsWith(std::wstring_view text,std::wstring_view suffix) {
    return text.size()>=suffix.size()&&text.substr(text.size()-suffix.size())==suffix;
}
void LoadCore() {
    const auto localization=state.directory/L"localization.json";
    const auto generic=state.directory/L"generic.json";
    const auto generic_folder=state.directory/L"genericTrans";
    std::error_code error;
    if(fs::is_regular_file(localization,error)) {
        try{Log(L"localization.json: "+std::to_wstring(LoadTextMap(localization,state.i18n))+L" I18n keys");}
        catch(const std::exception& failure){Log(L"localization.json rejected: "+ErrorText(failure));}
    }else Log(L"localization.json not present; I18n keys keep original text");
    if(fs::is_regular_file(generic,error)) {
        try{Log(L"generic.json: "+std::to_wstring(LoadTextMap(generic,state.generic))+L" texts");}
        catch(const std::exception& failure){Log(L"generic.json rejected: "+ErrorText(failure));}
    }
    std::size_t files{},texts{},templates{};
    if(fs::is_directory(generic_folder,error)) {
        std::vector<fs::path> paths;
        for(fs::recursive_directory_iterator it(generic_folder,error),end;!error&&it!=end;it.increment(error))
            if(it->is_regular_file(error)&&EndsWith(it->path().filename().wstring(),L".json"))paths.push_back(it->path());
        if(error)Log(L"genericTrans listing failed: "+Widen(error.message()));
        std::sort(paths.begin(),paths.end());
        for(const auto& path:paths) {
            const bool format=EndsWith(path.filename().wstring(),L".fmt.json");
            try{(format?templates:texts)+=LoadTextMap(path,format?state.templates:state.generic);files++;}
            catch(const std::exception& failure){Log(L"genericTrans file rejected: "+path.wstring()+L": "+ErrorText(failure));}
        }
    }
    Log(L"genericTrans: "+std::to_wstring(files)+L" files, "+std::to_wstring(texts)+L" texts, "+std::to_wstring(templates)+L" number templates");
    for(const auto& [key,value]:state.i18n)state.coreOutputs.insert(value);
    for(const auto& [key,value]:state.generic)state.coreOutputs.insert(value);
    for(const auto& [key,value]:state.templates)state.coreOutputs.insert(value);
}
void CollectMasterOutputs(const MasterTable& table,const Value& value,const std::wstring& path) {
    if(value.kind==Kind::Object) {
        for(std::size_t i=0;i<value.names.size();i++) {
            const auto child=path.empty()?value.names[i]:path+L"."+value.names[i];
            if(table.keyPaths.contains(child))continue;
            const auto& item=value.items[i];
            if(item.kind==Kind::String&&!item.text.empty())state.masterOutputs.insert(item.text);
            else CollectMasterOutputs(table,item,child);
        }
    }else if(value.kind==Kind::Array) {
        for(const auto& item:value.items) {
            if(item.kind==Kind::String&&!item.text.empty())state.masterOutputs.insert(item.text);
            else CollectMasterOutputs(table,item,path);
        }
    }
}
void LoadMaster(MasterTable& table) {
    table.document=gakumas::json::Parse(ReadBytes(table.file));
    const auto rules=table.document.Find(L"rules");
    const auto keys=rules?rules->Find(L"primaryKeys"):nullptr;
    const auto data=table.document.Find(L"data");
    if(!keys||keys->kind!=Kind::Array||keys->items.empty())throw std::runtime_error("rules.primaryKeys must be a non-empty array");
    if(!data||data->kind!=Kind::Array)throw std::runtime_error("data must be an array");
    for(const auto& key:keys->items) {
        if(key.kind!=Kind::String||key.text.empty())throw std::runtime_error("rules.primaryKeys must contain field paths");
        table.keyPaths.insert(key.text);
        const auto dot=key.text.rfind(L'.');
        if(dot==std::wstring::npos)table.rootKeys.push_back(key.text);
        else table.arrayKeys[key.text.substr(0,dot)].push_back(key.text.substr(dot+1));
    }
    if(table.rootKeys.empty())throw std::runtime_error("rules.primaryKeys has no top-level field");
    for(const auto& row:data->items)if(row.kind!=Kind::Object)throw std::runtime_error("every data row must be an object");
    CollectMasterOutputs(table,*data,L"");
    table.valid=true;
}
void LoadMasters() {
    std::size_t valid{},rows{};
    for(const auto& table:state.masters) {
        try {
            LoadMaster(*table);valid++;
            rows+=table->document.Find(L"data")->items.size();
        }catch(const std::exception& failure) {
            Log(L"masterTrans/"+table->name+L".json rejected: "+ErrorText(failure));
        }
    }
    Log(L"masterTrans: "+std::to_wstring(valid)+L"/"+std::to_wstring(state.masters.size())+L" tables, "+std::to_wstring(rows)+L" rows");
}
DWORD WINAPI LoadTranslations(void*) {
    try{LoadCore();}catch(const std::exception& failure){Log(L"Text translation loading failed: "+ErrorText(failure));}
    SetEvent(state.coreReady);
    try{LoadMasters();}catch(const std::exception& failure){Log(L"Master translation loading failed: "+ErrorText(failure));}
    SetEvent(state.masterReady);
    return ERROR_SUCCESS;
}
bool MasterReady() {return WaitForSingleObject(state.masterReady,0)==WAIT_OBJECT_0;}

// ---------------------------------------------------------------- managed strings

std::wstring_view View(unity::String* value) {
    if(!value)return {};
    const auto length=api->string_length(value);
    if(length<=0||length>4*1024*1024)return {};
    return {api->string_chars(value),static_cast<std::size_t>(length)};
}
std::mutex managed_lock;
std::unordered_map<const std::wstring*,unity::String*> permanent_strings;
struct DynamicString {unity::String* value; void* handle;};
std::unordered_map<std::wstring,DynamicString,WideHash,WideEqual> dynamic_strings;

// Translation tables are immutable after loading, so a translation's address
// identifies its rooted managed copy for the lifetime of the process.
unity::String* Permanent(const std::wstring& text) {
    std::lock_guard guard(managed_lock);
    if(const auto found=permanent_strings.find(&text);found!=permanent_strings.end())return found->second;
    const auto value=api->string_new_utf16(text.data(),static_cast<std::int32_t>(text.size()));
    if(!value||!api->gchandle_new(reinterpret_cast<unity::Object*>(value),false))return nullptr;
    permanent_strings.emplace(&text,value);
    return value;
}
unity::String* Dynamic(std::wstring_view source,const std::wstring& text) {
    std::lock_guard guard(managed_lock);
    if(const auto found=dynamic_strings.find(source);found!=dynamic_strings.end())return found->second.value;
    if(dynamic_strings.size()>=kDynamicCacheLimit) {
        for(const auto& [key,entry]:dynamic_strings)api->gchandle_free(entry.handle);
        dynamic_strings.clear();
    }
    const auto value=api->string_new_utf16(text.data(),static_cast<std::int32_t>(text.size()));
    if(!value)return nullptr;
    const auto handle=api->gchandle_new(reinterpret_cast<unity::Object*>(value),false);
    if(!handle)return nullptr;
    dynamic_strings.emplace(std::wstring(source),DynamicString{value,handle});
    return value;
}

// ---------------------------------------------------------------- generic text

bool IsDigit(wchar_t c) {return c>=L'0'&&c<=L'9';}
// Replaces each ASCII number (digits with inner ',' or '.') by {0}, {1}, ...
bool TemplateKey(std::wstring_view text,std::wstring& key,std::vector<std::wstring_view>& values) {
    key.clear();values.clear();
    for(std::size_t i=0;i<text.size();) {
        if(!IsDigit(text[i])){key.push_back(text[i++]);continue;}
        const auto start=i;
        while(i<text.size()&&(IsDigit(text[i])||((text[i]==L','||text[i]==L'.')&&i+1<text.size()&&IsDigit(text[i+1]))))i++;
        key+=L"{"+std::to_wstring(values.size())+L"}";
        values.push_back(text.substr(start,i-start));
    }
    return !values.empty();
}
std::wstring FillTemplate(std::wstring_view pattern,const std::vector<std::wstring_view>& values) {
    std::wstring output;
    for(std::size_t i=0;i<pattern.size();) {
        if(pattern[i]==L'{') {
            const auto close=pattern.find(L'}',i+1);
            if(close!=std::wstring_view::npos&&close>i+1) {
                std::size_t index{};bool number=true;
                for(auto j=i+1;j<close;j++){if(!IsDigit(pattern[j])){number=false;break;}index=index*10+static_cast<std::size_t>(pattern[j]-L'0');}
                if(number&&index<values.size()){output+=values[index];i=close+1;continue;}
            }
        }
        output.push_back(pattern[i++]);
    }
    return output;
}
unity::String* TranslateText(unity::String* source) {
    const auto text=View(source);
    if(text.empty())return nullptr;
    if(const auto found=state.generic.find(text);found!=state.generic.end())return Permanent(found->second);
    if(!state.templates.empty()) {
        thread_local std::wstring key;
        thread_local std::vector<std::wstring_view> values;
        if(TemplateKey(text,key,values)) {
            if(const auto found=state.templates.find(key);found!=state.templates.end())
                return Dynamic(text,FillTemplate(found->second,values));
        }
    }
    return nullptr;
}

// ---------------------------------------------------------------- IL2CPP lookup

// Generic instances compare by their definition name only (System.ReadOnlySpan
// for ReadOnlySpan<byte>), independent of how IL2CPP formats the arguments.
std::string TypeName(const unity::Type* type) {
    const auto raw=api->type_get_name(type);
    if(!raw)return {};
    std::string name;bool generic=false;
    for(const char* cursor=raw;*cursor;cursor++) {
        if(*cursor=='`'){generic=true;while(cursor[1]>='0'&&cursor[1]<='9')cursor++;continue;}
        if(*cursor=='<'||(generic&&*cursor=='['))break;
        name.push_back(*cursor);
    }
    api->free_memory(raw);
    return name;
}
unity::Class* Class(const char* namespaze,const char* name) {
    std::size_t count{};const auto assemblies=api->domain_get_assemblies(domain,&count);
    for(std::size_t i=0;i<count;i++)
        if(const auto type=api->class_from_name(api->assembly_get_image(assemblies[i]),namespaze,name))return type;
    throw std::runtime_error(std::string("IL2CPP class missing: ")+namespaze+"."+name);
}
const unity::MethodInfo* Method(unity::Class* type,const char* name,std::initializer_list<std::string_view> parameters) {
    const unity::MethodInfo* result{};
    void* iterator{};
    while(const auto method=api->class_get_methods(type,&iterator)) {
        if(std::strcmp(api->method_get_name(method),name)!=0||api->method_get_param_count(method)!=parameters.size())continue;
        std::uint32_t index{};bool match=true;
        for(const auto expected:parameters)if(TypeName(api->method_get_param(method,index++))!=expected){match=false;break;}
        if(!match)continue;
        if(result)throw std::runtime_error(std::string("IL2CPP method is ambiguous: ")+api->class_get_name(type)+"."+name);
        result=method;
    }
    if(!result||!result->methodPointer)throw std::runtime_error(std::string("IL2CPP method missing: ")+api->class_get_name(type)+"."+name);
    return result;
}
unity::FieldInfo* Field(unity::Class* type,const char* name) {
    if(const auto field=api->class_get_field_from_name(type,name))return field;
    throw std::runtime_error(std::string("IL2CPP field missing: ")+api->class_get_name(type)+"."+name);
}
unity::Object* Invoke(const unity::MethodInfo* method,void* self,void** arguments) {
    unity::Exception* exception{};
    const auto value=api->runtime_invoke(method,self,arguments,&exception);
    if(exception) {
        char detail[4096]{};
        if(api->format_exception)api->format_exception(exception,detail,static_cast<int>(sizeof(detail)));
        throw std::runtime_error(detail[0]?detail:"managed exception");
    }
    return value;
}

// ---------------------------------------------------------------- I18n

using SetValueFunction=void(*)(unity::Object*,unity::String*,unity::String*,const unity::MethodInfo*);
SetValueFunction original_set_value{};

// Qua.UI.I18nHelper.AddLocalizationText stores every parsed CSV value with
// Qua.UI.I18n.SetValue(key, value); I18nText/I18nTMProText and Get* read it.
void HookSetValue(unity::Object* self,unity::String* key,unity::String* value,const unity::MethodInfo* method) {
    unity::String* replacement{};
    const auto name=View(key);
    if(!name.empty()) {
        if(const auto found=state.i18n.find(name);found!=state.i18n.end())replacement=Permanent(found->second);
    }
    original_set_value(self,key,replacement?replacement:value,method);
}

// ---------------------------------------------------------------- TMP and UGUI text

using ParseFunction=void(*)(unity::Object*,const unity::MethodInfo*);
using SetTextFunction=void(*)(unity::Object*,unity::String*,void*,void*);
using UiSetTextFunction=void(*)(unity::Object*,unity::String*,const unity::MethodInfo*);
ParseFunction original_parse{};
SetTextFunction original_set_text{};
UiSetTextFunction original_ui_set_text{};
unity::FieldInfo* text_field{};
unity::FieldInfo* input_source_field{};
const unity::MethodInfo* get_font{};
const unity::MethodInfo* create_font_asset{};
const unity::MethodInfo* get_material{};
const unity::MethodInfo* get_shader{};
const unity::MethodInfo* set_shader{};
const unity::MethodInfo* set_hide_flags{};
const unity::MethodInfo* get_fallback_fonts{};
const unity::MethodInfo* set_fallback_fonts{};
unity::Class* fallback_list_class{};
bool font_attempted{};

// The game's Japanese font assets lack simplified Chinese glyphs. A dynamic
// font asset built from the configured font file is appended to TMP_Settings
// general fallbacks, which TMP_Text.GetTextElement searches for missing glyphs.
void CreateFallbackFont(unity::Object* text) {
    const auto path=api->string_new_utf16(state.font.data(),static_cast<std::int32_t>(state.font.size()));
    if(!path)throw std::runtime_error("font path allocation failed");
    auto face=kFontFace,size=kFontSamplingSize,padding=kFontPadding,mode=kGlyphRenderModeSdfaa,width=kFontAtlasSize,height=kFontAtlasSize;
    void* create[]{path,&face,&size,&padding,&mode,&width,&height};
    const auto asset=Invoke(create_font_asset,nullptr,create);
    if(!asset)throw std::runtime_error("TMP_FontAsset.CreateFontAsset returned null for the font file");
    if(!api->gchandle_new(asset,false))throw std::runtime_error("font asset root failed");
    auto flags=kHideDontUnloadUnusedAsset;
    void* hide[]{&flags};Invoke(set_hide_flags,asset,hide);
    if(const auto primary=Invoke(get_font,text,nullptr)) {
        const auto primary_material=Invoke(get_material,primary,nullptr);
        const auto own_material=Invoke(get_material,asset,nullptr);
        if(primary_material&&own_material) {
            if(const auto shader=Invoke(get_shader,primary_material,nullptr)){void* assign[]{shader};Invoke(set_shader,own_material,assign);}
        }
    }
    auto list=Invoke(get_fallback_fonts,nullptr,nullptr);
    if(!list) {
        list=api->object_new(fallback_list_class);
        if(!list)throw std::runtime_error("fallback font list allocation failed");
        const auto construct=api->class_get_method_from_name(fallback_list_class,".ctor",0);
        if(!construct)throw std::runtime_error("List<TMP_FontAsset> constructor missing");
        Invoke(construct,list,nullptr);
        void* assign[]{list};Invoke(set_fallback_fonts,nullptr,assign);
    }
    const auto add=api->class_get_method_from_name(api->object_get_class(list),"Add",1);
    if(!add)throw std::runtime_error("List<TMP_FontAsset>.Add missing");
    void* item[]{asset};Invoke(add,list,item);
    Log(L"Fallback font registered in TMP_Settings: "+state.font);
}
void EnsureFallbackFont(unity::Object* text) {
    // TMP text processing runs on Unity's main thread only.
    if(font_attempted)return;
    font_attempted=true;
    try{CreateFallbackFont(text);}
    catch(const std::exception& failure){Log(L"Fallback font unavailable; glyphs missing from the game font stay blank: "+ErrorText(failure));}
}
void RestoreText(unity::Object* self,unity::String* text) {
    api->field_set_value_object(self,text_field,reinterpret_cast<unity::Object*>(text));
}
// ParseInputText reads m_text for TextInputBox/TextString sources (prefab text
// and the text setter). The translation is visible only while parsing, so the
// text getter keeps returning the original string to game logic.
void HookParseInputText(unity::Object* self,const unity::MethodInfo* method) {
    std::int32_t source{-1};
    api->field_get_value(self,input_source_field,&source);
    if(source!=kTextInputBox&&source!=kTextString){original_parse(self,method);return;}
    EnsureFallbackFont(self);
    unity::String* text{};
    api->field_get_value(self,text_field,&text);
    const auto translated=TranslateText(text);
    if(!translated){original_parse(self,method);return;}
    api->field_set_value_object(self,text_field,reinterpret_cast<unity::Object*>(translated));
    struct Restore {
        unity::Object* target;unity::String* original;
        ~Restore(){RestoreText(target,original);}
    } restore{self,text};
    original_parse(self,method);
}
// SetText(string) and SetText(string, bool) share one body that copies the
// characters into the backing array; the third and fourth registers pass through.
void HookSetText(unity::Object* self,unity::String* text,void* third,void* fourth) {
    EnsureFallbackFont(self);
    const auto translated=TranslateText(text);
    original_set_text(self,translated?translated:text,third,fourth);
}
void HookUiSetText(unity::Object* self,unity::String* text,const unity::MethodInfo* method) {
    const auto translated=TranslateText(text);
    original_ui_set_text(self,translated?translated:text,method);
}

// ---------------------------------------------------------------- inline icon placeholders

using HideReplaceTextFunction=void(*)(unity::Object*,unity::Object*,const unity::MethodInfo*);
HideReplaceTextFunction original_hide_replace_text{};
unity::FieldInfo* setter_replace_text{};
unity::FieldInfo* setter_hit_count{};
unity::FieldInfo* setter_hit_list{};
unity::FieldInfo* list_items{};
unity::FieldInfo* list_size{};
unity::FieldInfo* info_character_count{};
unity::FieldInfo* info_characters{};
unity::FieldInfo* info_meshes{};
std::size_t character_size{},character_code{},character_material{},character_vertex{},character_visible{};
std::size_t mesh_size{},mesh_colors{};
constexpr std::int64_t kQuadVertices=4;
constexpr std::size_t kColorSize=4;
constexpr std::size_t kColorAlpha=3;

std::size_t ArrayLength(unity::Object* array) {
    std::uintptr_t length{};
    std::memcpy(&length,reinterpret_cast<const char*>(array)+unity::kArrayLengthOffset,sizeof(length));
    return length;
}
char* ArrayData(unity::Object* array) {return reinterpret_cast<char*>(array)+unity::kArrayDataOffset;}
template<typename T> T ReadAt(const char* address) {T value{};std::memcpy(&value,address,sizeof(T));return value;}
// Offset of a field inside an unboxed struct (an array element).
std::size_t StructOffset(unity::Class* type,const char* name) {
    const auto offset=api->field_get_offset(Field(type,name));
    if(offset<unity::kObjectHeaderSize)throw std::runtime_error(std::string("IL2CPP struct field offset invalid: ")+api->class_get_name(type)+"."+name);
    return offset-unity::kObjectHeaderSize;
}

// TextInLineObjectSetter.HideReplaceText makes the placeholder characters that
// inline icons cover transparent ("○〆" in produce descriptions): for each
// position UpdatePosition found in the parsed text it clears the alpha of the
// four vertices at characterInfo[i].vertexIndex in
// meshInfo[characterInfo[i].materialReferenceIndex].colors32, without checking
// that the character is one TMP rendered. TMP assigns vertexIndex only to the
// characters it fills into a mesh, so a position past the rendered characters
// (cut by the ellipsis, or kept from the text the view showed before) carries
// the vertex index of an older layout. With the game's single font that index
// stays inside the one mesh; with the fallback font a text spans several
// meshes of different sizes and the index leaves the bounds of the mesh the
// character now belongs to (IndexOutOfRangeException in the render callback).
// This replacement hides the same vertices for the positions that hold a
// rendered placeholder character and whose vertices lie inside their mesh.
void HookHideReplaceText(unity::Object* self,unity::Object* text_info,const unity::MethodInfo*) {
    if(!self||!text_info)return;
    std::int32_t hits{};
    unity::String* placeholder{};
    unity::Object* list{};
    api->field_get_value(self,setter_hit_count,&hits);
    api->field_get_value(self,setter_replace_text,&placeholder);
    api->field_get_value(self,setter_hit_list,&list);
    if(hits<=0||!placeholder||!list)return;
    unity::Object* items{};
    std::int32_t size{};
    api->field_get_value(list,list_items,&items);
    api->field_get_value(list,list_size,&size);
    std::int32_t rendered{};
    unity::Object* characters{};
    unity::Object* meshes{};
    api->field_get_value(text_info,info_character_count,&rendered);
    api->field_get_value(text_info,info_characters,&characters);
    api->field_get_value(text_info,info_meshes,&meshes);
    if(!items||!characters||!meshes)return;
    const std::int64_t length=api->string_length(placeholder);
    const auto units=api->string_chars(placeholder);
    const auto positions=std::min<std::int64_t>({hits,size,static_cast<std::int64_t>(ArrayLength(items))});
    const auto character_count=std::min<std::int64_t>(rendered,static_cast<std::int64_t>(ArrayLength(characters)));
    const auto mesh_count=static_cast<std::int64_t>(ArrayLength(meshes));
    for(std::int64_t i=0;i<positions;i++) {
        const std::int64_t start=ReadAt<std::int32_t>(ArrayData(items)+static_cast<std::size_t>(i)*sizeof(std::int32_t));
        for(std::int64_t offset=0;offset<length;offset++) {
            const auto index=start+offset;
            if(index<0||index>=character_count)continue;
            const auto character=ArrayData(characters)+static_cast<std::size_t>(index)*character_size;
            if(ReadAt<wchar_t>(character+character_code)!=units[offset]||ReadAt<std::uint8_t>(character+character_visible)==0)continue;
            const std::int64_t material=ReadAt<std::int32_t>(character+character_material);
            const std::int64_t vertex=ReadAt<std::int32_t>(character+character_vertex);
            if(material<0||material>=mesh_count||vertex<0)continue;
            const auto colors=ReadAt<unity::Object*>(ArrayData(meshes)+static_cast<std::size_t>(material)*mesh_size+mesh_colors);
            if(!colors||vertex+kQuadVertices>static_cast<std::int64_t>(ArrayLength(colors)))continue;
            const auto quad=ArrayData(colors)+static_cast<std::size_t>(vertex)*kColorSize;
            for(std::int64_t corner=0;corner<kQuadVertices;corner++)quad[static_cast<std::size_t>(corner)*kColorSize+kColorAlpha]=0;
        }
    }
}

// ---------------------------------------------------------------- master data

struct FieldMeta {
    unity::FieldInfo* field{};
    int kind{};
    unity::Class* type{};
    bool enumeration{};
};
std::mutex master_lock;
std::unordered_map<unity::Class*,MasterTable*> master_classes;
std::unordered_map<unity::Class*,std::unordered_map<std::wstring,FieldMeta>> field_cache;
std::unordered_map<unity::Class*,std::unordered_map<std::wstring,std::int32_t>> enum_cache;
using MergeFunction=void(*)(unity::Object*,void*,const unity::MethodInfo*);
MergeFunction original_merge{};

// Google.Protobuf's C# generator stores a proto field json_name as the private
// field json_name + "_".
const FieldMeta* FieldOf(unity::Class* type,const std::wstring& json_name) {
    auto& fields=field_cache[type];
    if(const auto found=fields.find(json_name);found!=fields.end())return found->second.field?&found->second:nullptr;
    FieldMeta meta;
    const auto name=gakumas::json::WideToUtf8(json_name)+"_";
    meta.field=api->class_get_field_from_name(type,name.c_str());
    if(meta.field) {
        const auto field_type=api->field_get_type(meta.field);
        meta.kind=api->type_get_type(field_type);
        meta.type=api->class_from_type(field_type);
        meta.enumeration=meta.kind==unity::kTypeValueType&&meta.type&&api->class_is_enum(meta.type);
    }
    const auto& stored=fields.emplace(json_name,meta).first->second;
    return stored.field?&stored:nullptr;
}
// protoc's C# enum value naming: strip the enum name prefix (case-insensitive,
// underscores ignored), then convert SHOUTY_CASE to PascalCase.
std::wstring EnumMemberName(std::wstring_view enum_name,std::wstring_view value) {
    std::wstring prefix;
    for(const wchar_t c:enum_name)if(c!=L'_')prefix.push_back(static_cast<wchar_t>(towlower(c)));
    std::size_t p{},v{};
    std::wstring_view stripped=value;
    bool matched=true;
    while(p<prefix.size()&&v<value.size()) {
        if(value[v]==L'_'){v++;continue;}
        if(static_cast<wchar_t>(towlower(value[v]))!=prefix[p++]){matched=false;break;}
        v++;
    }
    if(matched&&p==prefix.size()) {
        while(v<value.size()&&value[v]==L'_')v++;
        if(v<value.size())stripped=value.substr(v);
    }
    std::wstring result;wchar_t previous=L'_';
    for(const wchar_t c:stripped) {
        if(!iswalnum(c)){previous=c;continue;}
        if(!iswalnum(previous)||iswdigit(previous))result.push_back(static_cast<wchar_t>(towupper(c)));
        else if(iswlower(previous))result.push_back(c);
        else result.push_back(static_cast<wchar_t>(towlower(c)));
        previous=c;
    }
    if(!result.empty()&&iswdigit(result[0]))result.insert(result.begin(),L'_');
    return result;
}
bool EnumValue(unity::Class* type,std::wstring_view original_name,std::int32_t& value) {
    auto& members=enum_cache[type];
    if(members.empty()) {
        void* iterator{};
        while(const auto field=api->class_get_fields(type,&iterator)) {
            const auto flags=api->field_get_flags(field);
            if(!(flags&unity::kFieldStatic)||!(flags&unity::kFieldLiteral))continue;
            std::int32_t member{};
            api->field_static_get_value(field,&member);
            members.emplace(Widen(api->field_get_name(field)),member);
        }
    }
    const auto found=members.find(EnumMemberName(Widen(api->class_get_name(type)),original_name));
    if(found==members.end())return false;
    value=found->second;return true;
}
bool IntegerOf(const Value& value,std::int64_t& result) {
    if(value.kind==Kind::Integer){result=value.integer;return true;}
    if(value.kind!=Kind::String||value.text.empty())return false;
    std::size_t used{};
    try{result=std::stoll(value.text,&used);}catch(...){return false;}
    return used==value.text.size();
}
bool ObjectKeyPart(unity::Object* object,const FieldMeta& meta,std::wstring& output) {
    switch(meta.kind) {
    case unity::kTypeString:{unity::String* text{};api->field_get_value(object,meta.field,&text);output+=L"s:";output+=View(text);return true;}
    case unity::kTypeBoolean:{bool flag{};api->field_get_value(object,meta.field,&flag);output+=flag?L"b:1":L"b:0";return true;}
    case unity::kTypeInt32:{std::int32_t number{};api->field_get_value(object,meta.field,&number);output+=L"i:"+std::to_wstring(number);return true;}
    case unity::kTypeUInt32:{std::uint32_t number{};api->field_get_value(object,meta.field,&number);output+=L"i:"+std::to_wstring(number);return true;}
    case unity::kTypeInt64:{std::int64_t number{};api->field_get_value(object,meta.field,&number);output+=L"i:"+std::to_wstring(number);return true;}
    case unity::kTypeUInt64:{std::uint64_t number{};api->field_get_value(object,meta.field,&number);output+=L"i:"+std::to_wstring(number);return true;}
    case unity::kTypeValueType:
        if(!meta.enumeration)return false;
        {std::int32_t number{};api->field_get_value(object,meta.field,&number);output+=L"i:"+std::to_wstring(number);return true;}
    default:return false;
    }
}
bool JsonKeyPart(const Value& value,const FieldMeta& meta,std::wstring& output) {
    switch(meta.kind) {
    case unity::kTypeString:
        if(value.kind!=Kind::String)return false;
        output+=L"s:";output+=value.text;return true;
    case unity::kTypeBoolean:
        if(value.kind!=Kind::Boolean)return false;
        output+=value.boolean?L"b:1":L"b:0";return true;
    case unity::kTypeInt32:case unity::kTypeUInt32:case unity::kTypeInt64:case unity::kTypeUInt64: {
        std::int64_t number{};
        if(!IntegerOf(value,number))return false;
        output+=L"i:"+std::to_wstring(number);return true;
    }
    case unity::kTypeValueType: {
        if(!meta.enumeration)return false;
        std::int32_t number{};
        if(value.kind==Kind::Integer)number=static_cast<std::int32_t>(value.integer);
        else if(value.kind!=Kind::String||!EnumValue(meta.type,value.text,number))return false;
        output+=L"i:"+std::to_wstring(number);return true;
    }
    default:return false;
    }
}
void BuildIndex(MasterTable& table,unity::Class* type) {
    std::size_t unresolved{},duplicates{};
    std::vector<const FieldMeta*> metas;
    for(const auto& key:table.rootKeys) {
        const auto meta=FieldOf(type,key);
        if(!meta){Log(L"masterTrans/"+table.name+L".json: primary key "+key+L" is not a field of the client class; table skipped");return;}
        metas.push_back(meta);
    }
    for(const auto& row:table.document.Find(L"data")->items) {
        std::wstring key;bool resolved=true;
        for(std::size_t i=0;i<table.rootKeys.size()&&resolved;i++) {
            const auto value=row.Find(table.rootKeys[i]);
            resolved=value&&JsonKeyPart(*value,*metas[i],key);
            key.push_back(kKeySeparator);
        }
        if(!resolved){unresolved++;continue;}
        if(!table.rows.emplace(std::move(key),&row).second)duplicates++;
    }
    Log(L"masterTrans/"+table.name+L".json indexed: "+std::to_wstring(table.rows.size())+L" rows"+
        (unresolved?L", "+std::to_wstring(unresolved)+L" rows with unresolvable keys":L"")+
        (duplicates?L", "+std::to_wstring(duplicates)+L" duplicate keys ignored":L""));
}
void ApplyObject(unity::Object* object,const Value& json,const std::wstring& path,const MasterTable& table);
bool ElementMatches(unity::Object* element,const Value& json,const std::vector<std::wstring>& keys) {
    const auto type=api->object_get_class(element);
    for(const auto& key:keys) {
        const auto value=json.Find(key);
        const auto meta=FieldOf(type,key);
        std::wstring left,right;
        if(!value||!meta||!ObjectKeyPart(element,*meta,left)||!JsonKeyPart(*value,*meta,right)||left!=right)return false;
    }
    return true;
}
void ApplyRepeated(unity::Object* repeated,const Value& json,const std::wstring& path,const MasterTable& table) {
    const auto type=api->object_get_class(repeated);
    const auto array_field=api->class_get_field_from_name(type,"array");
    const auto count_field=api->class_get_field_from_name(type,"count");
    if(!array_field||!count_field)return;
    unity::Object* array{};std::int32_t count{};
    api->field_get_value(repeated,array_field,&array);
    api->field_get_value(repeated,count_field,&count);
    if(!array||count<=0)return;
    const auto array_class=api->object_get_class(array);
    const auto element_kind=api->type_get_type(api->class_get_type(api->class_get_element_class(array_class)));
    if(api->class_array_element_size(array_class)!=static_cast<int>(sizeof(void*)))return;
    const auto elements=reinterpret_cast<unity::Object**>(reinterpret_cast<unsigned char*>(array)+unity::kArrayDataOffset);
    const auto size=static_cast<std::size_t>(count);
    if(element_kind==unity::kTypeString) {
        if(json.items.size()!=size)return;
        for(std::size_t i=0;i<size;i++) {
            const auto& item=json.items[i];
            if(item.kind!=Kind::String||item.text.empty())continue;
            if(const auto text=Permanent(item.text))
                api->gc_wbarrier_set_field(array,reinterpret_cast<void**>(&elements[i]),text);
        }
        return;
    }
    if(element_kind!=unity::kTypeClass)return;
    const auto keys=table.arrayKeys.find(path);
    if(keys==table.arrayKeys.end()) {
        if(json.items.size()!=size)return;
        for(std::size_t i=0;i<size;i++)if(elements[i]&&json.items[i].kind==Kind::Object)ApplyObject(elements[i],json.items[i],path,table);
        return;
    }
    std::vector<bool> used(size);
    for(const auto& item:json.items) {
        if(item.kind!=Kind::Object)continue;
        for(std::size_t i=0;i<size;i++) {
            if(used[i]||!elements[i]||!ElementMatches(elements[i],item,keys->second))continue;
            used[i]=true;ApplyObject(elements[i],item,path,table);break;
        }
    }
}
void ApplyObject(unity::Object* object,const Value& json,const std::wstring& path,const MasterTable& table) {
    const auto type=api->object_get_class(object);
    for(std::size_t i=0;i<json.names.size();i++) {
        const auto& name=json.names[i];
        const auto child=path.empty()?name:path+L"."+name;
        if(table.keyPaths.contains(child))continue;
        const auto meta=FieldOf(type,name);
        if(!meta)continue;
        const auto& value=json.items[i];
        if(value.kind==Kind::String) {
            if(meta->kind!=unity::kTypeString||value.text.empty())continue;
            if(const auto text=Permanent(value.text))api->field_set_value_object(object,meta->field,reinterpret_cast<unity::Object*>(text));
        }else if(value.kind==Kind::Object||value.kind==Kind::Array) {
            if(meta->kind!=unity::kTypeClass&&meta->kind!=unity::kTypeGenericInstance)continue;
            unity::Object* nested{};
            api->field_get_value(object,meta->field,&nested);
            if(!nested)continue;
            if(value.kind==Kind::Object)ApplyObject(nested,value,child,table);
            else ApplyRepeated(nested,value,child,table);
        }
    }
}
// Every master row is parsed by MasterBase<TKey,TData>.GetData through
// MessageExtensions.MergeFrom(IMessage, ReadOnlySpan<byte>) on a new message.
void HookMergeFrom(unity::Object* message,void* span,const unity::MethodInfo* method) {
    original_merge(message,span,method);
    if(!message)return;
    const auto type=api->object_get_class(message);
    const auto found=master_classes.find(type);
    if(found==master_classes.end())return;
    auto& table=*found->second;
    WaitForSingleObject(state.masterReady,INFINITE);
    if(!table.valid)return;
    std::lock_guard guard(master_lock);
    if(!table.indexed){BuildIndex(table,type);table.indexed=true;}
    if(table.rows.empty())return;
    std::wstring key;
    for(const auto& name:table.rootKeys) {
        const auto meta=FieldOf(type,name);
        if(!meta||!ObjectKeyPart(message,*meta,key))return;
        key.push_back(kKeySeparator);
    }
    if(const auto row=table.rows.find(key);row!=table.rows.end())ApplyObject(message,*row->second,L"",table);
}

// ---------------------------------------------------------------- ADV scripts

struct UniTask {
    unity::Object* source{};
    std::int16_t token{};
    std::array<std::uint8_t,6> padding{};
};
struct CancellationToken {void* source;};
static_assert(sizeof(UniTask)==16);
static_assert(sizeof(CancellationToken)==8);
using LoadAsyncFunction=UniTask(*)(unity::Object*,unity::String*,CancellationToken,bool,const unity::MethodInfo*);
using LoadScriptFunction=void(*)(unity::Object*,unity::String*,const unity::MethodInfo*);
LoadAsyncFunction original_load_async{};
LoadScriptFunction original_load_script{};
unity::FieldInfo* presenter_engine{};
struct PendingScript {unity::Object* presenter; void* handle; std::wstring name;};
std::mutex script_lock;
std::vector<PendingScript> pending_scripts;
constexpr std::size_t kPendingScriptLimit=16;

// AdvPresenterBase._LoadAsync(scriptName) downloads the script text and passes
// it to its engine's Uguiss.Engine.LoadScript; the presenter is kept rooted
// until that call so its engine identifies the script name.
UniTask HookLoadAsync(unity::Object* presenter,unity::String* name,CancellationToken cancellation,bool without_voice,const unity::MethodInfo* method) {
    const auto script=View(name);
    if(presenter&&!script.empty()) {
        std::lock_guard guard(script_lock);
        for(auto it=pending_scripts.begin();it!=pending_scripts.end();) {
            if(it->presenter==presenter){api->gchandle_free(it->handle);it=pending_scripts.erase(it);}else ++it;
        }
        if(pending_scripts.size()>=kPendingScriptLimit){api->gchandle_free(pending_scripts.front().handle);pending_scripts.erase(pending_scripts.begin());}
        if(const auto handle=api->gchandle_new(presenter,false))pending_scripts.push_back({presenter,handle,std::wstring(script)});
    }
    return original_load_async(presenter,name,cancellation,without_voice,method);
}
std::wstring TakeScriptName(unity::Object* engine) {
    std::lock_guard guard(script_lock);
    for(auto it=pending_scripts.begin();it!=pending_scripts.end();++it) {
        unity::Object* current{};
        api->field_get_value(it->presenter,presenter_engine,&current);
        if(current!=engine)continue;
        auto name=std::move(it->name);
        api->gchandle_free(it->handle);pending_scripts.erase(it);
        return name;
    }
    return {};
}
bool SafeFileName(std::wstring_view name) {
    if(name.empty()||name.size()>200||name==L"."||name==L"..")return false;
    for(const wchar_t c:name)if(c<0x20||c==L'/'||c==L'\\'||c==L':'||c==L'*'||c==L'?'||c==L'"'||c==L'<'||c==L'>'||c==L'|')return false;
    return true;
}
void HookLoadScript(unity::Object* engine,unity::String* script,const unity::MethodInfo* method) {
    auto name=TakeScriptName(engine);
    if(name.empty()||!SafeFileName(name)){original_load_script(engine,script,method);return;}
    if(!EndsWith(name,L".txt"))name+=L".txt";
    unity::String* replacement{};
    const auto file=state.directory/L"resource"/name;
    std::error_code error;
    if(fs::is_regular_file(file,error)) {
        try {
            const auto text=gakumas::json::Utf8ToWide(ReadBytes(file));
            replacement=api->string_new_utf16(text.data(),static_cast<std::int32_t>(text.size()));
            Log(L"ADV script translated: "+name);
        }catch(const std::exception& failure){Log(L"ADV script "+name+L" rejected; original kept: "+ErrorText(failure));}
    }else {
        Log(L"ADV script untranslated: "+name);
    }
    original_load_script(engine,replacement?replacement:script,method);
}

// ---------------------------------------------------------------- install

template<typename T> void CreateHook(const unity::MethodInfo* method,void* replacement,T& original,std::vector<void*>& created) {
    const auto status=MH_CreateHook(method->methodPointer,replacement,reinterpret_cast<void**>(&original));
    if(status!=MH_OK)throw std::runtime_error(std::string("MinHook: ")+MH_StatusToString(status));
    created.push_back(method->methodPointer);
}
void MapMasterClasses() {
    if(state.masters.empty())return;
    const auto sample=Class("Campus.Common.Proto.Client.Master","Localization");
    std::size_t count{};const auto assemblies=api->domain_get_assemblies(domain,&count);
    const unity::Image* image{};
    for(std::size_t i=0;i<count&&!image;i++) {
        const auto candidate=api->assembly_get_image(assemblies[i]);
        if(api->class_from_name(candidate,"Campus.Common.Proto.Client.Master","Localization")==sample)image=candidate;
    }
    if(!image)throw std::runtime_error("master data image unavailable");
    std::size_t missing{};
    for(const auto& table:state.masters) {
        const auto name=gakumas::json::WideToUtf8(table->name);
        if(const auto type=api->class_from_name(image,"Campus.Common.Proto.Client.Master",name.c_str()))master_classes.emplace(type,table.get());
        else{Log(L"masterTrans/"+table->name+L".json has no client master class; ignored");missing++;}
    }
    Log(L"masterTrans classes mapped: "+std::to_wstring(master_classes.size())+(missing?L", "+std::to_wstring(missing)+L" unknown":L""));
}
}

void gakumas::StartLocalization(const SessionConfig& session) {
    if(!session.localization){gakumas::DiagnosticLog(L"[Localize] Disabled by launcher");return;}
    state.directory=session.localizationDirectory;
    state.font=session.localizationFont;
    std::error_code error;
    if(!fs::is_directory(state.directory,error)){Log(L"Translation directory missing; localization off: "+state.directory.wstring());return;}
    const auto master_folder=state.directory/L"masterTrans";
    if(fs::is_directory(master_folder,error)) {
        for(fs::directory_iterator it(master_folder,error),end;!error&&it!=end;it.increment(error)) {
            const auto file=it->path().filename().wstring();
            if(!it->is_regular_file(error)||!EndsWith(file,L".json"))continue;
            auto table=std::make_unique<MasterTable>();
            table->name=file.substr(0,file.size()-5);table->file=it->path();
            state.masters.push_back(std::move(table));
        }
        if(error)Log(L"masterTrans listing failed: "+Widen(error.message()));
    }
    state.coreReady=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    state.masterReady=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    if(!state.coreReady||!state.masterReady){Log(L"Event creation failed; localization off");return;}
    const auto loader=CreateThread(nullptr,0,LoadTranslations,nullptr,0,nullptr);
    if(!loader){Log(L"Loader thread creation failed; localization off");return;}
    CloseHandle(loader);
    state.enabled=true;
    Log(L"Translation directory: "+state.directory.wstring()+L"; font: "+state.font);
}

void gakumas::PrepareLocalization(unity::Api& unity_api,unity::Domain* unity_domain) {
    if(!state.enabled)return;
    api=&unity_api;domain=unity_domain;
    WaitForSingleObject(state.coreReady,INFINITE);
    std::vector<void*> created;
    try {
        const auto i18n=Class("Qua.UI","I18n");
        const auto set_value=Method(i18n,"SetValue",{"System.String","System.String"});
        const auto tmp_text=Class("TMPro","TMP_Text");
        const auto parse=Method(tmp_text,"ParseInputText",{});
        const auto set_text=Method(tmp_text,"SetText",{"System.String"});
        text_field=Field(tmp_text,"m_text");
        input_source_field=Field(tmp_text,"m_inputSource");
        get_font=Method(tmp_text,"get_font",{});
        const auto ui_set_text=Method(Class("UnityEngine.UI","Text"),"set_text",{"System.String"});
        create_font_asset=Method(Class("TMPro","TMP_FontAsset"),"CreateFontAsset",{"System.String","System.Int32","System.Int32","System.Int32","UnityEngine.TextCore.LowLevel.GlyphRenderMode","System.Int32","System.Int32"});
        get_material=Method(Class("TMPro","TMP_Asset"),"get_material",{});
        const auto material=Class("UnityEngine","Material");
        get_shader=Method(material,"get_shader",{});
        set_shader=Method(material,"set_shader",{"UnityEngine.Shader"});
        set_hide_flags=Method(Class("UnityEngine","Object"),"set_hideFlags",{"UnityEngine.HideFlags"});
        const auto settings=Class("TMPro","TMP_Settings");
        get_fallback_fonts=Method(settings,"get_fallbackFontAssets",{});
        set_fallback_fonts=Method(settings,"set_fallbackFontAssets",{"System.Collections.Generic.List"});
        fallback_list_class=api->class_from_type(api->field_get_type(Field(settings,"m_fallbackFontAssets")));
        if(!fallback_list_class)throw std::runtime_error("TMP_Settings fallback list type unavailable");
        const auto setter=Class("Campus.Common","TextInLineObjectSetter");
        const auto hide_replace_text=Method(setter,"HideReplaceText",{"TMPro.TMP_TextInfo"});
        setter_replace_text=Field(setter,"_replaceText");
        setter_hit_count=Field(setter,"_hitCount");
        setter_hit_list=Field(setter,"_hitIndexList");
        const auto hit_list=api->class_from_type(api->field_get_type(setter_hit_list));
        if(!hit_list)throw std::runtime_error("TextInLineObjectSetter hit list type unavailable");
        list_items=Field(hit_list,"_items");
        list_size=Field(hit_list,"_size");
        const auto text_info=Class("TMPro","TMP_TextInfo");
        info_character_count=Field(text_info,"characterCount");
        info_characters=Field(text_info,"characterInfo");
        info_meshes=Field(text_info,"meshInfo");
        const auto character_info=Class("TMPro","TMP_CharacterInfo");
        character_code=StructOffset(character_info,"character");
        character_material=StructOffset(character_info,"materialReferenceIndex");
        character_vertex=StructOffset(character_info,"vertexIndex");
        character_visible=StructOffset(character_info,"isVisible");
        mesh_colors=StructOffset(Class("TMPro","TMP_MeshInfo"),"colors32");
        const auto character_array=api->class_from_type(api->field_get_type(info_characters));
        const auto mesh_array=api->class_from_type(api->field_get_type(info_meshes));
        if(!character_array||!mesh_array)throw std::runtime_error("TMP_TextInfo array types unavailable");
        character_size=static_cast<std::size_t>(api->class_array_element_size(character_array));
        mesh_size=static_cast<std::size_t>(api->class_array_element_size(mesh_array));
        if(std::max({character_code+sizeof(wchar_t),character_material+sizeof(std::int32_t),character_vertex+sizeof(std::int32_t),character_visible+sizeof(bool)})>character_size||
            mesh_colors+sizeof(void*)>mesh_size)
            throw std::runtime_error("TMP character or mesh info layout unexpected");
        const auto merge=Method(Class("Google.Protobuf","MessageExtensions"),"MergeFrom",{"Google.Protobuf.IMessage","System.ReadOnlySpan"});
        const auto presenter=Class("Campus.Common","AdvPresenterBase");
        const auto load_async=Method(presenter,"_LoadAsync",{"System.String","System.Threading.CancellationToken","System.Boolean"});
        presenter_engine=Field(presenter,"<Engine>k__BackingField");
        const auto load_script=Method(Class("Uguiss","Engine"),"LoadScript",{"System.String"});
        MapMasterClasses();

        CreateHook(set_value,reinterpret_cast<void*>(&HookSetValue),original_set_value,created);
        CreateHook(parse,reinterpret_cast<void*>(&HookParseInputText),original_parse,created);
        CreateHook(set_text,reinterpret_cast<void*>(&HookSetText),original_set_text,created);
        CreateHook(ui_set_text,reinterpret_cast<void*>(&HookUiSetText),original_ui_set_text,created);
        CreateHook(hide_replace_text,reinterpret_cast<void*>(&HookHideReplaceText),original_hide_replace_text,created);
        if(!master_classes.empty())CreateHook(merge,reinterpret_cast<void*>(&HookMergeFrom),original_merge,created);
        CreateHook(load_async,reinterpret_cast<void*>(&HookLoadAsync),original_load_async,created);
        CreateHook(load_script,reinterpret_cast<void*>(&HookLoadScript),original_load_script,created);
        Log(L"Hooks prepared: I18n.SetValue, TMP_Text.ParseInputText/SetText, UI.Text.set_text, TextInLineObjectSetter.HideReplaceText, "+
            std::wstring(master_classes.empty()?L"":L"MessageExtensions.MergeFrom (master data), ")+L"AdvPresenterBase._LoadAsync, Uguiss.Engine.LoadScript");
    }catch(const std::exception& failure) {
        for(const auto target:created)MH_RemoveHook(target);
        master_classes.clear();
        Log(L"Localization hooks not installed: "+ErrorText(failure));
    }
}
