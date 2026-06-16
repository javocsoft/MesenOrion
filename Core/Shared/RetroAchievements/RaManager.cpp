#include "pch.h"
#include "Shared/RetroAchievements/RaManager.h"
#include "Shared/Emulator.h"
#include "Shared/RomInfo.h"
#include "Shared/SystemActionManager.h"
#include "Shared/MessageManager.h"
#include "Shared/MemoryType.h"
#include "Shared/SettingTypes.h"
#include "rc_consoles.h"
#include "rc_error.h"

RaManager::RaManager(Emulator* emu)
{
	_emu = emu;
	_client = rc_client_create(RaManager::ReadMemoryCallback, RaManager::ServerCallCallback);
	if(_client) {
		rc_client_set_userdata(_client, this);
		rc_client_set_event_handler(_client, RaManager::EventHandlerCallback);
		rc_client_enable_logging(_client, RC_CLIENT_LOG_LEVEL_WARN, RaManager::LogCallback);
		//Hardcore mode is opt-in (off by default)
		rc_client_set_hardcore_enabled(_client, 0);
	}
}

RaManager::~RaManager()
{
	if(_client) {
		rc_client_destroy(_client);
		_client = nullptr;
	}
}

RaManager* RaManager::GetInstance(rc_client_t* client)
{
	return (RaManager*)rc_client_get_userdata(client);
}

//---------------------------------------------------------------------------------------
// Memory mapping (RetroAchievements address space -> Mesen memory blocks)
//---------------------------------------------------------------------------------------

bool RaManager::MapNes(uint32_t addr, MemoryType& type, uint32_t& offset)
{
	if(addr <= 0x07FF) {
		type = MemoryType::NesInternalRam; offset = addr; return true;
	}
	if(addr >= 0x6000 && addr <= 0x7FFF) {
		//Cartridge RAM at $6000-$7FFF: prefer battery save RAM, fall back to work RAM
		ConsoleMemoryInfo save = _emu->GetMemory(MemoryType::NesSaveRam);
		type = (save.Memory != nullptr && save.Size > 0) ? MemoryType::NesSaveRam : MemoryType::NesWorkRam;
		offset = addr - 0x6000;
		return true;
	}
	return false;
}

bool RaManager::MapSnes(uint32_t addr, MemoryType& type, uint32_t& offset)
{
	if(addr <= 0x01FFFF) {
		type = MemoryType::SnesWorkRam; offset = addr; return true;
	}
	if(addr >= 0x020000 && addr <= 0x09FFFF) {
		type = MemoryType::SnesSaveRam; offset = addr - 0x020000; return true;
	}
	if(addr >= 0x0A0000 && addr <= 0x0A07FF) {
		type = MemoryType::Sa1InternalRam; offset = addr - 0x0A0000; return true;
	}
	return false;
}

bool RaManager::MapGameboy(uint32_t addr, MemoryType& type, uint32_t& offset)
{
	if(addr >= 0x8000 && addr <= 0x9FFF) { type = MemoryType::GbVideoRam; offset = addr - 0x8000; return true; }
	if(addr >= 0xA000 && addr <= 0xBFFF) { type = MemoryType::GbCartRam; offset = addr - 0xA000; return true; }
	if(addr >= 0xC000 && addr <= 0xDFFF) { type = MemoryType::GbWorkRam; offset = addr - 0xC000; return true; }
	if(addr >= 0xFE00 && addr <= 0xFE9F) { type = MemoryType::GbSpriteRam; offset = addr - 0xFE00; return true; }
	if(addr >= 0xFF80 && addr <= 0xFFFE) { type = MemoryType::GbHighRam; offset = addr - 0xFF80; return true; }
	//GBC extended banks (system RAM banks 2-7, cartridge RAM banks 1-15)
	if(addr >= 0x10000 && addr <= 0x15FFF) { type = MemoryType::GbWorkRam; offset = 0x2000 + (addr - 0x10000); return true; }
	if(addr >= 0x16000 && addr <= 0x33FFF) { type = MemoryType::GbCartRam; offset = 0x2000 + (addr - 0x16000); return true; }
	return false;
}

bool RaManager::MapGba(uint32_t addr, MemoryType& type, uint32_t& offset)
{
	if(addr <= 0x007FFF) { type = MemoryType::GbaIntWorkRam; offset = addr; return true; }
	if(addr >= 0x008000 && addr <= 0x047FFF) { type = MemoryType::GbaExtWorkRam; offset = addr - 0x008000; return true; }
	if(addr >= 0x048000 && addr <= 0x057FFF) { type = MemoryType::GbaSaveRam; offset = addr - 0x048000; return true; }
	return false;
}

bool RaManager::ReadByte(uint32_t address, uint8_t& value)
{
	MemoryType type;
	uint32_t offset = 0;
	bool mapped = false;

	switch(_emu->GetConsoleType()) {
		case ConsoleType::Nes: mapped = MapNes(address, type, offset); break;
		case ConsoleType::Snes: mapped = MapSnes(address, type, offset); break;
		case ConsoleType::Gameboy: mapped = MapGameboy(address, type, offset); break;
		case ConsoleType::Gba: mapped = MapGba(address, type, offset); break;
		default: return false;
	}

	if(!mapped) {
		return false;
	}

	ConsoleMemoryInfo mem = _emu->GetMemory(type);
	if(mem.Memory == nullptr || offset >= mem.Size) {
		return false;
	}

	value = ((uint8_t*)mem.Memory)[offset];
	return true;
}

uint32_t RaManager::ReadMemoryCallback(uint32_t address, uint8_t* buffer, uint32_t num_bytes, rc_client_t* client)
{
	RaManager* mgr = GetInstance(client);
	if(!mgr) {
		return 0;
	}

	for(uint32_t i = 0; i < num_bytes; i++) {
		uint8_t v = 0;
		mgr->ReadByte(address + i, v); //unmapped addresses read as 0
		buffer[i] = v;
	}
	return num_bytes;
}

//---------------------------------------------------------------------------------------
// HTTP bridge (delegated to the C#/UI layer)
//---------------------------------------------------------------------------------------

void RaManager::ServerCallCallback(const rc_api_request_t* request, rc_client_server_callback_t callback, void* callback_data, rc_client_t* client)
{
	RaManager* mgr = GetInstance(client);
	if(!mgr || !mgr->_httpHandler) {
		//No HTTP transport available - fail the request immediately so rc_client doesn't stall
		rc_api_server_response_t resp = {};
		resp.http_status_code = 0;
		callback(&resp, callback_data);
		return;
	}

	int id;
	{
		auto lock = mgr->_requestLock.AcquireSafe();
		id = mgr->_nextRequestId++;
		mgr->_pendingRequests[id] = { callback, callback_data };
	}

	mgr->_httpHandler(id, request->url,
		request->post_data ? request->post_data : "",
		request->content_type ? request->content_type : "");
}

void RaManager::DeliverHttpResponse(int requestId, int statusCode, const char* body, uint32_t bodyLength)
{
	PendingRequest req;
	{
		auto lock = _requestLock.AcquireSafe();
		auto it = _pendingRequests.find(requestId);
		if(it == _pendingRequests.end()) {
			return;
		}
		req = it->second;
		_pendingRequests.erase(it);
	}

	rc_api_server_response_t resp = {};
	resp.body = body;
	resp.body_length = bodyLength;
	resp.http_status_code = statusCode;
	req.Callback(&resp, req.CallbackData);
}

//---------------------------------------------------------------------------------------
// Events
//---------------------------------------------------------------------------------------

void RaManager::EventHandlerCallback(const rc_client_event_t* event, rc_client_t* client)
{
	RaManager* mgr = GetInstance(client);
	if(mgr) {
		mgr->HandleEvent(event);
	}
}

void RaManager::HandleEvent(const rc_client_event_t* event)
{
	switch(event->type) {
		case RC_CLIENT_EVENT_ACHIEVEMENT_TRIGGERED:
			OnAchievementTriggered(event->achievement);
			break;

		case RC_CLIENT_EVENT_GAME_COMPLETED:
			MessageManager::DisplayMessage("RetroAchievements", "RaGameCompleted");
			break;

		case RC_CLIENT_EVENT_LEADERBOARD_STARTED:
			if(event->leaderboard && event->leaderboard->title) {
				MessageManager::DisplayMessage("RetroAchievements", string("Leaderboard attempt started: ") + event->leaderboard->title);
			}
			break;

		case RC_CLIENT_EVENT_LEADERBOARD_FAILED:
			if(event->leaderboard && event->leaderboard->title) {
				MessageManager::DisplayMessage("RetroAchievements", string("Leaderboard attempt failed: ") + event->leaderboard->title);
			}
			break;

		case RC_CLIENT_EVENT_LEADERBOARD_SUBMITTED:
			if(event->leaderboard && event->leaderboard->title) {
				string value = event->leaderboard->tracker_value ? event->leaderboard->tracker_value : "";
				MessageManager::DisplayMessage("RetroAchievements", string("Submitted ") + value + " to " + event->leaderboard->title);
			}
			break;

		case RC_CLIENT_EVENT_LEADERBOARD_TRACKER_SHOW:
		case RC_CLIENT_EVENT_LEADERBOARD_TRACKER_UPDATE:
			if(event->leaderboard_tracker) {
				_leaderboardTrackers[event->leaderboard_tracker->id] = event->leaderboard_tracker->display;
				UpdateLeaderboardTrackerDisplay();
			}
			break;

		case RC_CLIENT_EVENT_LEADERBOARD_TRACKER_HIDE:
			if(event->leaderboard_tracker) {
				_leaderboardTrackers.erase(event->leaderboard_tracker->id);
				UpdateLeaderboardTrackerDisplay();
			}
			break;

		case RC_CLIENT_EVENT_RESET:
			//Raised when hardcore is enabled mid-session - reset the game to prevent cheating
			if(_emu->GetSystemActionManager()) {
				_emu->GetSystemActionManager()->Reset();
			}
			break;

		case RC_CLIENT_EVENT_SERVER_ERROR:
			//An API request failed and will not be retried (e.g. an unlock could not be saved)
			if(event->server_error && event->server_error->error_message) {
				MessageManager::DisplayMessage("RetroAchievements", string("Server error: ") + event->server_error->error_message);
			}
			break;

		case RC_CLIENT_EVENT_DISCONNECTED:
			MessageManager::DisplayMessage("RetroAchievements", "Connection lost - unlocks are pending");
			break;

		case RC_CLIENT_EVENT_RECONNECTED:
			MessageManager::DisplayMessage("RetroAchievements", "Reconnected - pending unlocks sent");
			break;

		default:
			break;
	}
}

void RaManager::UpdateLeaderboardTrackerDisplay()
{
	//Combine all active leaderboard trackers into one string for the on-screen overlay (empty = hide)
	string combined;
	for(auto& kv : _leaderboardTrackers) {
		if(!combined.empty()) {
			combined += "\n";
		}
		combined += kv.second;
	}
	NotifyState(RaUiEvent::RaLeaderboardTracker, combined);
}

void RaManager::OnAchievementTriggered(const rc_client_achievement_t* ach)
{
	if(!ach) {
		return;
	}
	//Notify the UI (it shows a toast with the badge, refreshes an open list, and plays the sound).
	//Payload: title <0x1F> points <0x1F> badge URL
	const char sep = '\x1f';
	string payload = string(ach->title ? ach->title : "");
	payload += sep;
	payload += std::to_string((int)ach->points);
	payload += sep;
	// Use the API to get the URL - this handles the case where badge_url is null
	// and falls back to building the URL from badge_name.
	char badgeUrlBuf[512] = {};
	if(rc_client_achievement_get_image_url(ach, RC_CLIENT_ACHIEVEMENT_STATE_UNLOCKED, badgeUrlBuf, sizeof(badgeUrlBuf)) == RC_OK) {
		payload += badgeUrlBuf;
	} else {
		payload += (ach->badge_url ? ach->badge_url : "");
	}
	NotifyState(RaUiEvent::RaAchievementUnlocked, payload);
}

void RaManager::LogCallback(const char* message, const rc_client_t* client)
{
	MessageManager::Log(string("[RA] ") + message);
}

//---------------------------------------------------------------------------------------
// Account / session
//---------------------------------------------------------------------------------------

void RaManager::NotifyState(int eventType, const string& message)
{
	if(_stateHandler) {
		_stateHandler(eventType, message.c_str());
	}
}

void RaManager::LoginCallback(int result, const char* error_message, rc_client_t* client, void* userdata)
{
	RaManager* mgr = GetInstance(client);
	if(result == RC_OK) {
		const rc_client_user_t* user = rc_client_get_user_info(client);
		string name = user && user->display_name ? user->display_name : "";
		MessageManager::DisplayMessage("RetroAchievements", string("Logged in as ") + name);
		if(mgr) {
			mgr->NotifyState(RaUiEvent::RaLoginSuccess, name);
			//If a game is already running (e.g. logging in mid-session, or after re-login),
			//(re)load its achievements now - they wouldn't load otherwise until the next ROM load.
			mgr->LoadGame();
		}
	} else {
		string err = error_message ? error_message : "";
		MessageManager::DisplayMessage("RetroAchievements", string("Login failed: ") + err);
		if(mgr) {
			mgr->NotifyState(RaUiEvent::RaLoginFailed, err);
		}
	}
}

void RaManager::LoadGameCallback(int result, const char* error_message, rc_client_t* client, void* userdata)
{
	RaManager* mgr = GetInstance(client);
	if(result == RC_OK) {
		if(mgr) {
			mgr->_gameLoaded = true;
		}
		const rc_client_game_t* game = rc_client_get_game_info(client);
		string title = game && game->title ? game->title : "Game loaded";
		MessageManager::DisplayMessage("RetroAchievements", title);
		if(mgr) {
			mgr->NotifyState(RaUiEvent::RaGameReady, title);
		}
	} else if(result == RC_NO_GAME_LOADED) {
		//No achievements for this game
		if(mgr) {
			mgr->NotifyState(RaUiEvent::RaGameFailed, "No achievements for this game");
		}
	} else {
		string err = error_message ? error_message : "";
		MessageManager::DisplayMessage("RetroAchievements", string("Load failed: ") + err);
		if(mgr) {
			mgr->NotifyState(RaUiEvent::RaGameFailed, err);
		}
	}
}

void RaManager::Login(const string& username, const string& password)
{
	if(_client) {
		rc_client_begin_login_with_password(_client, username.c_str(), password.c_str(), RaManager::LoginCallback, this);
	}
}

void RaManager::LoginWithToken(const string& username, const string& token)
{
	if(_client) {
		rc_client_begin_login_with_token(_client, username.c_str(), token.c_str(), RaManager::LoginCallback, this);
	}
}

void RaManager::Logout()
{
	if(_client) {
		rc_client_logout(_client);
		_gameLoaded = false;
		NotifyState(RaUiEvent::RaLoggedOut, "");
	}
}

//Serializes the loaded game's achievements as records (one per line), with fields separated
//by the unit-separator char (0x1F): id, state, points, percent, title, description, progress.
static string RaSanitize(const char* text)
{
	string out = text ? text : "";
	for(char& c : out) {
		if(c == '\n' || c == '\r' || c == '\x1f') {
			c = ' ';
		}
	}
	return out;
}

string RaManager::GetAchievementListData()
{
	string result;
	if(!_client || !_gameLoaded) {
		return result;
	}

	rc_client_achievement_list_t* list = rc_client_create_achievement_list(_client,
		RC_CLIENT_ACHIEVEMENT_CATEGORY_CORE, RC_CLIENT_ACHIEVEMENT_LIST_GROUPING_PROGRESS);
	if(!list) {
		return result;
	}

	const char sep = '\x1f';
	for(uint32_t b = 0; b < list->num_buckets; b++) {
		const rc_client_achievement_bucket_t& bucket = list->buckets[b];
		for(uint32_t a = 0; a < bucket.num_achievements; a++) {
			const rc_client_achievement_t* ach = bucket.achievements[a];
			if(!ach) {
				continue;
			}
			const char* badge = (ach->state == RC_CLIENT_ACHIEVEMENT_STATE_UNLOCKED) ? ach->badge_url : ach->badge_locked_url;
			result += std::to_string(ach->id); result += sep;
			result += std::to_string((int)ach->state); result += sep;
			result += std::to_string((int)ach->points); result += sep;
			result += std::to_string((int)ach->measured_percent); result += sep;
			result += RaSanitize(ach->title); result += sep;
			result += RaSanitize(ach->description); result += sep;
			result += RaSanitize(ach->measured_progress); result += sep;
			result += (badge ? badge : "");
			result += '\n';
		}
	}

	rc_client_destroy_achievement_list(list);
	return result;
}

bool RaManager::IsLoggedIn()
{
	return _client && rc_client_get_user_info(_client) != nullptr;
}

string RaManager::GetLoginToken()
{
	if(_client) {
		const rc_client_user_t* user = rc_client_get_user_info(_client);
		if(user && user->token) {
			return user->token;
		}
	}
	return "";
}

void RaManager::LoadGame()
{
	if(!_client || !IsLoggedIn()) {
		return;
	}
	UnloadGame();

	uint32_t consoleId = 0;
	switch(_emu->GetConsoleType()) {
		case ConsoleType::Nes: consoleId = RC_CONSOLE_NINTENDO; break;
		case ConsoleType::Snes: consoleId = RC_CONSOLE_SUPER_NINTENDO; break;
		case ConsoleType::Gba: consoleId = RC_CONSOLE_GAMEBOY_ADVANCE; break;
		case ConsoleType::Gameboy: consoleId = RC_CONSOLE_GAMEBOY; break;
		default: return; //console not supported yet
	}

	vector<uint8_t>& romData = _emu->GetRomInfo().RomFile.GetData();
	if(romData.empty()) {
		return;
	}

	//Detect Game Boy Color from the cartridge header (byte $143)
	if(consoleId == RC_CONSOLE_GAMEBOY && romData.size() > 0x143 && (romData[0x143] & 0x80)) {
		consoleId = RC_CONSOLE_GAMEBOY_COLOR;
	}
	_consoleId = consoleId;

	rc_client_begin_identify_and_load_game(_client, consoleId, nullptr, romData.data(), romData.size(), RaManager::LoadGameCallback, this);
}

void RaManager::UnloadGame()
{
	if(_client && _gameLoaded) {
		rc_client_unload_game(_client);
		_gameLoaded = false;
		if(!_leaderboardTrackers.empty()) {
			_leaderboardTrackers.clear();
			UpdateLeaderboardTrackerDisplay();
		}
	}
}

void RaManager::SetHardcoreEnabled(bool enabled)
{
	if(_client) {
		rc_client_set_hardcore_enabled(_client, enabled ? 1 : 0);
	}
}

bool RaManager::IsHardcoreEnabled()
{
	return _client && rc_client_get_hardcore_enabled(_client) != 0;
}

bool RaManager::AreRestrictionsActive()
{
	return _client && _gameLoaded && rc_client_get_hardcore_enabled(_client) != 0;
}

void RaManager::DropToSoftcoreForResume()
{
	//RetroAchievements requires that resuming a session from a save state drops to softcore.
	if(_client && rc_client_get_hardcore_enabled(_client)) {
		rc_client_set_hardcore_enabled(_client, 0);
		MessageManager::DisplayMessage("RetroAchievements", "RaResumeSoftcore");
	}
}

void RaManager::ProcessFrame()
{
	if(!_client) {
		return;
	}
	if(_gameLoaded) {
		rc_client_do_frame(_client);
	} else {
		rc_client_idle(_client);
	}
}
