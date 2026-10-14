namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// surfaces.json (GROUND.md §6.1): the drawn tops of the outcrops' stage models, measured by the model viewer
/// (<c>--survey-surfaces</c>) as rings of heights by direction. Its top-level fields are checked here; each kind's table
/// is read with the kind whose weak spot sits on it (<see cref="GatherReader"/>).
/// </summary>
internal sealed class SurfacesReader : IContentReader
{
    public const string FileName = "surfaces.json";

    public string File => FileName;

    public void Read(ContentReading r) => r.Root(FileName, "rings", "directions", "kinds");

    public void Check(ContentChecking c)
    {
        // Each table is checked against its kind as the kind is read.
    }
}
