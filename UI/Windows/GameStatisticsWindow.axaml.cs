using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Windows
{
	public class GameStatRow
	{
		public string Name { get; set; } = "";
		public string PlaytimeText { get; set; } = "";
		public string LastPlayedText { get; set; } = "";
	}

	public class GameStatisticsWindow : MesenWindow
	{
		private ItemsControl _list = null!;
		private TextBlock _summary = null!;
		private TextBlock _empty = null!;

		public GameStatisticsWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstStats");
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");

			this.GetControl<Button>("btnClear").Click += (s, e) => {
				PlaytimeTracker.Clear();
				Refresh();
			};
			Refresh();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void Refresh()
		{
			List<GameStat> stats = PlaytimeTracker.GetStats();
			_list.ItemsSource = stats.Select(s => new GameStatRow() {
				Name = s.Name,
				PlaytimeText = FormatPlaytime(s.TotalTime),
				LastPlayedText = s.LastPlayed.ToString("yyyy-MM-dd HH:mm")
			}).ToList();

			_empty.IsVisible = stats.Count == 0;
			if(stats.Count > 0) {
				TimeSpan total = TimeSpan.FromSeconds(stats.Sum(s => s.TotalSeconds));
				_summary.Text = $"{stats.Count} games · {FormatPlaytime(total)} total";
			} else {
				_summary.Text = "";
			}
		}

		private static string FormatPlaytime(TimeSpan t)
		{
			if(t.TotalHours >= 1) {
				return $"{(int)t.TotalHours}h {t.Minutes}m";
			}
			if(t.TotalMinutes >= 1) {
				return $"{t.Minutes}m {t.Seconds}s";
			}
			return $"{t.Seconds}s";
		}
	}
}
