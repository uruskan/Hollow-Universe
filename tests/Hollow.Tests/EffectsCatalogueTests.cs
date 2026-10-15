using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Content;

namespace Hollow.Tests;

/// <summary>
/// The effects catalogue's items (GROUND.md §9.3, IW4), checked without Godot: the views play an item's effects by role from
/// fx.json's items, never by its name in code, so every item there is one of the game's, every effect it names is in the
/// catalogue (a loop where a loop is played), and every gun has its shot's flash (and one for each part it takes on its
/// muzzle), every explosive and every round with a blast its burst, every round with no damage of its own (a rocket,
/// drawn as its model in flight) its flight loop, whatever leaves a fire the fire and the patch it leaves, and every
/// deployable that burns fuel its fire.
/// </summary>
public static class EffectsCatalogueTests
{
    // Roles whose effect is a loop (started and stopped); the rest are played once.
    private static readonly HashSet<string> Loops = new(StringComparer.Ordinal) { "flight", "fire" };

    [Test]
    public static void EveryItemNamesItsEffects()
    {
        GameContent c = ContentFixture.Content;
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "fx.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var effects = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (JsonElement e in doc.RootElement.GetProperty("effects").EnumerateArray())
        {
            effects[e.GetProperty("key").GetString()!] = e.TryGetProperty("loop", out _);
        }
        var items = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (JsonProperty item in doc.RootElement.GetProperty("items").EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
        {
            items[item.Name] = item.Value.EnumerateObject().ToDictionary(r => r.Name, r => r.Value.GetString()!, StringComparer.Ordinal);
        }
        var problems = new List<string>();
        foreach ((string item, Dictionary<string, string> roles) in items)
        {
            if (c.Item(item) == null && c.Deployable(item) == null)
            {
                problems.Add($"{item}: not an item or a deployable");
            }
            foreach ((string role, string key) in roles)
            {
                if (!effects.TryGetValue(key, out bool loop))
                {
                    problems.Add($"{item}'s {role}: no effect {key}");
                }
                else if (loop != Loops.Contains(role.Split(':')[0]))
                {
                    problems.Add($"{item}'s {role}: {key} is {(loop ? "a loop" : "played once")}");
                }
            }
        }
        string? Effect(string item, string role) => items.TryGetValue(item, out Dictionary<string, string>? roles) ? roles.GetValueOrDefault(role) : null;
        void Need(string item, string role, string why)
        {
            if (Effect(item, role) == null)
            {
                problems.Add($"{item}: no {role} effect ({why})");
            }
        }
        foreach (FirearmStats gun in c.Firearms)
        {
            Need(gun.Item.Key, "muzzle", "a gun");
            foreach (ItemDef part in gun.Attachments.Where(a => a.Attachment?.Slot == AttachmentSlot.Muzzle))
            {
                if (Effect(gun.Item.Key, "muzzle:" + part.Key) == null && Effect(part.Key, "muzzle") == null)
                {
                    problems.Add($"{gun.Item.Key} with the {part.Key}: no muzzle effect");
                }
            }
        }
        foreach (ThrowableStats t in c.Throwables)
        {
            Need(t.Item.Key, "explode", "an explosive");
        }
        foreach (BulletStats round in c.Bullets)
        {
            if (round.Blast != null)
            {
                Need(round.Item.Key, "explode", "a round with a blast");
            }
            if (c.Firearms.Any(g => g.Calibre == round.Calibre && g.Damage <= 0))
            {
                Need(round.Item.Key, "flight", "a round drawn in flight (its gun does no damage of its own)");
            }
            if (round.Blast?.Fire != null)
            {
                Need(round.Item.Key, "fire", "it leaves a fire");
                Need(round.Item.Key, "fireOut", "it leaves a fire");
            }
        }
        foreach (DeployableDef d in c.Deployables.Where(d => d.Device != null))
        {
            Need(d.Key, "fire", "it burns fuel");
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Fixtures.Report($"{items.Count} items name {items.Values.Sum(r => r.Count)} effects of {effects.Count}");
    }
}
