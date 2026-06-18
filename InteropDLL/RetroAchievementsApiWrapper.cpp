#include "Common.h"
#include <cstring>
#include "Core/Shared/Emulator.h"
#include "Core/Shared/RetroAchievements/RaManager.h"

#if !defined(_WIN32) || defined(USE_SDL_BACKEND)
	#include <SDL.h>
	#include <thread>
	#include <vector>
	#include <cmath>
#elif defined(_WIN32)
	#include <windows.h>
#endif

extern unique_ptr<Emulator> _emu;

//Plays a short two-tone "achievement unlocked" chime. Uses SDL (already linked) on
//Linux/macOS; falls back to a system beep on the native Windows build (no SDL).
static void RaPlayChime()
{
#if !defined(_WIN32) || defined(USE_SDL_BACKEND)
	std::thread([]() {
		if(SDL_WasInit(SDL_INIT_AUDIO) == 0 && SDL_InitSubSystem(SDL_INIT_AUDIO) != 0) {
			return;
		}

		const double PI = 3.14159265358979323846;
		const int freq = 48000;
		const int durationMs = 300;
		int sampleCount = freq * durationMs / 1000;
		std::vector<int16_t> buffer(sampleCount);

		const double splitPoint = 0.4; //first tone for 40% of the duration, second tone after
		for(int i = 0; i < sampleCount; i++) {
			double t = (double)i / freq;
			double progress = (double)i / sampleCount;
			double toneFreq = progress < splitPoint ? 987.77 : 1318.51; //B5 then E6
			double localProgress = progress < splitPoint ? (progress / splitPoint) : ((progress - splitPoint) / (1.0 - splitPoint));
			double envelope = exp(-3.5 * localProgress);
			double sample = sin(2.0 * PI * toneFreq * t) * envelope * 0.28;
			buffer[i] = (int16_t)(sample * 32767);
		}

		SDL_AudioSpec want;
		SDL_zero(want);
		want.freq = freq;
		want.format = AUDIO_S16SYS;
		want.channels = 1;
		want.samples = 1024;

		SDL_AudioDeviceID dev = SDL_OpenAudioDevice(nullptr, 0, &want, nullptr, 0);
		if(dev == 0) {
			return;
		}
		SDL_QueueAudio(dev, buffer.data(), (Uint32)(buffer.size() * sizeof(int16_t)));
		SDL_PauseAudioDevice(dev, 0);
		while(SDL_GetQueuedAudioSize(dev) > 0) {
			SDL_Delay(20);
		}
		SDL_Delay(50);
		SDL_CloseAudioDevice(dev);
	}).detach();
#elif defined(_WIN32)
	MessageBeep(MB_OK);
#endif
}

extern "C"
{
	//Registers the C# delegate that performs the actual HTTP requests (TLS handled by HttpClient).
	DllExport void __stdcall RaSetHttpCallback(RaHttpRequestHandler callback)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->SetHttpHandler(callback);
		}
	}

	//Registers the C# delegate that receives RA state changes (login result, game session, etc.).
	DllExport void __stdcall RaSetStateCallback(RaStateChangedHandler callback)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->SetStateHandler(callback);
		}
	}

	//Fills 'buffer' with the serialized achievement list and returns the full length (so the
	//caller can resize and retry if it was truncated).
	DllExport int __stdcall RaGetAchievementList(char* buffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		string data = ra ? ra->GetAchievementListData() : "";
		if(buffer && maxLength > 0) {
			int copyLen = (int)data.size();
			if(copyLen > maxLength - 1) {
				copyLen = maxLength - 1;
			}
			memcpy(buffer, data.data(), copyLen);
			buffer[copyLen] = '\0';
		}
		return (int)data.size();
	}

	//Called by C# once an HTTP response is available for a request previously handed to the callback.
	DllExport void __stdcall RaDeliverHttpResponse(int requestId, int statusCode, const char* body, int bodyLength)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->DeliverHttpResponse(requestId, statusCode, body, (uint32_t)(bodyLength < 0 ? 0 : bodyLength));
		}
	}

	DllExport void __stdcall RaLogin(const char* username, const char* password)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->Login(username ? username : "", password ? password : "");
		}
	}

	DllExport void __stdcall RaLoginWithToken(const char* username, const char* token)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->LoginWithToken(username ? username : "", token ? token : "");
		}
	}

	DllExport void __stdcall RaLogout()
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->Logout();
		}
	}

	DllExport bool __stdcall RaIsLoggedIn()
	{
		RaManager* ra = _emu->GetRaManager();
		return ra && ra->IsLoggedIn();
	}

	//Copies the current login token (used to persist credentials without storing the password).
	DllExport void __stdcall RaGetToken(char* outBuffer, int maxLength)
	{
		if(!outBuffer || maxLength <= 0) {
			return;
		}
		RaManager* ra = _emu->GetRaManager();
		string token = ra ? ra->GetLoginToken() : "";
		strncpy(outBuffer, token.c_str(), maxLength - 1);
		outBuffer[maxLength - 1] = '\0';
	}

	//Plays the achievement-unlocked sound (gating by config is done on the C# side)
	DllExport void __stdcall RaPlaySound()
	{
		RaPlayChime();
	}

	DllExport void __stdcall RaSetHardcoreEnabled(bool enabled)
	{
		RaManager* ra = _emu->GetRaManager();
		if(ra) {
			ra->SetHardcoreEnabled(enabled);
		}
	}

	DllExport bool __stdcall RaIsHardcoreEnabled()
	{
		RaManager* ra = _emu->GetRaManager();
		return ra && ra->IsHardcoreEnabled();
	}

	//Copies the logged-in user's display name.
	DllExport void __stdcall RaGetUserDisplayName(char* outBuffer, int maxLength)
	{
		if(!outBuffer || maxLength <= 0) {
			return;
		}
		RaManager* ra = _emu->GetRaManager();
		string name = ra ? ra->GetUserDisplayName() : "";
		strncpy(outBuffer, name.c_str(), maxLength - 1);
		outBuffer[maxLength - 1] = '\0';
	}

	//Copies the logged-in user's avatar image URL.
	DllExport void __stdcall RaGetUserAvatarUrl(char* outBuffer, int maxLength)
	{
		if(!outBuffer || maxLength <= 0) {
			return;
		}
		RaManager* ra = _emu->GetRaManager();
		string url = ra ? ra->GetUserAvatarUrl() : "";
		strncpy(outBuffer, url.c_str(), maxLength - 1);
		outBuffer[maxLength - 1] = '\0';
	}

	//Fills 'buffer' with the serialized leaderboard list and returns the full length (so the
	//caller can resize and retry if it was truncated).
	DllExport int __stdcall RaGetLeaderboardList(char* buffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		string data = ra ? ra->GetLeaderboardListData() : "";
		if(buffer && maxLength > 0) {
			int copyLen = (int)data.size();
			if(copyLen > maxLength - 1) {
				copyLen = maxLength - 1;
			}
			memcpy(buffer, data.data(), copyLen);
			buffer[copyLen] = '\0';
		}
		return (int)data.size();
	}

	//Shared helper: copies a std::string into a caller-provided buffer (null-terminated, truncated).
	static void RaCopyString(const string& src, char* outBuffer, int maxLength)
	{
		if(!outBuffer || maxLength <= 0) {
			return;
		}
		int copyLen = (int)src.size();
		if(copyLen > maxLength - 1) {
			copyLen = maxLength - 1;
		}
		memcpy(outBuffer, src.data(), copyLen);
		outBuffer[copyLen] = '\0';
	}

	//Copies the logged-in user's score as "hardcoreScore <0x1F> softcoreScore".
	DllExport void __stdcall RaGetUserScore(char* outBuffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		RaCopyString(ra ? ra->GetUserScore() : "", outBuffer, maxLength);
	}

	//Copies the current game's title.
	DllExport void __stdcall RaGetGameTitle(char* outBuffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		RaCopyString(ra ? ra->GetGameTitle() : "", outBuffer, maxLength);
	}

	//Copies the current game's icon URL.
	DllExport void __stdcall RaGetGameImageUrl(char* outBuffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		RaCopyString(ra ? ra->GetGameImageUrl() : "", outBuffer, maxLength);
	}

	//Copies the current rich presence message (empty if unavailable).
	DllExport void __stdcall RaGetRichPresence(char* outBuffer, int maxLength)
	{
		RaManager* ra = _emu->GetRaManager();
		RaCopyString(ra ? ra->GetRichPresence() : "", outBuffer, maxLength);
	}
}
