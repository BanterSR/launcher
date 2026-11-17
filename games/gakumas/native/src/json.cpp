#include "json.hpp"
#include <Windows.h>
#include <charconv>
#include <stdexcept>

namespace gakumas::json {
namespace {
constexpr int kMaximumDepth=256;

class Parser {
public:
    explicit Parser(std::string_view source):input(source) {
        if(input.size()>=3&&static_cast<unsigned char>(input[0])==0xEF&&static_cast<unsigned char>(input[1])==0xBB&&static_cast<unsigned char>(input[2])==0xBF)position=3;
    }
    Value Document() {
        Value result=ParseValue(0);
        SkipSpace();
        if(position!=input.size())Fail("unexpected trailing data");
        return result;
    }
private:
    std::string_view input;
    std::size_t position{};

    [[noreturn]] void Fail(const char* reason) const {
        throw std::runtime_error("JSON error at byte "+std::to_string(position)+": "+reason);
    }
    void SkipSpace() {
        while(position<input.size()) {
            const char c=input[position];
            if(c!=' '&&c!='\t'&&c!='\n'&&c!='\r')break;
            position++;
        }
    }
    bool Consume(std::string_view token) {
        if(input.substr(position,token.size())!=token)return false;
        position+=token.size();return true;
    }
    Value ParseValue(int depth) {
        if(depth>kMaximumDepth)Fail("nesting too deep");
        SkipSpace();
        if(position>=input.size())Fail("unexpected end of input");
        Value value;
        switch(input[position]) {
        case '{':ParseObject(value,depth);break;
        case '[':ParseArray(value,depth);break;
        case '"':value.kind=Kind::String;value.text=ParseString();break;
        case 't':if(!Consume("true"))Fail("invalid literal");value.kind=Kind::Boolean;value.boolean=true;break;
        case 'f':if(!Consume("false"))Fail("invalid literal");value.kind=Kind::Boolean;value.boolean=false;break;
        case 'n':if(!Consume("null"))Fail("invalid literal");value.kind=Kind::Null;break;
        default:ParseNumber(value);break;
        }
        return value;
    }
    void ParseObject(Value& value,int depth) {
        value.kind=Kind::Object;position++;
        SkipSpace();
        if(position<input.size()&&input[position]=='}'){position++;return;}
        for(;;) {
            SkipSpace();
            if(position>=input.size()||input[position]!='"')Fail("object member name expected");
            value.names.push_back(ParseString());
            SkipSpace();
            if(position>=input.size()||input[position]!=':')Fail("':' expected");
            position++;
            value.items.push_back(ParseValue(depth+1));
            SkipSpace();
            if(position>=input.size())Fail("unterminated object");
            if(input[position]==','){position++;continue;}
            if(input[position]=='}'){position++;return;}
            Fail("',' or '}' expected");
        }
    }
    void ParseArray(Value& value,int depth) {
        value.kind=Kind::Array;position++;
        SkipSpace();
        if(position<input.size()&&input[position]==']'){position++;return;}
        for(;;) {
            value.items.push_back(ParseValue(depth+1));
            SkipSpace();
            if(position>=input.size())Fail("unterminated array");
            if(input[position]==','){position++;continue;}
            if(input[position]==']'){position++;return;}
            Fail("',' or ']' expected");
        }
    }
    void AppendUtf8(std::wstring& output,std::size_t start,std::size_t end) {
        if(end<=start)return;
        const auto count=static_cast<int>(end-start);
        const auto length=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,input.data()+start,count,nullptr,0);
        if(length<=0)Fail("invalid UTF-8 in string");
        const auto offset=output.size();
        output.resize(offset+static_cast<std::size_t>(length));
        MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,input.data()+start,count,output.data()+offset,length);
    }
    unsigned Hex(char c) {
        if(c>='0'&&c<='9')return static_cast<unsigned>(c-'0');
        if(c>='a'&&c<='f')return static_cast<unsigned>(c-'a'+10);
        if(c>='A'&&c<='F')return static_cast<unsigned>(c-'A'+10);
        Fail("invalid \\u escape");
    }
    std::wstring ParseString() {
        position++;
        std::wstring output;
        std::size_t run=position;
        while(position<input.size()) {
            const char c=input[position];
            if(c=='"'){AppendUtf8(output,run,position);position++;return output;}
            if(static_cast<unsigned char>(c)<0x20)Fail("control character in string");
            if(c!='\\'){position++;continue;}
            AppendUtf8(output,run,position);
            if(++position>=input.size())Fail("unterminated escape");
            switch(input[position]) {
            case '"':output.push_back(L'"');break;
            case '\\':output.push_back(L'\\');break;
            case '/':output.push_back(L'/');break;
            case 'b':output.push_back(L'\b');break;
            case 'f':output.push_back(L'\f');break;
            case 'n':output.push_back(L'\n');break;
            case 'r':output.push_back(L'\r');break;
            case 't':output.push_back(L'\t');break;
            case 'u': {
                if(position+4>=input.size())Fail("truncated \\u escape");
                unsigned unit=0;
                for(int i=1;i<=4;i++)unit=unit*16+Hex(input[position+i]);
                output.push_back(static_cast<wchar_t>(unit));
                position+=4;break;
            }
            default:Fail("invalid escape");
            }
            position++;run=position;
        }
        Fail("unterminated string");
    }
    void ParseNumber(Value& value) {
        const auto start=position;
        if(position<input.size()&&input[position]=='-')position++;
        bool real=false;
        while(position<input.size()) {
            const char c=input[position];
            if(c>='0'&&c<='9'){position++;continue;}
            if(c=='.'||c=='e'||c=='E'||c=='+'||(c=='-'&&position>start)){real=true;position++;continue;}
            break;
        }
        const auto first=input.data()+start,last=input.data()+position;
        if(first==last||(last-first==1&&*first=='-'))Fail("invalid value");
        if(!real) {
            const auto result=std::from_chars(first,last,value.integer);
            if(result.ec==std::errc{}&&result.ptr==last){value.kind=Kind::Integer;value.real=static_cast<double>(value.integer);return;}
        }
        const auto result=std::from_chars(first,last,value.real);
        if(result.ec!=std::errc{}||result.ptr!=last)Fail("invalid number");
        value.kind=Kind::Real;
    }
};
}

const Value* Value::Find(std::wstring_view name) const {
    if(kind!=Kind::Object)return nullptr;
    for(std::size_t i=0;i<names.size();i++)if(names[i]==name)return &items[i];
    return nullptr;
}

Value Parse(std::string_view utf8) {
    return Parser(utf8).Document();
}

std::wstring Utf8ToWide(std::string_view utf8) {
    if(utf8.size()>=3&&static_cast<unsigned char>(utf8[0])==0xEF&&static_cast<unsigned char>(utf8[1])==0xBB&&static_cast<unsigned char>(utf8[2])==0xBF)utf8.remove_prefix(3);
    if(utf8.empty())return {};
    const auto count=static_cast<int>(utf8.size());
    const auto length=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,utf8.data(),count,nullptr,0);
    if(length<=0)throw std::runtime_error("invalid UTF-8 text");
    std::wstring output(static_cast<std::size_t>(length),L'\0');
    MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,utf8.data(),count,output.data(),length);
    return output;
}

std::string WideToUtf8(std::wstring_view wide) {
    if(wide.empty())return {};
    const auto count=static_cast<int>(wide.size());
    const auto length=WideCharToMultiByte(CP_UTF8,0,wide.data(),count,nullptr,0,nullptr,nullptr);
    if(length<=0)return {};
    std::string output(static_cast<std::size_t>(length),'\0');
    WideCharToMultiByte(CP_UTF8,0,wide.data(),count,output.data(),length,nullptr,nullptr);
    return output;
}

void AppendQuoted(std::string& output,std::wstring_view text) {
    std::wstring escaped;
    escaped.reserve(text.size()+2);
    escaped.push_back(L'"');
    for(const wchar_t c:text) {
        switch(c) {
        case L'"':escaped+=L"\\\"";break;
        case L'\\':escaped+=L"\\\\";break;
        case L'\n':escaped+=L"\\n";break;
        case L'\r':escaped+=L"\\r";break;
        case L'\t':escaped+=L"\\t";break;
        case L'\b':escaped+=L"\\b";break;
        case L'\f':escaped+=L"\\f";break;
        default:
            if(c<0x20){wchar_t buffer[8]{};swprintf_s(buffer,L"\\u%04x",static_cast<unsigned>(c));escaped+=buffer;}
            else escaped.push_back(c);
        }
    }
    escaped.push_back(L'"');
    output+=WideToUtf8(escaped);
}
}
