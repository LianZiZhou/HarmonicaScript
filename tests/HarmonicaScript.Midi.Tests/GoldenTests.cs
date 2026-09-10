using System.Globalization;
using System.Text;
using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Source;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Profiles;

namespace HarmonicaScript.Midi.Tests;

/// <summary>
/// Golden files over the CONVERSION SUMMARY and the LOWERED EVENT LIST.
///
/// Deliberately not the exporter bytes for every fixture: a serialiser bug is format-wide rather
/// than fixture-specific, so a handful of chosen byte goldens catch it, while committing hundreds
/// of megabytes of generated XML that nobody will ever read catches nothing extra and makes every
/// diff unreviewable.
///
/// What IS golden-filed here is where the real regressions live: which notes survived, what
/// transposition won, and the exact control-event stream. Changing one of these files without a
/// GOLDEN-CHANGE commit trailer fails CI, because a silently normalised regression is how
/// "it used to sound better" becomes unfalsifiable.
/// </summary>
public sealed class GoldenTests
{
    private static readonly LoadedInstrument Profiles =
        new ProfileStore().LoadValidated("df.harmonica.v1", "df.reference");

    /// <summary>A deliberately small, human-reviewable rendering. A reviewer must be able to read the diff.</summary>
    private static string Render(HarmonicaScript.Core.Conversion.ConversionResult result, SourceSong song)
    {
        var report = result.Score.Report;
        var builder = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        builder.Append(inv, $"transpose        {result.Score.Transpose}\n");
        builder.Append(inv, $"keyDistance      {result.Search.Best.KeyDistance}\n");
        builder.Append(inv, $"hardInfeasible   {result.Search.Best.HardInfeasible}\n");
        builder.Append(inv, $"musicalLossPpm   {result.Search.Best.MusicalLossPpm}\n");
        builder.Append(inv, $"grade            {report.Grade} ({report.PlayabilityScore})\n");
        builder.Append(inv, $"notes            {report.AudibleNoteCount}/{report.SourceNoteCount}\n");
        builder.Append(inv, $"dropped/folded   {report.DroppedCount}/{report.FoldedCount}\n");
        builder.Append(inv, $"merged/shifted   {report.MergedCount}/{report.ShiftedCount}\n");
        builder.Append(inv, $"modifierEvents   {report.TotalModifierEvents}\n");
        builder.Append(inv, $"exclusiveSwaps   {report.ExclusiveSwaps}\n");
        builder.Append(inv, $"totalEvents      {result.Timeline.Events.Count}\n");
        builder.Append(inv, $"durationMs       {result.Timeline.TotalDurationMs}\n");
        builder.Append('\n');

        builder.Append("counters\n");
        foreach (var counter in report.Counters.Where(c => c.Value != 0).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            builder.Append(inv, $"  {counter.Id,-34} {counter.Value}\n");
        }

        builder.Append("\nfirst 60 events\n");
        foreach (var e in result.Timeline.Events.Take(60))
        {
            var label = e.Device == InputDeviceKind.Mouse
                ? ((MouseButton)e.Code).ToString()
                : Profiles.Keys.Describe(new Binding(e.Device, e.Code));

            builder.Append(inv, $"  {e.TimeMs,8} {e.Phase,-12} {label,-8} {(e.IsDown ? "down" : "up"),-5} note={e.NoteId}\n");
        }

        return builder.ToString();
    }

    public static TheoryData<string, string> GoldenFixtures()
    {
        // Six deliberately chosen cases rather than the whole corpus: short, chromatic, a long
        // modifier run, a wide range, all three mouse buttons, and pathological density.
        var data = new TheoryData<string, string>();
        data.Add("pd", "ode-to-joy");
        data.Add("pd", "fur-elise");
        data.Add("generated", "chromatic-full-range");
        data.Add("generated", "exclusive-swap-storm");
        data.Add("generated", "five-octave-piano");
        data.Add("generated", "band-seam-oscillation");
        return data;
    }

    [Theory]
    [MemberData(nameof(GoldenFixtures))]
    public Task ConversionIsStable(string category, string name)
    {
        var song = Corpus.Read(category, name);
        var result = Converter.Convert(song, Profiles.ToProfileSet(), new ConversionSettings());

        return Verify(Render(result, song))
            .UseDirectory(Path.Combine(Corpus.Root, "snapshots"))
            .UseFileName($"{category}.{name}");
    }
}
