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
using HollowUniverse.Audio;
using HollowUniverse.Characters;
using HollowUniverse.Fx;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The other players (GROUND.md §4.8): each a <see cref="CharacterView"/> fed every frame from what the client
/// replicates (position, up, yaw, pitch, the velocity seen, stance, ground, the item in hand), with their swings and
/// shots, flinches when hurt, a ragdoll when they die, lying down asleep when logged off, and their clothing. Corpses
/// are ragdolls too: a player's own body when they died in view, a fresh one when the corpse comes into range later;
/// after <see cref="BodySeconds"/>, or when first seen far away, only the body's backpack is drawn.
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
        public FxHandle Flame;
        public Node3D? FlameSocket;
        public FootSurface Surface = FootSurface.Grass;
        public Num.Vector3 SurfaceAt = new(float.MaxValue);
    }

    private sealed class Corpse
    {
        public CharacterView? View;
        public Node3D? Bag;
        public int Frames;
        public bool Fallen;
        public double Since;
    }

    /// <summary>How long a body lies in view before only its backpack is left (Rust's corpse turning into a bag).</summary>
    public const double BodySeconds = 150;

    /// <summary>A corpse first seen farther than this is drawn as its backpack at once (no body to make).</summary>
    public const float BodyDistance = 80;

    private readonly Dictionary<uint, Person> _people = new();
    private readonly Dictionary<uint, Corpse> _corpses = new();
    private readonly HashSet<uint> _seen = new();
    private ushort[] _myWear = Array.Empty<ushort>();
    private uint _wearRevision = uint.MaxValue;

    public GameClient Client { get; set; } = null!;

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

    public override void _Ready() => Client.Survival().LookChanged += OnLook;

    public override void _ExitTree() => Client.Survival().LookChanged -= OnLook;

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

    private void Update(Person p, RemoteEntity e, float dt)
    {
        if (p.Body != e.Body && !p.Down)
        {
            // Another body: a new view (a view's body is chosen when it is made), dressed as before.
            Release(p.View);
            p.View = NewView(e);
            p.View.Wear(ItemLooks.Outfit(Client.Content, p.Wear));
            p.Body = e.Body;
            p.Frames = 0;
        }
        p.Frames++;
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
        // In a ship's seat (G6): the character contract has no sitting pose, so the pilot isn't drawn (§11).
        p.View.Visible = e.Stance != Stance.Seated;
        (HeldKind kind, string? model) = e.Held != 0 && Client.Content.Item(e.Held) is { } item && ItemLooks.Held(item.Key) is { } held
            ? held : (HeldKind.Unarmed, (string?)null);
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
            Stance = swimming ? CharacterStance.Swim : e.Stance is Stance.Crouching or Stance.Crawling ? CharacterStance.Crouch : CharacterStance.Stand,
            Grounded = (e.Flags & MoveFlags.OnGround) != 0 || swimming,
            Held = kind,
            HeldItem = model,
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
                ItemDef? heldItem = e.Held != 0 ? Client.Content.Item(e.Held) : null;
                switch (e.Action)
                {
                    case ActionKind.Shoot:
                        p.View.Trigger(CharacterEvent.Fire);
                        if (heldItem?.Firearm != null)
                        {
                            Shot(p, e, heldItem, up);
                        }
                        break;
                    case ActionKind.Reload:
                        p.View.Trigger(CharacterEvent.Reload);
                        if (heldItem?.Firearm is { } gun)
                        {
                            ReloadSounds(heldItem.Key, (float)gun.ReloadSeconds, head);
                        }
                        break;
                    case ActionKind.Draw:
                        Sounds.At("bow.draw", head);
                        break;
                    default:
                        p.View.Trigger(CharacterEvent.Swing);
                        Sounds.At(heldItem != null ? Sounds.Item(heldItem.Key, "swing") ?? "melee.swing" : "melee.swing", head);
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
                if (!GunEffects.FleshShown(chest, Client.ClockSeconds))
                {
                    FxDirector.Play("blood.hit", chest + front * 0.15f, front, new FxParams { Direction = front });
                }
            }
        }
        p.Actions = e.ActionCount;
        p.Health = e.Health;
    }

    private int _silencers = -1;

    // A torch in their hand burns (the effects library's flame on its flame socket), out when they put it away.
    private static void Torch(Person p, Node3D? socket)
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
            p.Flame = FxDirector.Start("fire.torch", socket.GlobalPosition, Vector3.Up, new FxParams { Follow = socket });
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

    // A reload's parts as they come: magazine out, in, the slide or bolt.
    private void ReloadSounds(string gun, float seconds, Vector3 at)
    {
        Sounds.At(Sounds.Item(gun, "magOut"), at);
        GetTree().CreateTimer(seconds * 0.55).Timeout += () => Sounds.At(Sounds.Item(gun, "magIn"), at);
        GetTree().CreateTimer(seconds * 0.85).Timeout += () => Sounds.At(Sounds.Item(gun, "rack"), at);
    }

    // Their footsteps, at their feet, on the ground under them (looked up again every 4 m).
    private void Steps(Person p, RemoteEntity e, Num.Vector3 up, bool swimming, float dt)
    {
        if (p.Feet == null)
        {
            p.Feet = new FootstepDriver { Name = "Feet " + e.Name };
            AddChild(p.Feet);
        }
        if (Num.Vector3.Distance(e.Position, p.SurfaceAt) > 4)
        {
            p.SurfaceAt = e.Position;
            p.Surface = GroundAudio.SurfaceAt(Client, e.Position);
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
            Surface = p.Surface,
            WaterDepth = GroundAudio.WaterDepth(Client, e.Position),
        }, GroundView.ToGodot(e.Position));
    }

    // Corpses: a dead player's ragdoll becomes their corpse; a corpse that came into range on its own gets a body that
    // falls where it lies; an old or far one is its backpack.
    private void UpdateCorpses()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var things = new HashSet<uint>();
        foreach (RemoteThing thing in Client.Survival().Things)
        {
            if (thing.Kind != StaticKind.Corpse)
            {
                continue;
            }
            things.Add(thing.NetId);
            if (_corpses.TryGetValue(thing.NetId, out Corpse? known))
            {
                // A body aboard a ship (IRON_WAKE.md §4.10) is its backpack, where the ship is drawn this frame.
                if (thing.Aboard && known.Bag != null)
                {
                    known.Bag.Transform = new Transform3D(NatureView.Upright(thing.Up, thing.Yaw), GroundView.ToGodot(thing.Position));
                }
                if (known.View != null && now - known.Since > BodySeconds)
                {
                    known.View.QueueFree();
                    known.View = null;
                    known.Bag = Bag(thing);
                }
                continue;
            }
            Vector3 at = GroundView.ToGodot(thing.Position);
            // Far, or aboard a ship (a ragdoll doesn't ride a deck): its backpack.
            if (thing.Aboard || Num.Vector3.Distance(thing.Position, Client.Character.Position) > BodyDistance)
            {
                _corpses[thing.NetId] = new Corpse { Bag = Bag(thing), Fallen = true, Since = now };
                continue;
            }
            Person? died = _people.Values.FirstOrDefault(p => p.Dead && p.View.GlobalPosition.DistanceTo(at) < 3f && string.Equals(p.Name, thing.Name, StringComparison.Ordinal));
            if (died != null)
            {
                // Adopt the falling body; the player gets a new one when they come back.
                uint key = _people.First(kv => kv.Value == died).Key;
                _people.Remove(key);
                _corpses[thing.NetId] = new Corpse { View = died.View, Fallen = true, Since = now };
                continue;
            }
            Person? owner = _people.Values.FirstOrDefault(p => p.Name == thing.Name);
            byte body = thing.Name == Client.Name ? Client.Body : owner?.Body ?? 0;
            var view = new CharacterView { Name = "Corpse " + thing.Name, Body = (BodyType)body };
            AddChild(view);
            ushort[] wear = thing.Name == Client.Name ? _myWear : owner?.Wear ?? Array.Empty<ushort>();
            view.Wear(ItemLooks.Outfit(Client.Content, wear));
            Num.Vector3 up = Client.Frame.UpAt(thing.Position);
            view.Apply(CharacterFrame.Standing(at, GroundView.ToGodot(up), thing.Yaw));
            _corpses[thing.NetId] = new Corpse { View = view, Since = now };
        }
        foreach ((uint id, Corpse corpse) in _corpses)
        {
            if (!corpse.Fallen && ++corpse.Frames >= 2)
            {
                corpse.Fallen = true;
                corpse.View?.Trigger(CharacterEvent.Death, Vector3.Zero);
                FrameEvents.Note("corpse");
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
