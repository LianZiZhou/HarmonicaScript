using System.Runtime.CompilerServices;

namespace HarmonicaScript.Midi.Tests;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Golden files are byte-exact evidence: no scrubbers, no auto-normalisation, and the
        // output must be identical on every OS and architecture.
        VerifierSettings.DontScrubDateTimes();
        VerifierSettings.DontScrubGuids();
        VerifierSettings.UseUtf8NoBom();
    }
}
