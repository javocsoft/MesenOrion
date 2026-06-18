using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
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
	public class NamedState : ReactiveObject
	{
		public string Path { get; set; } = "";
		public string Name { get; set; } = "";
		public string DateText { get; set; } = "";

		[Reactive] public Bitmap? Thumb { get; set; }
	}

	public class NamedSaveStatesWindow : MesenWindow
	{
		private ItemsControl _list = null!;
		private TextBlock _empty = null!;
		private TextBlock _game = null!;
		private TextBox _name = null!;

		public NamedSaveStatesWindow()
		{
			InitializeComponent();
			_list = this.GetControl<ItemsControl>("lstStates");
			_empty = this.GetControl<TextBlock>("lblEmpty");
			_game = this.GetControl<TextBlock>("lblGame");
			_name = this.GetControl<TextBox>("txtName");

			this.GetControl<Button>("btnRefresh").Click += (s, e) => Refresh();
			this.GetControl<Button>("btnSave").Click += OnSaveNew;
			_name.KeyDown += (s, e) => { if(e.Key == Key.Enter) { OnSaveNew(s, e); } };

			_game.Text = EmuApi.GetRomInfo().GetRomName();
			Refresh();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		//Per-game folder for named states, kept separate from the numbered slot states.
		private static string GetFolder()
		{
			string rom = EmuApi.GetRomInfo().GetRomName();
			return System.IO.Path.Combine(ConfigManager.SaveStateFolder, "Named", rom);
		}

		private static string Sanitize(string name)
		{
			foreach(char c in System.IO.Path.GetInvalidFileNameChars()) {
				name = name.Replace(c, '_');
			}
			return name.Trim();
		}

		private void Refresh()
		{
			List<NamedState> states = new();
			try {
				string folder = GetFolder();
				if(Directory.Exists(folder)) {
					foreach(string file in Directory.GetFiles(folder, "*." + FileDialogHelper.MesenSaveStateExt)) {
						states.Add(new NamedState() {
							Path = file,
							Name = System.IO.Path.GetFileNameWithoutExtension(file),
							DateText = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm")
						});
					}
				}
			} catch { }

			states = states.OrderByDescending(s => s.DateText).ToList();
			_list.ItemsSource = states;
			_empty.IsVisible = states.Count == 0;

			_ = LoadThumbnailsAsync(states);
		}

		private static async Task LoadThumbnailsAsync(List<NamedState> states)
		{
			foreach(NamedState state in states) {
				Bitmap? thumb = await Task.Run(() => {
					try {
						return EmuApi.GetSaveStatePreview(state.Path);
					} catch {
						return null;
					}
				});
				if(thumb != null) {
					Dispatcher.UIThread.Post(() => state.Thumb = thumb);
				}
			}
		}

		private void OnSaveNew(object? sender, RoutedEventArgs e)
		{
			if(!EmuApi.IsRunning()) {
				return;
			}
			string name = Sanitize(_name.Text ?? "");
			if(name.Length == 0) {
				return;
			}
			string folder = GetFolder();
			Directory.CreateDirectory(folder);
			EmuApi.SaveStateFile(System.IO.Path.Combine(folder, name + "." + FileDialogHelper.MesenSaveStateExt));
			_name.Text = "";
			Refresh();
		}

		private void OnStateDoubleTapped(object? sender, TappedEventArgs e)
		{
			if(sender is Control c && c.DataContext is NamedState state) {
				EmuApi.LoadStateFile(state.Path);
			}
		}

		private void OnStateContextRequested(object? sender, ContextRequestedEventArgs e)
		{
			if(sender is not Control c || c.DataContext is not NamedState state) {
				return;
			}

			MenuItem load = new() { Header = "Load" };
			load.Click += (s, _) => EmuApi.LoadStateFile(state.Path);

			MenuItem overwrite = new() { Header = "Overwrite with current state" };
			overwrite.Click += (s, _) => {
				if(EmuApi.IsRunning()) {
					EmuApi.SaveStateFile(state.Path);
					Refresh();
				}
			};

			MenuItem delete = new() { Header = "Delete" };
			delete.Click += async (s, _) => {
				if(await MesenMsgBox.Show(this, "DeleteNamedStateConfirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question, state.Name) == DialogResult.Yes) {
					try { File.Delete(state.Path); } catch { }
					Refresh();
				}
			};

			ContextMenu menu = new();
			menu.Items.Add(load);
			menu.Items.Add(overwrite);
			menu.Items.Add(new Separator());
			menu.Items.Add(delete);
			menu.Open(c);
			e.Handled = true;
		}
	}
}
