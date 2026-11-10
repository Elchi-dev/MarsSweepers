# lib

Reference assemblies the build compiles against. None of them are in the
repository: the BepInEx ones are LGPL and come with BepInEx, the game's are
made from your own copy of the game.

| File | Where it comes from |
| --- | --- |
| `BepInEx.Core.dll`, `BepInEx.Unity.IL2CPP.dll`, `BepInEx.Unity.Common.dll`, `0Harmony.dll`, `Il2CppInterop.Runtime.dll`, `Il2CppInterop.Common.dll` | `BepInEx/core/` of a BepInEx 6 be.791 install |
| `Il2Cppmscorlib.dll`, `UnityEngine.CoreModule.dll` | `BepInEx/interop/` after the game ran once with BepInEx |
| `interop/Assembly-CSharp.dll`, `interop/com.rlabrecque.steamworks.net.dll`, `interop/__Generated.dll` | `BepInEx/interop/` |
| `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791.zip` | https://builds.bepinex.dev/projects/bepinex_be, build 791, Unity IL2CPP win x64. The installer embeds it |

Game version these were made from: Moon Sweepers 1.0.5 (Unity 6000.6.0f1,
IL2CPP metadata v108). Another game version needs new interop assemblies,
and the hooks in `docs/HOOKS.md` checked again.
