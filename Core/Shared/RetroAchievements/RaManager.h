#pragma once
#include "pch.h"
#include <unordered_map>
#include "rc_client.h"
#include "Utilities/SimpleLock.h"

class Emulator;
enum class MemoryType;

//Function the UI layer registers to perform the actual HTTP request. TLS/HTTP is handled
//in C# (HttpClient); once the response is ready it must be returned via DeliverHttpResponse.
typedef void (*RaHttpRequestHandler)(int requestId, const char* url, const char* postData, const char* contentType);

//State changes reported to the UI (login result, game session ready, etc.)
enum RaUiEvent
{
	RaLoginSuccess = 1,
	RaLoginFailed = 2,
	RaGameReady = 3,
	RaGameFailed = 4,
	RaLoggedOut = 5
};
typedef void (*RaStateChangedHandler)(int eventType, const char* message);

//rcheevos integration manager (RetroAchievements). Owns the rc_client, feeds it the
//emulator memory each frame, and bridges its server requests to the C#/UI HTTP layer.
class RaManager
{
private:
	Emulator* _emu;
	rc_client_t* _client = nullptr;
	bool _gameLoaded = false;
	uint32_t _consoleId = 0;

	RaHttpRequestHandler _httpHandler = nullptr;
	RaStateChangedHandler _stateHandler = nullptr;

	void NotifyState(int eventType, const string& message);

	struct PendingRequest
	{
		rc_client_server_callback_t Callback;
		void* CallbackData;
	};
	std::unordered_map<int, PendingRequest> _pendingRequests;
	int _nextRequestId = 1;
	SimpleLock _requestLock;

	//Per-console memory mapping (RetroAchievements address space -> Mesen memory blocks)
	bool ReadByte(uint32_t address, uint8_t& value);
	bool MapNes(uint32_t addr, MemoryType& type, uint32_t& offset);
	bool MapSnes(uint32_t addr, MemoryType& type, uint32_t& offset);
	bool MapGameboy(uint32_t addr, MemoryType& type, uint32_t& offset);
	bool MapGba(uint32_t addr, MemoryType& type, uint32_t& offset);

	void OnAchievementTriggered(const rc_client_achievement_t* ach);
	void HandleEvent(const rc_client_event_t* event);

	//Static C callbacks for rc_client
	static uint32_t ReadMemoryCallback(uint32_t address, uint8_t* buffer, uint32_t num_bytes, rc_client_t* client);
	static void ServerCallCallback(const rc_api_request_t* request, rc_client_server_callback_t callback, void* callback_data, rc_client_t* client);
	static void EventHandlerCallback(const rc_client_event_t* event, rc_client_t* client);
	static void LogCallback(const char* message, const rc_client_t* client);
	static void LoginCallback(int result, const char* error_message, rc_client_t* client, void* userdata);
	static void LoadGameCallback(int result, const char* error_message, rc_client_t* client, void* userdata);

	static RaManager* GetInstance(rc_client_t* client);

public:
	RaManager(Emulator* emu);
	~RaManager();

	//HTTP bridge (used by the C#/UI layer)
	void SetHttpHandler(RaHttpRequestHandler handler) { _httpHandler = handler; }
	void DeliverHttpResponse(int requestId, int statusCode, const char* body, uint32_t bodyLength);

	//UI state notifications + data
	void SetStateHandler(RaStateChangedHandler handler) { _stateHandler = handler; }
	string GetAchievementListData();

	//Account
	void Login(const string& username, const string& password);
	void LoginWithToken(const string& username, const string& token);
	void Logout();
	bool IsLoggedIn();
	string GetLoginToken();

	//Game session
	void LoadGame();
	void UnloadGame();

	//Hardcore mode
	void SetHardcoreEnabled(bool enabled);
	bool IsHardcoreEnabled();
	//True when hardcore restrictions (no save states/rewind/cheats/speed) should be enforced:
	//hardcore enabled AND an achievement-tracked game session is active.
	bool AreRestrictionsActive();

	//Called once per emulated frame from the emulation thread
	void ProcessFrame();
};
