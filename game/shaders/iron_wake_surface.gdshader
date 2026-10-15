// The Iron Wake's finishes (docs/IRON_WAKE.md §9.2 "As built", written by art/import/iron_wake.py): one surface for
// every paint, steel, deck, machined, rubber and fabric finish, lit by Godot's own lighting (no light function).
// - Textures tile on the model's UVs times uv_scale. The albedo's alpha is the paint layer on painted finishes: the
//   tint colours it, and where the source shows chips and rust (alpha 0) their own colour stays.
// - Vertex colour (linear, as the GLB has it): R the contact occlusion baked by rays, G how convex the surface is
//   round the vertex, B how concave. A vertex's value spreads over its faces, so edges are found where the normal
//   turns fast across the surface (the bevels and rims): the curvature from the interpolated normal's change per
//   metre, gated by G. A big flat panel between convex corners does not wear, nor does a pipe. The asset's parts are
//   separate solids, so where they meet reads convex to G; cavities and junctions come from the occlusion (R).
// - Edges chip: paint to bare steel, steel to bright worn steel. Cavities and contact shadow gather dirt and rust.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D steel_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rust_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float uv_scale = 1.0;
uniform float normal_strength = 1.0;
uniform float painted = 0.0;        // 1: the albedo's alpha is a paint layer the tint colours
uniform float chip = 0.0;           // how readily edges wear through
uniform float rust = 0.0;           // rust in cavities and in some chips
uniform float grime = 0.4;          // dirt in the contact shadow and cavities
uniform float contact = 0.42;       // the baked contact occlusion's strength (lerp(1, occlusion, contact))
uniform float curvature_low = 15.0;  // 1/m: below this the surface counts as flat (radius over 6.7 cm: pipes, drums)
uniform float curvature_high = 60.0; // 1/m: a bevel tighter than 1.7 cm counts fully as an edge
uniform vec3 grime_colour : source_color = vec3(0.075, 0.068, 0.058);
uniform vec3 bare_steel : source_color = vec3(0.56, 0.57, 0.58);

void fragment() {
	vec2 uv = UV * uv_scale;
	vec4 base = texture(albedo_tex, uv);
	vec3 orm = texture(orm_tex, uv).rgb;
	float occlusion = COLOR.r;
	float convex = COLOR.g;
	float concave = COLOR.b;
	float n1 = texture(noise_tex, uv * 0.73 + vec2(0.31, 0.17)).r;
	float n2 = texture(noise_tex, uv * 2.9 + vec2(0.62, 0.45)).r;

	float paint = painted * base.a;
	vec3 colour = mix(base.rgb, base.rgb * tint.rgb, painted > 0.5 ? base.a : 1.0);
	float metallic = orm.b;
	float roughness = orm.g;

	// Curvature from how fast the interpolated normal turns per metre of surface.
	float dn = length(fwidth(NORMAL));
	float dp = max(length(fwidth(VERTEX)), 1e-5);
	float bend = smoothstep(curvature_low, curvature_high, dn / dp);
	float edge = bend * smoothstep(0.08, 0.45, convex);
	// Worn in patches along an edge, never all of it.
	float patches = smoothstep(0.45, 0.7, n1 * 0.65 + n2 * 0.35);
	float wear = smoothstep(0.25, 0.55, edge * chip * patches * 1.6);

	// Paint chips to bare steel; bare and dark steel wear bright and smooth.
	vec3 steel = texture(steel_tex, uv * 1.37).rgb;
	vec3 bright = steel * bare_steel / max(dot(steel, vec3(0.3333)), 0.02) * 0.62;
	float through = wear * mix(1.0, paint, painted);
	colour = mix(colour, bright, through);
	metallic = mix(metallic, 0.95, through);
	roughness = mix(roughness, 0.32, through);
	float polish = wear * (1.0 - painted);
	colour *= 1.0 + polish * 0.35;
	roughness = mix(roughness, roughness * 0.7, polish);

	// Cavities and the contact shadow: dirt, and rust where water would stand.
	float hidden = max(concave * (1.0 - bend * 0.5), (1.0 - occlusion));
	float r = smoothstep(0.5, 0.82, hidden * rust * 1.5 + (n2 - 0.5) * 0.6 + (n1 - 0.5) * 0.3);
	r = max(r, through * rust * smoothstep(0.62, 0.9, n2));
	vec3 rusty = texture(rust_tex, uv * 1.9).rgb;
	colour = mix(colour, rusty, r * 0.85);
	metallic = mix(metallic, 0.0, r);
	roughness = mix(roughness, 0.86, r);
	float dirt = smoothstep(0.2, 0.75, hidden) * grime;
	colour = mix(colour, grime_colour, dirt * 0.45);
	roughness = mix(roughness, 0.8, dirt * 0.4);

	ALBEDO = colour * mix(1.0, occlusion, contact);
	METALLIC = clamp(metallic, 0.0, 1.0);
	ROUGHNESS = clamp(roughness, 0.04, 1.0);
	AO = orm.r;
	NORMAL_MAP = texture(normal_tex, uv).rgb;
	NORMAL_MAP_DEPTH = normal_strength;
}
