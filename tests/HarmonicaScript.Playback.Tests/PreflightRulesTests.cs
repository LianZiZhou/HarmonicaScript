using HarmonicaScript.Playback;

namespace HarmonicaScript.Playback.Tests;

/// <summary>
/// The diagnosis ladder, tested as a pure function against synthetic fact sets.
///
/// This is the one piece of user-facing decision logic in the project, it fires exactly when a
/// user is already frustrated, and it can be exercised completely without Windows or the game.
/// </summary>
public sealed class PreflightRulesTests
{
    private static EnvironmentFacts Healthy => new()
    {
        GameProcessFound = true,
        GameWindowFocused = true,
        ExclusiveFullscreen = false,
        ForegroundKeyboardLayout = 0x0409,
        WeAreElevated = false,
        NotepadDeliverySucceeded = true,
        BackendAvailable = true,
        PlatformDescription = "Windows 11",
    };

    private static PreflightRung Rung(EnvironmentFacts facts, string code) =>
        PreflightRules.Evaluate(facts).Single(r => r.Code == code);

    [Fact]
    public void AHealthyEnvironmentHasNoBlockers()
    {
        var rungs = PreflightRules.Evaluate(Healthy);
        Assert.DoesNotContain(rungs, r => r.Verdict == RungVerdict.Fail);
        Assert.StartsWith("no blocker found", PreflightRules.Summarise(rungs), StringComparison.Ordinal);
    }

    [Fact]
    public void APassingNotepadTestDoesNotClearTheElevationRung()
    {
        // THE correction that matters. Notepad runs at medium integrity, so if the game is
        // elevated the delivery test passes and UIPI is still silently discarding everything.
        // A ladder that marked rung 4 clear here would send users hunting for the wrong cause.
        var rungs = PreflightRules.Evaluate(Healthy);

        Assert.Equal(RungVerdict.Pass, rungs.Single(r => r.Code == "delivery").Verdict);

        var uipi = rungs.Single(r => r.Code == "uipi");
        Assert.Equal(RungVerdict.Unknown, uipi.Verdict);
        Assert.Contains("does NOT rule this out", uipi.EvidenceEn, StringComparison.Ordinal);
        Assert.Contains("medium integrity", uipi.EvidenceEn, StringComparison.Ordinal);
    }

    [Fact]
    public void UnfocusedGameIsAHardBlock()
    {
        var rung = Rung(Healthy with { GameWindowFocused = false }, "focus");
        Assert.Equal(RungVerdict.Fail, rung.Verdict);
        Assert.Contains("NOT focused", rung.EvidenceEn, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingGameProcessIsAHardBlock()
    {
        Assert.Equal(RungVerdict.Fail, Rung(Healthy with { GameProcessFound = false }, "focus").Verdict);
    }

    [Fact]
    public void ExclusiveFullscreenIsDetectedAndTheRemedyIsNamed()
    {
        var rung = Rung(Healthy with { ExclusiveFullscreen = true }, "fullscreen");
        Assert.Equal(RungVerdict.Fail, rung.Verdict);
        Assert.Contains("borderless", rung.EvidenceEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("无边框", rung.EvidenceZh, StringComparison.Ordinal);
    }

    [Fact]
    public void ChineseKeyboardLayoutWarnsWithoutBlocking()
    {
        // The layout is observable; the IME's conversion MODE is not. So this warns rather than
        // claiming a certainty it does not have - the garbled Notepad readback is what proves it.
        var rung = Rung(Healthy with { ForegroundKeyboardLayout = 0x0804 }, "ime");
        Assert.Equal(RungVerdict.Warn, rung.Verdict);
    }

    [Fact]
    public void GarbledNotepadReadbackFailsDeliveryAndPointsAtTheIme()
    {
        var rung = Rung(
            Healthy with { NotepadDeliverySucceeded = false, NotepadReadback = "阻侧从" },
            "delivery");

        Assert.Equal(RungVerdict.Fail, rung.Verdict);
        Assert.Contains("IME", rung.EvidenceEn, StringComparison.Ordinal);
        Assert.Contains("输入法", rung.EvidenceZh, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnrunDeliveryTestIsUnknownNotPassing()
    {
        Assert.Equal(RungVerdict.Unknown, Rung(Healthy with { NotepadDeliverySucceeded = null }, "delivery").Verdict);
    }

    [Fact]
    public void InGameRebindingIsReportedAsUnobservableRatherThanGuessed()
    {
        var rung = Rung(Healthy, "binding");
        Assert.Equal(RungVerdict.Unknown, rung.Verdict);
        Assert.Contains("cannot be observed", rung.EvidenceEn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSummaryNamesRungSixHonestlyWhenEverythingElseIsClear()
    {
        // "All five green and still nothing" is the project-killing case. It must be stated as
        // itself, never dressed up as a generic failure.
        var summary = PreflightRules.Summarise(PreflightRules.Evaluate(Healthy));
        Assert.Contains("filtering injected input", summary, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRungCarriesBothLanguages()
    {
        Assert.All(PreflightRules.Evaluate(Healthy), rung =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rung.EvidenceEn));
            Assert.False(string.IsNullOrWhiteSpace(rung.EvidenceZh));
        });
    }

    [Fact]
    public void EvaluationIsAPureFunctionOfTheFacts()
    {
        var facts = Healthy with { ExclusiveFullscreen = true, ForegroundKeyboardLayout = 0x0804 };

        var a = PreflightRules.Evaluate(facts);
        var b = PreflightRules.Evaluate(facts);

        Assert.Equal(a.Select(r => (r.Number, r.Code, r.Verdict)), b.Select(r => (r.Number, r.Code, r.Verdict)));
    }
}
