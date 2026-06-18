using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Mesen.Interop;
using Mesen.Utilities;
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
		private ProgressBar _mastery = null!;
		private TextBlock _masteryLabel = null!;
		private Grid _masteryPanel = null!;
		private ComboBox _filter = null!;
		private ComboBox _sort = null!;
		private TextBlock _gameTitle = null!;
		private Border _gameIconPanel = null!;
		private Image _gameIcon = null!;
		private Border _challengesPanel = null!;
		private ItemsControl _challenges = null!;

		private List<RaAchievement> _all = new();
		private string _gameIconUrl = "";

		public RaAchievementListWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstAchievements");
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");
			_mastery = this.GetControl<ProgressBar>("barMastery");
			_masteryLabel = this.GetControl<TextBlock>("lblMastery");
			_masteryPanel = this.GetControl<Grid>("pnlMastery");
			_filter = this.GetControl<ComboBox>("cboFilter");
			_sort = this.GetControl<ComboBox>("cboSort");
			_gameTitle = this.GetControl<TextBlock>("lblGameTitle");
			_gameIconPanel = this.GetControl<Border>("pnlGameIcon");
			_gameIcon = this.GetControl<Image>("imgGameIcon");
			_challengesPanel = this.GetControl<Border>("pnlChallenges");
			_challenges = this.GetControl<ItemsControl>("lstChallenges");

			this.GetControl<Button>("btnRefresh").Click += (s, e) => Refresh();
			_filter.SelectionChanged += (s, e) => ApplyView();
			_sort.SelectionChanged += (s, e) => ApplyView();
			RetroAchievementsApi.StateChanged += OnStateChanged;
			RetroAchievementsApi.ChallengesChanged += RefreshChallenges;
			Refresh();
			RefreshChallenges();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnStateChanged(RaEvent ev, string message)
		{
			if(ev == RaEvent.GameReady || ev == RaEvent.GameFailed || ev == RaEvent.LoggedOut || ev == RaEvent.AchievementUnlocked || ev == RaEvent.HardcoreChanged) {
				Refresh();
			}
		}

		private void Refresh()
		{
			_all = RetroAchievementsApi.GetAchievementList();

			string title = RetroAchievementsApi.GetGameTitle();
			_gameTitle.Text = title.Length > 0 ? title : "Achievements";
			LoadGameIcon();

			bool any = _all.Count > 0;
			_empty.IsVisible = !any;
			_masteryPanel.IsVisible = any;

			int unlocked = _all.Count(a => a.Unlocked);
			int points = _all.Where(a => a.Unlocked).Sum(a => a.Points);
			int totalPoints = _all.Sum(a => a.Points);
			_summary.Text = any
				? $"{unlocked} / {_all.Count} unlocked   ({points} / {totalPoints} points)"
				: "";

			double pct = any ? (double)unlocked / _all.Count * 100 : 0;
			_mastery.Value = pct;
			_masteryLabel.Text = any ? $"{(int)Math.Round(pct)}%" : "";

			ApplyView();
		}

		//Applies the current filter + sort selection to the cached achievement list.
		private void ApplyView()
		{
			IEnumerable<RaAchievement> view = _all;

			switch(_filter.SelectedIndex) {
				case 1: view = view.Where(a => a.Unlocked); break;
				case 2: view = view.Where(a => a.Locked); break;
			}

			view = _sort.SelectedIndex switch {
				1 => view.OrderByDescending(a => a.Points).ThenBy(a => a.Title),
				2 => view.OrderBy(a => a.Points).ThenBy(a => a.Title),
				3 => view.OrderBy(a => a.Title),
				_ => view
			};

			_list.ItemsSource = view.ToList();
		}

		private async void LoadGameIcon()
		{
			string url = RetroAchievementsApi.GetGameImageUrl();
			if(url.Length == 0) {
				_gameIconPanel.IsVisible = false;
				return;
			}
			if(url == _gameIconUrl && _gameIcon.Source != null) {
				_gameIconPanel.IsVisible = true;
				return;
			}
			_gameIconUrl = url;
			Bitmap? bmp = await RetroAchievementsApi.GetImageAsync(url);
			if(bmp != null && _gameIconUrl == url) {
				_gameIcon.Source = bmp;
				_gameIconPanel.IsVisible = true;
			}
		}

		private void RefreshChallenges()
		{
			List<RaChallenge> challenges = RetroAchievementsApi.GetActiveChallenges();
			_challenges.ItemsSource = challenges;
			_challengesPanel.IsVisible = challenges.Count > 0;
		}

		private void OnAchievementClick(object? sender, PointerPressedEventArgs e)
		{
			if(sender is Control c && c.DataContext is RaAchievement ach && ach.Id > 0) {
				ApplicationHelper.OpenBrowser("https://retroachievements.org/achievement/" + ach.Id);
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			RetroAchievementsApi.StateChanged -= OnStateChanged;
			RetroAchievementsApi.ChallengesChanged -= RefreshChallenges;
			base.OnClosed(e);
		}
	}
}
