## Windows

**Requirements:**
- [Visual Studio 2022 or newer](https://visualstudio.microsoft.com/) with the **Desktop development with C++** workload
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

**Build (Debug / development):**

1. Open `Mesen.sln` in Visual Studio.
2. Set configuration to `Release` / `x64` and build the solution.
3. Set the startup project to `UI` and run.

Alternatively, from the command line (requires MSBuild in PATH):
```cmd
msbuild Mesen.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v145 /m
```

**Build a distributable single-file executable** (same as the Linux release):
```cmd
msbuild Mesen.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v145 /m
cd UI
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
```
The output will be in `bin\win-x64\Release\win-x64\publish\`. Copy `MesenCore.dll` and `Dependencies.zip` from `bin\win-x64\Release\` into that folder to complete the package.

> **Note:** An OpenGL 2.0-capable GPU is required for GLSL shader support. Set `MESEN_NO_GL=1` (or enable *Use Software Renderer* in Settings) to disable the OpenGL renderer.

## Linux

To build under Linux you need a version of Clang or GCC that supports C++17.  

Additionally, SDL2 and the [.NET 8 SDK](https://learn.microsoft.com/en-us/dotnet/core/install/linux) must also be installed.

Once SDL2 and the .NET 8 SDK are installed, run `make` to compile with Clang.  
To compile with GCC instead, use `USE_GCC=true make`.  
**Note:** Mesen Orion usually runs faster when built with Clang instead of GCC.

GLSL shaders require the OpenGL renderer (default on Linux). Set `MESEN_NO_GL=1` to fall back to the classic SDL renderer if needed.

## macOS

To build macOS, install SDL2 (i.e via Homebrew) and the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).  

Once SDL2 and the .NET 8 SDK are installed, run `make`.