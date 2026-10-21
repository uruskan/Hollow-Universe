using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Building;

/// <summary>A target that carries a lock. The shared lock rules own every access and code operation.</summary>
public interface ILockHost : ITargetable
{
    DoorLock? Lock { get; set; }
    bool Lockable { get; }
    bool LockGone { get; }
    void LockChanged();
}

/// <summary>Validated restoration shared by building doors and standalone lock hosts.</summary>
public static class LockState
{
    public static KeyLock RestoreKey(EntityId id, ulong owner, int keyCode, bool locked) =>
        new(id, owner, Math.Clamp(keyCode, 1, BuildingWorld.MaxKeyCode)) { Locked = locked };

    public static CodeLock RestoreCode(EntityId id, ulong owner, ulong hash, bool locked,
        IEnumerable<ulong> authorised, int maximumAuthorised)
    {
        var result = new CodeLock(id, owner) { CodeHash = hash, Locked = locked && hash != 0 };
        result.AuthorisedList.Clear();
        result.AuthorisedList.AddRange(authorised.Distinct().Take(Math.Max(0, maximumAuthorised)));
        return result;
    }
}

/// <summary>The authored lock socket, shared by placement and the visual attached to the moving leaf.</summary>
public static class LockMounts
{
    public static bool TryCompoundPose(GameContent content, DeployableDef def, bool open,
        Vector3 position, Vector3 up, float yaw, out TargetPose pose)
    {
        pose = default;
        if (def.Gate?.LockSocket is not { } mount || content.Compound.Find(def.Assembly) is not { } assembly ||
            assembly.Part(mount.Part) is not { } part) return false;
        CompoundSocket? socket = assembly.Sockets.FirstOrDefault(s => s.Part == mount.Part && s.Name == mount.Name);
        if (socket == null) return false;
        float degrees = open ? def.Gate.Parts.FirstOrDefault(p => p.Part == mount.Part).OpenYaw : 0;
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180);
        Quaternion root = Geometry.Upright(up, yaw);
        Vector3 local = part.Pivot + Vector3.Transform(socket.At - part.Pivot, turn);
        pose = new TargetPose(position + Vector3.Transform(local, root), Quaternion.Normalize(root * turn));
        return true;
    }
}
