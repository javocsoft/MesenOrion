using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Interop;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mesen.Windows
{
	public class RaAchievementListWindow : MesenWindow
	{
		private ItemsControl _list = null!;
		private TextBlock _summary = null!;
		private TextBlock _empty = null!;

		public RaAchievementListWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstAchievements");
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
			if(ev == RaEvent.GameReady || ev == RaEvent.GameFailed || ev == RaEvent.LoggedOut || ev == RaEvent.AchievementUnlocked) {
				Refresh();
			}
		}

		private void Refresh()
		{
			List<RaAchievement> achievements = RetroAchievementsApi.GetAchievementList();
			_list.ItemsSource = achievements;

			bool any = achievements.Count > 0;
			_empty.IsVisible = !any;

			int unlocked = achievements.Count(a => a.Unlocked);
			int points = achievements.Where(a => a.Unlocked).Sum(a => a.Points);
			int totalPoints = achievements.Sum(a => a.Points);
			_summary.Text = any
				? $"{unlocked} / {achievements.Count} unlocked   ({points} / {totalPoints} points)"
				: "";
		}

		protected override void OnClosed(EventArgs e)
		{
			RetroAchievementsApi.StateChanged -= OnStateChanged;
			base.OnClosed(e);
		}
	}
}
