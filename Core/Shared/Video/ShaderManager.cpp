#include "pch.h"
#include <algorithm>
#ifndef _WIN32
#include <unistd.h>
#endif
#include "Shared/Video/ShaderManager.h"
#include "Utilities/FolderUtilities.h"

SimpleLock ShaderManager::_lock;
vector<string> ShaderManager::_shaderPaths;
vector<string> ShaderManager::_shaderNames;
vector<string> ShaderManager::_favoriteNames;
int ShaderManager::_currentIndex = -1;
std::atomic<uint64_t> ShaderManager::_changeId{ 1 };
std::unordered_map<string, float> ShaderManager::_paramOverrides;

vector<string> ShaderManager::GetCandidateFolders()
{
	//Look both next to the executable and in the Mesen data folder, accepting either
	//"Shaders" or "shaders" (the Linux filesystem is case-sensitive).
	vector<string> folders;

	// Executable directory first so the Shaders/ folder shipped alongside Mesen.exe
	// in the release ZIP is always found, even on a fresh installation.
	string exeDir = FolderUtilities::GetExeFolder();
	if(!exeDir.empty()) {
		folders.push_back(FolderUtilities::CombinePath(exeDir, "Shaders"));
		folders.push_back(FolderUtilities::CombinePath(exeDir, "shaders"));
#ifndef _WIN32
		//When installed via the .deb, the binary lives in <prefix>/bin and the shaders
		//in <prefix>/share/mesen-orion/shaders (e.g. /usr or /usr/local). Derive it.
		folders.push_back(FolderUtilities::CombinePath(exeDir, "../share/mesen-orion/shaders"));
#endif
	}

#ifndef _WIN32
	//Standard install locations used by the .deb package.
	folders.push_back("/usr/share/mesen-orion/shaders");
	folders.push_back("/usr/local/share/mesen-orion/shaders");
#endif

	// Mesen data folder (HomeFolder/Shaders) — fallback / user-installed shaders.
	try {
		string shaderFolder = FolderUtilities::GetShaderFolder();
		if(!shaderFolder.empty()) {
			folders.push_back(shaderFolder);
		}
	} catch(...) {
		//Shader folder not accessible - ignore
	}

	try {
		string home = FolderUtilities::GetHomeFolder();
		folders.push_back(FolderUtilities::CombinePath(home, "Shaders"));
		folders.push_back(FolderUtilities::CombinePath(home, "shaders"));
	} catch(...) {
		//Home folder not set yet - ignore
	}

	return folders;
}

void ShaderManager::RefreshShaderList()
{
	auto lock = _lock.AcquireSafe();

	string previousPath = (_currentIndex >= 0 && _currentIndex < (int)_shaderPaths.size()) ? _shaderPaths[_currentIndex] : "";

	_shaderPaths.clear();
	_shaderNames.clear();

	vector<string> files;
	for(string& folder : GetCandidateFolders()) {
		for(string& file : FolderUtilities::GetFilesInFolder(folder, { ".glslp", ".glsl" }, false)) {
			files.push_back(file);
		}
	}
	std::sort(files.begin(), files.end());

	for(string& file : files) {
		string name = FolderUtilities::GetFilename(file, true);
		//Skip duplicate display names (same shader found in more than one candidate folder)
		if(std::find(_shaderNames.begin(), _shaderNames.end(), name) != _shaderNames.end()) {
			continue;
		}
		_shaderPaths.push_back(file);
		_shaderNames.push_back(name);
	}

	//Try to keep the previously selected shader selected after a refresh
	_currentIndex = -1;
	if(!previousPath.empty()) {
		for(size_t i = 0; i < _shaderPaths.size(); i++) {
			if(_shaderPaths[i] == previousPath) {
				_currentIndex = (int)i;
				break;
			}
		}
	}

	_changeId++;
}

vector<string> ShaderManager::GetShaderNames()
{
	auto lock = _lock.AcquireSafe();
	return _shaderNames;
}

string ShaderManager::GetCurrentShaderPath()
{
	auto lock = _lock.AcquireSafe();
	if(_currentIndex >= 0 && _currentIndex < (int)_shaderPaths.size()) {
		return _shaderPaths[_currentIndex];
	}
	return "";
}

string ShaderManager::GetCurrentShaderName()
{
	auto lock = _lock.AcquireSafe();
	if(_currentIndex >= 0 && _currentIndex < (int)_shaderNames.size()) {
		return _shaderNames[_currentIndex];
	}
	return "None";
}

void ShaderManager::SetIndexInternal(int index)
{
	//Caller must hold _lock
	if(index < -1) {
		index = -1;
	}
	if(index >= (int)_shaderPaths.size()) {
		index = (int)_shaderPaths.size() - 1;
	}
	if(index != _currentIndex) {
		_currentIndex = index;
		//A different shader uses different parameters - drop the previous overrides so
		//the new shader starts at its own defaults (the UI re-applies saved values).
		_paramOverrides.clear();
		_changeId++;
	}
}

void ShaderManager::SelectShaderByName(const string& name)
{
	auto lock = _lock.AcquireSafe();
	if(name.empty() || name == "None") {
		SetIndexInternal(-1);
		return;
	}
	for(size_t i = 0; i < _shaderNames.size(); i++) {
		if(_shaderNames[i] == name || _shaderPaths[i] == name) {
			SetIndexInternal((int)i);
			return;
		}
	}
	//Not found - leave selection unchanged
}

void ShaderManager::NextShader()
{
	auto lock = _lock.AcquireSafe();
	if(_shaderPaths.empty()) {
		return;
	}
	//Cycle: none(-1) -> 0 -> 1 -> ... -> last -> none
	int next = _currentIndex + 1;
	if(next >= (int)_shaderPaths.size()) {
		next = -1;
	}
	SetIndexInternal(next);
}

void ShaderManager::PreviousShader()
{
	auto lock = _lock.AcquireSafe();
	if(_shaderPaths.empty()) {
		return;
	}
	//Cycle: none(-1) -> last -> ... -> 0 -> none
	int prev = _currentIndex - 1;
	if(_currentIndex == -1) {
		prev = (int)_shaderPaths.size() - 1;
	}
	SetIndexInternal(prev);
}

bool ShaderManager::IsFavoriteInternal(const string& name)
{
	return std::find(_favoriteNames.begin(), _favoriteNames.end(), name) != _favoriteNames.end();
}

void ShaderManager::SetFavoritesFromString(const string& joinedNames)
{
	auto lock = _lock.AcquireSafe();
	_favoriteNames.clear();
	const string delim = "[!|!]";
	size_t pos = 0;
	while(pos < joinedNames.size()) {
		size_t next = joinedNames.find(delim, pos);
		string name = (next == string::npos) ? joinedNames.substr(pos) : joinedNames.substr(pos, next - pos);
		if(!name.empty()) {
			_favoriteNames.push_back(name);
		}
		if(next == string::npos) {
			break;
		}
		pos = next + delim.size();
	}
}

void ShaderManager::CycleFavorite(bool forward)
{
	//Caller must hold _lock
	//Build the sequence of selectable indices: none(-1) followed by the favorites
	//that currently exist in the shader list (in list order).
	vector<int> seq;
	seq.push_back(-1);
	for(size_t i = 0; i < _shaderNames.size(); i++) {
		if(IsFavoriteInternal(_shaderNames[i])) {
			seq.push_back((int)i);
		}
	}
	if(seq.size() <= 1) {
		//No favorites available
		return;
	}

	int pos = 0;
	for(size_t k = 0; k < seq.size(); k++) {
		if(seq[k] == _currentIndex) {
			pos = (int)k;
			break;
		}
	}

	int count = (int)seq.size();
	int target = forward ? (pos + 1) % count : (pos - 1 + count) % count;
	SetIndexInternal(seq[target]);
}

float ShaderManager::GetShaderParamValue(const string& name, float defaultValue)
{
	auto lock = _lock.AcquireSafe();
	auto it = _paramOverrides.find(name);
	return it != _paramOverrides.end() ? it->second : defaultValue;
}

void ShaderManager::SetShaderParamValue(const string& name, float value)
{
	auto lock = _lock.AcquireSafe();
	_paramOverrides[name] = value;
}

void ShaderManager::ClearShaderParamOverrides()
{
	auto lock = _lock.AcquireSafe();
	_paramOverrides.clear();
}

void ShaderManager::NextFavoriteShader()
{
	auto lock = _lock.AcquireSafe();
	CycleFavorite(true);
}

void ShaderManager::PreviousFavoriteShader()
{
	auto lock = _lock.AcquireSafe();
	CycleFavorite(false);
}
