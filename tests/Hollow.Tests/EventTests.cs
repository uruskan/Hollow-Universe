using System.Collections.Generic;
using Hollow.Simulation;
using Hollow.Simulation.Entities;

namespace Hollow.Tests;

public static class EventTests
{
    /// <summary>
    /// GROUND_SYSTEMS.md §12.3, K7: an event is a type; every subscriber hears it the moment it happens, in the order
    /// things happened; its list keeps it, by kind, until whoever consumes the lists clears them.
    /// </summary>
    [Test]
    public static void EventsAreTypedAndHeard()
    {
        var events = new ZoneEvents();
        var heard = new List<string>();
        events.On<Died>((in Died e) => heard.Add($"died:{e.Cause}"));
        events.On<Notice>((in Notice e) => heard.Add($"notice:{e.Text}"));
        events.On<Died>((in Died e) => heard.Add("again"));
        events.Add(new Notice(new EntityId(1), "first"));
        events.Add(new Died(new EntityId(1), EntityId.None, "Starved"));
        events.Add(new Notice(new EntityId(2), "last"));
        Assert.Equal("notice:first, died:Starved, again, notice:last", string.Join(", ", heard), "every subscriber, as things happened");
        Assert.Equal(2, events.Of<Notice>().Count, "the notices kept");
        Assert.Equal(1, events.Of<Died>().Count, "the death kept");
        Assert.Equal(0, events.Of<Detonated>().Count, "nothing of a kind nobody raised");
        Assert.Equal(3, events.Count, "all of them");
        events.Clear();
        Assert.Equal(0, events.Count, "cleared");
        Assert.Equal(0, events.Of<Notice>().Count, "cleared by kind too");
    }
}
