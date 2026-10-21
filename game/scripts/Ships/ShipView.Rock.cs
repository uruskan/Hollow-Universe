using Godot;
using Hollow.Simulation.Ships.Modules;

namespace HollowUniverse.Ships;

/// <summary>
/// A vehicle's engine rocking under its torque (VEHICLES.md §9.10; <c>Hollow.Client.Vehicles.EngineRock</c>): the engine
/// module's model (the first module with power) leans about its crank, the length of the vehicle through the module's
/// centre of mass, and the whole model twists a little about its length at the hubs' height. Presentation only: the hull,
/// the seats and the wheels' contacts stay where the motor has them.
/// </summary>
public partial class ShipView
{
    private Node3D? _engine;
    private Transform3D _engineRest;
    private Vector3 _crank, _twistAt;
    private bool _rockFound;

    /// <summary>Leans the engine <paramref name="lean"/> radians about its crank and twists the chassis <paramref name="twist"/>.</summary>
    public void ShowRock(float lean, float twist)
    {
        if (!_rockFound)
        {
            FindEngine();
        }
        if (_engine != null)
        {
            var turn = new Transform3D(new Basis(Vector3.Back, lean), Vector3.Zero);
            _engine.Transform = new Transform3D(Basis.Identity, _crank) * turn * new Transform3D(Basis.Identity, -_crank) * _engineRest;
        }
        var body = new Transform3D(new Basis(Vector3.Back, twist), Vector3.Zero);
        _model.Transform = new Transform3D(Basis.Identity, _twistAt) * body * new Transform3D(Basis.Identity, -_twistAt);
    }

    private void FindEngine()
    {
        if (!IsInsideTree())
        {
            return;
        }
        _rockFound = true;
        foreach (PlacedModule placed in _def.Placed)
        {
            if (placed.Module.Power <= 0 || _model.FindChild($"Module_{placed.Entry.Socket}_{placed.Entry.Level}", false, false) is not Node3D node)
            {
                continue;
            }
            _engine = node;
            _engineRest = node.Transform;
            System.Numerics.Vector3 c = placed.Origin + placed.Module.CentreOfMass;
            _crank = new Vector3(c.X, c.Y, c.Z);
            break;
        }
        float hubs = 0;
        foreach (var w in _def.Wheels)
        {
            hubs += w.Hub.Y;
        }
        hubs = _def.Wheels.Count > 0 ? hubs / _def.Wheels.Count : _def.CentreOfMass.Y;
        _twistAt = new Vector3(0, hubs, _def.CentreOfMass.Z);
    }
}
