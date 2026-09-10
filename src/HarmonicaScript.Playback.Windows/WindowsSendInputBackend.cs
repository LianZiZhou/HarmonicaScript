using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Playback.Windows;

/// <summary>Abstracted so the marshalling can be asserted byte for byte without a real Windows box.</summary>
public interface ISendInputApi
{
    uint Send(Input[] inputs, int count);
}

[SupportedOSPlatform("windows")]
public sealed class RealSendInputApi : ISendInputApi
{
    public uint Send(Input[] inputs, int count) => NativeMethods.SendInput((uint)count, inputs, Marshal.SizeOf<Input>());
}

/// <summary>
/// The production Windows backend.
///
/// Deliberately NOT marked [SupportedOSPlatform("windows")]: everything in here is struct
/// arithmetic behind the <see cref="ISendInputApi"/> seam, and only <see cref="RealSendInputApi"/>
/// actually touches user32. Keeping the gate on the seam rather than the class is what lets the
/// flag arithmetic, the union layout and the release-all bookkeeping all be asserted from macOS.
///
/// Scan codes, never virtual keys: games that read DirectInput or raw input routinely ignore
/// VK-only injection, which is why the whole pipeline carries PS/2 Set-1 codes alongside the
/// canonical HID identity. Every event sharing a deadline goes in ONE SendInput call, because
/// MSDN guarantees a single call's events are not interspersed with other input - and that
/// guarantee is exactly what makes "arm the modifier, then strike" atomic.
/// </summary>
public sealed class WindowsSendInputBackend : IInputBackend
{
    private readonly ISendInputApi _api;
    private readonly IReadOnlyDictionary<ushort, (ushort Scan, bool Extended)> _scanCodes;
    private readonly HashSet<ushort> _keysDown = [];
    private readonly HashSet<MouseButton> _mouseDown = [];

    public WindowsSendInputBackend(
        IReadOnlyDictionary<ushort, (ushort Scan, bool Extended)> scanCodes,
        ISendInputApi? api = null)
    {
        ArgumentNullException.ThrowIfNull(scanCodes);
        _scanCodes = scanCodes;

        _api = api ?? (OperatingSystem.IsWindows()
            ? new RealSendInputApi()
            : throw new PlatformNotSupportedException(
                "The real SendInput path needs Windows. Inject an ISendInputApi to exercise the marshalling elsewhere."));
    }

    public string Id => "win-sendinput";

    public void Emit(ReadOnlySpan<CompiledEvent> batch)
    {
        if (batch.Length == 0)
        {
            return;
        }

        var inputs = new Input[batch.Length];
        for (var i = 0; i < batch.Length; i++)
        {
            inputs[i] = Build(batch[i]);
            Track(batch[i]);
        }

        _api.Send(inputs, batch.Length);
    }

    public void ReleaseAll(ReleaseReason reason)
    {
        var inputs = new List<Input>(_keysDown.Count + _mouseDown.Count);

        foreach (var hid in _keysDown)
        {
            inputs.Add(BuildKey(hid, down: false));
        }

        foreach (var button in _mouseDown)
        {
            inputs.Add(BuildMouse(button, down: false));
        }

        _keysDown.Clear();
        _mouseDown.Clear();

        if (inputs.Count > 0)
        {
            _api.Send([.. inputs], inputs.Count);
        }
    }

    public void Dispose() => ReleaseAll(ReleaseReason.ProcessExit);

    public Input Build(CompiledEvent e) =>
        e.IsMouse ? BuildMouse((MouseButton)e.Code, e.IsDown) : BuildKey(e.Code, e.IsDown);

    private Input BuildKey(ushort hid, bool down)
    {
        if (!_scanCodes.TryGetValue(hid, out var mapping))
        {
            throw new KeyNotFoundException($"HID usage {hid} has no scan code in the key table.");
        }

        // KEYEVENTF_SCANCODE on BOTH halves. Key-up therefore carries 0x000A, not 0x0002.
        var flags = Win32Flags.KeyEventScanCode;
        if (!down)
        {
            flags |= Win32Flags.KeyEventKeyUp;
        }

        if (mapping.Extended)
        {
            flags |= Win32Flags.KeyEventExtendedKey;
        }

        return new Input
        {
            Type = Input.TypeKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput { Vk = 0, Scan = mapping.Scan, Flags = flags, Time = 0, ExtraInfo = 0 },
            },
        };
    }

    private static Input BuildMouse(MouseButton button, bool down)
    {
        var flags = (button, down) switch
        {
            (MouseButton.Left, true) => Win32Flags.MouseEventLeftDown,
            (MouseButton.Left, false) => Win32Flags.MouseEventLeftUp,
            (MouseButton.Right, true) => Win32Flags.MouseEventRightDown,
            (MouseButton.Right, false) => Win32Flags.MouseEventRightUp,
            (MouseButton.Middle, true) => Win32Flags.MouseEventMiddleDown,
            (MouseButton.Middle, false) => Win32Flags.MouseEventMiddleUp,
            _ => throw new NotSupportedException($"{button} is not a supported instrument control."),
        };

        // dx, dy and mouseData all stay zero, and MOUSEEVENTF_MOVE is never set: the cursor and
        // the in-game camera must not move.
        return new Input
        {
            Type = Input.TypeMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput { Dx = 0, Dy = 0, MouseData = 0, Flags = flags, Time = 0, ExtraInfo = 0 },
            },
        };
    }

    private void Track(CompiledEvent e)
    {
        if (e.IsMouse)
        {
            if (e.IsDown)
            {
                _mouseDown.Add((MouseButton)e.Code);
            }
            else
            {
                _mouseDown.Remove((MouseButton)e.Code);
            }
        }
        else if (e.IsDown)
        {
            _keysDown.Add(e.Code);
        }
        else
        {
            _keysDown.Remove(e.Code);
        }
    }
}

/// <summary>Records the marshalled structs instead of sending them, so the byte layout is testable anywhere.</summary>
public sealed class RecordingSendInputApi : ISendInputApi
{
    public List<Input> Sent { get; } = [];

    public List<int> BatchSizes { get; } = [];

    public uint Send(Input[] inputs, int count)
    {
        BatchSizes.Add(count);
        Sent.AddRange(inputs.Take(count));
        return (uint)count;
    }
}
