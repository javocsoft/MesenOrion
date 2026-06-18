#pragma once
#include "pch.h"

class Emulator;
class DebugHud;

//An on-screen "quick menu" drawn over the game (RetroArch-style), navigated with the
//controller/keyboard via the QuickMenu* shortcuts. While it is open the game is paused.
class QuickMenu
{
private:
	enum class QuickAction { Resume, SaveState, LoadState, Reset, PowerCycle, PowerOff };

	struct MenuItem
	{
		string Label;
		QuickAction Action;
	};

	Emulator* _emu;
	atomic<bool> _open;
	atomic<int> _selected;
	bool _pausedByMenu = false;
	vector<MenuItem> _items;

	void Open();

public:
	QuickMenu(Emulator* emu);

	bool IsOpen() { return _open.load(); }
	void Toggle();
	void Close();
	void MoveUp();
	void MoveDown();
	void Select();

	void Draw(DebugHud* hud, uint32_t width, uint32_t height);
};
