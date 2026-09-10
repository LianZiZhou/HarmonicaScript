using System.Diagnostics;

namespace HarmonicaScript.Playback;

/// <summary>Real time, with a hybrid sleep/spin wait. Never <c>Thread.Sleep(delta)</c> per note.</summary>
public sealed class StopwatchClock : IClock
{
    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public void WaitUntil(long targetTicks)
    {
        var spinThreshold = Frequency / 500;   // ~2 ms

        while (true)
        {
            var remaining = targetTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            if (remaining > spinThreshold)
            {
                // Sleep most of the way, leaving the tail to the spin: a coarse OS timer cannot
                // hit a musical deadline, and spinning the whole interval burns a core.
                var ms = (int)((remaining - spinThreshold) * 1000 / Frequency);
                if (ms > 0)
                {
                    Thread.Sleep(ms);
                    continue;
                }
            }

            Thread.SpinWait(64);
        }
    }
}

/// <summary>
/// Virtual time. <see cref="WaitUntil"/> simply moves the clock, so a three-minute song runs in
/// microseconds and every timing-dependent path becomes a deterministic assertion.
/// </summary>
public sealed class VirtualClock(long frequency = 10_000_000) : IClock
{
    private long _now;

    public long Frequency { get; } = frequency;

    public long GetTimestamp() => _now;

    public void WaitUntil(long targetTicks) => _now = Math.Max(_now, targetTicks);

    /// <summary>Advances without a wait, for simulating an external delay.</summary>
    public void Advance(long ticks) => _now += ticks;

    public long MsToTicks(int ms) => (long)ms * Frequency / 1000;
}
