// The effects library's particle step (GROUND.md §9.3). Every particle is emitted by FxBatch with all of its data
// (GPUParticles3D.EmitParticle); this shader only moves it and ends it.
//   TRANSFORM  position; the basis carries the size (billboards: uniform scale and nothing else; streaks: the base
//              length in X, the streak in Y; chunks: turn and size; rings: Y is the ring's normal)
//   VELOCITY   metres a second (world)
//   COLOR      tint and opacity; chunks keep the floor's height in A
//   CUSTOM     x lifetime (s), y age (s), z random 0..1, w the batch's own parameter (growth, width)
shader_type particles;
render_mode disable_velocity, disable_force;

uniform float gravity = 0.0;          // m/s² downwards; negative rises
uniform float drag = 0.0;             // share of the velocity lost a second
uniform float turbulence = 0.0;       // m/s² of slow wander
uniform float turbulence_scale = 2.0; // metres per noise cell
uniform bool collide = false;         // bounce off the floor kept in COLOR.a
uniform float bounce = 0.3;
uniform float friction = 0.5;
uniform int align = 0;                // 0 none, 1 streak along the velocity, 2 tumble (meshes)
uniform float stretch = 0.02;         // streak length per m/s
uniform float tumble = 10.0;          // radians a second at 10 m/s
uniform sampler2D noise_tex : repeat_enable, filter_linear;

mat3 turn(vec3 axis, float angle) {
	float s = sin(angle), c = cos(angle), t = 1.0 - c;
	return mat3(
		vec3(t * axis.x * axis.x + c, t * axis.x * axis.y + s * axis.z, t * axis.x * axis.z - s * axis.y),
		vec3(t * axis.x * axis.y - s * axis.z, t * axis.y * axis.y + c, t * axis.y * axis.z + s * axis.x),
		vec3(t * axis.x * axis.z + s * axis.y, t * axis.y * axis.z - s * axis.x, t * axis.z * axis.z + c));
}

void start() {
	CUSTOM.y = 0.0;
}

void process() {
	CUSTOM.y += DELTA;
	if (CUSTOM.y >= CUSTOM.x) {
		ACTIVE = false;
	}
	vec3 v = VELOCITY;
	vec3 p = TRANSFORM[3].xyz;
	v.y -= gravity * DELTA;
	if (turbulence > 0.0) {
		vec2 a = p.xz / turbulence_scale + vec2(CUSTOM.z * 5.3, TIME * 0.11);
		vec2 b = p.zy / turbulence_scale + vec2(TIME * 0.09, CUSTOM.z * 3.1);
		vec3 n = vec3(texture(noise_tex, a).r, texture(noise_tex, b).g, texture(noise_tex, a.yx + 0.37).b) - 0.5;
		v += n * (turbulence * 2.0 * DELTA);
	}
	v /= 1.0 + drag * DELTA;
	p += v * DELTA;
	if (collide && p.y < COLOR.a) {
		p.y = COLOR.a;
		if (v.y < 0.0) {
			v.y = -v.y * bounce;
			v.xz *= friction;
			if (v.y < 0.6) {
				v.y = 0.0;
				v.xz *= 0.7;
			}
		}
	}
	TRANSFORM[3].xyz = p;
	VELOCITY = v;
	if (align == 1) {
		float base = length(TRANSFORM[0].xyz);
		float speed = length(v);
		vec3 dir = speed > 0.001 ? v / speed : normalize(TRANSFORM[1].xyz + vec3(0.0, 1e-5, 0.0));
		TRANSFORM[1].xyz = dir * (base + speed * stretch);
	} else if (align == 2) {
		float speed = length(v);
		if (speed > 0.05) {
			vec3 axis = normalize(vec3(sin(CUSTOM.z * 31.0), cos(CUSTOM.z * 17.0), sin(CUSTOM.z * 53.0) + 0.3));
			mat3 r = turn(axis, tumble * DELTA * min(speed, 14.0) * 0.1 * (0.5 + CUSTOM.z));
			TRANSFORM[0].xyz = r * TRANSFORM[0].xyz;
			TRANSFORM[1].xyz = r * TRANSFORM[1].xyz;
			TRANSFORM[2].xyz = r * TRANSFORM[2].xyz;
		}
	}
}
