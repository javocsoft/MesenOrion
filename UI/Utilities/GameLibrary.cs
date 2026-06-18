using Avalonia.Media.Imaging;
using Mesen.Config;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Mesen.Utilities
{
	//Persisted library state: folders to scan, favorites, and the card size (1x-3x).
	public class GameLibraryData
	{
		public List<string> Folders { get; set; } = new();
		public List<string> Favorites { get; set; } = new();
		public int CardScale { get; set; } = 1;
	}

	//A scanned game as stored in the on-disk cache (no images, just metadata).
	public class CachedGame
	{
		public string Path { get; set; } = "";
		public string Name { get; set; } = "";
		public string System { get; set; } = "";
		public string Icon { get; set; } = "";
	}

	//A game shown in the library (display model).
	public class LibraryGame : ReactiveObject
	{
		public string Path { get; set; } = "";
		public string Name { get; set; } = "";
		public string System { get; set; } = "";
		public string Icon { get; set; } = "";

		//Starts as the system icon; replaced by the game's captured cover/screenshot once loaded.
		[Reactive] public Bitmap? IconImage { get; set; }
		[Reactive] public bool HasCover { get; set; }
		[Reactive] public bool IsFavorite { get; set; }

		//Set once we've attempted to load this game's cover (used with lazy/virtualized loading).
		public bool CoverChecked { get; set; }
	}

	public static class GameLibrary
	{
		private static string FilePath => System.IO.Path.Combine(ConfigManager.HomeFolder, "library.json");
		private static string CachePath => System.IO.Path.Combine(ConfigManager.HomeFolder, "library-cache.json");

		private static GameLibraryData _data = Load();
		private static List<CachedGame>? _cache;

		//Maps a file extension (lowercase, no dot) to a system label + icon asset.
		private static readonly Dictionary<string, (string System, string Icon)> _systems = new() {
			{ "nes", ("NES", "NesIcon") }, { "fds", ("NES", "NesIcon") }, { "unif", ("NES", "NesIcon") },
			{ "unf", ("NES", "NesIcon") }, { "qd", ("NES", "NesIcon") }, { "studybox", ("NES", "NesIcon") },
			{ "nsf", ("NES", "NesIcon") }, { "nsfe", ("NES", "NesIcon") },
			{ "sfc", ("SNES", "SnesIcon") }, { "smc", ("SNES", "SnesIcon") }, { "fig", ("SNES", "SnesIcon") },
			{ "bs", ("SNES", "SnesIcon") }, { "st", ("SNES", "SnesIcon") }, { "spc", ("SNES", "SnesIcon") },
			{ "gb", ("Game Boy", "GameboyIcon") }, { "gbc", ("Game Boy", "GameboyIcon") },
			{ "gbx", ("Game Boy", "GameboyIcon") }, { "gbs", ("Game Boy", "GameboyIcon") },
			{ "gba", ("Game Boy Advance", "GbaIcon") },
			{ "pce", ("PC Engine", "PceIcon") }, { "sgx", ("PC Engine", "PceIcon") }, { "hes", ("PC Engine", "PceIcon") },
			{ "sms", ("Master System", "SmsIcon") }, { "gg", ("Game Gear", "SmsIcon") },
			{ "sg", ("SG-1000", "Drive") }, { "col", ("ColecoVision", "Drive") },
			{ "ws", ("WonderSwan", "WsIcon") }, { "wsc", ("WonderSwan", "WsIcon") },
		};
		//Archive extensions are scanned too, but their system is detected from their contents.
		private static readonly HashSet<string> _archiveExts = new() { "zip", "7z" };

		private static readonly Dictionary<string, Bitmap> _iconCache = new();

		private static Bitmap? GetIcon(string icon)
		{
			if(!_iconCache.TryGetValue(icon, out var bmp)) {
				try {
					bmp = ImageUtilities.BitmapFromAsset("Assets/" + icon + ".png");
				} catch {
					bmp = null;
				}
				if(bmp != null) {
					_iconCache[icon] = bmp;
				}
			}
			return bmp;
		}

		public static IReadOnlyList<string> Folders => _data.Folders;

		public static int CardScale
		{
			get => Math.Clamp(_data.CardScale, 1, 3);
			set { _data.CardScale = Math.Clamp(value, 1, 3); Save(); }
		}

		public static void AddFolder(string folder)
		{
			if(!string.IsNullOrWhiteSpace(folder) && !_data.Folders.Contains(folder)) {
				_data.Folders.Add(folder);
				Save();
			}
		}

		public static void RemoveFolder(string folder)
		{
			if(_data.Folders.Remove(folder)) {
				Save();
			}
		}

		public static void ToggleFavorite(LibraryGame game)
		{
			if(_data.Favorites.Contains(game.Path)) {
				_data.Favorites.Remove(game.Path);
				game.IsFavorite = false;
			} else {
				_data.Favorites.Add(game.Path);
				game.IsFavorite = true;
			}
			Save();
		}

		//Returns the games from the cache (fast). Triggers a rescan the first time if there's no cache.
		public static List<LibraryGame> GetGames()
		{
			_cache ??= LoadCache();
			if(_cache.Count == 0 && _data.Folders.Count > 0 && !File.Exists(CachePath)) {
				return Rescan();
			}
			return _cache.Select(ToLibraryGame).ToList();
		}

		//Re-scans the configured folders, rebuilds + persists the cache, and returns the games.
		public static List<LibraryGame> Rescan()
		{
			_cache = ScanFolders();
			SaveCache();
			return _cache.Select(ToLibraryGame).ToList();
		}

		private static LibraryGame ToLibraryGame(CachedGame c) => new() {
			Path = c.Path,
			Name = c.Name,
			System = c.System,
			Icon = c.Icon,
			IconImage = GetIcon(c.Icon),
			IsFavorite = _data.Favorites.Contains(c.Path)
		};

		private static List<CachedGame> ScanFolders()
		{
			List<CachedGame> games = new();
			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

			foreach(string folder in _data.Folders) {
				if(!Directory.Exists(folder)) {
					continue;
				}
				IEnumerable<string> files;
				try {
					files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories);
				} catch {
					continue;
				}

				foreach(string file in files) {
					string ext = System.IO.Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
					string system;
					string icon;

					if(_systems.TryGetValue(ext, out var info)) {
						(system, icon) = info;
					} else if(_archiveExts.Contains(ext)) {
						//Detect the real console from the archive's contents (.zip only); else "Archive"
						var inner = ext == "zip" ? DetectArchiveSystem(file) : null;
						(system, icon) = inner ?? ("Archive", "Drive");
					} else {
						continue;
					}

					if(!seen.Add(file)) {
						continue;
					}
					games.Add(new CachedGame() {
						Path = file,
						Name = System.IO.Path.GetFileNameWithoutExtension(file),
						System = system,
						Icon = icon
					});
				}
			}

			return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		//Peeks at a .zip's entry names (no decompression) to map it to a console.
		private static (string System, string Icon)? DetectArchiveSystem(string path)
		{
			try {
				using FileStream fs = File.OpenRead(path);
				using ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read);
				foreach(ZipArchiveEntry entry in zip.Entries) {
					string iext = System.IO.Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant();
					if(_systems.TryGetValue(iext, out var info)) {
						return info;
					}
				}
			} catch { }
			return null;
		}

		//Loads a game's cover image: the captured cover (Screenshot.png inside its .rgd), falling back
		//to the most recent normal screenshot for that game. Returns null if neither exists.
		public static Bitmap? TryLoadCover(string gameName)
		{
			try {
				string rgd = System.IO.Path.Combine(ConfigManager.RecentGamesFolder, gameName + ".rgd");
				if(File.Exists(rgd)) {
					using FileStream fs = File.OpenRead(rgd);
					using ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read);
					ZipArchiveEntry? entry = zip.GetEntry("Screenshot.png");
					if(entry != null) {
						using Stream s = entry.Open();
						using MemoryStream ms = new MemoryStream();
						s.CopyTo(ms);
						ms.Seek(0, SeekOrigin.Begin);
						return new Bitmap(ms);
					}
				}
			} catch { }

			try {
				string dir = ConfigManager.ScreenshotFolder;
				if(Directory.Exists(dir)) {
					string? shot = Directory.EnumerateFiles(dir, gameName + "_*.png")
						.OrderByDescending(f => File.GetLastWriteTime(f))
						.FirstOrDefault();
					if(shot != null) {
						using FileStream fs = File.OpenRead(shot);
						return Bitmap.DecodeToWidth(fs, 320);
					}
				}
			} catch { }

			return null;
		}

		private static GameLibraryData Load()
		{
			try {
				if(File.Exists(FilePath)) {
					var data = (GameLibraryData?)JsonSerializer.Deserialize(
						File.ReadAllText(FilePath), typeof(GameLibraryData), MesenSerializerContext.Default);
					if(data != null) {
						return data;
					}
				}
			} catch { }
			return new GameLibraryData();
		}

		private static void Save()
		{
			try {
				File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, typeof(GameLibraryData), MesenSerializerContext.Default));
			} catch { }
		}

		private static List<CachedGame> LoadCache()
		{
			try {
				if(File.Exists(CachePath)) {
					var data = (List<CachedGame>?)JsonSerializer.Deserialize(
						File.ReadAllText(CachePath), typeof(List<CachedGame>), MesenSerializerContext.Default);
					if(data != null) {
						return data;
					}
				}
			} catch { }
			return new List<CachedGame>();
		}

		private static void SaveCache()
		{
			try {
				File.WriteAllText(CachePath, JsonSerializer.Serialize(_cache, typeof(List<CachedGame>), MesenSerializerContext.Default));
			} catch { }
		}
	}
}
