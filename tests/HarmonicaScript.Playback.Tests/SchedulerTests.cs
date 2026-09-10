using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Midi.Fixtures;
using HarmonicaScript.Playback;
using HarmonicaScript.Project;

namespace HarmonicaScript.Playback.Tests;

/// <summary>
/// The entire playback engine, tested deterministically on a machine with no Windows and no game,
/// via <see cref="VirtualClock"/> plus <see cref="TraceBackend"/>.
/// </summary>
public sealed class SchedulerTests
{
    private static readonly Lazy<ConversionService.Outcome> LazyOutcome = new(() =>
    {
        var root = FixtureCorpus.FindTestDataRoot() ?? throw new DirectoryNotFoundException("testdata/");
        FixtureCorpus.WriteAll(root);
        return new ConversionService().Convert(Path.Combine(root, "generated", "exclusive-swap-storm.mid"));
    });

    private static InputTimeline Timeline => LazyOutcome.Value.Conversion.Timeline;

    private static (Scheduler Scheduler, VirtualClock Clock, TraceBackend Backend, CompiledEvent[] Events) Build(
        InputTimeline? timeline = null,
        IInputBackend? backend = null)
    {
        var clock = new VirtualClock();
        var trace = backend as TraceBackend ?? new TraceBackend();
        var target = backend ?? trace;
        var events = Scheduler.Compile(clock, timeline ?? Timeline, clock.GetTimestamp(), 1.0);
        return (new Scheduler(clock, target), clock, trace, events);
    }

    [Fact]
    public void PlaysEveryEventAndEndsHoldingNothing()
    {
        var (scheduler, _, backend, events) = Build();
        var outcome = scheduler.Play(events, new PlaybackOptions());

        Assert.Equal(ReleaseReason.EndOfSong, outcome.Reason);
        Assert.Equal(events.Length, outcome.EventsEmitted);
        Assert.Empty(backend.Held);
    }

    [Fact]
    public void BatchesEventsThatShareAnInstantIntoOneCall()
    {
        // MSDN guarantees a single SendInput call's events are not interspersed with other input.
        // That guarantee is the ONLY thing making "arm the modifier, then strike" atomic, so the
        // batching must be real rather than incidental.
        var (scheduler, _, backend, events) = Build();
        scheduler.Play(events, new PlaybackOptions());

        var distinctDeadlines = events.Select(e => e.DeadlineTicks).Distinct().Count();
        Assert.Equal(distinctDeadlines, backend.BatchCount);
        Assert.True(backend.BatchCount < events.Length, "some events must genuinely share an instant");
    }

    [Fact]
    public void ReleasesEverythingWhenTheUserStopsMidSong()
    {
        var (scheduler, _, backend, events) = Build();

        var thread = new Thread(() => scheduler.Play(events, new PlaybackOptions()));
        scheduler.RequestStop();
        thread.Start();
        thread.Join();

        Assert.Empty(backend.Held);
        Assert.Contains(backend.Lines, l => l.Kind == "RELEASEALL");
    }

    [Fact]
    public void StopsAndReleasesTheInstantTheGameLosesFocus()
    {
        // Prevents the catastrophic failure: four minutes of zxcvbnm typed into Discord.
        var (scheduler, _, backend, events) = Build();
        var batches = 0;

        var outcome = scheduler.Play(events, new PlaybackOptions(), foregroundCheck: () => ++batches < 5);

        Assert.Equal(ReleaseReason.FocusLost, outcome.Reason);
        Assert.Empty(backend.Held);
        Assert.True(outcome.EventsEmitted < events.Length);
    }

    [Fact]
    public void EmitsAReleaseAllBeforeTheFirstNote()
    {
        // Recovery from a PREVIOUS hard stop. MSDN is explicit that injected input does not reset
        // keyboard state, so this - not a hold cap - is what un-sticks a key after a crash.
        var (scheduler, _, backend, events) = Build();
        scheduler.Play(events, new PlaybackOptions());

        var first = backend.Lines[0];
        Assert.Equal("RELEASEALL", first.Kind);
        Assert.Contains("SessionStart", first.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FaultMode.ThrowInEmit)]
    [InlineData(FaultMode.StopSilently)]
    [InlineData(FaultMode.KillWithoutRelease)]
    [InlineData(FaultMode.DisposeMidSong)]
    [InlineData(FaultMode.ThrowInReleaseAll)]
    [InlineData(FaultMode.HangThenThrow)]
    public void NothingIsEverLeftHeldNoMatterHowPlaybackDies(FaultMode mode)
    {
        // A stuck held MOUSE button in an online shooter is continuous fire. "Nothing is left
        // held" cannot be an intention; it has to be a property proven against deliberate failure
        // at every point in the stream.
        var timeline = Timeline;

        for (var failAt = 1; failAt < Math.Min(timeline.Events.Count, 200); failAt += 17)
        {
            var inner = new TraceBackend();
            var faulty = new FaultInjectingBackend(inner, failAt, mode);
            var clock = new VirtualClock();
            var events = Scheduler.Compile(clock, timeline, clock.GetTimestamp(), 1.0);

            new Scheduler(clock, faulty).Play(events, new PlaybackOptions());

            Assert.Empty(inner.Held);
            Assert.Equal("RELEASEALL", inner.Lines[^1].Kind);
        }
    }

    [Fact]
    public void SpeedScalesDeadlinesWithoutReorderingAnything()
    {
        var clock = new VirtualClock();
        var slow = Scheduler.Compile(clock, Timeline, 0, 0.5);
        var fast = Scheduler.Compile(clock, Timeline, 0, 2.0);

        Assert.Equal(slow.Length, fast.Length);
        Assert.True(slow[^1].DeadlineTicks > fast[^1].DeadlineTicks);

        for (var i = 1; i < slow.Length; i++)
        {
            Assert.True(slow[i].DeadlineTicks >= slow[i - 1].DeadlineTicks);
            Assert.True(fast[i].DeadlineTicks >= fast[i - 1].DeadlineTicks);
        }
    }

    [Fact]
    public void RejectsANonPositiveSpeed()
    {
        var clock = new VirtualClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.Compile(clock, Timeline, 0, 0));
    }

    [Fact]
    public void TraceIsStableAcrossRunsSoAUsersLogDiffsAgainstCi()
    {
        var first = Run();
        var second = Run();
        Assert.Equal(first, second);

        static string Run()
        {
            var (scheduler, _, backend, events) = Build();
            scheduler.Play(events, new PlaybackOptions());
            return TraceWriter.Write(backend, "abc123", "df.harmonica.v1@1", "df.reference@1", 1.0, "trace");
        }
    }

    [Fact]
    public void TraceRecordsScheduledTimesSoGoldenFilesAndRealRunsAreComparable()
    {
        var (scheduler, _, backend, events) = Build();
        scheduler.Play(events, new PlaybackOptions());

        var text = TraceWriter.Write(backend, "abc123", "df.harmonica.v1@1", "df.reference@1", 1.0, "trace", maxLateMs: 0.7);

        Assert.StartsWith("# harmonicascript trace v1", text, StringComparison.Ordinal);
        Assert.Contains("stuckAtEnd=0", text, StringComparison.Ordinal);
        Assert.Contains("maxLateMs=0.7", text, StringComparison.Ordinal);
        Assert.Contains("RELEASEALL", text, StringComparison.Ordinal);
    }
}
