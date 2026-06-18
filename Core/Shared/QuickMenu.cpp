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

	const int itemHeight = 13;
	const int pad = 8;
	int count = (int)_items.size();
	int panelW = 150;
	int panelH = pad * 2 + (count + 1) * itemHeight + 4;
	int x = ((int)width - panelW) / 2;
	int y = ((int)height - panelH) / 2;

	//NOTE: the HUD uses an inverted alpha byte (0x00 = opaque, 0xFF = transparent).
	//Solid dark background + gold border so it stays readable over any scene.
	const int BG = 0x00101010;        //solid dark
	const int ACCENT = 0x00FFCC00;    //gold (opaque)
	const int HILITE = 0x002860D0;    //blue (opaque)
	const int WHITE = 0x00FFFFFF;     //opaque white text
	const int GRAY = 0x00C8C8C8;      //opaque light-grey text
	const int NOBG = 0xFF000000;      //transparent text background (no box)

	hud->DrawRectangle(x, y, panelW, panelH, BG, true, 1);
	hud->DrawRectangle(x, y, panelW, panelH, ACCENT, false, 1);
	hud->DrawRectangle(x + 1, y + 1, panelW - 2, panelH - 2, ACCENT, false, 1);

	int tx = x + pad;
	hud->DrawString(tx, y + pad, "QUICK MENU", ACCENT, NOBG, 1);

	int sel = _selected.load();
	for(int i = 0; i < count; i++) {
		int iy = y + pad + (i + 1) * itemHeight + 4;
		if(i == sel) {
			//Highlight bar for the selected entry
			hud->DrawRectangle(x + 4, iy - 2, panelW - 8, itemHeight, HILITE, true, 1);
			hud->DrawString(tx, iy, _items[i].Label, WHITE, NOBG, 1);
		} else {
			hud->DrawString(tx, iy, _items[i].Label, GRAY, NOBG, 1);
		}
	}
}
