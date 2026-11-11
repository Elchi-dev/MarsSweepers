// dstorage.dll proxy for Moon Sweepers: forwards DirectStorage to the game's own copy (dstorage_original.dll)
// and, when the game loads it, loads BepInEx's Doorstop from BepInEx\doorstop\winhttp.dll by its full path.
// Used when Windows does not search the game folder first for the game's DLLs, so a winhttp.dll or
// version.dll in the game folder is never picked (Windows has its own copies of both); dstorage.dll has no
// Windows copy, so the game always loads this file from its folder.
typedef void *HMODULE;
typedef unsigned long DWORD;
typedef int BOOL;
typedef unsigned short WCHAR;
__declspec(dllimport) DWORD __stdcall GetModuleFileNameW(HMODULE, WCHAR *, DWORD);
__declspec(dllimport) HMODULE __stdcall LoadLibraryW(const WCHAR *);
__declspec(dllimport) BOOL __stdcall DisableThreadLibraryCalls(HMODULE);

static const WCHAR suffix[] = L"BepInEx\\doorstop\\winhttp.dll";

BOOL __stdcall DllMain(HMODULE self, DWORD reason, void *reserved)
{
    if (reason != 1) return 1;
    DisableThreadLibraryCalls(self);
    WCHAR path[1024];
    DWORD n = GetModuleFileNameW(self, path, 1000);
    if (n == 0 || n >= 1000) return 1;
    while (n > 0 && path[n - 1] != L'\\') n--;
    for (int i = 0; suffix[i] && n < 1020; i++) path[n++] = suffix[i];
    path[n] = 0;
    LoadLibraryW(path);
    return 1;
}
