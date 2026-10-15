using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Animals;

// The animals' AI (GROUND.md §6.9): what each one notices of players, noises and other animals, and what it does about
// it by its temperament. Runs on the slow tick, staggered across animals; the steering (AnimalWorld.Steering.cs) turns
// the decision into a heading and a speed that Move follows every tick.
public sealed partial class AnimalWorld
{
    /// <summary>A warning (a growl, a snort, a bear rearing) lasts this long before a provoked animal charges.</summary>
    public const float WarningSeconds = 1.4f;

    /// <summary>At most this many of a pack bite at once; the rest circle, waiting their turn.</summary>
    public const int PackAttackers = 2;

    /// <summary>What an animal noticed on one think.</summary>
    private struct Senses
    {
        /// <summary>The nearest person or predator noticed: to run from, or to watch.</summary>
        public bool Threat;
        public AnimalTarget ThreatTarget;
        public Vector3 ThreatPoint;
        public float ThreatDistance;

        /// <summary>A gunshot or blast heard.</summary>
        public bool Loud;
        public Vector3 LoudPoint;

        /// <summary>Another noise heard (a blow, chopping): worth a look.</summary>
        public bool Noise;
        public Vector3 NoisePoint;

        /// <summary>The nearest prey noticed (for hunters): a person or a deer.</summary>
        public AnimalTarget Prey;
        public Vector3 PreyPoint;
        public float PreyDistance;

        /// <summary>A predator of its own kind's prey (wolves, bears) close by.</summary>
        public bool Predator;
        public Vector3 PredatorPoint;
        public float PredatorDistance;
    }

    private void Think(Animal a)
    {
        AnimalSpecies s = a.Species;
        long since = Math.Max(1, Tick - a.LastThink);
        float seconds = since / (float)CharacterTicks.Rate;
        a.LastThink = Tick;
        a.Hunger = Math.Clamp(a.Hunger + s.HungerRate * seconds - (a.State == AnimalState.Eat ? 0.06f * seconds : 0), 0, 1);
        // Its surroundings mapped ahead (on workers), so its steering finds the grid ready.
        Nav.Prefetch(a.Position, 64);
        Senses n = Perceive(a, since);
        bool alarmed = a.Herd.AlarmTick > a.AlarmSeen && Tick - a.Herd.AlarmTick <= 2 * CharacterTicks.Rate;
        a.AlarmSeen = Math.Max(a.AlarmSeen, a.Herd.AlarmTick);
        bool hurt = Tick - a.HurtTick <= CharacterTicks.From(Math.Max(6, s.ChaseSeconds));
        a.Alert = n.Threat || n.Loud || hurt || alarmed;

        if (s.Temperament == Temperament.Flee)
        {
            ThinkFlighty(a, n, alarmed, hurt);
        }
        else
        {
            ThinkFighter(a, n, alarmed, hurt);
        }
        TrackProgress(a, seconds);
    }

    // ---------------------------------------------------------------- perception

    private Senses Perceive(Animal a, long since)
    {
        AnimalSpecies s = a.Species;
        AnimalSenses senses = Content.AnimalSenses;
        var n = new Senses { ThreatDistance = float.MaxValue, PreyDistance = float.MaxValue, PredatorDistance = float.MaxValue };
        Vector3 up = UpAt(a.Position);
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(a.Yaw, up);
        float halfCone = s.Cone / 2;
        float cosCone = halfCone >= MathF.PI ? -2 : (float)DMath.Cos(halfCone);
        bool Sees(Vector3 at, float range, out float distance)
        {
            Vector3 d = at - a.Position;
            Vector3 flat = d - up * Vector3.Dot(d, up);
            distance = flat.Length();
            if (distance > range)
            {
                return false;
            }
            return distance <= senses.Near || distance < 1e-3f || Vector3.Dot(flat / distance, forward) >= cosCone;
        }

        foreach (AnimalObserver o in Observers)
        {
            float range = s.Sight * (o.Crouching ? senses.CrouchSight : 1) * (o.Velocity.LengthSquared() < 0.25f ? senses.StillSight : 1) *
                (o.Lying ? 0.5f : 1);
            bool seen = Sees(o.Position, range, out float distance);
            bool heard = !seen && o.Noise * s.Hearing >= distance;
            bool known = a.HurtBy.Kind == TargetKind.Player && a.HurtBy.Id == o.Id && Tick - a.HurtTick < 10 * CharacterTicks.Rate &&
                distance < s.Sight * 1.5f;
            if (!seen && !heard && !known)
            {
                continue;
            }
            if (distance < n.ThreatDistance)
            {
                n.Threat = true;
                n.ThreatTarget = AnimalTarget.Player(o.Id);
                n.ThreatPoint = o.Position;
                n.ThreatDistance = distance;
            }
            if (distance < n.PreyDistance)
            {
                n.Prey = AnimalTarget.Player(o.Id);
                n.PreyPoint = o.Position;
                n.PreyDistance = distance;
            }
        }

        // Noises since its last think.
        foreach (AnimalNoise noise in Noises)
        {
            if (noise.Tick <= Tick - since || noise.Tick > Tick)
            {
                continue;
            }
            float distance = Vector3.Distance(noise.Position, a.Position);
            if (distance > noise.Radius * s.Hearing)
            {
                continue;
            }
            if (noise.Loud)
            {
                n.Loud = true;
                n.LoudPoint = noise.Position;
            }
            else
            {
                n.Noise = true;
                n.NoisePoint = noise.Position;
            }
        }

        // Other animals: predators for the prey, prey for the hunters.
        float reach = MathF.Max(s.Sight, s.FleeDistance + 12);
        Near(a.Position, reach, _near);
        foreach (Animal other in _near)
        {
            if (other == a || other.IsDead || other.Removed || other.Species == s)
            {
                continue;
            }
            if (s.Prey && other.Species.Temperament == Temperament.Hunt && Sees(other.Position, s.FleeDistance + 12, out float d) &&
                d < n.PredatorDistance)
            {
                n.Predator = true;
                n.PredatorPoint = other.Position;
                n.PredatorDistance = d;
            }
            if (s.Temperament == Temperament.Hunt && other.Species.Prey && Sees(other.Position, s.Sight, out float p) && p < n.PreyDistance)
            {
                n.Prey = AnimalTarget.Animal(other.Id);
                n.PreyPoint = other.Position;
                n.PreyDistance = p;
            }
        }
        return n;
    }

    // ---------------------------------------------------------------- flighty animals (deer)

    private void ThinkFlighty(Animal a, in Senses n, bool alarmed, bool hurt)
    {
        AnimalSpecies s = a.Species;
        bool close = n.Threat && n.ThreatDistance < s.FleeDistance;
        bool predator = n.Predator && n.PredatorDistance < s.FleeDistance + 12;
        if (hurt || n.Loud || close || predator || alarmed)
        {
            Vector3 from = close ? n.ThreatPoint : predator ? n.PredatorPoint : n.Loud ? n.LoudPoint : hurt || alarmed ? a.Herd.ThreatPoint : n.ThreatPoint;
            if (!alarmed || a.State != AnimalState.Flee)
            {
                Raise(a.Herd, from, a);
            }
            Flee(a, from, CharacterTicks.From(RngFor(a).Range(5, 8)));
            return;
        }
        if (a.State == AnimalState.Flee)
        {
            // Keeps running while the threat is near or the flight is young; then turns to look back.
            bool stillNear = n.Threat && n.ThreatDistance < s.Calm;
            if (Tick < a.Until || stillNear)
            {
                Flee(a, stillNear ? n.ThreatPoint : a.TargetPoint, 0);
                return;
            }
            Watch(a, a.TargetPoint, CharacterTicks.From(2.5));
            return;
        }
        if (n.Threat)
        {
            // Noticed, not yet close: heads up, watching.
            Watch(a, n.ThreatPoint, CharacterTicks.From(2));
            return;
        }
        if (n.Noise && a.State != AnimalState.Alert)
        {
            Watch(a, n.NoisePoint, CharacterTicks.From(1.5));
            return;
        }
        if (a.State == AnimalState.Alert && Tick < a.Until)
        {
            Face(a, a.TargetPoint);
            return;
        }
        Calm(a);
    }

    // ---------------------------------------------------------------- animals that fight (boar, wolf, bear)

    private void ThinkFighter(Animal a, in Senses n, bool alarmed, bool hurt)
    {
        AnimalSpecies s = a.Species;
        Herd herd = a.Herd;
        bool weak = a.HealthFraction < s.FleeHealth;

        // Badly hurt: it runs.
        if (weak && (hurt || n.Threat || alarmed))
        {
            Flee(a, hurt ? HurtPoint(a, n) : n.Threat ? n.ThreatPoint : herd.ThreatPoint, CharacterTicks.From(8));
            return;
        }
        if (a.State == AnimalState.Flee && Tick < a.Until)
        {
            Flee(a, a.TargetPoint, 0);
            return;
        }
        // Hurt: it turns on whoever did it (boars charge when hurt; wolves and bears too).
        if (hurt && !a.HurtBy.IsNone && TryTarget(a.HurtBy, out Vector3 attacker, out _) && !(a.State == AnimalState.Chase && a.Target == a.HurtBy))
        {
            BeginChase(a, a.HurtBy, attacker);
            if (s.Temperament == Temperament.Hunt)
            {
                ShareHunt(herd, a.HurtBy);
            }
        }
        // A pack mate's alarm: a hunter joins the fight against the threat.
        if (alarmed && s.Temperament == Temperament.Hunt && herd.Prey.IsNone && n.Threat && a.State != AnimalState.Chase)
        {
            ShareHunt(herd, n.ThreatTarget);
        }
        // In a chase: keep at it, attack in reach, or give up.
        if (a.State is AnimalState.Chase or AnimalState.Attack)
        {
            if (KeepChasing(a, n))
            {
                return;
            }
            GiveUp(a);
            return;
        }
        // A hungry hunter hunts what it notices, or joins its pack's hunt.
        if (s.Temperament == Temperament.Hunt && a.Hunger >= s.HuntHunger && Tick >= a.HuntAfter)
        {
            if (!herd.Prey.IsNone && Tick - herd.PreySeenTick < 8 * CharacterTicks.Rate && TryTarget(herd.Prey, out Vector3 prey, out _))
            {
                BeginChase(a, herd.Prey, prey);
                return;
            }
            if (!n.Prey.IsNone)
            {
                ShareHunt(herd, n.Prey);
                BeginChase(a, n.Prey, n.PreyPoint);
                return;
            }
        }
        // Too close: a warning first (from when they came within its space), then a charge.
        if (s.PersonalSpace > 0 && n.Threat && n.ThreatDistance < s.PersonalSpace)
        {
            if (a.Warned != n.ThreatTarget || a.WarnStart == Animal.Never)
            {
                a.Warned = n.ThreatTarget;
                a.WarnStart = Tick;
            }
            else if (Tick - a.WarnStart >= CharacterTicks.From(WarningSeconds))
            {
                a.WarnStart = Animal.Never;
                BeginChase(a, n.ThreatTarget, n.ThreatPoint);
                if (s.Temperament == Temperament.Hunt)
                {
                    ShareHunt(herd, n.ThreatTarget);
                }
                return;
            }
            Watch(a, n.ThreatPoint, CharacterTicks.From(4), n.ThreatTarget);
            Face(a, n.ThreatPoint);
            return;
        }
        a.WarnStart = Animal.Never;
        // Gunfire frightens a boar that isn't fighting.
        if (n.Loud && s.Temperament == Temperament.Defend)
        {
            Flee(a, n.LoudPoint, CharacterTicks.From(6));
            return;
        }
        // Feeding at a kill.
        if (a.State == AnimalState.Eat && Tick < a.Until)
        {
            Face(a, a.TargetPoint);
            return;
        }
        if (n.Threat && n.ThreatDistance < s.Sight * 0.6f && a.State != AnimalState.Alert)
        {
            Watch(a, n.ThreatPoint, CharacterTicks.From(2.5), n.ThreatTarget);
            return;
        }
        if ((n.Loud || n.Noise) && a.State != AnimalState.Alert)
        {
            Watch(a, n.Loud ? n.LoudPoint : n.NoisePoint, CharacterTicks.From(2));
            return;
        }
        if (a.State == AnimalState.Alert && Tick < a.Until)
        {
            Face(a, a.TargetPoint);
            return;
        }
        Calm(a);
    }

    private Vector3 HurtPoint(Animal a, in Senses n) =>
        TryTarget(a.HurtBy, out Vector3 at, out _) ? at : n.Threat ? n.ThreatPoint : a.Herd.ThreatPoint;

    private void ShareHunt(Herd herd, AnimalTarget prey)
    {
        if (prey.IsNone)
        {
            return;
        }
        herd.Prey = prey;
        herd.PreySeenTick = Tick;
        foreach (Animal mate in herd.Members)
        {
            if (!mate.IsDead && !mate.Removed && mate.State is not (AnimalState.Chase or AnimalState.Attack or AnimalState.Flee))
            {
                mate.NextThink = Math.Min(mate.NextThink, Tick + 1 + mate.Slot);
                mate.Asleep = false;
            }
        }
    }

    private void BeginChase(Animal a, AnimalTarget target, Vector3 at)
    {
        if (a.Species.Attack == null)
        {
            return;
        }
        if (a.State is not (AnimalState.Chase or AnimalState.Attack) || a.Target != target)
        {
            a.ChaseStart = Tick;
            a.ChaseOrigin = a.Position;
            a.HasWaypoint = false;
            a.Stuck = 0;
        }
        a.Target = target;
        a.TargetPoint = at;
        a.TargetSeen = Tick;
        if (a.State != AnimalState.Attack)
        {
            SetState(a, AnimalState.Chase);
        }
        ChaseSteer(a, at);
    }

    // A chase goes on while the target is there to chase: alive, not too far from where it began, not too long without
    // getting close, and reachable. In reach it attacks (a pack takes turns).
    private bool KeepChasing(Animal a, in Senses n)
    {
        AnimalSpecies s = a.Species;
        if (!TryTarget(a.Target, out Vector3 at, out _))
        {
            if (a.Target.Kind == TargetKind.Animal && Find(a.Target.Id) is { IsDead: true } kill && s.Temperament == Temperament.Hunt)
            {
                // The prey is down: the pack feeds.
                Feed(a, kill);
                return true;
            }
            return false;
        }
        a.TargetPoint = at;
        a.TargetSeen = Tick;
        if (a.Target == a.Herd.Prey)
        {
            a.Herd.PreySeenTick = Tick;
        }
        float distance = Vector3.Distance(a.Position, at);
        if (distance < 6 && a.Target.Kind == TargetKind.Player && !_host.CanAttackPlayer(a, a.Target.Id))
        {
            a.StrikeTick = -1;
            return false;
        }
        if (distance < 6)
        {
            a.ChaseStart = Tick;
        }
        bool tooLong = Tick - a.ChaseStart > CharacterTicks.From(s.ChaseSeconds);
        bool tooFar = Vector3.Distance(a.ChaseOrigin, a.Position) > s.ChaseRange && Tick - a.HurtTick > CharacterTicks.From(3);
        if (tooLong || tooFar || a.Stuck > 4)
        {
            return false;
        }
        if (a.State == AnimalState.Attack)
        {
            // The wind-up: keep facing the target; a wolf lunges, a boar runs through, a bear stands.
            AttackSteer(a, at);
            return true;
        }
        AttackStats attack = s.Attack!;
        if (Tick >= a.AttackReady && InReach(a, at, attack.Reach, 0.75f) && MayAttack(a)
            && (a.Target.Kind != TargetKind.Player || _host.CanAttackPlayer(a, a.Target.Id)))
        {
            StartAttack(a, a.Target);
            AttackSteer(a, at);
            return true;
        }
        ChaseSteer(a, at);
        return true;
    }

    // A pack bites two at a time: the two nearest the prey go in, the rest circle until one of them falls back.
    private bool MayAttack(Animal a)
    {
        if (a.Herd.Members.Count < 2)
        {
            return true;
        }
        float mine = Vector3.DistanceSquared(a.Position, a.TargetPoint);
        int ahead = 0;
        foreach (Animal mate in a.Herd.Members)
        {
            if (mate == a || mate.IsDead || mate.Removed || mate.Target != a.Target || mate.State is not (AnimalState.Chase or AnimalState.Attack))
            {
                continue;
            }
            float theirs = Vector3.DistanceSquared(mate.Position, a.TargetPoint);
            if (mate.State == AnimalState.Attack || theirs < mine || (theirs == mine && mate.Slot < a.Slot))
            {
                ahead++;
            }
        }
        return ahead < PackAttackers;
    }

    private void GiveUp(Animal a)
    {
        if (a.Target == a.Herd.Prey)
        {
            a.Herd.Prey = AnimalTarget.None;
        }
        a.StrikeTick = -1;
        // Not again at once: it watches a moment, and a hunter waits before hunting again.
        a.Until = Tick + CharacterTicks.From(3);
        if (a.Species.Temperament == Temperament.Hunt)
        {
            a.HuntAfter = Tick + CharacterTicks.From(20);
        }
        a.Stuck = 0;
        SetState(a, AnimalState.Alert);
        a.DesiredSpeed = 0;
        Face(a, a.TargetPoint);
        a.Target = AnimalTarget.None;
        a.HurtBy = AnimalTarget.None;
    }

    private void Feed(Animal a, Animal kill)
    {
        a.Herd.Prey = AnimalTarget.None;
        a.Target = AnimalTarget.Animal(kill.Id);
        a.TargetPoint = kill.Position;
        if (a.State != AnimalState.Eat)
        {
            SetState(a, AnimalState.Eat);
            a.Until = Tick + CharacterTicks.From(RngFor(a).Range(25, 40));
        }
        float distance = Vector3.Distance(a.Position, kill.Position);
        if (distance > a.Species.Length + 0.6f)
        {
            Steer(a, kill.Position, a.Species.WalkSpeed);
        }
        else
        {
            a.DesiredSpeed = 0;
            Face(a, kill.Position);
        }
    }

    // ---------------------------------------------------------------- calm life: idle, graze, wander with the herd

    private void Calm(Animal a)
    {
        AnimalSpecies s = a.Species;
        Herd herd = a.Herd;
        Animal? leader = herd.Leader;
        a.Target = AnimalTarget.None;
        if (a.State is AnimalState.Flee or AnimalState.Chase or AnimalState.Attack or AnimalState.Alert or AnimalState.Eat)
        {
            SetState(a, AnimalState.Idle);
            a.Until = Tick + CharacterTicks.From(RngFor(a).Range(1, 3));
        }
        // A follower keeps near its leader: catches up when left behind, walks along when the herd moves.
        if (leader != null && leader != a)
        {
            float apart = Vector3.Distance(a.Position, leader.Position);
            if (apart > s.Cohesion)
            {
                SetState(a, AnimalState.Wander);
                Steer(a, FollowPoint(a, leader), apart > 2 * s.Cohesion ? s.TrotSpeed : s.WalkSpeed * 1.25f);
                return;
            }
            if (leader.State == AnimalState.Wander && leader.Speed > 0.3f && apart > s.Cohesion * 0.35f)
            {
                SetState(a, AnimalState.Wander);
                Steer(a, FollowPoint(a, leader), s.WalkSpeed * 1.1f);
                return;
            }
        }
        // Standing inside another: a step aside.
        if (a.State is AnimalState.Idle or AnimalState.Graze && Crowded(a, out Vector3 aside))
        {
            Steer(a, aside, s.WalkSpeed, path: false);
            return;
        }
        if (Tick < a.Until)
        {
            if (a.State == AnimalState.Wander)
            {
                Vector3 goal = leader == a || leader == null ? herd.WanderGoal : FollowPoint(a, leader);
                if (Vector3.Distance(a.Position, goal) < 2.5f)
                {
                    a.Until = Tick;
                }
                else
                {
                    Steer(a, goal, s.WalkSpeed);
                    return;
                }
            }
            else
            {
                a.DesiredSpeed = 0;
                return;
            }
        }
        // Something new to do.
        Rng rng = RngFor(a);
        double pick = rng.NextDouble();
        bool grazer = s.Temperament != Temperament.Hunt;
        if (leader == a || leader == null)
        {
            if (pick < 0.45)
            {
                herd.WanderGoal = WanderGoal(a, ref rng);
                SetState(a, AnimalState.Wander);
                a.Until = Tick + CharacterTicks.From(rng.Range(12, 25));
                Steer(a, herd.WanderGoal, s.WalkSpeed);
                return;
            }
        }
        else if (pick < 0.2)
        {
            SetState(a, AnimalState.Wander);
            a.Until = Tick + CharacterTicks.From(rng.Range(3, 6));
            Steer(a, FollowPoint(a, leader), s.WalkSpeed);
            return;
        }
        // Grazers graze; hunters sniff about (the same head-down state).
        SetState(a, rng.Chance(grazer ? 0.6 : 0.3) ? AnimalState.Graze : AnimalState.Idle);
        a.Until = Tick + CharacterTicks.From(rng.Range(4, 11));
        a.DesiredSpeed = 0;
    }

    // Another animal closer than their bodies allow: the point a step away from it.
    private bool Crowded(Animal a, out Vector3 aside)
    {
        aside = default;
        Near(a.Position, 2.5f, _near);
        foreach (Animal other in _near)
        {
            if (other == a || other.Removed || (other.IsDead && other.Species != a.Species))
            {
                continue;
            }
            Vector3 up = UpAt(a.Position);
            Vector3 d = OnGround(a.Position - other.Position, up);
            float apart = d.Length();
            float room = a.Species.Radius + other.Species.Radius + 0.25f;
            if (apart < room)
            {
                if (apart < 1e-3f)
                {
                    float yaw = (float)((a.Seed % 628) / 100.0);
                    d = Facing.Basis(yaw, up).Forward;
                    apart = 1;
                }
                aside = a.Position + d / apart * (room + 1.2f);
                return true;
            }
        }
        return false;
    }

    // Followers spread out behind and beside the leader, each at its own place.
    private Vector3 FollowPoint(Animal a, Animal leader)
    {
        float angle = MathF.PI + (a.Slot % 2 == 0 ? 1 : -1) * (0.45f + 0.35f * (a.Slot / 2));
        float distance = 2.2f + 1.2f * (a.Slot / 2) + a.Species.Length * 0.5f;
        float yaw = leader.Yaw + angle;
        return leader.Position + Facing.Basis(yaw, UpAt(leader.Position)).Forward * distance;
    }

    // A point within the herd's home range on ground it can walk.
    private Vector3 WanderGoal(Animal a, ref Rng rng)
    {
        Herd herd = a.Herd;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = rng.NextDouble() * DMath.TwoPi;
            // Mostly onward from where it stands, kept within the home range.
            float step = (float)rng.Range(15, 40);
            Vector3 from = Vector3.Distance(a.Position, herd.Home) > herd.Range * 0.7f ? herd.Home : a.Position;
            Vector3 point = Along(from, angle, step);
            if (Vector3.Distance(point, herd.Home) > herd.Range)
            {
                continue;
            }
            if (Nav.Walkable(point, a.Species.MaxSlope))
            {
                return Nav.Ground(point);
            }
        }
        return herd.Home;
    }

    // ---------------------------------------------------------------- the decisions' movement

    private void Flee(Animal a, Vector3 from, long least)
    {
        if (a.State != AnimalState.Flee)
        {
            SetState(a, AnimalState.Flee);
            a.HasWaypoint = false;
            a.Stuck = 0;
        }
        if (least > 0)
        {
            a.Until = Math.Max(a.Until, Tick + least);
        }
        a.TargetPoint = from;
        a.StrikeTick = -1;
        FleeSteer(a, from);
    }

    private void Watch(Animal a, Vector3 at, long seconds, AnimalTarget target = default)
    {
        if (a.State != AnimalState.Alert || a.Target != target)
        {
            // A new thing to watch: its warning starts now.
            a.State = AnimalState.Alert;
            a.StateSince = Tick;
        }
        a.Until = Tick + seconds;
        a.TargetPoint = at;
        a.Target = target;
        a.DesiredSpeed = 0;
        Face(a, at);
    }

    private void Face(Animal a, Vector3 at)
    {
        Vector3 up = UpAt(a.Position);
        Vector3 d = OnGround(at - a.Position, up);
        if (d.LengthSquared() > 0.01f)
        {
            a.DesiredYaw = YawOf(d, up);
        }
        a.DesiredSpeed = 0;
    }

    // Notices when it makes no headway (a corner, a wall): a chase gives up, a flight or a walk tries elsewhere.
    private void TrackProgress(Animal a, float seconds)
    {
        float wanted = a.DesiredSpeed * seconds;
        float made = Vector3.Distance(a.Position, a.LastThinkPosition);
        a.LastThinkPosition = a.Position;
        if (wanted > 0.5f && made < wanted * 0.25f)
        {
            a.Stuck += seconds;
            if (a.Stuck > 2 && a.State == AnimalState.Wander)
            {
                a.Herd.WanderGoal = a.Herd.Home;
                a.HasWaypoint = false;
            }
        }
        else
        {
            a.Stuck = MathF.Max(0, a.Stuck - seconds);
        }
    }
}
