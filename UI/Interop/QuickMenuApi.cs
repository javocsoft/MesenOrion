using Avalonia.Threading;
using Mesen.Utilities;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mesen.Interop
{
	//Bridges the on-screen quick menu's "Load Game" list (drawn by the core) with the UI: the UI
	//pushes the favorite games to the core, and the core calls back here when one is picked.
	public static class QuickMenuApi
	{
		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void QuickMenuLoadCallback(IntPtr path);

		private static QuickMenuLoadCallback? _loadCallback;

		//Raised (on the UI thread) when the user picks a game in the quick menu
		public static event Action<string>? LoadRequested;

		public static void Init()
		{
			_loadCallback = OnLoadRequested;
			QuickMenuSetLoadCallback(_loadCallback);
		}

		private static void OnLoadRequested(IntPtr pathPtr)
		{
			string path = Marshal.PtrToStringUTF8(pathPtr) ?? "";
			if(path.Length > 0) {
				Dispatcher.UIThread.Post(() => LoadRequested?.Invoke(path));
			}
		}

		//Pushes the current favorites to the core as "name \x1f path" records, one per line.
		public static void SetFavorites()
		{
			StringBuilder sb = new StringBuilder();
			foreach((string name, string path) in GameLibrary.GetFavorites()) {
				sb.Append(name);
				sb.Append('\x1f');
				sb.Append(path);
				sb.Append('\n');
			}
			QuickMenuSetGames(sb.ToString());
		}

		[DllImport(EmuApi.DllName)] private static extern void QuickMenuSetGames([MarshalAs(UnmanagedType.LPUTF8Str)] string data);
		[DllImport(EmuApi.DllName)] private static extern void QuickMenuSetLoadCallback(QuickMenuLoadCallback callback);
	}
}
