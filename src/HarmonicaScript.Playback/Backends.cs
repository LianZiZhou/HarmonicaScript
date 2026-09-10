using System.Text;

namespace HarmonicaScript.Playback;

/// <summary>One line of the canonical trace.</summary>
public readonly record struct TraceLine(double AtMs, string Kind, string Detail);

/// <summary>
/// Records everything instead of sending it. THE test backend: the test IS the transcript.
///
/// Its output goes through the same <see cref="TraceWriter"/> as a production run, so a user's
/// emailed log is directly diffable against a CI golden file - which is the entire remote
/// debugging story for a developer who cannot run the game.
/// </summary>
public sealed class TraceBackend : IInputBackend
{
    private readonly List<TraceLine> _lines = [];

    public string Id => "trace";

    public IReadOnlyList<TraceLine> Lines => _lines;

    /// <summary>Controls currently held, so a test can assert nothing outlives the song.</summary>
    public HashSet<(ushort Code, bool IsMouse)> Held { get; } = [];

    public int MaxConcurrentHeld { get; private set; }

    public int BatchCount { get; private set; }

    public void Emit(ReadOnlySpan<CompiledEvent> batch)
    {
        if (batch.Length == 0)
        {
            return;
        }

        BatchCount++;
        _lines.Add(new TraceLine(batch[0].TimeMs, "BATCH", $"n={batch.Length}"));

        foreach (var e in batch)
        {
            var key = (e.Code, e.IsMouse);
            if (e.IsDown)
            {
                Held.Add(key);
            }
            else
            {
                Held.Remove(key);
            }

            MaxConcurrentHeld = Math.Max(MaxConcurrentHeld, Held.Count);
            _lines.Add(new TraceLine(
                e.TimeMs,
                e.IsDown ? "DOWN" : "UP",
                $"{(e.IsMouse ? "mouse" : "key")} 0x{e.Code:X2} note={e.NoteId}"));
        }
    }

    public void ReleaseAll(ReleaseReason reason)
    {
        var released = Held.Count;
        Held.Clear();
        _lines.Add(new TraceLine(_lines.Count == 0 ? 0 : _lines[^1].AtMs, "RELEASEALL", $"reason={reason} released={released}"));
    }

    public void Dispose()
    {
    }
}

public enum FaultMode
{
    ThrowInEmit,
    StopSilently,
    KillWithoutRelease,
    DisposeMidSong,
    ThrowInReleaseAll,
    HangThenThrow,
}

/// <summary>
/// Wraps another backend and fails at a chosen event, in a chosen way.
///
/// A stuck held MOUSE button in an online shooter is continuous fire and is the worst thing this
/// project can produce, so "nothing is ever left held" cannot be a design intention - it has to
/// be a property proven against deliberate failure at every point in the stream.
/// </summary>
public sealed class FaultInjectingBackend(IInputBackend inner, int failAtEvent, FaultMode mode) : IInputBackend
{
    private int _seen;

    public string Id => $"fault({mode})";

    public bool Fired { get; private set; }

    public void Emit(ReadOnlySpan<CompiledEvent> batch)
    {
        _seen += batch.Length;
        if (_seen < failAtEvent || Fired)
        {
            inner.Emit(batch);
            return;
        }

        Fired = true;
        switch (mode)
        {
            case FaultMode.ThrowInEmit:
                throw new InvalidOperationException("injected fault: Emit");
            case FaultMode.HangThenThrow:
                throw new TimeoutException("injected fault: hang");
            case FaultMode.StopSilently:
                return;
            case FaultMode.KillWithoutRelease:
                throw new OperationCanceledException("injected fault: hard kill");
            case FaultMode.DisposeMidSong:
                inner.Dispose();
                throw new ObjectDisposedException(nameof(FaultInjectingBackend));
            case FaultMode.ThrowInReleaseAll:
                inner.Emit(batch);
                return;
            default:
                inner.Emit(batch);
                return;
        }
    }

    public void ReleaseAll(ReleaseReason reason)
    {
        if (mode == FaultMode.ThrowInReleaseAll && Fired)
        {
            // Even this must not leave the instrument held: the guard retries on the inner backend.
            inner.ReleaseAll(reason);
            throw new InvalidOperationException("injected fault: ReleaseAll");
        }

        inner.ReleaseAll(reason);
    }

    public void Dispose() => inner.Dispose();
}

/// <summary>
/// The ONE canonical trace writer. Used for CI golden files AND for the production run log, so
/// the two are diffable by construction rather than by convention.
///
/// The event column carries the SCHEDULED time; realised lateness lives only in the summary, so a
/// golden file and a real run differ only where something actually went wrong.
/// </summary>
public static class TraceWriter
{
    public static string Write(
        TraceBackend backend,
        string timelineSha,
        string instrumentRef,
        string timingRef,
        double speed,
        string backendId,
        double maxLateMs = 0)
    {
        ArgumentNullException.ThrowIfNull(backend);

        var builder = new StringBuilder();
        builder.Append("# harmonicascript trace v1\n");
        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"# timeline=sha256:{timelineSha} instrument={instrumentRef} timing={timingRef} speed={speed:0.000} backend={backendId}\n");

        foreach (var line in backend.Lines)
        {
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"{line.AtMs,10:0.000}  {line.Kind,-11}{line.Detail}\n");
        }

        builder.Append(System.Globalization.CultureInfo.InvariantCulture,
            $"# summary events={backend.Lines.Count(l => l.Kind is "DOWN" or "UP")} batches={backend.BatchCount} "
            + $"maxConcurrentHeld={backend.MaxConcurrentHeld} stuckAtEnd={backend.Held.Count} maxLateMs={maxLateMs:0.0}\n");

        return builder.ToString();
    }
}
