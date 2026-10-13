using System;
using System.Collections.Generic;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Animals;
using Hollow.Surface;
using Num = System.Numerics;

namespace HollowUniverse.Animals;

/// <summary>
/// The animals in view (GROUND.md §6.9): one <see cref="AnimalView"/> per animal, made when it appears, given its frame
/// every frame, and put back among the spares of its species when it goes. The seam for the ground game: set
/// <see cref="Client"/> and call <see cref="Refresh"/> every frame, as with the people:
/// <code>
/// _animals = new AnimalsView { Name = "Animals", Client = Client };
/// AddChild(_animals);
/// ...
/// _animals.Refresh(delta);
/// </code>
/// Without a client it draws what <see cref="Source"/> gives (the sandbox's local zone), or frames given to
/// <see cref="Apply"/> directly. Render space is the zone's frame (the ground game's).
/// </summary>
public partial class AnimalsView : Node3D
{
    private readonly Dictionary<uint, AnimalView> _views = new();
    private readonly Dictionary<string, Stack<AnimalView>> _spare = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _seen = new();
    private readonly List<uint> _gone = new();
    private readonly List<AnimalFrame> _frames = new();
    private bool _announced;

    /// <summary>The ground game's client: its replicated animals.</summary>
    public GameClient? Client { get; set; }

    /// <summary>Another source of frames when there is no client (a local zone).</summary>
    public Func<IEnumerable<AnimalFrame>>? Source { get; set; }

    /// <summary>Where the camera is (far animals animate less often); the viewport's camera when not set.</summary>
    public Func<Vector3>? Eye { get; set; }

    /// <summary>Footsteps and voices (off for silent previews).</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>The views now, by animal ID.</summary>
    public IReadOnlyDictionary<uint, AnimalView> Views => _views;

    public AnimalView? ViewOf(uint id) => _views.GetValueOrDefault(id);

    /// <summary>Spare views kept per species, so animals coming into range don't each cost a model.</summary>
    public const int SparePerSpecies = 6;

    /// <summary>Every frame: the client's animals (or the source's), drawn.</summary>
    public void Refresh(double delta)
    {
        _frames.Clear();
        if (Client != null)
        {
            Collect(Client, _frames);
        }
        else if (Source != null)
        {
            _frames.AddRange(Source());
        }
        Apply(_frames, delta);
    }

    /// <summary>Draws these animals; any drawn before and not among them goes.</summary>
    public void Apply(IReadOnlyList<AnimalFrame> frames, double delta)
    {
        Vector3 eye = Eye?.Invoke() ?? GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        _seen.Clear();
        foreach (AnimalFrame f in frames)
        {
            if (!_views.TryGetValue(f.Id, out AnimalView? view) || view.Model.Key != f.Species)
            {
                if (view != null)
                {
                    Release(view);
                    _views.Remove(f.Id);
                }
                view = Take(f.Species);
                if (view == null)
                {
                    continue;
                }
                _views[f.Id] = view;
            }
            _seen.Add(f.Id);
            view.Apply(f, delta, eye);
        }
        _gone.Clear();
        foreach (uint id in _views.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }
        foreach (uint id in _gone)
        {
            Release(_views[id]);
            _views.Remove(id);
        }
    }

    /// <summary>A client's animals as frames (render space = the zone's frame).</summary>
    public static void Collect(GameClient client, List<AnimalFrame> into)
    {
        if (client.Zone == null)
        {
            return;
        }
        ZoneFrame frame = client.Frame;
        foreach (ClientAnimal a in client.Animals)
        {
            into.Add(new AnimalFrame
            {
                Id = a.NetId,
                Species = a.SpeciesKey,
                Data = a.Species,
                Position = ToRender(a.Position),
                Up = ToRender(frame.UpAt(a.Position)),
                Yaw = a.Yaw,
                State = a.State,
                Speed = a.Speed,
                Alert = a.Alert,
                Warning = a.Warning,
                Dead = a.Dead,
                Health = a.Health,
                AttackCount = a.AttackCount,
                HurtCount = a.HurtCount,
                Attack = a.LastAttack,
            });
        }
    }

    /// <summary>A zone's animal as a frame (a local zone: the sandbox), at a position and heading given (interpolated).</summary>
    public static AnimalFrame Of(Animal a, ZoneFrame frame, Num.Vector3 position, float yaw) => new()
    {
        Id = (uint)a.Id.Value,
        Species = a.Species.Key,
        Data = a.Species,
        Position = ToRender(position),
        Up = ToRender(frame.UpAt(position)),
        Yaw = yaw,
        State = a.State,
        Speed = a.IsDead ? 0 : a.Speed,
        Alert = a.Alert,
        Warning = a.Warning,
        Dead = a.IsDead,
        Health = a.IsDead ? a.HarvestFraction : a.HealthFraction,
        AttackCount = a.AttackCount,
        HurtCount = a.HurtCount,
        Attack = a.LastAttack,
    };

    public static Vector3 ToRender(Num.Vector3 v) => new(v.X, v.Y, v.Z);

    private AnimalView? Take(string species)
    {
        if (_spare.TryGetValue(species, out Stack<AnimalView>? stack) && stack.TryPop(out AnimalView? kept))
        {
            kept.Visible = true;
            return kept;
        }
        if (AnimalAssets.Get(species) is not { } model)
        {
            return null;
        }
        AnimalView view = AnimalView.Create(model);
        view.Sounds = Sounds;
        AddChild(view);
        if (!_announced)
        {
            _announced = true;
            GD.Print($"[animals] first animal drawn: {species}");
        }
        return view;
    }

    private void Release(AnimalView view)
    {
        if (!_spare.TryGetValue(view.Model.Key, out Stack<AnimalView>? stack))
        {
            _spare[view.Model.Key] = stack = new Stack<AnimalView>();
        }
        if (stack.Count >= SparePerSpecies)
        {
            view.QueueFree();
            return;
        }
        view.Reset();
        view.Visible = false;
        stack.Push(view);
    }
}
