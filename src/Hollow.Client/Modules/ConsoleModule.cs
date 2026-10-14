using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Net.Protocol;
using Hollow.Simulation.Movement;

namespace Hollow.Client;

/// <summary>A line of the server's answer to a console command (its number), or a line it sent by itself (0).</summary>
public readonly record struct ConsoleAnswer(uint Seq, ConsoleReplyKind Kind, string Text);

/// <summary>The zone's day as the server keeps it (GROUND.md §8.2): the planet's spin, which an admin may have set.</summary>
public readonly record struct ZoneDay(double BaseTime, double BaseTurns, double Scale, bool Overridden);

// The F1 console's side of the client (GROUND.md §8.2): lines go to the server to run as this player, its answers come
// back line by line; the server says whether the player is an admin and who is on; the zone's clock; the cheats the
// prediction runs with.
public sealed class ConsoleModule : ClientModule
{
    private uint _consoleSeq;
    private (string Name, bool Admin)[] _serverPlayers = Array.Empty<(string, bool)>();

    /// <summary>The server's answers to console lines, line by line, as they arrive.</summary>
    public event Action<ConsoleAnswer>? ConsoleAnswered;

    /// <summary>The zone's clock changed (an admin's <c>time</c> or <c>timescale</c>, or joining).</summary>
    public event Action<ZoneDay>? ZoneDayChanged;

    /// <summary>Whether this player may run the admin commands here (the server says, at joining and when it changes).</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>The players on the server, by name, and whether each is an admin.</summary>
    public IReadOnlyList<(string Name, bool Admin)> ServerPlayers => _serverPlayers;

    /// <summary>How many players the server takes.</summary>
    public int MaxPlayers { get; private set; }

    /// <summary>The zone's clock as the server last sent it; null before it has.</summary>
    public ZoneDay? Day { get; private set; }

    /// <summary>An admin's god mode and noclip on the player's character, as predicted (the server sets them).</summary>
    public MoveCheats Cheats => Character.Cheats;

    /// <summary>
    /// Sends a line to the server's console, to run as this player; returns its number, which the answer's lines carry
    /// (0 when not playing: nothing is sent).
    /// </summary>
    public uint SendConsole(string text)
    {
        text = text.Trim();
        if (State != ClientState.Playing || text.Length == 0)
        {
            return 0;
        }
        uint seq = ++_consoleSeq;
        SendReliable(new ConsoleRun { Seq = seq, Text = text });
        return seq;
    }

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<ConsoleReply>((_, in m) => ConsoleAnswered?.Invoke(new ConsoleAnswer(m.Seq, m.Kind, m.Text ?? "")))
            .On<ServerState>((_, in m) => OnServerState(m))
            .On<ZoneClock>((_, in m) => OnZoneClock(m));
    }

    private void OnServerState(in ServerState m)
    {
        IsAdmin = m.Admin;
        MaxPlayers = (int)Math.Min(m.MaxPlayers, int.MaxValue);
        _serverPlayers = (m.Players ?? Array.Empty<ServerPlayerNet>()).Select(p => (p.Name ?? "", p.Admin)).ToArray();
    }

    private void OnZoneClock(in ZoneClock m)
    {
        if (double.IsFinite(m.BaseTime) && double.IsFinite(m.BaseTurns) && m.Scale >= 0 && double.IsFinite(m.Scale))
        {
            Day = new ZoneDay(m.BaseTime, m.BaseTurns, m.Scale, m.Overridden);
            ZoneDayChanged?.Invoke(Day.Value);
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class ConsoleModuleAccess
{
    public static ConsoleModule Console(this GameClient client) => client.Require<ConsoleModule>();
}
