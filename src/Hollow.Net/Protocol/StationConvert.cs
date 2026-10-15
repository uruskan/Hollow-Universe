using System;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;

namespace Hollow.Net.Protocol;

/// <summary>
/// Between the crewed batteries' simulation values and their wire form (IW3, IRON_WAKE.md §6): a gunner's controls (the
/// gunner's client predicts its mount with <c>ToInput(ToControls(...))</c>, the values the server gets), and a ship's
/// stations' records, which a client applies onto its own copy of each station.
/// </summary>
public static class StationConvert
{
    public static StationControls ToControls(in StationInput input) => new()
    {
        Fire = input.Fire,
        Zoom = input.Zoom,
        Choice = (byte)Math.Min(input.Choice, (byte)3),
        Cycle = (byte)(input.Cycle & 3),
    };

    /// <summary>A gunner's controls from a command (untrusted), the asked direction being its view; none: hands off.</summary>
    public static StationInput ToInput(in InputCommand command, Vector3 view) =>
        command.Station is { Length: > 0 } s
            ? new StationInput { View = view, Fire = s[0].Fire, Zoom = s[0].Zoom, Choice = (byte)Math.Min(s[0].Choice, (byte)3), Cycle = (byte)(s[0].Cycle & 3) }
            : default;

    /// <summary>Every station of a ship as the wire carries it.</summary>
    public static ShipStations Stations(Ship ship, uint netId)
    {
        var stations = new StationNet[ship.Stations.Length];
        for (int i = 0; i < stations.Length; i++)
        {
            stations[i] = ToNet(ship.Stations[i]);
        }
        return new ShipStations { Ship = netId, Stations = stations };
    }

    public static StationNet ToNet(StationState st)
    {
        var barrels = new StationBarrelNet[st.Chambers.Length];
        for (int b = 0; b < barrels.Length; b++)
        {
            barrels[b] = new StationBarrelNet { Chamber = st.Chambers[b], ShotTick = (uint)Math.Max(0, st.ShotTicks[b]) };
        }
        var loaders = new LoaderNet[st.Loaders.Length];
        for (int l = 0; l < loaders.Length; l++)
        {
            LoaderState r = st.Loaders[l];
            loaders[l] = new LoaderNet
            {
                Active = r.Active, Waiting = r.Waiting, Reverse = r.Reverse, Since = (uint)Math.Max(0, r.Since), At = BitConverter.SingleToUInt32Bits(r.At),
                Kind = r.Kind, Count = (byte)Math.Min(r.Count, (byte)15),
            };
        }
        BatteryDef battery = st.Def.Battery;
        var counts = new MagazineCountNet[Math.Min(3, battery.Ammo.Count)];
        for (int k = 0; k < counts.Length; k++)
        {
            counts[k] = new MagazineCountNet { Count = (uint)st.Count(battery.Ammo[k].Id) };
        }
        return new StationNet
        {
            Station = (byte)st.Def.Index,
            Chosen = st.Chosen,
            Shots = st.Shots,
            Limit = st.Limit,
            Barrels = barrels,
            Loaders = loaders,
            Counts = counts,
            Refused = st.Refused,
            RefusedDetail = (uint)Math.Max(0, st.RefusedDetail),
            RefusedTick = (uint)Math.Max(0, st.RefusedTick),
        };
    }

    /// <summary>
    /// A station's record from the wire onto a client's copy (untrusted: sizes clamped to its kind's, finite times): its
    /// chambers, shots, loaders and refusal, and its magazine as one slot a kind holding the count.
    /// </summary>
    public static void Apply(in StationNet net, StationState st, GameContent content)
    {
        BatteryDef battery = st.Def.Battery;
        st.Chosen = (byte)Math.Min(net.Chosen, Math.Max(0, battery.Ammo.Count - 1));
        st.Shots = net.Shots;
        st.Limit = net.Limit;
        StationBarrelNet[] barrels = net.Barrels ?? Array.Empty<StationBarrelNet>();
        for (int b = 0; b < st.Chambers.Length; b++)
        {
            st.Chambers[b] = b < barrels.Length && battery.Takes((ushort)Math.Min(barrels[b].Chamber, ushort.MaxValue)) ? (ushort)barrels[b].Chamber : (ushort)0;
            st.ShotTicks[b] = b < barrels.Length && barrels[b].ShotTick > 0 ? barrels[b].ShotTick : long.MinValue / 4;
        }
        LoaderNet[] loaders = net.Loaders ?? Array.Empty<LoaderNet>();
        for (int l = 0; l < st.Loaders.Length; l++)
        {
            if (l >= loaders.Length)
            {
                st.Loaders[l] = default;
                continue;
            }
            LoaderNet r = loaders[l];
            float at = BitConverter.UInt32BitsToSingle(r.At);
            st.Loaders[l] = new LoaderState
            {
                Active = r.Active, Waiting = r.Waiting, Reverse = r.Reverse, Since = r.Since, At = float.IsFinite(at) ? Math.Clamp(at, 0, 600) : 0,
                Kind = battery.Takes((ushort)Math.Min(r.Kind, ushort.MaxValue)) ? (ushort)r.Kind : (ushort)0, Count = r.Count,
            };
        }
        MagazineCountNet[] counts = net.Counts ?? Array.Empty<MagazineCountNet>();
        st.Magazine.Clear();
        for (int k = 0; k < battery.Ammo.Count && k < counts.Length && k < st.Magazine.Size; k++)
        {
            int count = (int)Math.Min(counts[k].Count, 9999);
            if (count > 0)
            {
                st.Magazine.Set(k, new ItemStack(battery.Ammo[k].Id, count));
            }
        }
        st.Refused = Enum.IsDefined(net.Refused) ? net.Refused : OutcomeCode.Invalid;
        st.RefusedDetail = (int)Math.Min(net.RefusedDetail, 1000);
        st.RefusedTick = net.RefusedTick;
    }
}
