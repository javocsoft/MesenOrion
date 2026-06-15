# Mesen Orion

<img width="64" height="64" alt="mesenorion-128x128" src="https://github.com/user-attachments/assets/5afa6d43-7dac-4afc-970d-0be7028a099f" />

**Mesen Orion** is a multi-system emulator (NES, SNES, Game Boy, Game Boy Advance, PC Engine, SMS/Game Gear, WonderSwan) for Windows, Linux and macOS.

It is a fork of [Mesen2](https://github.com/SourMesen/Mesen2) by Sour (whose 2.x line is no longer maintained), continuing development with a renewed UI and rendering-pipeline improvements. The first release of this fork is **v3.0.0**.

Repository: <https://github.com/javocsoft/MesenOrion>

> Mesen Orion is based on Mesen2 and continues its development. It is **not** affiliated with the original Mesen project or with MesenCE.

## What's new in Mesen Orion 3.0.0

Building on Mesen 2.1.1, this fork adds:

#### Rendering (Windows & Linux / OpenGL)
- **GLSL shader support** via an OpenGL rendering path on both **Windows and Linux**. Drop libretro-style shaders (`.glslp` multi-pass presets or single `.glsl` files) into a **`Shaders`** folder and select them from the UI. The folder is searched next to the binary, in the Mesen data folder, and — on Linux — in `/usr/share/mesen-orion/shaders` (so packaged installs can ship a system-wide shader collection).
- On Windows, a native **WGL/OpenGL renderer** is used in place of DirectX, enabling the same GLSL pipeline available on Linux.
- Switching between the OpenGL renderer and the **Software renderer** no longer requires a restart — the change applies immediately from the settings.
- A dedicated **Shaders** tab in *Options → Video* with shader selection, **favorites** management, and live, adjustable **shader parameters** (`#pragma parameter`) with a *Reset to defaults* option.
- A **Favorite Shader** entry in the main menu for quick switching.
- In-game shader switching:
  - `Page Down` / `Page Up` — next / previous shader
  - `Shift + Page Down` / `Shift + Page Up` — next / previous **favorite** shader
  - All bindable to a gamepad in *Preferences → Shortcut Keys*.

#### Picture presets
- Save/apply/rename/reorder named **picture presets** (brightness, contrast, hue, saturation, scanlines, bilinear) in the *Picture* tab.
- Cycle presets in-game with `Shift + P`.
- **Export/Import** of presets and shader favorites to share configurations.

#### Net Play
- The *Start Server* dialog shows your **local IP** and can fetch your **public IP** (on demand), each with a copy button, plus a port-forwarding hint.
- Password fields now mask input with an **eye toggle** to reveal, and a copy button.
- A **Test connection** button in the *Connect to server* dialog.

#### Recording & capture
- Built-in **GIF recorder** with `Shift + G` to toggle Record / Stop, plus *Tools → GIF Recorder* menu entries.
- An **always-visible on-screen indicator** while recording, and a notification showing where each GIF was saved.
- Automatic, timestamped filenames; the **output folder is configurable** (defaults to a dedicated `Gif` folder).

#### Interface & usability
- An optional, auto-hiding **status bar** (toggle from the menu or settings) showing the running **core**, emulation **speed**, **video size**, active **video filter**, current **shader**, **aspect ratio** and **vsync** state. It auto-hides together with the menu bar.
- **Right-click context menu** on the recent-games list to **launch** a game or **remove** it from the list.
- The online **auto-update** check has been disabled and the *Check for updates* entry hidden (this fork is distributed independently).

#### Quality, performance & accessibility
- Fixes and clean-ups in the SDL/Linux layer (video blit, audio ring-buffer thread-safety, deferred exclusive-fullscreen handling).
- Optional `NATIVE=true` build flag to optimize for the local CPU.
- Localized strings and accessibility names for the new controls.

## Releases

Pre-built releases for Windows (x64) are published on the [fork's releases page](https://github.com/javocsoft/MesenOrion/releases) as a ready-to-run ZIP that includes the emulator, all required libraries, and the full **Shaders** collection.

For Linux and macOS, build from source (see **Compiling** below).

## Compiling

See [COMPILING.md](COMPILING.md).

**Windows:** a PowerShell script handles the full workflow — build, publish, and ZIP packaging:
```powershell
.\build-windows.ps1 -Package        # Release + distributable ZIP
.\build-windows.ps1 -Configuration All  # Debug + Release
```
Run from a *Developer PowerShell for VS* prompt. See `COMPILING.md` for all options.

**Linux:** building with GCC is recommended:

```sh
USE_GCC=true make -j$(nproc)
# Optional: optimize for your CPU (non-portable binary)
USE_GCC=true NATIVE=true make -j$(nproc)
```

To build **and** produce a ready-to-install Debian package (`mesen-orion_<version>_amd64.deb`),
a helper script handles the whole workflow (clean, build, stage files, `dpkg-deb`):

```sh
./build-linux.sh                # make clean + build (GCC) + package .deb
./build-linux.sh --skip-build   # only repackage the existing build output
```
Run `./build-linux.sh --help` for all options.

Or build a portable **AppImage** (bundles the Shaders collection and AppStream metadata):

```sh
./Linux/appimage/appimage.sh          # x86_64  -> MesenOrion-x86_64.AppImage
./Linux/appimage/appimage-arm64.sh    # ARM64   -> MesenOrion-aarch64.AppImage
```

### Publishing a release

The AppImage is distributed as a GitHub Release asset. Using the `gh` CLI:

```sh
gh release create v3.0.0 \
  MesenOrion-x86_64.AppImage \
  mesen-orion_3.0.0_amd64.deb \
  --title "Mesen Orion 3.0.0" --notes "Release notes here"
```

To also list the AppImage on [AppImageHub](https://github.com/AppImage/appimage.github.io)
(optional, public catalog), open a pull request there pointing to the release download URL.
The AppImage already embeds the `.desktop` entry, icon and AppStream metainfo required by
their automated checks.

GLSL shaders require the OpenGL renderer. On Linux this is the default; on Windows it is used automatically (requires OpenGL 2.0). Set `MESEN_NO_GL=1` (Linux) or enable *Use Software Renderer* in settings (Windows) to fall back to the non-GL renderer.

## Credits

Mesen Orion is built on top of **Mesen / Mesen2 by Sour**. Huge thanks to Sour and to all the original Mesen contributors. See the *About* window for the full list of acknowledgements and third-party software.

## License

Mesen Orion is available under the GPL V3 license. Full text here: <http://www.gnu.org/licenses/gpl-3.0.en.html>

Copyright (C) 2014-2026 Sour
Copyright (C) 2026 JavocSoft and Mesen Orion contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
