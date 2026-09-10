using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Profiles.Json;

namespace HarmonicaScript.Profiles;

/// <summary>One key, in every encoding any backend or exporter needs.</summary>
/// <param name="Hid">USB HID usage, page 0x07. The canonical identity (decision D4).</param>
/// <param name="ScanCode1">PS/2 Set-1 make code. Mandatory: Windows SendInput must use KEYEVENTF_SCANCODE, and Razer Synapse 3 stores Set-1.</param>
/// <param name="Extended">True for E0-prefixed keys.</param>
/// <param name="WindowsVk">Virtual-key code. Used for hotkeys, never for injection into the game.</param>
/// <param name="Ahk">AutoHotkey v2 token, e.g. <c>{sc02C}</c>.</param>
public readonly record struct KeyInfo(ushort Hid, string Name, int ScanCode1, bool Extended, int WindowsVk, string Ahk);

/// <summary>
/// Mouse button encodings. <paramref name="Razer3"/> is nullable ON PURPOSE: the one verified
/// real Synapse 3 macro contains keyboard events and the left button only, so right/middle are
/// unknown. An exporter refuses with a named missing capability rather than guessing - guessing
/// yields a file that imports cleanly and then presses the wrong button.
/// </summary>
public readonly record struct MouseInfo(MouseButton Button, int? Razer3, string Ahk, ushort WinDownFlag, ushort WinUpFlag);

/// <summary>Lookup from the canonical HID identity into every other encoding.</summary>
public sealed class KeyTable
{
    private readonly Dictionary<ushort, KeyInfo> _byHid;
    private readonly Dictionary<MouseButton, MouseInfo> _mouse;

    private KeyTable(Dictionary<ushort, KeyInfo> byHid, Dictionary<MouseButton, MouseInfo> mouse)
    {
        _byHid = byHid;
        _mouse = mouse;
    }

    public int KeyCount => _byHid.Count;

    public static KeyTable FromDto(KeyTableDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var keys = dto.Keys.ToDictionary(
            k => k.Hid,
            k => new KeyInfo(k.Hid, k.Name, k.ScanCode1, k.Extended, k.WindowsVk, k.Ahk));

        var mouse = new Dictionary<MouseButton, MouseInfo>();
        foreach (var m in dto.Mouse)
        {
            if (!Enum.TryParse<MouseButton>(m.Button, ignoreCase: true, out var button))
            {
                continue;
            }

            mouse[button] = new MouseInfo(
                button,
                m.Razer3,
                m.Ahk,
                ParseHex(m.WinDown),
                ParseHex(m.WinUp));
        }

        return new KeyTable(keys, mouse);
    }

    public bool TryGetKey(ushort hid, out KeyInfo info) => _byHid.TryGetValue(hid, out info);

    public KeyInfo Key(ushort hid) => _byHid.TryGetValue(hid, out var info)
        ? info
        : throw new KeyNotFoundException($"HID usage {hid} is not in keytable.json.");

    public bool TryGetMouse(MouseButton button, out MouseInfo info) => _mouse.TryGetValue(button, out info);

    public MouseInfo Mouse(MouseButton button) => _mouse.TryGetValue(button, out var info)
        ? info
        : throw new KeyNotFoundException($"Mouse button {button} is not in keytable.json.");

    /// <summary>Resolves a profile binding to a human-readable label for traces and reports.</summary>
    public string Describe(Binding binding) => binding.IsMouse
        ? binding.AsMouseButton.ToString()
        : TryGetKey(binding.Code, out var k) ? k.Name : $"hid{binding.Code}";

    /// <summary>Every binding in a profile must resolve, or a backend will fail at the worst moment.</summary>
    public IReadOnlyList<ProfileProblem> ValidateBindings(InstrumentProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var problems = new List<ProfileProblem>();

        foreach (var degree in profile.Degrees)
        {
            if (!Resolves(degree.Binding))
            {
                problems.Add(new ProfileProblem($"degrees[{degree.Index}].bind", $"{degree.Binding} is not in keytable.json."));
            }
        }

        foreach (var modifier in profile.Modifiers)
        {
            if (!Resolves(modifier.Binding))
            {
                problems.Add(new ProfileProblem($"modifiers.{modifier.Id}.bind", $"{modifier.Binding} is not in keytable.json."));
            }
        }

        return problems;
    }

    private bool Resolves(Binding binding) => binding.IsMouse
        ? _mouse.ContainsKey(binding.AsMouseButton)
        : _byHid.ContainsKey(binding.Code);

    private static ushort ParseHex(string? value) =>
        string.IsNullOrEmpty(value)
            ? (ushort)0
            : ushort.Parse(
                value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture);
}
