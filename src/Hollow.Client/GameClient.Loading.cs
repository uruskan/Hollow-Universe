using System.Collections.Generic;
using Hollow.Client.Loading;

namespace Hollow.Client;

// The client's part of the loading pipeline (GROUND.md §4.3.1): the connection (connected, welcomed, the character
// placed), and the world around the player as the server sends it (things, building pieces, natural states), which
// streams again whenever the player arrives somewhere new.
public sealed partial class GameClient
{
    public const string ConnectStage = "connect";
    public const string WorldStage = "world";

    // How far the connection is in each state (Idle, Connecting, Joining, Playing, Disconnected); placed is all of it.
    private static readonly double[] ConnectShare = { 0, 0.25, 0.5, 0.75, 0 };

    /// <summary>
    /// The client's loading stages: <c>connect</c> (done when the character is placed), and <c>world</c> (done once the
    /// things, building pieces and natural states around the player stopped coming for the plan's <c>quietSeconds</c>, or
    /// after its <c>mostSeconds</c>; again on every arrival). Both fail if the connection is lost or refused.
    /// </summary>
    public IReadOnlyList<LoadStage> LoadStages(LoadingPlan plan)
    {
        StagePlan world = plan.Stage(WorldStage);
        var backlog = new Backlog(this, new QuietStream(world.Number("quietSeconds", 0.5), world.Number("mostSeconds", 6)));
        bool Lost() => State == ClientState.Disconnected;
        return new[]
        {
            new LoadStage(ConnectStage, () => HasCharacter, () => HasCharacter ? 1 : ConnectShare[(int)State], Lost),
            new LoadStage(WorldStage, backlog.Done, backlog.Progress, Lost, LoadScope.Arrival, begin: backlog.Begin),
        };
    }

    // What the server has sent of the world around the player: a count of each kind of part, compared every frame.
    private sealed class Backlog(GameClient client, QuietStream stream)
    {
        private (int Things, long Pieces, int Natural) _seen;
        private bool _arrived;

        public void Begin() => _arrived = false;

        public bool Done()
        {
            Look();
            return _arrived && stream.Settled(client.ClockSeconds);
        }

        public double Progress()
        {
            Look();
            return _arrived ? stream.Progress(client.ClockSeconds) : 0;
        }

        // The stream is counted from the character's arrival; anything new restarts the quiet.
        private void Look()
        {
            if (!client.HasCharacter)
            {
                _arrived = false;
                return;
            }
            double now = client.ClockSeconds;
            (int, long, int) seen = (client._things.Count, client._buildingReplica?.PiecesReceived ?? 0, client.Natural?.Count ?? 0);
            if (!_arrived)
            {
                _arrived = true;
                _seen = seen;
                stream.Begin(now);
            }
            else if (seen != _seen)
            {
                _seen = seen;
                stream.Arrived(now);
            }
        }
    }
}
