using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Mesen.Config
{
	public class RetroAchievementsConfig : BaseConfig<RetroAchievementsConfig>
	{
		[Reactive] public bool Enabled { get; set; } = false;
		[Reactive] public string Username { get; set; } = "";

		//RetroAchievements login token (returned by the server after the first login).
		//Stored instead of the password, matching how official RA clients persist credentials.
		[Reactive] public string Token { get; set; } = "";

		[Reactive] public bool HardcoreMode { get; set; } = false;
		[Reactive] public bool EnableNotifications { get; set; } = true;
		[Reactive] public bool EnableSound { get; set; } = true;
		[Reactive] public bool EnableRichPresence { get; set; } = true;
		//On-screen "primed" badges shown while an achievement's condition is actively being met
		[Reactive] public bool EnableChallengeIndicators { get; set; } = true;
	}
}
