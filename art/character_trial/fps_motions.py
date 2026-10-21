"""Dedicated camera-first choreography for the survivor's separate FPS arms.

``sample(name, seconds, landmarks)`` returns the authored.Rig semantic pose.
It shares durations, anatomy and interpolation helpers with motions.py, never
its TPS trajectories. The torso stays in its bind frame; actual shoulders and
elbows solve the hands. Coordinates are metres, +Z up, -Y forward. The camera
looks slightly down along -Y; anatomical right (-X) is screen right.

Handled props use the calibrated anatomical palm basis and POSITIVE
along-cross-normal as thumb/up. Attachment and calibrated finger overrides are
the baker's responsibility. Rock targets are derived from one rigid frame, so
both palms oppose at the same object centre throughout the whole action.
"""
import math

import motions as M


def _base(m):
    pose = M._base(m, False)
    pose['root'] = (0., 0., 0.)
    pose['body'] = (0., 0., 0.)
    pose['rot'] = {bone: (0., 0., 0.) for bone in pose['rot']}
    for side, sign in (('l', 1.), ('r', -1.)):
        _empty(pose, side, (sign * .235, -.365, m['shoulder'] - .085), m)
    return pose


def _empty(pose, side, at, m, curl='relaxed', rotation=(0., 0., 0.)):
    sign = 1. if side == 'l' else -1.
    # The resting hand continues the lifted forearm instead of dropping at the
    # wrist. The palm remains turned inward, with the thumb on the upper side.
    along = M._turn((-sign * .16, -.92, .35), rotation)
    normal = M._turn((-sign * .88, .02, -.47), rotation)
    M._hand(pose, side, at, (sign * .32, .025, m['shoulder'] - .28),
            along, normal, curl)


def _handle(pose, at, pole, wrist, m):
    # The FPS forearm determines the metacarpal direction. Reusing the world
    # orientation of the TPS grip bent this wrist by 60–95 degrees.
    shoulder, _ = M._shoulder(pose, m, 'r')
    lengths = M._arm_lengths(m, 'r')
    distance = math.sqrt(M._dot(M._sub(at, shoulder), M._sub(at, shoulder)))
    if distance >= sum(lengths) - .005:
        raise ValueError('FPS handle target leaves no elbow bend')
    elbow = M._solve_elbow(shoulder, at, pole, lengths)
    forward = M._unit(M._sub(at, elbow))
    normal = M._unit(M._cross((0., 0., 1.), forward))
    up = M._unit(M._cross(forward, normal))
    extension, deviation, roll = map(math.radians, wrist)
    along = M._unit(M._add(M._mul(forward, math.cos(extension)),
                          M._mul(up, math.sin(extension))))
    along = M._unit(M._add(M._mul(along, math.cos(deviation)),
                          M._mul(normal, math.sin(deviation))))
    normal = M._unit(M._sub(normal, M._mul(along, M._dot(normal, along))))
    normal = M._add(M._mul(normal, math.cos(roll)),
                    M._mul(M._cross(along, normal), math.sin(roll)))
    M._hand(pose, 'r', at, elbow, along, normal, 'handle')


def _idle(t, m):
    p = _base(m)
    phase = math.tau * t / M.CLIP_BY_NAME['idle']['duration']
    breath = math.sin(phase)
    # Quiet, asymmetric breath; no viewmodel shoulder/root translation.
    for side, sign in (('l', 1.), ('r', -1.)):
        h = p['ik']['hand_' + side]
        h['at'] = M._add(h['at'], (sign * .0018 * breath,
                                  .0030 * breath, .0035 * breath))
    return p


def _gait(name, t, m):
    p = _base(m)
    phase = math.tau * t / M.CLIP_BY_NAME[name]['duration']
    swing = math.cos(phase)
    footfall = math.cos(2. * phase)
    for side, sign in (('l', 1.), ('r', -1.)):
        lead = sign * swing
        if name == 'sprint':
            # Folded running arms alternately enter the lower corners. The
            # passing arc stays in front of the torso, away from the shoulder.
            at = (sign * (.215 + .008 * math.sin(phase)),
                  -.325 + .080 * lead,
                  m['shoulder'] - .035 - .052 * lead + .008 * footfall)
            along = (-sign * .32, -.72, .615)
            normal = (-sign * .86, .18, -.28)
            M._hand(p, side, at, (sign * .30, .045, m['shoulder'] - .24),
                    along, normal, 'ready')
            p['fingers'][side] = ((42., 56., 39.), (22., 27., 17.))
        elif name == 'crouch_walk':
            at = (sign * (.245 + .006 * math.sin(phase)),
                  -.350 + .017 * lead,
                  m['shoulder'] - .075 - .010 * lead + .003 * footfall)
            _empty(p, side, at, m, 'ready', (3. * lead, 0., sign * 3.))
        else:
            at = (sign * (.235 + .006 * math.sin(phase)),
                  -.365 + .030 * lead,
                  m['shoulder'] - .085 - .017 * lead + .0045 * footfall)
            _empty(p, side, at, m, 'relaxed', (2. * lead, 0., 0.))
    return p


def _jump(t, m):
    # Time, half-width, depth, shoulder-relative height, pitch, finger tension.
    rows = [
        (0., .235, -.365, -.085, 0., 0.),
        (.17, .245, -.320, -.155, -8., .25),
        (.30, .240, -.370, -.015, 8., .60),
        (.49, .265, -.350, .030, 14., .35),
        (.70, .270, -.345, .010, 8., .20),
        (.89, .275, -.390, -.015, -2., .65),
        (1.0, .280, -.395, -.020, -4., .80),
        (1.10, .265, -.345, -.135, -12., .55),
        (1.34, .240, -.360, -.065, 2., .15),
        (1.8, .235, -.365, -.085, 0., 0.),
    ]
    width, depth, height, pitch, tension = M._keys([(r[0], r[1:]) for r in rows], t)
    p = _base(m)
    for side, sign in (('l', 1.), ('r', -1.)):
        _empty(p, side, (sign * width, depth, m['shoulder'] + height),
               m, rotation=(pitch, 0., sign * 4. * tension))
        p['fingers'][side] = M._mix(M._CURL['relaxed'], M._CURL['ready'], tension)
    return p


def _rock(t, m):
    # A larger overhead-to-forward arc, with one shared stone frame. Solve the
    # elbows from that frame so both wrists remain straight and neither palm
    # slides across the stone as its height changes.
    rows = [
        (0., (0., -.435, -.100)),
        (.16, (0., -.390, -.065)),
        (.37, (0., -.385, .165)),
        (.46, (0., -.395, .180)),
        (.54, (0., -.440, .135)),
        (19. / 30., (0., -.555, -.080)),
        (.71, (0., -.545, -.155)),
        (.88, (0., -.470, -.170)),
        (1.20, (0., -.435, -.100)),
        (1.50, (0., -.435, -.100)),
    ]
    center = M._keys(rows, t)
    center = M._add(center, (0., 0., m['shoulder']))
    p = _base(m)
    shoulder, _ = M._shoulder(p, m, 'r')
    upper, lower = M._arm_lengths(m, 'r')
    q = M._sub(M._add(center, (-(M.GRIP['normal'] + M.GRIP['rock_radius']), 0., 0.)), shoulder)
    length = M.GRIP['along'] + lower
    k = (M._dot(q, q) + length * length - upper * upper) / (2. * length)
    radius = math.hypot(q[1], q[2])
    if abs(k) > radius:
        raise ValueError('FPS two-hand rock target is outside the arm reach')
    angle = math.atan2(q[2], -q[1]) + math.acos(k / radius)
    along = (0., -math.cos(angle), math.sin(angle))
    for side, sign in (('l', 1.), ('r', -1.)):
        normal = (-sign, 0., 0.)
        wrist = M._add(center, M._add(M._mul(along, -M.GRIP['along']),
                      M._mul(normal, -(M.GRIP['normal'] + M.GRIP['rock_radius']))))
        elbow = M._sub(wrist, M._mul(along, M._arm_lengths(m, side)[1]))
        M._hand(p, side, wrist, elbow, along, normal, 'rock')
    return p


def _hatchet(t, m):
    # The elbow rises with the wind-up and stays above the wrist at contact.
    # This pitches the whole forearm into the blow instead of folding the wrist.
    rows = [
        (0., (-.235, -.340, -.150), (-.36, -.10, -.18), (5., 0., -4.)),
        (.18, (-.265, -.290, -.080), (-.39, -.13, -.14), (6., 0., -5.)),
        (.37, (-.235, -.320, .070), (-.33, -.24, .100), (7., 0., -8.)),
        (.46, (-.225, -.330, .090), (-.31, -.25, .130), (5., 0., -6.)),
        (.53, (-.200, -.380, .070), (-.32, -.24, .110), (2., 0., -2.)),
        (.60, (-.170, -.465, -.160), (-.28, -.24, .080), (-5., 0., 0.)),
        (.69, (-.135, -.430, -.220), (-.25, -.23, .020), (-7., 0., 2.)),
        (.87, (-.185, -.380, -.200), (-.33, -.14, -.14), (0., 0., -2.)),
        (1.17, (-.235, -.340, -.150), (-.36, -.10, -.18), (5., 0., -4.)),
        (1.40, (-.235, -.340, -.150), (-.36, -.10, -.18), (5., 0., -4.)),
    ]
    at, pole, wrist = M._keys([(r[0], r[1:]) for r in rows], t)
    p = _base(m)
    _handle(p, M._add(at, (0., 0., m['shoulder'])),
            M._add(pole, (0., 0., m['shoulder'])), wrist, m)
    # Free hand balances the blow low on the other side, never follows the axe.
    balance = math.sin(math.pi * M._clamp(t / 1.17)) ** 2
    _empty(p, 'l', (.245 + .018 * balance, -.360 - .015 * balance,
                    m['shoulder'] - .090 + .015 * balance), m, 'ready')
    return p


def _torch(t, m):
    # Starts beneath the lower frame, clears the corner, overshoots gently and
    # settles with the ember at the right of the view instead of over the aim.
    rows = [
        (0., (-.265, -.190, -.330), (-.38, -.08, -.14), (3., 0., -7.)),
        (.18, (-.270, -.290, -.275), (-.38, -.13, -.10), (3., 0., -6.)),
        (.25, (-.260, -.355, -.230), (-.37, -.17, -.06), (4., 0., -5.)),
        (.55, (-.245, -.405, -.205), (-.35, -.19, -.04), (6., 0., -4.)),
        (.76, (-.250, -.410, -.235), (-.36, -.18, -.07), (4., 0., -4.)),
        (1.10, (-.250, -.400, -.230), (-.36, -.18, -.07), (4., 0., -4.)),
        (1.60, (-.250, -.400, -.230), (-.36, -.18, -.07), (4., 0., -4.)),
    ]
    at, pole, wrist = M._keys([(r[0], r[1:]) for r in rows], t)
    p = _base(m)
    _handle(p, M._add(at, (0., 0., m['shoulder'])),
            M._add(pole, (0., 0., m['shoulder'])), wrist, m)
    _empty(p, 'l', (.245, -.360, m['shoulder'] - .090), m)
    return p


def sample(name, t, landmarks):
    """Return one fully specified FPS pose; no Blender or file access."""
    if name not in M.CLIP_BY_NAME:
        raise KeyError('Unknown FPS character trial clip: ' + str(name))
    if not math.isfinite(t):
        raise ValueError('Animation time must be finite')
    meta = M.CLIP_BY_NAME[name]
    t = t % meta['duration'] if meta['loop'] else M._clamp(t, 0., meta['duration'])
    m = M._measure(landmarks)
    if name == 'idle':
        pose = _idle(t, m)
    elif name in ('walk', 'sprint', 'crouch_walk'):
        pose = _gait(name, t, m)
    elif name == 'jump_land':
        pose = _jump(t, m)
    elif name == 'rock_strike':
        pose = _rock(t, m)
    elif name == 'hatchet_chop':
        pose = _hatchet(t, m)
    else:
        pose = _torch(t, m)
    scale = m['scale']
    for target in pose['ik'].values():
        target['at'] = M._mul(target['at'], scale)
        target['pole'] = M._mul(target['pole'], scale)
        target['along'] = M._unit(target['along'])
        target['normal'] = M._unit(M._add(target['normal'],
            M._mul(target['along'], -M._dot(target['normal'], target['along']))))
    return pose
