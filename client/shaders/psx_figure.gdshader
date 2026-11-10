shader_type spatial;
// PlayStation-style figures (decoration only): vertices snap to a coarse screen grid (the wobble), textures are mapped
// affine (no perspective correction, the warp), colour is cut to few steps with an ordered dither. Lit as usual.

uniform sampler2D albedo_tex : source_color, filter_nearest;
uniform vec4 albedo_colour : source_color = vec4(1.0);
uniform float snap_columns = 220.0;   // grid columns across the screen; rows follow a 16:9 screen
uniform float colour_steps = 24.0;    // levels per colour channel
uniform float dither = 0.6;
uniform float grime_dark = 0.62;      // grimdark grade: brightness
uniform float grime_desat = 0.55;     // grimdark grade: how much colour drains toward grey
uniform vec3 grime_tint : source_color = vec3(1.0, 0.93, 0.82);   // dirty warm cast           // 0 off, 1 full ordered dither

varying vec3 affine;

void vertex() {
	vec4 p = PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	vec2 grid = vec2(snap_columns, snap_columns * 0.5625);
	p.xy = floor(p.xy / p.w * grid + 0.5) / grid * p.w;
	POSITION = p;
	affine = vec3(UV * p.w, p.w);   // interpolated, divided by w again in fragment(): screen-linear UV
}

float bayer4(vec2 f) {
	int x = int(mod(f.x, 4.0));
	int y = int(mod(f.y, 4.0));
	int i = x + y * 4;
	float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	return m[i] / 16.0 - 0.5;
}

void fragment() {
	vec4 tex = texture(albedo_tex, affine.xy / affine.z) * albedo_colour;
	float grey = dot(tex.rgb, vec3(0.3, 0.59, 0.11));
	vec3 graded = mix(tex.rgb, vec3(grey), grime_desat) * grime_tint * grime_dark;
	vec3 c = graded + bayer4(FRAGCOORD.xy) * dither / colour_steps;
	ALBEDO = floor(c * colour_steps + 0.5) / colour_steps;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
