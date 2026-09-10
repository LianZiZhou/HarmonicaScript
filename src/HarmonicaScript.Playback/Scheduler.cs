using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Playback;

public sealed record PlaybackOptions(double Speed = 1.0, int CountdownSeconds = 3)
{
    /// <summary>Re-checked before EVERY batch. Prevents the catastrophic "typed zxcvbnm into Discord for four minutes".</summary>
    public bool RequireForeground { get; init; } = true;

    public bool ReleaseAllAtStart { get; init; } = true;
}

public sealed record PlaybackOutcome(
    ReleaseReason Reason,
    int EventsEmitted,
    double MaxLateMs,
    Exception? Error);

/// <summary>
/// Compiles a timeline to absolute clock deadlines ONCE, then plays it.
///
/// Absolute deadlines, never per-note sleeps: sleeping a delta accumulates every rounding error
/// and every scheduling hiccup, and over five minutes that is the difference between music and
/// a shambles.
/// </summary>
public sealed class Scheduler(IClock clock, IInputBackend backend)
{
    private volatile bool _stopRequested;

    /// <summary>Set by the panic hotkey or the UI. Checked between batches.</summary>
    public void RequestStop() => _stopRequested = true;

    public static CompiledEvent[] Compile(IClock clock, InputTimeline timeline, long originTicks, double speed)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timeline);

        if (speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be positive.");
        }

        var compiled = new CompiledEvent[timeline.Events.Count];
        for (var i = 0; i < timeline.Events.Count; i++)
        {
            var e = timeline.Events[i];

            // Sub-millisecond precision lives in the wait, never in the IR.
            var offsetTicks = (long)(e.TimeMs / speed * clock.Frequency / 1000.0);
            compiled[i] = new CompiledEvent(
                originTicks + offsetTicks, e.TimeMs, e.Code, e.IsDown, e.Device == InputDeviceKind.Mouse, e.NoteId);
        }

        return compiled;
    }

    /// <summary>
    /// Plays a compiled schedule.
    ///
    /// Every exit path - normal end, user stop, lost focus, exception - goes through
    /// <see cref="IInputBackend.ReleaseAll"/> in a finally. There is no path out of this method
    /// that can leave a control held.
    /// </summary>
    public PlaybackOutcome Play(
        ReadOnlySpan<CompiledEvent> events,
        PlaybackOptions options,
        Func<bool>? foregroundCheck = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _stopRequested = false;
        var reason = ReleaseReason.EndOfSong;
        var emitted = 0;
        double maxLateMs = 0;
        Exception? error = null;

        try
        {
            // Recovers from a PREVIOUS hard stop. MSDN is explicit that injected input does not
            // reset keyboard state, so this - not a hold cap - is the real recovery mechanism.
            if (options.ReleaseAllAtStart)
            {
                backend.ReleaseAll(ReleaseReason.SessionStart);
            }

            var i = 0;
            while (i < events.Length)
            {
                if (_stopRequested)
                {
                    reason = ReleaseReason.UserStop;
                    break;
                }

                var deadline = events[i].DeadlineTicks;
                var j = i;
                while (j < events.Length && events[j].DeadlineTicks == deadline)
                {
                    j++;
                }

                clock.WaitUntil(deadline);

                if (_stopRequested)
                {
                    reason = ReleaseReason.UserStop;
                    break;
                }

                // A few hundred nanoseconds, re-checked every batch rather than once at the start.
                if (options.RequireForeground && foregroundCheck is not null && !foregroundCheck())
                {
                    reason = ReleaseReason.FocusLost;
                    break;
                }

                var lateTicks = clock.GetTimestamp() - deadline;
                if (lateTicks > 0)
                {
                    maxLateMs = Math.Max(maxLateMs, lateTicks * 1000.0 / clock.Frequency);
                }

                backend.Emit(events[i..j]);
                emitted += j - i;
                i = j;
            }
        }
        catch (Exception ex)
        {
            reason = ReleaseReason.Exception;
            error = ex;
        }
        finally
        {
            // Belt and braces: if ReleaseAll itself throws, try once more, then let it go - we
            // are already on the failure path and must not mask the original error.
            try
            {
                backend.ReleaseAll(reason);
            }
            catch (Exception)
            {
                try
                {
                    backend.ReleaseAll(ReleaseReason.Exception);
                }
                catch (Exception)
                {
                    // Nothing further can be done here; the watchdog is the next line of defence.
                }
            }
        }

        return new PlaybackOutcome(reason, emitted, maxLateMs, error);
    }
}
