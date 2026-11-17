option casemap:none
EXTERN GakumasResolveVersionExport:PROC
.code
ForwardVersion PROC FRAME
    sub rsp, 88h
    .allocstack 88h
    .endprolog
    mov [rsp+20h], rcx
    mov [rsp+28h], rdx
    mov [rsp+30h], r8
    mov [rsp+38h], r9
    movdqu [rsp+40h], xmm0
    movdqu [rsp+50h], xmm1
    movdqu [rsp+60h], xmm2
    movdqu [rsp+70h], xmm3
    mov ecx, r10d
    call GakumasResolveVersionExport
    mov r11, rax
    mov rcx, [rsp+20h]
    mov rdx, [rsp+28h]
    mov r8, [rsp+30h]
    mov r9, [rsp+38h]
    movdqu xmm0, [rsp+40h]
    movdqu xmm1, [rsp+50h]
    movdqu xmm2, [rsp+60h]
    movdqu xmm3, [rsp+70h]
    add rsp, 88h
    jmp r11
ForwardVersion ENDP
VERSION_THUNK MACRO symbol, index
PUBLIC symbol
symbol PROC
    mov r10d, index
    jmp ForwardVersion
symbol ENDP
ENDM
VERSION_THUNK ProxyGetFileVersionInfoA, 0
VERSION_THUNK ProxyGetFileVersionInfoByHandle, 1
VERSION_THUNK ProxyGetFileVersionInfoExA, 2
VERSION_THUNK ProxyGetFileVersionInfoExW, 3
VERSION_THUNK ProxyGetFileVersionInfoSizeA, 4
VERSION_THUNK ProxyGetFileVersionInfoSizeExA, 5
VERSION_THUNK ProxyGetFileVersionInfoSizeExW, 6
VERSION_THUNK ProxyGetFileVersionInfoSizeW, 7
VERSION_THUNK ProxyGetFileVersionInfoW, 8
VERSION_THUNK ProxyVerFindFileA, 9
VERSION_THUNK ProxyVerFindFileW, 10
VERSION_THUNK ProxyVerInstallFileA, 11
VERSION_THUNK ProxyVerInstallFileW, 12
VERSION_THUNK ProxyVerLanguageNameA, 13
VERSION_THUNK ProxyVerLanguageNameW, 14
VERSION_THUNK ProxyVerQueryValueA, 15
VERSION_THUNK ProxyVerQueryValueW, 16
END
