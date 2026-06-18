using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Mesen.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Windows
{
	public class RaLeaderboardListWindow : MesenWindow
	{
		private ItemsControl _list = null!;
		private TextBlock _summary = null!;
		private TextBlock _empty = null!;

		public RaLeaderboardListWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstLeaderboards");
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");

			this.GetControl<Button>("btnRefresh").Click += (s, e) => Refresh();
			RetroAchievementsApi.StateChanged += OnStateChanged;
			Refresh();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnStateChanged(RaEvent ev, string message)
		{
			if(ev == RaEvent.GameReady || ev == RaEvent.GameFailed || ev == RaEvent.LoggedOut || ev == RaEvent.LeaderboardTracker || ev == RaEvent.HardcoreChanged) {
				Refresh();
			}
		}

		private void Refresh()
		{
			List<RaLeaderboard> leaderboards = RetroAchievementsApi.GetLeaderboardList();
			//Leaderboards with an attempt in progress first, then by title
			_list.ItemsSource = leaderboards
				.OrderByDescending(l => l.IsTracking)
				.ThenBy(l => l.Title)
				.ToList();

			bool any = leaderboards.Count > 0;
			_empty.IsVisible = !any;

			int tracking = leaderboards.Count(l => l.IsTracking);
			_summary.Text = any
				? (tracking > 0 ? $"{leaderboards.Count} leaderboards   ({tracking} tracking now)" : $"{leaderboards.Count} leaderboards")
				: "";
		}

		protected override void OnClosed(EventArgs e)
		{
			RetroAchievementsApi.StateChanged -= OnStateChanged;
			base.OnClosed(e);
		}
	}
}
