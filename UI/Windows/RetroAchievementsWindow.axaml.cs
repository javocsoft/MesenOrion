using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Utilities;
using System;

namespace Mesen.Windows
{
	public class RetroAchievementsWindow : MesenWindow
	{
		private RetroAchievementsConfig _cfg;
		private TextBlock _status = null!;
		private TextBox _username = null!;
		private TextBox _password = null!;
		private CheckBox _showPassword = null!;
		private StackPanel _loginPanel = null!;
		private StackPanel _loggedInPanel = null!;

		public RetroAchievementsWindow()
		{
			_cfg = ConfigManager.Config.RetroAchievements;
			DataContext = _cfg;
			InitializeComponent();

			_status = this.GetControl<TextBlock>("lblStatus");
			_username = this.GetControl<TextBox>("txtUsername");
			_password = this.GetControl<TextBox>("txtPassword");
			_showPassword = this.GetControl<CheckBox>("chkShow");
			_loginPanel = this.GetControl<StackPanel>("pnlLogin");
			_loggedInPanel = this.GetControl<StackPanel>("pnlLoggedIn");

			_username.Text = _cfg.Username;

			this.GetControl<Button>("btnLogin").Click += OnLogin;
			this.GetControl<Button>("btnLogout").Click += OnLogout;
			this.GetControl<Button>("btnAchievements").Click += OnViewAchievements;
			this.GetControl<Button>("btnClose").Click += OnClose;
			_showPassword.IsCheckedChanged += (s, e) => _password.RevealPassword = _showPassword.IsChecked == true;

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
			_status.Text = loggedIn
				? ("Logged in as " + _cfg.Username)
				: "Not logged in. Enter your retroachievements.org account to enable achievements.";
		}

		private void OnLogin(object? sender, RoutedEventArgs e)
		{
			string user = _username.Text ?? "";
			string pass = _password.Text ?? "";
			if(user.Trim().Length == 0 || pass.Length == 0) {
				_status.Text = "Please enter your username and password.";
				return;
			}
			_status.Text = "Logging in...";
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
					UpdateStatus();
					break;

				case RaEvent.LoginFailed:
					_status.Text = "Login failed: " + message;
					break;

				case RaEvent.LoggedOut:
				case RaEvent.GameReady:
				case RaEvent.GameFailed:
					UpdateStatus();
					break;
			}
		}

		private void OnViewAchievements(object? sender, RoutedEventArgs e)
		{
			ApplicationHelper.GetOrCreateUniqueWindow(this, () => new RaAchievementListWindow());
		}

		private void OnClose(object? sender, RoutedEventArgs e)
		{
			//Hardcore is disabled (pending RA approval for this emulator)
			RetroAchievementsApi.SetHardcoreEnabled(false);
			ConfigManager.Config.Save();
			Close();
		}

		protected override void OnClosed(EventArgs e)
		{
			RetroAchievementsApi.StateChanged -= OnStateChanged;
			base.OnClosed(e);
		}
	}
}
