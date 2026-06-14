#include "SdlGl.h"
#include "Core/Shared/MessageManager.h"

namespace glfn
{
	#define MESEN_GL_DEFINE(type, name) type name = nullptr;
	MESEN_GL_FUNCTIONS(MESEN_GL_DEFINE)
	MESEN_GL_OPTIONAL(MESEN_GL_DEFINE)
	#undef MESEN_GL_DEFINE

	bool LoadGlFunctions()
	{
		bool ok = true;

		#define MESEN_GL_LOAD(type, name) \
			name = (type)SDL_GL_GetProcAddress("gl" #name); \
			if(!name) { \
				MessageManager::Log(std::string("[SDL/GL] Missing OpenGL function: gl") + #name); \
				ok = false; \
			}
		MESEN_GL_FUNCTIONS(MESEN_GL_LOAD)
		#undef MESEN_GL_LOAD

		//Optional functions - log but don't fail if missing
		#define MESEN_GL_LOAD_OPT(type, name) name = (type)SDL_GL_GetProcAddress("gl" #name);
		MESEN_GL_OPTIONAL(MESEN_GL_LOAD_OPT)
		#undef MESEN_GL_LOAD_OPT

		return ok;
	}
}
