using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Building;

/// <summary>
/// What a placement would be: the piece, its structure (none yet for a free foundation), its pose in the structure's
/// frame, the socket it snaps to, and whether the rules allow it (with the reason when they don't). The client's ghost
/// and the server's check are the same object from the same code.
/// </summary>
public sealed class PlacementPlan
{
    public required PieceDef Piece { get; init; }

    /// <summary>The structure it joins, or null when it starts a new one (a free foundation).</summary>
    public Structure? Structure { get; init; }

    public BuildingFrame Frame { get; init; }

    public Pose Pose { get; init; }

    /// <summary>The piece it snaps to, or null for a free foundation.</summary>
    public Piece? Support { get; init; }

    public SocketId Socket { get; init; }

    public int Orientation { get; init; }

    /// <summary>
    /// A free foundation's aim point (zone frame: where the aim meets the ground, or the point on the aim at the free
    /// distance, whose height lifts the top) and its heading in 15° steps, as the wire asks for it.
    /// </summary>
    public Vector3 Point { get; init; }

    public int HeadingSteps { get; init; }

    public Outcome Outcome { get; internal set; } = Outcome.Done;

    /// <summary>The stability it would have, in thousandths.</summary>
    public int Stability { get; internal set; }

    public bool Valid => Outcome.Ok;

    /// <summary>Where the ghost goes in the zone.</summary>
    public (Vector3 Position, Quaternion Rotation) ZoneTransform => Frame.ToZone(Pose);

    public override string ToString() =>
        $"{Piece.Key} at {Pose.Position} yaw {Pose.YawDegrees}° on {(Support == null ? "the ground" : Support.Def.Key + " " + Socket)}: {Outcome}";
}

public sealed partial class BuildingWorld
{
    private readonly List<(double Score, Piece Support, SocketId Socket, int Orientation, Pose Pose)> _candidates = new();
    private readonly List<Piece> _aimed = new();
    private readonly List<OverlapHit> _overlaps = new();
    private readonly List<Piece> _supports = new();

    /// <summary>
    /// The placement the player is aiming at with a plan holding <paramref name="piece"/> (RustMVP's preview): every
    /// compatible socket of the pieces near the aim, in every orientation, scored by how near the aim passes, how far
    /// from what it hits, and how close its facing is to the player's (turned by <paramref name="turns"/> presses of
    /// R). The best few are checked and the first allowed one wins; when none is allowed the best is returned with its
    /// reason. A foundation with no socket near is placed on the ground. Returns null when there's nothing to show.
    /// </summary>
    public PlacementPlan? Plan(ulong player, PieceDef piece, Vector3 eye, Vector3 direction, int turns, IBuildingWallet? wallet, double now)
    {
        direction = Vector3.Normalize(direction);
        float reach = (float)Data.Reach, aimRadius = (float)Data.SocketAimRadius;
        Vector3 end = eye + direction * (reach + aimRadius);
        RayHit hit = default;
        bool aimHit = Physics != null && Physics.Ray(eye, direction, reach, out hit);

        _candidates.Clear();
        PiecesNear(eye, end, (float)(0.6 * Data.Width + aimRadius + Data.Storey), _aimed);
        foreach (Piece support in _aimed)
        {
            if (support.Collapsing || support.Gone)
            {
                continue;
            }
            Score(support, piece, eye, end, direction, aimHit ? hit.Point : null, turns);
        }
        _candidates.Sort((a, b) => a.Score.CompareTo(b.Score));

        PlacementPlan? best = null;
        int tried = 0;
        for (int i = 0; i < _candidates.Count && tried < Data.CandidatesTried; i++)
        {
            (double _, Piece support, SocketId socket, int orientation, Pose pose) = _candidates[i];
            if (Duplicate(i))
            {
                continue;
            }
            tried++;
            var plan = new PlacementPlan
            {
                Piece = piece,
                Structure = support.Structure,
                Frame = support.Structure.Frame,
                Pose = pose,
                Support = support,
                Socket = socket,
                Orientation = orientation,
            };
            Check(plan, player, eye, wallet);
            if (plan.Valid)
            {
                return plan;
            }
            best ??= plan;
        }
        if (best != null)
        {
            return best;
        }
        if (piece.Family == PieceFamily.Foundation)
        {
            return FreeFoundation(player, piece, eye, direction, turns, wallet);
        }
        return null;
    }

    // An earlier candidate (better scored) put the same piece in the same place.
    private bool Duplicate(int index)
    {
        (_, Piece support, _, _, Pose pose) = _candidates[index];
        for (int j = 0; j < index; j++)
        {
            (_, Piece other, _, _, Pose earlier) = _candidates[j];
            if (other.Structure.Frame.Equals(support.Structure.Frame) && PieceGeometry.SamePose(earlier, pose))
            {
                return true;
            }
        }
        return false;
    }

    private void Score(Piece support, PieceDef piece, Vector3 eye, Vector3 end, Vector3 direction, Vector3? hit, int turns)
    {
        BuildingFrame frame = support.Structure.Frame;
        Vector3D eyeLocal = frame.ToLocal(D(eye)), endLocal = frame.ToLocal(D(end));
        Vector3D? hitLocal = hit.HasValue ? frame.ToLocal(D(hit.Value)) : null;
        Vector3D forward = frame.DirectionToLocal(D(direction));
        double aim2 = Data.SocketAimRadius * Data.SocketAimRadius;
        var sockets = new List<SocketId>();
        PieceGeometry.Sockets(support.Def, piece, sockets);
        foreach (SocketId socket in sockets)
        {
            Vector3D anchor = PieceGeometry.Anchor(Data, support.Def, support.Pose, socket);
            double anchorMiss = PieceGeometry.DistanceToSegmentSquared(anchor, eyeLocal, endLocal);
            bool floor = piece.Family == PieceFamily.Floor;
            if (!floor && anchorMiss > aim2)
            {
                continue;
            }
            int count = PieceGeometry.Orientations(support.Def, socket, piece);
            for (int o = 0; o < count; o++)
            {
                if (!PieceGeometry.Snap(Data, support.Def, support.Pose, socket, piece, o, out Pose pose))
                {
                    continue;
                }
                double miss, bias;
                if (floor)
                {
                    miss = PieceGeometry.AimFootprint(Data, piece, pose, eyeLocal, endLocal);
                    if (miss > aim2)
                    {
                        continue;
                    }
                    bias = 0.001 * (pose.Position - eyeLocal).LengthSquared;
                }
                else
                {
                    miss = anchorMiss;
                    bias = hitLocal.HasValue ? 0.05 * (anchor - hitLocal.Value).LengthSquared : 0;
                }
                double turn = FacingError(piece, pose.Yaw, forward, turns);
                double score = miss + bias + 0.02 * turn * turn;
                if (support.Structure.At(piece, pose) != null)
                {
                    score += 1e7;
                }
                _candidates.Add((score, support, socket, o, pose));
            }
        }
    }

    /// <summary>
    /// How far (radians) a piece's facing at a yaw is from the facing the player asks for: a wall's hard side, a
    /// staircase's way up and a roof's rise away from the player, turned by the R presses.
    /// </summary>
    private static double FacingError(PieceDef piece, int yaw, Vector3D forward, int turns)
    {
        if (piece.IsSlab)
        {
            return 0;
        }
        double wanted = Math.Atan2(-forward.X, -forward.Z);
        int step = piece.Family switch
        {
            PieceFamily.Wall or PieceFamily.Roof => 6,
            _ => piece.IsTriangle ? 4 : 3,
        };
        wanted += turns * step * Math.PI / 6;
        // The piece's facing: walls face out (+Z), stairs and roofs rise toward −Z.
        double facing = yaw * Math.PI / 6 + (piece.Family == PieceFamily.Wall ? Math.PI : 0);
        double difference = Math.IEEERemainder(facing - wanted, 2 * Math.PI);
        return Math.Abs(difference);
    }

    /// <summary>
    /// A foundation on open ground (RustMVP's free foundation): where the aim meets the ground, at most
    /// <see cref="BuildingData.FreeFoundationDistance"/> away across it; aimed further, it stays at that distance and the
    /// aim's height there lifts its top (<see cref="FoundationAt"/>), so raising the view raises the foundation within
    /// its depth. Square to the player's view (15° steps, R turns it 90°). It starts a new structure whose frame has the
    /// planet's up there.
    /// </summary>
    private PlacementPlan? FreeFoundation(ulong player, PieceDef piece, Vector3 eye, Vector3 direction, int turns, IBuildingWallet? wallet)
    {
        if (Physics == null)
        {
            return null;
        }
        Vector3 up = Up(eye);
        Vector3 flat = direction - up * Vector3.Dot(direction, up);
        // How far the aim goes across the ground for each metre along it (straight up or down: nowhere).
        float across = flat.Length();
        if (across < 1e-3f)
        {
            return null;
        }
        flat /= across;
        float distance = (float)Data.FreeFoundationDistance;
        if (Physics.Ray(eye, direction, (float)Data.Reach, out RayHit hit) && hit.IsGround)
        {
            Vector3 toHit = hit.Point - eye;
            distance = Math.Min(distance, (toHit - up * Vector3.Dot(toHit, up)).Length());
        }
        // The point on the aim at that distance across the ground: the ground where the aim meets it nearer, else in
        // the air, its height the top's.
        Vector3 point = eye + direction * (distance / across);
        // Square to the view, in 15° steps; R turns it a quarter.
        BuildingFrame level = BuildingFrame.At(D(point), D(up), 0);
        Vector3D forward = level.DirectionToLocal(D(flat));
        double heading = Math.Atan2(-forward.X, -forward.Z) + turns * Math.PI / 2;
        heading = Math.Round(heading / (Math.PI / 12)) * (Math.PI / 12);
        PlacementPlan? plan = FoundationAt(piece, point, heading);
        if (plan != null)
        {
            Check(plan, player, eye, wallet);
        }
        return plan;
    }

    /// <summary>Kept between a free foundation's top and the ground's limits (metres), so float rays agree with it.</summary>
    private const double FoundationMargin = 0.02;

    /// <summary>
    /// A new structure's first foundation for a zone point on the player's aim, turned by
    /// <paramref name="headingRadians"/> from the zone's east: over the ground below the point, its frame's up the
    /// planet's there. Its top is the point's height plus the clearance (RustMVP's), held where the ground check allows
    /// it, wherever the slope leaves room: at least the clearance above the highest ground under its corners and centre,
    /// and no more than its depth above the lowest. A point on (or below) the ground gives the lowest such top. The
    /// client's ghost and the server's intent both come here with the same point. Unchecked (see <see cref="Check"/>);
    /// null where there's no ground.
    /// </summary>
    public PlacementPlan? FoundationAt(PieceDef piece, Vector3 point, double headingRadians)
    {
        if (Physics == null)
        {
            return null;
        }
        Vector3 up = Up(point);
        if (!GroundBelow(point + up * 10, up, 30, out RayHit ground))
        {
            return null;
        }
        Vector3 centre = ground.Point;
        up = Up(centre);
        Vector3D upD = D(up);
        BuildingFrame probe = BuildingFrame.At(D(centre), upD, headingRadians);
        double highest = double.MinValue, lowest = double.MaxValue;
        foreach (Vector3D sample in GroundSamples(piece))
        {
            Vector3 at = F(probe.ToZone(sample));
            if (GroundBelow(at + up * 10, up, 10 + (float)(Data.FoundationDepth + 10), out RayHit h))
            {
                highest = Math.Max(highest, 10 - h.Distance);
                lowest = Math.Min(lowest, 10 - h.Distance);
            }
        }
        if (highest == double.MinValue)
        {
            return null;
        }
        // Heights above the ground under the centre. Too steep to fit both limits: the lowest top (the check says why).
        double aimed = Vector3.Dot(point - centre, up) + Data.FoundationClearance;
        double low = highest + Data.FoundationClearance + FoundationMargin;
        double high = lowest + Data.FoundationDepth - FoundationMargin;
        double top = Math.Max(low, Math.Min(aimed, high));
        BuildingFrame frame = BuildingFrame.At(D(centre) + upD * top, upD, headingRadians);
        int steps = (int)Math.Round(headingRadians / HeadingStep);
        return new PlacementPlan
        {
            Piece = piece, Structure = null, Frame = frame, Pose = new Pose(Vector3D.Zero, 0), Point = point,
            HeadingSteps = ((steps % HeadingSteps) + HeadingSteps) % HeadingSteps,
        };
    }

    // The ground straight down from a point. A ray exactly along the seam of two ground tiles can slip between their
    // meshes, so a miss is tried again a millimetre aside.
    private bool GroundBelow(Vector3 from, Vector3 up, float length, out RayHit hit)
    {
        if (Physics!.Ray(from, -up, length, out hit, groundOnly: true))
        {
            return true;
        }
        var aside = new Vector3(1e-3f, 0, 1e-3f);
        return Physics.Ray(from + aside, -up, length, out hit, groundOnly: true) || Physics.Ray(from - aside, -up, length, out hit, groundOnly: true);
    }

    // Where the ground is sampled under a foundation: its corners and its centre (RustMVP's).
    private IEnumerable<Vector3D> GroundSamples(PieceDef piece)
    {
        foreach (Vector3D corner in PieceGeometry.Footprint(piece.Shape, Data.Width))
        {
            yield return corner;
        }
        yield return Vector3D.Zero;
    }

    /// <summary>
    /// Every rule a placement must pass, in order: the building's size, reach, sight, building rights, a free spot, the
    /// ground under a foundation, collision with pieces, the ground and objects, characters, stability, two claims
    /// joined, and the price. Sets the plan's outcome and stability.
    /// </summary>
    public void Check(PlacementPlan plan, ulong player, Vector3 eye, IBuildingWallet? wallet)
    {
        plan.Outcome = CheckRules(plan, player, eye, wallet);
    }

    private Outcome CheckRules(PlacementPlan plan, ulong player, Vector3 eye, IBuildingWallet? wallet)
    {
        PieceDef piece = plan.Piece;
        BuildingFrame frame = plan.Frame;
        Pose pose = plan.Pose;
        if (plan.Structure != null && plan.Structure.Count >= Data.MaxPiecesPerBuilding)
        {
            return OutcomeCode.TooManyPieces;
        }
        if (plan.Support != null && (plan.Support.Gone || plan.Support.Collapsing))
        {
            return OutcomeCode.NotFound;
        }

        // Reach, to the nearest point of the piece's logical surface.
        Vector3D eyeLocal = frame.ToLocal(D(eye));
        Vector3D nearest = PieceGeometry.ClosestPoint(Data, piece, pose, eyeLocal);
        double distance = (nearest - eyeLocal).Length;
        if (distance > Data.Reach)
        {
            return OutcomeCode.OutOfReach;
        }

        // Sight: nothing else between the eye and that point (a little short of it is fine).
        if (Physics != null && distance > Data.SightTolerance)
        {
            Vector3 target = F(frame.ToZone(nearest));
            // A foundation is meant to sit partly in the ground, so the ground never hides one.
            if (Physics.Ray(eye, target - eye, (float)distance, out RayHit blocker) &&
                !(blocker.IsGround && piece.Family == PieceFamily.Foundation) &&
                blocker.Distance < distance - Data.SightTolerance && Vector3.Distance(blocker.Point, target) > Data.SightTolerance)
            {
                return OutcomeCode.OutOfSight;
            }
        }

        // Building rights where the piece goes.
        (Vector3 zonePosition, Quaternion zoneRotation) = frame.ToZone(pose);
        if (PrivilegeAt(player, zonePosition, Data.ClaimPadding) == Privilege.Blocked)
        {
            return OutcomeCode.BuildingBlocked;
        }

        // A free spot.
        if (plan.Structure != null)
        {
            foreach (Structure s in Lattice(frame))
            {
                if (s.At(piece, pose) != null)
                {
                    return OutcomeCode.Occupied;
                }
            }
        }

        // The ground under a foundation.
        if (piece.Family == PieceFamily.Foundation && Physics != null)
        {
            Outcome ground = CheckGround(piece, frame, pose);
            if (!ground.Ok)
            {
                return ground;
            }
        }

        // Collision.
        if (Physics != null)
        {
            Outcome collision = CheckCollision(piece, frame, pose, zonePosition, zoneRotation, ignore: null);
            if (!collision.Ok)
            {
                return collision;
            }
        }
        if (CharacterInside(_parts[piece], zonePosition, zoneRotation))
        {
            return OutcomeCode.PlayerInTheWay;
        }

        // Stability, and the structures it would join.
        _supports.Clear();
        int stability = StabilityAt(piece, frame, pose, _supports);
        plan.Stability = stability;
        if (stability < Data.MinimumStability)
        {
            return OutcomeCode.Unstable.With((int)Math.Floor(stability / 10.0));
        }
        if (plan.Structure != null && JoinsClaims(piece, frame, pose))
        {
            return OutcomeCode.JoinsClaims;
        }

        // The price: a twig piece's cost in its material.
        if (wallet != null)
        {
            GradeDef twig = Data.Grades[0];
            int cost = Data.CostOf(piece, 0);
            if (wallet.Count(twig.Material) < cost)
            {
                return OutcomeCode.NotEnough.With(Data.MaterialItem(twig.Material));
            }
        }
        return Outcome.Done;
    }

    // RustMVP's ground check: under every corner and the centre, ground within the foundation's depth, at least the
    // clearance below its top, and not too steep.
    private Outcome CheckGround(PieceDef piece, in BuildingFrame frame, in Pose pose)
    {
        foreach (Vector3D sample in GroundSamples(piece))
        {
            Vector3 top = F(frame.ToZone(PieceGeometry.ToFrame(pose, sample)));
            Vector3 up = Up(top);
            const float above = 10;
            if (!Physics!.Ray(top + up * above, -up, above + (float)Data.FoundationDepth, out RayHit ground, groundOnly: true))
            {
                return OutcomeCode.Floating;
            }
            double below = ground.Distance - above;
            if (below < Data.FoundationClearance)
            {
                return OutcomeCode.Buried;
            }
            if (Vector3.Dot(ground.Normal, up) < Data.GroundNormalMin)
            {
                return OutcomeCode.TooSteep;
            }
        }
        return Outcome.Done;
    }

    /// <summary>
    /// Whether a piece at a pose would cut into anything: other pieces (except the ones it joins through a socket or
    /// meets along a seam), the ground (foundations are meant to sit in it), doors, devices and natural objects.
    /// </summary>
    private Outcome CheckCollision(PieceDef piece, in BuildingFrame frame, in Pose pose, Vector3 position, Quaternion rotation, Piece? ignore)
    {
        Physics!.Overlaps(ShapeOf(piece), position, rotation, (float)Data.ContactTolerance, _overlaps);
        foreach (OverlapHit hit in _overlaps)
        {
            if (PhysicsWorld.IsGroundBody(hit.Body))
            {
                if (piece.Family == PieceFamily.Foundation)
                {
                    continue;
                }
                return OutcomeCode.HitsGround;
            }
            object? owner = Physics.OwnerOf(hit.Body);
            if (owner is Piece other)
            {
                if (other == ignore || other.Gone || Joined(piece, frame, pose, other))
                {
                    continue;
                }
                return OutcomeCode.HitsPiece;
            }
            if (owner is DoorLeaf leaf && ignore != null && leaf.Door.Frame == ignore)
            {
                continue;
            }
            return owner is DoorLeaf ? OutcomeCode.HitsPiece : OutcomeCode.Blocked;
        }
        return Outcome.Done;
    }

    // Pieces that touch by design: linked through a socket either way, or meeting along a seam. Only within one frame.
    private bool Joined(PieceDef piece, in BuildingFrame frame, in Pose pose, Piece other)
    {
        if (other.Collapsing)
        {
            return true;
        }
        if (!other.Structure.Frame.Equals(frame))
        {
            return false;
        }
        return Supports(other.Def, other.Pose, piece, pose, out _) || Supports(piece, pose, other.Def, other.Pose, out _) ||
            PieceGeometry.WallTileSeam(Data, piece, pose, other.Def, other.Pose) ||
            PieceGeometry.WallTileSeam(Data, other.Def, other.Pose, piece, pose) ||
            PieceGeometry.TileTileSeam(Data, piece, pose, other.Def, other.Pose) ||
            PieceGeometry.WallWallSeam(Data, piece, pose, other.Def, other.Pose);
    }

    // Whether a piece at a pose would link structures that both have a claim device.
    private bool JoinsClaims(PieceDef piece, in BuildingFrame frame, in Pose pose)
    {
        ClaimDevice? first = null;
        _near.Clear();
        foreach (Structure s in Lattice(frame))
        {
            s.Near(pose.Position, LinkRadius, _near);
        }
        foreach (Piece other in _near)
        {
            if (other.Gone || other.Collapsing || other.Structure.Claim == null)
            {
                continue;
            }
            if (Supports(other.Def, other.Pose, piece, pose, out _) || Supports(piece, pose, other.Def, other.Pose, out _))
            {
                if (first != null && first != other.Structure.Claim)
                {
                    return true;
                }
                first = other.Structure.Claim;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether any character's capsule reaches into convex parts at a zone pose. The capsule's axis is sampled every
    /// 10 cm; each sample is a sphere tested against the parts' face planes.
    /// </summary>
    internal bool CharacterInside(ConvexPart[] parts, Vector3 position, Quaternion rotation)
    {
        if (Characters == null)
        {
            return false;
        }
        Quaternion inverse = Quaternion.Inverse(rotation);
        foreach (Capsule c in Characters())
        {
            float length = c.Height - 2 * c.Radius;
            int samples = Math.Max(2, (int)MathF.Ceiling(length / 0.1f) + 1);
            for (int i = 0; i < samples; i++)
            {
                Vector3 zone = c.Feet + c.Up * (c.Radius + length * i / (samples - 1));
                Vector3 local = Vector3.Transform(zone - position, inverse);
                foreach (ConvexPart part in parts)
                {
                    if (local.X < part.Min.X - c.Radius || local.X > part.Max.X + c.Radius ||
                        local.Y < part.Min.Y - c.Radius || local.Y > part.Max.Y + c.Radius ||
                        local.Z < part.Min.Z - c.Radius || local.Z > part.Max.Z + c.Radius)
                    {
                        continue;
                    }
                    if (part.Outside(local) < c.Radius)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Places a planned piece, after checking it again (the server never trusts a client's plan): pays the twig cost,
    /// starts a structure for a free foundation, and adds the piece at full twig health.
    /// </summary>
    public Outcome Place(PlacementPlan plan, ulong player, Vector3 eye, IBuildingWallet wallet, double now, out Piece? piece)
    {
        piece = null;
        Check(plan, player, eye, wallet);
        if (!plan.Valid)
        {
            return plan.Outcome;
        }
        GradeDef twig = Data.Grades[0];
        if (!wallet.Take(twig.Material, Data.CostOf(plan.Piece, 0)))
        {
            return OutcomeCode.NotEnough.With(Data.MaterialItem(twig.Material));
        }
        piece = plan.Structure == null
            ? AddFirst(plan.Frame, plan.Piece, 0, player, now)
            : Add(plan.Structure, plan.Piece, 0, plan.Pose, player, now);
        return Outcome.Done;
    }

    /// <summary>
    /// The placement a client asks for by its support, socket and orientation (the wire's intent): the server derives
    /// the pose itself and checks everything.
    /// </summary>
    public PlacementPlan? FromIntent(PieceDef piece, Piece support, SocketId socket, int orientation)
    {
        if (support.Gone || !PieceGeometry.Snap(Data, support.Def, support.Pose, socket, piece, orientation, out Pose pose))
        {
            return null;
        }
        return new PlacementPlan
        {
            Piece = piece,
            Structure = support.Structure,
            Frame = support.Structure.Frame,
            Pose = pose,
            Support = support,
            Socket = socket,
            Orientation = orientation,
        };
    }

    /// <summary>A free foundation turns in steps of 15° (24 to the turn).</summary>
    public const int HeadingSteps = 24;

    public const double HeadingStep = Math.PI / 12;

    /// <summary>Planet up at a zone point (the zone frame's), or +Y without physics.</summary>
    public Vector3 Up(Vector3 zonePoint) => Physics != null ? Physics.World.Frame.UpAt(zonePoint) : Vector3.UnitY;

    internal static Vector3D D(Vector3 v) => new(v.X, v.Y, v.Z);

    internal static Vector3 F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
