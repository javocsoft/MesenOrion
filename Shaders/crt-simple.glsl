// Simple CRT scanline shader (libretro GLSL format)
// Single-pass. Works with Mesen's Linux/OpenGL renderer.

#pragma parameter SCANLINE_STRENGTH "Scanline Strength" 0.5 0.0 1.0 0.05
#pragma parameter BRIGHTNESS_BOOST "Brightness Boost" 1.1 1.0 2.0 0.05

#if defined(VERTEX)

attribute vec4 VertexCoord;
attribute vec2 TexCoord;
uniform mat4 MVPMatrix;
varying vec2 vTexCoord;

void main()
{
	gl_Position = MVPMatrix * VertexCoord;
	vTexCoord = TexCoord;
}

#elif defined(FRAGMENT)

uniform sampler2D Texture;
uniform vec2 InputSize;
varying vec2 vTexCoord;

uniform float SCANLINE_STRENGTH;
uniform float BRIGHTNESS_BOOST;

void main()
{
	vec3 color = texture2D(Texture, vTexCoord).rgb * BRIGHTNESS_BOOST;

	// One dark band per source scanline.
	float line = 0.5 + 0.5 * sin(vTexCoord.y * InputSize.y * 3.14159265);
	float scan = 1.0 - SCANLINE_STRENGTH * (1.0 - line);

	gl_FragColor = vec4(color * scan, 1.0);
}

#endif
