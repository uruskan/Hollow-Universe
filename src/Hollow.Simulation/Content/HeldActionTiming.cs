using System;

namespace Hollow.Simulation.Content;

/// <summary>The durations presentation uses for an accepted or predicted held action, on the simulation's tick grid.</summary>
public static class HeldActionTiming
{
    public static float Seconds(int ticks) => ticks / (float)CharacterTicks.Rate;

    public static float Quantize(double seconds) => Seconds(Math.Max(0, CharacterTicks.From(seconds)));

    /// <summary>Rules own gameplay timing; a presentation-only action uses its authored duration.</summary>
    public static float Duration(ItemDef? item, string action, float fallback) => action switch
    {
        "swing" when item?.Melee is { } melee => Seconds(melee.SwingTicks),
        "reload" when item?.Firearm is { } gun => Seconds(gun.ReloadTicks),
        "draw" when item?.Bow is { } bow => Seconds(bow.DrawTicks),
        "throw" when item?.Throwable is { } thrown => Math.Max(Seconds(1), Quantize(thrown.CooldownSeconds)),
        "eat" => Quantize(1.3),
        "bandage" => Quantize(2.3),
        "inject" => Quantize(1.4),
        "repair" => Quantize(0.65),
        "use" => Quantize(0.65),
        _ => Math.Max(Seconds(1), Quantize(fallback)),
    };

    /// <summary>Gameplay anticipation before an instant server intent is dispatched. Art markers are retimed to this; editing art cannot change gameplay.</summary>
    public static float AnticipationSeconds(string action) => Quantize(action switch
    {
        "throw" => 0.25,
        "eat" => 0.4,
        "bandage" => 1.75,
        "inject" => 0.75,
        "repair" or "use" => 0.2,
        _ => 0,
    });

    public static float CommitFraction(string action, float duration) => Math.Clamp(AnticipationSeconds(action) / Math.Max(Seconds(1), duration), 0f, 0.95f);

    public static float StrikeFraction(MeleeStats melee) => melee.StrikeTicks / (float)melee.SwingTicks;

    /// <summary>Retimes authored anticipation/contact/recovery around the actual gameplay contact tick.</summary>
    public static float SamplePhase(float phase, float authoredMarker, float gameplayMarker)
    {
        phase = Math.Clamp(phase, 0f, 1f);
        authoredMarker = Math.Clamp(authoredMarker, 0.001f, 0.999f);
        gameplayMarker = Math.Clamp(gameplayMarker, 0.001f, 0.999f);
        return phase <= gameplayMarker ? phase / gameplayMarker * authoredMarker
            : authoredMarker + (phase - gameplayMarker) / (1f - gameplayMarker) * (1f - authoredMarker);
    }
}
