#include "pch.h"
#include "Shared/QuickMenu.h"
#include "Shared/Emulator.h"
#include "Shared/SaveStateManager.h"
#include "Shared/SystemActionManager.h"
#include "Shared/Video/DebugHud.h"
#include "Shared/Video/DrawStringCommand.h"

QuickMenu::QuickMenu(Emulator* emu)
{
	_emu = emu;
	_open = false;
	_gameList = false;
	_closing = false;
	_selected = 0;
	_gameSelected = 0;
	_items = {
		{ "Resume", QuickAction::Resume },
		{ "Load Game", QuickAction::LoadGame },
		{ "Save State", QuickAction::SaveState },
		{ "Load State", QuickAction::LoadState },
		{ "Reset", QuickAction::Reset },
		{ "Power Cycle", QuickAction::PowerCycle },
		{ "Power Off", QuickAction::PowerOff },
	};
}

void QuickMenu::SetGames(const char* data)
{
	vector<MenuGame> games;
	string s = data ? data : "";
	size_t pos = 0;
	while(pos < s.size()) {
		size_t nl = s.find('\n', pos);
		string line = s.substr(pos, nl == string::npos ? string::npos : nl - pos);
		pos = (nl == string::npos) ? s.size() : nl + 1;
		size_t sep = line.find('\x1f');
		if(sep != string::npos) {
			games.push_back({ line.substr(0, sep), line.substr(sep + 1) });
		}
	}

	std::lock_guard<std::mutex> lock(_gamesLock);
	_games = std::move(games);
	if(_gameSelected.load() >= (int)_games.size()) {
		_gameSelected = 0;
	}
}

void QuickMenu::Open()
{
	if(!_emu->IsRunning()) {
		return;
	}
	_selected = 0;
	_gameList = false;
	_closing = false;
	_pendingLoadPath.clear();
	_open = true;
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
	_gameList = false;
	//Defer the actual resume until the trigger key is released (see FinishClose), so the key that
	//confirmed/closed the menu doesn't bleed through to the game when it un-pauses.
	_closing = true;
}

//Completes a pending close once the trigger key has been released.
void QuickMenu::FinishClose()
{
	if(!_closing.load()) {
		return;
	}
	_closing = false;

	if(!_pendingLoadPath.empty()) {
		string path = _pendingLoadPath;
		_pendingLoadPath.clear();
		_pausedByMenu = false;
		//Clear the pause (set when the menu opened) so the newly loaded game runs instead of starting paused
		if(_emu->IsPaused()) {
			_emu->Resume();
		}
		if(_loadHandler) {
			_loadHandler(path.c_str());
		}
	} else if(_pausedByMenu) {
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

void QuickMenu::Back()
{
	if(!_open.load()) {
		return;
	}
	if(_gameList.load()) {
		//Return to the main menu from the game list
		_gameList = false;
	} else {
		Close();
	}
}

void QuickMenu::MoveUp()
{
	if(!_open.load()) {
		return;
	}
	if(_gameList.load()) {
		std::lock_guard<std::mutex> lock(_gamesLock);
		int n = (int)_games.size();
		if(n > 0) {
			_gameSelected = (_gameSelected.load() - 1 + n) % n;
		}
	} else {
		int n = (int)_items.size();
		_selected = (_selected.load() - 1 + n) % n;
	}
}

void QuickMenu::MoveDown()
{
	if(!_open.load()) {
		return;
	}
	if(_gameList.load()) {
		std::lock_guard<std::mutex> lock(_gamesLock);
		int n = (int)_games.size();
		if(n > 0) {
			_gameSelected = (_gameSelected.load() + 1) % n;
		}
	} else {
		int n = (int)_items.size();
		_selected = (_selected.load() + 1) % n;
	}
}

void QuickMenu::Select()
{
	if(!_open.load()) {
		return;
	}

	if(_gameList.load()) {
		//Load the selected favorite game (the UI does the actual loading)
		string path;
		{
			std::lock_guard<std::mutex> lock(_gamesLock);
			int sel = _gameSelected.load();
			if(sel >= 0 && sel < (int)_games.size()) {
				path = _games[sel].Path;
			}
		}
		if(!path.empty()) {
			//Load is deferred until the trigger key is released (see FinishClose)
			_pendingLoadPath = path;
			Close();
		}
		return;
	}

	switch(_items[_selected.load()].Action) {
		case QuickAction::Resume:
			Close();
			break;
		case QuickAction::LoadGame: {
			std::lock_guard<std::mutex> lock(_gamesLock);
			if(!_games.empty()) {
				_gameSelected = 0;
				_gameList = true;
			}
			break;
		}
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
			_gameList = false;
			_pausedByMenu = false;
			_emu->Stop(true);
			break;
	}
}

//Drawing -----------------------------------------------------------------------------------------
//NOTE: the HUD uses an inverted alpha byte (0x00 = opaque, 0xFF = transparent).

static const int QM_BG = 0x00101010;      //solid dark
static const int QM_ACCENT = 0x00FFCC00;  //gold (opaque)
static const int QM_HILITE = 0x002860D0;  //blue (opaque)
static const int QM_WHITE = 0x00FFFFFF;   //opaque white text
static const int QM_GRAY = 0x00C8C8C8;    //opaque light-grey text
static const int QM_NOBG = 0xFF000000;    //transparent text background (no box)

//Truncates a string with an ellipsis so it fits within maxW pixels (the HUD font is variable-width).
static string TruncateToWidth(const string& text, int maxW)
{
	string full = text;
	if((int)DrawStringCommand::MeasureString(full).X <= maxW) {
		return text;
	}
	string ell = "...";
	int ellW = (int)DrawStringCommand::MeasureString(ell).X;
	string result;
	for(size_t i = 0; i < text.size(); i++) {
		string cand = result + text[i];
		if((int)DrawStringCommand::MeasureString(cand).X + ellW > maxW) {
			break;
		}
		result = cand;
	}
	return result + "...";
}

void QuickMenu::Draw(DebugHud* hud, uint32_t width, uint32_t height)
{
	if(!_open.load()) {
		return;
	}
	if(_gameList.load()) {
		DrawGameList(hud, width, height);
	} else {
		DrawMainMenu(hud, width, height);
	}
}

void QuickMenu::DrawMainMenu(DebugHud* hud, uint32_t width, uint32_t height)
{
	const int itemHeight = 13;
	const int pad = 8;
	int count = (int)_items.size();
	int panelW = 150;
	int panelH = pad * 2 + (count + 1) * itemHeight + 4;
	int x = ((int)width - panelW) / 2;
	int y = ((int)height - panelH) / 2;

	hud->DrawRectangle(x, y, panelW, panelH, QM_BG, true, 1);
	hud->DrawRectangle(x, y, panelW, panelH, QM_ACCENT, false, 1);
	hud->DrawRectangle(x + 1, y + 1, panelW - 2, panelH - 2, QM_ACCENT, false, 1);

	int tx = x + pad;
	hud->DrawString(tx, y + pad, "QUICK MENU", QM_ACCENT, QM_NOBG, 1);

	int sel = _selected.load();
	for(int i = 0; i < count; i++) {
		int iy = y + pad + (i + 1) * itemHeight + 4;
		if(i == sel) {
			hud->DrawRectangle(x + 4, iy - 2, panelW - 8, itemHeight, QM_HILITE, true, 1);
			hud->DrawString(tx, iy, _items[i].Label, QM_WHITE, QM_NOBG, 1);
		} else {
			hud->DrawString(tx, iy, _items[i].Label, QM_GRAY, QM_NOBG, 1);
		}
	}
}

void QuickMenu::DrawGameList(DebugHud* hud, uint32_t width, uint32_t height)
{
	std::lock_guard<std::mutex> lock(_gamesLock);

	const int itemHeight = 12;
	const int pad = 8;
	const int maxVisible = 10;
	int total = (int)_games.size();
	int sel = _gameSelected.load();
	if(sel >= total) {
		sel = total > 0 ? total - 1 : 0;
	}

	int visible = total < maxVisible ? total : maxVisible;
	int start = 0;
	if(total > maxVisible) {
		start = sel - maxVisible / 2;
		if(start < 0) {
			start = 0;
		}
		if(start > total - maxVisible) {
			start = total - maxVisible;
		}
	}

	int panelW = 220;
	int panelH = pad * 2 + (visible + 1) * itemHeight + 6;
	int x = ((int)width - panelW) / 2;
	int y = ((int)height - panelH) / 2;

	hud->DrawRectangle(x, y, panelW, panelH, QM_BG, true, 1);
	hud->DrawRectangle(x, y, panelW, panelH, QM_ACCENT, false, 1);
	hud->DrawRectangle(x + 1, y + 1, panelW - 2, panelH - 2, QM_ACCENT, false, 1);

	int tx = x + pad;
	int maxTextW = panelW - pad * 2;
	hud->DrawString(tx, y + pad, "LOAD GAME (favorites)", QM_ACCENT, QM_NOBG, 1);

	for(int row = 0; row < visible; row++) {
		int i = start + row;
		int iy = y + pad + (row + 1) * itemHeight + 6;
		//Truncate long names with an ellipsis so they never wrap onto the next line
		string name = TruncateToWidth(_games[i].Name, maxTextW);
		if(i == sel) {
			hud->DrawRectangle(x + 4, iy - 2, panelW - 8, itemHeight, QM_HILITE, true, 1);
			hud->DrawString(tx, iy, name, QM_WHITE, QM_NOBG, 1);
		} else {
			hud->DrawString(tx, iy, name, QM_GRAY, QM_NOBG, 1);
		}
	}
}
