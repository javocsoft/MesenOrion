#include "GlShaderChain.h"
#include "Core/Shared/MessageManager.h"
#include "Utilities/FolderUtilities.h"
#include <fstream>
#include <sstream>
#include <map>
#include <cstring>
#include <cstdlib>
#include <cstdio>
#include <algorithm>

using glfn::CreateShader; using glfn::ShaderSource; using glfn::CompileShader;
using glfn::GetShaderiv; using glfn::GetShaderInfoLog; using glfn::DeleteShader;
using glfn::AttachShader; using glfn::DetachShader; using glfn::CreateProgram;
using glfn::LinkProgram; using glfn::GetProgramiv; using glfn::GetProgramInfoLog;
using glfn::UseProgram; using glfn::DeleteProgram; using glfn::GetUniformLocation;
using glfn::GetAttribLocation; using glfn::Uniform1i; using glfn::Uniform1f;
using glfn::Uniform2fv; using glfn::UniformMatrix4fv; using glfn::EnableVertexAttribArray;
using glfn::DisableVertexAttribArray; using glfn::VertexAttribPointer; using glfn::GenBuffers;
using glfn::BindBuffer; using glfn::BufferData; using glfn::DeleteBuffers;
using glfn::GenVertexArrays; using glfn::BindVertexArray; using glfn::DeleteVertexArrays;
using glfn::GenFramebuffers; using glfn::BindFramebuffer; using glfn::FramebufferTexture2D;
using glfn::CheckFramebufferStatus; using glfn::DeleteFramebuffers; using glfn::ActiveTexture;
// GL 1.0/1.1 entry points are called with their gl* prefix (see SdlGl.h):
// glGenTextures, glBindTexture, glTexImage2D, glTexParameteri, glDeleteTextures,
// glViewport, glDrawArrays.

namespace
{
	std::string Trim(const std::string& s)
	{
		size_t a = s.find_first_not_of(" \t\r\n");
		if(a == std::string::npos) {
			return "";
		}
		size_t b = s.find_last_not_of(" \t\r\n");
		return s.substr(a, b - a + 1);
	}

	std::string StripQuotes(const std::string& s)
	{
		if(s.size() >= 2 && (s.front() == '"' || s.front() == '\'') && s.back() == s.front()) {
			return s.substr(1, s.size() - 2);
		}
		return s;
	}
}

GlShaderChain::GlShaderChain()
{
}

GlShaderChain::~GlShaderChain()
{
	Cleanup();
}

void GlShaderChain::Cleanup()
{
	for(ShaderPass& pass : _passes) {
		if(pass.Program) {
			DeleteProgram(pass.Program);
		}
		if(pass.Fbo) {
			DeleteFramebuffers(1, &pass.Fbo);
		}
		if(pass.Texture) {
			glDeleteTextures(1, &pass.Texture);
		}
	}
	_passes.clear();

	if(_quadVbo) {
		DeleteBuffers(1, &_quadVbo);
		_quadVbo = 0;
	}
	if(_quadVboFlip) {
		DeleteBuffers(1, &_quadVboFlip);
		_quadVboFlip = 0;
	}
	if(_vao && glfn::DeleteVertexArrays) {
		DeleteVertexArrays(1, &_vao);
		_vao = 0;
	}
	_valid = false;
}

std::string GlShaderChain::ReadFile(const std::string& path, bool& ok)
{
	std::ifstream file(path, std::ios::binary);
	if(!file) {
		ok = false;
		return "";
	}
	std::stringstream ss;
	ss << file.rdbuf();
	ok = true;
	return ss.str();
}

bool GlShaderChain::InitQuads()
{
	// Each vertex: vec2 clip pos, vec2 texcoord. Drawn as a triangle strip.
	// Normal: clip(-1,-1)->uv(0,0) ... used when sampling FBO textures and for screen output.
	const float quad[] = {
		-1.0f, -1.0f, 0.0f, 0.0f,
		 1.0f, -1.0f, 1.0f, 0.0f,
		-1.0f,  1.0f, 0.0f, 1.0f,
		 1.0f,  1.0f, 1.0f, 1.0f,
	};
	// V-flipped texcoords: used for the pass that samples the original (top-left
	// origin) emulator frame so the displayed image ends up upright.
	const float quadFlip[] = {
		-1.0f, -1.0f, 0.0f, 1.0f,
		 1.0f, -1.0f, 1.0f, 1.0f,
		-1.0f,  1.0f, 0.0f, 0.0f,
		 1.0f,  1.0f, 1.0f, 0.0f,
	};

	if(glfn::GenVertexArrays) {
		GenVertexArrays(1, &_vao);
	}
	GenBuffers(1, &_quadVbo);
	GenBuffers(1, &_quadVboFlip);
	if(!_quadVbo || !_quadVboFlip) {
		_error = "Failed to allocate GL buffers for shader quads.";
		return false;
	}

	BindBuffer(GL_ARRAY_BUFFER, _quadVbo);
	BufferData(GL_ARRAY_BUFFER, sizeof(quad), quad, GL_STATIC_DRAW);
	BindBuffer(GL_ARRAY_BUFFER, _quadVboFlip);
	BufferData(GL_ARRAY_BUFFER, sizeof(quadFlip), quadFlip, GL_STATIC_DRAW);
	BindBuffer(GL_ARRAY_BUFFER, 0);
	return true;
}

bool GlShaderChain::Load(const std::string& presetPath)
{
	Cleanup();
	_error.clear();

	if(!InitQuads()) {
		return false;
	}

	_presetDir = FolderUtilities::GetFolderName(presetPath);

	std::string ext = FolderUtilities::GetExtension(presetPath);
	std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);

	if(ext == ".glslp") {
		if(!ParsePreset(presetPath)) {
			Cleanup();
			return false;
		}
	} else {
		// Treat a standalone .glsl as a single-pass preset that scales to the viewport.
		ShaderPass pass;
		pass.Config.ShaderPath = presetPath;
		pass.Config.ScaleTypeX = "viewport";
		pass.Config.ScaleTypeY = "viewport";
		pass.Config.ScaleX = 1.0f;
		pass.Config.ScaleY = 1.0f;
		_passes.push_back(pass);
	}

	if(_passes.empty()) {
		_error = "Shader preset contains no passes.";
		Cleanup();
		return false;
	}

	for(ShaderPass& pass : _passes) {
		if(!BuildPass(pass)) {
			Cleanup();
			return false;
		}
	}

	_valid = true;
	return true;
}

bool GlShaderChain::ParsePreset(const std::string& path)
{
	bool ok = false;
	std::string content = ReadFile(path, ok);
	if(!ok) {
		_error = "Could not read preset file: " + path;
		return false;
	}

	std::map<std::string, std::string> kv;
	std::stringstream ss(content);
	std::string line;
	while(std::getline(ss, line)) {
		std::string t = Trim(line);
		if(t.empty() || t[0] == '#' || (t.size() >= 2 && t[0] == '/' && t[1] == '/')) {
			continue;
		}
		size_t eq = t.find('=');
		if(eq == std::string::npos) {
			continue;
		}
		std::string key = Trim(t.substr(0, eq));
		std::string val = StripQuotes(Trim(t.substr(eq + 1)));
		kv[key] = val;
	}

	auto get = [&](const std::string& k) -> std::string {
		auto it = kv.find(k);
		return it == kv.end() ? std::string() : it->second;
	};

	int count = 0;
	if(!get("shaders").empty()) {
		count = atoi(get("shaders").c_str());
	}
	if(count <= 0) {
		_error = "Preset is missing a valid 'shaders' count.";
		return false;
	}

	for(int i = 0; i < count; i++) {
		std::string n = std::to_string(i);
		ShaderPass pass;
		std::string shaderRel = get("shader" + n);
		if(shaderRel.empty()) {
			_error = "Preset is missing shader" + n;
			return false;
		}
		pass.Config.ShaderPath = FolderUtilities::CombinePath(_presetDir, shaderRel);

		std::string filt = get("filter_linear" + n);
		pass.Config.FilterLinear = (filt == "true" || filt == "1");

		std::string scaleType = get("scale_type" + n);
		std::string scaleTypeX = get("scale_type_x" + n);
		std::string scaleTypeY = get("scale_type_y" + n);
		if(!scaleType.empty()) {
			pass.Config.ScaleTypeX = pass.Config.ScaleTypeY = scaleType;
		}
		if(!scaleTypeX.empty()) {
			pass.Config.ScaleTypeX = scaleTypeX;
		}
		if(!scaleTypeY.empty()) {
			pass.Config.ScaleTypeY = scaleTypeY;
		}

		std::string scale = get("scale" + n);
		std::string scaleX = get("scale_x" + n);
		std::string scaleY = get("scale_y" + n);
		if(!scale.empty()) {
			pass.Config.ScaleX = pass.Config.ScaleY = (float)atof(scale.c_str());
		}
		if(!scaleX.empty()) {
			pass.Config.ScaleX = (float)atof(scaleX.c_str());
		}
		if(!scaleY.empty()) {
			pass.Config.ScaleY = (float)atof(scaleY.c_str());
		}

		std::string wrap = get("wrap_mode" + n);
		if(!wrap.empty()) {
			pass.Config.WrapMode = wrap;
		}

		std::string fcm = get("frame_count_mod" + n);
		if(!fcm.empty()) {
			pass.Config.FrameCountMod = atoi(fcm.c_str());
		}

		pass.Config.Alias = get("alias" + n);

		_passes.push_back(pass);
	}

	// Preset-level parameter overrides (name = value) are applied in ApplyParameters
	// via this stored map.
	_presetParams.clear();
	std::string paramList = get("parameters");
	if(!paramList.empty()) {
		std::stringstream ps(paramList);
		std::string name;
		while(std::getline(ps, name, ';')) {
			name = Trim(name);
			std::string v = get(name);
			if(!v.empty()) {
				_presetParams[name] = (float)atof(v.c_str());
			}
		}
	}

	return true;
}

GLuint GlShaderChain::CompileProgram(const std::string& source, std::string& errorOut)
{
	// Inject the stage define after a leading #version directive (if any), so
	// libretro-style shaders that branch on VERTEX / FRAGMENT compile correctly.
	auto buildSource = [&](const char* stageDefine) -> std::string {
		std::string src = source;
		size_t versionPos = src.find("#version");
		std::string header;
		std::string body;
		if(versionPos != std::string::npos) {
			size_t eol = src.find('\n', versionPos);
			if(eol == std::string::npos) {
				eol = src.size();
			} else {
				eol += 1;
			}
			header = src.substr(0, eol);
			body = src.substr(eol);
		} else {
			header = "#version 120\n";
			body = src;
		}
		std::string defines = std::string("#define ") + stageDefine + "\n";
		return header + defines + body;
	};

	auto compileStage = [&](GLenum type, const std::string& src) -> GLuint {
		GLuint shader = CreateShader(type);
		const char* ptr = src.c_str();
		ShaderSource(shader, 1, &ptr, nullptr);
		CompileShader(shader);
		GLint ok = GL_FALSE;
		GetShaderiv(shader, GL_COMPILE_STATUS, &ok);
		if(!ok) {
			char log[2048];
			GLsizei len = 0;
			GetShaderInfoLog(shader, sizeof(log) - 1, &len, log);
			log[len] = '\0';
			errorOut = std::string(type == GL_VERTEX_SHADER ? "Vertex" : "Fragment") + " shader compile error:\n" + log;
			DeleteShader(shader);
			return 0;
		}
		return shader;
	};

	GLuint vs = compileStage(GL_VERTEX_SHADER, buildSource("VERTEX"));
	if(!vs) {
		return 0;
	}
	GLuint fs = compileStage(GL_FRAGMENT_SHADER, buildSource("FRAGMENT"));
	if(!fs) {
		DeleteShader(vs);
		return 0;
	}

	GLuint program = CreateProgram();
	AttachShader(program, vs);
	AttachShader(program, fs);
	LinkProgram(program);
	DetachShader(program, vs);
	DetachShader(program, fs);
	DeleteShader(vs);
	DeleteShader(fs);

	GLint linked = GL_FALSE;
	GetProgramiv(program, GL_LINK_STATUS, &linked);
	if(!linked) {
		char log[2048];
		GLsizei len = 0;
		GetProgramInfoLog(program, sizeof(log) - 1, &len, log);
		log[len] = '\0';
		errorOut = std::string("Shader link error:\n") + log;
		DeleteProgram(program);
		return 0;
	}
	return program;
}

void GlShaderChain::BindStandardLocations(ShaderPass& pass)
{
	GLuint p = pass.Program;
	pass.uMVP = GetUniformLocation(p, "MVPMatrix");
	pass.uFrameCount = GetUniformLocation(p, "FrameCount");
	pass.uFrameDirection = GetUniformLocation(p, "FrameDirection");
	pass.uOutputSize = GetUniformLocation(p, "OutputSize");
	pass.uTextureSize = GetUniformLocation(p, "TextureSize");
	pass.uInputSize = GetUniformLocation(p, "InputSize");
	pass.uTexture = GetUniformLocation(p, "Texture");
	pass.aVertexCoord = GetAttribLocation(p, "VertexCoord");
	pass.aTexCoord = GetAttribLocation(p, "TexCoord");
	pass.aColor = GetAttribLocation(p, "Color");
}

void GlShaderChain::ApplyParameters(ShaderPass& pass, const std::string& source)
{
	// Parse "#pragma parameter NAME "desc" initial min max step" lines.
	std::stringstream ss(source);
	std::string line;
	while(std::getline(ss, line)) {
		std::string t = Trim(line);
		const std::string tag = "#pragma parameter";
		if(t.compare(0, tag.size(), tag) != 0) {
			continue;
		}
		std::string rest = Trim(t.substr(tag.size()));
		// name is the first whitespace-delimited token
		size_t sp = rest.find_first_of(" \t");
		if(sp == std::string::npos) {
			continue;
		}
		std::string name = rest.substr(0, sp);

		// description is quoted, followed by: initial min max step
		size_t q1 = rest.find('"');
		size_t q2 = (q1 == std::string::npos) ? std::string::npos : rest.find('"', q1 + 1);
		std::string desc;
		float initial = 0.0f, minVal = 0.0f, maxVal = 1.0f, step = 0.01f;
		if(q2 != std::string::npos) {
			desc = rest.substr(q1 + 1, q2 - q1 - 1);
			std::string nums = Trim(rest.substr(q2 + 1));
			sscanf(nums.c_str(), "%f %f %f %f", &initial, &minVal, &maxVal, &step);
		}

		(void)desc; (void)minVal; (void)maxVal; (void)step;

		// Preset files may override the default value.
		auto it = _presetParams.find(name);
		float defaultValue = (it != _presetParams.end()) ? it->second : initial;

		GLint loc = GetUniformLocation(pass.Program, name.c_str());
		if(loc >= 0) {
			ShaderPass::PassParam p;
			p.Loc = loc;
			p.Name = name;
			p.Default = defaultValue;
			pass.Params.push_back(p);
		}
	}
}

bool GlShaderChain::BuildPass(ShaderPass& pass)
{
	bool ok = false;
	std::string source = ReadFile(pass.Config.ShaderPath, ok);
	if(!ok) {
		_error = "Could not read shader: " + pass.Config.ShaderPath;
		return false;
	}

	std::string err;
	pass.Program = CompileProgram(source, err);
	if(!pass.Program) {
		_error = "[" + pass.Config.ShaderPath + "] " + err;
		return false;
	}

	BindStandardLocations(pass);
	ApplyParameters(pass, source);
	return true;
}

static GLint WrapModeToGl(const std::string& mode)
{
	if(mode == "repeat") {
		return GL_REPEAT;
	}
	if(mode == "mirrored_repeat") {
		return GL_MIRRORED_REPEAT;
	}
	// clamp_to_border behaves like clamp_to_edge here (no border colour configured)
	return GL_CLAMP_TO_EDGE;
}

void GlShaderChain::DrawQuad(ShaderPass& pass, bool flipped)
{
	if(glfn::BindVertexArray) {
		BindVertexArray(_vao);
	}
	BindBuffer(GL_ARRAY_BUFFER, flipped ? _quadVboFlip : _quadVbo);

	const GLsizei stride = 4 * sizeof(float);
	if(pass.aVertexCoord >= 0) {
		EnableVertexAttribArray(pass.aVertexCoord);
		VertexAttribPointer(pass.aVertexCoord, 2, GL_FLOAT, GL_FALSE, stride, (void*)0);
	}
	if(pass.aTexCoord >= 0) {
		EnableVertexAttribArray(pass.aTexCoord);
		VertexAttribPointer(pass.aTexCoord, 2, GL_FLOAT, GL_FALSE, stride, (void*)(2 * sizeof(float)));
	}

	glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);

	if(pass.aVertexCoord >= 0) {
		DisableVertexAttribArray(pass.aVertexCoord);
	}
	if(pass.aTexCoord >= 0) {
		DisableVertexAttribArray(pass.aTexCoord);
	}
	BindBuffer(GL_ARRAY_BUFFER, 0);
	if(glfn::BindVertexArray) {
		BindVertexArray(0);
	}
}

void GlShaderChain::Render(GLuint inputTexture, int inputWidth, int inputHeight, int viewportWidth, int viewportHeight, int frameCount)
{
	if(!_valid || _passes.empty()) {
		return;
	}

	static const float identity[16] = {
		1, 0, 0, 0,
		0, 1, 0, 0,
		0, 0, 1, 0,
		0, 0, 0, 1
	};

	int curInputW = inputWidth;
	int curInputH = inputHeight;
	int curTexW = inputWidth;
	int curTexH = inputHeight;
	GLuint curInputTex = inputTexture;

	size_t lastPass = _passes.size() - 1;

	for(size_t i = 0; i < _passes.size(); i++) {
		ShaderPass& pass = _passes[i];
		bool isLast = (i == lastPass);

		// Resolve output size for this pass.
		auto resolveAxis = [&](const std::string& type, float scale, int srcSize, int viewSize) -> int {
			if(type == "viewport") {
				return std::max(1, (int)(viewSize * scale));
			} else if(type == "absolute") {
				return std::max(1, (int)scale);
			}
			// source
			return std::max(1, (int)(srcSize * scale));
		};

		int outW, outH;
		if(isLast) {
			// Final pass always fills the screen viewport.
			outW = viewportWidth;
			outH = viewportHeight;
		} else {
			outW = resolveAxis(pass.Config.ScaleTypeX, pass.Config.ScaleX, curInputW, viewportWidth);
			outH = resolveAxis(pass.Config.ScaleTypeY, pass.Config.ScaleY, curInputH, viewportHeight);
		}
		pass.OutWidth = outW;
		pass.OutHeight = outH;

		// Target framebuffer: intermediate passes render to their FBO texture.
		if(isLast) {
			BindFramebuffer(GL_FRAMEBUFFER, 0);
		} else {
			if(pass.Texture == 0) {
				glGenTextures(1, &pass.Texture);
				GenFramebuffers(1, &pass.Fbo);
			}
			if(pass.TexWidth != outW || pass.TexHeight != outH) {
				glBindTexture(GL_TEXTURE_2D, pass.Texture);
				glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, outW, outH, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
				glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
				glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
				glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
				glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
				pass.TexWidth = outW;
				pass.TexHeight = outH;
			}
			BindFramebuffer(GL_FRAMEBUFFER, pass.Fbo);
			FramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, pass.Texture, 0);
			if(CheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
				BindFramebuffer(GL_FRAMEBUFFER, 0);
				continue;
			}
		}

		glViewport(0, 0, outW, outH);
		UseProgram(pass.Program);

		// Bind input texture (unit 0) with this pass's filtering / wrap settings.
		ActiveTexture(GL_TEXTURE0);
		glBindTexture(GL_TEXTURE_2D, curInputTex);
		GLint filter = pass.Config.FilterLinear ? GL_LINEAR : GL_NEAREST;
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter);
		GLint wrap = WrapModeToGl(pass.Config.WrapMode);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, wrap);
		glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, wrap);

		if(pass.uTexture >= 0) {
			Uniform1i(pass.uTexture, 0);
		}
		if(pass.uMVP >= 0) {
			UniformMatrix4fv(pass.uMVP, 1, GL_FALSE, identity);
		}
		if(pass.uFrameDirection >= 0) {
			Uniform1i(pass.uFrameDirection, 1);
		}
		if(pass.uFrameCount >= 0) {
			int fc = pass.Config.FrameCountMod > 0 ? (frameCount % pass.Config.FrameCountMod) : frameCount;
			Uniform1i(pass.uFrameCount, fc);
		}
		if(pass.uInputSize >= 0) {
			float v[2] = { (float)curInputW, (float)curInputH };
			Uniform2fv(pass.uInputSize, 1, v);
		}
		if(pass.uTextureSize >= 0) {
			float v[2] = { (float)curTexW, (float)curTexH };
			Uniform2fv(pass.uTextureSize, 1, v);
		}
		if(pass.uOutputSize >= 0) {
			float v[2] = { (float)outW, (float)outH };
			Uniform2fv(pass.uOutputSize, 1, v);
		}

		// User-tweakable shader parameters (#pragma parameter), read live each frame.
		for(ShaderPass::PassParam& param : pass.Params) {
			Uniform1f(param.Loc, ShaderManager::GetShaderParamValue(param.Name, param.Default));
		}

		// Only the pass reading the original emulator frame needs the V-flip.
		DrawQuad(pass, i == 0);

		// Output of this pass becomes the input of the next.
		curInputTex = pass.Texture;
		curInputW = outW;
		curInputH = outH;
		curTexW = pass.TexWidth;
		curTexH = pass.TexHeight;
	}

	UseProgram(0);
	BindFramebuffer(GL_FRAMEBUFFER, 0);
}
