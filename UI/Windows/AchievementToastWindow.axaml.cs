using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;

namespace Mesen.Windows
{
	public class AchievementToastWindow : Window
	{
		private Image _badgeImage = null!;

		public AchievementToastWindow()
		{
			InitializeComponent();
			_badgeImage = this.FindControl<Image>("BadgeImage")!;
		}

		public void SetBadge(Bitmap? badge)
		{
			_badgeImage.Source = badge;
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}
	}
}
