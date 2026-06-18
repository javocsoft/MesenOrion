using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Mesen.Windows
{
	public class PrivacyPolicyWindow : MesenWindow
	{
		//Kept in sync with PRIVACY.md in the repository root.
		private const string PolicyText =
@"Last updated: 2026-06-18 (applies to Mesen Orion 3.0.2)

OVERVIEW
Mesen Orion is a free, open-source emulator. It does NOT operate any server, does
not collect analytics or telemetry, and does not send your data to its developers.
There is no ""Mesen Orion server"": the application runs entirely on your device.

DATA STORED ON YOUR DEVICE
Mesen Orion stores its settings and your data only locally, on your computer, under
your control:
 • Configuration and preferences, input mappings, etc.
 • Save states, battery saves, screenshots, recorded GIFs/videos, recent-game thumbnails.
 • If you use RetroAchievements: your RetroAchievements username and a login token
   (NOT your password) are stored locally so you can stay logged in.
You can delete this data at any time by removing the Mesen Orion data folder.

DATA SENT TO THIRD PARTIES
Mesen Orion only communicates over the internet in optional cases, and only directly
with the corresponding third-party service:

 1. RetroAchievements (only if you enable it and log in): the app connects directly to
    retroachievements.org to log you in, identify the game you are playing (by a hash
    of the ROM), unlock achievements, submit leaderboard scores and update your
    ""rich presence"" status. This is sent directly from your device to RetroAchievements;
    Mesen Orion's developers never receive or store it. Your use of RetroAchievements is
    governed by RetroAchievements' own privacy policy and terms (https://retroachievements.org/).
    RetroAchievements operates its own servers; their locations, data retention and GDPR
    handling are described in their policy.

 2. Net Play (only if you use it): connects directly to the host/peer you choose. No data
    passes through any Mesen Orion server (there is none).

WHAT WE DO NOT DO
 • We do not run any server or backend for Mesen Orion.
 • We do not collect telemetry, analytics, usage statistics or crash reports.
 • We do not show ads.
 • We do not sell or share your data (we don't have it).
 • The online auto-update check has been disabled in this fork, so the app does not
   contact any server on startup.

DATA RETENTION
Because Mesen Orion has no server, it retains NO data about you on any server. Data
stored locally on your device is kept until you delete it. Data you choose to send to
RetroAchievements is retained by RetroAchievements according to their policy, not by
Mesen Orion.

YOUR RIGHTS (GDPR AND SIMILAR)
Mesen Orion does not process your personal data on any server, so there is no
server-side data for us to access, correct or erase. You have full control over the
data stored locally on your device and can delete it at any time. For data held by
RetroAchievements (if you opt in), please refer to RetroAchievements' privacy policy
and contact them directly.

CHILDREN
Mesen Orion does not knowingly collect any personal data from anyone, including children.

CHANGES
This policy may be updated; the latest version ships with the application and is also
available in the project repository.

CONTACT
Mesen Orion is maintained by JavocSoft.
Source code and issues: https://github.com/javocsoft/MesenOrion";

		public PrivacyPolicyWindow()
		{
			InitializeComponent();
			this.GetControl<SelectableTextBlock>("txtPolicy").Text = PolicyText;
			this.GetControl<Button>("btnClose").Click += OnClose;
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void OnClose(object? sender, RoutedEventArgs e)
		{
			Close();
		}
	}
}
