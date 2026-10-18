using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>Common attack transport, budget and result route. Domain handlers retain their simulation rules.</summary>
public sealed class AttackModule : ServerModule
{
    private readonly Dictionary<AttackDomain, Action<PlayerSession, AttackRequest>> _handlers = new();
    private readonly Dictionary<AttackDomain, Action<PlayerSession, AttackId, bool>> _cancellations = new();
    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        _handlers.Add(AttackDomain.Melee, (s, r) => Zone.Combat().QueueMelee(s.Entity, r));
        _cancellations.Add(AttackDomain.Melee, (s, id, acknowledge) => Zone.Combat().CancelMelee(s.Entity, id, acknowledge));
        _handlers.Add(AttackDomain.Station, (s, r) => Server.Ships().QueueAttack(s, r));
        handlers.On<AttackIntent>((PlayerSession session, in AttackIntent m) =>
        {
            if (!session.Playing || m.Seq == 0 || !_handlers.TryGetValue(m.Kind, out var handler)) return;
            if (Zone.Players().PlayerOf(session.Entity) is not { } player || m.Session != player.AttackSession) return;
            var request = new AttackRequest(new AttackId(m.Kind, m.Seq, player.AttackSession), m.Tick, m.Slot, Expected(m.Item),
                Quantize.FromOctahedral(m.ViewU, m.ViewV), Server.Ships().FrameKey(m.Frame), m.ViewDelay,
                new EntityId(Server.Ships().FrameKey(m.Ship)), m.Station, m.Cancel);
            if (m.Cancel)
            {
                // Always advance the compact cancellation frontier. Only acknowledgements for unknown/duplicate
                // IDs spend this budget; already queued/active attempts receive their final decision once.
                if (_cancellations.TryGetValue(m.Kind, out var cancel))
                    cancel(session, request.Id, session.State<CancelBudget>().Bucket.Spend(Zone.Tick, 32, 32));
                return;
            }
            // All attack domains share the firearm/reload bucket.
            if (!session.State<GunBudget>().Spend(Zone.Tick,
                Content.Survival.GunBurst, Content.Survival.GunActionsPerSecond))
            {
                request = request with { Refused = OutcomeCode.RateLimited };
            }
            handler(session, request);
        });
        Zone.Events.On<AttackResolved>((in AttackResolved e) =>
        {
            if (SessionOf(e.Player) is { } to) SendResult(to, e.Resolution);
        });
    }
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        Zone.Combat().BeginMeleeSession(session.Entity);
        if (Zone.Players().PlayerOf(session.Entity) is { } player) player.GunAttackJournal.Clear();
    }
    private void SendResult(PlayerSession session, in AttackResolution r)
    {
        Writer.Reset();
        new AttackResult
        {
            Kind = r.Id.Kind, Seq = r.Id.Sequence, Session = r.Id.Session, Code = r.Code, RequestedTick = r.RequestedTick,
            StartTick = r.StartTick, ContactTick = r.ContactTick, ReadyTick = r.ReadyTick,
            StateSequence = r.StateSequence, Barrel = (sbyte)r.Barrel, Cancelled = r.Cancelled,
        }.WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableUnordered);
    }
    private sealed class CancelBudget { public GunBudget Bucket { get; } = new(); }
}
