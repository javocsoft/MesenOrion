#include "pch.h"
#include "Shared/QuickMenu.h"
#include "Shared/Emulator.h"
#include "Shared/SaveStateManager.h"
#include "Shared/SystemActionManager.h"
#include "Shared/Video/DebugHud.h"

QuickMenu::QuickMenu(Emulator* emu)
{
	_emu = emu;
	_open = false;
	_selected = 0;
	_items = {
		{ "Resume", QuickAction::Resume },
		{ "Save State", QuickAction::SaveState },
		{ "Load State", QuickAction::LoadState },
		{ "Reset", QuickAction::Reset },
		{ "Power Cycle", QuickAction::PowerCycle },
		{ "Power Off", QuickAction::PowerOff },
	};
}

void QuickMenu::Open()
{
	if(!_emu->IsRunning()) {
		return;
	}
	_selected = 0;
	_open = true;
	//Pause the game while the menu is open (so controller input drives the menu, not the game)
	if(!_emu->IsPaused()) {
		_emu->Pause();
		_pausedByMenu = true;
	}
}

void QuickMenu::Close()
{
	if(!_open.load()) {
		return;
	}
	_open = false;
	if(_pausedByMenu) {
		_pausedByMenu = false;
		_emu->Resume();
	}
}

void QuickMenu::Toggle()
{
	if(_open.load()) {
		Close();
	} else {
		Open();
	}
}

void QuickMenu::MoveUp()
{
	if(!_open.load()) {
		return;
	}
	int n = (int)_items.size();
	_selected = (_selected.load() - 1 + n) % n;
}

void QuickMenu::MoveDown()
{
	if(!_open.load()) {
		return;
	}
	int n = (int)_items.size();
	_selected = (_selected.load() + 1) % n;
}

void QuickMenu::Select()
{
	if(!_open.load()) {
		return;
	}
	switch(_items[_selected.load()].Action) {
		case QuickAction::Resume:
			Close();
			break;
		case QuickAction::SaveState:
			_emu->GetSaveStateManager()->SaveState();
			Close();
			break;
		case QuickAction::LoadState:
			_emu->GetSaveStateManager()->LoadState();
			Close();
			break;
		case QuickAction::Reset:
			Close();
			_emu->GetSystemActionManager()->Reset();
			break;
		case QuickAction::PowerCycle:
			Close();
			_emu->GetSystemActionManager()->PowerCycle();
			break;
		case QuickAction::PowerOff:
			_open = false;
			_pausedByMenu = false;
			_emu->Stop(true);
			break;
	}
}

void QuickMenu::Draw(DebugHud* hud, uint32_t width, uint32_t height)
{
	if(!_open.load()) {
		return;
	}

	const int itemHeight = 12;
	int count = (int)_items.size();
	int panelW = 120;
	int panelH = (count + 1) * itemHeight + 14;
	int x = ((int)width - panelW) / 2;
	int y = ((int)height - panelH) / 2;

	//Panel background + accent border
	hud->DrawRectangle(x, y, panelW, panelH, 0xC0000000, true, 1);
	hud->DrawRectangle(x, y, panelW, panelH, 0xFFFFCC00, false, 1);

	hud->DrawString(x + 8, y + 5, "QUICK MENU", 0xFFFFCC00, 0, 1);

	int sel = _selected.load();
	for(int i = 0; i < count; i++) {
		int ty = y + 7 + (i + 1) * itemHeight;
		if(i == sel) {
			hud->DrawRectangle(x + 3, ty - 1, panelW - 6, itemHeight, 0xFF3060C0, true, 1);
		}
		hud->DrawString(x + 8, ty, _items[i].Label, 0xFFFFFFFF, 0, 1);
	}
}
