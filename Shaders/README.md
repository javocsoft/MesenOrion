# GLSL Shaders (Windows & Linux / OpenGL renderer)

Mesen Orion's Windows and Linux builds render through OpenGL and can apply **libretro-style GLSL
shader presets** on top of the emulated video output.

- **Windows** uses a native WGL/OpenGL renderer that supports the same shader pipeline.
- **Linux** uses an SDL/OpenGL renderer.

> On Windows, make sure *Use Software Renderer* is **disabled** in Options → Video to use the
> OpenGL path (required for shaders).

## Where to put shaders

Drop your shader files in Mesen's **Shaders** folder:

```
<Mesen data folder>/Shaders/
```

(the same place as `HdPacks`, `SaveStates`, etc. — it is created automatically on
first launch). The example files next to this README (`crt-simple.glsl` /
`crt-simple.glslp`) can be copied there as a starting point.

Both file types are listed in the UI:

- `*.glslp` — a multi-pass **preset** (recommended)
- `*.glsl`  — a single shader file (treated as a 1-pass, viewport-scaled preset)

> The scan is **non-recursive**: only files directly inside `Shaders/` are listed.
> Put a preset's helper `.glsl` files in a subfolder and reference them with a
> relative path (e.g. `shader0 = mypack/pass1.glsl`) to keep the list clean.

## Selecting a shader

- **From the UI:** Options → Video → **Shaders** tab. Pick a shader from the
  dropdown, mark shaders as **favorite** (and remove favorites) there. The tab
  also lists the current shortcut keys.
- **In-game (keyboard):**
  - `Page Down` / `Page Up` = next / previous shader (cycles through
    `None → shader1 → shader2 → … → None`).
  - `Shift + Page Down` / `Shift + Page Up` = next / previous **favorite** shader.
  - All configurable in Preferences → Shortcut Keys.
- **In-game (gamepad):** bind *Next/Previous Shader* and *Next/Previous Favorite
  Shader* to any controller buttons in Preferences → Shortcut Keys.

## Preset format (.glslp)

A practical subset of the libretro GLSL spec is supported:

```ini
shaders = 2

shader0 = pass1.glsl
filter_linear0 = false
scale_type0 = source
scale0 = 1.0

shader1 = pass2.glsl
filter_linear1 = true
scale_type1 = viewport
scale1 = 1.0
```

Supported keys (per pass `N`):
`shaderN`, `filter_linearN`, `scale_typeN` / `scale_type_xN` / `scale_type_yN`
(`source` | `viewport` | `absolute`), `scaleN` / `scale_xN` / `scale_yN`,
`wrap_modeN` (`clamp_to_edge` | `clamp_to_border` | `repeat` | `mirrored_repeat`),
`frame_count_modN`, `aliasN`. Preset-level `parameters = a;b;c` overrides with
`name = value` are applied too.

## Shader uniforms / attributes

Each pass is compiled twice with `VERTEX` and `FRAGMENT` defined (so use
`#if defined(VERTEX) … #elif defined(FRAGMENT) … #endif`). These standard
libretro variables are provided:

| Type      | Name                              |
|-----------|-----------------------------------|
| attribute | `VertexCoord`, `TexCoord`, `Color`|
| uniform   | `MVPMatrix`, `FrameCount`, `FrameDirection`, `OutputSize`, `TextureSize`, `InputSize`, `Texture` |

`#pragma parameter NAME "desc" initial min max step` declares a tweakable value;
its `initial` (or the preset override) is applied to the matching
`uniform float NAME;`.

## Current limitations

- No LUT `textures = …` entries.
- No history / previous-frame samplers (`PassPrev*`, `OrigTexture`, …).
- No float / sRGB intermediate framebuffers.
- The final pass always fills the screen viewport.

These cover most single- and multi-pass CRT / scanline / LCD shaders. The
`.slang` (Vulkan) format is **not** supported — convert to GLSL first.

## Falling back to the software renderer

If OpenGL context creation fails, you can disable the OpenGL renderer:

- **Windows:** enable *Use Software Renderer* in Options → Video. The change applies immediately without restarting.
- **Linux:** set the environment variable `MESEN_NO_GL=1` before launching Mesen to use the classic SDL renderer (no shader support).
