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
        public Vector3 Velocity;
        public byte Actions;
        public byte Health = 255;
        public bool Down;
        public bool Dead;
        public int Frames;
        public string Name = "";
        public ushort[] Wear = Array.Empty<ushort>();
        public byte Body;
        public FootstepDriver? Feet;
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

    /// <summary>Living, awake players near enough to name: the point above their head and their name.</summary>
    public IEnumerable<(Vector3 Head, string Name, float Distance)> Names(Vector3 eye) =>
        _people.Values.Where(p => !p.Down && p.View.Visible)
            .Select(p => (p.View.GlobalPosition + p.View.GlobalBasis.Y * 1.95f, p.Name, p.View.GlobalPosition.DistanceTo(eye)))
            .Where(n => n.Item3 < 40);

    /// <summary>The character view of a player by name (for screenshots that face them).</summary>
    public CharacterView? ViewOf(string name) => _people.Values.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.View;

    public override void _Ready() => Client.LookChanged += OnLook;

    public override void _ExitTree() => Client.LookChanged -= OnLook;

    private void OnLook(RemoteEntity entity)
    {
        if (_people.TryGetValue(entity.NetId, out Person? p))
        {
            p.Wear = entity.Wear;
            p.View.Wear(ItemLooks.Outfit(Client.Content, entity.Wear));
            FrameEvents.Note("clothes");
        }
    }

    private CharacterView NewView(RemoteEntity entity)
    {
        var view = new CharacterView { Name = "Player " + entity.Name, Body = (BodyType)entity.Body };
        AddChild(view);
        return view;
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
            Update(p, entity, dt);
        }
        foreach (uint gone in _people.Keys.Where(k => !_seen.Contains(k)).ToList())
        {
            _people[gone].View.QueueFree();
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
            p.View.QueueFree();
            p.View = NewView(e);
            p.View.Wear(ItemLooks.Outfit(Client.Content, p.Wear));
            p.Body = e.Body;
            p.Frames = 0;
        }
        p.Frames++;
        Num.Vector3 up = Client.Frame.UpAt(e.Position);
        Vector3 seen = GroundView.ToGodot((e.Position - p.Last) / dt);
        p.Velocity = p.Frames < 3 ? Vector3.Zero : p.Velocity.Lerp(seen, 0.3f);
        p.Last = e.Position;
        bool swimming = (e.Flags & MoveFlags.Swimming) != 0;
        (HeldKind kind, string? model) = e.Held != 0 && Client.Content.Item(e.Held) is { } item && ItemLooks.Held(item.Key) is { } held
            ? held : (HeldKind.Unarmed, (string?)null);
        p.View.Apply(new CharacterFrame
        {
            Position = GroundView.ToGodot(e.Position),
            Up = GroundView.ToGodot(up),
            Yaw = e.Yaw,
            Pitch = e.Pitch,
            Velocity = p.Velocity,
            // Crawling (down, G4) has no pose in the character contract yet: crouched is the nearest.
            Stance = swimming ? CharacterStance.Swim : e.Stance is Stance.Crouching or Stance.Crawling ? CharacterStance.Crouch : CharacterStance.Stand,
            Grounded = (e.Flags & MoveFlags.OnGround) != 0 || swimming,
            Held = kind,
            HeldItem = model,
        });
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
                Vector3 push = dead ? -CharacterFrame.Facing(GroundView.ToGodot(up), e.Yaw) * 1.5f : Vector3.Zero;
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
                            Shot(e, heldItem, up);
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
                p.View.Trigger(CharacterEvent.Hit, -CharacterFrame.Facing(GroundView.ToGodot(up), e.Yaw));
                Sounds.At("survival.hurt", head);
            }
        }
        p.Actions = e.ActionCount;
        p.Health = e.Health;
    }

    private int _silencers = -1;

    // Another player's shot (G4): the gun's sound at its muzzle (layered by distance, arriving at the speed of sound)
    // and, unless silenced, a flash.
    private void Shot(RemoteEntity e, ItemDef gun, Num.Vector3 up)
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
        if (!silenced)
        {
            ShotEffects.Provider.MuzzleFlash(this, at, GroundView.ToGodot(CharacterMotor.ViewDirection(e.Yaw, e.Pitch, up)));
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
        foreach (RemoteThing thing in Client.Things)
        {
            if (thing.Kind != StaticKind.Corpse)
            {
                continue;
            }
            things.Add(thing.NetId);
            if (_corpses.TryGetValue(thing.NetId, out Corpse? known))
            {
                if (known.View != null && now - known.Since > BodySeconds)
                {
                    known.View.QueueFree();
                    known.View = null;
                    known.Bag = Bag(thing);
                }
                continue;
            }
            Vector3 at = GroundView.ToGodot(thing.Position);
            if (Num.Vector3.Distance(thing.Position, Client.Character.Position) > BodyDistance)
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
        Num.Vector3 up = Client.Frame.UpAt(thing.Position);
        bag.Transform = new Transform3D(NatureView.Upright(up, thing.Yaw), GroundView.ToGodot(thing.Position));
        AddChild(bag);
        return bag;
    }
}
