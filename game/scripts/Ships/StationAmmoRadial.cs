using System.Collections.Generic;
using System.Globalization;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Ships;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ships;

/// <summary>
/// A gunner's rockets (IW3, IRON_WAKE.md §6.5), R held at a station (P3's ammo radial): every kind the battery takes, with
/// how many the magazine holds; choosing one makes it the station's (loads take it). A tap of R (the current option, before
/// the radial opens) cycles instead: the next kind the magazine holds, and rounds of another kind already chambered drawn
/// back. With none of a kind, its option says why.
/// </summary>
public sealed class StationAmmoRadial(GameClient client) : IRadialSource
{
    private readonly List<RadialOption> _options = new();
    private readonly List<int> _kinds = new();
    private int _current = -1;
    private int _reads;

    public IReadOnlyList<RadialOption> Options
    {
        get
        {
            _reads++;
            Refresh();
            return _options;
        }
    }

    public int Current
    {
        get
        {
            Refresh();
            return _current;
        }
    }

    public void Choose(int index)
    {
        if (index < 0 || index >= _kinds.Count)
        {
            return;
        }
        // The radial reads its options every frame it is open; a tap reads them once to check the choice.
        if (index == _current && _reads <= 2)
        {
            client.Stations().Cycle();
        }
        else
        {
            client.Stations().Choose(_kinds[index]);
        }
    }

    private void Refresh()
    {
        _options.Clear();
        _kinds.Clear();
        _current = -1;
        if (client.Stations().Own is not { } own || client.Stations().Display(own.Ship, own.Station.Index) is not { } st)
        {
            return;
        }
        BatteryDef battery = own.Station.Battery;
        int chosen = st.Chosen;
        for (int k = 0; k < battery.Ammo.Count; k++)
        {
            ItemDef round = battery.Ammo[k];
            int count = st.Count(round.Id);
            _options.Add(new RadialOption(RadialIcon.Item(round.Key), round.Name.ToUpperInvariant(), round.Description,
                count.ToString(CultureInfo.InvariantCulture) + " IN THE MAGAZINE", Reason: count == 0 ? client.Content.Texts.Line(OutcomeCode.Empty) : null));
            _kinds.Add(k);
        }
        // What a tap cycles to: the next kind the magazine holds after the chosen one (the chosen one when none).
        for (int step = 1; step <= battery.Ammo.Count; step++)
        {
            int next = (chosen + step) % battery.Ammo.Count;
            if (st.Count(battery.Ammo[next].Id) > 0)
            {
                _current = next;
                break;
            }
        }
        if (_current < 0)
        {
            _current = chosen;
        }
    }
}
