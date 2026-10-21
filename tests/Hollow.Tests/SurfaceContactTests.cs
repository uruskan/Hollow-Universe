using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class SurfaceContactTests
{
    [Test]
    public static void BulletAndArrowTerminalRetainContactWhenTheirDamageDestroysTheTarget()
    {
        foreach (bool arrow in new[] { false, true })
        {
            using var rig = new GameRig();
            Entity shooter = rig.Add("ContactShooter", 40, 40);
            if (!arrow) Guns.Arm(rig, shooter, "rifle", "rifle-ammo", 1);
            Deployable wall = rig.Zone.Players().SpawnDeployable(rig.C.Deployable("stone-compound-wall")!, rig.Zone.GroundAt(40, 48), 0, rig.P(shooter).Id);
            wall.Health = .0001f;
            Vector3 aim = wall.Position + wall.Up * 1.5f;
            Vector3 eye = rig.Zone.EyeOf(rig.Ch(shooter).State);
            rig.Clear(eye, aim, 8);
            if (arrow)
            {
                rig.Zone.Combat().Launch(new Projectile { Id = 710, Shooter = rig.P(shooter).Id, Ammo = rig.C.ItemByKey("arrow"),
                    Weapon = rig.C.ItemByKey("bow").Id, Damage = 50, Position = eye,
                    Velocity = Vector3.Normalize(aim - eye) * 100, LifetimeTicks = 120 });
            }
            else
            {
                rig.Look(shooter, aim);
                Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, shooter, 1), "live firearm admitted");
            }
            ProjectileEnded? terminal = null;
            for (int tick = 0; tick < 30 && terminal == null; tick++)
            {
                rig.Step();
                foreach (ProjectileEnded ended in rig.Zone.Events.Of<ProjectileEnded>()) terminal = ended;
            }
            Assert.True(terminal != null, arrow ? "arrow ends via combat pipeline" : "bullet ends via gun pipeline");
            SurfaceContact contact = terminal!.Value.Contact;
            Assert.Equal(wall.Ref, contact.Target.Whole, "terminal preserves destroyed target identity");
            Assert.Equal("rock", rig.C.Surfaces.Key(contact.Surface), "terminal preserves pre-damage material");
            Assert.True(contact.Anchored && contact.Target.Part > 0, "terminal preserves contacted assembly part");
            Assert.True(!rig.Zone.Targets().Exists(wall.Ref), "the same impact really destroyed the target");
            Assert.Near(1, contact.Normal.Length(), .0001, "terminal carries a unit collision normal");
        }
    }

    [Test]
    public static void ActualShipTraceKeepsContactInTheMovingHullFrame()
    {
        using var rig = new GameRig();
        ShipDef def = rig.C.Ship("tin-finch")!;
        Vector3 ground = rig.Zone.GroundAt(80, 80);
        Ship ship = rig.Zone.Ships().SpawnShip(def, ground + rig.Zone.Frame.UpAt(ground) * 12, .6f, EntityId.None, 100);
        ShipPose original = ShipFrame.PoseOf(ship.State, def.CentreOfMass);
        HullBox box = def.Hull[0];
        Vector3 localOrigin = box.Centre - new Vector3(box.HalfExtents.X + 8, 0, 0);
        Vector3 origin = original.ToWorld(localOrigin), direction = original.DirectionToWorld(Vector3.UnitX);
        rig.Clear(origin, origin + direction * 30, 8);
        TraceHit hit = rig.Zone.Damage().Trace(origin, direction, 30, 0, EntityId.None, rig.Zone.Tick, bullet: true);
        Assert.Equal(ship.Ref, hit.Contact.Target, "actual ship hull receiver");
        Assert.Equal("hull", rig.C.Surfaces.Key(hit.Contact.Surface), "ship data preserves distinct hull response");
        Assert.True(hit.Contact.Anchored, "moving hull anchor captured");
        Assert.Near(0, Vector3.Distance(-direction, hit.Contact.Normal), .0001, "oriented hull face normal");
        ship.State.Position += new Vector3(9, 3, -5);
        ship.State.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, .4f) * ship.State.Rotation);
        Assert.True(rig.Zone.Targets().TryPose(hit.Contact.Anchor, out TargetPose moved), "moved ship target pose");
        Assert.Near(0, Vector3.Distance(moved.ToZone(hit.Contact.LocalPoint), hit.Contact.PointAt(moved)), .0001, "contact remains in ship frame");
        Assert.True(Vector3.Distance(hit.Point, hit.Contact.PointAt(moved)) > 1, "contact follows real hull motion");
        Assert.Near(1, hit.Contact.NormalAt(moved).Length(), .0001, "rotated local normal stays unit");
    }

    [Test]
    public static void MalformedContactPayloadCannotCreateInvalidNormalsOrAnchors()
    {
        var target = new TargetRef(TargetFamily.Thing, 77, 2);
        SurfaceContactNet wire = SurfaceContactNet.Of(SurfaceContact.At(target, 255, Vector3.One, Vector3.UnitZ,
            new TargetPose(Vector3.Zero, Quaternion.Identity)));
        wire.NormalX = float.PositiveInfinity; wire.LocalX = float.NaN;
        SurfaceContact result = wire.At(Vector3.One);
        Assert.Equal(target, result.Target, "valid authoritative target retained");
        Assert.True(!result.Anchored, "nonfinite local data cannot create a moving attachment");
        Assert.Equal(Vector3.UnitY, result.Normal, "nonfinite normal has finite unit fallback");
        Assert.Equal("unknown", ContentFixture.Content.Surfaces.Key(result.Surface), "unrecognized surface is neutral");
        wire.Target.Family = (TargetFamily)255;
        result = wire.At(Vector3.One);
        Assert.True(result.Target.IsNone && !result.HasSurface && !result.Anchored, "unknown target family is dropped");
        result = wire.At(new Vector3(float.NaN, 0, 0));
        Assert.Equal(default(SurfaceContact), result, "nonfinite point cannot cross the presentation boundary");
    }

    [Test]
    public static void SerializedZeroNormalIsNotMistakenForAValidDiagonal()
    {
        var target = new TargetNet { Family = TargetFamily.Ship, Id = 55, Part = 0 };
        var wire = new SurfaceContactNet { Target = target, Anchor = target, Surface = ContentFixture.Content.Surfaces.FromShip("tin-finch") };
        var writer = new BitWriter(); wire.Write(writer);
        byte[] bytes = writer.ToArray(); var reader = new BitReader(); reader.Reset(bytes, 0, bytes.Length);
        SurfaceContactNet decoded = SurfaceContactNet.Read(reader);
        Assert.True(decoded.NormalX != 0, "12-bit signed range represents zero with a small quantization residual");
        SurfaceContact result = decoded.At(Vector3.One);
        Assert.Equal(Vector3.UnitY, result.Normal, "quantized zero normal uses a finite unit fallback");
        Assert.True(!result.Anchored, "quantized zero local normal cannot produce an invalid attachment basis");
        Assert.Equal("hull", ContentFixture.Content.Surfaces.Key(result.Surface), "valid physical identity remains intact");
    }

    [Test]
    public static void ActualCompoundContactsCarryMaterialIdentityAndSurviveDestruction()
    {
        using var rig = new GameRig();
        int index = 0;
        foreach ((string key, string surface) in new[] { ("stone-compound-wall", "rock"), ("stone-compound-gate", "rock"),
            ("auto-turret", "metal"), ("large-furnace", "rock"), ("wood-compound-wall", "wood") })
        {
            DeployableDef def = rig.C.Deployable(key)!;
            Deployable thing = rig.Zone.Players().SpawnDeployable(def, rig.Zone.GroundAt(60 + index++ * 20, 60), .3f, EntityId.None);
            Quaternion rotation = Geometry.Upright(thing.Up, thing.Yaw);
            TraceHit hit = default;
            // Sample several heights because these are authored hollow assemblies, not solid bounds boxes.
            for (int height = 1; height < 10; height++)
            {
                Vector3 origin = thing.Position + Vector3.Transform(new Vector3(.1f, def.HalfExtents.Y * height / 5, -8), rotation);
                Vector3 direction = Vector3.Transform(Vector3.Normalize(new Vector3(-.06f, 0, 1)), rotation);
                rig.Clear(origin, origin + direction * 16, 5);
                hit = rig.Zone.Damage().Trace(origin, direction, 16, 0, EntityId.None, rig.Zone.Tick, bullet: true);
                if (hit.Contact.Target.Whole == thing.Ref) break;
            }
            Assert.Equal(thing.Ref, hit.Contact.Target.Whole, key + " uses its actual Thing receiver");
            Assert.Equal(surface, rig.C.Surfaces.Key(hit.Contact.Surface), key + " physical surface");
            Assert.True(hit.Contact.Target.Part > 0 && hit.Contact.Anchored, key + " retains authored part");
            Assert.Near(1, hit.Contact.Normal.Length(), 1e-5, key + " unit face normal");
            Assert.True(rig.Zone.Targets().TryPose(hit.Contact.Anchor, out TargetPose pose), key + " anchor resolves");
            Assert.Near(0, Vector3.Distance(hit.Point, hit.Contact.PointAt(pose)), .001, key + " local position reconstructs");
            SurfaceContact captured = hit.Contact;
            rig.Zone.Things().Break(thing);
            Assert.True(!rig.Zone.Targets().Exists(thing.Ref), key + " destroyed target removed");
            Assert.Equal(surface, rig.C.Surfaces.Key(captured.Surface), key + " destruction cannot erase contact material");
            Assert.Equal(thing.Ref, captured.Target.Whole, key + " destruction cannot erase hit identity");
        }
    }

    [Test]
    public static void AnimalContactUsesFleshAndTheActualCapsuleNormal()
    {
        using var rig = new GameRig();
        Animal animal = AnimalRig.Herd(rig, "deer", 50, 50, 1).Leader!;
        rig.Step(); // Publish the newly spawned herd into the authoritative spatial query index.
        HitPose pose = rig.Zone.Animals().PoseOf(animal);
        Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
        AnimalHitboxes.Shapes(animal.Species, pose, shapes);
        Vector3 centre = (shapes[0].A + shapes[0].B) / 2;
        Vector3 right = Vector3.Normalize(Vector3.Cross(pose.Up, shapes[0].B - shapes[0].A));
        Vector3 origin = centre + right * 6 + pose.Up * .12f;
        Vector3 direction = -right;
        rig.Clear(origin, origin + direction * 12, 5);
        TraceHit hit = rig.Zone.Damage().Trace(origin, direction, 12, 0, EntityId.None, rig.Zone.Tick, bullet: true);
        Assert.Equal(TargetFamily.Animal, hit.Contact.Target.Family, "animal receiver");
        Assert.Equal(animal.Id.Value, hit.Contact.Target.Id, "animal identity");
        Assert.Equal("flesh", rig.C.Surfaces.Key(hit.Contact.Surface), "animal is flesh independent of terrain");
        HitShape shape = shapes[hit.Contact.Target.Part - 1];
        Vector3 expected = ContactGeometry.CapsuleNormal(hit.Point, shape.A, shape.B, -direction);
        Assert.Near(0, Vector3.Distance(expected, hit.Contact.Normal), .0001, "normal is the contacted capsule");
        Assert.True(Vector3.Distance(-direction, hit.Contact.Normal) > .05f, "oblique contact is not inverse flight direction");
    }

    [Test]
    public static void MovingCompoundLeafContactFollowsItsPartAndCarriesNormal()
    {
        GameContent content = ContentFixture.Content;
        DeployableDef def = content.Deployable("stone-compound-gate")!;
        CompoundAssembly assembly = content.Compound.Find(def.Assembly)!;
        int part = assembly.Parts.ToList().FindIndex(p => p.Name == def.Gate!.Parts[0].Part);
        var target = new TargetRef(TargetFamily.Thing, 123, (byte)(part + 1));
        Vector3 at = new(14, 20, -8);
        Assert.True(CompoundGeometry.TryPartPose(content, def, false, at, Vector3.UnitY, .4f, target.Part, out TargetPose closed), "closed part pose");
        SurfaceContact contact = SurfaceContact.At(target, content.Surfaces.FromDeployable(def.Key), closed.ToZone(new Vector3(.7f, .8f, .1f)),
            Vector3.Transform(Vector3.UnitZ, closed.Rotation), closed);
        Assert.True(CompoundGeometry.TryPartPose(content, def, true, at + new Vector3(3, 0, 0), Vector3.UnitY, .7f, target.Part, out TargetPose opened), "opened moving part pose");
        Assert.Near(0, Vector3.Distance(opened.ToZone(new Vector3(.7f, .8f, .1f)), contact.PointAt(opened)), .0001, "follows translation and leaf articulation");
        Assert.Near(0, Vector3.Distance(Vector3.Transform(Vector3.UnitZ, opened.Rotation), contact.NormalAt(opened)), .0001, "normal follows articulation");
    }

    [Test]
    public static void ContactWireRetainsFullIdentityAndMovingFrameCoordinates()
    {
        var target = new TargetRef(TargetFamily.Ship, 0xFEDCBA9876543210, 7);
        var pose = new TargetPose(new Vector3(300, -500, 700), Quaternion.CreateFromYawPitchRoll(.6f, -.3f, .2f));
        SurfaceContact contact = SurfaceContact.At(target, ContentFixture.Content.Surfaces.FromShip("iron-wake"),
            pose.ToZone(new Vector3(4, 2, -5)), Vector3.Normalize(new Vector3(.3f, .8f, .4f)), pose);
        var message = new ProjectileEnd { Id = 45, Shooter = 12, X = contact.Point.X, Y = contact.Point.Y, Z = contact.Point.Z,
            Contact = SurfaceContactNet.Of(contact) };
        var writer = new BitWriter(); message.Write(writer);
        byte[] bytes = writer.ToArray(); var reader = new BitReader(); reader.Reset(bytes, 0, bytes.Length);
        ProjectileEnd back = ProjectileEnd.Read(reader);
        SurfaceContact restored = back.Contact.At(new Vector3(back.X, back.Y, back.Z));
        Assert.True(!reader.Failed, "wire decoded");
        Assert.Equal(target, restored.Target, "64-bit identity and part");
        Assert.Equal(target, restored.Anchor, "moving frame identity and part");
        Assert.Equal(contact.Surface, restored.Surface, "physical material ID");
        Assert.Near(0, Vector3.Distance(contact.LocalPoint, restored.LocalPoint), .0001, "local contact is retained");
        Assert.Near(0, Vector3.Distance(contact.Normal, restored.Normal), .001, "quantized world normal");
        Assert.Near(0, Vector3.Distance(contact.LocalNormal, restored.LocalNormal), .001, "quantized local normal");
    }

    [Test]
    public static void PhysicalSurfaceCoverageAndExtensionsAreValidatedByContent()
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        JsonObject json = JsonNode.Parse(texts[PhysicalSurfaceData.FileName])!.AsObject();
        json["deployables"]!["auto-turret"] = null;
        texts[PhysicalSurfaceData.FileName] = json.ToJsonString();
        Assert.True(GameContent.FromTexts(texts).Problems.Any(p => p.Contains("auto-turret", StringComparison.Ordinal)), "missing material cannot silently become wood");
        json["definitions"]!.AsArray().Add(new JsonObject { ["id"] = 42, ["key"] = "ceramic" });
        json["deployables"]!["auto-turret"] = "ceramic";
        json["deployableParts"] = new JsonObject { ["auto-turret/1"] = "metal" };
        texts[PhysicalSurfaceData.FileName] = json.ToJsonString();
        GameContent modified = GameContent.FromTexts(texts);
        Assert.Equal(0, modified.Problems.Count, string.Join("; ", modified.Problems));
        Assert.Equal("ceramic", modified.Surfaces.Key(modified.Surfaces.FromDeployable("auto-turret")), "new material needs data only");
        Assert.Equal("metal", modified.Surfaces.Key(modified.Surfaces.FromDeployable("auto-turret", 1)), "an authored part overrides whole material");
        using var rig = new GameRig(content: modified);
        Deployable turret = rig.Zone.Players().SpawnDeployable(modified.Deployable("auto-turret")!, rig.Zone.GroundAt(70, 70), 0, EntityId.None);
        byte partSurface = rig.Zone.Targets().FamilyOf(TargetFamily.Thing)!.Surface(turret.Ref with { Part = 1 }, turret.Position);
        Assert.Equal("metal", modified.Surfaces.Key(partSurface), "registered authoritative receiver uses contacted part mapping");
        Assert.Equal("unknown", modified.Surfaces.Key(modified.Surfaces.FromShip("unregistered-ship")), "unknown ship never silently becomes hull");
    }
}
