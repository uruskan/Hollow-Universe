using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;

namespace Hollow.Server;

/// <summary>
/// Describes a target for the F2 panel's LOOK tab (GROUND.md §8.5): what its family resolves it to and every public
/// property and field of that object, by reflection, so every kind of thing is covered without a line of code of its
/// own; and what the rules make of it for the asking player (its capabilities, the viewer's flags, the verbs offered).
/// </summary>
internal static class DevInspector
{
    private const int MostLines = 64;
    private const int MostChars = 190;

    public static void Describe(Zone zone, GameContent content, PlayerSession session, in TargetRef target, List<DevLineNet> into)
    {
        TargetSystem targets = zone.Targets();
        Add(into, "Target", Format(target));
        if (targets.FamilyOf(target.Family) is not { } family)
        {
            Add(into, "Family", "none on the server");
            return;
        }
        if (!family.Exists(target.Id))
        {
            Add(into, "Exists", "no: nothing of that id in this family now");
        }
        if (family.TryPose(target, out TargetPose pose))
        {
            Add(into, "Position", GameServer.Format(pose.Position));
        }
        string model = family.ModelKey(target.Id);
        if (model.Length > 0)
        {
            Add(into, "Model", model);
        }
        Add(into, "Capabilities", Names(family.Capabilities(target.Id), targets.Capabilities));
        if (zone.Players().PlayerOf(session.Entity) is { } me)
        {
            Add(into, "Flags for you", targets.FlagsFor(target, targets.ViewerOf(me)).ToString());
            var options = new List<InteractOption>();
            if (zone.Interaction().Options(session.Entity, target, options))
            {
                Add(into, "Verbs for you", options.Count == 0 ? "none" : string.Join(", ", options.Select(o =>
                    (content.Interactions.Verb(o.Verb)?.Key ?? o.Verb.ToString(CultureInfo.InvariantCulture)) + (o.Enabled ? "" : $" ({o.Reason.Code})"))));
            }
        }
        object? resolved = family.Resolve(target.Id);
        if (resolved == null)
        {
            Add(into, "Object", "none: the ground, or one of the surface's own natural objects");
            return;
        }
        Type type = resolved.GetType();
        Add(into, "Object", type.Name);
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            if (into.Count >= MostLines)
            {
                return;
            }
            object? value;
            switch (member)
            {
                case PropertyInfo property when property.CanRead && property.GetIndexParameters().Length == 0:
                    try
                    {
                        value = property.GetValue(resolved);
                    }
                    catch (Exception)
                    {
                        // A getter that throws (a state it isn't in) is a fact too; it must not stop the tick.
                        value = "(throws)";
                    }
                    break;
                case FieldInfo field:
                    value = field.GetValue(resolved);
                    break;
                default:
                    continue;
            }
            Add(into, member.Name, Format(value, zone, content));
        }
    }

    private static void Add(List<DevLineNet> into, string label, string value) =>
        into.Add(new DevLineNet { Label = Clip(label, 36), Value = Clip(value, MostChars) });

    private static string Format(object? value, Zone zone, GameContent content) => value switch
    {
        null => "none",
        string s => s,
        bool b => b ? "yes" : "no",
        float f => Number(f),
        double d => Number(d),
        int or long or uint or ulong or byte or ushort or short or sbyte => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        Enum e => e.ToString(),
        Vector3 v => GameServer.Format(v),
        Quaternion q => $"({Number(q.X)}, {Number(q.Y)}, {Number(q.Z)}, {Number(q.W)})",
        EntityId id => id.IsNone ? "none"
            : id.Value.ToString(CultureInfo.InvariantCulture) + (zone.Players().TryFindPlayer(id, out _, out Player p) ? $" ({p.Name})" : ""),
        TargetRef t => Format(t),
        CapabilitySet c => Names(c, zone.Targets().Capabilities),
        ItemStack stack => stack.IsEmpty ? "empty" : $"{content.Item(stack.Item)?.Key ?? stack.Item.ToString(CultureInfo.InvariantCulture)} × {stack.Count}",
        IEnumerable items => Count(items),
        _ => Describe(value),
    };

    private static string Format(in TargetRef t) =>
        t.Family == TargetFamily.None ? "none" : $"{t.Family} {t.Id}" + (t.Part != 0 ? $", part {t.Part}" : "");

    private static string Names(CapabilitySet set, Registry<ICapability> registry)
    {
        var names = new List<string>();
        foreach (int index in set)
        {
            names.Add(index < registry.Keys.Count ? registry.Keys[index] : index.ToString(CultureInfo.InvariantCulture));
        }
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string Count(IEnumerable items)
    {
        int n = 0;
        foreach (object? _ in items)
        {
            if (++n >= 10000)
            {
                break;
            }
        }
        return n == 1 ? "1 item" : $"{n} items";
    }

    // An object of a kind the panel doesn't know: its key or name if it has one, its own text if it has that, else for
    // a value type its members, and for anything else its type.
    private static string Describe(object value)
    {
        Type type = value.GetType();
        if (type.GetProperty("Key", typeof(string))?.GetValue(value) is string key && key.Length > 0)
        {
            return type.GetProperty("Name", typeof(string))?.GetValue(value) is string name && name.Length > 0 && name != key ? $"{key} ({name})" : key;
        }
        if (type.GetProperty("Name", typeof(string))?.GetValue(value) is string named && named.Length > 0)
        {
            return named;
        }
        if (type.GetMethod("ToString", Type.EmptyTypes)?.DeclaringType is { } declared && declared != typeof(object) && declared != typeof(ValueType))
        {
            return value.ToString() ?? type.Name;
        }
        if (!type.IsValueType)
        {
            return type.Name;
        }
        var parts = new List<string>();
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).Take(8))
        {
            object? inner = member switch
            {
                PropertyInfo p when p.CanRead && p.GetIndexParameters().Length == 0 => p.GetValue(value),
                FieldInfo f => f.GetValue(value),
                _ => null,
            };
            if (inner != null && member is PropertyInfo or FieldInfo)
            {
                parts.Add($"{member.Name} {(inner is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : inner)}");
            }
        }
        return parts.Count > 0 ? string.Join(", ", parts) : type.Name;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Clip(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";
}
