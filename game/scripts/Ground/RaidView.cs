using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;
using HollowUniverse.Audio;
using HollowUniverse.Common;
using HollowUniverse.Fx;
using HollowUniverse.Items;
using G = HollowUniverse.Common.Ui.Ground;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Raiding in the ground game (GROUND.md §6.6): explosives drawn from the model catalogue on their arcs, stuck to what
/// they hit or lying where they came to rest; a timed charge's lamp blinking faster as its fuse runs down (and beeping
/// with it), a bag charge's or tin grenade's fuse sputtering and hissing; their throws, bounces, duds and relighting
/// heard; detonations through the effects library (flash, fireball, smoke, debris, scorch and the camera's shake) with
/// the blast's sound layered by distance; rockets drawn with their smoke trail; and raid alerts.
/// </summary>
public partial class RaidView : Node3D
{
    private sealed class Shown
    {
        public Node3D Node = null!;
        public Node3D? FuseSocket;
        public MeshInstance3D? Lamp;
        public OmniLight3D? Light;
        public FxHandle Fuse;
        public SoundHandle FuseSound;
        public double Blink;
        public bool On;
        public Vector3 Spin;
        public uint Revision;
        public ExplosiveState State;
        public bool Dud;
        // The arc before the latest bounce, drawn until the drawing's clock reaches the bounce.
        public Num.Vector3 PrevStart, PrevVelocity, PrevGravity;
        public uint PrevTick;
        public bool HasPrev;
    }

    private readonly Dictionary<EntityId, Shown> _shown = new();
    private readonly Dictionary<ClientProjectile, (Node3D Node, FxHandle Trail)> _rockets = new();
    private readonly HashSet<EntityId> _seen = new();
    private readonly List<EntityId> _gone = new();
    private readonly List<ClientProjectile> _rocketsGone = new();
    private static StandardMaterial3D? _lampMaterial;
    private readonly Random _random = new(0xB00);
    private int _awaySeen, _awayShown;
    private double _awaySince;

    public GameClient Client { get; set; } = null!;

    /// <summary>A timed charge's lamp is lit now (scripted pictures wait for it).</summary>
    public bool LampLit => _shown.Values.Any(s => s.Lamp?.Visible == true);

    /// <summary>Raid alerts, live and those that waited while the player was away.</summary>
    public RaidAlertsPanel Alerts { get; set; } = null!;

    public override void _Ready()
    {
        Client.Raid().Detonated += OnDetonated;
        Client.Raid().ExplosiveRemoved += OnRemoved;
        Client.Local.RaidAlertReceived += OnAlert;
    }

    public override void _ExitTree()
    {
        Client.Raid().Detonated -= OnDetonated;
        Client.Raid().ExplosiveRemoved -= OnRemoved;
        Client.Local.RaidAlertReceived -= OnAlert;
    }

    /// <summary>Every frame: the explosives in range, rockets in flight, the alerts that waited.</summary>
    public void Refresh(double delta)
    {
        // Explosives are drawn on the clock the other players are drawn on (a bounce arrives before it is drawn).
        double tick = Client.RenderTick;
        _seen.Clear();
        foreach (ClientExplosive e in Client.Raid().Explosives)
        {
            _seen.Add(e.Id);
            if (!_shown.TryGetValue(e.Id, out Shown? shown))
            {
                shown = Make(e);
                _shown[e.Id] = shown;
            }
            else if (shown.Revision != e.Revision)
            {
                Changed(shown, e);
            }
            Place(shown, e, tick, delta);
        }
        _gone.Clear();
        foreach (EntityId id in _shown.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }
        foreach (EntityId id in _gone)
        {
            Remove(id);
        }
        Rockets();
        AwayAlerts();
    }

    // At login, the alerts that waited while the player was away (they come with the join, one message): shown together
    // half a second after the last of them came.
    private void AwayAlerts()
    {
        int away = 0;
        foreach (RaidAlertView a in Client.Local.RaidAlerts)
        {
            if (a.WhileAway)
            {
                away++;
            }
        }
        if (away != _awaySeen)
        {
            _awaySeen = away;
            _awaySince = Client.ClockSeconds;
            return;
        }
        if (away > _awayShown && Client.ClockSeconds - _awaySince > 0.5)
        {
            Alerts.ShowAway(Client.Local.RaidAlerts.Where(a => a.WhileAway).Skip(_awayShown)
                .Select(a => (a.What + (a.Item != null ? $" ({a.Item.Name})" : ""), Details(a))).ToList());
            Sounds.Flat("ui.notify");
            _awayShown = away;
        }
    }

    // ---------------------------------------------------------------- explosives

    private Shown Make(ClientExplosive e)
    {
        var shown = new Shown { Node = new Node3D { Name = "Explosive " + e.Id }, Revision = e.Revision, State = e.State, Dud = e.Dud };
        Node3D? model = ModelCatalog.Instantiate(e.Item.Key);
        if (model != null)
        {
            shown.Node.AddChild(model);
            shown.FuseSocket = model.FindChild("socket_fuse", true, false) as Node3D;
        }
        else
        {
            shown.Node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.08f, 0.12f) }, Position = new Vector3(0, 0.04f, 0) });
        }
        // Each model lies on its bottom (+Y out of the surface). A timed charge carries a lamp on top that blinks.
        if (Sounds.Item(e.Item.Key, "beep") != null)
        {
            Aabb bounds = model != null ? ModelCatalog.LocalBounds(model) : new Aabb(new Vector3(-0.09f, 0, -0.06f), new Vector3(0.18f, 0.08f, 0.12f));
            _lampMaterial ??= LampMaterial();
            Vector3 top = new(bounds.GetCenter().X, bounds.End.Y + 0.004f, bounds.GetCenter().Z);
            shown.Lamp = new MeshInstance3D
            {
                Name = "Lamp",
                Mesh = new SphereMesh { Radius = 0.011f, Height = 0.016f, RadialSegments = 10, Rings = 5 },
                MaterialOverride = _lampMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = top,
                Visible = false,
            };
            shown.Node.AddChild(shown.Lamp);
            shown.Light = new OmniLight3D
            {
                LightColor = new Color(1f, 0.16f, 0.1f),
                LightEnergy = Glow.FireLight * 1.5f,
                OmniRange = 3f,
                Position = top + new Vector3(0, 0.03f, 0),
                Visible = false,
            };
            shown.Node.AddChild(shown.Light);
        }
        float spin = Sounds.Item(e.Item.Key, "bounce") != null ? 11 : 5;
        shown.Spin = new Vector3((float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f, (float)_random.NextDouble() - 0.5f).Normalized() * spin;
        AddChild(shown.Node);
        // Thrown or placed just now (not one that came into range): heard where it left the hand or where it was put.
        bool fresh = Client.ServerTickNow - e.StateTick < GameClient.TickRate * 0.6;
        bool mine = e.Owner == Client.NetId && Client.NetId != 0;
        if (fresh && !mine)
        {
            Sounds.At(Sounds.Item(e.Item.Key, e.State == ExplosiveState.Flying ? "throw" : "place"), GroundView.ToGodot(e.ArcStart));
        }
        else if (fresh && e.State != ExplosiveState.Flying)
        {
            Sounds.At(Sounds.Item(e.Item.Key, "place"), GroundView.ToGodot(e.ArcStart));
        }
        return shown;
    }

    // The server sent it again: a bounce, sticking, coming to rest, a dud, lit again.
    private void Changed(Shown shown, ClientExplosive e)
    {
        Vector3 at = GroundView.ToGodot(e.ArcStart);
        if (shown.State == ExplosiveState.Flying && shown.PrevTick != 0)
        {
            // Keep drawing the arc it was on (kept by Place) until the drawing's clock reaches the bounce.
            shown.HasPrev = true;
        }
        if (e.State == ExplosiveState.Flying && shown.State == ExplosiveState.Flying || e.State == ExplosiveState.Resting && shown.State == ExplosiveState.Flying)
        {
            Sounds.At(Sounds.Item(e.Item.Key, "bounce"), at, -4);
        }
        else if (e.State == ExplosiveState.Stuck && shown.State == ExplosiveState.Flying)
        {
            Sounds.At(Sounds.Item(e.Item.Key, "place"), at, -4);
        }
        if (e.Dud && !shown.Dud)
        {
            // The fuse went out: a sputter of sparks and a puff of smoke.
            FxDirector.Play("explosion.dud", at, GroundView.ToGodot(e.Normal));
            Sounds.At(Sounds.Item(e.Item.Key, "fizzle"), at);
        }
        else if (!e.Dud && shown.Dud)
        {
            Sounds.At(Sounds.Item(e.Item.Key, "relight"), at);
        }
        shown.Revision = e.Revision;
        shown.State = e.State;
        shown.Dud = e.Dud;
    }

    private void Place(Shown shown, ClientExplosive e, double tick, double delta)
    {
        Num.Vector3 at;
        bool flying = e.State == ExplosiveState.Flying;
        if (shown.HasPrev && tick < e.StateTick)
        {
            float t = (float)(Math.Max(0, tick - shown.PrevTick) / GameClient.TickRate);
            at = shown.PrevStart + shown.PrevVelocity * t + shown.PrevGravity * (0.5f * t * t);
            flying = true;
        }
        else
        {
            at = e.PositionAt(tick);
            shown.HasPrev = false;
            if (e.State == ExplosiveState.Flying)
            {
                // Remember this arc, for the next bounce.
                shown.PrevStart = e.ArcStart;
                shown.PrevVelocity = e.ArcVelocity;
                shown.PrevGravity = e.Gravity;
                shown.PrevTick = e.StateTick;
            }
        }
        Vector3 position = GroundView.ToGodot(at);
        if (flying)
        {
            // Tumbling through the air.
            shown.Node.Basis = (shown.Node.Basis * new Basis(shown.Spin.Normalized(), shown.Spin.Length() * (float)delta)).Orthonormalized();
        }
        else
        {
            // Stuck or lying: its bottom on the surface, turned a little by its ID so no two lie alike.
            Vector3 up = GroundView.ToGodot(e.Normal).Normalized();
            Vector3 side = Mathf.Abs(up.Dot(Vector3.Up)) > 0.9f ? Vector3.Forward : Vector3.Up;
            Vector3 x = side.Cross(up).Normalized();
            Basis lying = new Basis(x, up, x.Cross(up)).Orthonormalized();
            float turn = (e.Id.Value % 97) / 97f * 0.6f - 0.3f;
            shown.Node.Basis = new Basis(up, turn) * lying;
        }
        shown.Node.Position = position;
        bool burning = e.Burning && !e.Dud;
        bool lamp = shown.Lamp != null;
        // A fuse sputters and hisses while it burns (a timed charge only beeps).
        if (burning && !lamp && !shown.Fuse.IsValid)
        {
            Node3D follow = shown.FuseSocket ?? shown.Node;
            shown.Fuse = FxDirector.Start("fuse.burning", follow.GlobalPosition, Vector3.Up, new FxParams { Follow = follow });
            shown.FuseSound = Sounds.Loop(Sounds.Item(e.Item.Key, "fuse"), shown.Node);
        }
        else if (!burning && shown.Fuse.IsValid)
        {
            FxDirector.Stop(shown.Fuse);
            Sounds.Stop(shown.FuseSound, 0.1f);
            shown.Fuse = default;
            shown.FuseSound = SoundHandle.None;
        }
        if (!lamp)
        {
            return;
        }
        if (!burning)
        {
            shown.Lamp!.Visible = shown.Light!.Visible = false;
            return;
        }
        // The lamp blinks faster as the fuse runs down: once a second at first, five times a second at the end.
        double left = e.FuseSecondsLeft(Client.ServerTickNow);
        shown.Blink -= delta;
        if (shown.Blink <= 0)
        {
            shown.On = !shown.On;
            double period = Math.Clamp(left / 10, 0.1, 1.0);
            shown.Blink = shown.On ? Math.Min(0.09, period * 0.4) : period - Math.Min(0.09, period * 0.4);
            if (shown.On)
            {
                Sounds.At(Sounds.Item(e.Item.Key, "beep"), position);
            }
        }
        shown.Lamp!.Visible = shown.Light!.Visible = shown.On;
    }

    private static StandardMaterial3D LampMaterial()
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.02f, 0.02f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.1f, 0.06f),
            Roughness = 0.4f,
        };
        Glow.Track(material, 8f);
        return material;
    }

    private void OnRemoved(ClientExplosive e) => Remove(e.Id);

    private void Remove(EntityId id)
    {
        if (!_shown.Remove(id, out Shown? shown))
        {
            return;
        }
        if (shown.Fuse.IsValid)
        {
            FxDirector.Stop(shown.Fuse, immediate: true);
        }
        Sounds.Stop(shown.FuseSound, 0.05f);
        shown.Node.QueueFree();
    }

    // ---------------------------------------------------------------- detonations

    private void OnDetonated(DetonationEvent d)
    {
        Vector3 at = GroundView.ToGodot(d.Position);
        Vector3 normal = GroundView.ToGodot(d.Normal);
        string? item = d.Item?.Key;
        string key = d.Round ? "explosion.ammo" : item switch
        {
            "timed-charge" => "explosion.timed-charge",
            "bag-charge" => "explosion.bag-charge",
            "rocket" or "rocket-hv" => "explosion.rocket",
            _ => "explosion.grenade",
        };
        // The flash, fireball, smoke, dust, debris, scorch and the camera's shake are the effect's (fx.json).
        FxDirector.Play(key, at, normal);
        // The blast's sound has near, middle and far layers chosen by distance, delayed by the speed of sound.
        if (item != null)
        {
            Sounds.At(Sounds.Item(item, "explode") ?? (d.Round ? null : "explosion"), at);
            Sounds.At(Sounds.Item(item, "debris"), at, -3);
        }
        else if (!d.Round)
        {
            Sounds.At("explosion", at);
        }
        Remove(d.Explosive);
    }

    // ---------------------------------------------------------------- rockets

    /// <summary>A rocket: a round of the rocket calibre (it is fired like a bullet and explodes where it stops).</summary>
    public static bool IsRocket(GameClient client, ClientProjectile p) => client.Content.Item(p.Ammo)?.Bullet?.Calibre == "rocket";

    // Rockets in flight: the catalogue's rocket, nose first, with its exhaust and smoke trail, until it stops.
    private void Rockets()
    {
        double now = Client.ClockSeconds;
        foreach (ClientProjectile p in Client.Survival().Projectiles)
        {
            if (!IsRocket(Client, p))
            {
                continue;
            }
            Num.Vector3 at = p.PositionAt(now);
            bool arrived = p.EndedAt is { } end && Num.Vector3.DistanceSquared(at, end) < 0.04f;
            if (!_rockets.TryGetValue(p, out var drawn))
            {
                if (arrived)
                {
                    continue;
                }
                var holder = new Node3D { Name = "Rocket" };
                if (ModelCatalog.Instantiate(Client.Content.Item(p.Ammo)!.Key) is { } model)
                {
                    // The model lies along Z, nose to −Z, its axis 5 cm above its origin.
                    model.Position = new Vector3(0, -0.05f, 0);
                    holder.AddChild(model);
                }
                AddChild(holder);
                Vector3 direction = GroundView.ToGodot(p.DirectionAt(now));
                holder.GlobalTransform = Aligned(direction, GroundView.ToGodot(at));
                drawn = (holder, FxDirector.Start("trail.rocket", holder.GlobalPosition, Vector3.Up,
                    new FxParams { Follow = holder, Direction = direction }));
                _rockets[p] = drawn;
                Sounds.At(Sounds.Item("rocket", "flight"), holder.GlobalPosition);
            }
            if (arrived)
            {
                continue;
            }
            drawn.Node.GlobalTransform = Aligned(GroundView.ToGodot(p.DirectionAt(now)), GroundView.ToGodot(at));
        }
        _rocketsGone.Clear();
        foreach ((ClientProjectile p, (Node3D Node, FxHandle Trail) drawn) in _rockets)
        {
            bool live = Client.Survival().Projectiles.Contains(p) && !(p.EndedAt is { } end && Num.Vector3.DistanceSquared(p.PositionAt(now), end) < 0.04f);
            if (!live)
            {
                _rocketsGone.Add(p);
            }
        }
        foreach (ClientProjectile p in _rocketsGone)
        {
            (Node3D node, FxHandle trail) = _rockets[p];
            FxDirector.Stop(trail);
            node.QueueFree();
            _rockets.Remove(p);
        }
    }

    // −Z along the flight.
    private static Transform3D Aligned(Vector3 direction, Vector3 at)
    {
        Vector3 up = Mathf.Abs(direction.Y) > 0.95f ? Vector3.Right : Vector3.Up;
        return new Transform3D(Basis.LookingAt(direction.Normalized(), up), at);
    }

    // ---------------------------------------------------------------- raid alerts

    private void OnAlert(RaidAlertView alert)
    {
        if (alert.WhileAway)
        {
            return;
        }
        Alerts.ShowLive(alert.What + (alert.Item != null ? $" ({alert.Item.Name})" : ""), Details(alert));
        Sounds.Flat("ui.notify", 4);
    }

    // Where the base is from here and when, how many hits and how much broken.
    private string Details(RaidAlertView a)
    {
        Num.Vector3 d = a.Where - Client.Character.Position;
        float metres = d.Length();
        string[] compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        // From local north where the player stands (docs/WHOLE_WORLD.md §5).
        (Num.Vector3 north, Num.Vector3 east) = Hollow.Simulation.Movement.Facing.Compass(Client.Frame.UpAt(Client.Character.Position));
        double bearing = (Math.Atan2(Num.Vector3.Dot(d, east), Num.Vector3.Dot(d, north)) * 180 / Math.PI + 360) % 360;
        string where = metres < 15 ? "here" : $"{metres:0} m {compass[(int)Math.Round(bearing / 45) % 8]}";
        string broken = a.Destroyed > 0 ? $", {a.Destroyed} broken" : "";
        string hits = a.Hits == 1 ? "1 hit" : $"{a.Hits} hits";
        return $"{where.ToUpperInvariant()}  ·  {a.Last.ToLocalTime():HH:mm}  ·  {hits}{broken}";
    }
}

/// <summary>
/// Raid alerts on the screen (GROUND.md §6.6), in the RustMVP interface's manner: dark cards at the top right with a red
/// edge: one for each live alert ("BASE UNDER ATTACK", what was hit and with what, where, when, how many hits) for ten
/// seconds, and at login the alerts that waited while the player was away under "RAIDED WHILE YOU WERE AWAY" for
/// twenty. Drawn on the 1600 × 900 design canvas through <see cref="Ui.Px"/>; it never takes the mouse.
/// </summary>
public partial class RaidAlertsPanel : Control
{
    private sealed record Card(string Title, IReadOnlyList<(string What, string Details)> Lines, double Until);

    private readonly List<Card> _cards = new();
    private double _now;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void ShowLive(string what, string details)
    {
        _cards.Insert(0, new Card("BASE UNDER ATTACK", new[] { (what, details) }, _now + 10));
        while (_cards.Count > 4)
        {
            _cards.RemoveAt(_cards.Count - 1);
        }
        QueueRedraw();
    }

    public void ShowAway(IReadOnlyList<(string What, string Details)> lines)
    {
        _cards.Add(new Card("RAIDED WHILE YOU WERE AWAY", lines.TakeLast(6).ToList(), _now + 20));
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _now += delta;
        if (_cards.RemoveAll(c => c.Until < _now) > 0 || _cards.Count > 0)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float w = Ui.Pxf(440), x = Size.X - Ui.Pxf(20) - w, y = Ui.Pxf(20);
        foreach (Card card in _cards)
        {
            float alpha = (float)Math.Clamp(card.Until - _now, 0, 1);
            float h = Ui.Pxf(46 + 50 * card.Lines.Count);
            var r = new Rect2(x, y, w, h).Snapped();
            DrawRect(r, new Color(G.Dialog, G.Dialog.A * alpha));
            DrawRect(new Rect2(r.Position, new Vector2(Ui.Pxf(4), r.Size.Y)).Snapped(), new Color(G.Red, alpha));
            Text(card.Title, new Vector2(r.Position.X + Ui.Pxf(18), r.Position.Y + Ui.Pxf(32)), 24, new Color(G.Red, alpha), condensed: true, r.Size.X - Ui.Pxf(30));
            float line = r.Position.Y + Ui.Pxf(46);
            foreach ((string what, string details) in card.Lines)
            {
                Text(what, new Vector2(r.Position.X + Ui.Pxf(18), line + Ui.Pxf(20)), 18, new Color(G.Ink, alpha), condensed: false, r.Size.X - Ui.Pxf(30), bold: true);
                Text(details, new Vector2(r.Position.X + Ui.Pxf(18), line + Ui.Pxf(41)), 16, new Color(G.Muted, alpha), condensed: true, r.Size.X - Ui.Pxf(30));
                line += Ui.Pxf(50);
            }
            y += h + Ui.Pxf(10);
        }
    }

    // A line of text with its baseline at a point, cut with an ellipsis to a width.
    private void Text(string text, Vector2 baseline, float size, Color colour, bool condensed, float width, bool bold = false)
    {
        FontFile font = G.Font(size, bold, condensed);
        int px = Ui.Px(size);
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X > width)
        {
            while (text.Length > 1 && font.GetStringSize(text + "…", HorizontalAlignment.Left, -1, px).X > width)
            {
                text = text[..^1];
            }
            text = text.TrimEnd() + "…";
        }
        DrawString(font, baseline.Round(), text, HorizontalAlignment.Left, -1, px, colour);
    }
}
