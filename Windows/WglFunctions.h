#pragma once
// Minimal OpenGL function loader for the Windows/WGL shader renderer.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <GL/gl.h>
#include "GL/glext.h"

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

	// Optional entry points - VAOs not strictly required.
	#define MESEN_GL_OPTIONAL(X) \
		X(PFNGLGENVERTEXARRAYSPROC, GenVertexArrays) \
		X(PFNGLBINDVERTEXARRAYPROC, BindVertexArray) \
		X(PFNGLDELETEVERTEXARRAYSPROC, DeleteVertexArrays)

	#define MESEN_GL_DECLARE(type, name) extern type name;
	MESEN_GL_FUNCTIONS(MESEN_GL_DECLARE)
	MESEN_GL_OPTIONAL(MESEN_GL_DECLARE)
	#undef MESEN_GL_DECLARE

	// Resolves all entry points via wglGetProcAddress. Returns false if any are missing.
	// Must be called with a current WGL context.
	bool LoadGlFunctions();
}
