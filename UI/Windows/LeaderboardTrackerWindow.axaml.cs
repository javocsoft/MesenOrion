using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Mesen.Windows
{
	public class LeaderboardTrackerWindow : Window
	{
		public LeaderboardTrackerWindow()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
