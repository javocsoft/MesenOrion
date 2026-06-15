## Windows

**Requirements:**
- [Visual Studio 2022 or newer](https://visualstudio.microsoft.com/) with the **Desktop development with C++** workload
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Using the build script (recommended)

A PowerShell script is provided to handle the full build and packaging workflow.
Run it from a **Developer PowerShell for VS** prompt (so MSBuild is in PATH):

```powershell
# Build Release only
.\build-windows.ps1

# Build Debug and Release
.\build-windows.ps1 -Configuration All

# Build Release + generate distributable ZIP
.\build-windows.ps1 -Package

# Build Release + generate ZIP with a custom name
.\build-windows.ps1 -Package -ZipName "MesenOrion-v3.0.0-win-x64.zip"
```

Run `.\build-windows.ps1 -Help` for the full list of parameters.

### Manual build

**IDE:** open `Mesen.sln` in Visual Studio, set configuration to `Release` / `x64`,
build the solution, and set the `UI` project as the startup project.

**Command line** (MSBuild in PATH required):
```cmd
msbuild Mesen.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v145 /m
```

**Distributable single-file executable** (equivalent to the Linux release):
```cmd
msbuild Mesen.sln /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v145 /m
cd UI
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
```
The output lands in `bin\win-x64\Release\win-x64\publish\`. Copy `MesenCore.dll`,
`Dependencies.zip`, and the `Shaders\` folder from the repo root into that directory
to complete the package.

> **Note:** An OpenGL 2.0-capable GPU is required for GLSL shader support.
> Enable *Use Software Renderer* in Options → Video to disable the OpenGL renderer.

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