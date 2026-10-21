using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public sealed partial class Vegetation
{
    // Bounded native lifetime fixture. It invokes the production queue and layout builders with tiny managed input.
    // It does not load the flora library or claim to validate production art, shader output or frame cost.
    internal sealed class LifecycleGate : IDisposable
    {
        public readonly ManualResetEventSlim NearEntered = new(false), BlockEntered = new(false), Continue = new(false);
        public void Dispose() { NearEntered.Dispose(); BlockEntered.Dispose(); Continue.Dispose(); }
    }
    private sealed record LifecycleInput<T>(T Input, LifecycleGate? Gate, bool Near, bool Fault);

    internal void SetupLifecycleFixture(IPlantSource source)
    {
        if (_setup || _stopping) throw new InvalidOperationException("Vegetation.Setup is single-use.");
        _setup = true;
        _plants = source;
        _jobs.Start(source);
    }

    internal void RequestLifecycleScatter(TileKey key) => Request(key, true, 0);
    internal int CollectLifecycleScatter() { CollectScatter(); return _full.Count; }
    internal (int Scatter, int Tasks, int Cancelled, int Discarded) LifecycleCounts => _jobs.Counts;

    internal void QueueLifecycleLayouts(LifecycleGate? gate = null, bool fault = false)
    {
        var appearance = new AppearanceInput(1200, new(Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ), 1, true,
            new Dictionary<ScatterKind, SpeciesInput>
            {
                [ScatterKind.Fir] = new(1, new FloraLibrary.Bands(50, 190, true), true, new[] { new VariantInput(0, 0, 100.01f) }),
            });
        var tile = new TileKey(0, 1, 0, 0);
        var plants = new[] { new PlantInput(tile, new ScatterItem(0, ScatterKind.Fir, 0, Vector3D.UnitX, 0, 0.6f, 1), ScatterKind.Fir) };
        var camera = new Vector3D(1201.7, 0, 0);
        var near = new NearInput(appearance, plants, camera, camera, 1, 1, new());
        var block = new BlockInput(appearance, plants, camera);
        _nearJob = _jobs.Run(new LifecycleInput<NearInput>(near, gate, true, fault), static (input, token) =>
        {
            EnterLifecycle(input.Gate, input.Near, input.Fault);
            return ComputeNear(input.Input, token);
        });
        _blockJobs[tile.Packed] = (_jobs.Run(new LifecycleInput<BlockInput>(block, gate, false, fault), static (input, token) =>
        {
            EnterLifecycle(input.Gate, input.Near, input.Fault);
            return ImpostorBuffer(input.Input, token);
        }), new Block { Key = tile, Anchor = camera });
    }

    private static void EnterLifecycle(LifecycleGate? gate, bool near, bool fault)
    {
        if (gate != null)
        {
            (near ? gate.NearEntered : gate.BlockEntered).Set();
            if (!gate.Continue.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Vegetation probe was never released.");
        }
        if (fault) throw new InvalidOperationException("Expected vegetation lifetime-probe failure.");
    }

    internal bool LifecycleLayoutsComplete => _nearJob?.IsCompleted == true && AllBlocksComplete();
    private bool AllBlocksComplete()
    {
        foreach (var entry in _blockJobs.Values) if (!entry.Job.IsCompleted) return false;
        return true;
    }
    internal bool LifecycleOutputsValid
    {
        get
        {
            if (_nearJob is not { IsCompletedSuccessfully: true } near || near.Result.Count != 1 || near.Result.Sets.Count != 1) return false;
            foreach (var entry in _blockJobs.Values)
                if (!entry.Job.IsCompletedSuccessfully || entry.Job.Result.Count != 16) return false;
            return _blockJobs.Count == 1;
        }
    }
}
