using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mesen.Interop
{
	public enum RaEvent
	{
		LoginSuccess = 1,
		LoginFailed = 2,
		GameReady = 3,
		GameFailed = 4,
		LoggedOut = 5,
		AchievementUnlocked = 6,
		LeaderboardTracker = 7,
		GameCompleted = 8,          //mastery (message: title \x1f badgeUrl)
		ChallengeShow = 9,          //primed achievement appears (message: id \x1f badgeUrl)
		ChallengeHide = 10,         //primed achievement disappears (message: id)
		ProgressShow = 11,          //transient progress indicator (message: badgeUrl \x1f progressText)
		ProgressHide = 12,          //hide transient progress indicator
		LeaderboardScoreboard = 13, //rank received (message: title \x1f score \x1f rank \x1f total)
		//Raised from the C# side (not the core) when hardcore mode is toggled, so open RA windows
		//can refresh — toggling hardcore resets the runtime and changes which achievements/leaderboards
		//count as unlocked/active.
		HardcoreChanged = 100
	}

	public class RaAchievement : ReactiveObject
	{
		public uint Id { get; set; }
		public int State { get; set; }
		public int Points { get; set; }
		public int Percent { get; set; }
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public string Progress { get; set; } = "";
		public string BadgeUrl { get; set; } = "";

		[Reactive] public Bitmap? Badge { get; set; }

		public bool Unlocked => State == 2; //RC_CLIENT_ACHIEVEMENT_STATE_UNLOCKED

		//Measured achievements (e.g. "5/10 enemies") report live progress text + a 0-100 percent.
		public bool HasProgress => !Unlocked && Progress.Length > 0;
		public bool Locked => !Unlocked;
	}

	public class RaLeaderboard
	{
		public uint Id { get; set; }
		public int State { get; set; } //RC_CLIENT_LEADERBOARD_STATE_*
		public int Format { get; set; }
		public bool LowerIsBetter { get; set; }
		public string Title { get; set; } = "";
		public string Description { get; set; } = "";
		public string TrackerValue { get; set; } = "";

		//ACTIVE (1) = armed/available for this session; TRACKING (2) = an attempt is in progress right
		//now, with a live value showing on-screen.
		public bool IsActive => State == 1 || State == 2;
		public bool IsTracking => State == 2;
		public bool IsArmedOnly => State == 1; //available this session, but no attempt in progress
		public bool HasValue => IsTracking && TrackerValue.Length > 0;
	}

	//Bridges rcheevos' server requests (issued by the C++ RaManager) to .NET's HttpClient,
	//which handles HTTPS/TLS to retroachievements.org. Responses are returned to the core
	//via RaDeliverHttpResponse.
	public static class RetroAchievementsApi
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RaHttpRequestCallback(int requestId, IntPtr url, IntPtr postData, IntPtr contentType);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void RaStateCallback(int eventType, IntPtr message);

		//Must keep references so the GC doesn't collect the delegates handed to native code
		private static RaHttpRequestCallback? _callback;
		private static RaStateCallback? _stateCallback;
		private static readonly HttpClient _http = new HttpClient();
		private static bool _initialized;

		//Raised (on the UI thread) when the RA login/game state changes
		public static event Action<RaEvent, string>? StateChanged;

		//Raised (on the UI thread) when an achievement is unlocked, with its badge already loaded
		public static event Action<RaAchievement>? AchievementUnlocked;

		public static void Init()
		{
			if(_initialized) {
				return;
			}
			_initialized = true;

			_http.Timeout = TimeSpan.FromSeconds(30);
			//RetroAchievements requires a unique, stable user agent with a numeric, incrementing version:
			//"EmulatorName/x.y.z (OS ver)"
			string version = EmuApi.GetMesenVersion().ToString(3);
			string os = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Replace("(", "").Replace(")", "").Trim();
			if(os.Length > 60) {
				os = os.Substring(0, 60).Trim();
			}
			try {
				_http.DefaultRequestHeaders.UserAgent.ParseAdd($"MesenOrion/{version} ({os})");
			} catch {
				_http.DefaultRequestHeaders.UserAgent.ParseAdd($"MesenOrion/{version}");
			}

			_callback = OnHttpRequest;
			RaSetHttpCallback(_callback);

			_stateCallback = OnStateChanged;
			RaSetStateCallback(_stateCallback);
		}

		private static void OnStateChanged(int eventType, IntPtr messagePtr)
		{
			string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
			RaEvent ev = (RaEvent)eventType;
			//Marshal back to the UI thread before raising the event
			Dispatcher.UIThread.Post(() => StateChanged?.Invoke(ev, message));

			if(ev == RaEvent.AchievementUnlocked) {
				//message = "title \x1f points \x1f badgeUrl" - load the badge then raise the detailed event
				_ = RaiseAchievementToastAsync(message);
			}
		}

		private static async Task RaiseAchievementToastAsync(string packed)
		{
			string[] f = packed.Split('\x1f');
			RaAchievement ach = new RaAchievement() {
				Title = f.Length > 0 ? f[0] : "",
				Points = f.Length > 1 && int.TryParse(f[1], out int pts) ? pts : 0,
				BadgeUrl = f.Length > 2 ? f[2].Trim() : "",
				State = 2 //unlocked
			};
			EmuApi.WriteLogEntry("[RA] Toast for: " + ach.Title + " | BadgeUrl: '" + ach.BadgeUrl + "'");
			if(ach.BadgeUrl.Length > 0) {
				ach.Badge = await GetBadgeAsync(ach.BadgeUrl).ConfigureAwait(false);
			}
			EmuApi.WriteLogEntry("[RA] Badge loaded: " + (ach.Badge != null ? "YES" : "NO"));
			Dispatcher.UIThread.Post(() => AchievementUnlocked?.Invoke(ach));
		}

		public static List<RaAchievement> GetAchievementList()
		{
			List<RaAchievement> list = new List<RaAchievement>();
			byte[] buffer = new byte[256 * 1024];
			int length = RaGetAchievementList(buffer, buffer.Length);
			if(length > buffer.Length - 1) {
				buffer = new byte[length + 1];
				length = RaGetAchievementList(buffer, buffer.Length);
			}

			string data = Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1));
			foreach(string line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				string[] f = line.Split('\x1f');
				if(f.Length < 7) {
					continue;
				}
				RaAchievement ach = new RaAchievement() {
					Id = uint.TryParse(f[0], out uint id) ? id : 0,
					State = int.TryParse(f[1], out int st) ? st : 0,
					Points = int.TryParse(f[2], out int pt) ? pt : 0,
					Percent = int.TryParse(f[3], out int pc) ? pc : 0,
					Title = f[4],
					Description = f[5],
					Progress = f[6],
					BadgeUrl = f.Length > 7 ? f[7] : ""
				};
				list.Add(ach);
				if(ach.BadgeUrl.Length > 0) {
					_ = LoadBadgeAsync(ach);
				}
			}
			return list;
		}

		private static readonly Dictionary<string, Bitmap> _badgeCache = new();
		private static readonly object _badgeLock = new();

		private static async Task LoadBadgeAsync(RaAchievement ach)
		{
			Bitmap? bmp = await GetBadgeAsync(ach.BadgeUrl).ConfigureAwait(false);
			if(bmp != null) {
				Dispatcher.UIThread.Post(() => ach.Badge = bmp);
			}
		}

		//Downloads (and caches) a badge image. Returns null on error.
		private static async Task<Bitmap?> GetBadgeAsync(string url)
		{
			if(string.IsNullOrEmpty(url)) {
				EmuApi.WriteLogEntry("[RA] Badge URL is empty - no image will be shown");
				return null;
			}
			try {
				lock(_badgeLock) {
					if(_badgeCache.TryGetValue(url, out Bitmap? cached)) {
						return cached;
					}
				}
				EmuApi.WriteLogEntry("[RA] Downloading badge: " + url);
				byte[] bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
				Bitmap bmp = new Bitmap(new MemoryStream(bytes));
				lock(_badgeLock) {
					if(_badgeCache.TryGetValue(url, out Bitmap? existing)) {
						return existing;
					}
					_badgeCache[url] = bmp;
					return bmp;
				}
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[RA] Badge download failed for " + url + " - " + ex.Message);
				return null;
			}
		}

		private static void OnHttpRequest(int requestId, IntPtr urlPtr, IntPtr postPtr, IntPtr ctPtr)
		{
			//Marshal immediately - the native buffers are freed once this returns - then run async
			//so the emulation thread isn't blocked on network I/O.
			string url = Marshal.PtrToStringUTF8(urlPtr) ?? "";
			string post = Marshal.PtrToStringUTF8(postPtr) ?? "";
			string contentType = Marshal.PtrToStringUTF8(ctPtr) ?? "";
			_ = DoRequestAsync(requestId, url, post, contentType);
		}

		private static async Task DoRequestAsync(int requestId, string url, string postData, string contentType)
		{
			try {
				HttpResponseMessage resp;
				if(string.IsNullOrEmpty(postData)) {
					resp = await _http.GetAsync(url).ConfigureAwait(false);
				} else {
					ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(postData));
					if(!string.IsNullOrEmpty(contentType)) {
						try {
							content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
						} catch {
							//Ignore malformed content type, send without it
						}
					}
					resp = await _http.PostAsync(url, content).ConfigureAwait(false);
				}

				byte[] body = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
				Deliver(requestId, (int)resp.StatusCode, body);
			} catch {
				//Network error - report a status of 0 so rcheevos can surface/retry it
				Deliver(requestId, 0, Array.Empty<byte>());
			}
		}

		private static void Deliver(int requestId, int statusCode, byte[] body)
		{
			//Null-terminate for safety (rcheevos uses the explicit length, but be defensive)
			byte[] buffer = new byte[body.Length + 1];
			Array.Copy(body, buffer, body.Length);
			buffer[body.Length] = 0;
			RaDeliverHttpResponse(requestId, statusCode, buffer, body.Length);
		}

		//----- Public API used by the UI -----

		public static void Login(string username, string password) => RaLogin(username, password);
		public static void LoginWithToken(string username, string token) => RaLoginWithToken(username, token);
		public static void Logout() => RaLogout();
		public static bool IsLoggedIn() => RaIsLoggedIn();
		public static void SetHardcoreEnabled(bool enabled)
		{
			RaSetHardcoreEnabled(enabled);
			//Notify open RA windows (achievements/leaderboards) to refresh their state. Posted to the
			//UI thread since this can be called from a background thread during startup.
			Action<RaEvent, string>? handler = StateChanged;
			if(handler != null) {
				Dispatcher.UIThread.Post(() => handler(RaEvent.HardcoreChanged, ""));
			}
		}
		public static bool IsHardcoreEnabled() => RaIsHardcoreEnabled();
		public static void PlaySound() => RaPlaySound();

		public static string GetToken() => ReadString(RaGetToken, 128);
		public static string GetUserDisplayName() => ReadString(RaGetUserDisplayName, 128);
		public static string GetUserAvatarUrl() => ReadString(RaGetUserAvatarUrl, 256);
		public static string GetGameTitle() => ReadString(RaGetGameTitle, 512);
		public static string GetGameImageUrl() => ReadString(RaGetGameImageUrl, 256);
		public static string GetRichPresence() => ReadString(RaGetRichPresence, 512);

		//Returns (hardcore score, softcore score).
		public static (int Score, int Softcore) GetUserScore()
		{
			string[] f = ReadString(RaGetUserScore, 64).Split('\x1f');
			int score = f.Length > 0 && int.TryParse(f[0], out int s) ? s : 0;
			int soft = f.Length > 1 && int.TryParse(f[1], out int sc) ? sc : 0;
			return (score, soft);
		}

		//Downloads the logged-in user's avatar (cached like badges). Returns null on error.
		public static Task<Bitmap?> GetAvatarAsync(string url) => GetBadgeAsync(url);

		//Downloads (and caches) an arbitrary RA image (badge/icon). Returns null on error.
		public static Task<Bitmap?> GetImageAsync(string url) => GetBadgeAsync(url);

		private static string ReadString(Action<byte[], int> fill, int size)
		{
			byte[] buffer = new byte[size];
			fill(buffer, buffer.Length);
			int len = Array.IndexOf(buffer, (byte)0);
			if(len < 0) {
				len = buffer.Length;
			}
			return Encoding.UTF8.GetString(buffer, 0, len);
		}

		public static List<RaLeaderboard> GetLeaderboardList()
		{
			List<RaLeaderboard> list = new List<RaLeaderboard>();
			byte[] buffer = new byte[128 * 1024];
			int length = RaGetLeaderboardList(buffer, buffer.Length);
			if(length > buffer.Length - 1) {
				buffer = new byte[length + 1];
				length = RaGetLeaderboardList(buffer, buffer.Length);
			}

			string data = Encoding.UTF8.GetString(buffer, 0, Math.Min(length, buffer.Length - 1));
			foreach(string line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				string[] f = line.Split('\x1f');
				if(f.Length < 7) {
					continue;
				}
				list.Add(new RaLeaderboard() {
					Id = uint.TryParse(f[0], out uint id) ? id : 0,
					State = int.TryParse(f[1], out int st) ? st : 0,
					Format = int.TryParse(f[2], out int fmt) ? fmt : 0,
					LowerIsBetter = f[3] == "1",
					Title = f[4],
					Description = f[5],
					TrackerValue = f[6]
				});
			}
			return list;
		}

		[DllImport(EmuApi.DllName)] private static extern void RaSetHttpCallback(RaHttpRequestCallback callback);
		[DllImport(EmuApi.DllName)] private static extern void RaSetStateCallback(RaStateCallback callback);
		[DllImport(EmuApi.DllName)] private static extern int RaGetAchievementList(byte[] buffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaDeliverHttpResponse(int requestId, int statusCode, byte[] body, int bodyLength);
		[DllImport(EmuApi.DllName)] private static extern void RaLogin([MarshalAs(UnmanagedType.LPUTF8Str)] string username, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);
		[DllImport(EmuApi.DllName)] private static extern void RaLoginWithToken([MarshalAs(UnmanagedType.LPUTF8Str)] string username, [MarshalAs(UnmanagedType.LPUTF8Str)] string token);
		[DllImport(EmuApi.DllName)] private static extern void RaLogout();
		[DllImport(EmuApi.DllName)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool RaIsLoggedIn();
		[DllImport(EmuApi.DllName)] private static extern void RaGetToken(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetUserDisplayName(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetUserAvatarUrl(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetUserScore(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetGameTitle(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetGameImageUrl(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaGetRichPresence(byte[] outBuffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern int RaGetLeaderboardList(byte[] buffer, int maxLength);
		[DllImport(EmuApi.DllName)] private static extern void RaSetHardcoreEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
		[DllImport(EmuApi.DllName)] private static extern void RaPlaySound();
		[DllImport(EmuApi.DllName)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool RaIsHardcoreEnabled();
	}
}
