#include "pch.h"
#include "WglFunctions.h"
#include "Core/Shared/MessageManager.h"

namespace glfn
{
	#define MESEN_GL_DEFINE(type, name) type name = nullptr;
	MESEN_GL_FUNCTIONS(MESEN_GL_DEFINE)
	MESEN_GL_OPTIONAL(MESEN_GL_DEFINE)
	#undef MESEN_GL_DEFINE

	static void* GetGlProc(const char* name)
	{
		// wglGetProcAddress only works for GL 1.2+ extension functions.
		// GL 1.0/1.1 core functions are exported directly by opengl32.dll
		// and must be resolved via GetProcAddress on the module itself.
		void* p = (void*)wglGetProcAddress(name);
		if(!p || p == (void*)0x1 || p == (void*)0x2 || p == (void*)0x3 || p == (void*)-1) {
			HMODULE module = GetModuleHandleA("opengl32.dll");
			if(module) {
				p = (void*)GetProcAddress(module, name);
			}
		}
		return p;
	}

	bool LoadGlFunctions()
	{
		bool ok = true;

		#define MESEN_GL_LOAD(type, name) \
			name = (type)GetGlProc("gl" #name); \
			if(!name) { \
				MessageManager::Log(std::string("[WGL] Missing OpenGL function: gl") + #name); \
				ok = false; \
			}
		MESEN_GL_FUNCTIONS(MESEN_GL_LOAD)
		#undef MESEN_GL_LOAD

		// Optional functions - log but don't fail if missing
		#define MESEN_GL_LOAD_OPT(type, name) name = (type)GetGlProc("gl" #name);
		MESEN_GL_OPTIONAL(MESEN_GL_LOAD_OPT)
		#undef MESEN_GL_LOAD_OPT

		return ok;
	}
}
