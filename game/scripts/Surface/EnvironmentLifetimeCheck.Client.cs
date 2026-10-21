using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Placement;
using Hollow.Surface;
using HollowUniverse.Build;
using HollowUniverse.Characters;
using HollowUniverse.Ground;
using HollowUniverse.Ships;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Real clients/maps and native nodes, with constant ground and no scene-tree admission of art-loading views.
    private sealed class ClientProbeSurface(double radius) : ISurface, IDisposable
    {
        public double Radius => radius;
        public double SeaRadius => 0;
        public readonly ManualResetEventSlim Entered = new(false), Resume = new(false);
        public volatile bool Block;
        public int Disposals;
        public double HeightAt(Vector3D direction)
        {
            if (Disposals != 0) throw new ObjectDisposedException(nameof(ClientProbeSurface));
            if (Block)
            {
                Entered.Set();
                if (!Resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Client map probe was never released.");
            }
            return 1;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref Disposals);
            Entered.Dispose(); Resume.Dispose();
        }
    }

    private async Task CheckClientViews()
    {
        GameContent content = GameContent.LoadDefault();
        var surfaces = new List<ClientProbeSurface>();
        using var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
        {
            var surface = new ClientProbeSurface(descriptor.Radius);
            surfaces.Add(surface);
            return surface;
        }, static _ => new WorldMemoryEstimate(1, 1));
        using var client = new GameClient(new ClientOptions { Content = content, WorldStore = store });
        using var model = new ClientGroundUi(client);
        long oldBodyOwner = InventoryBody.Use(client), newBodyOwner = InventoryBody.Use(client);
        InventoryBody.Release(oldBodyOwner);
        Expect("old inventory preview owner cannot clear replacement", HollowUniverse.UI.CharacterPreview.Body != null,
            "a retiring view cannot remove a newer view's inventory body factory");
        InventoryBody.Release(newBodyOwner);
        Expect("current inventory preview owner releases client factories", HollowUniverse.UI.CharacterPreview.Body == null
            && HollowUniverse.UI.CharacterPreview.BodyLook == null, "no static factory retains the closed client");
        var building = new BuildView { Client = client };
        var things = new ThingsView { Client = client };
        var ships = new ShipsView { Client = client };
        var people = new PeopleView { Client = client };
        var placing = new PlacementView { Client = client };
        var fires = new FiresView { Client = client };
        var raid = new RaidView { Client = client };
        var debug = new DevDraw { Client = client };
        var presentation = new GroundView { Client = client };
        ClientSet(presentation, "_build", building);
        ClientSet(presentation, "_things", things);
        ClientSet(presentation, "_ships", ships);
        ClientSet(presentation, "_people", people);
        ClientSet(presentation, "_placing", placing);
        ClientSet(presentation, "_fires", fires);
        ClientSet(presentation, "_raid", raid);
        ClientSet(presentation, "_devDraw", debug);
        BuildingWorld? bound = null;
        BuildingRenderer? renderer = null;
        int retirements = 0;
        void Retire()
        {
            bool oldPhysicsLive = client.Physics != null;
            presentation.RetireWorld();
            if (bound != null)
            {
                Expect($"client view retirement {retirements} detaches before physics", oldPhysicsLive
                    && ClientField(building, "_world") == null && ClientField(building, "_target") == null
                    && !ClientSubscribed(bound, "Changed", building) && !ClientSubscribed(bound, "Changed", renderer!),
                    "building view and renderer release the old replica while its physics is still live");
                Expect($"client view retirement {retirements} clears reused IDs", ClientDictionary(things, "_things").Count == 0
                    && ClientDictionary(ships, "_drawn").Count == 0 && ClientDictionary(people, "_people").Count == 0
                    && ClientDictionary(people, "_corpses").Count == 0 && placing.Current == null
                    && ClientField(placing, "_lastPlaced") == null && !ClientSubscribed(client.Stations(), "Shot", ships)
                    && ClientDictionary(fires, "_shown").Count == 0 && ClientDictionary(raid, "_shown").Count == 0
                    && ClientDictionary(debug, "_tiles").Count == 0,
                    "old props, ships, people, corpses, placement details, fire/explosive IDs, debug tiles and station subscriptions are absent");
            }
            bound = null; renderer = null; retirements++;
        }
        client.WorldRetiring += Retire;
        try
        {
            ClientWelcome(client, 1000, 1);
            MapView previous = model.Map ?? throw new InvalidOperationException("First client map missing.");
            uint nextOffer = 2;
            foreach (double radius in new[] { 1000.0, 1100.0 })
            {
                bound = client.Building().World ?? throw new InvalidOperationException("Client building missing.");
                renderer = new BuildingRenderer();
                building.AddChild(renderer);
                // Empty worlds need no meshes/materials. Calling real Attach still exercises its event binding.
                renderer.Attach(bound, null!);
                ClientSet(building, "_renderer", renderer);
                building.BindWorld(bound);
                ClientSet(building, "_target", bound);
                SeedClientViewIds(things, ships, people, placing, content, bound);
                foreach (Node3D view in new Node3D[] { fires, raid })
                {
                    object shown = ClientNested(view.GetType(), "Shown");
                    var node = new Node3D(); view.AddChild(node);
                    ClientSet(shown, "Node", node);
                    ClientDictionary(view, "_shown").Add(new EntityId(7), shown);
                }
                var tile = new MeshInstance3D(); debug.AddChild(tile);
                ClientDictionary(debug, "_tiles").Add(TileKey.At(Vector3D.UnitX, 1), tile);
                ClientWelcome(client, radius, nextOffer++);
                MapView current = model.Map ?? throw new InvalidOperationException("Replacement client map missing.");
                Expect($"client map rebinds at radius {radius}", !ReferenceEquals(previous, current)
                    && (radius == 1000 ? previous.CacheKey == current.CacheKey : previous.CacheKey != current.CacheKey),
                    "same descriptor shares disk identity but uses a fresh ZoneMap render delegate");
                await CheckClientMapReader(current, (ClientProbeSurface)client.Surface!, store);
                previous = current;
            }
            client.Dispose();
            Retire(); // The presentation reset is also safe after the client has already retired.
            Expect("repeated client view retirement releases all leases", store.Diagnostics.ActiveLeases == 0,
                $"active leases={store.Diagnostics.ActiveLeases}; map worker pins drained before disposal");
            await store.BeginRetirement();
            Expect("client fixture worlds dispose once", surfaces.Count == 2 && surfaces.All(surface => surface.Disposals == 1),
                "two descriptor identities, each released exactly once after client and map readers close");
        }
        finally
        {
            foreach (ClientProbeSurface surface in surfaces.Where(surface => surface.Disposals == 0)) surface.Resume.Set();
            client.WorldRetiring -= Retire;
            Retire();
            client.Dispose();
            building.Free(); things.Free(); ships.Free(); people.Free(); placing.Free(); fires.Free(); raid.Free(); debug.Free();
            presentation.Free();
            await store.BeginRetirement();
        }
    }

    private async Task CheckClientMapReader(MapView map, ClientProbeSurface surface, WorldStore store)
    {
        surface.Entered.Reset(); surface.Resume.Reset(); surface.Block = true;
        using var cancellation = new CancellationTokenSource();
        int before = store.Diagnostics.ActiveLeases;
        Task<byte[]> pixels = Task.Run(() => map.RenderCancellable!(cancellation.Token));
        try
        {
            await Until(() => pixels.IsCompleted || surface.Entered.IsSet, "replacement map reaches its current source");
            Expect($"client map delegate reads current world {surface.Radius}", surface.Entered.IsSet
                && !pixels.IsCompleted && store.Diagnostics.ActiveLeases == before + 1,
                "the real UI render delegate reaches the new live map and takes a reader lease");
        }
        finally
        {
            cancellation.Cancel(); surface.Resume.Set();
            try { await pixels.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            surface.Block = false;
        }
        Expect($"client map cancellation drains {surface.Radius}", store.Diagnostics.ActiveLeases == before,
            "all admitted pixel workers returned before their lease was released");
    }

    private static void SeedClientViewIds(ThingsView things, ShipsView ships, PeopleView people, PlacementView placing,
        GameContent content, BuildingWorld world)
    {
        const uint reused = 7;
        var thing = new Node3D(); things.AddChild(thing);
        ClientDictionary(things, "_things").Add(reused, (thing, 1u));
        object drawn = ClientNested(typeof(ShipsView), "Drawn");
        var ship = new ShipView(content.Ships[0]); ships.AddChild(ship);
        ClientSet(drawn, "View", ship); ClientDictionary(ships, "_drawn").Add(reused, drawn);
        _ = typeof(ShipsView).GetProperty("Stations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ships);
        object person = ClientNested(typeof(PeopleView), "Person");
        var body = new CharacterView(); people.AddChild(body);
        ClientSet(person, "View", body); ClientSet(person, "Name", "Previous session");
        ClientDictionary(people, "_people").Add(reused, person);
        object corpse = ClientNested(typeof(PeopleView), "Corpse");
        var bag = new Node3D(); people.AddChild(bag);
        ClientSet(corpse, "Bag", bag); ClientDictionary(people, "_corpses").Add(reused, corpse);
        Placeable placeable = content.Placement.Of(content.Building.Pieces[0])!;
        var candidate = new PlacementCandidate(placeable) { Detail = world };
        ClientSet(placing, "<Current>k__BackingField", candidate);
        ClientSet(placing, "_lastPlaced", new PendingPlacement(1, placeable, default, System.Numerics.Quaternion.Identity, world));
    }

    private static object ClientNested(Type owner, string name) =>
        Activator.CreateInstance(owner.GetNestedType(name, BindingFlags.NonPublic)!, nonPublic: true)!;
    private static FieldInfo ClientMember(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Missing client probe field {owner.GetType().Name}.{name}.");
    private static object? ClientField(object owner, string name) => ClientMember(owner, name).GetValue(owner);
    private static void ClientSet(object owner, string name, object? value) => ClientMember(owner, name).SetValue(owner, value);
    private static IDictionary ClientDictionary(object owner, string name) => (IDictionary)ClientField(owner, name)!;
    private static bool ClientSubscribed(object source, string name, object target) =>
        (ClientField(source, name) as Delegate)?.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, target)) == true;
    private static void ClientWelcome(GameClient client, double radius, uint offerId)
    {
        ZoneSpec spec = ZoneSpec.StartingWorld(15, 30) with { Radius = radius, Ground = GroundKind.StandIn };
        ZoneInfo info = NetConvert.ToInfo(spec);
        EnvironmentIdentity identity = NetConvert.ToEnvironment(spec.EnvironmentDescriptor());
        var offer = new WorldOffer { OfferId = offerId, Zone = info, Environment = identity };
        typeof(GameClient).GetMethod("OnWorldOffer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, new object[] { offer });
        var welcome = new Welcome { OfferId = offer.OfferId, Environment = identity, Zone = info,
            AttackSession = 1, NetId = 1, EntityId = 1, ServerName = "native lifetime fixture" };
        typeof(GameClient).GetMethod("OnWelcome", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(client, new object[] { welcome });
    }
}
