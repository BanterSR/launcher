@echo off
rem Builds both launcher editions, each as one executable:
rem   dist\user\MomokoLauncher.exe      NativeAOT, size-optimised, for players
rem   dist\dev\MomokoLauncher.Dev.exe   developer build (DEV symbol, framework-dependent single file)
rem Needs the MSVC x64 environment for NativeAOT linking and the version.dll
rem hook: run from a "x64 Native Tools" prompt, call vcvars64.bat / a portable
rem MSVC setup first, or let this script find Visual Studio through vswhere.
rem Each edition is published to obj\publish\<edition> and only the executable
rem is copied to dist\<edition>; data\ and log\ written there at run time stay.
rem Usage: build.cmd [version]. A version X.Y.Z is compiled into both editions;
rem without it they are local builds (0.0.0-local) that do not check for updates.
setlocal EnableDelayedExpansion
set "ROOT=%~dp0"
set "VERSION_ARG="
if not "%~1"=="" set "VERSION_ARG=-p:Version=%~1"
if not defined VCToolsInstallDir (
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if not exist "!VSWHERE!" (echo MSVC x64 environment not found. Run vcvars64.bat first.& exit /b 1)
  for /f "tokens=*" %%I in ('"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath') do set "VSROOT=%%I"
  if not defined VSROOT (echo Install the Visual Studio Desktop C++ workload.& exit /b 1)
  call "!VSROOT!\VC\Auxiliary\Build\vcvars64.bat" >nul
  if errorlevel 1 exit /b 1
)
rem NativeAOT links with the MSVC tools of the current environment.
set "IlcUseEnvironmentalTools=true"
pushd "%ROOT%"
call :publish User MomokoLauncher.exe user
if errorlevel 1 (popd& exit /b 1)
call :publish Dev MomokoLauncher.Dev.exe dev
set "RESULT=%ERRORLEVEL%"
popd
exit /b %RESULT%

:publish
set "STAGE=obj\publish\%~1"
if exist "%STAGE%" rmdir /s /q "%STAGE%"
dotnet publish MomokoLauncher.csproj -c Release -p:Edition=%~1 %VERSION_ARG% -o "%STAGE%" -nologo
if errorlevel 1 exit /b 1
if not exist "dist\%~3" mkdir "dist\%~3"
copy /y "%STAGE%\%~2" "dist\%~3\%~2" >nul
exit /b %ERRORLEVEL%
