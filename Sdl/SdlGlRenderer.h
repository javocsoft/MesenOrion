#pragma once
#include <atomic>
#include <string>
#include <vector>
#include <memory>
#include "SdlGl.h"
#include "Core/Shared/Interfaces/IRenderingDevice.h"
#include "Utilities/SimpleLock.h"
#include "Core/Shared/Video/VideoRenderer.h"
#include "Core/Shared/RenderedFrame.h"

class Emulator;
class GlShaderChain;

// OpenGL-based renderer for Linux/SDL that supports libretro GLSL shader presets.
// Used in place of SdlRenderer when shaders are enabled. When no shader is
// selected it simply blits the emulator frame (passthrough), so it remains a
// fully functional renderer on its own.
class SdlGlRenderer : public IRenderingDevice
{
private:
	struct GlHud
	{
		GLuint Texture = 0;
		uint32_t Width = 0;
		uint32_t Height = 0;
	};

	Emulator* _emu;
	void* _windowHandle;
	SDL_Window* _sdlWindow = nullptr;
	SDL_GLContext _glContext = nullptr;

	static SimpleLock _frameLock;
	uint32_t* _frameBuffer = nullptr;
	const uint32_t _bytesPerPixel = 4;

	GLuint _frameTexture = 0;
	uint32_t _frameTexWidth = 0;
	uint32_t _frameTexHeight = 0;

	uint32_t _requiredWidth = 256;
	uint32_t _requiredHeight = 240;
	bool _frameChanged = true;

	uint32_t _screenWidth = 0;
	uint32_t _screenHeight = 0;

	bool _useBilinearInterpolation = false;
	bool _vsyncEnabled = false;

	// Passthrough / HUD drawing
	GLuint _blitProgram = 0;
	GLint _blitPosLoc = -1;
	GLint _blitTexCoordLoc = -1;
	GLint _blitSamplerLoc = -1;
	GLuint _quadVbo = 0;
	GLuint _quadVboFlip = 0;
	GLuint _vao = 0;

	GlHud _emuHud;
	GlHud _scriptHud;

	std::unique_ptr<GlShaderChain> _shaderChain;
	std::string _loadedShaderPath;
	std::atomic<bool> _shaderDirty{ true };
	int _frameCount = 0;

	std::atomic<bool> _newFullscreen{ false };
	bool _fullscreen = false;

	bool Init();
	void Cleanup();
	void LogSdlError(const char* msg);
	bool InitGlObjects();
	GLuint CompileBlitProgram();
	void EnsureFrameTexture(uint32_t width, uint32_t height);
	void UpdateShaderChain();
	void DrawTextureToScreen(GLuint texture, bool flipped, bool blend);
	void DrawHud(GlHud& hud, RenderSurfaceInfo& surface);
	void UpdateFullscreenState();

public:
	SdlGlRenderer(Emulator* emu, void* windowHandle);
	virtual ~SdlGlRenderer();

	void ClearFrame() override;
	void UpdateFrame(RenderedFrame& frame) override;
	void Render(RenderSurfaceInfo& emuHud, RenderSurfaceInfo& scriptHud) override;
	void Reset() override;
	void OnRendererThreadStarted() override;

	void SetExclusiveFullscreenMode(bool fullscreen, void* windowHandle) override;
};
