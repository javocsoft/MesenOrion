using Mesen.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mesen.Utilities
{
	public class GameStat
	{
		public string Name { get; set; } = "";
		public long TotalSeconds { get; set; }
		public DateTime LastPlayed { get; set; }

		public TimeSpan TotalTime => TimeSpan.FromSeconds(TotalSeconds);
	}

	//Tracks per-game playtime and last-played date, persisted to playtime.json in the Mesen data folder.
	//Session time is wall-clock between a game being loaded and stopped/replaced.
	public static class PlaytimeTracker
	{
		private static readonly object _lock = new();
		private static string FilePath => Path.Combine(ConfigManager.HomeFolder, "playtime.json");

		private static Dictionary<string, GameStat> _stats = Load();
		private static string? _currentGame;
		private static DateTime _sessionStart;

		public static void OnGameStart(string gameName)
		{
			//Flush any in-progress session first (e.g. when switching games)
			OnGameStop();
			if(string.IsNullOrWhiteSpace(gameName)) {
				return;
			}
			_currentGame = gameName;
			_sessionStart = DateTime.Now;
		}

		public static void OnGameStop()
		{
			if(_currentGame == null) {
				return;
			}
			long elapsed = (long)(DateTime.Now - _sessionStart).TotalSeconds;
			string game = _currentGame;
			_currentGame = null;

			if(elapsed <= 0) {
				return;
			}

			lock(_lock) {
				if(!_stats.TryGetValue(game, out GameStat? stat)) {
					stat = new GameStat() { Name = game };
					_stats[game] = stat;
				}
				stat.TotalSeconds += elapsed;
				stat.LastPlayed = DateTime.Now;
				Save();
			}
		}

		//Most recently played first
		public static List<GameStat> GetStats()
		{
			lock(_lock) {
				return _stats.Values.OrderByDescending(s => s.LastPlayed).ToList();
			}
		}

		public static void Clear()
		{
			lock(_lock) {
				_stats.Clear();
				Save();
			}
		}

		private static Dictionary<string, GameStat> Load()
		{
			try {
				if(File.Exists(FilePath)) {
					var data = (Dictionary<string, GameStat>?)JsonSerializer.Deserialize(
						File.ReadAllText(FilePath), typeof(Dictionary<string, GameStat>), MesenSerializerContext.Default);
					if(data != null) {
						return data;
					}
				}
			} catch { }
			return new Dictionary<string, GameStat>();
		}

		private static void Save()
		{
			try {
				File.WriteAllText(FilePath, JsonSerializer.Serialize(_stats, typeof(Dictionary<string, GameStat>), MesenSerializerContext.Default));
			} catch { }
		}
	}
}
