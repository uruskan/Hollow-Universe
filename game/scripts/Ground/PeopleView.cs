using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Client.Audio;
using Hollow.Client.Feedback;
using Hollow.Simulation.Targets;
using HollowUniverse.Audio;
using HollowUniverse.Characters;
using HollowUniverse.Fx;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The other players (GROUND.md §4.8): each a <see cref="CharacterView"/> fed every frame from what the client
/// replicates (position, up, yaw, pitch, the velocity seen, stance, ground, the item in hand), with their swings and
/// shots, flinches when hurt, a ragdoll when they die, lying down asleep when logged off, and their clothing. Corpses
/// use the server's supported pose and saved appearance until its replicated phase becomes a backpack.
/// </summary>
public partial class PeopleView : Node3D
{
    private sealed class Person
    {
        public CharacterView View = null!;
        public Num.Vector3 Last;

        // Aboard a ship (IRON_WAKE.md §4.8): where it last stood in the ship's frame, and that frame.
        public Num.Vector3 LastLocal;
        public uint LastFrame;
        public Vector3 Velocity;
        public byte Actions;
        public byte Health = 255;
        public bool Down;
        public bool Dead;
        public int Frames;
        public string Name = "";
        public Hollow.Simulation.Entities.EntityId Id;
        public ushort[] Wear = Array.Empty<ushort>();
        public byte Body;
        public FootstepDriver? Feet;
        public readonly RemoteMeleeSwingCue SwingCue = new();
        public readonly ActionCuePlayer ReloadCues = new();
        public byte ReloadAction;
        public ushort ReloadItem;
        public string ReloadKey = "";
        public string? SwingCueSound;
        public FxHandle Flame;
        public Node3D? FlameSocket;
        public FootSurface Surface = FootSurface.Grass;
        public Num.Vector3 SurfaceAt = new(float.MaxValue);
        public uint SurfaceFrame = uint.MaxValue;
    }

    private sealed class Corpse
    {
        public CharacterView? View;
        public Node3D? Bag;
    }

    private readonly Dictionary<uint, Person> _people = new();
    private readonly Dictionary<uint, Corpse> _corpses = new();
    private readonly HashSet<uint> _seen = new();
    private ushort[] _myWear = Array.Empty<ushort>();
    private uint _wearRevision = uint.MaxValue;
    private Person? _ownPlayer;
    private uint _ownWear = uint.MaxValue;
    private ushort _ownHeld;
    private bool _ownReloading;

    public GameClient Client { get; set; } = null!;
    public RecentContactFeedback? Contacts { get; set; }

    /// <summary>
    /// Players to name: the point above their head, their name, how far, and whether they are in the player's team.
    /// Living, awake players near enough; teammates at any distance (through walls, as Rust's), those out of range
    /// where the server last said (G7, GROUND.md §6.8).
    /// </summary>
    public IEnumerable<(Vector3 Head, string Name, float Distance, bool Team)> Names(Vector3 eye)
    {
        foreach (Person p in _people.Values)
        {
            if (p.Down || !p.View.Visible)
            {
                continue;
            }
            bool team = Client.World().IsTeammate(p.Id);
            float distance = p.View.GlobalPosition.DistanceTo(eye);
            if (distance < 40 || team)
            {
                yield return (p.View.GlobalPosition + p.View.GlobalBasis.Y * 1.95f, p.Name, distance, team);
            }
        }
        foreach (TeammateView mate in Client.Local.Teammates)
        {
            if (mate.State is Hollow.Net.Protocol.TeammateState.Awake or Hollow.Net.Protocol.TeammateState.Down && !_people.Values.Any(p => p.Id == mate.Id))
            {
                Vector3 head = GroundView.ToGodot(mate.Position + Client.Frame.UpAt(mate.Position) * 1.95f);
                yield return (head, mate.Name, head.DistanceTo(eye), true);
            }
        }
    }

    /// <summary>Where a player's gun shows its muzzle (by net ID), when they are drawn holding one: their tracers start there.</summary>
    public Vector3? MuzzleOf(uint netId) =>
        _people.TryGetValue(netId, out Person? p) && GunEffects.MuzzleEnd(p.View.HeldModel) is { } end ? end.GlobalPosition : null;

    /// <summary>The character view of a player by name (for screenshots that face them).</summary>
    public CharacterView? ViewOf(string name) => _people.Values.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.View;

    public override void _Ready()
    {
        Client.Survival().LookChanged += OnLook;
        Client.Local.Gun.ShotFired += OnOwnShot;
        Client.Melee.Started += OnOwnSwing;
        Client.Melee.Cancelled += CancelOwnAction;
        Client.Local.DamageTaken += OnOwnDamage;
    }

    public override void _ExitTree()
    {
        Client.Survival().LookChanged -= OnLook;
        Client.Local.Gun.ShotFired -= OnOwnShot;
        Client.Melee.Started -= OnOwnSwing;
        Client.Melee.Cancelled -= CancelOwnAction;
        Client.Local.DamageTaken -= OnOwnDamage;
        RetireWorld();
    }

    /// <summary>Person/corpse IDs and animation history never carry into another world session.</summary>
    public void RetireWorld()
    {
        foreach (Person person in _people.Values)
        {
            Torch(person, null);
            person.SwingCue.Cancel();
            person.View.CancelActions();
            Release(person.View);
            if (person.Feet is { } feet)
            {
                if (feet.GetParent() == this) RemoveChild(feet);
                feet.QueueFree();
            }
        }
        foreach (Corpse corpse in _corpses.Values)
        {
            foreach (Node3D? node in new Node3D?[] { corpse.View, corpse.Bag })
                if (node != null)
                {
                    if (node.GetParent() == this) RemoveChild(node);
                    node.QueueFree();
                }
        }
        if (_ownPlayer is { } own)
        {
            Torch(own, null);
            own.View.CancelActions();
            Release(own.View);
            _ownPlayer = null;
        }
        _people.Clear(); _corpses.Clear(); _seen.Clear();
        _myWear = Array.Empty<ushort>();
        _wearRevision = _ownWear = uint.MaxValue;
        _ownHeld = 0; _ownReloading = false;
    }

    private void OnLook(RemoteEntity entity)
    {
        if (_people.TryGetValue(entity.NetId, out Person? p))
        {
            p.Wear = entity.Wear;
            p.View.Wear(ItemLooks.Outfit(Client.Content, entity.Wear));
            FrameEvents.Note("clothes");
        }
    }

    // Bodies kept for players who come into range: making one costs a frame (the character package builds its meshes
    // and animation graph), so those who leave give theirs back, and a few of each body are made while the world opens.
    private readonly Dictionary<BodyType, Stack<CharacterView>> _spare = new();

    /// <summary>Spare bodies made while the world opens, per body type.</summary>
    public const int SpareBodies = 4;

    /// <summary>Makes the spare bodies (call while the world opens: each costs tens of milliseconds).</summary>
    public void MakeSpares()
    {
        foreach (BodyType body in new[] { BodyType.Male, BodyType.Female })
        {
            Stack<CharacterView> spare = Spare(body);
            while (spare.Count < SpareBodies)
            {
                var view = new CharacterView { Name = "Spare", Body = body, Visible = false };
                AddChild(view);
                spare.Push(view);
            }
        }
    }

    private Stack<CharacterView> Spare(BodyType body)
    {
        if (!_spare.TryGetValue(body, out Stack<CharacterView>? stack))
        {
            _spare[body] = stack = new Stack<CharacterView>();
        }
        return stack;
    }

    private CharacterView NewView(RemoteEntity entity)
    {
        var body = (BodyType)entity.Body;
        if (Spare(body).TryPop(out CharacterView? kept))
        {
            kept.Name = "Player " + entity.Name;
            kept.Visible = true;
            return kept;
        }
        var view = new CharacterView { Name = "Player " + entity.Name, Body = body };
        AddChild(view);
        return view;
    }

    // A body back to the spares (standing again if it fell), or freed when there are enough.
    private void Release(CharacterView view)
    {
        Stack<CharacterView> spare = Spare(view.Body);
        if (spare.Count >= 2 * SpareBodies || !IsInstanceValid(view))
        {
            view.QueueFree();
            return;
        }
        if (view.IsDead)
        {
            view.Revive();
        }
        view.Visible = false;
        spare.Push(view);
    }

    public void Refresh(double delta)
    {
        uint wearRevision = Client.Local.Inventory.Wear.Revision;
        if (!Client.Local.IsDead && wearRevision != _wearRevision)
        {
            _wearRevision = wearRevision;
            _myWear = Client.Local.Inventory.Wear.Slots.ToArray().Select(s => s.Item).Where(i => i != 0).ToArray();
        }
        _seen.Clear();
        float dt = (float)Math.Max(delta, 1e-4);
        foreach (RemoteEntity entity in Client.Entities)
        {
            _seen.Add(entity.NetId);
            if (!_people.TryGetValue(entity.NetId, out Person? p))
            {
                p = new Person { View = NewView(entity), Last = entity.Position, Name = entity.Name, Body = entity.Body };
                FrameEvents.Note("character");
                p.Wear = entity.Wear;
                p.View.Wear(ItemLooks.Outfit(Client.Content, entity.Wear));
                _people.Add(entity.NetId, p);
            }
            p.Id = entity.Id;
            Update(p, entity, dt);
        }
        foreach (uint gone in _people.Keys.Where(k => !_seen.Contains(k)).ToList())
        {
            Torch(_people[gone], null);
            Release(_people[gone].View);
            _people[gone].Feet?.QueueFree();
            _people.Remove(gone);
        }
        UpdateCorpses();
    }

    /// <summary>The ships as drawn (seated people sit where their seats are drawn).</summary>
    public ShipsView? Ships { get; set; }

    /// <summary>Whether the local full body is currently drawn, for camera and runtime checks.</summary>
    public bool OwnPlayerVisible => _ownPlayer?.View.Visible == true;

    /// <summary>Compatibility for seat-only callers; all local body presentation goes through <see cref="ShowOwnPlayer"/>.</summary>
    public void ShowOwnSeat(bool show) => ShowOwnPlayer(show && Client.Ships().Seated, Num.Vector3.Zero, 0);

    /// <summary>
    /// The predicted local player in an inspection camera, or a seat's ordinary orbit camera. Facing is the player's
    /// aim in world space, never the orbit camera's direction. The same body, clothing and held models draw both views.
    /// </summary>
    public void ShowOwnPlayer(bool show, Num.Vector3 facing, float pitch)
    {
        if (!show || !Client.HasCharacter || Client.Local.IsDead)
        {
            HideOwnPlayer();
            return;
        }
        CharacterFrame frame;
        bool seated = Client.Ships().Seated;
        ItemDef? item = seated ? null : Client.Local.HeldItem;
        if (seated)
        {
            if (SeatFrame(Client.EntityId, 0, 0, 0, 0) is not { } seat)
            {
                HideOwnPlayer();
                return;
            }
            frame = seat;
            frame.LookYaw = 0;
        }
        else
        {
            CharacterState state = Client.Character;
            Num.Vector3 velocity = state.Velocity;
            if (state.Frame != 0)
                velocity = Client.Ships().DrawnPose((uint)state.Frame) is { } ship ? ship.DirectionToWorld(velocity) : Num.Vector3.Zero;
            Vector3 up = GroundView.ToGodot(Client.RenderUp);
            bool swimming = (state.Flags & MoveFlags.Swimming) != 0;
            (HeldKind kind, string? model) = item != null && ItemLooks.Held(item.Key) is { } held
                ? held : (HeldKind.Unarmed, (string?)null);
            frame = new CharacterFrame
            {
                Position = GroundView.ToGodot(Client.RenderPosition),
                Up = up,
                Yaw = CharacterFrame.YawToward(up, GroundView.ToGodot(facing)),
                Pitch = pitch,
                Velocity = GroundView.ToGodot(velocity),
                Stance = Client.Local.IsWounded || state.Stance == Stance.Crawling ? CharacterStance.Crawl
                    : swimming ? CharacterStance.Swim : state.Stance == Stance.Crouching ? CharacterStance.Crouch : CharacterStance.Stand,
                Grounded = (state.Flags & MoveFlags.OnGround) != 0 || swimming,
                Held = kind,
                HeldItem = model,
                Aiming = Client.Local.Gun.Aimed,
            };
        }
        var body = (BodyType)Client.Body;
        if (_ownPlayer != null && _ownPlayer.View.Body != body)
        {
            Torch(_ownPlayer, null);
            Release(_ownPlayer.View);
            _ownPlayer = null;
        }
        if (_ownPlayer == null)
        {
            CharacterView view;
            if (!Spare(body).TryPop(out view!))
            {
                view = new CharacterView { Body = body };
                AddChild(view);
            }
            view.Name = "Local player";
            _ownPlayer = new Person { View = view, Body = Client.Body, Id = Client.EntityId };
            _ownWear = uint.MaxValue;
        }
        CharacterView own = _ownPlayer.View;
        if (_ownWear != Client.Local.Inventory.Wear.Revision)
        {
            _ownWear = Client.Local.Inventory.Wear.Revision;
            own.Wear(ItemLooks.Outfit(Client.Content, _myWear));
        }
        bool wasVisible = own.Visible;
        own.Visible = true;
        own.Apply(frame);
        ushort heldItem = item?.Id ?? 0;
        if (!wasVisible || _ownHeld != heldItem || seated || Client.Local.IsWounded)
        {
            own.CancelActions();
            _ownReloading = false;
        }
        _ownHeld = heldItem;
        if (item?.Firearm != null && frame.HeldItem != null && own.HeldModel is { } gun)
            GunEffects.Dress(Client.Content, gun, frame.HeldItem, Client.Local.Gun.Mods.Bits);
        bool reloading = !seated && !Client.Local.IsWounded && Client.Local.Gun.Reloading;
        if (reloading && !_ownReloading && item != null)
            PlayOwnAction(item, "reload", Client.Local.Gun.ReloadProgress);
        else if (!reloading && _ownReloading) own.CancelActions();
        _ownReloading = reloading;
        string? flame = item != null ? HollowUniverse.Items.ModelCatalog.Find(item.Key)?.Held?.Flame : null;
        Torch(_ownPlayer, flame != null ? GunEffects.Socket(own.HeldModel, "flame") : null, flame ?? "fire.torch");
    }

    private void HideOwnPlayer()
    {
        if (_ownPlayer != null)
        {
            if (_ownPlayer.View.Visible) _ownPlayer.View.CancelActions();
            _ownPlayer.View.Visible = false;
            Torch(_ownPlayer, null);
        }
        _ownReloading = false;
    }

    private void OnOwnShot(ShotEvent shot)
    {
        if (OwnPlayerVisible && !Client.Ships().Seated && !Client.Local.IsWounded)
            _ownPlayer!.View.Trigger(CharacterEvent.Fire);
    }

    private void OnOwnSwing(ItemDef item) => PlayOwnAction(item, "swing");

    private void PlayOwnAction(ItemDef item, string action, float phase = 0)
    {
        if (!OwnPlayerVisible || Client.Ships().Seated || Client.Local.IsWounded || HeldItems.Find(item.Key) == null ||
            !HeldPresentations.All.TryGetValue(item.Key, out HeldPresentationProfile? profile) ||
            !profile.Actions.TryGetValue(action, out HeldPresentationClip? clip)) return;
        _ownPlayer!.View.PlayItemAction(item.Key, action, HeldActionTiming.Duration(item, action, clip.Duration), phase);
    }

    private void CancelOwnAction() => _ownPlayer?.View.CancelActions();

    private void OnOwnDamage(DamageEvent damage)
    {
        if (!OwnPlayerVisible || Client.Local.IsDead) return;
        Vector3 direction = GroundView.ToGodot(Client.RenderPosition - damage.From).Normalized();
        _ownPlayer!.View.Trigger(CharacterEvent.Hit, direction);
    }

    // A seated character's frame (IRON_WAKE.md §9.6): the hips on the seat as its ship is drawn (a gunner's seat on its
    // turning mount), facing the seat's way with the head following the view; a station's gunner with both hands on its
    // grips (the data file's), a helm's pilot on the controls in front of the seat, a passenger's hands in the lap.
    private CharacterFrame? SeatedFrame(RemoteEntity e, float viewYaw) => SeatFrame(e.Id, e.Pitch, viewYaw, e.LookYaw, e.LookPitch);

    private CharacterFrame? SeatFrame(Hollow.Simulation.Entities.EntityId id, float pitch, float viewYaw, float lookYaw, float lookPitch)
    {
        if (Ships == null)
        {
            return null;
        }
        foreach (RemoteShip ship in Client.Ships().All)
        {
            int seat = ship.SeatOf(id);
            if (seat < 0 || ship.Def is not { } def || Ships.ViewOf(ship.NetId) is not { } view || seat >= def.Seats.Count)
            {
                continue;
            }
            Hollow.Simulation.Ships.ShipSeatDef s = def.Seats[seat];
            Transform3D at = view.SocketWorld(s.At);
            Vector3 up = at.Basis.Y.Normalized(), forward = at.Basis.Z.Normalized();
            float yaw = CharacterFrame.YawToward(up, forward);
            var frame = new CharacterFrame
            {
                Position = at.Origin,
                Seat = at.Origin,
                Up = up,
                Yaw = yaw,
                Pitch = pitch,
                LookYaw = Mathf.AngleDifference(yaw, viewYaw) + lookYaw,
                LookPitch = lookPitch,
                Stance = CharacterStance.Sit,
                Grounded = true,
            };
            if (def.StationOfSeat(seat) is { } station && def.Interior?.StationData.FirstOrDefault(g => g.Id == station.Id) is { GripLeft: { } l, GripRight: { } r })
            {
                frame.Grips = true;
                frame.GripLeft = view.SocketWorld(l);
                frame.GripRight = view.SocketWorld(r);
            }
            else if (s.Flies)
            {
                // A helm without grips of its own: the controls in front of the seat, at the hands' height (+X is the left).
                frame.Grips = true;
                frame.GripLeft = at * new Transform3D(Basis.Identity, new Vector3(0.2f, 0.32f, 0.42f));
                frame.GripRight = at * new Transform3D(Basis.Identity, new Vector3(-0.2f, 0.32f, 0.42f));
            }
            return frame;
        }
        return null;
    }

    private void Update(Person p, RemoteEntity e, float dt)
    {
        if (p.Body != e.Body && !p.Down)
        {
            p.SwingCue.Cancel();
            // Another body: a new view (a view's body is chosen when it is made), dressed as before.
            Release(p.View);
            p.View = NewView(e);
            p.View.Wear(ItemLooks.Outfit(Client.Content, p.Wear));
            p.Body = e.Body;
            p.Frames = 0;
        }
        p.Frames++;
        p.Dead = (e.Flags & MoveFlags.Dead) != 0;
        // Up where it stands as drawn (aboard a ship, its deck's: IRON_WAKE.md §4.8), and how it moves there: aboard, its
        // walk on the deck (the ship's own motion isn't a walk), turned as the ship is drawn.
        Num.Vector3 up = e.Up;
        Num.Vector3 moved = (e.Position - p.Last) / dt;
        if (e.Frame != 0)
        {
            moved = e.Frame == p.LastFrame && Client.Ships().DrawnPose(e.Frame) is { } ship
                ? ship.DirectionToWorld((e.LocalPosition - p.LastLocal) / dt) : Num.Vector3.Zero;
        }
        else if (p.LastFrame != 0)
        {
            moved = Num.Vector3.Zero; // just stepped off: one frame without a measure
        }
        Vector3 seen = GroundView.ToGodot(moved);
        // Facing where it looks, in the character view's own convention (its north is not the movement code's on a ball).
        float facing = CharacterFrame.YawToward(GroundView.ToGodot(up), GroundView.ToGodot(e.View));
        p.Velocity = p.Frames < 3 ? Vector3.Zero : p.Velocity.Lerp(seen, 0.3f);
        p.Last = e.Position;
        p.LastLocal = e.LocalPosition;
        p.LastFrame = e.Frame;
        bool swimming = (e.Flags & MoveFlags.Swimming) != 0;
        p.View.Visible = true;
        (HeldKind kind, string? model) = e.Held != 0 && Client.Content.Item(e.Held) is { } item && ItemLooks.Held(item.Key) is { } held
            ? held : (HeldKind.Unarmed, (string?)null);
        // In a ship's seat (IRON_WAKE.md §9.6): drawn sitting at the seat as the ship is drawn, every ship's.
        if (e.Stance == Stance.Seated && SeatedFrame(e, facing) is { } seated)
        {
            p.SwingCue.Cancel();
            p.ReloadCues.Cancel();
            p.View.Apply(seated);
            // Nothing in hand, no steps; what it did meanwhile isn't replayed when it stands up.
            Torch(p, null);
            p.Actions = e.ActionCount;
            p.Health = e.Health;
            p.Feet?.Reset();
            return;
        }
        p.View.Apply(new CharacterFrame
        {
            Position = GroundView.ToGodot(e.Position),
            Up = GroundView.ToGodot(up),
            Yaw = facing,
            Pitch = e.Pitch,
            LookYaw = e.LookYaw,
            LookPitch = e.LookPitch,
            Velocity = p.Velocity,
            // Crawling (down, G4) has no pose in the character contract yet: crouched is the nearest.
            Stance = e.Sleeping ? CharacterStance.Sleep : e.Wounded ? CharacterStance.Crawl : swimming ? CharacterStance.Swim : e.Stance == Stance.Crouching ? CharacterStance.Crouch : CharacterStance.Stand,
            Grounded = (e.Flags & MoveFlags.OnGround) != 0 || swimming,
            Held = kind,
            HeldItem = model,
            Aiming = e.Aiming,
        });
        if (model != null && e.Held != 0 && Client.Content.Item(e.Held)?.Firearm != null && p.View.HeldModel is { } theirGun)
        {
            // Their gun's attachments (HeldMods), on the model in their hands.
            GunEffects.Dress(Client.Content, theirGun, model, e.HeldMods);
        }
        if (p.Frames < 3)
        {
            // Let it take a pose before anything happens to it.
            p.Actions = e.ActionCount;
            p.Health = e.Health;
            return;
        }
        bool dead = (e.Flags & MoveFlags.Dead) != 0;
        bool down = dead || e.Sleeping;
        Vector3 head = GroundView.ToGodot(e.Position + up * 1.5f);
        Torch(p, !down && model == "torch" ? GunEffects.Socket(p.View.HeldModel, "flame") : null);
        if (!down)
        {
            Steps(p, e, up, swimming, dt);
        }
        else p.Feet?.Reset();
        if (down != p.Down)
        {
            p.Down = down;
            if (dead)
            {
                Sounds.At("survival.death", head);
            }
            if (down)
            {
                // Dead: pushed back from where they faced; asleep: they just lie down.
                Vector3 push = dead ? -CharacterFrame.Facing(GroundView.ToGodot(up), facing) * 1.5f : Vector3.Zero;
                p.View.Trigger(CharacterEvent.Death, push);
                FrameEvents.Note("ragdoll");
            }
            else
            {
                p.View.Revive();
            }
        }
        p.Dead = dead;
        if (!down)
        {
            if (e.ActionCount != p.Actions)
            {
                p.SwingCue.Cancel();
                ItemDef? heldItem = Client.Content.Item(e.ActionItem != 0 ? e.ActionItem : e.Held);
                HeldPresentationProfile? profile = heldItem != null && HeldItems.Find(heldItem.Key) != null ? HeldPresentations.Get(heldItem.Key) : null;
                string? action = e.Action switch
                {
                    ActionKind.Swing => "swing",
                    ActionKind.Reload => "reload",
                    ActionKind.Draw => "draw",
                    ActionKind.Throw => "throw",
                    ActionKind.Use => profile?.UseAction,
                    ActionKind.Repair => "repair",
                    _ => null,
                };
                if (action != null && heldItem != null && profile?.Actions.TryGetValue(action, out HeldPresentationClip? clip) == true)
                {
                    float duration = HeldActionTiming.Duration(heldItem, action, clip.Duration);
                    float phase = (float)Math.Max(0, e.RenderTick - e.ActionTick) / CharacterTicks.Rate / duration;
                    if (e.Action is ActionKind.Throw or ActionKind.Use or ActionKind.Repair)
                        phase += HeldActionTiming.CommitFraction(action, duration);
                    if (phase < 1f) p.View.PlayItemAction(heldItem.Key, action, duration, phase);
                }
                switch (e.Action)
                {
                    case ActionKind.None:
                        p.View.CancelActions();
                        break;
                    case ActionKind.Shoot:
                        p.View.Trigger(CharacterEvent.Fire);
                        if (heldItem?.Firearm != null)
                        {
                            Shot(p, e, heldItem, up);
                        }
                        break;
                    case ActionKind.Reload:
                        if (heldItem?.Firearm is { } gun)
                        {
                            p.ReloadAction = e.ActionCount;
                            p.ReloadItem = heldItem.Id;
                            p.ReloadKey = heldItem.Key;
                            p.ReloadCues.Start(ContactFeedback.Actions.For("reload"), HeldActionTiming.Duration(heldItem, "reload", HeldActionTiming.Seconds(gun.ReloadTicks)),
                                Math.Max(0, e.RenderTick - e.ActionTick) / CharacterTicks.Rate);
                        }
                        break;
                    case ActionKind.Draw:
                        Sounds.At("bow.draw", head);
                        break;
                    case ActionKind.Swing:
                        if (heldItem?.Melee != null)
                        {
                            p.SwingCueSound = Sounds.Item(heldItem.Key, "swing") ?? "melee.swing";
                            p.SwingCue.Start(e, heldItem);
                        }
                        break;
                    case ActionKind.Throw:
                        Sounds.At(heldItem != null ? Sounds.Item(heldItem.Key, "throw") : null, head);
                        break;
                    case ActionKind.Use:
                        Sounds.At(heldItem != null ? Sounds.Item(heldItem.Key, profile?.UseAction ?? "use") ?? Sounds.Item(heldItem.Key, "use") : null, head);
                        break;
                    case ActionKind.Repair:
                        Sounds.At(heldItem != null ? Sounds.Item(heldItem.Key, "repair") : null, head);
                        break;
                }
            }
            if (e.Health < p.Health && e.Health > 0)
            {
                Vector3 front = CharacterFrame.Facing(GroundView.ToGodot(up), facing);
                p.View.Trigger(CharacterEvent.Hit, -front);
                Sounds.At("survival.hurt", head);
                // A blow or a bite bleeds here; a bullet's blood was shown where it hit.
                Vector3 chest = GroundView.ToGodot(e.Position + up * 1.25f);
                if (Contacts?.Contains(new TargetRef(TargetFamily.Character, p.Id.Value), Client.ClockSeconds) != true)
                {
                    FxDirector.Play("blood.hit", chest + front * 0.15f, front, new FxParams { Direction = front });
                }
            }
        }
        if (p.SwingCue.Step(Client, e, !down))
            Sounds.At(p.SwingCueSound, head);
        p.ReloadCues.Advance(Math.Max(0, e.RenderTick - e.ActionTick) / CharacterTicks.Rate,
            !down && !e.Wounded && e.Stance != Stance.Seated && e.ActionCount == p.ReloadAction &&
            e.Action == ActionKind.Reload && e.Held == p.ReloadItem,
            role => Sounds.At(Sounds.Item(p.ReloadKey, role), head));
        p.Actions = e.ActionCount;
        p.Health = e.Health;
    }

    private int _silencers = -1;

    // A torch in their hand burns (the effects library's flame on its flame socket), out when they put it away.
    private static void Torch(Person p, Node3D? socket, string effect = "fire.torch")
    {
        if (socket == p.FlameSocket)
        {
            return;
        }
        if (p.Flame.IsValid)
        {
            FxDirector.Stop(p.Flame);
            p.Flame = default;
        }
        p.FlameSocket = socket;
        if (socket != null)
        {
            p.Flame = FxDirector.Start(effect, socket.GlobalPosition, Vector3.Up, new FxParams { Follow = socket });
        }
    }

    // Another player's shot (G4): the gun's sound at its muzzle (layered by distance, arriving at the speed of sound)
    // and its muzzle effect on the gun in their hands (silenced, braked or plain; a rocket's, with its back blast).
    private void Shot(Person p, RemoteEntity e, ItemDef gun, Num.Vector3 up)
    {
        if (_silencers < 0)
        {
            _silencers = Client.Content.Items.Where(i => i.Attachment is { Silenced: true }).Aggregate(0, (m, i) => m | 1 << i.Attachment!.Index);
        }
        bool silenced = (e.HeldMods & _silencers) != 0;
        Num.Vector3 eye = e.Position + up * (e.Stance == Stance.Crouching ? 1.0f : 1.55f);
        Num.Vector3 muzzle = eye + CharacterMotor.ViewDirection(e.Yaw, e.Pitch, up) * 0.75f;
        Vector3 at = GroundView.ToGodot(muzzle);
        Sounds.At(Sounds.Item(gun.Key, silenced ? "shotSilenced" : "shot"), at);
        string key = GunEffects.MuzzleKey(Client.Content, gun.Key, e.HeldMods);
        Node3D? held = p.View.HeldModel;
        if (GunEffects.MuzzleEnd(held) is { } end)
        {
            FxDirector.Muzzle(key, end, firstPerson: false);
        }
        else
        {
            Vector3 direction = GroundView.ToGodot(CharacterMotor.ViewDirection(e.Yaw, e.Pitch, up));
            FxDirector.Play(key, at, direction, new FxParams { Direction = direction });
        }
        if (FxCatalog.ItemEffect(gun.Key, "backblast") is { } backblast && GunEffects.Socket(held, "exhaust") is { } exhaust)
        {
            // A launcher's back blast out of its venturi (+X of the socket points backwards): the gun's own (fx.json items).
            Vector3 back = exhaust.GlobalBasis.X.Normalized();
            FxDirector.Play(backblast, exhaust.GlobalPosition, back, new FxParams { Direction = back });
        }
    }

    // Their footsteps in zone space, using deck-relative velocity and the surface directly underfoot.
    private void Steps(Person p, RemoteEntity e, Num.Vector3 up, bool swimming, float dt)
    {
        if (p.Feet == null)
        {
            p.Feet = new FootstepDriver { Name = "Feet " + e.Name };
            AddChild(p.Feet);
        }
        if (e.Frame != p.SurfaceFrame || Num.Vector3.Distance(e.Position, p.SurfaceAt) > 0.6f)
        {
            p.SurfaceAt = e.Position;
            p.SurfaceFrame = e.Frame;
            p.Surface = GroundAudio.SurfaceAt(Client, e.Position, e.Frame, e.LocalPosition);
        }
        Vector3 upG = GroundView.ToGodot(up);
        float vertical = p.Velocity.Dot(upG);
        float speed = (p.Velocity - upG * vertical).Length();
        p.Feet.Update(dt, new FootInput
        {
            Speed = speed,
            VerticalSpeed = vertical,
            Grounded = (e.Flags & MoveFlags.OnGround) != 0,
            Crouching = e.Stance is Stance.Crouching or Stance.Crawling,
            Swimming = swimming,
            Sprinting = speed > 4.5f,
            Suppressed = e.Stance == Stance.Seated || (e.Flags & MoveFlags.Dead) != 0,
            Surface = p.Surface,
            FootwearProfile = SoundLibrary.Footwear.Profile(Client.Content, e.Wear),
            WaterDepth = e.Frame == 0 ? GroundAudio.WaterDepth(Client, e.Position) : 0,
        }, GroundView.ToGodot(e.Position));
    }

    // The server owns support, phase and death-time appearance. A stable prone pose stays on Jolt building floors
    // and moving decks; a client-only ragdoll cannot collide with the server's building collision world.
    private void UpdateCorpses()
    {
        var things = new HashSet<uint>();
        foreach (Person person in _people.Values)
        {
            // Dead characters can remain replicated after their body expired. Their only visible representation is
            // the authoritative corpse, including for somebody who joins after it has already gone.
            if (person.Dead) person.View.Visible = false;
        }
        foreach (RemoteThing thing in Client.Survival().Things)
        {
            if (thing.Kind != StaticKind.Corpse)
            {
                continue;
            }
            things.Add(thing.NetId);
            if (!_corpses.TryGetValue(thing.NetId, out Corpse? known))
            {
                _corpses[thing.NetId] = known = new Corpse();
                FrameEvents.Note("corpse");
            }
            if (thing.IsBody)
            {
                if (known.View == null)
                {
                    known.Bag?.QueueFree();
                    known.Bag = null;
                    var body = (BodyType)thing.CorpseBody;
                    known.View = new CharacterView { Name = "Corpse " + thing.Name, Body = body,
                        Look = Looks.ForPlayer(thing.CorpseOwner, body), FootPlacement = false, BlinkOverride = 1 };
                    AddChild(known.View);
                    known.View.Wear(ItemLooks.Outfit(Client.Content, thing.CorpseWear));
                }
                (Num.Vector3 forward, _) = Facing.Basis(thing.Yaw, thing.Up);
                Vector3 up = GroundView.ToGodot(thing.Up);
                known.View.Apply(new CharacterFrame
                {
                    Position = GroundView.ToGodot(thing.Position), Up = up,
                    Yaw = CharacterFrame.YawToward(up, GroundView.ToGodot(forward)),
                    Stance = CharacterStance.Sleep, Grounded = true,
                });
            }
            else
            {
                known.View?.QueueFree();
                known.View = null;
                known.Bag ??= Bag(thing);
                if (known.Bag != null)
                    known.Bag.Transform = new Transform3D(NatureView.Upright(thing.Up, thing.Yaw), GroundView.ToGodot(thing.Position));
            }
        }
        foreach (uint gone in _corpses.Keys.Where(k => !things.Contains(k)).ToList())
        {
            _corpses[gone].View?.QueueFree();
            _corpses[gone].Bag?.QueueFree();
            _corpses.Remove(gone);
        }
    }

    // The backpack a body leaves, where it lies.
    private Node3D? Bag(RemoteThing thing)
    {
        if (GroundModels.Provider.Backpack() is not { } bag)
        {
            return null;
        }
        bag.Transform = new Transform3D(NatureView.Upright(thing.Up, thing.Yaw), GroundView.ToGodot(thing.Position));
        AddChild(bag);
        return bag;
    }
}
