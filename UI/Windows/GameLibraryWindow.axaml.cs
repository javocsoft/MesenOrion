using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mesen.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public class GameLibraryWindow : MesenWindow
	{
		private TextBlock _summary = null!;
		private TextBlock _empty = null!;
		private TextBlock _loading = null!;
		private ItemsControl _folders = null!;
		private ItemsControl _games = null!;
		private LayoutTransformControl _scaleHost = null!;
		private ScrollViewer _scroll = null!;
		private TextBox _search = null!;
		private ComboBox _system = null!;
		private ToggleButton _favorites = null!;
		private Slider _size = null!;

		private List<LibraryGame> _allGames = new();
		private int _coverLoadId;
		private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

		public GameLibraryWindow()
		{
			InitializeComponent();
			_summary = this.GetControl<TextBlock>("lblSummary");
			_empty = this.GetControl<TextBlock>("lblEmpty");
			_loading = this.GetControl<TextBlock>("lblLoading");
			_folders = this.GetControl<ItemsControl>("lstFolders");
			_games = this.GetControl<ItemsControl>("lstGames");
			_scaleHost = this.GetControl<LayoutTransformControl>("scaleHost");
			_scroll = this.GetControl<ScrollViewer>("scroll");
			_search = this.GetControl<TextBox>("txtSearch");
			_system = this.GetControl<ComboBox>("cboSystem");
			_favorites = this.GetControl<ToggleButton>("tglFavorites");
			_size = this.GetControl<Slider>("sldSize");

			this.GetControl<Button>("btnAddFolder").Click += OnAddFolder;
			this.GetControl<Button>("btnRefresh").Click += (s, e) => RefreshAll(rescan: true);

			//Debounce the search so typing doesn't rebuild the grid on every keystroke
			_searchTimer.Tick += (s, e) => { _searchTimer.Stop(); ApplyFilter(); };
			_search.TextChanged += (s, e) => { _searchTimer.Stop(); _searchTimer.Start(); };

			_system.SelectionChanged += (s, e) => ApplyFilter();
			_favorites.IsCheckedChanged += (s, e) => ApplyFilter();

			_size.Value = GameLibrary.CardScale;
			_size.PropertyChanged += (s, e) => {
				if(e.Property == Slider.ValueProperty) {
					GameLibrary.CardScale = (int)Math.Round(_size.Value);
					RecomputeScale();
				}
			};
			//Re-fit the grid to the available width whenever the window/scroll area is resized
			_scroll.PropertyChanged += (s, e) => {
				if(e.Property == Visual.BoundsProperty) {
					RecomputeScale();
				}
			};

			//Defer the (potentially slow) first load so the window paints the "Loading…" state first
			Dispatcher.UIThread.Post(() => RefreshAll(rescan: false), DispatcherPriority.Background);
		}

		//Cap how many cards are rendered at once - the grid isn't virtualized, so drawing thousands
		//would freeze the UI. Search/filters narrow things down to find anything beyond the cap.
		private const int MaxDisplay = 500;

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		//Scales the grid so a whole number of columns fills the available width (no wasted gap on the
		//right). The Size slider (1-3) sets the target card size, which decides how many columns fit.
		private void RecomputeScale()
		{
			double availW = _scroll.Bounds.Width - 18; //reserve space for the vertical scrollbar
			if(availW <= 50) {
				return;
			}
			const double cellBase = 158.0; //150px card + 8px margin
			int target = Math.Clamp((int)Math.Round(_size.Value), 1, 3);
			int columns = Math.Max(1, (int)Math.Round(availW / (cellBase * target)));
			double scale = availW / (columns * cellBase);
			_scaleHost.LayoutTransform = new ScaleTransform(scale, scale);
		}

		private async void RefreshAll(bool rescan)
		{
			_folders.ItemsSource = GameLibrary.Folders.ToList();
			_loading.IsVisible = true;
			_empty.IsVisible = false;

			//Scan / load the cache off the UI thread so the window stays responsive while opening
			List<LibraryGame> games = await Task.Run(() => rescan ? GameLibrary.Rescan() : GameLibrary.GetGames());
			_allGames = games;

			List<string> systems = new() { "All systems" };
			systems.AddRange(_allGames.Select(g => g.System).Distinct().OrderBy(s => s));
			_system.ItemsSource = systems;
			_system.SelectedIndex = 0;

			_loading.IsVisible = false;
			ApplyFilter();
			RecomputeScale();
		}

		private void ApplyFilter()
		{
			IEnumerable<LibraryGame> view = _allGames;

			string search = (_search.Text ?? "").Trim();
			if(search.Length > 0) {
				view = view.Where(g => g.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
			}

			if(_system.SelectedIndex > 0 && _system.SelectedItem is string sys) {
				view = view.Where(g => g.System == sys);
			}

			if(_favorites.IsChecked == true) {
				view = view.Where(g => g.IsFavorite);
			}

			List<LibraryGame> matched = view.ToList();
			bool capped = matched.Count > MaxDisplay;
			List<LibraryGame> result = capped ? matched.Take(MaxDisplay).ToList() : matched;
			_games.ItemsSource = result;

			bool hasFolders = GameLibrary.Folders.Count > 0;
			_empty.IsVisible = matched.Count == 0 && !_loading.IsVisible;
			if(!hasFolders) {
				_empty.Text = "Add a folder containing your ROMs to build your library.";
			} else if(_allGames.Count == 0) {
				_empty.Text = "No supported ROMs found. Click Rescan after adding ROMs.";
			} else {
				_empty.Text = "No games match the current filter.";
			}

			if(_allGames.Count == 0) {
				_summary.Text = "";
			} else if(capped) {
				_summary.Text = $"Showing {result.Count} of {matched.Count} matches — refine your search to see more";
			} else {
				_summary.Text = $"{matched.Count} / {_allGames.Count} games";
			}

			//Load covers only for the cards actually shown
			_ = LoadCoversAsync(result, ++_coverLoadId);
		}

		//Loads each game's captured cover/screenshot in the background and swaps it in when found.
		private async Task LoadCoversAsync(List<LibraryGame> games, int loadId)
		{
			foreach(LibraryGame game in games) {
				if(game.CoverChecked) {
					continue;
				}
				Bitmap? cover = await Task.Run(() => GameLibrary.TryLoadCover(game.Name));
				if(loadId != _coverLoadId) {
					return; //a newer refresh superseded this one
				}
				game.CoverChecked = true;
				if(cover != null) {
					game.IconImage = cover;
					game.HasCover = true;
				}
			}
		}

		private async void OnAddFolder(object? sender, RoutedEventArgs e)
		{
			string? folder = await FileDialogHelper.OpenFolder(this);
			if(folder != null) {
				GameLibrary.AddFolder(folder);
				RefreshAll(rescan: true);
			}
		}

		private void OnRemoveFolder(object? sender, RoutedEventArgs e)
		{
			if(sender is Control c && c.DataContext is string folder) {
				GameLibrary.RemoveFolder(folder);
				RefreshAll(rescan: true);
			}
		}

		private void OnToggleFavorite(object? sender, RoutedEventArgs e)
		{
			if(sender is Control c && c.DataContext is LibraryGame game) {
				GameLibrary.ToggleFavorite(game);
				if(_favorites.IsChecked == true) {
					ApplyFilter();
				}
			}
		}

		private void OnGameContextRequested(object? sender, ContextRequestedEventArgs e)
		{
			if(sender is not Control c || c.DataContext is not LibraryGame game) {
				return;
			}

			MenuItem play = new() { Header = "Play" };
			play.Click += (s, _) => { LoadRomHelper.LoadFile(game.Path); Close(); };

			MenuItem view = new() { Header = "View in folder" };
			view.Click += (s, _) => ApplicationHelper.ShowInFolder(game.Path);

			MenuItem fav = new() { Header = game.IsFavorite ? "Remove from favorites" : "Add to favorites" };
			fav.Click += (s, _) => {
				GameLibrary.ToggleFavorite(game);
				if(_favorites.IsChecked == true) {
					ApplyFilter();
				}
			};

			ContextMenu menu = new();
			menu.Items.Add(play);
			menu.Items.Add(view);
			menu.Items.Add(new Separator());
			menu.Items.Add(fav);
			menu.Open(c);
			e.Handled = true;
		}

		private void OnGameDoubleTapped(object? sender, TappedEventArgs e)
		{
			if(sender is Control c && c.DataContext is LibraryGame game) {
				LoadRomHelper.LoadFile(game.Path);
				Close();
			}
		}
	}
}
