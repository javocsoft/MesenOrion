#pragma once
#include "pch.h"
#include <atomic>
#include <unordered_map>
#include "Utilities/SimpleLock.h"

// Tracks the list of GLSL shader presets found in the Shaders folder and the
// currently selected one. Shared between the renderer (applies the shader),
// the shortcut handler (next/previous via keyboard or gamepad) and the interop
// layer (UI selection). Selection of -1 means "no shader" (passthrough).
//
// Implemented as a static singleton (like MessageManager) so it is reachable
// from the platform renderer without threading it through Emulator.
class ShaderManager
{
private:
	static SimpleLock _lock;
	static vector<string> _shaderPaths;     // full paths
	static vector<string> _shaderNames;     // display names (filename)
	static vector<string> _favoriteNames;   // display names marked as favorite
	static int _currentIndex;               // -1 = none
	static std::atomic<uint64_t> _changeId;

	//User overrides for shader parameters, keyed by parameter name. The renderer's
	//shader chain reads these live each frame (falling back to the shader's own
	//default when no override is set). Decoupled from metadata so the UI never has
	//to wait for the render thread.
	static std::unordered_map<string, float> _paramOverrides;

	static void SetIndexInternal(int index);
	static vector<string> GetCandidateFolders();
	static bool IsFavoriteInternal(const string& name);
	static void CycleFavorite(bool forward);

public:
	// Rescans the Shaders folder for *.glslp and *.glsl (non-recursive).
	static void RefreshShaderList();

	static vector<string> GetShaderNames();
	static string GetCurrentShaderPath();   // "" when none selected
	static string GetCurrentShaderName();   // "None" when none selected

	static void SelectShaderByName(const string& name); // "" or "None" => none
	static void NextShader();
	static void PreviousShader();

	//Favorites - set from the UI (names joined by "[!|!]"). Cycling moves only
	//between favorite shaders (plus "None").
	static void SetFavoritesFromString(const string& joinedNames);
	static void NextFavoriteShader();
	static void PreviousFavoriteShader();

	// Monotonic counter; renderers poll it to detect selection changes.
	static uint64_t GetChangeId() { return _changeId.load(); }

	// Shader parameter overrides. The UI parses parameter metadata from the shader
	// file itself, then pushes values here; the shader chain reads them live each
	// frame, using the supplied default when no override exists.
	static float GetShaderParamValue(const string& name, float defaultValue);
	static void SetShaderParamValue(const string& name, float value);
	static void ClearShaderParamOverrides();
};
