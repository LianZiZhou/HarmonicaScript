namespace HarmonicaScript.Core.Instrument;

/// <summary>
/// A physical control. Decision D4: the canonical key identity is the USB HID Usage ID
/// (page 0x07), because Bloody <c>.amc</c> and Redragon <c>.MSMACRO</c> store HID usages
/// literally, making translation a cast for those targets. PS/2 Set-1 scan codes (needed by
/// Windows SendInput and Razer) live in <c>keytable.json</c> as a mandatory second column,
/// never here.
/// </summary>
/// <param name="Device">Keyboard or mouse.</param>
/// <param name="Code">HID usage (page 0x07) when <see cref="InputDeviceKind.Keyboard"/>; a <see cref="MouseButton"/> value when <see cref="InputDeviceKind.Mouse"/>.</param>
public readonly record struct Binding(InputDeviceKind Device, ushort Code)
{
    public static Binding Key(ushort hidUsage) => new(InputDeviceKind.Keyboard, hidUsage);

    public static Binding Mouse(MouseButton button) => new(InputDeviceKind.Mouse, (ushort)button);

    public bool IsMouse => Device == InputDeviceKind.Mouse;

    public MouseButton AsMouseButton => IsMouse ? (MouseButton)Code : MouseButton.None;

    public override string ToString() =>
        IsMouse ? $"mouse:{AsMouseButton}" : $"key:hid{Code}";
}
