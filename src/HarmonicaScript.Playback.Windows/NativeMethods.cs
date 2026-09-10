using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HarmonicaScript.Playback.Windows;

[StructLayout(LayoutKind.Sequential)]
public struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KeyboardInput
{
    public ushort Vk;
    public ushort Scan;
    public uint Flags;
    public uint Time;
    public nuint ExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MouseInput Mouse;
    [FieldOffset(0)] public KeyboardInput Keyboard;
}

/// <summary>
/// Win32 INPUT. The union starts at offset 8 because of nuint alignment, giving 40 bytes on x64
/// and 28 on x86. <c>SendInput</c> validates <c>cbSize</c> and returns 0 without setting a useful
/// error if it is wrong, so this layout is asserted by a unit test that runs on every platform.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct Input
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public InputUnion Union;

    public const uint TypeMouse = 0;
    public const uint TypeKeyboard = 1;
}

/// <summary>
/// The Win32 flag values. Plain numbers, so they carry NO platform gate: the flag arithmetic in
/// <see cref="WindowsSendInputBackend"/> is exactly the part that most needs asserting from a
/// machine that is not Windows.
/// </summary>
internal static class Win32Flags
{
    // KEYEVENTF_SCANCODE must be set on BOTH halves: omitting it on key-up is the single most
    // common cause of stuck notes in this class of tool, and it is a one-line test.
    internal const uint KeyEventExtendedKey = 0x0001;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint KeyEventScanCode = 0x0008;

    // MOUSEEVENTF_MOVE is deliberately absent everywhere: the cursor and the in-game camera
    // must never move.
    internal const uint MouseEventLeftDown = 0x0002;
    internal const uint MouseEventLeftUp = 0x0004;
    internal const uint MouseEventRightDown = 0x0008;
    internal const uint MouseEventRightUp = 0x0010;
    internal const uint MouseEventMiddleDown = 0x0020;
    internal const uint MouseEventMiddleUp = 0x0040;

    internal const int QunsRunningD3dFullScreen = 3;
}

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetKeyboardLayout(uint threadId);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("shell32.dll")]
    internal static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static partial uint TimeBeginPeriod(uint ms);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    internal static partial uint TimeEndPeriod(uint ms);
}
