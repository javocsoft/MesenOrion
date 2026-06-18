using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.ViewModels;
using Mesen.Config;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using ReactiveUI;
using Mesen.Utilities;
using Mesen.Interop;
using Mesen.Views;
using Avalonia.Layout;
using Mesen.Debugger.Utilities;
using System.Threading;
using Mesen.Debugger.Windows;
using Avalonia.Input.Platform;
using System.Collections.Generic;
using Mesen.Controls;
using Mesen.Localization;
using System.Diagnostics;
using Avalonia.VisualTree;
using System.Text;

namespace Mesen.Windows
{
	public class MainWindow : MesenWindow
	{
		private DispatcherTimer _timerBackgroundFlag = new DispatcherTimer();
		private DispatcherTimer _achievementToastTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(2.5) };
		private MainWindowViewModel _model = null!;

		private NotificationListener? _listener = null;
		private ShortcutHandler _shortcutHandler;

		private MouseManager? _mouseManager = null;
		private IDisposable? _softwareRendererSubscription;
		private ContentControl _audioPlayer;
		private MainMenuView _mainMenu;
		private CommandLineHelper? _cmdLine;

		private bool _testModeEnabled;
		private bool _needResume = false;
		private bool _needCloseValidation = true;
		private bool _isClosing = false;

		private bool _preventFullscreenToggle = false;

		private Panel _rendererPanel;
		private NativeRenderer _renderer;
		private SoftwareRendererView _softwareRenderer;
		private Size _rendererSize;
		private bool _usesSoftwareRenderer;

		// On Windows, NativeControlHost HWNDs always paint above Avalonia Popups, so we use a
		// separate Topmost Window for the achievement toast instead of the in-XAML Popup.
		private AchievementToastWindow? _toastWindow;
		private LeaderboardTrackerWindow? _trackerWindow;

		// Achievement unlock toasts are shown one at a time; when several fire at once (common in
		// RetroAchievements) they queue up and display sequentially instead of overwriting each other.
		// Reused for achievement unlocks, mastery and leaderboard-rank notifications.
		private class ToastInfo
		{
			public string Header = "";
			public string Title = "";
			public string Subtitle = "";
			public Avalonia.Media.Imaging.Bitmap? Badge;
		}
		private Queue<ToastInfo> _toastQueue = new();
		private bool _toastShowing;

		// Active "primed"/challenge achievement badges, keyed by achievement id
		private Dictionary<uint, RaChallengeIndicator> _challengeBadges = new();
		private DispatcherTimer _progressToastTimer = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(2) };

		private FrameInfo _prevScreenSize;

		private Size _originalSize;
		private PixelPoint _originalPos;
		private WindowState _prevWindowState;

		//Used to suppress key-repeat keyup events on Linux
		private Dictionary<Key, IDisposable> _pendingKeyUpEvents = new();
		private bool _isLinux = false;

		private Stopwatch _stopWatch = Stopwatch.StartNew();
		private Dictionary<Key, long> _keyPressedStamp = new();
		private bool _focusInMenu;

		static MainWindow()
		{
			WindowStateProperty.Changed.AddClassHandler<MainWindow>((x, e) => x.OnWindowStateChanged());
			IsActiveProperty.Changed.AddClassHandler<MainWindow>((x, e) => x.OnActiveChanged());
		}

		public MainWindow()
		{
			_testModeEnabled = System.Diagnostics.Debugger.IsAttached;
			_isLinux = OperatingSystem.IsLinux();
			_usesSoftwareRenderer = ConfigManager.Config.Video.UseSoftwareRenderer || OperatingSystem.IsMacOS();

			_model = new MainWindowViewModel();
			DataContext = _model;
			InitGlobalShortcuts();

			EmuApi.InitDll();

			Directory.CreateDirectory(ConfigManager.HomeFolder);
			Directory.SetCurrentDirectory(ConfigManager.HomeFolder);

			InitializeComponent();

			_shortcutHandler = new ShortcutHandler(this);

			AddHandler(DragDrop.DropEvent, OnDrop);

			//Allows us to catch LeftAlt/RightAlt key presses
			AddHandler(InputElement.KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel, true);
			AddHandler(InputElement.KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel, true);

			_rendererPanel = this.GetControl<Panel>("RendererPanel");
			_rendererPanel.LayoutUpdated += RendererPanel_LayoutUpdated;

			if(OperatingSystem.IsWindows()) {
				// On Windows, NativeControlHost HWNDs always paint above Avalonia Popups, so
				// we disconnect the Popup binding and use a separate Topmost Window instead.
				var popup = this.GetControl<Popup>("AchievementToastPopup");
				popup.ClearValue(Popup.IsOpenProperty);
				popup.IsOpen = false;
				_toastWindow = new AchievementToastWindow();
				_toastWindow.DataContext = _model;
				_toastWindow.ShowActivated = false;

				var trackerPopup = this.GetControl<Popup>("LeaderboardTrackerPopup");
				trackerPopup.ClearValue(Popup.IsOpenProperty);
				trackerPopup.IsOpen = false;
				_trackerWindow = new LeaderboardTrackerWindow();
				_trackerWindow.DataContext = _model;
				_trackerWindow.ShowActivated = false;
			}

			_renderer = this.GetControl<NativeRenderer>("Renderer");
			_softwareRenderer = this.GetControl<SoftwareRendererView>("SoftwareRenderer");
			_audioPlayer = this.GetControl<ContentControl>("AudioPlayer");
			_mainMenu = this.GetControl<MainMenuView>("MainMenu");
			ConfigManager.Config.MainWindow.LoadWindowSettings(this);

			Console.CancelKeyPress += Console_CancelKeyPress;

#if DEBUG
			this.AttachDevTools();
#endif
		}

		private static void InitGlobalShortcuts()
		{
			if(Application.Current?.PlatformSettings == null) {
				return;
			}

			PlatformHotkeyConfiguration hotkeyConfig = Application.Current.PlatformSettings.HotkeyConfiguration;
			List <KeyGesture> gestures = hotkeyConfig.OpenContextMenu;
			for(int i = gestures.Count - 1; i >= 0; i--) {
				if(gestures[i].Key == Key.F10 && gestures[i].KeyModifiers == KeyModifiers.Shift) {
					//Disable Shift-F10 shortcut to open context menu - interferes with default shortcut for step back
					gestures.RemoveAt(i);
				}
			}
			hotkeyConfig.Copy.Add(new KeyGesture(Key.Insert, KeyModifiers.Control));
			hotkeyConfig.Paste.Add(new KeyGesture(Key.Insert, KeyModifiers.Shift));
			hotkeyConfig.Cut.Add(new KeyGesture(Key.Delete, KeyModifiers.Shift));
		}

		protected override void OnClosing(WindowClosingEventArgs e)
		{
			base.OnClosing(e);
			if(_needCloseValidation) {
				e.Cancel = true;
				ValidateExit();
			} else {
				if(!CloseEmu(false)) {
					e.Cancel = true;
				}
			}
		}

		private bool CloseEmu(bool force)
		{
			//Close all other windows first
			DebugWindowManager.CloseAllWindows();
			foreach(Window wnd in ApplicationHelper.GetOpenedWindows()) {
				if(wnd != this) {
					wnd.Close();
				}
			}

			if(!force && ApplicationHelper.GetOpenedWindows().Count > 1) {
				return false;
			}

			_timerBackgroundFlag.Stop();
			EmuApi.Stop();
			_listener?.Dispose();
			EmuApi.Release();
			ConfigManager.Config.MainWindow.SaveWindowSettings(this);
			ConfigManager.Config.Save();
			_isClosing = true;

			return true;
		}

		private async void ValidateExit()
		{
			if(!ConfigManager.Config.Preferences.ConfirmExitResetPower || await MesenMsgBox.Show(null, "ConfirmExit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
				_needCloseValidation = false;
				Close();
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			_softwareRendererSubscription?.Dispose();
			_mouseManager?.Dispose();
			_toastWindow?.Close();
			_trackerWindow?.Close();
		}

		private void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
		{
			_needCloseValidation = false;
			Dispatcher.UIThread.Post(() => {
				CloseEmu(true);
			});
		}

		private void OnDrop(object? sender, DragEventArgs e)
		{
			string? filename = e.Data.GetFiles()?.FirstOrDefault()?.Path.LocalPath;
			if(filename != null) {
				if(File.Exists(filename)) {
					LoadRomHelper.LoadFile(filename);
					Activate();
				} else {
					DisplayMessageHelper.DisplayMessage("Error", ResourceHelper.GetMessage("FileNotFound", filename));
				}
			}
		}

		protected override void OnOpened(EventArgs e)
		{
			base.OnOpened(e);

			if(Design.IsDesignMode) {
				return;
			}

			// Reposition the Windows toast window whenever the main window moves
			if(_toastWindow != null) {
				this.PositionChanged += (s, _) => {
					if(_toastWindow.IsVisible) {
						PositionToastWindow();
					}
				};
			}

			_mouseManager = new MouseManager(this, _usesSoftwareRenderer ? _softwareRenderer : _renderer, _mainMenu, _usesSoftwareRenderer);

			ConfigManager.Config.InitializeFontDefaults();
			ConfigManager.Config.Preferences.ApplyFontOptions();
			ConfigManager.Config.Debug.Fonts.ApplyConfig();

			_timerBackgroundFlag.Interval = TimeSpan.FromMilliseconds(100);
			_timerBackgroundFlag.Tick += timerUpdateBackgroundFlag;
			_timerBackgroundFlag.Start();

			//Give focus to panel to avoid menu being given focus by default
			this.GetControl<Panel>("RendererPanel").Focus();

			//Focus on the recent games dialog if it's visible
			//This also enables keyboard/gamepad navigation on the selection screen without having to click it first
			this.FindDescendantOfType<StateGrid>()?.Focus();

			Task.Run(() => {
				CommandLineHelper cmdLine = new CommandLineHelper(Program.CommandLineArgs, true);
				_cmdLine = cmdLine;

				EmuApi.InitializeEmu(
					ConfigManager.HomeFolder,
					TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
					_renderer.Handle,
					_usesSoftwareRenderer,
					cmdLine.NoAudio,
					cmdLine.NoVideo,
					cmdLine.NoInput
				);

				ConfigManager.Config.RemoveObsoleteConfig();

				//Register the RetroAchievements HTTP transport (rcheevos in the core delegates
				//its server requests to .NET's HttpClient), then auto-login with the saved token.
				RetroAchievementsApi.Init();
				RetroAchievementsApi.StateChanged += (ev, msg) => {
					if(ev == RaEvent.AchievementUnlocked && ConfigManager.Config.RetroAchievements.EnableSound) {
						RetroAchievementsApi.PlaySound();
					} else if(ev == RaEvent.LeaderboardTracker) {
						//Live leaderboard value (empty = hide)
						bool show = msg.Length > 0;
						_model.LeaderboardTrackerText = msg;
						if(_trackerWindow != null) {
							//Windows: separate Topmost window (NativeControlHost paints above Popups)
							if(show) {
								PositionTrackerWindow();
								if(!_trackerWindow.IsVisible) {
									_trackerWindow.Show(this);
								}
							} else {
								_trackerWindow.Hide();
							}
						} else {
							_model.LeaderboardTrackerVisible = show;
						}
					} else if(ev == RaEvent.GameCompleted) {
						OnGameMastered(msg);
					} else if(ev == RaEvent.LeaderboardScoreboard) {
						OnLeaderboardScoreboard(msg);
					} else if(ev == RaEvent.ChallengeShow) {
						OnChallengeShow(msg);
					} else if(ev == RaEvent.ChallengeHide) {
						OnChallengeHide(msg);
					} else if(ev == RaEvent.ProgressShow) {
						OnProgressShow(msg);
					} else if(ev == RaEvent.ProgressHide) {
						OnProgressHide();
					}
				};
				_progressToastTimer.Tick += (s, e) => {
					_progressToastTimer.Stop();
					_model.ProgressOpacity = 0;
					DispatcherTimer.RunOnce(() => _model.ProgressVisible = false, TimeSpan.FromMilliseconds(400));
				};
				_achievementToastTimer.Tick += (s, e) => {
					_achievementToastTimer.Stop();
					//Fade out, then hide the popup/window once the fade has finished and show the
					//next queued toast (if any).
					_model.AchievementToastOpacity = 0;
					DispatcherTimer.RunOnce(() => {
						if(_toastWindow != null) {
							_toastWindow.Hide();
						} else {
							_model.AchievementToastVisible = false;
						}
						_toastShowing = false;
						ShowNextToast();
					}, TimeSpan.FromMilliseconds(400));
				};
				RetroAchievementsApi.AchievementUnlocked += (ach) => {
					if(!ConfigManager.Config.RetroAchievements.EnableNotifications) {
						return;
					}
					//Queue the unlock; if nothing is showing it'll be displayed immediately, otherwise
					//it waits its turn so simultaneous unlocks don't overwrite one another.
					EnqueueToast(new ToastInfo() {
						Header = "Achievement Unlocked",
						Title = ach.Title,
						Subtitle = ach.Points > 0 ? (ach.Points + " points") : "",
						Badge = ach.Badge
					});
				};
				//Toggle the on-screen challenge indicators live when the setting changes (hide when
				//turned off, restore the currently-active set when turned back on).
				((System.ComponentModel.INotifyPropertyChanged)ConfigManager.Config.RetroAchievements).PropertyChanged += (s, e) => {
					if(e.PropertyName == nameof(RetroAchievementsConfig.EnableChallengeIndicators)) {
						RefreshChallengeOverlay();
					}
				};
				RetroAchievementsConfig raConfig = ConfigManager.Config.RetroAchievements;
				if(raConfig.Enabled && raConfig.Username.Length > 0 && raConfig.Token.Length > 0) {
					RetroAchievementsApi.SetHardcoreEnabled(raConfig.HardcoreMode);
					RetroAchievementsApi.LoginWithToken(raConfig.Username, raConfig.Token);
				}

				//InitializeDefaults must be after InitializeEmu, otherwise keybindings will be empty
				ConfigManager.Config.InitializeDefaults();
				ConfigManager.Config.UpgradeConfig();

				_listener = new NotificationListener();
				_listener.OnNotification += OnNotification;

				_model.Init(this);

				ConfigManager.Config.ApplyConfig();

				if(ConfigManager.Config.Preferences.OverrideGameFolder && Directory.Exists(ConfigManager.Config.Preferences.GameFolder)) {
					EmuApi.AddKnownGameFolder(ConfigManager.Config.Preferences.GameFolder);
				}
				foreach(RecentItem recentItem in ConfigManager.Config.RecentFiles.Items) {
					EmuApi.AddKnownGameFolder(recentItem.RomFile.Folder);
				}

				ConfigManager.Config.Preferences.UpdateFileAssociations();
				SingleInstance.Instance.ArgumentsReceived += Instance_ArgumentsReceived;

				Dispatcher.UIThread.Post(() => {
					//Subscribe to software renderer toggle so it applies without restarting
					_softwareRendererSubscription = ConfigManager.Config.Video
						.WhenAnyValue(v => v.UseSoftwareRenderer)
						.Skip(1)
						.Subscribe(useSoftware => {
							bool wasSoftware = _usesSoftwareRenderer;
							_usesSoftwareRenderer = useSoftware || OperatingSystem.IsMacOS();
							if(_usesSoftwareRenderer != wasSoftware) {
								EmuApi.SetSoftwareRendererMode(_usesSoftwareRenderer);
								_mouseManager?.Dispose();
								_mouseManager = new MouseManager(this, _usesSoftwareRenderer ? _softwareRenderer : _renderer, _mainMenu, _usesSoftwareRenderer);
							}
						});
				});

				Dispatcher.UIThread.Post(() => {
					cmdLine.LoadFiles();
					cmdLine.OnAfterInit(this);

					//Update check disabled in Mesen Orion - it points at the upstream Mesen
					//server (mesen.ca), not this fork. Re-enable once a fork release feed exists.
					//if(ConfigManager.Config.Preferences.AutomaticallyCheckForUpdates) {
					//	_model.MainMenu.CheckForUpdate(this, true);
					//}
				});
			});
		}

		private void Instance_ArgumentsReceived(object? sender, ArgumentsReceivedEventArgs e)
		{
			Dispatcher.UIThread.Post(() => {
				CommandLineHelper cmdLine = new(e.Args, false);

				//Set _cmdLine to allow Lua scripts to be loaded once/if a game is loaded
				_cmdLine = cmdLine;

				ConfigManager.Config.ApplyConfig();
				cmdLine.LoadFiles();
			});
		}

		private void OnNotification(NotificationEventArgs e)
		{
			DebugWindowManager.ProcessNotification(e);

			switch(e.NotificationType) {
				case ConsoleNotificationType.GameLoaded:
					CheatCodes.ApplyCheats();
					RomInfo romInfo = EmuApi.GetRomInfo();
					
					Dispatcher.UIThread.Post(() => {
						bool wasAudioFile = _model.AudioPlayer != null;
						bool updateConfig = _model.RomInfo.Format != romInfo.Format;
						_model.RomInfo = romInfo;

						if(updateConfig) {
							//Make sure any config overrides (video filter/aspect ratio) are applied when loading a different file
							ConfigManager.Config.Video.ApplyConfig();
						}

						bool isAudioFile = _model.AudioPlayer != null;
						if(wasAudioFile != isAudioFile) {
							//Force window size update when switching between an audio file and a regular rom
							Dispatcher.UIThread.Post(() => {
								ProcessResolutionChange();
							});
						}
					});

					GameConfig.LoadGameConfig(romInfo).ApplyConfig();

					GameLoadedEventParams evtParams = Marshal.PtrToStructure<GameLoadedEventParams>(e.Parameter);
					if(!evtParams.IsPowerCycle) {
						Dispatcher.UIThread.Post(() => {
							_model.RecentGames.Visible = false;
							if(IsKeyboardFocusWithin || IsActive || ApplicationHelper.GetActiveOrMainWindow() == this) {
								this.GetControl<Panel>("RendererPanel").Focus();
							}

							DispatcherTimer.RunOnce(() => {
								if(_cmdLine != null) {
									_cmdLine?.ProcessPostLoadCommandSwitches(this);
									_cmdLine = null;
								}

								if(WindowState == WindowState.FullScreen || WindowState == WindowState.Maximized) {
									//Force resize of renderer when loading a game while in fullscreen
									//Prevents some issues when fullscreen was turned on before loading a game, etc.
									_rendererSize = new Size();
									ResizeRenderer();
								}
							}, TimeSpan.FromMilliseconds(50));
						});
					}

					Dispatcher.UIThread.Post(() => {
						ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.Close();
					});

					LoadRomHelper.ResetReloadCounter();
					break;

				case ConsoleNotificationType.GameLoadFailed:
					LoadRomHelper.ResetReloadCounter();
					break;

				case ConsoleNotificationType.DebuggerResumed:
				case ConsoleNotificationType.GameResumed:
					Dispatcher.UIThread.Post(() => {
						_model.RecentGames.Visible = false;
						if(IsKeyboardFocusWithin) {
							this.GetControl<Panel>("RendererPanel").Focus();
						}
					});
					break;

				case ConsoleNotificationType.RequestConfigChange:
					Dispatcher.UIThread.Post(() => {
						UpdateInputConfiguration();
					});
					break;

				case ConsoleNotificationType.EmulationStopped:
					Dispatcher.UIThread.Post(() => {
						_model.RomInfo = new RomInfo();
						_model.RecentGames.Init(GameScreenMode.RecentGames);
					});
					break;

				case ConsoleNotificationType.ResolutionChanged:
					Dispatcher.UIThread.Post(() => {
						ProcessResolutionChange();
					});
					break;

				case ConsoleNotificationType.ExecuteShortcut:
					ExecuteShortcutParams p = Marshal.PtrToStructure<ExecuteShortcutParams>(e.Parameter);
					Dispatcher.UIThread.Post(() => {
						_shortcutHandler.ExecuteShortcut(p.Shortcut);
					});
					break;

				case ConsoleNotificationType.MissingFirmware: {
					MissingFirmwareMessage msg = Marshal.PtrToStructure<MissingFirmwareMessage>(e.Parameter);
					TaskCompletionSource tcs = new TaskCompletionSource();
					Dispatcher.UIThread.Post(async () => {
						await FirmwareHelper.RequestFirmwareFile(msg);
						tcs.SetResult();
					});
					tcs.Task.Wait();
					break;
				}

				case ConsoleNotificationType.SufamiTurboFilePrompt: {
					SufamiTurboFilePromptMessage msg = Marshal.PtrToStructure<SufamiTurboFilePromptMessage>(e.Parameter);
					TaskCompletionSource tcs = new TaskCompletionSource();
					Dispatcher.UIThread.Post(async () => {
						if(await MesenMsgBox.Show(this, "PromptLoadSufamiTurbo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) {
							string? selectedFile = await FileDialogHelper.OpenFile(null, this, FileDialogHelper.SufamiTurboExt);
							if(selectedFile != null) {
								byte[] file = Encoding.UTF8.GetBytes(selectedFile);
								Array.Copy(file, msg.Filename, file.Length);
								Marshal.StructureToPtr<SufamiTurboFilePromptMessage>(msg, e.Parameter, false);
							}
						}
						tcs.SetResult();
					});
					tcs.Task.Wait();
					break;
				}

				case ConsoleNotificationType.BeforeGameLoad:
					Dispatcher.UIThread.Post(() => {
						ApplicationHelper.GetExistingWindow<HdPackBuilderWindow>()?.Close();
					});
					break;

				case ConsoleNotificationType.RefreshSoftwareRenderer:
					SoftwareRendererFrame frame = Marshal.PtrToStructure<SoftwareRendererFrame>(e.Parameter);
					_softwareRenderer.UpdateSoftwareRenderer(frame);
					break;
			}
		}

		private static void UpdateInputConfiguration()
		{
			//Used to update input devices when the core requests changes (NES-only for now)
			ConfigManager.Config.Nes.UpdateInputFromCoreConfig();
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private void ProcessResolutionChange()
		{
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			FrameInfo baseScreenSize = EmuApi.GetBaseScreenSize();
			if(WindowState == WindowState.Normal) {
				double menuHeight = ConfigManager.Config.Preferences.AutoHideMenu ? 0 : _mainMenu.Bounds.Height;
				double height = ClientSize.Height - menuHeight - _audioPlayer.Bounds.Height;
				if(baseScreenSize.Width == _prevScreenSize.Height && baseScreenSize.Height == _prevScreenSize.Width) {
					//Rotation, swap sizes without changing scale
					double xScale = ClientSize.Width * dpiScale / _prevScreenSize.Width;
					double yScale = height * dpiScale / _prevScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale), Math.Round(yScale)));
				} else {
					double xScale = ClientSize.Width * dpiScale / baseScreenSize.Width;
					double yScale = height * dpiScale / baseScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale), Math.Round(yScale)));
				}
			} else if(WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) {
				if(_rendererSize == default) {
					ResizeRenderer();
				} else {
					double xScale = _rendererSize.Width * dpiScale / baseScreenSize.Width;
					double yScale = _rendererSize.Height * dpiScale / baseScreenSize.Height;
					SetScale(Math.Min(Math.Round(xScale, 2), Math.Round(yScale, 2)));
				}
			}
			_prevScreenSize = baseScreenSize;
		}

		public void SetScale(double scale)
		{
			if(scale < 1) {
				scale = 1;
			}

			//TODOv2 - Calling this twice seems to fix what might be an issue in Avalonia?
			//On the first call, when DPI > 100%, sometimes _rendererPanel's bounds are incorrect
			InternalSetScale(scale);
			InternalSetScale(scale);
		}

		private void InternalSetScale(double scale)
		{
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			double aspectRatio = EmuApi.GetAspectRatio();

			FrameInfo screenSize = EmuApi.GetBaseScreenSize();
			if(WindowState == WindowState.Normal) {
				_rendererSize = new Size();

				//When menu is set to auto-hide, don't count its height when calculating the window's final size
				double menuHeight = ConfigManager.Config.Preferences.AutoHideMenu ? 0 : _mainMenu.Bounds.Height;

				double width = Math.Max(MinWidth, Math.Round(screenSize.Height * aspectRatio * scale) / dpiScale);
				double height = Math.Max(MinHeight, screenSize.Height * scale / dpiScale);
				Width = width;
				Height = height + menuHeight + _audioPlayer.Bounds.Height;
				ResizeRenderer();
			} else if(WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) {
				_rendererSize = new Size(Math.Round(screenSize.Width * scale * aspectRatio) / dpiScale, Math.Round(screenSize.Height * scale) / dpiScale);
				ResizeRenderer();
			}
		}

		private void ResizeRenderer()
		{
			_rendererPanel.InvalidateMeasure();
			_rendererPanel.InvalidateArrange();
		}

		private void RendererPanel_LayoutUpdated(object? sender, EventArgs e)
		{
			double aspectRatio = EmuApi.GetAspectRatio();
			double dpiScale = LayoutHelper.GetLayoutScale(this);

			Size finalSize = _rendererSize == default ? _rendererPanel.Bounds.Size : _rendererSize;
			double height = finalSize.Height;
			double width = finalSize.Height * aspectRatio;
			if(Math.Round(width) > Math.Round(finalSize.Width)) {
				//Use renderer width to calculate the height instead of the opposite
				//when current window dimensions would cause cropping horizontally
				//if the screen width was calculated based on the height.
				width = finalSize.Width;
				height = width / aspectRatio;
			}

			if(ConfigManager.Config.Video.FullscreenForceIntegerScale && VisualRoot is Window wnd && (wnd.WindowState == WindowState.FullScreen || wnd.WindowState == WindowState.Maximized)) {
				FrameInfo baseSize = EmuApi.GetBaseScreenSize();
				double scale = height * dpiScale / baseSize.Height;
				if(scale != Math.Floor(scale)) {
					height = baseSize.Height * Math.Max(1, Math.Floor(scale / dpiScale));
					width = height * aspectRatio;
				}
			}

			uint realWidth = (uint)Math.Round(width * dpiScale);
			uint realHeight = (uint)Math.Round(height * dpiScale);
			EmuApi.SetRendererSize(realWidth, realHeight);
			_model.RendererSize = new Size(realWidth, realHeight);

			_renderer.Width = width;
			_renderer.Height = height;
			_model.SoftwareRenderer.Width = width;
			_model.SoftwareRenderer.Height = height;

			if(_toastWindow != null && _toastWindow.IsVisible) {
				PositionToastWindow();
			}
			if(_trackerWindow != null && _trackerWindow.IsVisible) {
				PositionTrackerWindow();
			}
		}

		private void PositionTrackerWindow()
		{
			if(_trackerWindow == null) {
				return;
			}
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			const double trackerHeight = 28;
			const double margin = 16;

			var panelBounds = _rendererPanel.Bounds;
			var panelBottomLeft = _rendererPanel.TranslatePoint(new Point(0, panelBounds.Height), this);
			double leftDip = panelBottomLeft.HasValue ? panelBottomLeft.Value.X : panelBounds.Left;
			double bottomDip = panelBottomLeft.HasValue ? panelBottomLeft.Value.Y : panelBounds.Bottom;

			int x = (int)Math.Round(Position.X + (leftDip + margin) * dpiScale);
			int y = (int)Math.Round(Position.Y + (bottomDip - trackerHeight - margin) * dpiScale);
			_trackerWindow.Position = new PixelPoint(x, y);
		}

		private void PositionToastWindow()
		{
			if(_toastWindow == null) {
				return;
			}
			double dpiScale = LayoutHelper.GetLayoutScale(this);
			// Approximate toast content size in DIPs (matches the XAML layout)
			const double toastWidth = 250;
			const double toastHeight = 72;
			const double margin = 16;

			// TranslatePoint returns coordinates in DIPs relative to this window's client area.
			// this.Position is in screen pixels. Multiply DIPs by dpiScale to get screen pixels.
			var panelBounds = _rendererPanel.Bounds;
			var panelBottomRight = _rendererPanel.TranslatePoint(new Point(panelBounds.Width, panelBounds.Height), this);
			double rightDip = panelBottomRight.HasValue ? panelBottomRight.Value.X : panelBounds.Right;
			double bottomDip = panelBottomRight.HasValue ? panelBottomRight.Value.Y : panelBounds.Bottom;

			int x = (int)Math.Round(Position.X + (rightDip - toastWidth - margin) * dpiScale);
			int y = (int)Math.Round(Position.Y + (bottomDip - toastHeight - margin) * dpiScale);
			_toastWindow.Position = new PixelPoint(x, y);
		}

		private void EnqueueToast(ToastInfo toast)
		{
			if(!ConfigManager.Config.RetroAchievements.EnableNotifications) {
				return;
			}
			_toastQueue.Enqueue(toast);
			ShowNextToast();
		}

		//Displays the next queued toast (achievement/mastery/leaderboard), if one isn't showing.
		private void ShowNextToast()
		{
			if(_toastShowing || _toastQueue.Count == 0) {
				return;
			}

			ToastInfo toast = _toastQueue.Dequeue();
			_toastShowing = true;

			_model.AchievementToastBadge = toast.Badge;
			_model.AchievementToastHasBadge = toast.Badge != null;
			_model.AchievementToastHeader = toast.Header;
			_model.AchievementToastTitle = toast.Title;
			_model.AchievementToastPoints = toast.Subtitle;
			_model.AchievementToastOpacity = 0;
			if(_toastWindow != null) {
				// Windows: show the separate Topmost window positioned over the bottom-right
				// corner of the main window (above the native renderer HWND).
				// Set the badge image directly (bypasses compiled-binding IImage coercion).
				_toastWindow.SetBadge(toast.Badge);
				PositionToastWindow();
				if(!_toastWindow.IsVisible) {
					_toastWindow.Show(this);
				}
			} else {
				_model.AchievementToastVisible = true;
			}
			//Fade in shortly after the popup/window opens, then auto-hide via the timer tick
			DispatcherTimer.RunOnce(() => _model.AchievementToastOpacity = 1, TimeSpan.FromMilliseconds(30));
			_achievementToastTimer.Stop();
			_achievementToastTimer.Start();
		}

		//----- RetroAchievements: mastery, leaderboard rank, challenge & progress indicators -----

		private void OnGameMastered(string msg)
		{
			string[] f = msg.Split('\x1f');
			string title = f.Length > 0 ? f[0] : "";
			string badgeUrl = f.Length > 1 ? f[1] : "";
			ShowToastWithBadge("Game Mastered!", title, "All achievements unlocked", badgeUrl);
		}

		private void OnLeaderboardScoreboard(string msg)
		{
			string[] f = msg.Split('\x1f');
			string title = f.Length > 0 ? f[0] : "";
			string score = f.Length > 1 ? f[1] : "";
			string rank = f.Length > 2 ? f[2] : "";
			string total = f.Length > 3 ? f[3] : "";
			string subtitle = score;
			if(rank.Length > 0) {
				subtitle += "   #" + rank + (total.Length > 0 ? " / " + total : "");
			}
			EnqueueToast(new ToastInfo() { Header = "Leaderboard: " + title, Title = subtitle, Subtitle = "", Badge = null });
		}

		//Loads the badge then enqueues the toast (used by mastery, which carries an image URL)
		private async void ShowToastWithBadge(string header, string title, string subtitle, string badgeUrl)
		{
			Avalonia.Media.Imaging.Bitmap? badge = badgeUrl.Length > 0 ? await RetroAchievementsApi.GetImageAsync(badgeUrl) : null;
			EnqueueToast(new ToastInfo() { Header = header, Title = title, Subtitle = subtitle, Badge = badge });
		}

		private async void OnChallengeShow(string msg)
		{
			//Always track the primed achievement (even when the indicators are hidden), so re-enabling
			//the feature can restore the currently-active set without waiting for the core to re-fire.
			//id <0x1F> badgeUrl <0x1F> title <0x1F> description
			string[] f = msg.Split('\x1f');
			if(f.Length < 2 || !uint.TryParse(f[0], out uint id) || _challengeBadges.ContainsKey(id)) {
				return;
			}
			RaChallengeIndicator indicator = new RaChallengeIndicator() {
				Id = id,
				Title = f.Length > 2 ? f[2] : "",
				Description = f.Length > 3 ? f[3] : "",
				Badge = await RetroAchievementsApi.GetImageAsync(f[1])
			};
			//Re-check after the await: the indicator may have been hidden meanwhile
			if(indicator.Badge == null || _challengeBadges.ContainsKey(id)) {
				return;
			}
			_challengeBadges[id] = indicator;
			if(ConfigManager.Config.RetroAchievements.EnableChallengeIndicators) {
				_model.ChallengeBadges.Add(indicator);
				_model.ChallengeVisible = _model.ChallengeBadges.Count > 0;
			}
		}

		private void OnChallengeHide(string msg)
		{
			if(uint.TryParse(msg, out uint id) && _challengeBadges.TryGetValue(id, out var indicator)) {
				_challengeBadges.Remove(id);
				_model.ChallengeBadges.Remove(indicator);
				_model.ChallengeVisible = _model.ChallengeBadges.Count > 0;
			}
		}

		//Rebuilds the visible challenge overlay from the tracked set (after the feature is toggled).
		private void RefreshChallengeOverlay()
		{
			_model.ChallengeBadges.Clear();
			if(ConfigManager.Config.RetroAchievements.EnableChallengeIndicators) {
				foreach(RaChallengeIndicator indicator in _challengeBadges.Values) {
					_model.ChallengeBadges.Add(indicator);
				}
			}
			_model.ChallengeVisible = _model.ChallengeBadges.Count > 0;
		}

		private async void OnProgressShow(string msg)
		{
			string[] f = msg.Split('\x1f');
			string badgeUrl = f.Length > 0 ? f[0] : "";
			string progress = f.Length > 1 ? f[1] : "";
			_model.ProgressBadge = badgeUrl.Length > 0 ? await RetroAchievementsApi.GetImageAsync(badgeUrl) : null;
			_model.ProgressText = progress;
			_model.ProgressVisible = true;
			DispatcherTimer.RunOnce(() => _model.ProgressOpacity = 1, TimeSpan.FromMilliseconds(30));
			_progressToastTimer.Stop();
			_progressToastTimer.Start();
		}

		private void OnProgressHide()
		{
			_progressToastTimer.Stop();
			_progressToastTimer.Start();
		}

		private void OnWindowStateChanged()
		{
			_rendererSize = new Size();
			ResizeRenderer();
		}

		public void ToggleFullscreen()
		{
			if(_preventFullscreenToggle) {
				return;
			}

			_preventFullscreenToggle = true;
			if(WindowState == WindowState.FullScreen) {
				Task.Run(() => {
					if(ConfigManager.Config.Video.UseExclusiveFullscreen) {
						EmuApi.SetExclusiveFullscreenMode(false, _renderer.Handle);
					}

					Dispatcher.UIThread.Post(() => {
						WindowState = _prevWindowState;
						if(_prevWindowState == WindowState.Normal) {
							Width = _originalSize.Width;
							Height = _originalSize.Height;
							Position = _originalPos;
						}
						_preventFullscreenToggle = false;
					});
				});
			} else {
				_originalSize = ClientSize;
				_originalPos = Position;
				_prevWindowState = WindowState;

				if(ConfigManager.Config.Video.UseExclusiveFullscreen) {
					if(!EmuApi.IsRunning()) {
						//Prevent entering fullscreen mode until a game is loaded
						_preventFullscreenToggle = false;
						return;
					}

					Task.Run(() => {
						EmuApi.SetExclusiveFullscreenMode(true, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
						_preventFullscreenToggle = false;

						Dispatcher.UIThread.Post(() => {
							WindowState = WindowState.FullScreen;
						});
					});
				} else {
					WindowState = WindowState.FullScreen;
					_preventFullscreenToggle = false;
				}
			}
		}

		protected override void OnLostFocus(RoutedEventArgs e)
		{
			base.OnLostFocus(e);
			if(WindowState == WindowState.FullScreen && ConfigManager.Config.Video.UseExclusiveFullscreen) {
				ToggleFullscreen();
			}
		}

		private bool ProcessTestModeShortcuts(Key key)
		{
			if(key == Key.F1) {
				if(TestApi.RomTestRecording()) {
					TestApi.RomTestStop();
				} else {
					RomTestHelper.RecordTest();
				}
				return true;
			} else if(key == Key.F2) {
				RomTestHelper.RunTest();
				return true;
			} else if(key == Key.F3) {
				RomTestHelper.RunAllTests();
				return true;
			} else if(key == Key.F7) {
				RomTestHelper.RunGbMicroTests();
				return true;
			} else if(key == Key.F8) {
				RomTestHelper.RunGambatteTests();
				return true;
			} else if(key == Key.F6) {
				//For testing purposes (to test for memory leaks)
				Task.Run(() => {
					for(int i = 0; i < 50; i++) {
						GC.Collect();
						GC.WaitForPendingFinalizers();
						Thread.Sleep(10);
					}
				});
				return true;
			}
			return false;
		}

		private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
		{
			if(_testModeEnabled && e.KeyModifiers == KeyModifiers.Alt && ProcessTestModeShortcuts(e.Key)) {
				return;
			}

			if(OperatingSystem.IsMacOS()) {
				//Keyhandler handles key internally on macOS
				return;
			}

			if(_focusInMenu) {
				return;
			}

			if(e.Key != Key.None) {
				_keyPressedStamp[e.Key] = _stopWatch.ElapsedTicks;

				if(_isLinux && _pendingKeyUpEvents.TryGetValue(e.Key, out IDisposable? cancelTimer)) {
					//Cancel any pending key up event
					cancelTimer.Dispose();
					_pendingKeyUpEvents.Remove(e.Key);
				}

				InputApi.SetKeyState((UInt16)e.Key, true);
			}

			if(e.Key == Key.Tab || e.Key == Key.F10) {
				//Prevent menu/window from handling these keys to avoid issue with custom shortcuts
				e.Handled = true;
			}
		}

		private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
		{
			if(OperatingSystem.IsMacOS()) {
				//Keyhandler handles key internally on macOS
				return;
			}

			if(e.Key != Key.None) {
				if(e.Key.IsSpecialKey() && (!_keyPressedStamp.TryGetValue(e.Key, out long stamp) || ((_stopWatch.ElapsedTicks - stamp) * 1000 / Stopwatch.Frequency) < 10)) {
					//Key up received without key down, or key pressed for less than 10 ms, pretend the key was pressed for 50ms
					//Some special keys can behave this way (e.g printscreen)
					InputApi.SetKeyState((UInt16)e.Key, true);
					DispatcherTimer.RunOnce(() => InputApi.SetKeyState((UInt16)e.Key, false), TimeSpan.FromMilliseconds(50), DispatcherPriority.MaxValue);
					_keyPressedStamp.Remove(e.Key);
					return;
				}

				_keyPressedStamp.Remove(e.Key);

				if(_isLinux) {
					//Process keyup events after 1ms on Linux to prevent key repeat from triggering key up/down repeatedly
					IDisposable cancelTimer = DispatcherTimer.RunOnce(() => InputApi.SetKeyState((UInt16)e.Key, false), TimeSpan.FromMilliseconds(1), DispatcherPriority.MaxValue);
					_pendingKeyUpEvents[e.Key] = cancelTimer;
				} else {
					InputApi.SetKeyState((UInt16)e.Key, false);
				}
			}
		}

		private void OnActiveChanged()
		{
			if(!_isClosing) {
				ConfigApi.SetEmulationFlag(EmulationFlags.InBackground, !IsActive);
				InputApi.ResetKeyState();
			}
		}

		private void timerUpdateBackgroundFlag(object? sender, EventArgs e)
		{
			_model.UpdateStatusBar();

			Window? activeWindow = ApplicationHelper.GetActiveWindow();

			PreferencesConfig cfg = ConfigManager.Config.Preferences;

			bool focusInMenu = MenuHelper.IsFocusInMenu(_mainMenu.MainMenu);
			if(focusInMenu && !_focusInMenu) {
				InputApi.ResetKeyState();
			}
			_focusInMenu = focusInMenu;

			bool needPause = activeWindow == null && cfg.PauseWhenInBackground;
			if(activeWindow != null) {
				bool isConfigWindow = (activeWindow != this) && !DebugWindowManager.IsDebugWindow(activeWindow);
				needPause |= cfg.PauseWhenInMenusAndConfig && !isConfigWindow && _mainMenu.MainMenu.IsOpen; //in main menu
				needPause |= cfg.PauseWhenInMenusAndConfig && isConfigWindow; //in a window that's neither the main window nor a debug tool
			}

			if(needPause) {
				if(!EmuApi.IsPaused()) {
					_needResume = true;

					DebuggerWindow? wnd = DebugWindowManager.GetDebugWindow<DebuggerWindow>(x => x.CpuType == _model.RomInfo.ConsoleType.GetMainCpuType());
					if(wnd != null) {
						//If the debugger window for the main cpu is opened, suppress the "bring to front on break" behavior
						wnd.SuppressBringToFront();
					}

					EmuApi.Pause();
				}
			} else if(_needResume) {
				//Don't resume if the load/save state dialog is opened
				if(!_model.RecentGames.Visible) {
					EmuApi.Resume();
					_needResume = false;
				}
			}
		}
	}
}
