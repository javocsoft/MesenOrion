#pragma once
// Minimal OpenGL function loader for the SDL/Linux shader renderer.
// Function pointers are resolved at runtime through SDL_GL_GetProcAddress so we
// don't depend on GLEW/GLAD or on the GL2+/GL3+ symbols being exported by libGL.
//
// All entry points live in the "glfn" namespace, e.g. glfn::CreateShader(...).

#define GL_GLEXT_PROTOTYPES 0
#include "SDL.h"
#include "SDL_opengl.h"

namespace glfn
{
	// X-macro list of every GL entry point used by the shader pipeline.
	#define MESEN_GL_FUNCTIONS(X) \
		X(PFNGLCREATESHADERPROC, CreateShader) \
		X(PFNGLSHADERSOURCEPROC, ShaderSource) \
		X(PFNGLCOMPILESHADERPROC, CompileShader) \
		X(PFNGLGETSHADERIVPROC, GetShaderiv) \
		X(PFNGLGETSHADERINFOLOGPROC, GetShaderInfoLog) \
		X(PFNGLDELETESHADERPROC, DeleteShader) \
		X(PFNGLATTACHSHADERPROC, AttachShader) \
		X(PFNGLDETACHSHADERPROC, DetachShader) \
		X(PFNGLCREATEPROGRAMPROC, CreateProgram) \
		X(PFNGLLINKPROGRAMPROC, LinkProgram) \
		X(PFNGLGETPROGRAMIVPROC, GetProgramiv) \
		X(PFNGLGETPROGRAMINFOLOGPROC, GetProgramInfoLog) \
		X(PFNGLUSEPROGRAMPROC, UseProgram) \
		X(PFNGLDELETEPROGRAMPROC, DeleteProgram) \
		X(PFNGLGETUNIFORMLOCATIONPROC, GetUniformLocation) \
		X(PFNGLGETATTRIBLOCATIONPROC, GetAttribLocation) \
		X(PFNGLBINDATTRIBLOCATIONPROC, BindAttribLocation) \
		X(PFNGLUNIFORM1IPROC, Uniform1i) \
		X(PFNGLUNIFORM1FPROC, Uniform1f) \
		X(PFNGLUNIFORM2FVPROC, Uniform2fv) \
		X(PFNGLUNIFORM4FVPROC, Uniform4fv) \
		X(PFNGLUNIFORMMATRIX4FVPROC, UniformMatrix4fv) \
		X(PFNGLENABLEVERTEXATTRIBARRAYPROC, EnableVertexAttribArray) \
		X(PFNGLDISABLEVERTEXATTRIBARRAYPROC, DisableVertexAttribArray) \
		X(PFNGLVERTEXATTRIBPOINTERPROC, VertexAttribPointer) \
		X(PFNGLGENBUFFERSPROC, GenBuffers) \
		X(PFNGLBINDBUFFERPROC, BindBuffer) \
		X(PFNGLBUFFERDATAPROC, BufferData) \
		X(PFNGLDELETEBUFFERSPROC, DeleteBuffers) \
		X(PFNGLGENFRAMEBUFFERSPROC, GenFramebuffers) \
		X(PFNGLBINDFRAMEBUFFERPROC, BindFramebuffer) \
		X(PFNGLFRAMEBUFFERTEXTURE2DPROC, FramebufferTexture2D) \
		X(PFNGLCHECKFRAMEBUFFERSTATUSPROC, CheckFramebufferStatus) \
		X(PFNGLDELETEFRAMEBUFFERSPROC, DeleteFramebuffers) \
		X(PFNGLACTIVETEXTUREPROC, ActiveTexture)
	// Note: GL 1.0/1.1 core entry points (glGenTextures, glTexImage2D, glViewport,
	// glClear, glDrawArrays, glBlendFunc, ...) have no PFN typedefs and are exported
	// directly by libGL, so they are called unqualified rather than loaded here.

	// Optional entry points - their absence is not fatal (VAOs are not required in a
	// compatibility profile, where the default VAO 0 is always valid).
	#define MESEN_GL_OPTIONAL(X) \
		X(PFNGLGENVERTEXARRAYSPROC, GenVertexArrays) \
		X(PFNGLBINDVERTEXARRAYPROC, BindVertexArray) \
		X(PFNGLDELETEVERTEXARRAYSPROC, DeleteVertexArrays)

	#define MESEN_GL_DECLARE(type, name) extern type name;
	MESEN_GL_FUNCTIONS(MESEN_GL_DECLARE)
	MESEN_GL_OPTIONAL(MESEN_GL_DECLARE)
	#undef MESEN_GL_DECLARE

	// Resolves all entry points. Returns false (and logs) if any are missing.
	// Must be called with a current GL context.
	bool LoadGlFunctions();
}
