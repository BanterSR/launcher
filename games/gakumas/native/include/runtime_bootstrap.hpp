#pragma once
#include <Windows.h>
namespace gakumas {
// Native-only: intercept UnityPlayer's GetProcAddress import before its runtime
// initialization. This function never calls an IL2CPP/GC API.
bool ArmRuntimeBootstrap();
bool RuntimeBootstrapArmed();
using RuntimeReadyCallback=void(*)(HMODULE,int,DWORD);
void SetRuntimeReadyCallback(RuntimeReadyCallback callback);
}
