using System.Globalization;
using System.Text.Json;
using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Project;

namespace HarmonicaScript.Cli;

/// <summary>
/// Renders the playability report.
///
/// Never a bare score. "78/100" tells a user nothing they can act on; the decomposition tells
/// them which setting to change, which is the entire purpose of computing it.
/// </summary>
internal static class Report
{
    internal static void Print(ConversionService.Outcome outcome, TextWriter output, bool asJson)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(output);

        var score = outcome.Conversion.Score;
        var report = score.Report;

        if (asJson)
        {
            PrintJson(outcome, output);
            return;
        }

        var invariant = CultureInfo.InvariantCulture;

        output.WriteLine($"{score.Meta.Title}  ({outcome.Song.FileName})");
        output.WriteLine(new string('-', 68));
        output.WriteLine($"  instrument      {score.Instrument}   timing {score.Timing}");

        if (!outcome.Profiles.Instrument.Provenance.VerifiedInGame)
        {
            output.WriteLine("  !! this instrument profile has NOT been verified in the running game");
            output.WriteLine("     see docs/CALIBRATION.md - six experiments, about half an hour");
        }

        output.WriteLine();
        output.WriteLine($"  grade           {report.Grade}  ({report.PlayabilityScore}/100)");

        foreach (var (reason, penalty) in report.PenaltyBreakdown().Where(p => p.Penalty > 0))
        {
            output.WriteLine($"                  -{penalty,-3} {reason}");
        }

        output.WriteLine();
        output.WriteLine($"  transpose       {score.Transpose:+#;-#;0} semitones "
            + $"(key distance {outcome.Conversion.Search.Best.KeyDistance}, "
            + $"{outcome.Conversion.Search.CandidatesEvaluated} candidates evaluated)");
        output.WriteLine($"  notes           {report.AudibleNoteCount} of {report.SourceNoteCount} kept "
            + $"({report.PreservedFraction * 100:0.0}%)");
        output.WriteLine($"  dropped         {report.DroppedCount}");
        output.WriteLine($"  octave-folded   {report.FoldedCount}");
        output.WriteLine($"  merged          {report.MergedCount}");
        output.WriteLine($"  shifted         {report.ShiftedCount}");
        output.WriteLine();
        output.WriteLine($"  modifier events {report.TotalModifierEvents}   exclusive swaps {report.ExclusiveSwaps}");
        output.WriteLine($"  longest hold    {report.LongestModifierRun} ms");
        output.WriteLine();
        output.WriteLine("  speed ceilings (notes per second)");
        output.WriteLine($"    same key      {report.SameKeyCeilingNps:0.0}");
        output.WriteLine($"    key change    {report.KeyChangeCeilingNps:0.0}");
        output.WriteLine($"    modifier swap {report.ModifierSwapCeilingNps:0.0}");
        output.WriteLine($"    EFFECTIVE     {report.EffectiveCeilingNps:0.0}   <- what this score actually needs to clear");
        output.WriteLine($"  this score      P50 {report.P50Nps:0.0}   P95 {report.P95Nps:0.0}   peak {report.PeakNps:0.0}");

        if (report.P95Nps > report.EffectiveCeilingNps)
        {
            output.WriteLine("    !! P95 exceeds the effective ceiling - try --speed below 1.0");
        }

        output.WriteLine();
        output.WriteLine("  top transposition candidates");
        foreach (var candidate in outcome.Conversion.Search.TopCandidates)
        {
            output.WriteLine(string.Create(
                invariant,
                $"    {candidate.Transpose,3}  infeasible {candidate.HardInfeasible,3}   "
                + $"loss {candidate.MusicalLossPpm / 10_000.0,6:0.00}%   key {candidate.KeyDistance}   "
                + $"mech {candidate.Mechanical / 1_000_000,8}"));
        }

        var worst = report.Diagnostics.Where(d => d.IsAudible).Take(8).ToList();
        if (worst.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  first audible changes");
            foreach (var d in worst)
            {
                output.WriteLine($"    {d.AtUs / 1000,8} ms  note {d.NoteId,5}  {d.Code} ({d.Arg0}, {d.Arg1})");
            }
        }
    }

    private static void PrintJson(ConversionService.Outcome outcome, TextWriter output)
    {
        var report = outcome.Conversion.Score.Report;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("title", outcome.Conversion.Score.Meta.Title);
            writer.WriteString("grade", report.Grade);
            writer.WriteNumber("playability", report.PlayabilityScore);
            writer.WriteNumber("transpose", outcome.Conversion.Score.Transpose);
            writer.WriteNumber("sourceNotes", report.SourceNoteCount);
            writer.WriteNumber("audibleNotes", report.AudibleNoteCount);
            writer.WriteNumber("dropped", report.DroppedCount);
            writer.WriteNumber("folded", report.FoldedCount);
            writer.WriteNumber("merged", report.MergedCount);
            writer.WriteNumber("shifted", report.ShiftedCount);
            writer.WriteNumber("modifierEvents", report.TotalModifierEvents);
            writer.WriteNumber("exclusiveSwaps", report.ExclusiveSwaps);
            writer.WriteNumber("effectiveCeilingNps", Math.Round(report.EffectiveCeilingNps, 2));
            writer.WriteNumber("p95Nps", Math.Round(report.P95Nps, 2));
            writer.WriteBoolean("profileVerifiedInGame", outcome.Profiles.Instrument.Provenance.VerifiedInGame);

            writer.WriteStartArray("counters");
            foreach (var counter in report.Counters)
            {
                writer.WriteStartObject();
                writer.WriteString("id", counter.Id);
                writer.WriteNumber("value", counter.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
