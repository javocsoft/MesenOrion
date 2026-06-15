using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Mesen.Config.Shortcuts;
using Mesen.Utilities;
using System;
using System.Collections.Generic;

namespace Mesen.Windows
{
	public class ShortcutsViewerWindow : MesenWindow
	{
		public List<ShortcutEntry> Shortcuts { get; }

		public ShortcutsViewerWindow()
		{
			Shortcuts = new List<ShortcutEntry>() {
				new("Take screenshot", Keys(EmulatorShortcut.TakeScreenshot)),
				new("Customize game cover", Keys(EmulatorShortcut.SetRecentGameScreenshot)),
				new("Change shader (next / previous)", Pair(EmulatorShortcut.NextShader, EmulatorShortcut.PreviousShader)),
				new("Change favorite shader (next / previous)", Pair(EmulatorShortcut.NextFavoriteShader, EmulatorShortcut.PreviousFavoriteShader)),
				new("Cycle picture preset", Keys(EmulatorShortcut.ApplyPicturePreset)),
				new("Record a GIF", Keys(EmulatorShortcut.ToggleGifRecorder)),
				new("Record video", Keys(EmulatorShortcut.ToggleRecordVideo)),
			};

			InitializeComponent();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		//Returns the currently-configured key combination for a shortcut (or a dash if unbound).
		private static string Keys(EmulatorShortcut shortcut)
		{
			string? combo = shortcut.GetShortcutKeys()?.ToString();
			return string.IsNullOrWhiteSpace(combo) ? "—" : combo;
		}

		private static string Pair(EmulatorShortcut next, EmulatorShortcut previous)
		{
			return Keys(next) + "   /   " + Keys(previous);
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);

			//Place this window next to the main emulator window (to the right, or to the left
			//if there isn't enough room) so it can be read while playing.
			Window? main = ApplicationHelper.GetMainWindow();
			if(main == null) {
				return;
			}

			int width = (int)(double.IsNaN(Width) ? Bounds.Width : Width);
			int x = main.Position.X + (int)main.Bounds.Width;
			int y = main.Position.Y;

			Screen? screen = Screens.ScreenFromWindow(main) ?? Screens.Primary;
			if(screen != null && x + width > screen.WorkingArea.Right) {
				x = main.Position.X - width;
				if(x < screen.WorkingArea.X) {
					x = screen.WorkingArea.X;
				}
			}

			Position = new PixelPoint(x, y);
		}
	}

	public class ShortcutEntry
	{
		public ShortcutEntry(string description, string keys)
		{
			Description = description;
			Keys = keys;
		}

		public string Description { get; set; }
		public string Keys { get; set; }
	}
}
