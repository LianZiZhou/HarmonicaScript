using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Export;
using HarmonicaScript.Export.Exporters;
using HarmonicaScript.Midi;
using HarmonicaScript.Profiles;

namespace HarmonicaScript.Project;

/// <summary>
/// The one place that assembles "a file on disk" into "a finished conversion".
///
/// Exists so the CLI and the GUI cannot drift apart: both call this, so a bug fixed for one is
/// fixed for both, and the GUI can never quietly use different defaults from the CLI that a user
/// reproduced their problem with.
/// </summary>
public sealed class ConversionService(string instrumentId = "df.harmonica.v1", string timingId = "df.reference")
{
    private readonly ProfileStore _store = new();

    public LoadedInstrument LoadProfiles() => _store.LoadValidated(instrumentId, timingId);

    public LoadedInstrument LoadProfiles(string instrument, string timing) => _store.LoadValidated(instrument, timing);

    public sealed record Outcome(
        Core.Source.SourceSong Song,
        ConversionResult Conversion,
        LoadedInstrument Profiles);

    public Outcome Convert(string midiPath, ConversionSettings? settings = null, LoadedInstrument? profiles = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(midiPath);

        var loaded = profiles ?? LoadProfiles();
        var song = MidiReader.Read(midiPath);
        var conversion = Converter.Convert(song, loaded.ToProfileSet(), settings ?? new ConversionSettings());

        return new Outcome(song, conversion, loaded);
    }

    /// <summary>
    /// Every shipped exporter. Composed HERE rather than in each front end, so adding a target is
    /// one line and both the CLI and the GUI pick it up without a hard-coded switch.
    /// </summary>
    public static IExporterRegistry BuildRegistry() => new ExporterRegistry(
    [
        new AutoHotkeyV2Exporter(),
        new HarmonicaScoreJsonExporter(),
        new HarmonicaScoreCsvExporter(),
        new JianpuExporter(),
        new KeyTapeExporter(),
        new ReducedMidiExporter(),

        // Vendor targets. Only Logitech ships ungated: its mouse encoding is publicly
        // documented. The rest refuse with a named missing capability until a real exported
        // sample containing right/middle mouse events exists - see docs/design/branchB.md.
        new LogitechLuaExporter(),
        new RazerSynapse3Exporter(),
        new BloodyAmcExporter(),
        new RedragonMsMacroExporter(),
    ]);

    public static ExportContext BuildExportContext(Outcome outcome, string outputStem)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var labels = outcome.Conversion.Score.Audible
            .Where(n => n.Fingering is not null)
            .ToDictionary(n => n.Id, n => n.EffectiveMidiNote.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new ExportContext(
            outcome.Conversion.Timeline,
            outcome.Conversion.Score,
            outcome.Profiles.Instrument,
            outcome.Profiles.Keys,
            outcome.Conversion.Score.Meta,
            labels,
            outputStem);
    }

    /// <summary>Runs the simulator over the finished timeline - what the instrument will actually sound.</summary>
    public static SimulationResult Simulate(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var behavior = outcome.Profiles.Instrument.Modifiers.Count == 0
            ? ModifierBehavior.Hold
            : outcome.Profiles.Instrument.Modifiers[0].Behavior;

        return InstrumentSimulator.Simulate(
            outcome.Profiles.Instrument, outcome.Conversion.Timeline.Events, behavior);
    }
}
