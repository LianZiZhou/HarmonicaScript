namespace HarmonicaScript.Core.Instrument;

/// <summary>How a modifier control behaves while the player interacts with it.</summary>
public enum ModifierBehavior : byte
{
    /// <summary>Momentary: active only while physically held. The Delta Force harmonica's three mouse modifiers.</summary>
    Hold = 0,

    /// <summary>Latching: a click flips the state and it stays flipped.</summary>
    Toggle = 1,
}

/// <summary>
/// How much evidence stands behind a number. The developer cannot run the game, so this is
/// tracked per field and surfaced in the UI rather than quietly averaged into confidence.
/// </summary>
public enum Provenance : byte
{
    /// <summary>Someone measured it in the running game and reported the method.</summary>
    Measured = 0,

    /// <summary>Read off a third-party tool that demonstrably works in game. Evidence of its author's belief, not of the game.</summary>
    ThirdPartyTool = 1,

    /// <summary>Deduced from something observable, but never checked directly.</summary>
    Inferred = 2,

    /// <summary>Made up because a number was needed. Says so.</summary>
    Invented = 3,
}

/// <summary>Which physical device a binding drives.</summary>
public enum InputDeviceKind : byte
{
    Keyboard = 0,
    Mouse = 1,
}

/// <summary>Mouse buttons, numbered as they appear in <see cref="Binding.Code"/>.</summary>
public enum MouseButton : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3,
    X1 = 4,
    X2 = 5,
}
