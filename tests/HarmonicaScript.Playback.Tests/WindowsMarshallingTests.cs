using System.Runtime.InteropServices;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Playback;
using HarmonicaScript.Playback.Windows;

namespace HarmonicaScript.Playback.Tests;

/// <summary>
/// Everything about the Windows input path that can be checked WITHOUT Windows.
///
/// This is the point of the ISendInputApi seam. The developer has no Windows machine, so the
/// choice is between asserting the struct layout and the flag arithmetic here, or discovering
/// they were wrong from a user who says "nothing happens" - which is indistinguishable from four
/// other causes. The remaining unverifiable surface is then literally one question: does the game
/// accept injected input.
/// </summary>
public sealed class WindowsMarshallingTests
{
    private static readonly Dictionary<ushort, (ushort Scan, bool Extended)> ScanCodes = new()
    {
        [29] = (0x2C, false),   // Z
        [27] = (0x2D, false),   // X
        [6] = (0x2E, false),    // C
        [54] = (0x33, false),   // comma
        [82] = (0x48, true),    // Up arrow, an E0-prefixed key
    };

    private static (WindowsSendInputBackend Backend, RecordingSendInputApi Api) Build()
    {
        var api = new RecordingSendInputApi();
        return (new WindowsSendInputBackend(ScanCodes, api), api);
    }

    [Fact]
    public void InputStructHasTheLayoutSendInputExpects()
    {
        // SendInput validates cbSize and returns 0 WITHOUT a useful error if it is wrong, so a
        // layout mistake would present as "nothing happens" with no diagnostic whatsoever.
        var expected = Environment.Is64BitProcess ? 40 : 28;
        Assert.Equal(expected, Marshal.SizeOf<Input>());

        // The union starts at offset 8 on x64 because of nuint alignment.
        Assert.Equal(Environment.Is64BitProcess ? 32 : 24, Marshal.SizeOf<InputUnion>());
        Assert.Equal(Environment.Is64BitProcess ? 24 : 16, Marshal.SizeOf<KeyboardInput>());
        Assert.Equal(Environment.Is64BitProcess ? 32 : 24, Marshal.SizeOf<MouseInput>());
    }

    [Fact]
    public void KeyDownUsesScanCodeAndAZeroVirtualKey()
    {
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, 29, IsDown: true, IsMouse: false, NoteId: 0)]);

        var input = Assert.Single(api.Sent);
        Assert.Equal(Input.TypeKeyboard, input.Type);
        Assert.Equal(0x2C, input.Union.Keyboard.Scan);

        // wVk MUST be zero when KEYEVENTF_SCANCODE is set.
        Assert.Equal(0, input.Union.Keyboard.Vk);
        Assert.Equal(0x0008u, input.Union.Keyboard.Flags);
    }

    [Fact]
    public void KeyUpCarriesScanCodeOrKeyUpNotJustKeyUp()
    {
        // 0x000A, not 0x0002. Dropping KEYEVENTF_SCANCODE on the key-up half is the single most
        // common cause of stuck notes in this class of tool - and it is a one-line test.
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, 29, IsDown: false, IsMouse: false, NoteId: 0)]);

        Assert.Equal(0x000Au, Assert.Single(api.Sent).Union.Keyboard.Flags);
    }

    [Fact]
    public void ExtendedKeysCarryTheExtendedFlag()
    {
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, 82, IsDown: true, IsMouse: false, NoteId: 0)]);

        Assert.Equal(0x0008u | 0x0001u, Assert.Single(api.Sent).Union.Keyboard.Flags);
    }

    [Theory]
    [InlineData(MouseButton.Left, true, 0x0002u)]
    [InlineData(MouseButton.Left, false, 0x0004u)]
    [InlineData(MouseButton.Right, true, 0x0008u)]
    [InlineData(MouseButton.Right, false, 0x0010u)]
    [InlineData(MouseButton.Middle, true, 0x0020u)]
    [InlineData(MouseButton.Middle, false, 0x0040u)]
    public void MouseFlagsMatchTheWin32Constants(MouseButton button, bool down, uint expected)
    {
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, (ushort)button, down, IsMouse: true, NoteId: -1)]);

        var input = Assert.Single(api.Sent);
        Assert.Equal(Input.TypeMouse, input.Type);
        Assert.Equal(expected, input.Union.Mouse.Flags);
    }

    [Fact]
    public void MouseEventsNeverMoveTheCursor()
    {
        // MOUSEEVENTF_MOVE (0x0001) must never appear: it would drag the in-game camera.
        var (backend, api) = Build();
        foreach (var button in new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle })
        {
            backend.Emit([new CompiledEvent(0, 0, (ushort)button, true, true, -1)]);
            backend.Emit([new CompiledEvent(0, 0, (ushort)button, false, true, -1)]);
        }

        Assert.All(api.Sent, input =>
        {
            Assert.Equal(0, input.Union.Mouse.Dx);
            Assert.Equal(0, input.Union.Mouse.Dy);
            Assert.Equal(0u, input.Union.Mouse.MouseData);
            Assert.Equal(0u, input.Union.Mouse.Flags & 0x0001u);
        });
    }

    [Fact]
    public void OneCallPerBatchSoModifierArmAndStrikeAreAtomic()
    {
        var (backend, api) = Build();
        backend.Emit(
        [
            new CompiledEvent(0, 0, (ushort)MouseButton.Right, true, true, -1),
            new CompiledEvent(0, 0, 29, true, false, 0),
        ]);

        Assert.Equal([2], api.BatchSizes);
    }

    [Fact]
    public void ReleaseAllReleasesExactlyWhatIsStillHeld()
    {
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, 29, true, false, 0)]);
        backend.Emit([new CompiledEvent(0, 0, (ushort)MouseButton.Right, true, true, -1)]);
        backend.Emit([new CompiledEvent(0, 0, 29, false, false, 0)]);   // released again already

        api.Sent.Clear();
        api.BatchSizes.Clear();
        backend.ReleaseAll(ReleaseReason.UserStop);

        var input = Assert.Single(api.Sent);
        Assert.Equal(Input.TypeMouse, input.Type);
        Assert.Equal(0x0010u, input.Union.Mouse.Flags);
    }

    [Fact]
    public void ReleaseAllIsIdempotent()
    {
        var (backend, api) = Build();
        backend.Emit([new CompiledEvent(0, 0, 29, true, false, 0)]);
        backend.ReleaseAll(ReleaseReason.UserStop);

        api.Sent.Clear();
        backend.ReleaseAll(ReleaseReason.UserStop);
        Assert.Empty(api.Sent);
    }

    [Fact]
    public void RefusesAnUnknownKeyRatherThanSendingGarbage()
    {
        var (backend, _) = Build();
        Assert.Throws<KeyNotFoundException>(() =>
            backend.Emit([new CompiledEvent(0, 0, 999, true, false, 0)]));
    }

    [Fact]
    public void TheEightHarmonicaKeysAreTheConsecutiveSetOneBlock()
    {
        // 0x2C..0x33, none extended. Asserted here because it is the property the whole Windows
        // path depends on and it is cheap to protect against a keytable edit.
        var store = new Profiles.ProfileStore();
        var loaded = store.LoadValidated("df.harmonica.v1", "df.reference");

        var codes = loaded.Instrument.Degrees.Select(d => loaded.Keys.Key(d.Binding.Code)).ToList();

        Assert.Equal([0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33], codes.Select(k => k.ScanCode1));
        Assert.All(codes, k => Assert.False(k.Extended));
    }
}
