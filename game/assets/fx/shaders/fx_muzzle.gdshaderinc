// The muzzle flash (GROUND.md §9.3): a few cards on the muzzle socket (+X along the barrel), drawn additively from the
// flash atlas: cards along the barrel show the flame from the side (UV2.x = 0, the atlas's top row), the card across it
// the star seen down the barrel (UV2.x = 1, the bottom row). A card seen edge-on fades out, so no flat edge ever shows.
// Instance parameters pick the variant and fade it over its two or three frames. The first-person variant draws with
// the view model's field of view, like the arms and the gun (character_common.gdshaderinc).

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform float brightness = 6.0;
#ifdef VIEWMODEL
uniform float viewmodel_fov : hint_range(30.0, 110.0) = 60.0;
uniform float viewmodel_depth : hint_range(0.01, 1.0) = 0.12;
#endif
instance uniform float fx_variant = 0.0;
instance uniform float fx_intensity = 1.0;

varying vec3 v_normal;
varying float v_kind;

void vertex() {
	v_kind = UV2.x;
	UV = (UV + vec2(floor(fx_variant), v_kind)) / vec2(4.0, 2.0);
	v_normal = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
#ifdef VIEWMODEL
	mat4 p = PROJECTION_MATRIX;
	float f = 1.0 / tan(radians(viewmodel_fov) * 0.5);
	p[0][0] = f * (p[0][0] / abs(p[1][1]));
	p[1][1] = f * sign(p[1][1]);
	POSITION = p * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	POSITION.z = mix(POSITION.w, POSITION.z, viewmodel_depth);
#endif
}

void fragment() {
	vec4 t = texture(atlas, UV);
	float facing = abs(dot(normalize(v_normal), normalize(-VERTEX)));
	ALBEDO = t.rgb * brightness * fx_intensity;
	ALPHA = clamp(t.a * smoothstep(0.08, 0.55, facing), 0.0, 1.0);
}
