#pragma once
#include <string>
#include <vector>
#include <utility>
#include <map>
#include "SdlGl.h"
#include "Core/Shared/Video/ShaderManager.h"

// Loads and runs a libretro-style GLSL shader preset (.glslp) or a single .glsl
// shader on top of the emulator's video output, using OpenGL.
//
// Supported subset of the libretro GLSL spec:
//  - Single and multi-pass .glslp presets (shaders=N, shaderM=..., etc.)
//  - scale_type / scale_type_x / scale_type_y = source | viewport | absolute
//  - scale / scale_x / scale_y
//  - filter_linearM (input filtering), wrap_modeM (clamp/edge/repeat/mirror)
//  - frame_count_modM
//  - #pragma parameter defaults + preset parameter overrides
//  - Standard uniforms/attributes: MVPMatrix, FrameCount, FrameDirection,
//    OutputSize, TextureSize, InputSize, Texture, VertexCoord, TexCoord, Color
//
// Not currently supported (documented limitations): LUT "textures=" entries,
// PassPrev*/Orig* history samplers, float/sRGB framebuffers, runtime parameter UI.

class ShaderPassConfig
{
public:
	std::string ShaderPath;
	bool FilterLinear = false;
	std::string ScaleTypeX = "source";
	std::string ScaleTypeY = "source";
	float ScaleX = 1.0f;
	float ScaleY = 1.0f;
	std::string WrapMode = "clamp_to_border";
	int FrameCountMod = 0;
	std::string Alias;
};

class ShaderPass
{
public:
	GLuint Program = 0;
	GLuint Fbo = 0;
	GLuint Texture = 0;
	int TexWidth = 0;
	int TexHeight = 0;
	int OutWidth = 0;
	int OutHeight = 0;

	GLint uMVP = -1;
	GLint uFrameCount = -1;
	GLint uFrameDirection = -1;
	GLint uOutputSize = -1;
	GLint uTextureSize = -1;
	GLint uInputSize = -1;
	GLint uTexture = -1;
	GLint aVertexCoord = -1;
	GLint aTexCoord = -1;
	GLint aColor = -1;

	ShaderPassConfig Config;
	//One entry per #pragma parameter used by this pass. The value is read live from
	//ShaderManager each frame (falling back to Default when the user hasn't overridden it).
	struct PassParam
	{
		GLint Loc = -1;
		std::string Name;
		float Default = 0;
	};
	std::vector<PassParam> Params;
};

class GlShaderChain
{
public:
	GlShaderChain();
	~GlShaderChain();

	// Loads a .glslp preset or a single .glsl file. Returns false on error.
	bool Load(const std::string& presetPath);
	bool IsValid() const { return _valid; }
	std::string GetError() const { return _error; }

	// Renders the chain. inputTexture holds the emulator frame (top-left origin),
	// content sized inputWidth x inputHeight. The final pass is drawn to the
	// currently bound framebuffer (the screen) using the given viewport size.
	void Render(GLuint inputTexture, int inputWidth, int inputHeight, int viewportWidth, int viewportHeight, int frameCount);

private:
	bool _valid = false;
	std::string _error;
	std::string _presetDir;
	std::vector<ShaderPass> _passes;
	std::map<std::string, float> _presetParams;

	GLuint _quadVbo = 0;       // clip-space quad, normal texcoords (FBO->FBO / FBO->screen)
	GLuint _quadVboFlip = 0;   // clip-space quad, V-flipped texcoords (reads top-left-origin input)
	GLuint _vao = 0;

	void Cleanup();
	bool InitQuads();
	bool ParsePreset(const std::string& path);
	bool BuildPass(ShaderPass& pass);
	GLuint CompileProgram(const std::string& source, std::string& errorOut);
	void BindStandardLocations(ShaderPass& pass);
	void ApplyParameters(ShaderPass& pass, const std::string& source);
	void DrawQuad(ShaderPass& pass, bool flipped);

	static std::string ReadFile(const std::string& path, bool& ok);
};
