# Native Hook dependency

MinHook 1.3.4, Tsuda Kageyu and contributors. Unmodified source is vendored in
vendor/minhook-1.3.4; original license is retained and copied beside the built DLL.

Source: https://github.com/TsudaKageyu/minhook/tree/v1.3.4
Archive: https://codeload.github.com/TsudaKageyu/minhook/zip/refs/tags/v1.3.4
Archive SHA-256: 172708123daa0c98d20d3a980b16a50be14af243dc95dee6f79c24193ad010e4

Unity API binding: exported IL2CPP C APIs from the installed Unity 6000.0
GameAssembly.dll. UnityWebRequest.SetUrl(string) is located by assembly, namespace,
class, method name and parameter count; there are no game-version RVAs in this hook.
MethodInfo.methodPointer uses the Unity 6000.0 x64 ABI, not a hardcoded function address.

Local launch compatibility also resolves the client account manager and DMM
purchase manager by IL2CPP class/method names. Managed constructors and setters
are invoked with il2cpp_runtime_invoke; reference fields use
il2cpp_field_set_value_object. GC handles use Unity 6000.0's pointer-sized
Il2CppGCHandle ABI. UniTask<reference-type> is the installed client's 24-byte x64
value type, returned by ordinary compiled C++ functions.

Startup loader design references (read-only, no framework code copied):
- MelonLoader's upstream README documents placing version.dll in the game directory:
  https://github.com/LavaGang/MelonLoader
- UnityDoorstop documents startup bootstrapping for Mono and IL2CPP:
  https://github.com/NeighTools/UnityDoorstop
- BepInEx IL2CPP deployment documentation:
  https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

The custom version.dll forwards the 17 System32 version.dll exports by name and
ordinal. UnityPlayer.dll in this installation imports GetFileVersionInfoSizeA,
GetFileVersionInfoA, and VerQueryValueA. No Detours, remote-thread injector,
process scanner, CoreCLR bootstrap, or external mod-loader runtime is shipped.

The bootstrap replaces only UnityPlayer's GetProcAddress import in memory and
wraps its il2cpp_init/il2cpp_init_utf16 lookup. Managed API operations occur
only after il2cpp_init returns on the same Unity-owned, registered thread. The
native worker handles Win32 setup only and does not call IL2CPP or GC functions.
