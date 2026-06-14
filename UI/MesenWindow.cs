using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mesen.Config;
using Mesen.Utilities;
using System;

namespace Mesen;

public class MesenWindow : Window
{
	static MesenWindow()
	{
		PopupRoot.ClientSizeProperty.Changed.AddClassHandler<PopupRoot>((s, e) => {
			foreach(var v in s.GetVisualChildren()) {
				SetTextRenderingMode(v);
			}
		});
	}

	protected override void OnInitialized()
	{
		base.OnInitialized();
		Focusable = true;
		SetTextRenderingMode(this);

		//Use the Mesen Orion icon for the window/taskbar icon, unless the window already
		//set its own (e.g. the debugger windows use CPU-specific icons).
		if(Icon is null) {
			try {
				Icon = new WindowIcon(ImageUtilities.BitmapFromAsset("Assets/mesenorion.webp"));
			} catch {
				//Ignore - fall back to the default icon if the asset can't be loaded
			}
		}
	}

	private static void SetTextRenderingMode(Visual v)
	{
		switch(ConfigManager.Config.Preferences.FontAntialiasing) {
			case FontAntialiasing.Disabled:
				RenderOptions.SetTextRenderingMode(v, TextRenderingMode.Alias);
				break;

			case FontAntialiasing.Antialias:
				RenderOptions.SetTextRenderingMode(v, TextRenderingMode.Antialias);
				break;

			default:
			case FontAntialiasing.SubPixelAntialias:
				RenderOptions.SetTextRenderingMode(v, TextRenderingMode.SubpixelAntialias);
				break;
		};
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);

		if(DataContext is IDisposable disposable) {
			disposable.Dispose();
		}

		//This fixes (or just dramatically reduces?) a memory leak
		//Most windows don't get GCed properly if this isn't done, leading to large memory leaks
		DataContext = null;
	}
}