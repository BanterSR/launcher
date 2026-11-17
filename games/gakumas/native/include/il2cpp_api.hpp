#pragma once
#include <Windows.h>
#include <cstddef>
#include <cstdint>

namespace unity {
struct Domain;
struct Assembly;
struct Image;
struct Class;
struct Object;
struct String;
struct Thread;
struct FieldInfo;
struct Exception;
struct Type;
// Unity 6000.0 IL2CPP MethodInfo begins with its methodPointer. All metadata
// lookup and string operations use the exported IL2CPP API, not fixed RVAs.
struct MethodInfo { void* methodPointer; };

// Il2CppTypeEnum values returned by type_get_type.
enum TypeKind : int {
    kTypeBoolean=0x02,kTypeInt32=0x08,kTypeUInt32=0x09,kTypeInt64=0x0a,kTypeUInt64=0x0b,
    kTypeString=0x0e,kTypeValueType=0x11,kTypeClass=0x12,kTypeGenericInstance=0x15,kTypeArray=0x1d
};
constexpr int kFieldStatic=0x0010;
constexpr int kFieldLiteral=0x0040;
// Il2CppArray: object header (16) + bounds (8) + max_length (8) before elements.
constexpr std::size_t kArrayDataOffset=32;

struct Api {
    Domain* (*domain_get)();
    const Assembly** (*domain_get_assemblies)(const Domain*, std::size_t*);
    const Image* (*assembly_get_image)(const Assembly*);
    const char* (*image_get_name)(const Image*);
    Class* (*class_from_name)(const Image*, const char*, const char*);
    Class* (*class_get_parent)(Class*);
    const MethodInfo* (*class_get_method_from_name)(Class*, const char*, int);
    std::uint32_t (*method_get_param_count)(const MethodInfo*);
    Thread* (*thread_current)();
    std::int32_t (*string_length)(String*);
    const wchar_t* (*string_chars)(String*);
    String* (*string_new_utf16)(const wchar_t*, std::int32_t);
    void* (*gchandle_new)(Object*, bool);
    void (*gchandle_free)(void*);
    Object* (*object_new)(const Class*);
    void (*runtime_class_init)(Class*);
    Object* (*runtime_invoke)(const MethodInfo*,void*,void**,Exception**);
    FieldInfo* (*class_get_field_from_name)(Class*,const char*);
    void (*field_get_value)(Object*,FieldInfo*,void*);
    void (*field_set_value)(Object*,FieldInfo*,void*);
    void (*field_set_value_object)(Object*,FieldInfo*,Object*);
    void (*field_static_get_value)(FieldInfo*,void*);
    void (*format_exception)(const Exception*,char*,int);
    void (*format_stack_trace)(const Exception*,char*,int);
    void (*raise_exception)(Exception*);
    const MethodInfo* (*class_get_methods)(Class*,void**);
    const char* (*method_get_name)(const MethodInfo*);
    const Type* (*method_get_param)(const MethodInfo*,std::uint32_t);
    char* (*type_get_name)(const Type*);
    void (*free_memory)(void*);
    const char* (*class_get_name)(Class*);
    const char* (*class_get_namespace)(Class*);
    Class* (*object_get_class)(Object*);
    const Type* (*field_get_type)(FieldInfo*);
    int (*type_get_type)(const Type*);
    Class* (*class_from_type)(const Type*);
    bool (*class_is_enum)(const Class*);
    FieldInfo* (*class_get_fields)(Class*,void**);
    const char* (*field_get_name)(FieldInfo*);
    int (*field_get_flags)(FieldInfo*);
    Class* (*class_get_element_class)(Class*);
    const Type* (*class_get_type)(Class*);
    int (*class_array_element_size)(const Class*);
    void (*gc_wbarrier_set_field)(Object*,void**,void*);

    bool Bind(HMODULE module);
};
}
