using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Planning;

namespace HarmonicaScript.Profiles;

public static class LoadedInstrumentExtensions
{
    /// <summary>Bundles the loaded profiles into the shape the converter consumes.</summary>
    public static LoadedProfileSet ToProfileSet(this LoadedInstrument loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        return new LoadedProfileSet(
            loaded.Emission,
            loaded.Timing,
            ArticulationModel.Build(loaded.Emission, loaded.Timing));
    }
}
