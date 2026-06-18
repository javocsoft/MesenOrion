#include "Common.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/QuickMenu.h"

extern unique_ptr<Emulator> _emu;

extern "C"
{
	//Pushes the favorite games list (UI -> core) as "name <0x1F> path" records, one per line.
	DllExport void __stdcall QuickMenuSetGames(const char* data)
	{
		QuickMenu* qm = _emu->GetQuickMenu();
		if(qm) {
			qm->SetGames(data ? data : "");
		}
	}

	//Registers the callback invoked when the user picks a game in the quick menu's "Load Game" list.
	DllExport void __stdcall QuickMenuSetLoadCallback(QuickMenuLoadHandler callback)
	{
		QuickMenu* qm = _emu->GetQuickMenu();
		if(qm) {
			qm->SetLoadHandler(callback);
		}
	}
}
