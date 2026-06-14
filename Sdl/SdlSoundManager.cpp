#include "SdlSoundManager.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/MessageManager.h"
#include "Core/Shared/Audio/SoundMixer.h"
#include "Core/Shared/Emulator.h"

SdlSoundManager::SdlSoundManager(Emulator* emu)
{
	_emu = emu;

	if(InitializeAudio(44100, false)) {
		_emu->GetSoundMixer()->RegisterAudioDevice(this);
	}
}

SdlSoundManager::~SdlSoundManager()
{
	Release();
}

void SdlSoundManager::FillAudioBuffer(void *userData, uint8_t *stream, int len)
{
	SdlSoundManager* soundManager = (SdlSoundManager*)userData;

	soundManager->ReadFromBuffer(stream, len);
}

void SdlSoundManager::Release()
{
	if(_audioDeviceID != 0) {
		Stop();
		SDL_CloseAudioDevice(_audioDeviceID);
	}

	if(_buffer) {
		delete[] _buffer;
		_buffer = nullptr;
		_bufferSize = 0;
	}
}

bool SdlSoundManager::InitializeAudio(uint32_t sampleRate, bool isStereo)
{
	if(SDL_InitSubSystem(SDL_INIT_AUDIO) != 0) {
		MessageManager::Log("[Audio] Failed to initialize audio subsystem");
		return false;
	}

	int isCapture = 0;

	_sampleRate = sampleRate;
	_isStereo = isStereo;
	_previousLatency = _emu->GetSettings()->GetAudioConfig().AudioLatency;

	int bytesPerSample = 2 * (isStereo ? 2 : 1);
	int32_t requestedByteLatency = (int32_t)((float)(sampleRate * _previousLatency) / 1000.0f * bytesPerSample);
	_bufferSize = (int32_t)std::ceil((double)requestedByteLatency * 2 / 0x10000) * 0x10000;
	_buffer = new uint8_t[_bufferSize];
	memset(_buffer, 0, _bufferSize);

	SDL_AudioSpec audioSpec;
	SDL_memset(&audioSpec, 0, sizeof(audioSpec));
	audioSpec.freq = sampleRate;
	audioSpec.format = AUDIO_S16SYS; //16-bit samples
	audioSpec.channels = isStereo ? 2 : 1;
	audioSpec.samples = 1024;
	audioSpec.callback = &SdlSoundManager::FillAudioBuffer;
	audioSpec.userdata = this;

	SDL_AudioSpec obtainedSpec;

	_audioDeviceID = SDL_OpenAudioDevice(_deviceName.empty() ? nullptr : _deviceName.c_str(), isCapture, &audioSpec, &obtainedSpec, 0);
	if(_audioDeviceID == 0 && !_deviceName.empty()) {
		MessageManager::Log("[Audio] Failed opening audio device '" + _deviceName + "', will retry with default device.");  
		_audioDeviceID = SDL_OpenAudioDevice(nullptr, isCapture, &audioSpec, &obtainedSpec, 0);
	}

	_writePosition.store(0, std::memory_order_relaxed);
	_readPosition.store(0, std::memory_order_relaxed);

	_needReset = false;

	return _audioDeviceID != 0;
}

string SdlSoundManager::GetAvailableDevices()
{
	string deviceString;
	for(string device : GetAvailableDeviceInfo()) {
		deviceString += device + std::string("||");
	}
	return deviceString;
}

vector<string> SdlSoundManager::GetAvailableDeviceInfo()
{
	vector<string> deviceList;
	int isCapture = 0;
	int deviceCount = SDL_GetNumAudioDevices(isCapture);

	if(deviceCount == -1) {
		//No devices found
	} else {
		for(int i = 0; i < deviceCount; i++) {
			deviceList.push_back(SDL_GetAudioDeviceName(i, isCapture));
		}
	}

	return deviceList;
}

void SdlSoundManager::SetAudioDevice(string deviceName)
{
	if(deviceName.compare(_deviceName) != 0) {
		_deviceName = deviceName;
		_needReset = true;
	}
}

void SdlSoundManager::ReadFromBuffer(uint8_t* output, uint32_t len)
{
	//Consumer side (SDL audio callback thread)
	uint32_t readPos = _readPosition.load(std::memory_order_relaxed);
	uint32_t writePos = _writePosition.load(std::memory_order_acquire);

	if(readPos + len < _bufferSize) {
		memcpy(output, _buffer+readPos, len);
		readPos += len;
	} else {
		uint32_t remainingBytes = _bufferSize - readPos;
		memcpy(output, _buffer+readPos, remainingBytes);
		memcpy(output+remainingBytes, _buffer, len - remainingBytes);
		readPos = len - remainingBytes;
	}

	_readPosition.store(readPos, std::memory_order_release);

	if(readPos >= writePos && readPos - writePos < _bufferSize / 2) {
		_bufferUnderrunEventCount++;
	}
}

void SdlSoundManager::WriteToBuffer(uint8_t* input, uint32_t len)
{
	//Producer side (emulation thread)
	uint32_t writePos = _writePosition.load(std::memory_order_relaxed);

	if(writePos + len < _bufferSize) {
		memcpy(_buffer+writePos, input, len);
		writePos += len;
	} else {
		uint32_t remainingBytes = _bufferSize - writePos;
		memcpy(_buffer+writePos, input, remainingBytes);
		memcpy(_buffer, input+remainingBytes, len - remainingBytes);
		writePos = len - remainingBytes;
	}

	_writePosition.store(writePos, std::memory_order_release);
}
void SdlSoundManager::PlayBuffer(int16_t *soundBuffer, uint32_t sampleCount, uint32_t sampleRate, bool isStereo)
{
	uint32_t bytesPerSample = 2 * (isStereo ? 2 : 1);
	uint32_t latency = _emu->GetSettings()->GetAudioConfig().AudioLatency;
	if(_sampleRate != sampleRate || _isStereo != isStereo || _needReset || _previousLatency != latency) {
		Release();
		InitializeAudio(sampleRate, isStereo);
	}

	WriteToBuffer((uint8_t*)soundBuffer, sampleCount * bytesPerSample);

	int32_t byteLatency = (int32_t)((float)(sampleRate * latency) / 1000.0f * bytesPerSample);
	uint32_t writePos = _writePosition.load(std::memory_order_acquire);
	uint32_t readPos = _readPosition.load(std::memory_order_acquire);
	int32_t playWriteByteLatency = (int32_t)writePos - (int32_t)readPos;
	if(playWriteByteLatency < 0) {
		playWriteByteLatency = _bufferSize - readPos + writePos;
	}

	if(playWriteByteLatency > byteLatency) {
		//Start playing
		SDL_PauseAudioDevice(_audioDeviceID, 0);
	}
}

void SdlSoundManager::Pause()
{
	SDL_PauseAudioDevice(_audioDeviceID, 1);
}

void SdlSoundManager::Stop()
{
	Pause();

	_readPosition.store(0, std::memory_order_relaxed);
	_writePosition.store(0, std::memory_order_relaxed);
	ResetStats();
}

void SdlSoundManager::ProcessEndOfFrame()
{
	ProcessLatency(_readPosition.load(std::memory_order_acquire), _writePosition.load(std::memory_order_acquire));

	uint32_t emulationSpeed = _emu->GetSettings()->GetEmulationSpeed();
	if(_averageLatency > 0 && emulationSpeed <= 100 && emulationSpeed > 0 && std::abs(_averageLatency - _emu->GetSettings()->GetAudioConfig().AudioLatency) > 50) {
		//Latency is way off (over 50ms gap), stop audio & start again
		Stop();
	}
}
