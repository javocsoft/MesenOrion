#pragma once
#include "pch.h"
#include <mutex>

class Emulator;
class DebugHud;

//Called (on the emulation/input thread) when the user picks a game in the quick menu's "Load Game"
//list. The UI registers this and loads the ROM on its own thread.
typedef void (*QuickMenuLoadHandler)(const char* path);

//An on-screen "quick menu" drawn over the game (RetroArch-style), navigated with the
//controller/keyboard via the QuickMenu* shortcuts. While it is open the game is paused.
class QuickMenu
{
private:
	enum class QuickAction { Resume, LoadGame, SaveState, LoadState, Reset, PowerCycle, PowerOff };

	struct MenuItem
	{
		string Label;
		QuickAction Action;
	};

	struct MenuGame
	{
		string Name;
		string Path;
	};

	Emulator* _emu;
	atomic<bool> _open;
	atomic<bool> _gameList;     //true = showing the favorites game list, false = main menu
	atomic<bool> _closing;      //menu closed, but resume/load deferred until the key is released
	atomic<int> _selected;      //selection in the main menu
	atomic<int> _gameSelected;  //selection in the game list
	bool _pausedByMenu = false;
	string _pendingLoadPath;    //non-empty = load this ROM once the trigger key is released
	vector<MenuItem> _items;

	std::mutex _gamesLock;
	vector<MenuGame> _games;    //favorite games pushed from the UI

	QuickMenuLoadHandler _loadHandler = nullptr;

	void Open();
	void DrawMainMenu(DebugHud* hud, uint32_t width, uint32_t height);
	void DrawGameList(DebugHud* hud, uint32_t width, uint32_t height);

public:
	QuickMenu(Emulator* emu);

	bool IsOpen() { return _open.load(); }
	//True while a close is pending the trigger key being released (avoids the key bleeding into the game)
	bool IsClosing() { return _closing.load(); }
	void FinishClose();
	void Toggle();
	void Close();
	void Back();
	void MoveUp();
	void MoveDown();
	void Select();

	//Interop with the UI
	void SetGames(const char* data);
	void SetLoadHandler(QuickMenuLoadHandler handler) { _loadHandler = handler; }

	void Draw(DebugHud* hud, uint32_t width, uint32_t height);
};
