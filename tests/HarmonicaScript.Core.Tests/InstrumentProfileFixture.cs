using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Profiles;

namespace HarmonicaScript.Core.Tests;

/// <summary>Loads the shipped Delta Force profile once for the whole assembly.</summary>
public sealed class DeltaForceProfile
{
    public static readonly LoadedInstrument Reference =
        new ProfileStore().LoadValidated("df.harmonica.v1", "df.reference");

    public static readonly LoadedInstrument Conservative =
        new ProfileStore().LoadValidated("df.harmonica.v1", "df.conservative");
}
