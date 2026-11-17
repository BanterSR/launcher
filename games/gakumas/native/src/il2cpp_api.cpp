#include "il2cpp_api.hpp"

namespace unity {
template<typename T> static bool Load(HMODULE module,const char* name,T& target) {
    target=reinterpret_cast<T>(GetProcAddress(module,name));
    return target!=nullptr;
}
bool Api::Bind(HMODULE module) {
    Load(module,"il2cpp_format_exception",format_exception);
    Load(module,"il2cpp_format_stack_trace",format_stack_trace);
    return Load(module,"il2cpp_domain_get",domain_get)
        && Load(module,"il2cpp_domain_get_assemblies",domain_get_assemblies)
        && Load(module,"il2cpp_assembly_get_image",assembly_get_image)
        && Load(module,"il2cpp_image_get_name",image_get_name)
        && Load(module,"il2cpp_class_from_name",class_from_name)
        && Load(module,"il2cpp_class_get_parent",class_get_parent)
        && Load(module,"il2cpp_class_get_method_from_name",class_get_method_from_name)
        && Load(module,"il2cpp_method_get_param_count",method_get_param_count)
        && Load(module,"il2cpp_thread_current",thread_current)
        && Load(module,"il2cpp_string_length",string_length)
        && Load(module,"il2cpp_string_chars",string_chars)
        && Load(module,"il2cpp_string_new_utf16",string_new_utf16)
        && Load(module,"il2cpp_gchandle_new",gchandle_new)
        && Load(module,"il2cpp_gchandle_free",gchandle_free)
        && Load(module,"il2cpp_object_new",object_new)
        && Load(module,"il2cpp_runtime_class_init",runtime_class_init)
        && Load(module,"il2cpp_runtime_invoke",runtime_invoke)
        && Load(module,"il2cpp_class_get_field_from_name",class_get_field_from_name)
        && Load(module,"il2cpp_field_get_value",field_get_value)
        && Load(module,"il2cpp_field_set_value",field_set_value)
        && Load(module,"il2cpp_field_set_value_object",field_set_value_object)
        && Load(module,"il2cpp_field_static_get_value",field_static_get_value)
        && Load(module,"il2cpp_raise_exception",raise_exception)
        && Load(module,"il2cpp_class_get_methods",class_get_methods)
        && Load(module,"il2cpp_method_get_name",method_get_name)
        && Load(module,"il2cpp_method_get_param",method_get_param)
        && Load(module,"il2cpp_type_get_name",type_get_name)
        && Load(module,"il2cpp_free",free_memory)
        && Load(module,"il2cpp_class_get_name",class_get_name)
        && Load(module,"il2cpp_class_get_namespace",class_get_namespace)
        && Load(module,"il2cpp_object_get_class",object_get_class)
        && Load(module,"il2cpp_field_get_type",field_get_type)
        && Load(module,"il2cpp_type_get_type",type_get_type)
        && Load(module,"il2cpp_class_from_type",class_from_type)
        && Load(module,"il2cpp_class_is_enum",class_is_enum)
        && Load(module,"il2cpp_class_get_fields",class_get_fields)
        && Load(module,"il2cpp_field_get_name",field_get_name)
        && Load(module,"il2cpp_field_get_flags",field_get_flags)
        && Load(module,"il2cpp_class_get_element_class",class_get_element_class)
        && Load(module,"il2cpp_class_get_type",class_get_type)
        && Load(module,"il2cpp_class_array_element_size",class_array_element_size)
        && Load(module,"il2cpp_gc_wbarrier_set_field",gc_wbarrier_set_field);
}
}
