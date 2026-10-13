using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Simulation.Building;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Build;

/// <summary>What the hammer's radial does to the piece aimed at: the game sends intents, the build sandbox applies the rules.</summary>
public interface IHammerActions
{
    void Upgrade(Piece piece, int grade);

    void Rotate(Piece piece);

    void Demolish(Piece piece);
}

/// <summary>
/// The building plan's radial (GROUND.md §6.3, §8): every piece of building.json's wheel in its clockwise order
/// (RustMVP's, matched to Rust's), drawn by its key's glyph, with its description and its twig cost against what the
/// player carries; choosing one makes it the plan's piece, affordable or not (the ghost then says why it can't be
/// placed). Twelve o'clock falls between the first piece and the last. Written once for the game (<c>BuildView</c>) and
/// the build sandbox.
/// </summary>
public sealed class PlanRadial : IRadialSource
{
    private readonly BuildingData _data;
    private readonly IBuildingWallet _wallet;
    private RadialOption[] _options = Array.Empty<RadialOption>();
    private int _owned = -1;

    public PlanRadial(BuildingData data, IBuildingWallet wallet)
    {
        _data = data;
        _wallet = wallet;
    }

    /// <summary>The chosen piece: an index into the data's wheel.</summary>
    public int Piece { get; set; }

    /// <summary>The chosen piece's definition.</summary>
    public PieceDef Chosen => _data.Wheel[Math.Clamp(Piece, 0, _data.Wheel.Count - 1)];

    /// <summary>A piece was chosen on the radial.</summary>
    public event Action? Changed;

    public IReadOnlyList<RadialOption> Options
    {
        get
        {
            GradeDef twig = _data.Grades[0];
            int owned = _wallet.Count(twig.Material);
            if (owned == _owned)
            {
                return _options;
            }
            _owned = owned;
            string material = _data.MaterialName(twig.Material);
            _options = new RadialOption[_data.Wheel.Count];
            for (int i = 0; i < _options.Length; i++)
            {
                PieceDef piece = _data.Wheel[i];
                int cost = _data.CostOf(piece, 0);
                _options[i] = new RadialOption(RadialIcon.Glyph(piece.Key), piece.Name, piece.Description, BuildingRadials.CostLine(cost, material, owned),
                    Warning: owned < cost ? $"Not enough {material.ToLowerInvariant()}" : null);
            }
            return _options;
        }
    }

    public int Current => Piece;

    public bool SplitAtTop => true;

    public void Choose(int index)
    {
        Piece = index;
        Changed?.Invoke();
    }
}

/// <summary>The building tools' radials, shared by the game and the build sandbox.</summary>
public static class BuildingRadials
{
    private const string AimAtPiece = "Aim at a building piece";
    private const string EditWindowOver = "The 10 minutes are over";

    /// <summary>
    /// The hammer's radial on the piece aimed at now (RustMVP's): upgrade to each higher grade, at that grade's cost
    /// against what the player carries; rotate; demolish. Each says why it can't be done: nothing aimed at, already at
    /// that grade or stronger, not enough of the material, a piece that doesn't turn, or the 10 minutes after placing
    /// are over.
    /// </summary>
    public static IRadialSource Hammer(Func<Piece?> target, BuildingData data, IBuildingWallet wallet, Func<double> clock, IHammerActions act) =>
        new RadialMenu(menu =>
        {
            Piece? piece = target();
            // What an option does to the piece (nothing without one: the radial never chooses those, a script might).
            Action On(Action<Piece> act) => () =>
            {
                if (piece != null)
                {
                    act(piece);
                }
            };
            for (int grade = 1; grade < data.Grades.Count; grade++)
            {
                GradeDef g = data.Grades[grade];
                string material = data.MaterialName(g.Material);
                int cost = piece == null ? 0 : data.CostOf(piece.Def, grade), owned = wallet.Count(g.Material), to = grade;
                string? reason = piece == null ? AimAtPiece
                    : piece.Grade >= grade ? "Already at this grade or stronger"
                    : owned < cost ? $"Not enough {material.ToLowerInvariant()}"
                    : null;
                menu.Add(new RadialOption(RadialIcon.Glyph("upgrade"), $"UPGRADE TO {g.Name.ToUpperInvariant()}", "Improve this piece's strength",
                    piece == null ? null : CostLine(cost, material, owned), reason), On(p => act.Upgrade(p, to)));
            }
            bool late = piece != null && clock() - piece.PlacedAt > data.EditWindow;
            menu.Add(new RadialOption(RadialIcon.Glyph("rotate"), "ROTATE", "Turn this piece around",
                    Reason: piece == null ? AimAtPiece : piece.Def.RotateSteps == 0 ? "This piece can't be rotated" : late ? EditWindowOver : null),
                On(act.Rotate));
            menu.Add(new RadialOption(RadialIcon.Glyph("demolish"), "DEMOLISH", "Remove this piece and unsupported pieces",
                    Reason: piece == null ? AimAtPiece : late ? EditWindowOver : null),
                On(act.Demolish));
        });

    /// <summary>RustMVP's cost line: "25 x WOOD (915)", the amount carried in brackets.</summary>
    public static string CostLine(int cost, string material, int owned) =>
        string.Create(CultureInfo.InvariantCulture, $"{cost} x {material.ToUpperInvariant()} ({owned})");
}
