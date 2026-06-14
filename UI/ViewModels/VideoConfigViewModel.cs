using Avalonia.Controls;
using Mesen.Config;
using Mesen.Config.Shortcuts;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;

namespace Mesen.ViewModels
{
	public class VideoConfigViewModel : DisposableViewModel
	{
		[ObservableAsProperty] public bool ShowCustomRatio { get; }
		[ObservableAsProperty] public bool ShowNtscBlarggSettings { get; }
		[ObservableAsProperty] public bool ShowNtscBisqwitSettings { get; }
		[ObservableAsProperty] public bool ShowLcdGridSettings { get; }
		public bool IsWindows { get; }
		public bool IsMacOs { get; }
		public bool IsLinux { get; }

		//Available GLSL shaders (Linux/OpenGL renderer), with "None" first.
		public List<string> AvailableShaders { get; private set; } = new List<string>() { "None" };

		//Shaders the user marked as favorite (shown in the Shaders tab).
		public ObservableCollection<string> FavoriteShaders { get; } = new ObservableCollection<string>();

		//Human-readable description of the shader-switching shortcut keys.
		public string ShaderShortcutsText { get; private set; } = "";

		//Tweakable parameters (#pragma parameter) of the active shader.
		public ObservableCollection<ShaderParamViewModel> ShaderParams { get; } = new ObservableCollection<ShaderParamViewModel>();
		public ReactiveCommand<Unit, Unit> RefreshShaderParamsCommand { get; }
		public ReactiveCommand<Unit, Unit> ResetShaderParamsCommand { get; }

		public ReactiveCommand<Unit, Unit> AddFavoriteCommand { get; }
		public ReactiveCommand<string, Unit> RemoveFavoriteCommand { get; }
		public ReactiveCommand<string, Unit> ApplyShaderCommand { get; }

		//Custom picture presets (Picture tab)
		public ObservableCollection<PicturePreset> PicturePresets { get; } = new ObservableCollection<PicturePreset>();
		[Reactive] public string NewPresetName { get; set; } = "";
		public ReactiveCommand<Unit, Unit> SavePresetCommand { get; }
		public ReactiveCommand<PicturePreset, Unit> ApplyPresetCommand { get; }
		public ReactiveCommand<PicturePreset, Unit> DeletePresetCommand { get; }
		public ReactiveCommand<PicturePreset, Unit> RenamePresetCommand { get; }
		public ReactiveCommand<PicturePreset, Unit> MovePresetUpCommand { get; }
		public ReactiveCommand<PicturePreset, Unit> MovePresetDownCommand { get; }
		public ReactiveCommand<Unit, Unit> ExportCustomizationsCommand { get; }
		public ReactiveCommand<Unit, Unit> ImportCustomizationsCommand { get; }

		public ReactiveCommand<Unit, Unit> PresetCompositeCommand { get; }
		public ReactiveCommand<Unit, Unit> PresetSVideoCommand { get; }
		public ReactiveCommand<Unit, Unit> PresetRgbCommand { get; }
		public ReactiveCommand<Unit, Unit> PresetMonochromeCommand { get; }
		public ReactiveCommand<Unit, Unit> ResetPictureSettingsCommand { get; }

		[Reactive] public VideoConfig Config { get; set; }
		[Reactive] public VideoConfig OriginalConfig { get; set; }
		public UInt32[] AvailableRefreshRates { get; } = new UInt32[] { 50, 60, 75, 100, 120, 144, 200, 240, 360 };

		public VideoConfigViewModel()
		{
			Config = ConfigManager.Config.Video;

			//Sync the selected shader from the actually-running shader before snapshotting
			//OriginalConfig. Otherwise an in-game shader change (hotkey) wouldn't be reflected
			//here, and clicking Cancel would revert to a stale value and drop the active shader.
			if(OperatingSystem.IsLinux() || OperatingSystem.IsWindows()) {
				string runningShader = EmuApi.GetCurrentShader();
				if(!string.IsNullOrEmpty(runningShader) && runningShader != Config.Shader) {
					Config.Shader = runningShader;
				}
			}

			OriginalConfig = Config.Clone();

			PresetCompositeCommand = ReactiveCommand.Create(() => SetNtscPreset(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15, false));
			PresetSVideoCommand = ReactiveCommand.Create(()=> SetNtscPreset(0, 0, 0, 0, 20, 0, 20, -100, -100, 0, 15, false));
			PresetRgbCommand = ReactiveCommand.Create(() => SetNtscPreset(0, 0, 0, 0, 20, 0, 70, -100, -100, -100, 15, false));
			PresetMonochromeCommand = ReactiveCommand.Create(() => SetNtscPreset(0, -100, 0, 0, 20, 0, 70, -20, -20, -10, 15, false));
			ResetPictureSettingsCommand = ReactiveCommand.Create(() => ResetPictureSettings());

			AddFavoriteCommand = ReactiveCommand.Create(() => AddCurrentShaderToFavorites());
			RemoveFavoriteCommand = ReactiveCommand.Create((string name) => RemoveFavorite(name));
			ApplyShaderCommand = ReactiveCommand.Create((string name) => { if(!string.IsNullOrEmpty(name)) { Config.Shader = name; } });

			SavePresetCommand = ReactiveCommand.Create(() => SaveCurrentAsPreset());
			ApplyPresetCommand = ReactiveCommand.Create((PicturePreset preset) => { if(preset != null) { Config.ApplyPicturePreset(preset); } });
			DeletePresetCommand = ReactiveCommand.Create((PicturePreset preset) => DeletePreset(preset));
			RenamePresetCommand = ReactiveCommand.Create((PicturePreset preset) => RenamePreset(preset));
			MovePresetUpCommand = ReactiveCommand.Create((PicturePreset preset) => MovePreset(preset, -1));
			MovePresetDownCommand = ReactiveCommand.Create((PicturePreset preset) => MovePreset(preset, 1));
			ExportCustomizationsCommand = ReactiveCommand.CreateFromTask(ExportCustomizationsAsync);
			ImportCustomizationsCommand = ReactiveCommand.CreateFromTask(ImportCustomizationsAsync);
			RefreshShaderParamsCommand = ReactiveCommand.Create(() => RefreshShaderParams());
			ResetShaderParamsCommand = ReactiveCommand.Create(() => ResetShaderParams());

			foreach(PicturePreset preset in Config.PicturePresets ?? new List<PicturePreset>()) {
				PicturePresets.Add(preset);
			}

			AddDisposable(this.WhenAnyValue(_ => _.Config.AspectRatio).Select(_ => _ == VideoAspectRatio.Custom).ToPropertyEx(this, _ => _.ShowCustomRatio));
			AddDisposable(this.WhenAnyValue(_ => _.Config.VideoFilter).Select(_ => _ == VideoFilterType.NtscBlargg).ToPropertyEx(this, _ => _.ShowNtscBlarggSettings));
			AddDisposable(this.WhenAnyValue(_ => _.Config.VideoFilter).Select(_ => _ == VideoFilterType.NtscBisqwit).ToPropertyEx(this, _ => _.ShowNtscBisqwitSettings));
			AddDisposable(this.WhenAnyValue(_ => _.Config.VideoFilter).Select(_ => _ == VideoFilterType.LcdGrid).ToPropertyEx(this, _ => _.ShowLcdGridSettings));
			AddDisposable(this.WhenAnyValue(_ => _.Config.UseSoftwareRenderer).Subscribe(softwareRenderer => {
				if(softwareRenderer) {
					//Not supported
					Config.UseExclusiveFullscreen = false;
					Config.VerticalSync = false;
				}
			}));

			//Exclusive fullscreen is only supported on Windows currently
			IsWindows = OperatingSystem.IsWindows();

			//MacOS only supports the software renderer
			IsMacOs = OperatingSystem.IsMacOS();

			//GLSL shaders support
			IsLinux = OperatingSystem.IsLinux();
			bool loadShaders = IsLinux || IsWindows;
			if(loadShaders) {
				EmuApi.RefreshShaderList();
				List<string> shaders = new List<string>() { "None" };
				shaders.AddRange(EmuApi.GetShaderList());
				//Ensure the currently configured shader is selectable even if the file is missing
				if(!string.IsNullOrEmpty(Config.Shader) && Config.Shader != "None" && !shaders.Contains(Config.Shader)) {
					shaders.Add(Config.Shader);
				}
				AvailableShaders = shaders;

				foreach(string fav in Config.FavoriteShaders ?? new List<string>()) {
					FavoriteShaders.Add(fav);
				}
				ShaderShortcutsText = BuildShaderShortcutsText();
			}

			if(Design.IsDesignMode) {
				return;
			}

			AddDisposable(ReactiveHelper.RegisterRecursiveObserver(Config, (s, e) => { Config.ApplyConfig(); }));
		}

		private void SetNtscPreset(int hue, int saturation, int contrast, int brightness, int sharpness, int gamma, int resolution, int artifacts, int fringing, int bleed, int scanlines, bool mergeFields)
		{
			Config.VideoFilter = VideoFilterType.NtscBlargg;
			Config.Hue = hue;
			Config.Saturation = saturation;
			Config.Contrast = contrast;
			Config.Brightness = brightness;
			Config.NtscSharpness = sharpness;
			Config.NtscGamma = gamma;
			Config.NtscResolution = resolution;
			Config.NtscArtifacts = artifacts;
			Config.NtscFringing = fringing;
			Config.NtscBleed = bleed;
			Config.NtscMergeFields = mergeFields;

			Config.ScanlineIntensity = scanlines;
		}

		private void ResetPictureSettings()
		{
			SetNtscPreset(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
			Config.NtscScale = NtscBisqwitFilterScale._2x;
			Config.NtscYFilterLength = 0;
			Config.NtscIFilterLength = 50;
			Config.NtscQFilterLength = 50;
			Config.VideoFilter = VideoFilterType.None;

			Config.LcdGridTopLeftBrightness = 100;
			Config.LcdGridTopRightBrightness = 85;
			Config.LcdGridBottomLeftBrightness = 85;
			Config.LcdGridBottomRightBrightness = 85;
		}

		private void AddCurrentShaderToFavorites()
		{
			string shader = Config.Shader;
			if(string.IsNullOrEmpty(shader) || shader == "None" || FavoriteShaders.Contains(shader)) {
				return;
			}
			FavoriteShaders.Add(shader);
			SyncFavoritesToConfig();
		}

		private void RemoveFavorite(string name)
		{
			if(name != null && FavoriteShaders.Remove(name)) {
				SyncFavoritesToConfig();
			}
		}

		private void SyncFavoritesToConfig()
		{
			Config.FavoriteShaders = new List<string>(FavoriteShaders);
			//Push to the core immediately so in-game favorite cycling reflects the change.
			EmuApi.SetFavoriteShaders(string.Join("[!|!]", FavoriteShaders));
		}

		private void SaveCurrentAsPreset()
		{
			string name = (NewPresetName ?? "").Trim();
			if(name.Length == 0) {
				name = "Preset " + (PicturePresets.Count + 1);
			}

			PicturePreset preset = new PicturePreset() {
				Name = name,
				Brightness = Config.Brightness,
				Contrast = Config.Contrast,
				Hue = Config.Hue,
				Saturation = Config.Saturation,
				ScanlineIntensity = Config.ScanlineIntensity,
				UseBilinearInterpolation = Config.UseBilinearInterpolation
			};

			//Replace an existing preset with the same name, otherwise add a new one.
			int existing = -1;
			for(int i = 0; i < PicturePresets.Count; i++) {
				if(PicturePresets[i].Name == name) {
					existing = i;
					break;
				}
			}
			if(existing >= 0) {
				PicturePresets[existing] = preset;
			} else {
				PicturePresets.Add(preset);
			}

			NewPresetName = "";
			SyncPresetsToConfig();
		}

		private void DeletePreset(PicturePreset preset)
		{
			if(preset != null && PicturePresets.Remove(preset)) {
				SyncPresetsToConfig();
			}
		}

		private void RenamePreset(PicturePreset preset)
		{
			string name = (NewPresetName ?? "").Trim();
			if(preset == null || name.Length == 0) {
				return;
			}
			int idx = PicturePresets.IndexOf(preset);
			if(idx < 0) {
				return;
			}
			//Rebuild the entry so the ListBox refreshes its displayed name.
			preset.Name = name;
			PicturePresets[idx] = new PicturePreset() {
				Name = name,
				Brightness = preset.Brightness,
				Contrast = preset.Contrast,
				Hue = preset.Hue,
				Saturation = preset.Saturation,
				ScanlineIntensity = preset.ScanlineIntensity,
				UseBilinearInterpolation = preset.UseBilinearInterpolation
			};
			NewPresetName = "";
			SyncPresetsToConfig();
		}

		private void MovePreset(PicturePreset preset, int delta)
		{
			if(preset == null) {
				return;
			}
			int idx = PicturePresets.IndexOf(preset);
			int newIdx = idx + delta;
			if(idx < 0 || newIdx < 0 || newIdx >= PicturePresets.Count) {
				return;
			}
			PicturePresets.Move(idx, newIdx);
			SyncPresetsToConfig();
		}

		private void SyncPresetsToConfig()
		{
			//Persist presets immediately and also update the snapshot so they survive a
			//Cancel of the settings dialog (presets are an explicit save action).
			Config.PicturePresets = new List<PicturePreset>(PicturePresets);
			OriginalConfig.PicturePresets = new List<PicturePreset>(PicturePresets);
			ConfigManager.Config.Save();
		}

		private async System.Threading.Tasks.Task ExportCustomizationsAsync()
		{
			string? path = await FileDialogHelper.SaveFile(null, "MesenOrionVideoCustomizations.json", ApplicationHelper.GetMainWindow(), "json");
			if(path == null) {
				return;
			}
			VideoCustomizationExport export = new VideoCustomizationExport() {
				PicturePresets = new List<PicturePreset>(PicturePresets),
				FavoriteShaders = new List<string>(FavoriteShaders),
				ShaderParameterValues = new List<ShaderParamValue>(Config.ShaderParameterValues ?? new List<ShaderParamValue>())
			};
			try {
				string json = JsonSerializer.Serialize(export, typeof(VideoCustomizationExport), MesenSerializerContext.Default);
				File.WriteAllText(path, json);
			} catch {
				//Ignore write errors
			}
		}

		private async System.Threading.Tasks.Task ImportCustomizationsAsync()
		{
			string? path = await FileDialogHelper.OpenFile(null, ApplicationHelper.GetMainWindow(), "json");
			if(path == null || !File.Exists(path)) {
				return;
			}
			try {
				string json = File.ReadAllText(path);
				if(JsonSerializer.Deserialize(json, typeof(VideoCustomizationExport), MesenSerializerContext.Default) is VideoCustomizationExport import) {
					PicturePresets.Clear();
					foreach(PicturePreset preset in import.PicturePresets ?? new List<PicturePreset>()) {
						PicturePresets.Add(preset);
					}
					SyncPresetsToConfig();

					FavoriteShaders.Clear();
					foreach(string fav in import.FavoriteShaders ?? new List<string>()) {
						FavoriteShaders.Add(fav);
					}
					SyncFavoritesToConfig();

					Config.ShaderParameterValues = new List<ShaderParamValue>(import.ShaderParameterValues ?? new List<ShaderParamValue>());
					OriginalConfig.ShaderParameterValues = new List<ShaderParamValue>(Config.ShaderParameterValues);
					ConfigManager.Config.Save();
				}
			} catch {
				//Ignore malformed files
			}
		}

		private void RefreshShaderParams()
		{
			ShaderParams.Clear();
			if(!IsLinux && !IsWindows) {
				return;
			}

			//Parse the parameters from the actually-selected shader file (independent of
			//the render thread), so switching shader + Refresh always shows the right list.
			string shader = string.IsNullOrEmpty(Config.Shader) ? "None" : Config.Shader;
			string path = EmuApi.GetCurrentShaderPath();
			foreach(ShaderParameterHelper.ShaderParam p in ShaderParameterHelper.Parse(path)) {
				double val = p.Default;

				//Apply any saved override for this shader/parameter and push it to the core.
				ShaderParamValue? saved = Config.ShaderParameterValues?.FirstOrDefault(x => x.Shader == shader && x.Name == p.Name);
				if(saved != null) {
					val = saved.Value;
				}
				EmuApi.SetShaderParameter(p.Name, val);

				ShaderParamViewModel vm = new ShaderParamViewModel {
					Name = p.Name,
					Desc = p.Desc,
					Min = p.Min,
					Max = p.Max,
					Step = p.Step,
					Value = val
				};
				AddDisposable(vm.WhenAnyValue(x => x.Value).Skip(1).Subscribe(_ => OnShaderParamChanged(vm)));
				ShaderParams.Add(vm);
			}
		}

		private void ResetShaderParams()
		{
			if(!IsLinux && !IsWindows) {
				return;
			}
			//Drop every override (core + saved config) so the shader uses its own defaults.
			EmuApi.ClearShaderParameters();
			string shader = string.IsNullOrEmpty(Config.Shader) ? "None" : Config.Shader;
			Config.ShaderParameterValues?.RemoveAll(x => x.Shader == shader);
			OriginalConfig.ShaderParameterValues = new List<ShaderParamValue>(Config.ShaderParameterValues ?? new List<ShaderParamValue>());
			ConfigManager.Config.Save();
			//Rebuild the sliders showing the shader's default values.
			RefreshShaderParams();
		}

		private void OnShaderParamChanged(ShaderParamViewModel vm)
		{
			EmuApi.SetShaderParameter(vm.Name, vm.Value);

			string shader = string.IsNullOrEmpty(Config.Shader) ? "None" : Config.Shader;
			Config.ShaderParameterValues ??= new List<ShaderParamValue>();
			ShaderParamValue? existing = Config.ShaderParameterValues.FirstOrDefault(x => x.Shader == shader && x.Name == vm.Name);
			if(existing != null) {
				existing.Value = vm.Value;
			} else {
				Config.ShaderParameterValues.Add(new ShaderParamValue { Shader = shader, Name = vm.Name, Value = vm.Value });
			}
			OriginalConfig.ShaderParameterValues = new List<ShaderParamValue>(Config.ShaderParameterValues);
			ConfigManager.Config.Save();
		}

		private static string BuildShaderShortcutsText()
		{
			string Line(EmulatorShortcut shortcut)
			{
				KeyCombination? keys = shortcut.GetShortcutKeys();
				string keyText = (keys != null && !keys.IsEmpty) ? keys.ToString() : "-";
				return ResourceHelper.GetEnumText(shortcut) + ": " + keyText;
			}

			return Line(EmulatorShortcut.NextShader) + "\n" +
				Line(EmulatorShortcut.PreviousShader) + "\n" +
				Line(EmulatorShortcut.NextFavoriteShader) + "\n" +
				Line(EmulatorShortcut.PreviousFavoriteShader);
		}
	}

	//Lightweight view model for a single tweakable shader parameter (slider).
	public class ShaderParamViewModel : ViewModelBase
	{
		public string Name { get; init; } = "";
		public string Desc { get; init; } = "";
		public double Min { get; init; } = 0;
		public double Max { get; init; } = 1;
		public double Step { get; init; } = 0.01;
		[Reactive] public double Value { get; set; } = 0;
	}
}
