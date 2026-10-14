// Shared by the character shaders (GROUND.md §4.8).
//
// Body regions: every vertex carries its region in UV2.x (index + 0.5). Bit i of hidden_regions hides region i: the
// vertex collapses to the origin, so its triangles have no area and draw nothing (no discard, no overdraw).
//
// First person (the *_viewmodel shaders define VIEWMODEL): drawn with their own vertical field of view (degrees) and
// with depth pulled towards the near plane, so the arms and the held item never clip into walls. Lighting is the
// same. (Writing POSITION at all makes Godot use it for every vertex, so only those variants do.)

instance uniform int hidden_regions = 0;
#ifdef VIEWMODEL
uniform float viewmodel_fov : hint_range(30.0, 110.0) = 60.0;
uniform float viewmodel_depth : hint_range(0.01, 1.0) = 0.12;
#endif

void vertex() {
	if (((hidden_regions >> int(UV2.x)) & 1) != 0) {
		VERTEX = vec3(0.0);
	}
#ifdef VIEWMODEL
	mat4 p = PROJECTION_MATRIX;
	float f = 1.0 / tan(radians(viewmodel_fov) * 0.5);
	p[0][0] = f * (p[0][0] / abs(p[1][1]));
	p[1][1] = f * sign(p[1][1]);
	POSITION = p * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0));
	POSITION.z = mix(POSITION.w, POSITION.z, viewmodel_depth);
#endif
}
