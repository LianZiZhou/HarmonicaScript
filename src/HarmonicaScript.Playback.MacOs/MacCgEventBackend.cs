using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Playback.MacOs;

/// <summary>
/// macOS CGEvent backend. DEVELOPMENT AND DEMO ONLY - the game is Windows-only, and this is
/// never advertised as a way to play it.
///
/// It exists for one reason that is worth its weight: with the loopback text profile it can play
/// a whole fixture into TextEdit and read the result back, which exercises the real OS input path
/// end to end on the only machine the developer actually has.
///
/// Requires Accessibility permission (System Settings -> Privacy &amp; Security -> Accessibility).
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacCgEventBackend : IInputBackend
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private readonly IReadOnlyDictionary<ushort, ushort> _virtualKeys;
    private readonly HashSet<ushort> _keysDown = [];
    private readonly HashSet<MouseButton> _mouseDown = [];

    public MacCgEventBackend(IReadOnlyDictionary<ushort, ushort> hidToMacVirtualKey)
    {
        ArgumentNullException.ThrowIfNull(hidToMacVirtualKey);
        _virtualKeys = hidToMacVirtualKey;
    }

    public string Id => "mac-cgevent";

    /// <summary>True when the process has been granted Accessibility permission.</summary>
    public static bool IsTrusted() => AXIsProcessTrusted();

    public void Emit(ReadOnlySpan<CompiledEvent> batch)
    {
        foreach (var e in batch)
        {
            if (e.IsMouse)
            {
                EmitMouse((MouseButton)e.Code, e.IsDown);
            }
            else
            {
                EmitKey(e.Code, e.IsDown);
            }
        }
    }

    public void ReleaseAll(ReleaseReason reason)
    {
        foreach (var hid in _keysDown.ToList())
        {
            EmitKey(hid, down: false);
        }

        foreach (var button in _mouseDown.ToList())
        {
            EmitMouse(button, down: false);
        }
    }

    public void Dispose() => ReleaseAll(ReleaseReason.ProcessExit);

    private void EmitKey(ushort hid, bool down)
    {
        if (!_virtualKeys.TryGetValue(hid, out var virtualKey))
        {
            throw new KeyNotFoundException($"HID usage {hid} has no macOS virtual key mapping.");
        }

        var handle = CGEventCreateKeyboardEvent(nint.Zero, virtualKey, down);
        if (handle == nint.Zero)
        {
            return;
        }

        CGEventPost(0, handle);
        CFRelease(handle);

        if (down)
        {
            _keysDown.Add(hid);
        }
        else
        {
            _keysDown.Remove(hid);
        }
    }

    private void EmitMouse(MouseButton button, bool down)
    {
        var (type, macButton) = (button, down) switch
        {
            (MouseButton.Left, true) => (1u, 0u),
            (MouseButton.Left, false) => (2u, 0u),
            (MouseButton.Right, true) => (3u, 1u),
            (MouseButton.Right, false) => (4u, 1u),
            (MouseButton.Middle, true) => (25u, 2u),
            (MouseButton.Middle, false) => (26u, 2u),
            _ => throw new NotSupportedException($"{button} is not supported."),
        };

        var handle = CGEventCreateMouseEvent(nint.Zero, type, default, macButton);
        if (handle == nint.Zero)
        {
            return;
        }

        CGEventPost(0, handle);
        CFRelease(handle);

        if (down)
        {
            _mouseDown.Add(button);
        }
        else
        {
            _mouseDown.Remove(button);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServices)]
    private static partial nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [LibraryImport(ApplicationServices)]
    private static partial nint CGEventCreateMouseEvent(nint source, uint mouseType, CGPoint position, uint mouseButton);

    [LibraryImport(ApplicationServices)]
    private static partial void CGEventPost(uint tap, nint eventRef);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint reference);
}
