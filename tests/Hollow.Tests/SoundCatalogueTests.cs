using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hollow.Client.Audio;

namespace Hollow.Tests;

/// <summary>
/// The ground game's sounds (GROUND.md §4.9), checked without Godot: every file of the catalogue is there and imported
/// as what it is, 48 kHz; its measurements (game/assets/audio/levels.json, made by art/audio/build.py with ffmpeg)
/// belong to the file as it is now, keep every peak under −1 dBTP and its loudness inside its category's range (the
/// interface's WAV files are also measured here); and every item that makes a sound has its events.
/// </summary>
public static class SoundCatalogueTests
{
    private static string Game => Path.Combine(Fixtures.Root, "game");

    private static SoundCatalogData Catalogue() =>
        SoundCatalogData.Load(Path.Combine(Game, "data", "ground", SoundCatalogData.FileName));

    [Test]
    public static void SoundFilesExistAndImport()
    {
        SoundCatalogData c = Catalogue();
        var used = new HashSet<string>(StringComparer.Ordinal);
        int files = 0;
        foreach (SoundEventData e in c.Events)
        {
            foreach (string res in e.AllFiles)
            {
                string path = Local(res);
                Assert.True(File.Exists(path), $"{e.Key}: {res} is missing");
                used.Add(Path.GetFullPath(path));
                string import = path + ".import";
                Assert.True(File.Exists(import), $"{e.Key}: {res} has no .import (run Godot's import)");
                string text = File.ReadAllText(import);
                if (path.EndsWith(".ogg", StringComparison.Ordinal))
                {
                    Assert.True(text.Contains("importer=\"oggvorbisstr\"", StringComparison.Ordinal), $"{res}: not imported as Ogg Vorbis");
                    Assert.True(text.Contains(e.Loop ? "loop=true" : "loop=false", StringComparison.Ordinal),
                        $"{res}: {(e.Loop ? "a loop not imported to loop" : "imported to loop")} (run art/audio/build.py, then the import)");
                    (int channels, int rate) = OggHeader(path);
                    Assert.Equal(48000, rate, $"{res}: sample rate");
                    Assert.True(channels is 1 or 2, $"{res}: {channels} channels");
                }
                else
                {
                    Assert.True(path.EndsWith(".wav", StringComparison.Ordinal), $"{res}: neither Ogg Vorbis nor WAV");
                    Assert.True(text.Contains("importer=\"wav\"", StringComparison.Ordinal), $"{res}: not imported as WAV");
                    Wav wav = ReadWav(path);
                    Assert.Equal(48000, wav.Rate, $"{res}: sample rate");
                }
                files++;
            }
        }
        // No stray files: everything in game/assets/audio is played by some event.
        foreach (string f in Directory.EnumerateFiles(Path.Combine(Game, "assets", "audio"), "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".ogg", StringComparison.Ordinal) || f.EndsWith(".wav", StringComparison.Ordinal)))
        {
            Assert.True(used.Contains(Path.GetFullPath(f)), $"{Path.GetFileName(f)} is in game/assets/audio but no event plays it");
        }
        Fixtures.Report($"{c.Events.Count} events, {files} files");
    }

    [Test]
    public static void SoundPeaksAndLoudnessInRange()
    {
        SoundCatalogData c = Catalogue();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Game, "assets", "audio", "levels.json")));
        JsonElement levels = doc.RootElement.GetProperty("files");
        double highestPeak = -100;
        int measured = 0, wavs = 0;
        foreach (SoundEventData e in c.Events)
        {
            foreach ((string res, string category) in e.Files.Select(f => (f, e.Category))
                         .Concat(e.Layers.SelectMany(l => l.Files.Select(f => (f, l.Category)))))
            {
                string rel = res["res://assets/audio/".Length..];
                Assert.True(levels.TryGetProperty(rel, out JsonElement m), $"{e.Key}: {rel} was never measured (run art/audio/build.py)");
                string path = Local(res);
                Assert.Equal(m.GetProperty("sha256").GetString(), Sha256(path), $"{rel}: changed since it was measured (run art/audio/build.py)");
                double peak = m.GetProperty("truePeak").GetDouble();
                Assert.True(peak <= -1.0, $"{rel}: true peak {peak} dBTP, above -1");
                highestPeak = Math.Max(highestPeak, peak);
                Assert.True(c.Categories.TryGetValue(category, out SoundCategory? cat), $"{rel}: unknown category {category}");
                double value = m.GetProperty(cat!.Metric).GetDouble();
                Assert.InRange(cat.Min, cat.Max, value, $"{rel}: {cat.Metric} loudness ({category})");
                Assert.True(m.GetProperty("dc").GetDouble() < 0.005, $"{rel}: DC offset {m.GetProperty("dc").GetDouble()}");
                Assert.True(m.GetProperty("lead").GetDouble() <= 300, $"{rel}: {m.GetProperty("lead").GetDouble()} ms of silence before it");
                // A one-shot's last 15 ms are far under its peak: its tail died away (or was faded), it wasn't cut off.
                // Clicks shorter than 80 ms are all attack and have no tail to speak of.
                if (!e.Loop && m.GetProperty("duration").GetDouble() >= 0.08)
                {
                    Assert.True(m.GetProperty("tail").GetDouble() <= -30, $"{rel}: ends {m.GetProperty("tail").GetDouble()} dB under its peak, cut off");
                }
                measured++;
                if (path.EndsWith(".wav", StringComparison.Ordinal))
                {
                    // The meter here and ffmpeg's agree on the interface's WAV files.
                    Wav wav = ReadWav(path);
                    LoudnessResult r = Loudness.Measure(wav.Channels, wav.Rate);
                    Assert.True(r.TruePeak <= -0.9, $"{rel}: true peak {r.TruePeak} dBTP measured here");
                    Assert.Near(m.GetProperty("event").GetDouble(), r.Event, 1.0, $"{rel}: event loudness measured here");
                    wavs++;
                }
            }
        }
        Fixtures.Report(string.Create(CultureInfo.InvariantCulture,
            $"{measured} files in range, highest true peak {highestPeak:0.00} dBTP; {wavs} WAV files measured here too"));
    }

    // Item keys of G2-G5 that make sounds, with the roles each must have (the ground mode asks for these).
    private static readonly (string Item, string[] Roles)[] RequiredItems =
    {
        ("rock", new[] { "swing", "hitFlesh", "hitWood", "hitStone", "hitOre" }),
        ("torch", new[] { "swing", "hitFlesh" }),
        ("bow", new[] { "draw", "release", "impact" }),
        ("arrow", new[] { "flight", "impact" }),
        ("pistol", new[] { "shot", "shotSilenced", "magOut", "magIn", "rack", "dryFire" }),
        ("rifle", new[] { "shot", "shotSilenced", "magOut", "magIn", "rack", "dryFire" }),
        ("building-plan", new[] { "placeTwig", "placeWood", "placeStone", "placeMetal", "placeArmoured" }),
        ("hammer", new[] { "upgradeWood", "upgradeStone", "upgradeMetal", "upgradeArmoured", "repair", "demolish", "collapse" }),
        ("code-lock", new[] { "press", "accept", "deny" }),
        ("timed-charge", new[] { "fuse", "beep", "explode" }),
        ("campfire", new[] { "place", "loop", "ignite", "extinguish" }),
        ("furnace", new[] { "place", "loop", "ignite", "extinguish" }),
        ("small-box", new[] { "place", "open", "close" }),
        ("large-box", new[] { "place", "open", "close" }),
        // IW4: every rocket's motor catching and its roar on it in flight (a loop the rockets' view stops where it stops);
        // the incendiary rocket's burst, its fuel catching and its fire.
        ("rocket", new[] { "ignite", "flight", "explode" }),
        ("rocket-hv", new[] { "ignite", "flight", "explode" }),
        ("rocket-incendiary", new[] { "ignite", "flight", "explode", "fireStart", "fire" }),
    };

    [Test]
    public static void SoundCatalogueCoversItems()
    {
        SoundCatalogData c = Catalogue();
        var problems = new List<string>();

        void Need(string item, params string[] roles)
        {
            if (!c.Items.ContainsKey(item))
            {
                problems.Add($"{item}: no sounds");
                return;
            }
            foreach (string role in roles)
            {
                string? key = c.ItemEvent(item, role);
                if (key == null || c.Find(key) == null)
                {
                    problems.Add($"{item}: no {role} sound");
                }
            }
        }

        foreach ((string item, string[] roles) in RequiredItems)
        {
            Need(item, roles);
        }
        // A rocket's roar follows it, a fire burns on: their roles play loops.
        foreach ((string item, string role) in new[] { ("rocket", "flight"), ("rocket-hv", "flight"), ("rocket-incendiary", "flight"), ("rocket-incendiary", "fire") })
        {
            if (c.ItemEvent(item, role) is { } key && c.Find(key) is { Loop: false })
            {
                problems.Add($"{item}: its {role} sound {key} isn't a loop");
            }
        }
        // Every tool, weapon, deployable, medicine and food of items.json.
        string data = Path.Combine(Game, "data", "ground");
        using (JsonDocument items = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "items.json"))))
        {
            foreach (JsonElement item in items.RootElement.GetProperty("items").EnumerateArray())
            {
                string key = item.GetProperty("key").GetString()!;
                if (item.TryGetProperty("melee", out _))
                {
                    Need(key, "swing", "hitFlesh", "hitHard", "hitWood", "hitStone", "hitOre");
                }
                if (item.TryGetProperty("bow", out _))
                {
                    Need(key, "draw", "release");
                }
                if (item.TryGetProperty("ammo", out _))
                {
                    Need(key, "impact");
                }
                if (item.TryGetProperty("deploys", out _))
                {
                    Need(key, "place");
                }
                if (item.TryGetProperty("consume", out _))
                {
                    Need(key, "use");
                }
            }
        }
        // Guns, doors, locks and explosives of the model catalogue (G3-G5).
        using (JsonDocument models = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "models.json"))))
        {
            foreach (JsonElement m in models.RootElement.GetProperty("models").EnumerateArray())
            {
                string key = m.GetProperty("key").GetString()!;
                string cls = m.TryGetProperty("class", out JsonElement k) ? k.GetString() ?? "" : "";
                switch (cls)
                {
                    case "gun":
                        Need(key, "shot", "magOut", "magIn", "rack", "dryFire");
                        break;
                    case "door":
                        Need(key, "open", "close");
                        break;
                    case "lock":
                    case "explosive":
                        Need(key, "place");
                        break;
                }
            }
        }
        // Every door kind of the building rules.
        using (JsonDocument building = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "building.json"))))
        {
            foreach (JsonElement door in building.RootElement.GetProperty("doors").EnumerateArray())
            {
                Need(door.GetProperty("key").GetString()!, "open", "close");
            }
        }
        // Every footstep the rules can ask for, and every ambience bed and accent.
        foreach (string key in Footsteps.AllKeys().Concat(new AmbienceLevels().Beds().Select(b => b.Key))
                     .Concat(new[] { AmbienceMix.OwlKey, AmbienceMix.CrowKey, AmbienceMix.ThunderKey }))
        {
            if (c.Find(key) == null)
            {
                problems.Add($"no event {key}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
        Fixtures.Report($"{c.Items.Count} items and door kinds with sounds");
    }

    private static string Local(string res)
    {
        Assert.True(res.StartsWith("res://", StringComparison.Ordinal), $"{res}: not a res:// path");
        return Path.Combine(Game, res["res://".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Channels and sample rate from an Ogg Vorbis file's identification header.</summary>
    private static (int Channels, int Rate) OggHeader(string path)
    {
        byte[] head = new byte[512];
        using (FileStream s = File.OpenRead(path))
        {
            s.ReadAtLeast(head, 64, throwOnEndOfStream: false);
        }
        Assert.True(Encoding.ASCII.GetString(head, 0, 4) == "OggS", $"{path}: not an Ogg file");
        int segments = head[26];
        int packet = 27 + segments;
        Assert.True(head[packet] == 1 && Encoding.ASCII.GetString(head, packet + 1, 6) == "vorbis", $"{path}: not Vorbis");
        int channels = head[packet + 11];
        int rate = BitConverter.ToInt32(head, packet + 12);
        return (channels, rate);
    }

    private sealed record Wav(int Rate, float[][] Channels);

    /// <summary>A 16-bit PCM WAV file's samples.</summary>
    private static Wav ReadWav(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        Assert.True(Encoding.ASCII.GetString(b, 0, 4) == "RIFF" && Encoding.ASCII.GetString(b, 8, 4) == "WAVE", $"{path}: not a WAV file");
        int pos = 12, channels = 0, rate = 0, bits = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4);
            int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ")
            {
                Assert.Equal(1, (int)BitConverter.ToInt16(b, pos + 8), $"{path}: not PCM");
                channels = BitConverter.ToInt16(b, pos + 10);
                rate = BitConverter.ToInt32(b, pos + 12);
                bits = BitConverter.ToInt16(b, pos + 22);
            }
            else if (id == "data")
            {
                Assert.Equal(16, bits, $"{path}: bits per sample");
                int frames = size / (2 * channels);
                var data = new float[channels][];
                for (int c = 0; c < channels; c++)
                {
                    data[c] = new float[frames];
                }
                for (int i = 0; i < frames; i++)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        data[c][i] = BitConverter.ToInt16(b, pos + 8 + (i * channels + c) * 2) / 32768f;
                    }
                }
                return new Wav(rate, data);
            }
            pos += 8 + size + (size & 1);
        }
        throw new TestFailure($"{path}: no data chunk");
    }
}
