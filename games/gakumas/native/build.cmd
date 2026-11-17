@echo off
setlocal EnableDelayedExpansion
set "ROOT=%~dp0"
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not defined VCToolsInstallDir (
  if not exist "%VSWHERE%" (echo Visual Studio C++ build tools were not found.& exit /b 1)
  for /f "tokens=*" %%I in ('"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath') do set "VSROOT=%%I"
  if not defined VSROOT (echo Install the Visual Studio Desktop C++ workload.& exit /b 1)
  call "!VSROOT!\VC\Auxiliary\Build\vcvars64.bat" >nul
  if errorlevel 1 exit /b 1
)
if not exist "%ROOT%build" mkdir "%ROOT%build"
if not exist "%ROOT%bin" mkdir "%ROOT%bin"
pushd "%ROOT%build"
cl /nologo /O2 /MT /W3 /c /I"%ROOT%vendor\minhook-1.3.4\include" "%ROOT%vendor\minhook-1.3.4\src\buffer.c" "%ROOT%vendor\minhook-1.3.4\src\hook.c" "%ROOT%vendor\minhook-1.3.4\src\trampoline.c" "%ROOT%vendor\minhook-1.3.4\src\hde\hde64.c"
if errorlevel 1 (popd& exit /b 1)
ml64 /nologo /c /Fo"%ROOT%build\version_forwarders.obj" "%ROOT%src\version_forwarders.asm"
if errorlevel 1 (popd& exit /b 1)
cl /nologo /std:c++20 /O2 /MT /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE /DNOMINMAX /LD /I"%ROOT%include" /I"%ROOT%vendor\minhook-1.3.4\include" "%ROOT%src\il2cpp_api.cpp" "%ROOT%src\json.cpp" "%ROOT%src\localization.cpp" "%ROOT%src\local_session.cpp" "%ROOT%src\diagnostics.cpp" "%ROOT%src\runtime_bootstrap.cpp" "%ROOT%src\sdk_hook.cpp" "%ROOT%src\version_proxy.cpp" buffer.obj hook.obj trampoline.obj hde64.obj version_forwarders.obj /link /DEF:"%ROOT%version.def" /OUT:"%ROOT%bin\version.dll" /IMPLIB:"%ROOT%build\version.lib" /DYNAMICBASE /NXCOMPAT /MACHINE:X64 kernel32.lib shlwapi.lib user32.lib
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%
