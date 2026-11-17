#pragma once
#include <cstdint>
#include <string>
#include <string_view>
#include <vector>

namespace gakumas::json {
enum class Kind : std::uint8_t {Null,Boolean,Integer,Real,String,Array,Object};

// Immutable JSON document node. Arrays keep their elements in items; objects
// keep member names in names and the member values at the same index in items.
struct Value {
    Kind kind{Kind::Null};
    bool boolean{};
    std::int64_t integer{};
    double real{};
    std::wstring text;
    std::vector<std::wstring> names;
    std::vector<Value> items;

    const Value* Find(std::wstring_view name) const;
};

// Parses UTF-8 JSON (an optional BOM is accepted). Throws std::runtime_error
// with the byte offset on malformed input.
Value Parse(std::string_view utf8);
std::wstring Utf8ToWide(std::string_view utf8);
std::string WideToUtf8(std::wstring_view wide);
// Appends text as a quoted, escaped JSON string in UTF-8.
void AppendQuoted(std::string& output,std::wstring_view text);
}
