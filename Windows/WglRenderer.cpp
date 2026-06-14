#include "pch.h"
#include "WglRenderer.h"
#include "../Sdl/GlShaderChain.h"
#include "Core/Shared/Emulator.h"
#include "Core/Shared/Video/VideoRenderer.h"
#include "Core/Shared/EmuSettings.h"
#include "Core/Shared/MessageManager.h"
#include "Core/Shared/RenderedFrame.h"
#include "Core/Shared/Video/ShaderManager.h"

using namespace glfn;

SimpleLock WglRenderer::_frameLock;

// WGL extension to create a modern OpenGL context (GL 3.3+)
typedef HGLRC(WINAPI* PFNWGLCREATECONTEXTATTRIBSARBPROC)(HDC hDC, HGLRC hShareContext, const int* attribList);
#define WGL_CONTEXT_MAJOR_VERSION_ARB     0x2091
#define WGL_CONTEXT_MINOR_VERSION_ARB     0x2092
#define WGL_CONTEXT_PROFILE_MASK_ARB      0x9126
#define WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB 0x00000002

WglRenderer::WglRenderer(Emulator* emu, HWND hwnd) : _hWnd(hwnd)
{
	_emu = emu;
	_shaderChain.reset(new GlShaderChain());
	_emu->GetVideoRenderer()->RegisterRenderingDevice(this);
}

WglRenderer::~WglRenderer()
{
	_emu->GetVideoRenderer()->UnregisterRenderingDevice(this);
	Cleanup();
	delete[] _frameBuffer;
}

bool WglRenderer::Init()
{
	_hDC = GetDC(_hWnd);
	if(!_hDC) {
		MessageManager::Log("[WGL] Failed to get device context.");
		return false;
	}

	// Set a pixel format compatible with OpenGL.
	// SetPixelFormat can only be called once per HWND (e.g. after a hot-swap
	// software → WGL), so skip it if a pixel format is already set.
	if(GetPixelFormat(_hDC) == 0) {
		PIXELFORMATDESCRIPTOR pfd = {};
		pfd.nSize = sizeof(pfd);
		pfd.nVersion = 1;
		pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
		pfd.iPixelType = PFD_TYPE_RGBA;
		pfd.cColorBits = 32;
		pfd.cDepthBits = 24;
		pfd.iLayerType = PFD_MAIN_PLANE;

		int pixelFormat = ChoosePixelFormat(_hDC, &pfd);
		if(!pixelFormat) {
			MessageManager::Log("[WGL] ChoosePixelFormat failed.");
			ReleaseDC(_hWnd, _hDC);
			_hDC = nullptr;
			return false;
		}
		if(!SetPixelFormat(_hDC, pixelFormat, &pfd)) {
			MessageManager::Log("[WGL] SetPixelFormat failed.");
			ReleaseDC(_hWnd, _hDC);
			_hDC = nullptr;
			return false;
		}
	}

	// Create a temporary legacy context to get wglCreateContextAttribsARB
	HGLRC tmpCtx = wglCreateContext(_hDC);
	if(!tmpCtx) {
		MessageManager::Log("[WGL] Failed to create legacy context.");
		ReleaseDC(_hWnd, _hDC);
		_hDC = nullptr;
		return false;
	}
	wglMakeCurrent(_hDC, tmpCtx);

	// Try to create a compatibility 3.3 context for better GLSL support
	PFNWGLCREATECONTEXTATTRIBSARBPROC wglCreateContextAttribsARB =
		(PFNWGLCREATECONTEXTATTRIBSARBPROC)wglGetProcAddress("wglCreateContextAttribsARB");

	if(wglCreateContextAttribsARB) {
		const int attribs[] = {
			WGL_CONTEXT_MAJOR_VERSION_ARB, 3,
			WGL_CONTEXT_MINOR_VERSION_ARB, 3,
			WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_COMPATIBILITY_PROFILE_BIT_ARB,
			0
		};
		HGLRC modernCtx = wglCreateContextAttribsARB(_hDC, nullptr, attribs);
		if(modernCtx) {
			wglMakeCurrent(_hDC, modernCtx);
			wglDeleteContext(tmpCtx);
			_hGLRC = modernCtx;
			MessageManager::Log("[WGL] OpenGL 3.3 compatibility context created.");
		} else {
			MessageManager::Log("[WGL] 3.3 context failed, falling back to legacy.");
			_hGLRC = tmpCtx;
		}
	} else {
		MessageManager::Log("[WGL] wglCreateContextAttribsARB not available, using legacy context.");
		_hGLRC = tmpCtx;
	}

	wglMakeCurrent(_hDC, _hGLRC);

	const char* glVersion = (const char*)glGetString(GL_VERSION);
	const char* glRenderer = (const char*)glGetString(GL_RENDERER);
	const char* glslVersion = (const char*)glGetString(GL_SHADING_LANGUAGE_VERSION);
	MessageManager::Log(std::string("[WGL] Context ready. GL_VERSION=") + (glVersion ? glVersion : "?") +
		" GLSL=" + (glslVersion ? glslVersion : "?") + " Renderer=" + (glRenderer ? glRenderer : "?"));

	if(!glfn::LoadGlFunctions()) {
		MessageManager::Log("[WGL] Could not resolve all required OpenGL functions.");
		Cleanup();
		return false;
	}

	if(!InitGlObjects()) {
		Cleanup();
		return false;
	}

	_shaderDirty = true;
	return true;
}

bool WglRenderer::InitGlObjects()
{
	if(glfn::GenVertexArrays) {
		GenVertexArrays(1, &_vao);
	}

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

GLuint WglRenderer::CompileBlitProgram()
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
			MessageManager::Log(std::string("[WGL] Blit shader error: ") + log);
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

void WglRenderer::Cleanup()
{
	if(!_hGLRC) {
		return;
	}
	wglMakeCurrent(_hDC, _hGLRC);

	_shaderChain.reset(new GlShaderChain());
	_loadedShaderPath.clear();

	if(_frameTexture) { glDeleteTextures(1, &_frameTexture); _frameTexture = 0; }
	if(_emuHud.Texture) { glDeleteTextures(1, &_emuHud.Texture); _emuHud.Texture = 0; }
	if(_scriptHud.Texture) { glDeleteTextures(1, &_scriptHud.Texture); _scriptHud.Texture = 0; }
	if(_quadVbo) { DeleteBuffers(1, &_quadVbo); _quadVbo = 0; }
	if(_quadVboFlip) { DeleteBuffers(1, &_quadVboFlip); _quadVboFlip = 0; }
	if(_vao && glfn::DeleteVertexArrays) { DeleteVertexArrays(1, &_vao); _vao = 0; }
	if(_blitProgram) { DeleteProgram(_blitProgram); _blitProgram = 0; }

	wglMakeCurrent(nullptr, nullptr);
	wglDeleteContext(_hGLRC);
	_hGLRC = nullptr;

	if(_hDC) {
		ReleaseDC(_hWnd, _hDC);
		_hDC = nullptr;
	}

	_frameTexWidth = _frameTexHeight = 0;
	_emuHud = GlHud();
	_scriptHud = GlHud();
}

void WglRenderer::OnRendererThreadStarted()
{
	Reset();
}

void WglRenderer::Reset()
{
	Cleanup();
	if(Init()) {
		_emu->GetVideoRenderer()->RegisterRenderingDevice(this);
	} else {
		Cleanup();
	}
}

void WglRenderer::EnsureFrameTexture(uint32_t width, uint32_t height)
{
	if(_frameTexture == 0) {
		glGenTextures(1, &_frameTexture);
		_frameTexWidth = _frameTexHeight = 0;
	}
	glBindTexture(GL_TEXTURE_2D, _frameTexture);
	if(_frameTexWidth != width || _frameTexHeight != height) {
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_BGRA_EXT, GL_UNSIGNED_INT_8_8_8_8_REV, nullptr);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
		_frameTexWidth = width;
		_frameTexHeight = height;
	}
}

void WglRenderer::ClearFrame()
{
	auto lock = _frameLock.AcquireSafe();
	if(_frameBuffer) {
		memset(_frameBuffer, 0, _requiredWidth * _requiredHeight * _bytesPerPixel);
	}
	_frameChanged = true;
}

void WglRenderer::UpdateFrame(RenderedFrame& frame)
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

void WglRenderer::UpdateShaderChain()
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
			MessageManager::Log("[WGL] Shader load failed: " + _shaderChain->GetError());
			_shaderChain.reset(new GlShaderChain());
		}
	}
}

void WglRenderer::DrawTextureToScreen(GLuint texture, bool flipped, bool blend)
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

void WglRenderer::DrawHud(GlHud& hud, RenderSurfaceInfo& surface)
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
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, surface.Width, surface.Height, 0, GL_BGRA_EXT, GL_UNSIGNED_INT_8_8_8_8_REV, surface.Buffer);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
		hud.Width = surface.Width;
		hud.Height = surface.Height;
	} else if(surface.IsDirty) {
		glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, surface.Width, surface.Height, GL_BGRA_EXT, GL_UNSIGNED_INT_8_8_8_8_REV, surface.Buffer);
	}

	DrawTextureToScreen(hud.Texture, true, true);
}

void WglRenderer::Render(RenderSurfaceInfo& emuHud, RenderSurfaceInfo& scriptHud)
{
	if(!_hGLRC) {
		if(!Init()) {
			return;
		}
	}

	wglMakeCurrent(_hDC, _hGLRC);

	VideoConfig cfg = _emu->GetSettings()->GetVideoConfig();
	_useBilinearInterpolation = cfg.UseBilinearInterpolation;

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
			glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, reqW, reqH, GL_BGRA_EXT, GL_UNSIGNED_INT_8_8_8_8_REV, _frameBuffer);
		}
	}

	glfn::BindFramebuffer(GL_FRAMEBUFFER, 0);
	glViewport(0, 0, (GLsizei)_screenWidth, (GLsizei)_screenHeight);
	glClearColor(0.0f, 0.0f, 0.0f, 1.0f);
	glClear(GL_COLOR_BUFFER_BIT);

	if(_shaderChain && _shaderChain->IsValid()) {
		_shaderChain->Render(_frameTexture, (int)reqW, (int)reqH, (int)_screenWidth, (int)_screenHeight, _frameCount);
		glfn::BindFramebuffer(GL_FRAMEBUFFER, 0);
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

	SwapBuffers(_hDC);
	_frameCount++;
}

void WglRenderer::SetExclusiveFullscreenMode(bool fullscreen, void* windowHandle)
{
	// Exclusive fullscreen is handled by the DX renderer; nothing to do here.
}
