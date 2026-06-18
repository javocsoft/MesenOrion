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
		private TextBox _search = null!;
		private ComboBox _sort = null!;

		private List<GameStat> _allStats = new();

		public GameStatisticsWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstStats");
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");
			_search = this.GetControl<TextBox>("txtSearch");
			_sort = this.GetControl<ComboBox>("cboSort");

			this.GetControl<Button>("btnClear").Click += (s, e) => {
				PlaytimeTracker.Clear();
				Refresh();
			};
			_search.TextChanged += (s, e) => ApplyView();
			_sort.SelectionChanged += (s, e) => ApplyView();
			Refresh();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void Refresh()
		{
			_allStats = PlaytimeTracker.GetStats();
			ApplyView();
		}

		private void ApplyView()
		{
			IEnumerable<GameStat> view = _allStats;

			string search = (_search.Text ?? "").Trim();
			if(search.Length > 0) {
				view = view.Where(s => s.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
			}

			view = _sort.SelectedIndex switch {
				1 => view.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
				2 => view.OrderByDescending(s => s.TotalSeconds).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
				_ => view.OrderByDescending(s => s.LastPlayed)
			};

			List<GameStat> result = view.ToList();
			_list.ItemsSource = result.Select(s => new GameStatRow() {
				Name = s.Name,
				PlaytimeText = FormatPlaytime(s.TotalTime),
				LastPlayedText = s.LastPlayed.ToString("yyyy-MM-dd HH:mm")
			}).ToList();

			_empty.IsVisible = result.Count == 0;
			if(_allStats.Count == 0) {
				_empty.Text = "No play data yet. Play a game and it'll show up here.";
				_summary.Text = "";
			} else if(result.Count == 0) {
				_empty.Text = "No games match your search.";
				_summary.Text = "";
			} else if(result.Count < _allStats.Count) {
				_summary.Text = $"Showing {result.Count} of {_allStats.Count} games";
			} else {
				TimeSpan total = TimeSpan.FromSeconds(_allStats.Sum(s => s.TotalSeconds));
				_summary.Text = $"{_allStats.Count} games · {FormatPlaytime(total)} total";
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
