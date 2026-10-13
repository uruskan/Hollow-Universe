using Hollow.Net.Replication;

namespace Hollow.Net.Protocol;

// The entity states snapshots carry, as the replication's generic sections see them (their serializers are generated).

public partial struct CharacterNetState : INetState<CharacterNetState>
{
    static int INetState<CharacterNetState>.FullSize => FullBits;
}

public partial struct ShipNetState : INetState<ShipNetState>
{
    static int INetState<ShipNetState>.FullSize => FullBits;
}
