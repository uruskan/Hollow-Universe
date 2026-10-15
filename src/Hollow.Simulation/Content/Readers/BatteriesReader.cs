using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// batteries.json (IRON_WAKE.md §6, §9.5): the crewed batteries' kinds, the one timing table the rules (IW3), the view and the
/// art's own script read: the rockets each takes and their launch profiles, how it fires, its recoil curve, its magazine,
/// its mount's rates and limits, and its loader's phases with the motions they drive and the holders that carry rounds. A
/// ship's stations name a kind (ships.json's data file); the ships' reader resolves them after this one.
/// </summary>
internal sealed class BatteriesReader : IContentReader
{
    public const string FileName = "batteries.json";

    private const float Degrees = MathF.PI / 180;

    public string File => FileName;

    public void Read(ContentReading r)
    {
        if (!r.Documents.ContainsKey(FileName))
        {
            return;
        }
        JsonElement root = r.Root(FileName, "format", "conventions", "batteries");
        JsonFields f = r.Fields;
        if (!f.TryChild(root, "batteries", out JsonElement all))
        {
            f.Problem(FileName, "\"batteries\" is missing");
            return;
        }
        int index = 0;
        foreach (JsonProperty entry in all.EnumerateObject())
        {
            BatteryDef? def = ReadBattery(r, entry.Name, entry.Value, index);
            if (def != null)
            {
                r.Loaded.Batteries.Add(def);
                index++;
            }
        }
    }

    private static BatteryDef? ReadBattery(ContentReading r, string key, JsonElement e, int index)
    {
        JsonFields f = r.Fields;
        string where = $"{FileName}: {JsonFields.Quote(key)}";
        f.OnlyKnown(e, where, "ammo", "carrier", "fire", "recoil", "slots", "launch", "mount", "loader");
        var ammo = new List<ItemDef>();
        foreach (string item in f.Texts(e, "ammo", where))
        {
            if (r.Resolve(item, where + ".ammo") is { } def)
            {
                ammo.Add(def);
            }
        }

        // How it fires.
        f.TryChild(e, "fire", out JsonElement fire);
        string mode = f.Text(fire, "mode", where + ".fire", "alternate");
        BatteryFire fireMode = mode switch
        {
            "alternate" => BatteryFire.Alternate,
            "ripple" => BatteryFire.Ripple,
            _ => Bad(f, where + ".fire", $"\"mode\" is alternate or ripple (not \"{mode}\")", BatteryFire.Alternate),
        };
        int[] order = f.Has(fire, "order") ? f.Integers(fire, "order", where + ".fire") : Array.Empty<int>();

        // The recoil curve (presentation, and how long a barrel recovers).
        f.TryChild(e, "recoil", out JsonElement recoil);
        var curve = new RecoilCurve
        {
            Travel = f.Number(recoil, "travel", where + ".recoil"),
            Axis = f.Vector(recoil, "axis", where + ".recoil"),
            Seconds = f.Number(recoil, "seconds", where + ".recoil"),
            Times = Floats(f, recoil, "times", where + ".recoil"),
            Fractions = Floats(f, recoil, "fractions", where + ".recoil"),
        };

        // Slots: the magazine, rounds a load holds, loads waiting in view.
        f.TryChild(e, "slots", out JsonElement slots);
        int magazine = f.Integer(slots, "magazine", where + ".slots");
        // A load's rounds: a clip's (or a cassette's) on a mount, one a barrel.
        int perLoad = f.Has(slots, "clip") ? f.Integer(slots, "clip", where + ".slots")
            : f.Has(slots, "cassette") ? f.Integer(slots, "cassette", where + ".slots") : 1;
        int waiting = (f.Has(slots, "rack") ? f.Integer(slots, "rack", where + ".slots") : 0) +
            (f.Has(slots, "hood") ? f.Integer(slots, "hood", where + ".slots") : 0) +
            (f.Has(slots, "waiting") ? f.Integer(slots, "waiting", where + ".slots") : 0);

        // Launch profiles, by the rockets it takes.
        f.TryChild(e, "launch", out JsonElement launch);
        var profiles = new List<LaunchProfile>();
        if (f.TryChild(launch, "profiles", out JsonElement profileBlock))
        {
            foreach (JsonProperty p in profileBlock.EnumerateObject())
            {
                string at = $"{where}.launch.profiles.{p.Name}";
                if (r.Resolve(p.Name, at) is not { } item)
                {
                    continue;
                }
                profiles.Add(new LaunchProfile
                {
                    Item = item,
                    Muzzle = f.Number(p.Value, "muzzle", at),
                    Ignite = f.Number(p.Value, "ignite", at, 0),
                    Accel = f.Number(p.Value, "accel", at, 0),
                    Burn = f.Number(p.Value, "burn", at, 0),
                    Top = f.Number(p.Value, "top", at, 0),
                    Lifetime = f.Number(p.Value, "lifetime", at),
                    Reach = f.Number(p.Value, "reach", at, 0),
                });
            }
        }
        else
        {
            f.Problem(where + ".launch", "\"profiles\" is missing");
        }
        var launchDef = new BatteryLaunch
        {
            GravityBurning = f.Number(launch, "gravityBurning", where + ".launch", 0.15f),
            Gravity = f.Number(launch, "gravity", where + ".launch", 1f),
            Drag = f.Number(launch, "drag", where + ".launch", 0f),
            Profiles = profiles,
        };

        // The mount.
        f.TryChild(e, "mount", out JsonElement mount);
        f.TryChild(mount, "yaw", out JsonElement yaw);
        f.TryChild(mount, "pitch", out JsonElement pitch);
        var mountDef = new BatteryMount
        {
            YawRate = f.Number(yaw, "rate", where + ".mount.yaw") * Degrees,
            YawAccel = f.Number(yaw, "accel", where + ".mount.yaw") * Degrees,
            Outboard = f.Number(yaw, "outboard", where + ".mount.yaw", 0) * Degrees,
            PitchMin = f.Number(pitch, "min", where + ".mount.pitch") * Degrees,
            PitchMax = f.Number(pitch, "max", where + ".mount.pitch") * Degrees,
            PitchRate = f.Number(pitch, "rate", where + ".mount.pitch") * Degrees,
            PitchAccel = f.Number(pitch, "accel", where + ".mount.pitch") * Degrees,
            Alignment = f.Number(mount, "alignment", where + ".mount") * Degrees,
            Health = f.Number(mount, "health", where + ".mount", 0),
            Zoom = f.Number(mount, "zoom", where + ".mount", 1),
            LoaderSeconds = f.Number(mount, "loaderSeconds", where + ".mount", 0),
        };

        return new BatteryDef
        {
            Key = key,
            Index = index,
            Ammo = ammo,
            Fire = fireMode,
            Interval = f.Number(fire, "interval", where + ".fire", 0),
            Order = order,
            Recoil = curve,
            MagazineSlots = magazine,
            RoundsPerLoad = perLoad,
            Waiting = waiting,
            Launch = launchDef,
            Mount = mountDef,
            Loader = ReadLoader(f, e, where),
        };
    }

    private static BatteryLoader ReadLoader(JsonFields f, JsonElement e, string where)
    {
        string at = where + ".loader";
        if (!f.TryChild(e, "loader", out JsonElement loader))
        {
            f.Problem(at, "is missing");
            return new BatteryLoader();
        }
        string per = f.Text(loader, "per", at, "barrel");
        var phases = new List<LoaderPhase>();
        JsonElement[] list = f.List(loader, "phases", at);
        float start = 0;
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement p = list[i];
            string name = f.Text(p, "name", $"{at}.phases[{i}]");
            string phaseAt = $"{at}.phases.{name}";
            var motions = new List<LoaderMotion>();
            foreach (JsonElement m in f.List(p, "motions", phaseAt, required: false))
            {
                motions.Add(ReadMotion(f, m, phaseAt));
            }
            string loopEffect = "", loopUntil = "";
            if (f.TryChild(p, "loop", out JsonElement loop))
            {
                loopEffect = f.Text(loop, "effect", phaseAt + ".loop");
                loopUntil = f.Text(loop, "until", phaseAt + ".loop", "");
            }
            var phase = new LoaderPhase
            {
                Index = i,
                Name = name,
                Seconds = f.Number(p, "seconds", phaseAt),
                Sound = f.Text(p, "sound", phaseAt, ""),
                EndSound = f.Text(p, "endSound", phaseAt, ""),
                Effects = f.Texts(p, "effects", phaseAt),
                LoopEffect = loopEffect,
                LoopUntilName = loopUntil,
                Motions = motions,
            };
            phase.Start = start;
            start += phase.Seconds;
            phases.Add(phase);
        }
        var holders = new List<LoaderHolder>();
        foreach (JsonElement h in f.List(loader, "holders", at, required: false))
        {
            int what = 0;
            if (h.TryGetProperty("what", out JsonElement w))
            {
                what = w.ValueKind == JsonValueKind.String && w.GetString() == "current" ? LoaderHolder.Current
                    : w.ValueKind == JsonValueKind.Number ? w.GetInt32() : Bad(f, at + ".holders", "\"what\" is a number or \"current\"", 0);
            }
            holders.Add(new LoaderHolder
            {
                Node = f.Text(h, "node", at + ".holders"),
                Slots = f.Texts(h, "slots", at + ".holders"),
                What = what,
                FromName = f.Text(h, "from", at + ".holders", ""),
                UntilName = f.Text(h, "until", at + ".holders", ""),
            });
        }
        var result = new BatteryLoader
        {
            Per = per == "mount" ? LoaderPer.Mount : per == "barrel" ? LoaderPer.Barrel : Bad(f, at, $"\"per\" is barrel or mount (not \"{per}\")", LoaderPer.Barrel),
            WaitsBeforeName = f.Text(loader, "waitsBefore", at, ""),
            Phases = phases,
            Holders = holders,
        };
        // Names to indexes, once the phases are known.
        result.WaitsBefore = result.WaitsBeforeName.Length == 0 ? 0 : result.IndexOf(result.WaitsBeforeName);
        foreach (LoaderPhase phase in phases)
        {
            if (phase.LoopUntilName.Length > 0)
            {
                phase.LoopUntil = result.IndexOf(phase.LoopUntilName);
            }
        }
        foreach (LoaderHolder holder in holders)
        {
            holder.From = holder.FromName.Length == 0 ? -1 : result.IndexOf(holder.FromName);
            holder.Until = holder.UntilName.Length == 0 ? -1 : result.IndexOf(holder.UntilName);
        }
        return result;
    }

    private static LoaderMotion ReadMotion(JsonFields f, JsonElement m, string where)
    {
        string kind = f.Text(m, "kind", where, "translate");
        bool outboard = m.TryGetProperty("axis", out JsonElement axis) && axis.ValueKind == JsonValueKind.String && axis.GetString() == "outboard";
        bool curve = m.TryGetProperty("curve", out JsonElement c) && c.ValueKind == JsonValueKind.String && c.GetString() == "recoil";
        float spanStart = 0, spanEnd = 1;
        if (f.Has(m, "span") && f.Numbers(m, "span", where, 2) is { } span)
        {
            (spanStart, spanEnd) = ((float)span[0], (float)span[1]);
        }
        string ease = f.Text(m, "ease", where, "linear");
        return new LoaderMotion
        {
            Node = f.Text(m, "node", where),
            Rotate = kind == "rotate",
            Axis = outboard ? Vector3.UnitX : f.Vector(m, "axis", where),
            Outboard = outboard,
            From = curve ? 0 : f.Number(m, "from", where, 0),
            To = curve ? 0 : f.Number(m, "to", where, 0),
            Ease = ease switch
            {
                "in" => LoaderEase.In,
                "out" => LoaderEase.Out,
                "inOut" => LoaderEase.InOut,
                _ => LoaderEase.Linear,
            },
            SpanStart = spanStart,
            SpanEnd = spanEnd,
            Curve = curve,
            Mirror = f.Flag(m, "mirror", where, false),
        };
    }

    private static float[] Floats(JsonFields f, JsonElement o, string name, string where) =>
        f.List(o, name, where).Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (float)x.GetDouble()).ToArray();

    private static T Bad<T>(JsonFields f, string where, string what, T fallback)
    {
        f.Problem(where, what);
        return fallback;
    }

    public void Check(ContentChecking c)
    {
        foreach (BatteryDef b in c.Loaded.Batteries)
        {
            string where = $"{FileName}: {JsonFields.Quote(b.Key)}";
            if (b.Ammo.Count is 0 or > 3)
            {
                c.Problem(where + ".ammo", "one to three rockets (the station's choice goes on the wire in two bits)");
            }
            foreach (ItemDef item in b.Ammo)
            {
                if (b.Launch.Of(item.Id) is not { } p)
                {
                    c.Problem(where + ".launch.profiles", $"no profile for \"{item.Key}\"");
                }
                else if (p.Muzzle is <= 0 or > 2000 || p.Lifetime is <= 0 or > 15 || p.Accel < 0 || p.Burn < 0 || p.Ignite < 0)
                {
                    c.Problem($"{where}.launch.profiles.{item.Key}", "muzzle 0 … 2000 m/s, lifetime 0 … 15 s, the motor's numbers not negative");
                }
                if (item.Bullet?.Blast == null)
                {
                    c.Problem(where + ".ammo", $"\"{item.Key}\" isn't a round with a blast (weapons.json)");
                }
            }
            if (b.MagazineSlots is < 1 or > 64 || b.RoundsPerLoad is < 1 or > 8)
            {
                c.Problem(where + ".slots", "a magazine of 1 … 64 slots, 1 … 8 rounds a load");
            }
            if (b.Recoil.Times.Count != b.Recoil.Fractions.Count || b.Recoil.Times.Count < 2)
            {
                c.Problem(where + ".recoil", "\"times\" and \"fractions\" are as long as each other, two or more");
            }
            if (b.Mount.YawRate <= 0 || b.Mount.YawAccel <= 0 || b.Mount.PitchRate <= 0 || b.Mount.PitchAccel <= 0 || b.Mount.PitchMin >= b.Mount.PitchMax)
            {
                c.Problem(where + ".mount", "rates and accelerations positive, pitch's min below its max");
            }
            if (b.Fire == BatteryFire.Ripple && b.Interval <= 0)
            {
                c.Problem(where + ".fire", "a ripple needs an interval");
            }
            BatteryLoader l = b.Loader;
            if (l.Phases.Count == 0 || l.Phases.Any(p => p.Seconds <= 0))
            {
                c.Problem(where + ".loader", "phases, each of some seconds");
            }
            if (l.WaitsBefore < 0)
            {
                c.Problem(where + ".loader.waitsBefore", $"no phase \"{l.WaitsBeforeName}\"");
            }
            if (b.Mount.LoaderSeconds > 0 && MathF.Abs(l.Seconds - b.Mount.LoaderSeconds) > 0.011f)
            {
                c.Problem(where + ".loader", $"its phases take {l.Seconds:0.00} s, the mount says {b.Mount.LoaderSeconds:0.00}");
            }
            foreach (LoaderPhase p in l.Phases)
            {
                if (p.LoopEffect.Length > 0 && p.LoopUntilName.Length > 0 && p.LoopUntil < 0)
                {
                    c.Problem($"{where}.loader.phases.{p.Name}.loop", $"no phase \"{p.LoopUntilName}\"");
                }
            }
            foreach (LoaderHolder h in l.Holders)
            {
                if ((h.FromName.Length > 0 && h.From < 0) || (h.UntilName.Length > 0 && h.Until < 0))
                {
                    c.Problem(where + ".loader.holders", $"{h.Node}: its from or until names no phase");
                }
            }
        }
    }
}
