#include "Common.h"
#include <cstring>
#include "Core/Shared/Emulator.h"
#include "Core/Shared/RetroAchievements/RaManager.h"

extern unique_ptr<Emulator> _emu;

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
}
