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
.\build-windows.ps1 -Package -ZipName "MesenOrion-v3.0.1-win-x64.zip"
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

### Building a Debian package (.deb)

A helper script mirrors the Windows `build-windows.ps1` workflow: it cleans, builds
(GCC by default), stages the published files into the `distributable/mesen-orion/`
package tree (`usr/bin/mesen-orion`, `usr/lib/mesen-orion/`, `usr/share/mesen-orion/shaders/`)
and runs `dpkg-deb`, producing `mesen-orion_<version>_amd64.deb` (version taken from
`distributable/mesen-orion/DEBIAN/control`).

```sh
./build-linux.sh                # make clean + build (GCC) + package .deb
./build-linux.sh --clang        # build with Clang instead of GCC
./build-linux.sh --skip-build   # only repackage the existing build output
./build-linux.sh --output /tmp  # place the .deb elsewhere
./build-linux.sh --help         # all options
```

Requires `dpkg-deb` (from the `dpkg` package), plus `make`, a C++17 compiler and the .NET 8 SDK.

### Building an AppImage

A self-contained, single-file **AppImage** can be built from the repository root:

```sh
./Linux/appimage/appimage.sh          # x86_64
./Linux/appimage/appimage-arm64.sh    # ARM64
```

These compile the app, download `appimagetool`, stage everything (including the bundled
**Shaders** collection under `usr/share/mesen-orion/shaders/` and the AppStream metainfo) and
produce `MesenOrion-<version>-x86_64.AppImage` / `MesenOrion-<version>-aarch64.AppImage`
(version read from `distributable/mesen-orion/DEBIAN/control`, following AppImageHub's
standard naming).

> The AppImage links SDL2 and OpenGL (libGL) dynamically, so the target machine must have
> those libraries installed (they are not bundled).

## macOS

To build macOS, install SDL2 (i.e via Homebrew) and the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).  

Once SDL2 and the .NET 8 SDK are installed, run `make`.