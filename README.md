# Mesen Orion

**Mesen Orion** is a multi-system emulator (NES, SNES, Game Boy, Game Boy Advance, PC Engine, SMS/Game Gear, WonderSwan) for Windows, Linux and macOS.

It is a fork of [Mesen2](https://github.com/SourMesen/Mesen2) by Sour (whose 2.x line is no longer maintained), continuing development with a renewed UI and rendering-pipeline improvements. The first release of this fork is **v3.0.0**.

Repository: <https://github.com/javocsoft/MesenOrion>

> Mesen Orion is based on Mesen2 and continues its development. It is **not** affiliated with the original Mesen project or with MesenCE.

## What's new in Mesen Orion 3.0.0

Building on Mesen 2.1.1, this fork adds:

#### Rendering (Linux / OpenGL)
- **GLSL shader support** via an OpenGL rendering path. Drop libretro-style shaders (`.glslp` multi-pass presets or single `.glsl` files) into a **`Shaders`** folder (next to the binary or in the Mesen data folder) and select them from the UI.
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

#### Quality, performance & accessibility
- Fixes and clean-ups in the SDL/Linux layer (video blit, audio ring-buffer thread-safety, deferred exclusive-fullscreen handling).
- Optional `NATIVE=true` build flag to optimize for the local CPU.
- Localized strings and accessibility names for the new controls.

## Releases

Build from source for now (see **Compiling** below). Pre-built releases for this fork, when available, will be published on the [fork's releases page](https://github.com/javocsoft/MesenOrion/releases).

## Compiling

See [COMPILING.md](COMPILING.md).

On Linux, building with GCC is recommended:

```sh
USE_GCC=true make -j$(nproc)
# Optional: optimize for your CPU (non-portable binary)
USE_GCC=true NATIVE=true make -j$(nproc)
```

GLSL shaders require the OpenGL renderer (default on Linux). Set `MESEN_NO_GL=1` to fall back to the classic SDL renderer if needed.

## Credits

Mesen Orion is built on top of **Mesen / Mesen2 by Sour**. Huge thanks to Sour and to all the original Mesen contributors. See the *About* window for the full list of acknowledgements and third-party software.

## License

Mesen Orion is available under the GPL V3 license. Full text here: <http://www.gnu.org/licenses/gpl-3.0.en.html>

Copyright (C) 2014-2025 Sour
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
