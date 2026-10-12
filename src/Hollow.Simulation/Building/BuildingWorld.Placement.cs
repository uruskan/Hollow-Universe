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
    /// A foundation on open ground: along the aim, at most <see cref="BuildingData.FreeFoundationDistance"/> away
    /// across the ground, square to the player's view (15° steps, R turns it 90°), its top just above the highest
    /// ground under it. It starts a new structure whose frame has the planet's up there.
    /// </summary>
    private PlacementPlan? FreeFoundation(ulong player, PieceDef piece, Vector3 eye, Vector3 direction, int turns, IBuildingWallet? wallet)
    {
        if (Physics == null)
        {
            return null;
        }
        Vector3 up = Up(eye);
        Vector3 flat = direction - up * Vector3.Dot(direction, up);
        if (flat.LengthSquared() < 1e-6f)
        {
            return null;
        }
        flat = Vector3.Normalize(flat);
        float maxDistance = (float)Data.FreeFoundationDistance;
        Vector3 centre;
        if (Physics.Ray(eye, direction, (float)Data.Reach, out RayHit hit) && hit.IsGround)
        {
            Vector3 across = hit.Point - eye;
            float horizontal = (across - up * Vector3.Dot(across, up)).Length();
            centre = horizontal <= maxDistance ? hit.Point : eye + flat * maxDistance;
        }
        else
        {
            centre = eye + flat * maxDistance;
        }
        // The ground under that point.
        if (!Physics.Ray(centre + up * 10, -up, 30, out RayHit ground, groundOnly: true))
        {
            return null;
        }
        centre = ground.Point;
        up = Up(centre);
        Vector3D upD = D(up);
        BuildingFrame level = BuildingFrame.At(D(centre), upD, 0);
        Vector3D forward = level.DirectionToLocal(D(flat));
        double heading = Math.Atan2(-forward.X, -forward.Z) + turns * Math.PI / 2;
        heading = Math.Round(heading / (Math.PI / 12)) * (Math.PI / 12);
        BuildingFrame probe = BuildingFrame.At(D(centre), upD, heading);
        // Raise its top just above the highest ground under it.
        double highest = double.MinValue;
        foreach (Vector3D sample in GroundSamples(piece))
        {
            Vector3 point = F(probe.ToZone(sample));
            if (Physics.Ray(point + up * 10, -up, 10 + (float)(Data.FoundationDepth + 10), out RayHit h, groundOnly: true))
            {
                highest = Math.Max(highest, 10 - h.Distance);
            }
        }
        if (highest == double.MinValue)
        {
            return null;
        }
        BuildingFrame frame = BuildingFrame.At(D(centre) + upD * (highest + Data.FoundationClearance + 0.02), upD, heading);
        var plan = new PlacementPlan { Piece = piece, Structure = null, Frame = frame, Pose = new Pose(Vector3D.Zero, 0) };
        Check(plan, player, eye, wallet);
        return plan;
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
            return Outcome.Refuse(Refusal.TooManyPieces);
        }
        if (plan.Support != null && (plan.Support.Gone || plan.Support.Collapsing))
        {
            return Outcome.Refuse(Refusal.Gone);
        }

        // Reach, to the nearest point of the piece's logical surface.
        Vector3D eyeLocal = frame.ToLocal(D(eye));
        Vector3D nearest = PieceGeometry.ClosestPoint(Data, piece, pose, eyeLocal);
        double distance = (nearest - eyeLocal).Length;
        if (distance > Data.Reach)
        {
            return Outcome.Refuse(Refusal.TooFar);
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
                return Outcome.Refuse(Refusal.OutOfSight);
            }
        }

        // Building rights where the piece goes.
        (Vector3 zonePosition, Quaternion zoneRotation) = frame.ToZone(pose);
        if (PrivilegeAt(player, zonePosition, Data.ClaimPadding) == Privilege.Blocked)
        {
            return Outcome.Refuse(Refusal.BuildingBlocked);
        }

        // A free spot.
        if (plan.Structure != null)
        {
            foreach (Structure s in Lattice(frame))
            {
                if (s.At(piece, pose) != null)
                {
                    return Outcome.Refuse(Refusal.Occupied);
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
            return Outcome.Refuse(Refusal.PlayerInTheWay);
        }

        // Stability, and the structures it would join.
        _supports.Clear();
        int stability = StabilityAt(piece, frame, pose, _supports);
        plan.Stability = stability;
        if (stability < Data.MinimumStability)
        {
            return Outcome.Refuse(Refusal.Unstable, $"Not stable enough ({stability / 10.0:0.#}%)");
        }
        if (plan.Structure != null && JoinsClaims(piece, frame, pose))
        {
            return Outcome.Refuse(Refusal.JoinsClaims);
        }

        // The price: a twig piece's cost in its material.
        if (wallet != null)
        {
            GradeDef twig = Data.Grades[0];
            int cost = Data.CostOf(piece, 0);
            if (wallet.Count(twig.Material) < cost)
            {
                return Outcome.Refuse(Refusal.NotEnough, $"Not enough {Data.MaterialName(twig.Material).ToLowerInvariant()}");
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
                return Outcome.Refuse(Refusal.Floating);
            }
            double below = ground.Distance - above;
            if (below < Data.FoundationClearance)
            {
                return Outcome.Refuse(Refusal.Buried);
            }
            if (Vector3.Dot(ground.Normal, up) < Data.GroundNormalMin)
            {
                return Outcome.Refuse(Refusal.TooSteep);
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
                return Outcome.Refuse(Refusal.HitsGround);
            }
            object? owner = Physics.OwnerOf(hit.Body);
            if (owner is Piece other)
            {
                if (other == ignore || other.Gone || Joined(piece, frame, pose, other))
                {
                    continue;
                }
                return Outcome.Refuse(Refusal.HitsPiece);
            }
            if (owner is DoorLeaf leaf && ignore != null && leaf.Door.Frame == ignore)
            {
                continue;
            }
            return Outcome.Refuse(owner is DoorLeaf ? Refusal.HitsPiece : Refusal.HitsObject);
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
            return Outcome.Refuse(Refusal.NotEnough, $"Not enough {Data.MaterialName(twig.Material).ToLowerInvariant()}");
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

    /// <summary>Planet up at a zone point (the zone frame's), or +Y without physics.</summary>
    public Vector3 Up(Vector3 zonePoint) => Physics != null ? Physics.World.Frame.UpAt(zonePoint) : Vector3.UnitY;

    internal static Vector3D D(Vector3 v) => new(v.X, v.Y, v.Z);

    internal static Vector3 F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
