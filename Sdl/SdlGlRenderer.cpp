#include "SdlGlRenderer.h"
#include "GlShaderChain.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/Video/VideoRenderer.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/MessageManager.h"
#include "Core/Shared/RenderedFrame.h"
#include "Core/Shared/Video/ShaderManager.h"

using namespace glfn;

SimpleLock SdlGlRenderer::_frameLock;

SdlGlRenderer::SdlGlRenderer(Emulator* emu, void* windowHandle) : _windowHandle(windowHandle)
{
	_emu = emu;
	_shaderChain.reset(new GlShaderChain());
	_emu->GetVideoRenderer()->RegisterRenderingDevice(this);
}

SdlGlRenderer::~SdlGlRenderer()
{
	_emu->GetVideoRenderer()->UnregisterRenderingDevice(this);
	Cleanup();
	delete[] _frameBuffer;
}

void SdlGlRenderer::LogSdlError(const char* msg)
{
	MessageManager::Log(msg);
	MessageManager::Log(SDL_GetError());
}

bool SdlGlRenderer::Init()
{
	//Crucial: a window created via SDL_CreateWindowFrom() does not get the
	//SDL_WINDOW_OPENGL flag, which SDL_GL_CreateContext() requires. This hint makes
	//SDL create the foreign window as OpenGL-capable. Without it the GL context
	//creation (or rendering) fails and the screen stays black.
	SDL_SetHint("SDL_VIDEO_FOREIGN_WINDOW_OPENGL", "1");

	const char* originalHint = SDL_GetHint("SDL_VIDEODRIVER");
	SDL_SetHint("SDL_VIDEODRIVER", "x11");
	if(SDL_InitSubSystem(SDL_INIT_VIDEO) != 0) {
		LogSdlError("[SDL/GL] Failed to initialize video subsystem.");
		return false;
	}

	if(SDL_GL_LoadLibrary(NULL) != 0) {
		LogSdlError("[SDL/GL] Failed to load OpenGL library.");
	}

	//Request a compatibility profile so legacy (GLSL 1.20) libretro shaders compile
	//while modern features (VAOs, FBOs) remain available.
	SDL_GL_SetAttribute(SDL_GL_CONTEXT_MAJOR_VERSION, 3);
	SDL_GL_SetAttribute(SDL_GL_CONTEXT_MINOR_VERSION, 3);
	SDL_GL_SetAttribute(SDL_GL_CONTEXT_PROFILE_MASK, SDL_GL_CONTEXT_PROFILE_COMPATIBILITY);
	SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);

	_sdlWindow = SDL_CreateWindowFrom(_windowHandle);
	if(!_sdlWindow) {
		MessageManager::Log("[SDL/GL] Failed to create window with x11 driver, retrying with default...");
		SDL_QuitSubSystem(SDL_INIT_VIDEO);
		SDL_SetHint("SDL_VIDEODRIVER", originalHint);
		if(SDL_InitSubSystem(SDL_INIT_VIDEO) != 0) {
			LogSdlError("[SDL/GL] Failed to initialize video subsystem.");
			return false;
		}
		_sdlWindow = SDL_CreateWindowFrom(_windowHandle);
		if(!_sdlWindow) {
			LogSdlError("[SDL/GL] Failed to create window from handle.");
			return false;
		}
	}

	_glContext = SDL_GL_CreateContext(_sdlWindow);
	if(!_glContext) {
		//Retry without forcing a profile/version - let the driver pick a default context.
		MessageManager::Log("[SDL/GL] Compatibility 3.3 context failed, retrying with default attributes...");
		SDL_GL_ResetAttributes();
		SDL_GL_SetAttribute(SDL_GL_DOUBLEBUFFER, 1);
		_glContext = SDL_GL_CreateContext(_sdlWindow);
		if(!_glContext) {
			LogSdlError("[SDL/GL] Failed to create OpenGL context.");
			return false;
		}
	}

	SDL_GL_MakeCurrent(_sdlWindow, _glContext);

	if(!glfn::LoadGlFunctions()) {
		MessageManager::Log("[SDL/GL] Could not resolve all required OpenGL functions - shader rendering unavailable.");
		return false;
	}

	const char* glVersion = (const char*)glGetString(GL_VERSION);
	const char* glRenderer = (const char*)glGetString(GL_RENDERER);
	const char* glslVersion = (const char*)glGetString(GL_SHADING_LANGUAGE_VERSION);
	MessageManager::Log(std::string("[SDL/GL] Context created. GL_VERSION=") + (glVersion ? glVersion : "?") +
		" GLSL=" + (glslVersion ? glslVersion : "?") + " Renderer=" + (glRenderer ? glRenderer : "?") +
		(glfn::GenVertexArrays ? " (VAO)" : " (no VAO)"));

	if(!InitGlObjects()) {
		return false;
	}

	_vsyncEnabled = _emu->GetSettings()->GetVideoConfig().VerticalSync;
	SDL_GL_SetSwapInterval(_vsyncEnabled ? 1 : 0);

	_shaderDirty = true;
	return true;
}

bool SdlGlRenderer::InitGlObjects()
{
	if(glfn::GenVertexArrays) {
		GenVertexArrays(1, &_vao);
	}

	// Fullscreen quad. Normal texcoords + V-flipped variant (for top-left origin sources).
	const float quad[] = {
		-1.0f, -1.0f, 0.0f, 0.0f,
		 1.0f, -1.0f, 1.0f, 0.0f,
		-1.0f,  1.0f, 0.0f, 1.0f,
		 1.0f,  1.0f, 1.0f, 1.0f,
	};
	const float quadFlip[] = {
		-1.0f, -1.0f, 0.0f, 1.0f,
		 1.0f, -1.0f, 1.0f, 1.0f,
		-1.0f,  1.0f, 0.0f, 0.0f,
		 1.0f,  1.0f, 1.0f, 0.0f,
	};
	GenBuffers(1, &_quadVbo);
	GenBuffers(1, &_quadVboFlip);
	BindBuffer(GL_ARRAY_BUFFER, _quadVbo);
	BufferData(GL_ARRAY_BUFFER, sizeof(quad), quad, GL_STATIC_DRAW);
	BindBuffer(GL_ARRAY_BUFFER, _quadVboFlip);
	BufferData(GL_ARRAY_BUFFER, sizeof(quadFlip), quadFlip, GL_STATIC_DRAW);
	BindBuffer(GL_ARRAY_BUFFER, 0);

	_blitProgram = CompileBlitProgram();
	if(!_blitProgram) {
		return false;
	}
	_blitPosLoc = GetAttribLocation(_blitProgram, "Position");
	_blitTexCoordLoc = GetAttribLocation(_blitProgram, "TexCoord");
	_blitSamplerLoc = GetUniformLocation(_blitProgram, "Tex");
	return true;
}

GLuint SdlGlRenderer::CompileBlitProgram()
{
	const char* vsSrc =
		"#version 120\n"
		"attribute vec2 Position;\n"
		"attribute vec2 TexCoord;\n"
		"varying vec2 vTex;\n"
		"void main() { vTex = TexCoord; gl_Position = vec4(Position, 0.0, 1.0); }\n";
	const char* fsSrc =
		"#version 120\n"
		"varying vec2 vTex;\n"
		"uniform sampler2D Tex;\n"
		"void main() { gl_FragColor = texture2D(Tex, vTex); }\n";

	auto compile = [&](GLenum type, const char* src) -> GLuint {
		GLuint sh = CreateShader(type);
		ShaderSource(sh, 1, &src, nullptr);
		CompileShader(sh);
		GLint ok = GL_FALSE;
		GetShaderiv(sh, GL_COMPILE_STATUS, &ok);
		if(!ok) {
			char log[1024];
			GLsizei len = 0;
			GetShaderInfoLog(sh, sizeof(log) - 1, &len, log);
			log[len] = '\0';
			MessageManager::Log(std::string("[SDL/GL] Blit shader error: ") + log);
			DeleteShader(sh);
			return 0;
		}
		return sh;
	};

	GLuint vs = compile(GL_VERTEX_SHADER, vsSrc);
	GLuint fs = compile(GL_FRAGMENT_SHADER, fsSrc);
	if(!vs || !fs) {
		if(vs) DeleteShader(vs);
		if(fs) DeleteShader(fs);
		return 0;
	}
	GLuint prog = CreateProgram();
	AttachShader(prog, vs);
	AttachShader(prog, fs);
	LinkProgram(prog);
	DeleteShader(vs);
	DeleteShader(fs);
	GLint linked = GL_FALSE;
	GetProgramiv(prog, GL_LINK_STATUS, &linked);
	if(!linked) {
		DeleteProgram(prog);
		return 0;
	}
	return prog;
}

void SdlGlRenderer::Cleanup()
{
	if(!_glContext) {
		return;
	}
	SDL_GL_MakeCurrent(_sdlWindow, _glContext);

	_shaderChain.reset(new GlShaderChain());
	_loadedShaderPath.clear();

	if(_frameTexture) { glDeleteTextures(1, &_frameTexture); _frameTexture = 0; }
	if(_emuHud.Texture) { glDeleteTextures(1, &_emuHud.Texture); _emuHud.Texture = 0; }
	if(_scriptHud.Texture) { glDeleteTextures(1, &_scriptHud.Texture); _scriptHud.Texture = 0; }
	if(_quadVbo) { DeleteBuffers(1, &_quadVbo); _quadVbo = 0; }
	if(_quadVboFlip) { DeleteBuffers(1, &_quadVboFlip); _quadVboFlip = 0; }
	if(_vao && glfn::DeleteVertexArrays) { DeleteVertexArrays(1, &_vao); _vao = 0; }
	if(_blitProgram) { DeleteProgram(_blitProgram); _blitProgram = 0; }

	SDL_GL_DeleteContext(_glContext);
	_glContext = nullptr;

	_frameTexWidth = _frameTexHeight = 0;
	_emuHud = GlHud();
	_scriptHud = GlHud();
	_fullscreen = false;
}

void SdlGlRenderer::OnRendererThreadStarted()
{
	//SDL/GL must be set up on the thread that renders - rebuild everything here.
	Reset();
}

void SdlGlRenderer::Reset()
{
	Cleanup();
	if(Init()) {
		_emu->GetVideoRenderer()->RegisterRenderingDevice(this);
	} else {
		Cleanup();
	}
}

void SdlGlRenderer::EnsureFrameTexture(uint32_t width, uint32_t height)
{
	if(_frameTexture == 0) {
		glGenTextures(1, &_frameTexture);
		_frameTexWidth = _frameTexHeight = 0;
	}
	glBindTexture(GL_TEXTURE_2D, _frameTexture);
	if(_frameTexWidth != width || _frameTexHeight != height) {
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_BGRA, GL_UNSIGNED_INT_8_8_8_8_REV, nullptr);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
		_frameTexWidth = width;
		_frameTexHeight = height;
	}
}

void SdlGlRenderer::ClearFrame()
{
	auto lock = _frameLock.AcquireSafe();
	if(_frameBuffer) {
		memset(_frameBuffer, 0, _requiredWidth * _requiredHeight * _bytesPerPixel);
	}
	_frameChanged = true;
}

void SdlGlRenderer::UpdateFrame(RenderedFrame& frame)
{
	auto lock = _frameLock.AcquireSafe();
	if(_frameBuffer == nullptr || _requiredWidth != frame.Width || _requiredHeight != frame.Height) {
		_requiredWidth = frame.Width;
		_requiredHeight = frame.Height;
		delete[] _frameBuffer;
		_frameBuffer = new uint32_t[frame.Width * frame.Height];
	}
	memcpy(_frameBuffer, frame.FrameBuffer, frame.Width * frame.Height * _bytesPerPixel);
	_frameChanged = true;
}

void SdlGlRenderer::UpdateShaderChain()
{
	static uint64_t lastChangeId = 0;
	uint64_t changeId = ShaderManager::GetChangeId();
	if(changeId == lastChangeId && !_shaderDirty) {
		return;
	}
	lastChangeId = changeId;
	_shaderDirty = false;

	string path = ShaderManager::GetCurrentShaderPath();
	if(path == _loadedShaderPath) {
		return;
	}
	_loadedShaderPath = path;

	_shaderChain.reset(new GlShaderChain());
	if(!path.empty()) {
		if(!_shaderChain->Load(path)) {
			MessageManager::Log("[SDL/GL] Shader load failed: " + _shaderChain->GetError());
			_shaderChain.reset(new GlShaderChain());
		}
	}
}

void SdlGlRenderer::DrawTextureToScreen(GLuint texture, bool flipped, bool blend)
{
	UseProgram(_blitProgram);
	if(blend) {
		glEnable(GL_BLEND);
		glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
	} else {
		glDisable(GL_BLEND);
	}

	ActiveTexture(GL_TEXTURE0);
	glBindTexture(GL_TEXTURE_2D, texture);
	if(_blitSamplerLoc >= 0) {
		Uniform1i(_blitSamplerLoc, 0);
	}

	if(glfn::BindVertexArray) {
		BindVertexArray(_vao);
	}
	BindBuffer(GL_ARRAY_BUFFER, flipped ? _quadVboFlip : _quadVbo);
	const GLsizei stride = 4 * sizeof(float);
	if(_blitPosLoc >= 0) {
		EnableVertexAttribArray(_blitPosLoc);
		VertexAttribPointer(_blitPosLoc, 2, GL_FLOAT, GL_FALSE, stride, (void*)0);
	}
	if(_blitTexCoordLoc >= 0) {
		EnableVertexAttribArray(_blitTexCoordLoc);
		VertexAttribPointer(_blitTexCoordLoc, 2, GL_FLOAT, GL_FALSE, stride, (void*)(2 * sizeof(float)));
	}
	glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
	if(_blitPosLoc >= 0) DisableVertexAttribArray(_blitPosLoc);
	if(_blitTexCoordLoc >= 0) DisableVertexAttribArray(_blitTexCoordLoc);
	BindBuffer(GL_ARRAY_BUFFER, 0);
	if(glfn::BindVertexArray) {
		BindVertexArray(0);
	}
	if(blend) {
		glDisable(GL_BLEND);
	}
}

void SdlGlRenderer::DrawHud(GlHud& hud, RenderSurfaceInfo& surface)
{
	if(surface.Buffer == nullptr || surface.Width == 0 || surface.Height == 0) {
		return;
	}

	bool sizeChanged = (hud.Texture == 0 || hud.Width != surface.Width || hud.Height != surface.Height);
	if(hud.Texture == 0) {
		glGenTextures(1, &hud.Texture);
	}
	glBindTexture(GL_TEXTURE_2D, hud.Texture);
	if(sizeChanged) {
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, surface.Width, surface.Height, 0, GL_BGRA, GL_UNSIGNED_INT_8_8_8_8_REV, surface.Buffer);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
		hud.Width = surface.Width;
		hud.Height = surface.Height;
	} else if(surface.IsDirty) {
		glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, surface.Width, surface.Height, GL_BGRA, GL_UNSIGNED_INT_8_8_8_8_REV, surface.Buffer);
	}

	DrawTextureToScreen(hud.Texture, true, true);
}

void SdlGlRenderer::Render(RenderSurfaceInfo& emuHud, RenderSurfaceInfo& scriptHud)
{
	if(!_glContext) {
		if(!Init()) {
			return;
		}
	}

	SDL_GL_MakeCurrent(_sdlWindow, _glContext);
	UpdateFullscreenState();

	VideoConfig cfg = _emu->GetSettings()->GetVideoConfig();
	if(_vsyncEnabled != cfg.VerticalSync) {
		_vsyncEnabled = cfg.VerticalSync;
		SDL_GL_SetSwapInterval(_vsyncEnabled ? 1 : 0);
	}
	_useBilinearInterpolation = cfg.UseBilinearInterpolation;

	//Use the renderer size reported by the video renderer (the authoritative target
	//size computed by the UI). SDL_GL_GetDrawableSize is unreliable for a window
	//created via SDL_CreateWindowFrom (it can return a stale size, leaving the image
	//in a sub-rect of the window).
	FrameInfo size = _emu->GetVideoRenderer()->GetRendererSize();
	if(size.Width > 0 && size.Height > 0) {
		_screenWidth = size.Width;
		_screenHeight = size.Height;
	}
	if(_screenWidth == 0 || _screenHeight == 0) {
		return;
	}

	UpdateShaderChain();

	uint32_t reqW, reqH;
	{
		auto lock = _frameLock.AcquireSafe();
		reqW = _requiredWidth;
		reqH = _requiredHeight;
		EnsureFrameTexture(reqW, reqH);
		if(_frameBuffer) {
			glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
			glBindTexture(GL_TEXTURE_2D, _frameTexture);
			glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, reqW, reqH, GL_BGRA, GL_UNSIGNED_INT_8_8_8_8_REV, _frameBuffer);
		}
	}

	glBindFramebuffer(GL_FRAMEBUFFER, 0);
	glViewport(0, 0, (GLsizei)_screenWidth, (GLsizei)_screenHeight);
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
	glClear(GL_COLOR_BUFFER_BIT);

	if(_shaderChain && _shaderChain->IsValid()) {
		_shaderChain->Render(_frameTexture, (int)reqW, (int)reqH, (int)_screenWidth, (int)_screenHeight, _frameCount);
		//Restore state the shader chain may have changed.
		glBindFramebuffer(GL_FRAMEBUFFER, 0);
		glViewport(0, 0, (GLsizei)_screenWidth, (GLsizei)_screenHeight);
	} else {
		GLint filter = _useBilinearInterpolation ? GL_LINEAR : GL_NEAREST;
		glBindTexture(GL_TEXTURE_2D, _frameTexture);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter);
		DrawTextureToScreen(_frameTexture, true, false);
	}

	DrawHud(_scriptHud, scriptHud);
	DrawHud(_emuHud, emuHud);

	static bool loggedFirstFrame = false;
	if(!loggedFirstFrame) {
		loggedFirstFrame = true;
		GLenum err = glGetError();
		MessageManager::Log("[SDL/GL] First frame: screen=" + std::to_string(_screenWidth) + "x" + std::to_string(_screenHeight) +
			" frame=" + std::to_string(reqW) + "x" + std::to_string(reqH) +
			" shader=" + (_shaderChain && _shaderChain->IsValid() ? "yes" : "no") +
			" frameBuffer=" + (_frameBuffer ? "ok" : "null") +
			" glError=" + std::to_string((int)err));
	}

	SDL_GL_SwapWindow(_sdlWindow);
	_frameCount++;
}

void SdlGlRenderer::SetExclusiveFullscreenMode(bool fullscreen, void* windowHandle)
{
	_newFullscreen.store(fullscreen, std::memory_order_release);
}

void SdlGlRenderer::UpdateFullscreenState()
{
	bool fullscreen = _newFullscreen.load(std::memory_order_acquire);
	if(fullscreen == _fullscreen || !_sdlWindow) {
		return;
	}

	if(fullscreen) {
		VideoConfig cfg = _emu->GetSettings()->GetVideoConfig();
		uint32_t flags = SDL_WINDOW_FULLSCREEN_DESKTOP;
		if(cfg.FullscreenResWidth > 0 && cfg.FullscreenResHeight > 0) {
			uint32_t refreshRate = _emu->GetFps() < 55 ? cfg.ExclusiveFullscreenRefreshRatePal : cfg.ExclusiveFullscreenRefreshRateNtsc;
			int displayIndex = SDL_GetWindowDisplayIndex(_sdlWindow);
			SDL_DisplayMode desired = {};
			desired.w = (int)cfg.FullscreenResWidth;
			desired.h = (int)cfg.FullscreenResHeight;
			desired.refresh_rate = (int)refreshRate;
			SDL_DisplayMode closest;
			if(SDL_GetClosestDisplayMode(displayIndex < 0 ? 0 : displayIndex, &desired, &closest) && SDL_SetWindowDisplayMode(_sdlWindow, &closest) == 0) {
				flags = SDL_WINDOW_FULLSCREEN;
			}
		}
		if(SDL_SetWindowFullscreen(_sdlWindow, flags) != 0) {
			LogSdlError("[SDL/GL] Failed to enter fullscreen mode.");
			return;
		}
	} else {
		if(SDL_SetWindowFullscreen(_sdlWindow, 0) != 0) {
			LogSdlError("[SDL/GL] Failed to leave fullscreen mode.");
			return;
		}
	}
	_fullscreen = fullscreen;
}
