using System.Text;
using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Export;
using HarmonicaScript.Export.Exporters;
using HarmonicaScript.Midi;
using HarmonicaScript.Midi.Fixtures;
using HarmonicaScript.Project;

namespace HarmonicaScript.Export.Tests;

public sealed class ExporterTests
{
    private static readonly Lazy<ConversionService.Outcome> LazyOutcome = new(() =>
    {
        var root = FixtureCorpus.FindTestDataRoot()
            ?? throw new DirectoryNotFoundException("testdata/ not found");
        FixtureCorpus.WriteAll(root);

        return new ConversionService().Convert(Path.Combine(root, "pd", "fur-elise.mid"));
    });

    private static ConversionService.Outcome Outcome => LazyOutcome.Value;

    private static (ExportResult Result, byte[] Bytes) Run(IMacroExporter exporter)
    {
        using var stream = new MemoryStream();
        var result = exporter.Write(ConversionService.BuildExportContext(Outcome, "fur-elise"), stream);
        return (result, stream.ToArray());
    }

    public static TheoryData<string> AllTargets()
    {
        var data = new TheoryData<string>();
        foreach (var exporter in ConversionService.BuildRegistry().All)
        {
            data.Add(exporter.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void EveryTargetEitherWritesBytesOrRefusesWithANamedReason(string id)
    {
        var exporter = ConversionService.BuildRegistry().ById(id)!;
        var (result, bytes) = Run(exporter);

        if (result.Ok)
        {
            Assert.NotEmpty(bytes);
        }
        else
        {
            // A refusal must be actionable, never a bare failure.
            Assert.NotEmpty(result.Warnings);
            Assert.All(result.Warnings, w => Assert.False(string.IsNullOrWhiteSpace(w.Code)));
            Assert.All(result.Warnings, w => Assert.True(w.Detail.Length > 40, "a refusal must explain what would unblock it"));
        }
    }

    [Fact]
    public void JsonTimelineRoundTripsExactly()
    {
        var (result, bytes) = Run(new HarmonicaScoreJsonExporter());
        Assert.True(result.Ok);

        using var stream = new MemoryStream(bytes);
        var parsed = HarmonicaScoreJsonExporter.Read(stream);

        Assert.Equal(Outcome.Conversion.Timeline.Events, parsed.Events);
        Assert.Equal(Outcome.Conversion.Timeline.ScoreSha256, parsed.ScoreSha256);
        Assert.Equal(Outcome.Conversion.Timeline.TotalDurationMs, parsed.TotalDurationMs);
    }

    [Fact]
    public void CsvTimelineRoundTripsExactly()
    {
        var (result, bytes) = Run(new HarmonicaScoreCsvExporter());
        Assert.True(result.Ok);

        using var stream = new MemoryStream(bytes);
        Assert.Equal(Outcome.Conversion.Timeline.Events, HarmonicaScoreCsvExporter.Read(stream));
    }

    [Fact]
    public void ReducedMidiReopensWithAnIdenticalPitchAndOnsetSequence()
    {
        // The interop claim, checked rather than asserted: what we write must come back as the
        // same monophonic line, or the whole "feed it to any existing auto-player" promise is empty.
        var (result, bytes) = Run(new ReducedMidiExporter());
        Assert.True(result.Ok);

        var reopened = MidiReader.ReadBytes(bytes, "reduced.mid");
        var expected = Outcome.Conversion.Score.Audible.OrderBy(n => n.ScheduledDownUs).ToList();

        Assert.Equal(expected.Count, reopened.Notes.Count);
        Assert.Equal(
            expected.Select(n => (int)n.EffectiveMidiNote),
            reopened.Notes.Select(n => (int)n.Pitch));

        // Onsets survive the tick round-trip to within one tick.
        for (var i = 0; i < expected.Count; i++)
        {
            var deltaMs = Math.Abs(reopened.Notes[i].OnsetUs - expected[i].ScheduledDownUs) / 1000;
            Assert.True(deltaMs <= 2, $"note {i} moved {deltaMs}ms in the MIDI round-trip");
        }
    }

    [Fact]
    public void AutoHotkeyScriptBlindsEverySendAndReleasesOnExit()
    {
        var (result, bytes) = Run(new AutoHotkeyV2Exporter());
        Assert.True(result.Ok);

        var text = Encoding.UTF8.GetString(bytes);

        // {Blind} is mandatory: we hold a mouse modifier across many notes, and an un-blinded
        // Send would "restore" modifier state and release the control we are holding.
        var sends = text.Split('\n').Where(l => l.Contains("Send(\"{", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(sends);
        Assert.All(sends, line => Assert.Contains("{Blind}", line, StringComparison.Ordinal));

        Assert.Contains("OnExit(Cleanup)", text, StringComparison.Ordinal);
        Assert.Contains("timeBeginPeriod", text, StringComparison.Ordinal);
        Assert.Contains("timeEndPeriod", text, StringComparison.Ordinal);
        Assert.Contains("QueryPerformanceCounter", text, StringComparison.Ordinal);

        // Scan codes, not characters: many games ignore virtual-key injection outright.
        Assert.Contains("{sc02C ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Send(\"z\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoHotkeyReleasesEveryControlItCanEverPress()
    {
        var (_, bytes) = Run(new AutoHotkeyV2Exporter());
        var text = Encoding.UTF8.GetString(bytes);
        var cleanup = text[text.IndexOf("ReleaseAll() {", StringComparison.Ordinal)..];
        cleanup = cleanup[..cleanup.IndexOf("\r\n}", StringComparison.Ordinal)];

        foreach (var degree in Outcome.Profiles.Instrument.Degrees)
        {
            var key = Outcome.Profiles.Keys.Key(degree.Binding.Code);
            Assert.Contains($"sc{key.ScanCode1:X3} up", cleanup, StringComparison.Ordinal);
        }

        foreach (var modifier in Outcome.Profiles.Instrument.Modifiers)
        {
            var mouse = Outcome.Profiles.Keys.Mouse(modifier.Binding.AsMouseButton);
            Assert.Contains($"{mouse.Ahk} up", cleanup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RazerRefusesBecauseItsMouseEncodingHasNeverBeenObserved()
    {
        // The gate, asserted directly. This instrument's three modifiers ARE the mouse buttons,
        // so a Razer file that guessed them would import cleanly and play the wrong thing.
        var (result, _) = Run(new RazerSynapse3Exporter());

        Assert.False(result.Ok);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("unverifiedMouseEncoding", warning.Code);
        Assert.Contains("guess", warning.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BloodyAndRedragonAreAlsoGated()
    {
        Assert.False(Run(new BloodyAmcExporter()).Result.Ok);
        Assert.False(Run(new RedragonMsMacroExporter()).Result.Ok);
    }

    [Fact]
    public void LogitechShipsUngatedBecauseItsMouseApiIsDocumented()
    {
        var (result, bytes) = Run(new LogitechLuaExporter());
        Assert.True(result.Ok);

        var text = Encoding.UTF8.GetString(bytes);

        // The documented numbering trap: PressMouseButton is 1=L 2=M 3=R.
        Assert.Contains("PressMouseButton(3)", text, StringComparison.Ordinal);   // 升调 = right
        Assert.Contains("PressMouseButton(2)", text, StringComparison.Ordinal);   // 半音 = middle
        Assert.Contains("PROFILE_DEACTIVATED", text, StringComparison.Ordinal);
        Assert.Contains("releaseAll()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DeltaTimesReconstructTheAbsoluteTimelineWithoutDrift()
    {
        // Residual-carrying deltaisation. Recomputing each delta from a rounded predecessor is
        // how a five-minute piece ends up seconds late.
        var root = FixtureCorpus.FindTestDataRoot()!;
        var longPiece = new ConversionService().Convert(Path.Combine(root, "generated", "five-minute-drift.mid"));

        var lowered = MacroLowering.Lower(longPiece.Conversion.Timeline, new ExporterCapabilities());
        var events = lowered.Parts.SelectMany(p => p.Events).ToList();

        var reconstructed = 0L;
        foreach (var e in events)
        {
            reconstructed += e.DelayBeforeMs;
        }

        var expected = longPiece.Conversion.Timeline.Events[^1].TimeMs;
        Assert.True(Math.Abs(reconstructed - expected) <= 1, $"drift {Math.Abs(reconstructed - expected)}ms over {expected / 1000}s");
    }

    [Fact]
    public void JianpuRendersFurEliseCorrectly()
    {
        var (result, bytes) = Run(new JianpuExporter());
        Assert.True(result.Ok);

        var text = Encoding.UTF8.GetString(bytes);

        // E5 D#5 E5 D#5 E5 -> +3 +#2 +3 +#2 +3 relative to the C4 base.
        Assert.Contains("+3 +#2 +3 +#2 +3", text, StringComparison.Ordinal);
        Assert.Contains("@transpose:", text, StringComparison.Ordinal);

        // The one grammar ambiguity must be documented in the file itself.
        Assert.Contains("GLUED", text, StringComparison.Ordinal);
    }
}
