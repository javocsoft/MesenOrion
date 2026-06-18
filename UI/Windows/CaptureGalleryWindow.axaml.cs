using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public class CaptureItem : ReactiveObject
	{
		public string FilePath { get; set; } = "";
		public string FileName { get; set; } = "";
		public string DateText { get; set; } = "";
		public string TypeTag { get; set; } = "";

		[Reactive] public Bitmap? Thumb { get; set; }
	}

	public class CaptureGalleryWindow : MesenWindow
	{
		private ItemsControl _list = null!;
		private TextBlock _summary = null!;
		private TextBlock _empty = null!;

		public CaptureGalleryWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstCaptures");
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");

			this.GetControl<Button>("btnRefresh").Click += (s, e) => Refresh();
			this.GetControl<Button>("btnScreenshots").Click += (s, e) => OpenFolder(ConfigManager.ScreenshotFolder);
			this.GetControl<Button>("btnGifs").Click += (s, e) => OpenFolder(ConfigManager.GifFolder);
			Refresh();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void Refresh()
		{
			List<CaptureItem> items = new();
			AddFiles(items, ConfigManager.ScreenshotFolder, "*.png", "PNG");
			AddFiles(items, ConfigManager.GifFolder, "*.gif", "GIF");

			//Newest first (filenames are timestamped, but use the file's write time to be safe)
			items = items.OrderByDescending(i => File.Exists(i.FilePath) ? File.GetLastWriteTime(i.FilePath) : DateTime.MinValue).ToList();

			_list.ItemsSource = items;
			_empty.IsVisible = items.Count == 0;
			_summary.Text = items.Count > 0 ? $"{items.Count} captures" : "";

			//Load the thumbnails off the UI thread
			_ = LoadThumbnailsAsync(items);
		}

		private static void AddFiles(List<CaptureItem> items, string folder, string pattern, string tag)
		{
			try {
				if(!Directory.Exists(folder)) {
					return;
				}
				foreach(string file in Directory.GetFiles(folder, pattern)) {
					items.Add(new CaptureItem() {
						FilePath = file,
						FileName = Path.GetFileName(file),
						DateText = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm"),
						TypeTag = tag
					});
				}
			} catch { }
		}

		private static async Task LoadThumbnailsAsync(List<CaptureItem> items)
		{
			foreach(CaptureItem item in items) {
				Bitmap? thumb = await Task.Run(() => {
					try {
						using FileStream fs = File.OpenRead(item.FilePath);
						return Bitmap.DecodeToWidth(fs, 320);
					} catch {
						return null;
					}
				});
				if(thumb != null) {
					Dispatcher.UIThread.Post(() => item.Thumb = thumb);
				}
			}
		}

		private void OnCaptureClick(object? sender, PointerPressedEventArgs e)
		{
			if(sender is Control c && c.DataContext is CaptureItem item && File.Exists(item.FilePath)) {
				ApplicationHelper.OpenFileOrFolder(item.FilePath);
			}
		}

		private static void OpenFolder(string folder)
		{
			try {
				Directory.CreateDirectory(folder);
				ApplicationHelper.OpenFileOrFolder(folder);
			} catch { }
		}
	}
}
