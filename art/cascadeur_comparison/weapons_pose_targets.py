"""Pure geometry for six isolated Cascadeur FPS weapon candidates.

No app, Blender, NumPy, or file I/O.  Input source transforms are Blender world
metres; output native point targets are Y-up centimetres.  A weapon and both
hands move as one rigid assembly while gripping.  Reloads detach the left hand
and animate a real magazine/slide/bolt; grasp poses must be supplied by the
asset preparation step.  The native author, not this module, applies IK and
interpolation and validates every resulting frame.

build_weapon_targets(weapon_manifest, native_reference, clip) returns:
  keyframes[frame][point_name] = world XYZ centimetres
  prop_keyframes[frame][bone_name] = native local_position/local_rotation
  finger_keyframes[frame][bone_name] = native local_rotation, when supplied
  end_frame, fps, metadata (phase, grip reference, reach and wrist measurements)

Manifest contract is deliberately explicit.  ``ready`` has bone_positions_m
and bone_rotations.  ``weapon_frame`` has position_m and rotation mapping GLB
(+X forward,+Y up,+Z side) to Blender world.  ``parts`` declares a parent and
pivot_weapon_m for each part bone. ``grasps.magazine`` and ``grasps.rack`` have
hand_position_m, hand_rotation, optional finger_local_rotations, and part_bone.
Those grasp frames are measured with every weapon part in its READY position.
"""
from __future__ import annotations

import math


C = [[1., 0., 0.], [0., 0., 1.], [0., -1., 0.]]
IDENTITY = [[1., 0., 0.], [0., 1., 0.], [0., 0., 1.]]
RIGHT, UP, FORWARD = [-1., 0., 0.], [0., 1., 0.], [0., 0., 1.]
CLIP_WEAPON = {
    'heavyaxe_chop': 'heavyaxe', 'pickaxe_swing': 'pickaxe',
    'pistol_fire': 'pistol', 'pistol_reload': 'pistol',
    'rifle_fire': 'rifle', 'rifle_reload': 'rifle',
}


def add(a, b):
    return [x+y for x, y in zip(a, b)]


def sub(a, b):
    return [x-y for x, y in zip(a, b)]


def mul(a, amount):
    return [x*amount for x in a]


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def length(a):
    return math.sqrt(dot(a, a))


def unit(a):
    size = length(a)
    if size < 1e-9:
        raise ValueError('Degenerate direction')
    return mul(a, 1./size)


def cross(a, b):
    return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]


def trans(a):
    return [list(row) for row in zip(*a)]


def mv(a, b):
    return [dot(row, b) for row in a]


def mm(a, b):
    return [[dot(row, column) for column in zip(*b)] for row in a]


def lerp(a, b, t):
    return add(mul(a, 1.-t), mul(b, t))


def degrees_between(a, b):
    return math.degrees(math.acos(max(-1., min(1., dot(unit(a), unit(b))))))


def axis_rotation(axis, degrees):
    x, y, z = unit(axis)
    a = math.radians(degrees)
    c, s, t = math.cos(a), math.sin(a), 1.-math.cos(a)
    return [[t*x*x+c, t*x*y-s*z, t*x*z+s*y],
            [t*x*y+s*z, t*y*y+c, t*y*z-s*x],
            [t*x*z-s*y, t*y*z+s*x, t*z*z+c]]


def _quaternion(matrix):
    m = matrix
    trace = m[0][0]+m[1][1]+m[2][2]
    if trace > 0:
        s = math.sqrt(trace+1.)*2.
        return [s/4., (m[2][1]-m[1][2])/s, (m[0][2]-m[2][0])/s, (m[1][0]-m[0][1])/s]
    i = max(range(3), key=lambda n: m[n][n])
    j, k = (i+1) % 3, (i+2) % 3
    s = math.sqrt(max(0., 1.+m[i][i]-m[j][j]-m[k][k]))*2.
    q = [(m[k][j]-m[j][k])/s, 0., 0., 0.]
    q[i+1], q[j+1], q[k+1] = s/4., (m[j][i]+m[i][j])/s, (m[k][i]+m[i][k])/s
    return q


def _matrix(q):
    w, x, y, z = unit(q)
    return [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
            [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
            [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]


def blend_rotation(a, b, t):
    qa, qb = _quaternion(a), _quaternion(b)
    if dot(qa, qb) < 0:
        qb = mul(qb, -1.)
    cosine = max(-1., min(1., dot(qa, qb)))
    if cosine > .9995:
        return _matrix(lerp(qa, qb, t))
    theta = math.acos(cosine)
    return _matrix(add(mul(qa, math.sin((1-t)*theta)/math.sin(theta)),
                       mul(qb, math.sin(t*theta)/math.sin(theta))))


def _world_delta(pose):
    # XYZ are screen right/up/forward in centimetres. Pitch raises the muzzle;
    # yaw points it right; positive roll is clockwise as seen by the player.
    x, y, z, pitch, yaw, roll = pose
    translation = add(add(mul(RIGHT, x), mul(UP, y)), mul(FORWARD, z))
    rotation = mm(axis_rotation(FORWARD, -roll),
                  mm(axis_rotation(UP, -yaw), axis_rotation(RIGHT, pitch)))
    return translation, rotation


def _event(frame, phase, pose=(0, 0, 0, 0, 0, 0), left='support',
           blend=0., magazine=(0, 0, 0, 0), rack=0.):
    # Magazine displacement is GLB-local cm, plus a rotation about GLB +Z.
    return dict(frame=frame, phase=phase, pose=list(pose), left=left,
                blend=blend, magazine=list(magazine), rack=rack)


def choreography(clip):
    """Intentional sparse poses; interpolation remains native Cascadeur IK."""
    E = _event
    if clip == 'heavyaxe_chop':
        return [E(0, 'ready'), E(6, 'weight preload', (1, -2, -2, -5, 0, 2)),
                E(12, 'lift', (2, 13, 0, 17, -3, -2)),
                E(18, 'high shoulder windup', (3, 29, 0, 37, -5, -4)),
                E(21, 'weight starts falling', (1.8, 22.4, 3.6, 26.8, -2, -1.6)),
                E(23, 'accelerate', (1, 18, 6, 20, 0, 0)),
                E(26, 'descending strike', (-2, -4, 4, -25, 0, 0)),
                E(28, 'contact', (-3, -8, 0, -35, 0, 0)),
                E(31, 'weight follow through', (-3, -12, 0, -45, 0, 0)),
                E(35, 'check momentum', (-2, -7, -2, -32, 0, 0)),
                E(41, 'recover', (-1, -1, 1, -5, 0, 0)),
                E(50, 'return to ready'), E(60, 'settled')]
    if clip == 'pickaxe_swing':
        return [E(0, 'ready'), E(6, 'brace', (1, -1, -3, -3, -2, 0)),
                E(12, 'raise point', (2, 14, -3, 17, -4, -3)),
                E(17, 'high rear cock', (3, 29, -4, 32, -5, -4)),
                E(21, 'drive point', (0, 15, 6, 8, -2, -2)),
                E(24, 'point contact', (-2, -6, 0, -35, 0, 0)),
                E(26, 'penetration', (-2, -12, 0, -45, 0, 0)),
                E(30, 'lodged beat', (-2, -12, -1, -45, 0, 0)),
                E(34, 'extract', (0, -3, -4, -20, -2, 0)),
                E(37, 'point clears and wrist rises', (.5, 4, -3, -5, -2.5, -1)),
                E(40, 'clear surface', (1, 9, -3, 8, -3, -2)),
                E(46, 'ready'), E(54, 'settled')]
    if clip == 'pistol_fire':
        return [E(0, 'aim'), E(2, 'trigger break'),
                E(3, 'recoil and slide back', (.2, 1.1, -2.2, 4.8, .4, .5), rack=-2.4),
                E(5, 'slide forward, wrist lag', (.1, .8, -1.3, 3.1, .2, .2)),
                E(8, 'return', (0, -.15, .2, -.6, 0, 0)),
                E(12, 'settle', (0, .05, -.05, .15, 0, 0)), E(15, 'aim')]
    if clip == 'rifle_fire':
        return [E(0, 'aim'), E(2, 'trigger break'),
                E(3, 'stock recoil and bolt back', (.1, .4, -1.8, 1.5, .3, .25), rack=-4.),
                E(5, 'bolt forward', (.05, .2, -.8, .8, .1, .1)),
                E(7, 'return', (0, -.08, .15, -.25, 0, 0)),
                E(10, 'settle', (0, .03, -.05, .1, 0, 0)), E(12, 'aim')]
    if clip == 'pistol_reload':
        cant = (0, 12, 8, 20, -20, 0)
        return [E(0, 'ready'), E(5, 'raise magwell into view', (0, 6, 4, 10, -10, 0)),
                E(10, 'support leaves grip', cant, 'to_magazine', .55),
                E(14, 'grasp magazine', cant, 'magazine'),
                E(16, 'establish magazine grip', cant, 'magazine'),
                E(18, 'release and withdraw', cant, 'magazine', magazine=(0, -4, 0, 0)),
                E(23, 'lower magazine', cant, 'magazine', magazine=(-4, -12, -4, -20)),
                E(25, 'turn magazine below view', cant, 'magazine', magazine=(-8, -19, -5.5, -40)),
                E(28, 'magazine out of view', cant, 'magazine', magazine=(-14, -26, -7, -60)),
                E(33, 'exchange below view', cant, 'magazine', magazine=(-14, -26, -7, -60)),
                E(38, 'new magazine rises', cant, 'magazine', magazine=(-12, -22, -6, -45)),
                E(43, 'align to well', cant, 'magazine', magazine=(-3, -8, -3, -10)),
                E(48, 'insert', cant, 'magazine', magazine=(0, -1.2, 0, 0)),
                E(50, 'magazine seats', cant, 'magazine'),
                E(51, 'seat magazine', cant, 'magazine'),
                E(54, 'clear outside receiver', cant, 'to_rack', .45),
                E(56, 'approach rear serrations', cant, 'to_rack', .85),
                E(57, 'grasp slide', cant, 'rack'),
                E(60, 'pull slide', cant, 'rack', rack=-2.4),
                E(61, 'let slide go', cant, 'from_rack', .05, rack=-2.4),
                E(64, 'release slide', cant, 'from_rack', .25),
                E(67, 'hand clears outside receiver', cant, 'from_rack', .5),
                E(72, 'support returns', (0, 6, 4, 10, -10, 0), 'from_rack', .85),
                E(78, 'ready')]
    if clip == 'rifle_reload':
        cant = (0, 12, 0, 3, 0, 0)
        return [E(0, 'ready'), E(6, 'raise magwell into view', (0, 6, 0, 1.5, 0, 0)),
                E(12, 'support leaves foregrip', cant, 'to_magazine', .6),
                E(16, 'grasp magazine', cant, 'magazine'),
                E(18, 'establish magazine grip', cant, 'magazine'),
                E(20, 'release and rock forward', cant, 'magazine', magazine=(.5, -2, 0, 6)),
                E(25, 'withdraw magazine', cant, 'magazine', magazine=(0, -7, -3, 5)),
                E(31, 'magazine out of view', cant, 'magazine', magazine=(-10, -25, -6, -35)),
                E(36, 'exchange below view', cant, 'magazine', magazine=(-10, -25, -6, -35)),
                E(42, 'bring up new magazine', cant, 'magazine', magazine=(-8, -21, -5, -20)),
                E(48, 'align front lug', cant, 'magazine', magazine=(-2, -9, -2, 6)),
                E(54, 'front lug catches', cant, 'magazine', magazine=(.5, -3, 0, 6)),
                E(58, 'rock magazine in', cant, 'magazine', magazine=(0, -.8, 0, 3)),
                E(61, 'seated', cant, 'magazine'),
                E(63, 'seating check', cant, 'magazine'),
                E(67, 'clear outside magazine well', cant, 'from_magazine', .25),
                E(71, 'return around receiver', cant, 'from_magazine', .55),
                E(76, 'support returns', (0, 6, 0, 1.5, 0, 0), 'from_magazine', .85),
                E(82, 'ready')]
    raise ValueError('Unsupported clip: '+clip)


def _sample_ready(manifest):
    ready = manifest['ready']
    return ({name: mul(mv(C, p), 100.) for name, p in ready['bone_positions_m'].items()},
            {name: mm(C, r) for name, r in ready['bone_rotations'].items()})


def _bounded_shoulder(start, wrist, shift, upper, lower, clavicle=None):
    # Small clavicle movement is allowed, not arbitrary limb stretching.
    shifted = add(start, mul(shift, .20))
    displacement = sub(shifted, start)
    if length(displacement) > 4.:
        shifted = add(start, mul(unit(displacement), 4.))
    def on_clavicle_sphere(point):
        if clavicle is None:
            return point
        return add(clavicle, mul(unit(sub(point, clavicle)), length(sub(start, clavicle))))

    shifted = on_clavicle_sphere(shifted)
    target = sub(wrist, shifted)
    maximum = (upper+lower)*.975
    for unused in range(24):
        if length(target) <= maximum+1e-5:
            break
        shifted = on_clavicle_sphere(add(shifted, mul(unit(target), length(target)-maximum)))
        target = sub(wrist, shifted)
    if length(sub(shifted, start)) > 7.:
        raise ValueError('Pose requires more than 7 cm of shoulder displacement')
    return shifted


def _frame(forearm, hinge):
    f, h = unit(forearm), unit(hinge)
    return trans([f, h, cross(f, h)])


def _planned_wrist_twist(shoulder, elbow, wrist, hand_rotation, calibration):
    forearm = sub(wrist, elbow)
    hinge = unit(cross(sub(elbow, shoulder), forearm))
    basis = _frame(forearm, hinge)
    lower_rotation = mm(basis, calibration['forearm_frame_to_bone'])
    local = mm(trans(lower_rotation), hand_rotation)
    delta = mm(local, trans(calibration['ready_hand_local']))
    q = _quaternion(delta)
    axis = calibration['forearm_axis_local']
    return 2.*math.degrees(math.atan2(abs(dot(q[1:], axis)), abs(q[0])))


def _elbow_for_hand(shoulder, wrist, hand_long, pole, upper, lower,
                    hand_rotation=None, calibration=None, bend_limit=24.):
    """Keep the elbow down/outside; use the available natural wrist tolerance.

    Minimizing wrist bend alone often raises the elbow across the FPS camera.
    Choose the nearest pole solution inside the wrist limit instead.  Sampling
    only the exact two-bone elbow circle never stretches either arm segment.
    """
    d = sub(wrist, shoulder)
    reach = length(d)
    if not abs(upper-lower)+.05 < reach < upper+lower-.05:
        raise ValueError('Unreachable hand target')
    axis = unit(d)
    along = (upper*upper-lower*lower+reach*reach)/(2.*reach)
    center = add(shoulder, mul(axis, along))
    radius = math.sqrt(max(0., upper*upper-along*along))
    neutral = sub(wrist, mul(unit(hand_long), lower))
    wanted = sub(neutral, center)
    wanted = sub(wanted, mul(axis, dot(wanted, axis)))
    preferred = sub(pole, center)
    preferred = sub(preferred, mul(axis, dot(preferred, axis)))
    normal = unit(wanted if length(wanted) > 1e-6 else preferred)
    tangent = cross(axis, normal)
    preferred = unit(preferred if length(preferred) > 1e-6 else normal)
    preferred_elbow = add(center, mul(preferred, radius))

    def penalty(candidate):
        bend = degrees_between(sub(wrist, candidate), hand_long)
        limits = _elbow_limits(shoulder, wrist, candidate)
        violation = sum(value*value for value in limits.values())
        twist_penalty = 0.
        if calibration is not None:
            twist = _planned_wrist_twist(shoulder, candidate, wrist, hand_rotation, calibration)
            twist_penalty = max(0., twist-35.)**2
        return 1000.*(max(0., bend-bend_limit)**2+violation+twist_penalty)+length(sub(candidate, pole))**2

    # Exact preferred direction is included so a good ready elbow is retained.
    candidates = [preferred_elbow]
    for index in range(144):
        a = index*2.*math.pi/144.
        direction = add(mul(normal, math.cos(a)), mul(tangent, math.sin(a)))
        candidates.append(add(center, mul(direction, radius)))
    return min(candidates, key=penalty)


def _elbow_limits(shoulder, wrist, elbow):
    side = -1. if shoulder[0] < 0. else 1.
    # The preserved camera is at(0,168,4)cm, looking slightly down. A low elbow
    # near the body may safely cross the screen centre below the visible frame;
    # a high close elbow there fills the view with the upper arm. Penalize the
    # actual central camera region rather than an arbitrary world-X strip.
    delta = sub(elbow, [0., 168., 4.])
    depth = dot(delta, [0., -.205517143, .978653491])
    vertical = dot(delta, [0., .978653491, .205517143])
    intrusion = 0.
    if 0. < depth < 35. and vertical > -.45*depth and abs(delta[0]) < .65*depth:
        intrusion = min(.65*depth-abs(delta[0]), vertical+.45*depth, 35.-depth)
    return {
        'camera_intrusion_cm': intrusion,
        'wrong_side_cm': max(0., -2.-side*elbow[0]),
        'ahead_of_wrist_cm': max(0., elbow[2]-(wrist[2]-3.)),
        'above_shoulder_and_hand_cm': max(0., elbow[1]-(max(shoulder[1], wrist[1])-1.)),
    }


def _calibration(native_reference, positions, rotations, side):
    joints, points = native_reference['joints']['0'], native_reference['points']['0']
    names = {part: part+'_'+side for part in ('upperarm', 'lowerarm', 'hand', 'middle_01')}
    S, E, W = (joints[names[part]]['global_position'] for part in ('upperarm', 'lowerarm', 'hand'))
    hinge_offset = sub(points['lowerarm_AdditionalPoint_'+side], E)
    hinge_axis = unit(cross(sub(E, S), sub(W, E)))
    sign = 1. if dot(hinge_offset, hinge_axis) >= 0 else -1.
    radius = length(hinge_offset)
    error = length(sub(hinge_offset, mul(hinge_axis, radius*sign)))
    if error > .02:
        raise ValueError(f'{side} native elbow shared hinge mismatch: {error:.5f} cm')
    hand = names['hand']
    # Store hand controls in the native hand's local frame, then restore the
    # source basis when producing targets. This undoes control-generation drift.
    inverse = trans(joints[hand]['global_rotation'])
    offsets = {part: mv(inverse, sub(points['hand_'+part+'_'+side], W))
               for part in ('DirectionPoint', 'AdditionalPoint')}
    source_s, source_e, source_w = (positions[names[part]] for part in ('upperarm', 'lowerarm', 'hand'))
    source_forearm = sub(source_w, source_e)
    source_hinge = unit(cross(sub(source_e, source_s), source_forearm))
    source_basis = _frame(source_forearm, source_hinge)
    lower_rotation = rotations[names['lowerarm']]
    return dict(upper=length(sub(positions[names['lowerarm']], positions[names['upperarm']])),
                lower=length(sub(positions[hand], positions[names['lowerarm']])),
                hinge_sign=sign, hinge_radius=radius, hinge_reference_error_cm=error,
                point_offsets=offsets,
                forearm_frame_to_bone=mm(trans(source_basis), lower_rotation),
                ready_hand_local=mm(trans(lower_rotation), rotations[hand]),
                forearm_axis_local=unit(mv(trans(lower_rotation), source_forearm)),
                metacarpal_local=mv(trans(rotations[hand]), sub(positions[names['middle_01']], positions[hand])))


def _part_motion(event, bone):
    if bone == 'prop_magazine':
        x, y, z, degrees = event['magazine']
        return [x, y, z], axis_rotation([0., 0., 1.], degrees)
    if bone in ('prop_slide', 'prop_bolt'):
        return [event['rack'], 0., 0.], IDENTITY
    return [0., 0., 0.], IDENTITY


def _native_local_scale(reference, positions, bone, parent):
    local = reference['joints']['0'][bone]['local_position']
    world_length = length(sub(positions[bone], positions[parent]))
    if length(local) < 1e-7:
        # FBX inputs are metres with uniform skeleton scale100; prepare-step
        # may explicitly record another value for coincident part origins.
        return 100.
    result = world_length/length(local)
    if not math.isfinite(result) or result < .001:
        raise ValueError('Invalid native parent scale for '+bone)
    return result


def _grasp_pose(manifest, name):
    try:
        grasp = manifest['grasps'][name]
    except KeyError:
        raise ValueError(f'Reload requires measured grasps.{name}; a contact point is not a hand pose') from None
    return (mul(mv(C, grasp['hand_position_m']), 100.), mm(C, grasp['hand_rotation']), grasp)


def build_weapon_targets(manifest, native_reference, clip):
    """Build sparse rigid grips and FK moving-part plans; never mutate inputs."""
    if clip not in CLIP_WEAPON:
        raise ValueError('Unsupported weapon clip: '+clip)
    if manifest['weapon_id'] != CLIP_WEAPON[clip]:
        raise ValueError('Clip and weapon manifest mismatch')
    positions, rotations = _sample_ready(manifest)
    wp = mul(mv(C, manifest['weapon_frame']['position_m']), 100.)
    wr = mm(C, manifest['weapon_frame']['rotation'])
    wi = trans(wr)
    calibration = {side: _calibration(native_reference, positions, rotations, side) for side in ('r', 'l')}
    events = choreography(clip)
    if clip.endswith('_reload'):
        # FK part curves and IK hand curves have different native tangents.
        # Keep the authored magazine motion/contact coherent with sparse
        # 2-frame breakdowns during its held travel, rather than independent
        # unconstrained IK/FK cubic spans of five or six frames.
        expanded = []
        for a, b in zip(events, events[1:]):
            expanded.append(a)
            if a['left'] == b['left'] == 'magazine':
                for frame in range(a['frame']+2, b['frame'], 2):
                    t = (frame-a['frame'])/(b['frame']-a['frame'])
                    expanded.append(_event(frame, a['phase']+' contact breakdown',
                                           lerp(a['pose'], b['pose'], t), 'magazine',
                                           magazine=lerp(a['magazine'], b['magazine'], t)))
        expanded.append(events[-1])
        events = expanded
    reload = clip.endswith('_reload')
    grasps = {name: _grasp_pose(manifest, name) for name in ('magazine', 'rack')} if reload else {}
    parts = manifest.get('parts', {})
    tip_name = 'impact' if 'impact' in manifest.get('sockets_weapon_m', {}) else 'muzzle'
    tip_local = mul(manifest.get('sockets_weapon_m', {}).get(tip_name, [0., 0., 0.]), 100.)
    if clip.startswith(('pistol_', 'rifle_')):
        expected = 'prop_slide' if clip.startswith('pistol_') else 'prop_bolt'
        if not {'prop_magazine', expected}.issubset(parts):
            raise ValueError('Gun input is missing genuine magazine or moving action parts')
    ready_local = native_reference['joints']['0']
    out = dict(keyframes={}, prop_keyframes={}, finger_keyframes={}, end_frame=events[-1]['frame'], fps=30,
               metadata={'clip': clip, 'weapon_id': manifest['weapon_id'],
                         'method': 'Sparse native IK point targets; rigid measured grips; local FK weapon parts',
                         'basis': 'Cascadeur Y-up cm; bone rotation C @ Blender R',
                         'elbow_hinge_policy': 'Both sides derive sign from native reference; shared normal cross(E-S,W-E)',
                         'finger_policy': 'Ready fingers held, measured magazine/rack grasp fingers if supplied',
                         'tip_socket': tip_name,
                         'magazine_exchange': 'Same mesh reused while below view; native capture must confirm it is hidden' if reload else None,
                         'calibration': calibration, 'poses': {}, 'visual_acceptance': False})
    local_scales = {bone: _native_local_scale(native_reference, positions, bone, part['parent'])
                    for bone, part in parts.items()}

    def move(p, delta, rotation):
        return add(add(wp, delta), mv(rotation, sub(p, wp)))

    for event in events:
        frame = event['frame']
        delta, rotation = _world_delta(event['pose'])
        point_targets, part_targets, finger_targets, measures, part_worlds = {}, {}, {}, {}, {}
        main_weapon_position = add(wp, delta)
        main_weapon_rotation = mm(rotation, wr)
        left_ready = (move(positions['hand_l'], delta, rotation), mm(rotation, rotations['hand_l']))

        def moving_grasp(name):
            p, r, grasp = grasps[name]
            bone = grasp.get('part_bone', 'prop_magazine' if name == 'magazine' else
                             ('prop_slide' if clip.startswith('pistol_') else 'prop_bolt'))
            part = parts[bone]
            part_delta, part_rotation = _part_motion(event, bone)
            pivot = mul(part['pivot_weapon_m'], 100.)
            local_p = mv(wi, sub(p, wp))
            changed_p = add(add(pivot, part_delta), mv(part_rotation, sub(local_p, pivot)))
            global_p = add(main_weapon_position, mv(main_weapon_rotation, changed_p))
            global_r = mm(main_weapon_rotation, mm(part_rotation, mm(wi, r)))
            return global_p, global_r, grasp

        left_mode = event['left']
        if not reload or left_mode == 'support':
            left_p, left_r = left_ready
            left_contact = 'weapon'
            finger_grasp, finger_mix = None, 0.
        elif left_mode == 'magazine':
            left_p, left_r, finger_grasp = moving_grasp('magazine')
            left_contact, finger_mix = 'prop_magazine', 1.
        elif left_mode == 'rack':
            left_p, left_r, finger_grasp = moving_grasp('rack')
            left_contact, finger_mix = finger_grasp.get('part_bone', 'rack'), 1.
        elif left_mode == 'to_magazine':
            p, r, finger_grasp = moving_grasp('magazine')
            left_p, left_r = lerp(left_ready[0], p, event['blend']), blend_rotation(left_ready[1], r, event['blend'])
            left_contact, finger_mix = 'free', event['blend']
        elif left_mode == 'to_rack':
            p0, r0, unused = moving_grasp('magazine')
            p1, r1, finger_grasp = moving_grasp('rack')
            left_p, left_r = lerp(p0, p1, event['blend']), blend_rotation(r0, r1, event['blend'])
            left_contact, finger_mix = 'free', event['blend']
        elif left_mode == 'from_rack':
            p, r, finger_grasp = moving_grasp('rack')
            left_p, left_r = lerp(p, left_ready[0], event['blend']), blend_rotation(r, left_ready[1], event['blend'])
            # Clear the receiver while returning, so the wrist path does not
            # cut straight through its top surface between reach and support.
            left_p = add(left_p, mul(UP, 3.*math.sin(math.pi*event['blend'])))
            left_contact, finger_mix = 'free', 1.-event['blend']
        elif left_mode == 'from_magazine':
            p, r, finger_grasp = moving_grasp('magazine')
            left_p, left_r = lerp(p, left_ready[0], event['blend']), blend_rotation(r, left_ready[1], event['blend'])
            clearance = math.sin(math.pi*event['blend'])
            left_p = add(left_p, mv(main_weapon_rotation, [0., 2.*clearance, -6.*clearance]))
            left_contact, finger_mix = 'free', 1.-event['blend']
        else:
            raise ValueError('Unknown left hand mode '+left_mode)

        if clip == 'pistol_reload' and left_mode in ('to_rack', 'from_rack'):
            # Approach the exposed slide side outside its receiver envelope;
            # an endpoint-only wrist check cannot detect a palm cutting through
            # the slide during a direct magazine-to-rack or rack-to-grip lerp.
            clearance = math.sin(math.pi*event['blend'])
            left_p = add(left_p, mv(main_weapon_rotation, [0., 4.*clearance, -10.*clearance]))

        for side in ('r', 'l'):
            hand = 'hand_'+side
            cal = calibration[side]
            wrist, hand_rotation = ((move(positions[hand], delta, rotation), mm(rotation, rotations[hand]))
                                    if side == 'r' else (left_p, left_r))
            try:
                shoulder = _bounded_shoulder(positions['upperarm_'+side], wrist, delta, cal['upper'], cal['lower'],
                                              positions['clavicle_'+side])
            except ValueError as exc:
                raise ValueError(f'{clip} frame{frame} {side}: {exc}; wrist={wrist}') from exc
            pole = add(positions['lowerarm_'+side], mul(delta, .35))
            if side == 'l' and finger_grasp and finger_grasp.get('elbow_hint_m') is not None:
                hint = move(mul(mv(C, finger_grasp['elbow_hint_m']), 100.), delta, rotation)
                hint_mix = event['blend'] if left_mode == 'to_rack' else (1.-event['blend'] if left_mode == 'from_rack' else 1.)
                pole = lerp(pole, hint, hint_mix)
            long = mv(hand_rotation, cal['metacarpal_local'])
            guarded = clip.startswith(('heavyaxe_', 'pickaxe_'))
            elbow = _elbow_for_hand(shoulder, wrist, long, pole, cal['upper'], cal['lower'],
                                    hand_rotation, cal if guarded else None,
                                    bend_limit=22. if guarded else 24.)
            forearm = sub(wrist, elbow)
            bend = degrees_between(forearm, long)
            free_wrist_correction = 0.
            if side == 'l' and left_contact == 'free' and bend > 22.:
                # A detached hand can relax through the transition.  Never
                # change a hand relative to a weapon, magazine, or slide grip.
                correction_axis = cross(long, forearm)
                free_wrist_correction = bend-22.
                correction = axis_rotation(correction_axis, free_wrist_correction)
                hand_rotation = mm(correction, hand_rotation)
                long = mv(correction, long)
                bend = degrees_between(forearm, long)
            point_targets['upperarm_MainPoint_'+side] = shoulder
            point_targets['lowerarm_MainPoint_'+side] = elbow
            point_targets['hand_MainPoint_'+side] = wrist
            for name, offset in cal['point_offsets'].items():
                point_targets['hand_'+name+'_'+side] = add(wrist, mv(hand_rotation, offset))
            hinge = unit(cross(sub(elbow, shoulder), forearm))
            point_targets['lowerarm_AdditionalPoint_'+side] = add(elbow, mul(hinge, cal['hinge_sign']*cal['hinge_radius']))
            measures[side] = dict(shoulder=shoulder, elbow=elbow, wrist=wrist, hand_rotation=hand_rotation,
                                  wrist_bend_degrees=bend, reach_fraction=length(sub(wrist, shoulder))/(cal['upper']+cal['lower']),
                                  wrist_axial_delta_degrees=_planned_wrist_twist(shoulder, elbow, wrist, hand_rotation, cal),
                                  shoulder_displacement_cm=length(sub(shoulder, positions['upperarm_'+side])),
                                  free_wrist_correction_degrees=free_wrist_correction,
                                  elbow_limit_violations_cm=_elbow_limits(shoulder, wrist, elbow),
                                  contact='weapon' if side == 'r' else left_contact,
                                  weapon_local_wrist_cm=mv(trans(main_weapon_rotation), sub(wrist, main_weapon_position)))

        for bone, part in parts.items():
            parent = part['parent']
            d, q = _part_motion(event, bone)
            parent_inverse = trans(rotations[parent])
            pivot = mul(part['pivot_weapon_m'], 100.)
            ready_position_in_weapon = mv(wi, sub(positions[bone], wp))
            moved_position_in_weapon = add(add(pivot, d), mv(q, sub(ready_position_in_weapon, pivot)))
            weapon_offset = sub(moved_position_in_weapon, ready_position_in_weapon)
            local_offset = mul(mv(parent_inverse, mv(wr, weapon_offset)), 1./local_scales[bone])
            world_delta_rotation = mm(wr, mm(q, wi))
            local_rotation_delta = mm(parent_inverse, mm(world_delta_rotation, rotations[parent]))
            part_targets[bone] = {
                'local_position': add(ready_local[bone]['local_position'], local_offset),
                'local_rotation': mm(local_rotation_delta, ready_local[bone]['local_rotation']),
            }
            part_worlds[bone] = {
                'position_cm': add(main_weapon_position, mv(main_weapon_rotation, moved_position_in_weapon)),
                'rotation': mm(main_weapon_rotation, mm(q, mm(wi, rotations[bone]))),
            }
        # Ready finger local rotations are explicitly keyed too, so a preceding
        # reload grasp cannot leak into the final ready/support pose.
        for bone, row in ready_local.items():
            if bone.startswith(('thumb_', 'index_', 'middle_', 'ring_', 'pinky_')) and bone.endswith('_l'):
                target = row['local_rotation']
                if finger_grasp and bone in finger_grasp.get('finger_local_rotations', {}):
                    # Magazine-to-rack transitions start from the actual
                    # magazine grasp, not the unrelated support-hand curl.
                    if left_mode == 'to_rack':
                        target = grasps['magazine'][2].get('finger_local_rotations', {}).get(bone, target)
                    target = blend_rotation(target, finger_grasp['finger_local_rotations'][bone], finger_mix)
                finger_targets[bone] = {'local_rotation': target}
        out['keyframes'][frame], out['prop_keyframes'][frame], out['finger_keyframes'][frame] = point_targets, part_targets, finger_targets
        out['metadata']['poses'][frame] = dict(phase=event['phase'], hands=measures, left_mode=left_mode,
                                              weapon_position_cm=main_weapon_position, weapon_rotation=main_weapon_rotation,
                                              tip_position_cm=add(main_weapon_position, mv(main_weapon_rotation, tip_local)),
                                              parts=part_worlds,
                                              magazine_translation_weapon_cm=event['magazine'][:3],
                                              magazine_rotation_degrees=event['magazine'][3], rack_translation_cm=event['rack'])
    if out['keyframes'][0] != out['keyframes'][out['end_frame']]:
        raise AssertionError('Final native point targets do not return exactly to ready')
    if out['prop_keyframes'][0] != out['prop_keyframes'][out['end_frame']]:
        raise AssertionError('Final part transforms do not return to ready')
    bends = [m['wrist_bend_degrees'] for p in out['metadata']['poses'].values() for m in p['hands'].values()]
    out['metadata']['maximum_planned_wrist_bend_degrees'] = max(bends)
    out['metadata']['native_local_scale_cm'] = local_scales
    out['metadata']['requires_wrist_revision'] = max(bends) > 25.
    elbow_violation = max(v for p in out['metadata']['poses'].values() for hand in p['hands'].values()
                          for v in hand['elbow_limit_violations_cm'].values())
    out['metadata']['maximum_elbow_limit_violation_cm'] = elbow_violation
    out['metadata']['requires_elbow_revision'] = elbow_violation > .25
    out['metadata']['requires_pose_revision'] = max(bends) > 25. or elbow_violation > .25
    axial = max(hand['wrist_axial_delta_degrees'] for p in out['metadata']['poses'].values() for hand in p['hands'].values())
    out['metadata']['maximum_planned_wrist_axial_delta_degrees'] = axial
    out['metadata']['requires_axial_revision'] = clip.startswith(('heavyaxe_', 'pickaxe_')) and axial > 40.
    out['metadata']['requires_pose_revision'] |= out['metadata']['requires_axial_revision']
    tips = [row['tip_position_cm'] for row in out['metadata']['poses'].values()]
    out['metadata']['tip_bounds_cm'] = {
        'minimum': [min(point[axis] for point in tips) for axis in range(3)],
        'maximum': [max(point[axis] for point in tips) for axis in range(3)],
    }
    # Native constraints need a constant local offset through each hold. The
    # release key remains in contact but its outgoing interval is unconstrained.
    # Thus [start,end) selects the driver; the end key gets index0 if followed
    # by a free transition. A final end key may retain the driver for its hold.
    intervals = []
    ordered = sorted(out['metadata']['poses'])
    for start, end in zip(ordered, ordered[1:]):
        a = out['metadata']['poses'][start]['hands']['l']['contact']
        b = out['metadata']['poses'][end]['hands']['l']['contact']
        if a == b and a != 'free':
            if intervals and intervals[-1]['driver'] == a and intervals[-1]['end_frame'] == start:
                intervals[-1]['end_frame'] = end
            else:
                intervals.append({'start_frame': start, 'end_frame': end, 'driver': a})
    out['constraint_intervals'] = intervals
    for interval in intervals:
        frame = interval['start_frame']
        pose = out['metadata']['poses'][frame]
        driver = interval['driver']
        if driver == 'weapon':
            dp, dr = pose['weapon_position_cm'], pose['weapon_rotation']
            interval['bone_driver'] = 'prop_hatchet'
        else:
            dp, dr = pose['parts'][driver]['position_cm'], pose['parts'][driver]['rotation']
            interval['bone_driver'] = driver
        interval['local_point_offsets_cm'] = {
            name: mv(trans(dr), sub(out['keyframes'][frame][name], dp))
            for name in ('hand_MainPoint_l', 'hand_DirectionPoint_l', 'hand_AdditionalPoint_l')
        }
    # Never falsely label an analytically feasible frame as visually approved.
    # Caller must reject requires_wrist_revision and verify native interpolation
    # and contact throughout the resulting clip before presenting acceptance.
    return out


def audit_native_wrist_twist(transforms):
    """Measure axial wrist change separately from forearm/metacarpal bend.

    This is change from the authored ready frame, not an anatomical diagnosis.
    A large value merits visual inspection; genuine reload regrips can rotate
    the hand.  Native local-position units cancel when normalizing the axis.
    """
    result = {}
    first = transforms['0'] if '0' in transforms else transforms[0]
    for side in ('r', 'l'):
        name = 'hand_'+side
        baseline = first[name]['local_rotation']
        values = {}
        for frame, joints in transforms.items():
            current = joints[name]
            delta = mm(current['local_rotation'], trans(baseline))
            q = _quaternion(delta)
            projection = dot(q[1:], unit(current['local_position']))
            values[int(frame)] = 2.*math.degrees(math.atan2(abs(projection), abs(q[0])))
        maximum_frame = max(values, key=values.get)
        result[side] = {'degrees_by_frame': values, 'maximum_degrees': values[maximum_frame],
                        'maximum_frame': maximum_frame}
    return result
