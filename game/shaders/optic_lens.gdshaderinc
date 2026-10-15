// Lens-local +X is the optical axis. The virtual reticle is collimated: it moves
// within the real opening when the eye moves, and cannot draw outside that mesh.
uniform vec3 glass_colour : source_color = vec3(0.18, 0.35, 0.38);
uniform float glass_alpha : hint_range(0.0, 0.2) = 0.025;
uniform vec3 reticle_colour : source_color = vec3(1.0, 0.12, 0.025);
uniform int reticle_style = 0; // 0 ring/dot, 1 crosshair
// Local magnified optics supply a linear HDR world image only while aiming. Holo/TPS remain glass.
uniform sampler2D optical_image : filter_linear, repeat_disable;
uniform bool optical_image_enabled = false;
uniform vec3 optical_centre = vec3(0.0);
uniform vec2 optical_size = vec2(1.0);
instance uniform float optic_exposure = 1.0;
varying vec3 lens_point;
varying vec3 lens_eye;
#ifdef VIEWMODEL
uniform float viewmodel_fov : hint_range(30.0, 110.0) = 60.0;
uniform float viewmodel_depth : hint_range(0.01, 1.0) = 0.12;
#endif

void vertex() {
    lens_point = VERTEX;
    lens_eye = (inverse(MODEL_MATRIX) * INV_VIEW_MATRIX[3]).xyz;
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
    vec3 ray = lens_point - lens_eye;
    vec2 angular = ray.yz / max(ray.x, 0.0001);
    float radius = length(angular);
    float aa = max(length(fwidth(angular)), 0.00012);
    float mark = 0.0;
    if (lens_eye.x < -0.001 && ray.x > 0.001) {
        if (reticle_style == 0) {
            float dot_coverage = 1.0 - smoothstep(0.0011, 0.0011 + aa, radius);
            float ring = 1.0 - smoothstep(0.00035, 0.00035 + aa, abs(radius - 0.007));
            mark = max(dot_coverage, ring);
        } else {
            float vertical = (1.0 - smoothstep(0.00025, 0.00025 + aa, abs(angular.x)))
                * (1.0 - smoothstep(0.015, 0.015 + aa, abs(angular.y)));
            float horizontal = (1.0 - smoothstep(0.00025, 0.00025 + aa, abs(angular.y)))
                * (1.0 - smoothstep(0.015, 0.015 + aa, abs(angular.x)));
            mark = max(vertical, horizontal);
        }
    }
    ALBEDO = mix(glass_colour, reticle_colour, mark) / max(optic_exposure, 0.001);
    ALPHA = max(glass_alpha, mark);
    if (optical_image_enabled) {
        vec3 point = lens_point - optical_centre;
        vec2 image_uv = vec2(point.z, -point.y) / optical_size + vec2(0.5);
        vec3 world = texture(optical_image, image_uv).rgb;
        ALBEDO = mix(world, reticle_colour / max(optic_exposure, 0.001), mark);
        ALPHA = 1.0;
    }
}
