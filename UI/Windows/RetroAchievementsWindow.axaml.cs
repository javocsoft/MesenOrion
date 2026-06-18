using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Utilities;
using System;
using System.ComponentModel;

namespace Mesen.Windows
{
	public class RetroAchievementsWindow : MesenWindow
	{
		private static readonly IBrush DotConnected = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
		private static readonly IBrush DotConnecting = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
		private static readonly IBrush DotOffline = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
		private static readonly IBrush DotError = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

		private RetroAchievementsConfig _cfg;
		private TextBlock _status = null!;
		private Ellipse _statusDot = null!;
		private TextBlock _user = null!;
		private TextBlock _provider = null!;
		private Image _avatar = null!;
		private Image _avatarFallback = null!;
		private string _avatarUrl = "";
		private TextBox _username = null!;
		private TextBox _password = null!;
		private CheckBox _showPassword = null!;
		private CheckBox _hardcore = null!;
		private Button _login = null!;
		private Button _achievements = null!;
		private Button _leaderboards = null!;
		private StackPanel _loginPanel = null!;
		private Grid _loggedInPanel = null!;

		//Guards re-entrancy while we revert the hardcore checkbox after a declined confirmation
		private bool _suppressHardcoreEvent;

		public RetroAchievementsWindow()
		{
			_cfg = ConfigManager.Config.RetroAchievements;
			DataContext = _cfg;
			InitializeComponent();

			_status = this.GetControl<TextBlock>("lblStatus");
			_statusDot = this.GetControl<Ellipse>("statusDot");
			_user = this.GetControl<TextBlock>("lblUser");
			_provider = this.GetControl<TextBlock>("lblProvider");
			_avatar = this.GetControl<Image>("imgAvatar");
			_avatarFallback = this.GetControl<Image>("imgAvatarFallback");
			_username = this.GetControl<TextBox>("txtUsername");
			_password = this.GetControl<TextBox>("txtPassword");
			_showPassword = this.GetControl<CheckBox>("chkShow");
			_hardcore = this.GetControl<CheckBox>("chkHardcore");
			_login = this.GetControl<Button>("btnLogin");
			_achievements = this.GetControl<Button>("btnAchievements");
			_leaderboards = this.GetControl<Button>("btnLeaderboards");
			_loginPanel = this.GetControl<StackPanel>("pnlLogin");
			_loggedInPanel = this.GetControl<Grid>("pnlLoggedIn");

			_username.Text = _cfg.Username;
			_hardcore.IsChecked = _cfg.HardcoreMode;

			_login.Click += OnLogin;
			this.GetControl<Button>("btnLogout").Click += OnLogout;
			_achievements.Click += OnViewAchievements;
			_leaderboards.Click += OnViewLeaderboards;
			this.GetControl<Button>("btnClose").Click += OnClose;
			_showPassword.IsCheckedChanged += (s, e) => _password.RevealPassword = _showPassword.IsChecked == true;
			_hardcore.IsCheckedChanged += OnHardcoreChanged;
			//Pressing Enter in either field submits the login
			_username.KeyDown += OnLoginFieldKeyDown;
			_password.KeyDown += OnLoginFieldKeyDown;

			//Apply + persist any setting change immediately (no need to use the Close button)
			((INotifyPropertyChanged)_cfg).PropertyChanged += OnConfigChanged;

			RetroAchievementsApi.StateChanged += OnStateChanged;
			UpdateStatus();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void UpdateStatus()
		{
			bool loggedIn = RetroAchievementsApi.IsLoggedIn();
			_loginPanel.IsVisible = !loggedIn;
			_loggedInPanel.IsVisible = loggedIn;
			_achievements.IsEnabled = loggedIn;
			_leaderboards.IsEnabled = loggedIn;
			ToolTip.SetTip(_achievements, loggedIn ? null : "Log in to view achievements.");
			ToolTip.SetTip(_leaderboards, loggedIn ? null : "Log in to view leaderboards.");

			if(loggedIn) {
				_statusDot.Fill = DotConnected;
				_status.Text = "Connected to retroachievements.org";
				string name = RetroAchievementsApi.GetUserDisplayName();
				_user.Text = name.Length > 0 ? name : _cfg.Username;
				(int score, int softcore) = RetroAchievementsApi.GetUserScore();
				//Show both scores so "2" isn't ambiguous: hardcore points + softcore points
				_provider.Text = (score > 0 || softcore > 0)
					? string.Format("{0:N0} hardcore · {1:N0} softcore pts", score, softcore)
					: "retroachievements.org";
				ToolTip.SetTip(_provider, "Hardcore points count only unlocks earned in hardcore mode; softcore points include all unlocks.");
				LoadAvatar();
			} else {
				_statusDot.Fill = DotOffline;
				_status.Text = "Not logged in. Enter your retroachievements.org account to enable achievements.";
				_avatarUrl = "";
				_avatar.IsVisible = false;
				_avatarFallback.IsVisible = true;
			}
		}

		private async void LoadAvatar()
		{
			string url = RetroAchievementsApi.GetUserAvatarUrl();
			if(url.Length == 0) {
				_avatar.IsVisible = false;
				_avatarFallback.IsVisible = true;
				return;
			}
			if(url == _avatarUrl && _avatar.Source != null) {
				return; //already loaded
			}
			_avatarUrl = url;
			Bitmap? bmp = await RetroAchievementsApi.GetAvatarAsync(url);
			//Guard against a stale download if the user logged out/in while it was in flight
			if(bmp != null && _avatarUrl == url) {
				_avatar.Source = bmp;
				_avatar.IsVisible = true;
				_avatarFallback.IsVisible = false;
			}
		}

		private void SetLoginBusy(bool busy)
		{
			_login.IsEnabled = !busy;
			_login.Content = busy ? "Logging in…" : "Log In";
		}

		private void OnLoginFieldKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
		{
			if(e.Key == Avalonia.Input.Key.Enter && _login.IsEnabled) {
				OnLogin(sender, e);
				e.Handled = true;
			}
		}

		private void OnLogin(object? sender, RoutedEventArgs e)
		{
			string user = _username.Text ?? "";
			string pass = _password.Text ?? "";
			if(user.Trim().Length == 0 || pass.Length == 0) {
				_statusDot.Fill = DotError;
				_status.Text = "Please enter your username and password.";
				return;
			}
			_statusDot.Fill = DotConnecting;
			_status.Text = "Logging in…";
			SetLoginBusy(true);
			RetroAchievementsApi.Login(user, pass);
		}

		private void OnLogout(object? sender, RoutedEventArgs e)
		{
			RetroAchievementsApi.Logout();
			_cfg.Token = "";
			ConfigManager.Config.Save();
			UpdateStatus();
		}

		private void OnStateChanged(RaEvent ev, string message)
		{
			switch(ev) {
				case RaEvent.LoginSuccess:
					//Persist the username + session token so we can auto-login next time
					_cfg.Username = (_username.Text ?? "").Trim();
					_cfg.Token = RetroAchievementsApi.GetToken();
					_cfg.Enabled = true;
					ConfigManager.Config.Save();
					_password.Text = "";
					SetLoginBusy(false);
					UpdateStatus();
					break;

				case RaEvent.LoginFailed:
					SetLoginBusy(false);
					_statusDot.Fill = DotError;
					_status.Text = "Login failed: " + message;
					break;

				case RaEvent.LoggedOut:
				case RaEvent.GameReady:
				case RaEvent.GameFailed:
					UpdateStatus();
					break;
			}
		}

		private async void OnHardcoreChanged(object? sender, RoutedEventArgs e)
		{
			if(_suppressHardcoreEvent) {
				return;
			}

			bool enable = _hardcore.IsChecked == true;
			if(enable && EmuApi.IsRunning()) {
				//Hardcore disables save states, rewind, cheats and speed changes mid-game; make sure
				//the user understands before pulling those features out from under a running game.
				DialogResult result = await MesenMsgBox.Show(
					this,
					"RetroAchievementsHardcoreConfirm",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Warning
				);
				if(result != DialogResult.Yes) {
					_suppressHardcoreEvent = true;
					_hardcore.IsChecked = false;
					_suppressHardcoreEvent = false;
					return;
				}
			}

			_cfg.HardcoreMode = enable;
		}

		private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
		{
			//Hardcore needs to be pushed to the core; the rest just needs to be persisted
			if(e.PropertyName == nameof(RetroAchievementsConfig.HardcoreMode)) {
				RetroAchievementsApi.SetHardcoreEnabled(_cfg.HardcoreMode);
				if(_hardcore.IsChecked != _cfg.HardcoreMode) {
					_suppressHardcoreEvent = true;
					_hardcore.IsChecked = _cfg.HardcoreMode;
					_suppressHardcoreEvent = false;
				}
			}
			ConfigManager.Config.Save();
		}

		private void OnViewAchievements(object? sender, RoutedEventArgs e)
		{
			ApplicationHelper.GetOrCreateUniqueWindow(this, () => new RaAchievementListWindow());
		}

		private void OnViewLeaderboards(object? sender, RoutedEventArgs e)
		{
			ApplicationHelper.GetOrCreateUniqueWindow(this, () => new RaLeaderboardListWindow());
		}

		private void OnClose(object? sender, RoutedEventArgs e)
		{
			//Settings are already applied and saved live (see OnConfigChanged); just close.
			Close();
		}

		protected override void OnClosed(EventArgs e)
		{
			((INotifyPropertyChanged)_cfg).PropertyChanged -= OnConfigChanged;
			RetroAchievementsApi.StateChanged -= OnStateChanged;
			base.OnClosed(e);
		}
	}
}
