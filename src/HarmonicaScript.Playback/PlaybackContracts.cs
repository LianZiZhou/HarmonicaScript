using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Playback;

/// <summary>A control event compiled to an absolute clock deadline.</summary>
public readonly record struct CompiledEvent(long DeadlineTicks, int TimeMs, ushort Code, bool IsDown, bool IsMouse, int NoteId);

public enum ReleaseReason
{
    EndOfSong,
    UserStop,
    FocusLost,
    Exception,
    ProcessExit,
    Signal,
    SessionStart,
}

/// <summary>
/// A monotonic clock. Injectable so the ENTIRE playback engine - countdown, focus guard, panic
/// hotkey, pause, seek, exception and signal paths - runs deterministically in a unit test on a
/// machine that has no game and no Windows.
/// </summary>
public interface IClock
{
    long Frequency { get; }

    long GetTimestamp();

    /// <summary>Blocks until <paramref name="targetTicks"/>. A virtual clock simply jumps.</summary>
    void WaitUntil(long targetTicks);
}

/// <summary>Where control events actually go.</summary>
public interface IInputBackend : IDisposable
{
    string Id { get; }

    /// <summary>Every event in the span shares one deadline and must be delivered atomically.</summary>
    void Emit(ReadOnlySpan<CompiledEvent> batch);

    void ReleaseAll(ReleaseReason reason);
}

/// <summary>
/// RAW FACTS ONLY - no policy, no verdicts. Keeping the probe and the rules apart is what makes
/// the one piece of user-facing decision logic in this project unit-testable on a Mac.
/// </summary>
public sealed record EnvironmentFacts
{
    public bool GameProcessFound { get; init; }

    public bool GameWindowFocused { get; init; }

    public bool ExclusiveFullscreen { get; init; }

    /// <summary>Keyboard layout id of the foreground window's thread. 0x0804 is Chinese (Simplified).</summary>
    public int ForegroundKeyboardLayout { get; init; }

    public bool WeAreElevated { get; init; }

    /// <summary>Set only after the Notepad delivery test has actually run.</summary>
    public bool? NotepadDeliverySucceeded { get; init; }

    public string? NotepadReadback { get; init; }

    public bool BackendAvailable { get; init; }

    public string PlatformDescription { get; init; } = string.Empty;
}

public interface IEnvironmentProbe
{
    EnvironmentFacts Read();
}

/// <summary>Everything the scheduler needs about the instrument to release what it may have pressed.</summary>
public sealed record PlaybackProfile(InstrumentProfile Instrument, IReadOnlyDictionary<ushort, ushort> KeyboardScanCodes);
