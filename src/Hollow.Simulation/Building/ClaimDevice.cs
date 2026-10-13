using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.Building;

/// <summary>
/// The claim device, our tool cupboard (GROUND.md §6.3): it stands on a piece of a structure, lists the players
/// authorised to build there, gives that structure an area of building rights, and holds the materials that pay its
/// upkeep. One per connected structure.
/// </summary>
public sealed class ClaimDevice : IBuildingPart, IOwned
{
    internal readonly List<ulong> AuthorisedList = new();
    internal uint Body;

    internal ClaimDevice(EntityId id, ulong owner, Piece support, Vector3D position, int yaw, double health)
    {
        Id = id;
        Owner = owner;
        Support = support;
        Position = position;
        Yaw = yaw;
        Health = health;
        AuthorisedList.Add(owner);
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Cupboard, Id.Value);

    /// <summary>The player who placed it (authorised from the start).</summary>
    public ulong Owner { get; }

    ulong IOwned.OwnerId => Owner;

    /// <summary>The foundation or floor it stands on; it falls with it.</summary>
    public Piece Support { get; }

    public Structure Structure => Support.Structure;

    /// <summary>Where it stands, in its structure's frame.</summary>
    public Vector3D Position { get; }

    public int Yaw { get; }

    public bool Gone { get; internal set; }

    /// <summary>A claim device is its own base.</summary>
    public ClaimDevice? Base => this;

    public IReadOnlyList<ulong> Authorised => AuthorisedList;

    public bool IsAuthorised(ulong player) => AuthorisedList.Contains(player);

    /// <summary>Its Jolt body's ID.</summary>
    public uint BodyId => Body;

    /// <summary>
    /// The upkeep store: whole items by material key. A dictionary of its own until the zone gives the device its
    /// container (the loot panel's), which then pays the upkeep.
    /// </summary>
    public IBuildingWallet Store { get; set; } = new Wallet();

    public double Health { get; internal set; }

    public double LastDamagedAt { get; internal set; } = double.NegativeInfinity;

    public (Vector3 Position, Quaternion Rotation) ZoneTransform => Structure.Frame.ToZone(new Pose(Position, Yaw));
}

public sealed partial class BuildingWorld
{
    /// <summary>Every claim device in the world.</summary>
    public IEnumerable<ClaimDevice> Claims => _structures.Where(s => s.Claim != null).Select(s => s.Claim!);

    /// <summary>
    /// Places a claim device on the top of a foundation or floor (<paramref name="localPoint"/> in its structure's frame),
    /// turned by <paramref name="yaw"/> 30° steps: one per structure, on the slab's footprint. Its owner is authorised.
    /// Reach, sight, building rights and a clear space are the placement pipeline's (GROUND_SYSTEMS.md §6).
    /// </summary>
    public Outcome PlaceClaim(ulong player, Piece support, Vector3D localPoint, int yaw, out ClaimDevice? device)
    {
        device = null;
        if (support.Gone || support.Collapsing)
        {
            return OutcomeCode.NotFound;
        }
        if (!support.Def.IsSlab)
        {
            return OutcomeCode.NotOnStructure;
        }
        if (support.Structure.Claim != null)
        {
            return OutcomeCode.HasClaim;
        }
        var position = new Vector3D(localPoint.X, support.Pose.Position.Y, localPoint.Z);
        Vector3D onPiece = PieceGeometry.ToLocal(support.Pose, position);
        if (PieceGeometry.ClosestOnFootprint(support.Def.Shape, Data.Width, onPiece) is var inside &&
            (inside - new Vector3D(onPiece.X, 0, onPiece.Z)).LengthSquared > 1e-6)
        {
            return OutcomeCode.NotOnStructure;
        }
        var pose = new Pose(position, PieceGeometry.Wrap(yaw));
        (Vector3 zonePosition, Quaternion zoneRotation) = support.Structure.Frame.ToZone(pose);
        ConvexPart[] parts = { PieceShapes.Claim(Data) };
        device = new ClaimDevice(_newId(), player, support, position, pose.Yaw, Data.ClaimHealth);
        support.Structure.Claim = device;
        _claimIds[device.Id] = device;
        if (Physics != null)
        {
            device.Body = Physics.Add(device, Physics.ShapeFor(Data.ClaimItem, parts), zonePosition, zoneRotation);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimAdded, support, Claim: device, Structure: support.Structure));
        return Outcome.Done;
    }

    internal void RemoveClaim(ClaimDevice device, Removal reason = Removal.Taken)
    {
        if (device.Gone)
        {
            return;
        }
        device.Gone = true;
        _claimIds.Remove(device.Id);
        Physics?.Remove(device.Body, device);
        Structure structure = device.Support.Structure;
        if (structure.Claim == device)
        {
            structure.Claim = null;
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimRemoved, device.Support, Claim: device, Structure: structure, Removal: reason));
    }

    /// <summary>
    /// Damages a claim device through its protection (building.json's claim: a grade's hard side). At zero it breaks:
    /// the structure loses its building rights and upkeep, and the zone spills its store.
    /// </summary>
    public DamageResult Damage(ClaimDevice device, in DamageHit hit, double now)
    {
        if (device.Gone)
        {
            return default;
        }
        double loss = Resolve(Data.ClaimProtection.Hard, hit, device.Health);
        if (loss <= 0)
        {
            return default;
        }
        device.Health -= loss;
        device.LastDamagedAt = now;
        bool destroyed = device.Health <= 0;
        if (destroyed)
        {
            RemoveClaim(device, Removal.Destroyed);
        }
        else
        {
            Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, device.Support, Claim: device, Structure: device.Structure));
        }
        return new DamageResult(loss, false, destroyed);
    }

    /// <summary>Takes a claim device down (by an authorised player): the structure loses its building rights and upkeep.</summary>
    public Outcome PickUpClaim(ulong player, ClaimDevice device)
    {
        if (device.Gone)
        {
            return OutcomeCode.NotFound;
        }
        if (!device.IsAuthorised(player))
        {
            return OutcomeCode.NotAuthorised;
        }
        RemoveClaim(device);
        return Outcome.Done;
    }

    /// <summary>Adds a player to a device's list (anyone may, as in Rust; the list has a limit).</summary>
    public Outcome Authorise(ulong player, ClaimDevice device)
    {
        if (device.Gone)
        {
            return OutcomeCode.NotFound;
        }
        if (!device.IsAuthorised(player))
        {
            if (device.AuthorisedList.Count >= Data.ClaimMaxAuthorised)
            {
                return OutcomeCode.ListFull;
            }
            device.AuthorisedList.Add(player);
            Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, device.Support, Claim: device, Structure: device.Structure));
        }
        return Outcome.Done;
    }

    /// <summary>Takes a player off a device's list.</summary>
    public Outcome Deauthorise(ulong player, ClaimDevice device)
    {
        if (device.AuthorisedList.Remove(player))
        {
            Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, device.Support, Claim: device, Structure: device.Structure));
        }
        return Outcome.Done;
    }

    /// <summary>Clears a device's list; only someone on it may.</summary>
    public Outcome ClearAuthorised(ulong player, ClaimDevice device)
    {
        if (!device.IsAuthorised(player))
        {
            return OutcomeCode.NotAuthorised;
        }
        device.AuthorisedList.Clear();
        Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, device.Support, Claim: device, Structure: device.Structure));
        return Outcome.Done;
    }

    // ---------------------------------------------------------------- building rights

    /// <summary>
    /// Building rights at a zone point for a player: a claimed structure covers the points within
    /// <see cref="BuildingData.ClaimPieceRadius"/> of any of its pieces or <see cref="BuildingData.ClaimDeviceRadius"/>
    /// of its device, plus <paramref name="padding"/>, so the area grows with the base and never leaks into a structure
    /// it doesn't touch. The player may build only where every covering device lists them (RustMVP's strict rule:
    /// Rust's twig-floor exception for strangers isn't kept).
    /// </summary>
    public Privilege PrivilegeAt(ulong player, Vector3 point, double padding = 0)
    {
        bool covered = false;
        foreach (Structure s in _structures)
        {
            ClaimDevice? claim = s.Claim;
            if (claim == null || !Covers(s, claim, point, padding))
            {
                continue;
            }
            if (!claim.IsAuthorised(player))
            {
                return Privilege.Blocked;
            }
            covered = true;
        }
        return covered ? Privilege.Allowed : Privilege.None;
    }

    private bool Covers(Structure structure, ClaimDevice claim, Vector3 point, double padding)
    {
        Vector3D local = structure.Frame.ToLocal(D(point));
        double deviceRadius = Data.ClaimDeviceRadius + padding;
        if ((local - claim.Position).LengthSquared <= deviceRadius * deviceRadius)
        {
            return true;
        }
        _near.Clear();
        structure.Near(local, Data.ClaimPieceRadius + padding, _near);
        return _near.Count > 0;
    }

    // ---------------------------------------------------------------- upkeep and decay

    /// <summary>
    /// Rust's upkeep bracket rate for a structure of <paramref name="pieces"/> pieces: the first bracket's pieces pay its
    /// fraction of their cost a period, the next bracket's theirs, and so on; the rate is the average.
    /// </summary>
    public double UpkeepRate(int pieces)
    {
        if (pieces <= 0 || Data.UpkeepBrackets.Count == 0)
        {
            return Data.UpkeepBrackets.Count > 0 ? Data.UpkeepBrackets[0].Fraction : 0;
        }
        int left = pieces;
        double paid = 0;
        for (int i = 0; i < Data.UpkeepBrackets.Count && left > 0; i++)
        {
            (int count, double fraction) = Data.UpkeepBrackets[i];
            int n = i == Data.UpkeepBrackets.Count - 1 || count <= 0 ? left : Math.Min(left, count);
            paid += n * fraction;
            left -= n;
        }
        return paid / pieces;
    }

    /// <summary>A structure's upkeep for one period (24 hours), by material: Σ cost of each piece at its grade × the bracket rate.</summary>
    public Dictionary<string, double> UpkeepPerPeriod(Structure structure)
    {
        var upkeep = new Dictionary<string, double>(StringComparer.Ordinal);
        int count = structure.PieceSet.Count(p => !p.Collapsing);
        double rate = UpkeepRate(count);
        foreach (Piece p in structure.PieceSet)
        {
            if (p.Collapsing)
            {
                continue;
            }
            string material = Data.Grades[p.Grade].Material;
            upkeep[material] = upkeep.GetValueOrDefault(material) + Data.CostOf(p.Def, p.Grade) * rate;
        }
        return upkeep;
    }

    /// <summary>
    /// How long the device's store keeps the whole structure from decaying, in seconds: the shortest time any material
    /// it needs lasts (infinity when nothing needs paying); 0 without a device.
    /// </summary>
    public double ProtectedSeconds(Structure structure)
    {
        ClaimDevice? claim = structure.Claim;
        if (claim == null)
        {
            return 0;
        }
        double period = Data.UpkeepPeriodHours * 3600, best = double.PositiveInfinity;
        foreach ((string material, double perPeriod) in UpkeepPerPeriod(structure))
        {
            if (perPeriod <= 0)
            {
                continue;
            }
            double credit = claim.Store.Count(material) - structure.UpkeepDebt.GetValueOrDefault(material);
            best = Math.Min(best, Math.Max(0, credit) / (perPeriod / period));
        }
        return best;
    }

    /// <summary>
    /// Applies <paramref name="seconds"/> of upkeep and decay to every structure in closed form (ARCHITECTURE.md §10.4):
    /// work in proportion to the events in the interval, never to its length. Between events everything is linear:
    /// each material is paid from the device's store at its rate until it runs out, and every piece whose material
    /// isn't paid (or that has no device) loses its full health over its grade's decay time. Events are a material
    /// running out and a piece reaching zero (it's removed and what it held comes down at once); after each the rates
    /// are worked out again. Called with a long interval when a region loads, and with short ones while it's live:
    /// the two agree within rounding (a test checks).
    /// </summary>
    public void AdvanceTime(double seconds, double now)
    {
        if (!(seconds > 0) || Mirror)
        {
            return;
        }
        _advanced.Clear();
        AdvanceAll(seconds, now);
        // What decayed or paid upkeep is announced once, after the whole interval.
        foreach (object changed in _advanced)
        {
            switch (changed)
            {
                case Piece { Gone: false } p:
                    Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, p, Structure: p.Structure));
                    break;
                case ClaimDevice { Gone: false } c:
                    Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, c.Support, Claim: c, Structure: c.Structure));
                    break;
            }
        }
        _advanced.Clear();
    }

    private readonly HashSet<object> _advanced = new();

    private void AdvanceAll(double seconds, double now)
    {
        var queue = new Queue<(Structure Structure, double Seconds)>(_structures.Select(s => (s, seconds)));
        while (queue.Count > 0)
        {
            (Structure structure, double left) = queue.Dequeue();
            int known = _nextStructure;
            double used = Advance(structure, left, now);
            if (used >= left)
            {
                continue;
            }
            // A removal split the structure: every part carries on from that moment.
            if (_structures.Contains(structure))
            {
                queue.Enqueue((structure, left - used));
            }
            foreach (Structure s in _structures.Where(s => s.Number >= known))
            {
                queue.Enqueue((s, left - used));
            }
        }
    }

    // Advances one structure until the time is used up, or until a removal split it (returns the time used then).
    private double Advance(Structure structure, double seconds, double now)
    {
        double t = 0, period = Data.UpkeepPeriodHours * 3600;
        var rates = new Dictionary<string, double>(StringComparer.Ordinal);
        var unpaid = new HashSet<string>(StringComparer.Ordinal);
        var dead = new List<Piece>();
        for (int events = 0; t < seconds && structure.Count > 0 && _structures.Contains(structure) && events < 1_000_000; events++)
        {
            ClaimDevice? claim = structure.Claim;
            rates.Clear();
            foreach ((string material, double perPeriod) in UpkeepPerPeriod(structure))
            {
                rates[material] = perPeriod / period;
            }
            // Which materials are paid during this interval is settled at its start: one that runs out at its end
            // still paid for all of it.
            unpaid.Clear();
            foreach ((string material, double rate) in rates)
            {
                if (rate > 0 && (claim == null ||
                    claim.Store.Count(material) - structure.UpkeepDebt.GetValueOrDefault(material) <= 1e-9))
                {
                    unpaid.Add(material);
                }
            }
            bool Paid(string material) => !unpaid.Contains(material);

            // The next event.
            double dt = seconds - t;
            if (claim != null)
            {
                foreach ((string material, double rate) in rates)
                {
                    if (rate > 0 && Paid(material))
                    {
                        double credit = claim.Store.Count(material) - structure.UpkeepDebt.GetValueOrDefault(material);
                        dt = Math.Min(dt, credit / rate);
                    }
                }
            }
            foreach (Piece p in structure.PieceSet)
            {
                if (!p.Collapsing && !Paid(Data.Grades[p.Grade].Material))
                {
                    dt = Math.Min(dt, p.Health / DecayRate(p));
                }
            }
            dt = Math.Max(0, dt);

            // Everything linear up to it.
            if (claim != null)
            {
                foreach ((string material, double rate) in rates)
                {
                    if (rate <= 0 || !Paid(material))
                    {
                        continue;
                    }
                    double debt = structure.UpkeepDebt.GetValueOrDefault(material) + rate * dt;
                    int stock = claim.Store.Count(material);
                    int whole = (int)Math.Min(stock, Math.Floor(debt + 1e-9));
                    if (whole > 0 && claim.Store.Take(material, whole))
                    {
                        _advanced.Add(claim);
                    }
                    debt -= whole;
                    // Run out: what's left of the debt is rounding.
                    structure.UpkeepDebt[material] = stock - whole <= 0 && debt < 1e-6 ? 0 : Math.Max(0, debt);
                }
            }
            dead.Clear();
            foreach (Piece p in structure.PieceSet)
            {
                if (p.Collapsing || Paid(Data.Grades[p.Grade].Material))
                {
                    continue;
                }
                if (dt > 0)
                {
                    _advanced.Add(p);
                }
                p.Health -= DecayRate(p) * dt;
                if (p.Health <= 1e-9 * p.MaxHealth)
                {
                    dead.Add(p);
                }
            }
            t += dt;
            if (dead.Count > 0)
            {
                int known = _nextStructure;
                foreach (Piece p in dead)
                {
                    p.Health = 0;
                    Remove(p, now + t, immediate: true, Removal.Decayed);
                }
                if (_nextStructure != known)
                {
                    return t;
                }
            }
        }
        return seconds;
    }

    /// <summary>Health a piece loses per second while its upkeep isn't paid.</summary>
    public double DecayRate(Piece piece) => piece.MaxHealth / (Data.Grades[piece.Grade].DecayHours * 3600);
}
